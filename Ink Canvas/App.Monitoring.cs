using H.NotifyIcon;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using iNKORE.UI.WPF.Modern.Controls;
using Microsoft.Win32;
using Newtonsoft.Json;
using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Application = System.Windows.Application;
using MessageBox = System.Windows.MessageBox;
using Timer = System.Threading.Timer;
using Ink_Canvas.Windows;

namespace Ink_Canvas
{
    public partial class App : Application
    {
    // LTSC（2026-11-17）：从 App.xaml.cs 拆出。
    //
    // 这一整段是进程监控与崩溃处理：崩溃监听、终止监控、Win32 钩子、
    // 会话结束、未处理异常、CPU 采样、崩溃日志、看门狗。
    // 原文件和启动流程混在一起，但两者是两条线——启动跑一次，监控一直跑。

        // 初始化崩溃监听器
        private void InitializeCrashListeners()
        {
            if (crashListenersInitialized) return;

            try
            {
                // 确保崩溃日志目录存在
                if (!Directory.Exists(crashLogFile))
                {
                    Directory.CreateDirectory(crashLogFile);
                }

                // 注册非UI线程未处理异常处理程序
                AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

                // LTSC P2：补上 UI 线程异常。
                // 原来只挂了 AppDomain.UnhandledException，那个只收非 UI 线程；
                // WPF 的 UI 线程异常走 DispatcherUnhandledException，不挂就连
                // ProcessExit 也不会触发——日志会停在抛异常那一行，不留任何痕迹。
                // 实测证据：三份日志都断在中间，Crashes 目录里没有对应文件。
                this.DispatcherUnhandledException += App_DispatcherUnhandledException;

                // 注册控制台Ctrl+C等终止信号处理
                Console.CancelKeyPress += Console_CancelKeyPress;

                // 注册系统会话结束事件（关机、注销等）
                SystemEvents.SessionEnding += SystemEvents_SessionEnding;

                // 注册进程退出处理程序
                AppDomain.CurrentDomain.ProcessExit += CurrentDomain_ProcessExit;

                // 尝试注册Windows关闭消息监听
                SetConsoleCtrlHandler(ConsoleCtrlHandler, true);

                try
                {
                    TrySetupTerminationMonitoring();
                }
                catch (Exception monitorEx)
                {
                    LogHelper.WriteLogToFile($"设置终止监控失败: {monitorEx.Message}", LogHelper.LogType.Warning);
                }

                crashListenersInitialized = true;
                LogHelper.WriteLogToFile("已初始化崩溃监听器");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"初始化崩溃监听器失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void TrySetupTerminationMonitoring()
        {
            try
            {
                processDestroyHookCallback = OnWinEventMainWindowDestroyed;

                // 等主窗口句柄可用后再开始监听
                Dispatcher.BeginInvoke(new Action(BindMainWindowLifecycle), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"初始化终止监控失败: {ex.GetType().FullName}: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void BindMainWindowLifecycle()
        {
            try
            {
                if (Current?.MainWindow == null)
                {
                    return;
                }

                Current.MainWindow.SourceInitialized -= MainWindow_SourceInitialized;
                Current.MainWindow.SourceInitialized += MainWindow_SourceInitialized;
            }
            catch (Exception)
            {
            }
        }

        private void MainWindow_SourceInitialized(object sender, EventArgs e)
        {
            try
            {
                if (!(sender is Window window))
                {
                    return;
                }

                monitoredMainWindowHandle = new WindowInteropHelper(window).Handle;
                if (monitoredMainWindowHandle == IntPtr.Zero)
                {
                    return;
                }

                RegisterMainWindowDestroyHook();
            }
            catch (Exception)
            {
            }
        }

        private void RegisterMainWindowDestroyHook()
        {
            if (processDestroyHook != IntPtr.Zero || monitoredMainWindowHandle == IntPtr.Zero)
            {
                return;
            }

            processDestroyHook = SetWinEventHook(
                EVENT_OBJECT_DESTROY,
                EVENT_OBJECT_DESTROY,
                IntPtr.Zero,
                processDestroyHookCallback,
                (uint)currentProcessId,
                0,
                WINEVENT_OUTOFCONTEXT);

            if (processDestroyHook == IntPtr.Zero)
            {
                return;
            }
        }

        private void OnWinEventMainWindowDestroyed(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime)
        {
            if (eventType != EVENT_OBJECT_DESTROY || mainWindowDestroyedLogged)
            {
                return;
            }

            if (idObject != OBJID_WINDOW || idChild != CHILDID_SELF)
            {
                return;
            }

            if (hwnd != monitoredMainWindowHandle || hwnd == IntPtr.Zero)
            {
                return;
            }

            mainWindowDestroyedLogged = true;
        }

        private void CleanupTerminationMonitoring()
        {
            try
            {
                if (processDestroyHook != IntPtr.Zero)
                {
                    UnhookWinEvent(processDestroyHook);
                    processDestroyHook = IntPtr.Zero;
                }
            }
            catch
            {
            }
        }

        // Windows控制台控制处理程序
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleCtrlDelegate handler, bool add);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out FILETIME lpIdleTime, out FILETIME lpKernelTime, out FILETIME lpUserTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct FILETIME
        {
            public uint dwLowDateTime;
            public uint dwHighDateTime;
        }

        private delegate bool ConsoleCtrlDelegate(int ctrlType);
        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject, int idChild, uint dwEventThread, uint dwmsEventTime);

        private const uint EVENT_OBJECT_DESTROY = 0x8001;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;
        private const int OBJID_WINDOW = 0;
        private const int CHILDID_SELF = 0;

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc, WinEventDelegate lpfnWinEventProc, uint idProcess, uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        private static bool ConsoleCtrlHandler(int ctrlType)
        {
            string eventType = "未知控制类型";

            // 使用传统switch语句替代switch表达式
            switch (ctrlType)
            {
                case 0:
                    eventType = "CTRL_C_EVENT";
                    break;
                case 1:
                    eventType = "CTRL_BREAK_EVENT";
                    break;
                case 2:
                    eventType = "CTRL_CLOSE_EVENT";
                    break;
                case 5:
                    eventType = "CTRL_LOGOFF_EVENT";
                    break;
                case 6:
                    eventType = "CTRL_SHUTDOWN_EVENT";
                    break;
                default:
                    eventType = $"未知控制类型({ctrlType})";
                    break;
            }

            WriteCrashLog($"接收到系统控制信号: {eventType}");

            // 返回true表示已处理该事件
            return false;
        }

        // 系统会话结束事件处理
        private void SystemEvents_SessionEnding(object sender, SessionEndingEventArgs e)
        {
            string reason = e.Reason == SessionEndReasons.Logoff ? "用户注销" : "系统关机";
            WriteCrashLog($"系统会话即将结束: {reason}");

            if (!powerPointShutdownCleanupCompleted)
            {
                WriteCrashLog("PowerPoint模块等待WPF会话结束事件清理");
            }

        }

        private void App_SessionEnding(object sender, System.Windows.SessionEndingCancelEventArgs e)
        {
            CleanupPowerPointModuleForShutdown();
        }

        private void CleanupPowerPointModuleForShutdown()
        {
            if (powerPointShutdownCleanupCompleted) return;

            try
            {
                if (Current?.MainWindow is MainWindow mainWindow)
                {
                    mainWindow.UnloadPPTModuleForShutdown();
                    powerPointShutdownCleanupCompleted = true;
                    WriteCrashLog("PowerPoint模块已在系统关机时清理");
                }
            }
            catch (Exception ex)
            {
                WriteCrashLog($"清理资源失败: {ex.Message}");
            }
        }

        // 控制台取消事件处理
        private void Console_CancelKeyPress(object sender, ConsoleCancelEventArgs e)
        {
            WriteCrashLog($"接收到控制台中断信号: {e.SpecialKey}");
            e.Cancel = true; // 取消默认处理
        }

        // 处理非UI线程的未处理异常
        private void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
        {
            try
            {
                var exception = e.ExceptionObject as Exception;

                if (exception is System.Runtime.InteropServices.COMException comEx)
                {
                    var hr = (uint)comEx.HResult;
                    if (hr == 0x80004005 || hr == 0x8001010E || hr == 0x800706B5 ||
                        hr == 0x800706BA || hr == 0x8001010A || hr == 0x80010001 ||
                        hr == 0x80010108 || hr == 0x8001010D || hr == 0x800706BE)
                    {
                        LogHelper.WriteLogToFile(
                            $"非UI线程检测到PPT/WPS COM对象异常（已安全处理）: HR=0x{hr:X8}, {comEx.Message}",
                            LogHelper.LogType.Warning
                        );
                        return;
                    }
                }

                if (exception is System.Runtime.InteropServices.InvalidComObjectException)
                {
                    LogHelper.WriteLogToFile(
                        $"非UI线程检测到无效COM对象异常（已安全处理）: {exception.Message}",
                        LogHelper.LogType.Warning
                    );
                    return;
                }

                if (exception is InvalidOperationException invalidOpEx && IsCrossThreadUiAccess(invalidOpEx))
                {
                    LogHelper.WriteLogToFile(
                        $"检测到DynamicRenderer线程访问异常: {invalidOpEx.Message}",
                        LogHelper.LogType.Warning
                    );
                    return;
                }

                string errorMessage = exception?.ToString() ?? "未知异常";
                lastErrorMessage = errorMessage;

                WriteCrashLog($"捕获到未处理的异常: {errorMessage}");

                if (e.IsTerminating)
                {
                    WriteCrashLog("应用程序即将终止");
                }
            }
            catch (Exception ex)
            {
                // 尝试在最后时刻记录错误
                try
                {
                    string timeStr = (appStartTime != default(DateTime) && appStartTime != DateTime.MinValue)
                        ? appStartTime.ToString("yyyy-MM-dd-HH-mm-ss")
                        : DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
                    File.AppendAllText(
                        Path.Combine(crashLogFile, $"Crash_{timeStr}.txt"),
                        $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] 记录未处理异常时发生错误: {ex.Message}\r\n"
                    );
                }
                catch (Exception innerEx) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + innerEx.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
            }
        }

        // 处理进程退出事件
        private void CurrentDomain_ProcessExit(object sender, EventArgs e)
        {
            CleanupTerminationMonitoring();
            TimeSpan runDuration = DateTime.Now - appStartTime;
            string durationText = FormatTimeSpan(runDuration);
            WriteCrashLog($"应用程序退出，运行时长: {durationText}");

            // 如果有最后错误消息，记录到日志
            if (!string.IsNullOrEmpty(lastErrorMessage))
            {
                WriteCrashLog($"最后错误信息: {lastErrorMessage}");
            }
        }

        // 格式化时间跨度
        private static string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalDays >= 1)
            {
                return $"{timeSpan.Days}天 {timeSpan.Hours}小时 {timeSpan.Minutes}分钟";
            }

            if (timeSpan.TotalHours >= 1)
            {
                return $"{timeSpan.Hours}小时 {timeSpan.Minutes}分钟";
            }

            if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.Minutes}分钟 {timeSpan.Seconds}秒";
            }

            return $"{timeSpan.Seconds}秒";
        }

        private static void UpdateCpuUsageSnapshot()
        {
            try
            {
                if (!GetSystemTimes(out FILETIME idleTime, out FILETIME kernelTime, out FILETIME userTime))
                {
                    return;
                }

                DateTime sampleTime = DateTime.UtcNow;
                ulong currentIdleTime = ToUInt64(idleTime);
                ulong currentKernelTime = ToUInt64(kernelTime);
                ulong currentUserTime = ToUInt64(userTime);
                // LTSC C-5：Process 实现了 IDisposable，这里用 using 是正确的写法。
                //
                // 2026-11-17 更正：上面原来写着「GetCurrentProcess() 每次都会开一个内核句柄，
                // 45 分钟约 2700 次，从不释放」——**这个结论被实测推翻了**。
                // 实测：Process.GetCurrentProcess() 返回的是缓存伪句柄，
                // 300 次往返句柄增长为 0，对照组（故意不释放真实 FileStream）涨 300。
                // 也就是说这条从来就不是泄漏点，加 using 是对的写法，但不是修复了泄漏。
                // 详见 docs/PRIORITY.md 的 C-5 节：P1 → P3，标「实测不成立」。
                TimeSpan currentProcessCpuTime;
                using (var selfProcess = Process.GetCurrentProcess())
                {
                    currentProcessCpuTime = selfProcess.TotalProcessorTime;
                }

                lock (cpuUsageLock)
                {
                    if (hasCpuUsageSample)
                    {
                        ulong idleDelta = currentIdleTime - previousSystemIdleTime;
                        ulong kernelDelta = currentKernelTime - previousSystemKernelTime;
                        ulong userDelta = currentUserTime - previousSystemUserTime;
                        ulong totalDelta = kernelDelta + userDelta;

                        if (totalDelta > 0)
                        {
                            ulong busyDelta = totalDelta > idleDelta ? totalDelta - idleDelta : 0;
                            lastSystemCpuUsagePercent = Math.Clamp(busyDelta * 100d / totalDelta, 0d, 100d);
                        }

                        double elapsedSeconds = (sampleTime - previousCpuSampleTime).TotalSeconds;
                        double processCpuSeconds = (currentProcessCpuTime - previousProcessTotalProcessorTime).TotalSeconds;
                        if (elapsedSeconds > 0 && Environment.ProcessorCount > 0 && processCpuSeconds >= 0)
                        {
                            lastProcessCpuUsagePercent = Math.Clamp(processCpuSeconds / (elapsedSeconds * Environment.ProcessorCount) * 100d, 0d, 100d);
                        }
                    }

                    previousSystemIdleTime = currentIdleTime;
                    previousSystemKernelTime = currentKernelTime;
                    previousSystemUserTime = currentUserTime;
                    previousProcessTotalProcessorTime = currentProcessCpuTime;
                    previousCpuSampleTime = sampleTime;
                    hasCpuUsageSample = true;
                }
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }

        private static ulong ToUInt64(FILETIME fileTime)
        {
            return ((ulong)fileTime.dwHighDateTime << 32) | fileTime.dwLowDateTime;
        }

        private static string FormatCpuUsagePercent(double? cpuUsagePercent)
        {
            return cpuUsagePercent.HasValue
                ? cpuUsagePercent.Value.ToString("F1", CultureInfo.InvariantCulture) + "%"
                : "采样不足";
        }




        /// <summary>
        /// 取给人看的版本号。
        ///
        /// 不再用 Assembly.Version 拼——NBGV 会覆盖 AssemblyVersion，
        /// 拼出来是 1.0.0.28 这种和产品版本对不上的数。
        /// 直接读 InformationalVersion（形如 0.1a+abc1234），
        /// 取加号前那一段。
        /// </summary>
        internal static string GetDisplayVersion()
        {
            try
            {
                var info = Assembly.GetExecutingAssembly()
                    .GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                {
                    var s = info.InformationalVersion.Trim();
                    int plus = s.IndexOf('+');
                    if (plus > 0) s = s.Substring(0, plus);
                    if (!string.IsNullOrEmpty(s)) return s;
                }
            }
            catch { }

            // 兜底：InformationalVersion 读不到时退回四段数字
            try
            {
                var v = Assembly.GetExecutingAssembly().GetName().Version;
                if (v != null) return v.Major + "." + v.Minor + "." + v.Build + "." + v.Revision;
            }
            catch { }

            return "unknown";
        }



        private static bool IsLaunchByFileOrUri(string[] args)
        {
            if (args == null || args.Length == 0) return false;
            foreach (string a in args)
            {
                if (string.IsNullOrWhiteSpace(a)) continue;
                string t = a.Trim();
                if (t.StartsWith("icc:", StringComparison.OrdinalIgnoreCase)) return true;
                if (Path.GetExtension(t).Equals(".icstk", StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        // 记录崩溃日志
        private static void WriteCrashLog(string message)
        {
            try
            {
                UpdateCpuUsageSnapshot();

                // 确保目录存在
                if (!Directory.Exists(crashLogFile))
                {
                    Directory.CreateDirectory(crashLogFile);
                }

                string appStartTimeStr = (appStartTime != default(DateTime) && appStartTime != DateTime.MinValue)
                    ? appStartTime.ToString("yyyy-MM-dd-HH-mm-ss")
                    : DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
                string logFileName = Path.Combine(crashLogFile, $"Crash_{appStartTimeStr}.txt");

                // 收集系统状态信息
                var currentProcess = Process.GetCurrentProcess();
                string memoryUsage = (currentProcess.WorkingSet64 / (1024 * 1024)) + " MB";
                string cpuTime = currentProcess.TotalProcessorTime.ToString();
                string processUptime = FormatTimeSpan(DateTime.Now - currentProcess.StartTime);
                string systemCpuUsage = FormatCpuUsagePercent(lastSystemCpuUsagePercent);
                string processCpuUsage = FormatCpuUsagePercent(lastProcessCpuUsagePercent);

                string statusInfo = $"[内存: {memoryUsage}, CPU时间: {cpuTime}, 进程CPU占用: {processCpuUsage}, 系统CPU占用: {systemCpuUsage}, 运行时长: {processUptime}]";

                // 写入日志
                File.AppendAllText(
                    logFileName,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [PID:{currentProcessId}] {message}\r\n{statusInfo}\r\n\r\n"
                );

                // 同时记录到主日志
                LogHelper.WriteLogToFile(message, LogHelper.LogType.Error);
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }

        // 增加字段保存崩溃后操作设置
        public static CrashActionType CrashAction = CrashActionType.ShowCrashWindow;

        /// <summary>
        /// LTSC D-03: 在启动看门狗之前，把 CrashAction 从 Settings.json 读出来。
        ///
        /// 为什么需要它：<see cref="StartWatchdogIfNeeded"/> 的调用点在 App 构造函数里，
        /// 而 Settings.json 原本要到 App_Startup 才读。构造函数期 CrashAction 恒为字段默认值
        /// （ShowCrashWindow），导致两个后果：
        ///   1. 用户把「崩溃后操作」设为「静默重启」后，必须整个重启应用才生效，UI 无提示；
        ///   2. 「静默重启」这个配置在课堂里恰恰是最需要可靠生效的一档。
        ///
        /// 本方法只做读取与赋值，不启动任何东西，失败时保持默认值（弹窗），
        /// 因此最坏情况等价于修复前的行为，不会引入新的静默失败。
        /// </summary>
        private static void TrySyncCrashActionBeforeWatchdog()
        {
            try
            {
                SyncCrashActionFromParsed(ReadSettingsJsonOnce());
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[LTSC D-03] 提前同步 CrashAction 失败，保持默认值: {ex.Message}");
            }
        }

        public static void SyncCrashActionFromSettings()
        {
            try
            {
                // 优先从 Settings.json 直接读取
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "Settings.json");
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    dynamic obj = JsonConvert.DeserializeObject(json);
                    int crashAction = 2;
                    try { crashAction = (int)(obj["startup"]["crashAction"] ?? 2); } catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                    CrashAction = (CrashActionType)crashAction;
                }
                // 从主窗口同步
                else if (Ink_Canvas.MainWindow.Settings != null && Ink_Canvas.MainWindow.Settings.Startup != null)
                {
                    CrashAction = (CrashActionType)Ink_Canvas.MainWindow.Settings.Startup.CrashAction;
                }
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }

        private static void SyncCrashActionFromParsed(dynamic parsedSettings)
        {
            try
            {
                int crashAction = 2;
                try { crashAction = (int)(parsedSettings?["startup"]?["crashAction"] ?? 2); } catch { }
                CrashAction = (CrashActionType)crashAction;
            }
            catch { }
        }

        private void App_DispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (e.Exception is System.Runtime.InteropServices.COMException comEx)
            {
                var hr = (uint)comEx.HResult;
                if (hr == 0x80004005 || hr == 0x8001010E || hr == 0x800706B5 ||
                    hr == 0x800706BA || hr == 0x8001010A || hr == 0x80010001 ||
                    hr == 0x80010108 || hr == 0x8001010D || hr == 0x800706BE)
                {
                    LogHelper.WriteLogToFile(
                        $"检测到PPT/WPS COM对象异常（已安全处理）: HR=0x{hr:X8}, {comEx.Message}",
                        LogHelper.LogType.Warning
                    );
                    e.Handled = true;
                    return;
                }
            }

            if (e.Exception is System.Runtime.InteropServices.InvalidComObjectException)
            {
                LogHelper.WriteLogToFile(
                    $"检测到无效COM对象异常（已安全处理）: {e.Exception.Message}",
                    LogHelper.LogType.Warning
                );
                e.Handled = true;
                return;
            }

            // 检查是否是DynamicRenderer线程访问UI对象的已知问题
            // 检查是否是 DynamicRenderer / 跨线程访问 UI 对象的已知问题。
            // 判据见 IsCrossThreadUiAccess：只认类型名，不认会随语言变的异常消息。
            if (e.Exception is InvalidOperationException invalidOpEx && IsCrossThreadUiAccess(invalidOpEx))
            {
                LogHelper.WriteLogToFile(
                    $"检测到DynamicRenderer线程访问异常（已安全处理）: {invalidOpEx.Message}",
                    LogHelper.LogType.Warning
                );
                e.Handled = true;
                return;
            }

            Ink_Canvas.MainWindow.ShowNewMessage(MainWindowStrings.Main_App_UnexpectedError);
            LogHelper.NewLog(e.Exception.ToString());

            // 记录到崩溃日志
            lastErrorMessage = e.Exception.ToString();
            WriteCrashLog($"UI线程未处理异常: {e.Exception}");

            e.Handled = true;

            SyncCrashActionFromSettings(); // 崩溃时同步最新设置

            if (CrashAction == CrashActionType.ShowCrashWindow)
            {
                try
                {
                    new CrashWindow
                    {
                        CrashInfo = lastErrorMessage
                    }.ShowDialog();
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                return;
            }

            if (CrashAction == CrashActionType.SilentRestart && !IsAppExitByUser)
            {
                TryRestartWithBreaker($"UI线程未处理异常: {e.Exception.GetType().Name}");
            }
            // CrashActionType.NoAction 时不做处理
        }
    }
}
