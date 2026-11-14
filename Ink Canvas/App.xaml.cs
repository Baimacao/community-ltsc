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
using SplashScreen = Ink_Canvas.Windows.SplashScreen;
using Timer = System.Threading.Timer;

namespace Ink_Canvas
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        Mutex mutex;

        /// <summary>
        /// UI 线程卡死超过这个秒数就强制重启，不看 CrashAction 档位。
        /// 理由见 StartHeartbeatMonitor 心跳分支里的注释：
        /// 卡死时弹窗档的 ShowCrashWindow 排不进消息循环、不处理档等于让用户干等。
        /// 取 20 秒——足够区分「短暂抖一下」和「真卡死」。
        /// </summary>
        private const double ForceRestartAfterSeconds = 20.0;

        public void ReleaseMutexForRestart()
        {
            try
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                    mutex = null;
                }
            }
            catch (Exception ex)
            {
                ExceptionHandler.HandleException(ex, "释放互斥体失败（重启时）", LogHelper.LogType.Warning);
            }
        }

        public static string[] StartArgs;
        public static string RootPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;

        // 新增：标记是否通过--board参数启动
        public static bool StartWithBoardMode = false;
        // 新增：标记是否通过--show参数启动
        public static bool StartWithShowMode = false;
        // 新增：是否启用快速启动模式（默认关闭）
        public static bool IsFastStartupEnabled { get; private set; }
        // 新增：保存看门狗进程对象
        public static Process watchdogProcess;
        // 新增：标记是否为软件内主动退出
        public static bool IsAppExitByUser;
        // 新增：标记是否正在触发安装更新（用于跳过某些交互确认）
        public static bool IsUpdateInstalling;
        // 新增：标记是否启用了UIA置顶功能
        public static bool IsUIAccessTopMostEnabled;
        // 新增：标记是否正在显示 OOBE（首次启动向导），看门狗在此期间不判定为卡死/假死
        public static bool IsOobeShowing;
        // 新增：退出信号文件路径
        private static string watchdogExitSignalFile = Path.Combine(Path.GetTempPath(), "icc_watchdog_exit_" + Process.GetCurrentProcess().Id + ".flag");
        // 新增：崩溃日志文件路径
        private static string crashLogFile = Path.Combine(AppDomain.CurrentDomain.SetupInformation.ApplicationBase, "Crashes");
        // 新增：进程ID
        private static int currentProcessId = Process.GetCurrentProcess().Id;
        // 新增：应用启动时间
        internal static DateTime appStartTime { get; private set; }
        // 新增：最后一次错误信息
        private static string lastErrorMessage = string.Empty;
        private volatile bool powerPointShutdownCleanupCompleted;
        // 新增：是否已初始化崩溃监听器
        private static bool crashListenersInitialized;
        private static readonly object cpuUsageLock = new object();
        private static bool hasCpuUsageSample;
        private static ulong previousSystemIdleTime;
        private static ulong previousSystemKernelTime;
        private static ulong previousSystemUserTime;
        private static TimeSpan previousProcessTotalProcessorTime;
        private static DateTime previousCpuSampleTime = DateTime.MinValue;
        private static double? lastSystemCpuUsagePercent;
        private static double? lastProcessCpuUsagePercent;
        private IntPtr processDestroyHook = IntPtr.Zero;
        private IntPtr monitoredMainWindowHandle = IntPtr.Zero;
        private bool mainWindowDestroyedLogged;
        private WinEventDelegate processDestroyHookCallback;
        // 新增：启动画面相关
        private static SplashScreen _splashScreen;
        private static bool _isSplashScreenShown = false;
        // _pendingLocalizedResourceSet removed - using Strings.LoadAllToResources
        private static readonly Stopwatch startupStopwatch = new Stopwatch();
        private static readonly Stopwatch splashStopwatch = new Stopwatch();

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int SetCurrentProcessExplicitAppUserModelID(string appId);

        /// <summary>
        /// 判断原版 ICC-CE（不是本程序）是否正在运行。
        /// LTSC（2026-11-13）：两个白板同时开会抢同一支触控笔，
        /// 老师只会看到"画不出线"，想不到是两个程序打架。所以 LTSC 主动让路。
        ///
        /// 判据说明（这里容易错）：
        ///   1. 两者 AssemblyName 都是 InkCanvasForClass，进程名区分不了；
        ///   2. 只能靠可执行文件路径区分——本程序路径 != 对方路径，才算原版；
        ///   3. 必须排除自己（GetProcessesByName 会把自己也列进来）。
        /// </summary>
        /// <summary>
        /// 判断一个 InvalidOperationException 是不是「跨线程访问 UI 对象」。
        ///
        /// LTSC（2026-11-13）为什么不能只看 Message：
        ///   WPF 的这条异常消息来自系统资源字符串，随系统语言变化。
        ///   原来的判据写死了中文"调用线程无法访问此对象"和英文
        ///   "because another thread owns it"，但英文系统实际给的是
        ///   "because a different thread owns it"——另一个 vs 一个不同，对不上。
        ///   结果就是：英文环境下这道防护形同虚设。
        ///
        /// 可靠判据：WPF 抛这个异常前一定会走 VerifyAccess，
        /// 而 VerifyAccess 是类型名，不随语言翻译。堆栈里出现下面任一即可。
        /// </summary>
        private static bool IsCrossThreadUiAccess(Exception ex)
        {
            try
            {
                var message = ex?.Message ?? "";
                var stack = ex?.StackTrace ?? "";

                // 堆栈里的 WPF 内部类型名（语言无关）
                if (stack.IndexOf("VerifyAccess", StringComparison.Ordinal) >= 0) return true;
                if (stack.IndexOf("DispatcherObject", StringComparison.Ordinal) >= 0) return true;
                if (stack.IndexOf("DependencyObject", StringComparison.Ordinal) >= 0 &&
                    stack.IndexOf("System.Windows.Threading", StringComparison.Ordinal) >= 0) return true;

                // 原来的文本判据保留（能命中的场景别丢），但补上 "a different thread"
                if (message.Contains("调用线程无法访问此对象")) return true;
                if (message.Contains("because another thread owns it")) return true;
                if (message.Contains("because a different thread owns it")) return true;
                if (message.Contains("cannot access this object because")) return true;

                // InkCanvas 渲染链路的已知调用点
                if (stack.IndexOf("DynamicRenderer", StringComparison.Ordinal) >= 0) return true;
                if (stack.IndexOf("CompositionTarget.get_RootVisual", StringComparison.Ordinal) >= 0) return true;

                return false;
            }
            catch { return false; }
        }

        private static bool IsUpstreamIccRunning()
        {
            try
            {
                string selfPath = null;
                try { selfPath = System.Reflection.Assembly.GetEntryAssembly()?.Location; } catch { }
                if (string.IsNullOrEmpty(selfPath))
                {
                    try { selfPath = Process.GetCurrentProcess().MainModule?.FileName; } catch { }
                }
                if (string.IsNullOrEmpty(selfPath)) return false;

                foreach (var p in Process.GetProcessesByName("InkCanvasForClass"))
                {
                    try
                    {
                        if (p.Id == Process.GetCurrentProcess().Id) continue;
                        string otherPath = p.MainModule?.FileName;
                        if (string.IsNullOrEmpty(otherPath)) continue;
                        // 路径相同 = 同一个程序开两份，交给原互斥体逻辑处理；
                        // 路径不同 = 原版 ICC 在跑，LTSC 让路。
                        if (!string.Equals(otherPath, selfPath, StringComparison.OrdinalIgnoreCase))
                        {
                            LogHelper.WriteLogToFile(
                                $"发现另一个 ICC 实例: {otherPath}（本程序: {selfPath}）",
                                LogHelper.LogType.Event);
                            return true;
                        }
                    }
                    catch { }
                    finally { try { p.Dispose(); } catch { } }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("IsUpstreamIccRunning: " + ex);
            }
            return false;
        }

        public App()
        {
            System.Windows.Forms.Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);

            // 注意：此处显式禁用 Switch.System.Windows.Input.Stylus.EnablePointerSupport。
            // 启用该开关会让 WPF 使用 WM_POINTER 触摸栈，导致 DragMove() 和 DoDragDrop()
            // （gong-wpf-dragdrop 库内部使用）的模态消息循环无法接收触摸释放消息。
            // 在模拟触摸屏（UU 远程、spacedesk 等远程控制软件注入的虚拟触摸）下，
            // 拖动操作会进入假死状态，直到呼出鼠标或点击其他窗口生成真实鼠标消息才解除。
            // ClassIsland 项目同样注释掉了该开关，参考其做法保持一致。
            // AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", true);

            try
            {
                // LTSC：和上游 ICC-CE 分开，任务栏里不再合并图标。
                // 这一处必须和上游不同，否则同一台电脑上装两个版本时任务栏会并成一个。
                SetCurrentProcessExplicitAppUserModelID("InkCanvasForClass.LTSC");
            }
            catch
            {
            }

            // 如果是看门狗子进程，直接进入看门狗主循环并终止主流程
            var args = Environment.GetCommandLineArgs();
            if (args.Length >= 2 && args[1] == "--watchdog")
            {
                RunWatchdogIfNeeded();
                Environment.Exit(0);
                return;
            }

            if (args.Contains("--enable-uia-topmost-helper"))
            {
                // 检查是否为原进程令牌模式（通过 --uia-source-pid 参数判断）
                uint sourcePid = 0;
                for (int i = 0; i < args.Length - 1; i++)
                {
                    if (string.Equals(args[i], "--uia-source-pid", StringComparison.OrdinalIgnoreCase)
                        && uint.TryParse(args[i + 1], out uint parsedPid))
                    {
                        sourcePid = parsedPid;
                        break;
                    }
                }

                if (sourcePid != 0)
                {
                    Environment.Exit(UIAccessHelper.LaunchNormalUserWithUIAccessFromElevatedHelper_ProcessToken(sourcePid) ? 0 : 1);
                }
                else
                {
                    Environment.Exit(UIAccessHelper.LaunchNormalUserWithUIAccessFromElevatedHelper() ? 0 : 1);
                }
                return;
            }

            // LTSC D-03: 在启动看门狗之前就把 CrashAction 从 Settings.json 读出来。
            //
            // 原实现的问题：StartWatchdogIfNeeded() 在本构造函数里判断
            //   if (CrashAction == CrashActionType.SilentRestart)
            // 而那一刻 CrashAction 还是字段默认值 ShowCrashWindow（见下方字段声明），
            // Settings.json 要到 App_Startup 才读。结果是：
            //   - 默认配置下看门狗永远不启动（正确）；
            //   - 但用户把「崩溃后操作」改成「静默重启」后，也必须重启应用才生效，
            //     而且 UI 上没有任何提示。
            //
            // 修法：ReadSettingsJsonOnce + SyncCrashActionFromParsed 都是静态方法，
            // 不依赖 MainWindow，可以在这里提前调用。调用后 CrashAction 就是用户真实设置。
            // 注意：不改变默认值（仍是 ShowCrashWindow = 弹窗），只是把时序提前。
            TrySyncCrashActionBeforeWatchdog();

            Startup += App_Startup;
            SessionEnding += App_SessionEnding;
            DispatcherUnhandledException += App_DispatcherUnhandledException;
            StartHeartbeatMonitor();

            // 初始化全局异常和进程结束处理
            InitializeCrashListeners();

            // 仅在崩溃后操作为静默重启时才启动看门狗
            // 在更新模式下不启动看门狗，避免干扰更新流程
            args = Environment.GetCommandLineArgs();
            bool isUpdateMode = args.Contains("--update-mode");
            bool isFinalApp = args.Contains("--final-app");

            if (CrashAction == CrashActionType.SilentRestart && !isUpdateMode && !isFinalApp)
            {
                StartWatchdogIfNeeded();
            }
            Exit += App_Exit; // 注册退出事件
        }

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

            DeviceIdentifier.SaveUsageStatsOnShutdown();
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
                catch (Exception innerEx) { System.Diagnostics.Debug.WriteLine(innerEx); }
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
                // LTSC C-5：Process 实现了 IDisposable，GetCurrentProcess() 每次都会开一个内核句柄。
                // 这一句由 heartbeat 定时器每秒执行一次，45 分钟约 2700 次，从不释放。
                // 实测里观察到的「句柄只涨不落」有一部分就是它。
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
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

        public static void ShowSplashScreen()
        {
            if (_isSplashScreenShown)
            {
                LogHelper.WriteLogToFile("启动画面已经显示，跳过重复显示");
                return;
            }

            try
            {
                LogHelper.WriteLogToFile("开始创建启动画面...");
                _splashScreen = new SplashScreen();
                LogHelper.WriteLogToFile("启动画面对象创建成功，准备显示...");
                _splashScreen.Show();
                _isSplashScreenShown = true;
                splashScreenStartTime = DateTime.Now;
                splashStopwatch.Restart();
                LogHelper.WriteLogToFile("启动画面已显示");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"显示启动画面失败: {ex.Message}", LogHelper.LogType.Error);
                LogHelper.WriteLogToFile($"异常堆栈: {ex.StackTrace}", LogHelper.LogType.Error);
            }
        }

        // 关闭启动画面
        public static void CloseSplashScreen()
        {
            if (!_isSplashScreenShown || _splashScreen == null) return;

            try
            {
                _splashScreen.CloseSplashScreen();
                _isSplashScreenShown = false;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"关闭启动画面失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        // 设置启动画面进度
        public static void SetSplashProgress(int progress)
        {
            if (_splashScreen != null)
            {
                _splashScreen.SetProgress(progress);
            }
        }

        // 设置启动画面消息
        public static void SetSplashMessage(string message)
        {
            if (_splashScreen != null)
            {
                _splashScreen.SetLoadingMessage(message);
            }
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
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
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
                    try { crashAction = (int)(obj["startup"]["crashAction"] ?? 2); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                    CrashAction = (CrashActionType)crashAction;
                }
                // 从主窗口同步
                else if (Ink_Canvas.MainWindow.Settings != null && Ink_Canvas.MainWindow.Settings.Startup != null)
                {
                    CrashAction = (CrashActionType)Ink_Canvas.MainWindow.Settings.Startup.CrashAction;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
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
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                return;
            }

            if (CrashAction == CrashActionType.SilentRestart && !IsAppExitByUser)
            {
                TryRestartWithBreaker($"UI线程未处理异常: {e.Exception.GetType().Name}");
            }
            // CrashActionType.NoAction 时不做处理
        }

        private TaskbarIcon _taskbar;

        /// <summary>
        /// 处理应用启动流程：根据命令行与设置显示启动画面、初始化组件与遥测、处理更新相关逻辑、单实例检查并在必要时通过 IPC 与已运行实例通信，最终创建并显示主窗口并启动文件关联与 IPC 监听器。
        /// </summary>
        /// <param name="sender">事件的发送者（通常为 Application 对象）。</param>
        /// <param name="e">启动事件参数；其 Args 可包含控制启动流程的标志，例如:
        /// - "--final-app"：表示这是更新后的最终应用启动（会清理更新标记等）
        /// - "--update-mode"：表示以更新模式启动（跳过主窗口显示）
        /// - "--board"：直接进入白板模式
        /// - "--show"：退出收纳模式并恢复浮动栏
        /// - "--skip-mutex-check"：跳过单实例互斥检查
        /// - "-m"：允许多实例启动
        /// 另外也可能包含以 "icc:" 开头的 URI 参数或 .icstk 文件路径用于启动时的 IPC 交互。</param>
        /// <summary>
        /// LTSC D-09：检测到以管理员权限启动时的处理入口。
        /// 由设置项 Advanced.AdminLaunchAction 控制：
        ///   0 = 弹窗提醒（默认）
        ///   1 = 自动以普通权限重启
        ///   2 = 不做处理
        /// </summary>
        private void TryHandleAdminLaunch()
        {
            try
            {
                if (!AppRestartHelper.IsRunningAsAdmin()) return;

                int action = 0;
                try
                {
                    action = SettingsManager.Settings?.Advanced?.AdminLaunchAction ?? 0;
                }
                catch
                {
                    action = 0;
                }

                LogHelper.WriteLogToFile($"检测到以管理员权限运行，处理方式={action}", LogHelper.LogType.Warning);

                if (action == 2) return; // 不处理

                if (action == 1)
                {
                    // 自动降权重启。给日志留一行，方便回查。
                    LogHelper.WriteLogToFile("按设置自动以普通权限重启", LogHelper.LogType.Event);
                    AppRestartHelper.RestartApp(false);
                    return;
                }

                // 默认：弹窗提醒。延迟到主窗口建立后再弹，避免盖在启动画面上。
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var win = new Windows.AdminLaunchWindow();
                        win.ShowDialog();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"显示管理员权限提醒失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                }), DispatcherPriority.ApplicationIdle);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"管理员权限检测异常: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        async void App_Startup(object sender, StartupEventArgs e)
        {
            appStartTime = DateTime.Now;
            appStartupStartTime = DateTime.Now;
            startupStopwatch.Restart();

            // 启动阶段跳过昂贵的 StackTrace 采集
            LogHelper.SuppressCallerInfo = true;

            // 一次性读取并解析 Settings.json，避免重复 I/O + dynamic 反序列化
            dynamic parsedSettings = ReadSettingsJsonOnce();

            // 从缓存设置同步 CrashAction（替代原构造函数中的 SyncCrashActionFromSettings）
            SyncCrashActionFromParsed(parsedSettings);
            IsFastStartupEnabled = IsFastStartupEnabledFromParsed(parsedSettings);
            LogHelper.WriteLogToFile($"App | 快速启动模式: {(IsFastStartupEnabled ? "启用" : "关闭")}");

            TryApplyPreferredLanguageFromParsedSettings(parsedSettings);

            // 根据设置决定是否显示启动画面（复用已解析的设置对象）
            if (ShouldShowSplashScreenFromParsed(parsedSettings) && !IsLaunchByFileOrUri(e.Args))
            {
                // 注入缓存 JSON 给 SplashScreen，避免其构造期间重复读取 + 解析 Settings.json
                SplashScreen.CachedSettingsJson = CachedSettingsJson;
                ShowSplashScreen();
                SetSplashMessage("正在启动 Ink Canvas...");
                SetSplashProgress(25);

                // 强制刷新UI，确保启动画面显示
                Application.Current.Dispatcher.Invoke(() => { }, DispatcherPriority.Render);
            }
            RootPath = AppDomain.CurrentDomain.SetupInformation.ApplicationBase;

            var version = Assembly.GetExecutingAssembly().GetName().Version;
            var informationalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
            string versionString = version.Major + "." + version.Minor + "." + version.Build + "." + version.Revision;
            if (informationalVersion != null)
            {
                string infoVersion = informationalVersion.InformationalVersion;
                int lastDotIndex = infoVersion.LastIndexOf('.');
                if (lastDotIndex >= 0 && lastDotIndex < infoVersion.Length - 7)
                {
                    versionString += " (" + infoVersion.Substring(lastDotIndex + 1) + ")";
                }
            }
            LogHelper.NewLog(string.Format("Ink Canvas Starting (Version: {0})", versionString));

            // LTSC D-09：管理员权限检测。
            // Windows UIPI 会隔断不同完整性级别的进程，ICC 以管理员运行会连不上普通权限的
            // PowerPoint（GetActiveObject → MK_E_UNAVAILABLE），PPT 联动整体失效。
            // 处理方式由设置项 AdminLaunchAction 决定：0 弹窗 / 1 自动降权重启 / 2 不处理。
            TryHandleAdminLaunch();

            // 检查是否为最终应用启动（更新后的应用）
            bool isFinalApp = e.Args.Contains("--final-app");
            bool skipMutexCheck = e.Args.Contains("--skip-mutex-check");

            // 检查是否通过--board参数启动
            bool hasBoardArg = e.Args.Contains("--board");
            if (hasBoardArg)
            {
                StartWithBoardMode = true;
                LogHelper.WriteLogToFile("App | 检测到--board参数，将直接进入白板模式");
            }

            // 检查是否通过--show参数启动
            bool hasShowArg = e.Args.Contains("--show");
            if (hasShowArg)
            {
                StartWithShowMode = true;
                LogHelper.WriteLogToFile("App | 检测到--show参数，将退出收纳模式并恢复浮动栏");
            }

            // 记录最终应用启动状态
            if (isFinalApp)
            {
                LogHelper.WriteLogToFile("App | 检测到最终应用启动（更新后的应用）");
            }

            if (_isSplashScreenShown)
            {
                SetSplashMessage("正在加载配置...");
                SetSplashProgress(50);
            }

            // 处理更新模式启动
            bool isUpdateMode = AutoUpdateHelper.HandleUpdateModeStartup(e.Args);

            // 如果是更新模式，不显示主窗口但保持应用运行
            if (isUpdateMode)
            {
                LogHelper.WriteLogToFile("App | 检测到更新模式，跳过主窗口显示，保持应用运行");
                return;
            }

            // LTSC UI-2：这里原来是整块「等待更新进程结束」的逻辑——
            // 读 update_in_progress.tmp → 找进程 → while(...) Thread.Sleep(500) 最长 5 秒。
            // 它跑在 App_Startup 里、主窗口 Show() 之前，也就是老师双击图标后
            // 那几秒空白。
            //
            // D-01 把更新功能整个移除之后，update_in_progress.tmp 在全库范围内
            // 只被读、没有任何地方写（已全量搜索确认），这个分支永远不会进入。
            // 留着它只是让人以为「还有更新流程」，所以连判断一起删掉，
            // 只保留「顺手清理旧版残留文件」这一件仍然有意义的事。
            string updateMarkerFile = Path.Combine(RootPath, "update_in_progress.tmp");
            bool isUpdateInProgress = false;

            if (isFinalApp)
            {
                try
                {
                    if (File.Exists(updateMarkerFile))
                    {
                        File.Delete(updateMarkerFile);
                        LogHelper.WriteLogToFile("App | 最终应用启动，清理旧版更新标记文件");
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"App | 清理旧版更新标记文件失败: {ex.Message}", LogHelper.LogType.Warning);
                }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        await Task.Delay(3000);
                        AutoUpdateHelper.DeleteUpdatesFolder();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"App | 删除旧版 AutoUpdate 文件夹失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                });
            }

            // 如果是更新过程、更新模式、最终应用或跳过Mutex检查，跳过Mutex检查
            if (!isUpdateInProgress && !isUpdateMode && !isFinalApp && !skipMutexCheck)
            {
                bool ret;
                // LTSC 自己的互斥体：加 LTSC 后缀，和上游 ICC-CE 分开。
                mutex = new Mutex(true, "InkCanvasForClass CE LTSC", out ret);

                // LTSC（2026-11-13）方案 B：多一层保护。
                // 光把互斥体改名只是让两个版本能共存，但两个白板同时开着会抢同一支笔，
                // 老师只会看到"画不出线"，根本想不到是两个程序在打架。
                // 所以：检测到原版 ICC-CE 正在运行就直接退出，并明确告诉用户怎么办。
                if (!ret && !e.Args.Contains("-m") && IsUpstreamIccRunning())
                {
                    LogHelper.NewLog("Detected upstream ICC-CE running");
                    LogHelper.WriteLogToFile(
                        "检测到原版 ICC-CE 正在运行，LTSC 拒绝启动（避免两个白板抢触控笔）",
                        LogHelper.LogType.Warning);
                    // 注意：这里可能还压着 ICC 自己的"以管理员权限运行"提示框，
                    // 直接 Show 会和它叠在一起、谁也不点就一起挂住。
                    // 所以先做退出清理，再在一个短命线程里弹窗，主线程立刻走人。
                    IsAppExitByUser = true;
                    try
                    {
                        StartupCount.Reset();
                        File.WriteAllText(watchdogExitSignalFile, "exit");
                        if (watchdogProcess != null && !watchdogProcess.HasExited) watchdogProcess.Kill();
                    }
                    catch { }

                    try
                    {
                        var tip = new System.Threading.Thread(() =>
                        {
                            try
                            {
                                System.Windows.MessageBox.Show(
                                    "检测到原版 InkCanvasForClass（ICC-CE）正在运行。\n\n" +
                                    "两个白板同时开启会争用触控笔，可能出现画不出线、笔迹跑偏等问题。\n\n" +
                                    "ICC LTSC 现在退出。请先关闭原版 ICC，再重新启动 LTSC。",
                                    "ICC CE Lite",
                                    System.Windows.MessageBoxButton.OK,
                                    System.Windows.MessageBoxImage.Warning);
                            }
                            catch { }
                        });
                        tip.IsBackground = true;
                        tip.Start();
                        tip.Join(3000);   // 最多等 3 秒，用户不点也照退
                    }
                    catch { }

                    Environment.Exit(0);
                    return;
                }

                if (!ret && !e.Args.Contains("-m")) //-m multiple
                {
                    LogHelper.NewLog("Detected existing instance");

                    // 检查是否有.icstk文件参数
                    string icstkFile = FileAssociationManager.GetIcstkFileFromArgs(e.Args);
                    if (!string.IsNullOrEmpty(icstkFile))
                    {
                        LogHelper.WriteLogToFile($"检测到已运行实例，尝试通过IPC发送文件: {icstkFile}", LogHelper.LogType.Event);

                        // 尝试通过IPC发送文件路径给已运行实例
                        if (FileAssociationManager.TrySendFileToExistingInstance(icstkFile))
                        {
                            LogHelper.WriteLogToFile("文件路径已通过IPC发送给已运行实例", LogHelper.LogType.Event);
                        }
                        else
                        {
                            LogHelper.WriteLogToFile("通过IPC发送文件路径失败", LogHelper.LogType.Warning);
                        }
                    }
                    // 检查是否有--board参数
                    else if (hasBoardArg)
                    {
                        LogHelper.WriteLogToFile("检测到已运行实例且有--board参数，尝试通过IPC发送白板模式命令", LogHelper.LogType.Event);

                        // 尝试通过IPC发送白板模式命令给已运行实例
                        if (FileAssociationManager.TrySendBoardModeCommandToExistingInstance())
                        {
                            LogHelper.WriteLogToFile("白板模式命令已通过IPC发送给已运行实例", LogHelper.LogType.Event);
                        }
                        else
                        {
                            LogHelper.WriteLogToFile("通过IPC发送白板模式命令失败", LogHelper.LogType.Warning);
                        }
                    }
                    // 检查是否有--show参数
                    else if (hasShowArg)
                    {
                        LogHelper.WriteLogToFile("检测到已运行实例且有--show参数，尝试通过IPC发送展开浮动栏命令", LogHelper.LogType.Event);

                        // 尝试通过IPC发送展开浮动栏命令给已运行实例
                        if (FileAssociationManager.TrySendShowModeCommandToExistingInstance())
                        {
                            LogHelper.WriteLogToFile("展开浮动栏命令已通过IPC发送给已运行实例", LogHelper.LogType.Event);
                        }
                        else
                        {
                            LogHelper.WriteLogToFile("通过IPC发送展开浮动栏命令失败", LogHelper.LogType.Warning);
                        }
                    }
                    // 检查是否有URI参数
                    else if (e.Args.Any(a => a.StartsWith("icc:", StringComparison.OrdinalIgnoreCase)))
                    {
                        string uriArg = e.Args.FirstOrDefault(a => a.StartsWith("icc:", StringComparison.OrdinalIgnoreCase));
                        LogHelper.WriteLogToFile($"检测到已运行实例且有URI参数: {uriArg}", LogHelper.LogType.Event);

                        // 尝试通过IPC发送URI命令给已运行实例
                        if (FileAssociationManager.TrySendUriCommandToExistingInstance(uriArg))
                        {
                            LogHelper.WriteLogToFile("URI命令已通过IPC发送给已运行实例", LogHelper.LogType.Event);
                        }
                        else
                        {
                            LogHelper.WriteLogToFile("通过IPC发送URI命令失败", LogHelper.LogType.Warning);
                        }
                    }
                    else
                    {
                        LogHelper.WriteLogToFile("检测到已运行实例，但无文件参数", LogHelper.LogType.Event);
                    }

                    LogHelper.NewLog("Ink Canvas automatically closed");
                    IsAppExitByUser = true; // 多开时标记为用户主动退出
                    // 写入退出信号，确保看门狗不会重启
                    try
                    {
                        StartupCount.Reset();
                        File.WriteAllText(watchdogExitSignalFile, "exit");
                        if (watchdogProcess != null && !watchdogProcess.HasExited)
                        {
                            watchdogProcess.Kill();
                        }
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                    Environment.Exit(0);
                }
            }
            else
            {
                if (isUpdateMode)
                {
                    LogHelper.WriteLogToFile("App | 更新模式启动，跳过重复运行检测");
                }
                else if (isFinalApp)
                {
                    LogHelper.WriteLogToFile("App | 最终应用启动，跳过重复运行检测");
                }
                else if (skipMutexCheck)
                {
                    LogHelper.WriteLogToFile("App | 跳过Mutex检查模式启动，跳过重复运行检测");
                }
                else
                {
                    LogHelper.WriteLogToFile("App | 更新过程中，跳过重复运行检测");
                }

                // 在特殊模式下，创建一个临时的Mutex以避免其他检查出错
                string mutexName = isFinalApp ? "InkCanvasForClass CE LTSC Final" : "InkCanvasForClass CE LTSC Update";
                mutex = new Mutex(true, mutexName, out bool tempRet);

                // 额外等待一小段时间确保更新进程完全退出
                await Task.Delay(100);
                LogHelper.WriteLogToFile("App | 特殊模式等待完成，继续启动");
            }

            _taskbar = (TaskbarIcon)FindResource("TaskbarTrayIcon");

            StartArgs = e.Args;

            // 在非更新模式下创建主窗口
            if (_isSplashScreenShown)
            {
                SetSplashMessage("正在初始化主界面...");
                SetSplashProgress(75);
            }
            var mainWindow = new MainWindow();
            MainWindow = mainWindow;

            // 主窗口加载完成后关闭启动画面
            mainWindow.Loaded += (s, args) =>
            {
                isStartupComplete = true;
                startupCompleteHeartbeat = DateTime.Now;

                // 启动完成，恢复日志调用栈采集
                LogHelper.SuppressCallerInfo = false;

                // ── LTSC P5 / C5·C7（2026-11-11）─────────────────────────────
                // 原来这里是同步 Reset()，也就是「主窗口一加载完就清零」。
                //
                // 而崩溃重启循环的形状是：
                //   TryRestartWithBreaker → Increment() → count=1 → Process.Start → Exit
                //   新进程 → mainWindow.Loaded → Reset() → count=0
                //   再崩 → Increment() → count=1 → ...
                //
                // 计数永远停在 1，TryRestartWithBreaker 里那个 count >= 5 永远不成立，
                // 熔断形同虚设 —— 这就是社区说的「无限重启」。
                //
                // 修法：只有**稳定运行足够久**才算真启动成功。
                // 判据用「距上次重启记录的时间」：刚重启完就又走到这里，说明是崩溃循环，
                // 不清零；跑够 StableRunBeforeReset 才清。
                _ = ResetStartupCountWhenStable();

                if (_isSplashScreenShown && splashStopwatch.IsRunning)
                {
                    LogHelper.WriteLogToFile($"启动完成心跳已记录，启动画面显示时长: {splashStopwatch.Elapsed.TotalSeconds:F2}秒");
                }
                else
                {
                    LogHelper.WriteLogToFile($"启动完成心跳已记录");
                }
                LogHelper.WriteLogToFile($"启动时长: {startupStopwatch.Elapsed.TotalSeconds:F2}秒");

                if (_isSplashScreenShown)
                {
                    SetSplashMessage("启动完成！");
                    SetSplashProgress(100);
                    Task.Delay(100).ContinueWith(_ =>
                    {
                        Dispatcher.Invoke(() =>
                        {
                            // 延迟关闭启动画面，让用户看到完成消息
                            Task.Delay(100).ContinueWith(__ =>
                            {
                                Dispatcher.Invoke(() => CloseSplashScreen());
                            });
                        });
                    });
                }
            };

            mainWindow.Show();

            if (IsFastStartupEnabled)
            {
                _ = RunFastStartupPostRenderTasksAsync(mainWindow);
                _ = Dispatcher.BeginInvoke(new Action(() => _taskbar?.ForceCreate()), DispatcherPriority.ContextIdle);
            }
            else
            {
                WindowTopmostManager.Initialize(mainWindow, skipScan: true);
                _ = Task.Run(() => Dispatcher.Invoke(() => _taskbar?.ForceCreate()));
            }

            // LTSC D-03 自测入口：--test-crash 启动后延迟触发一次真实的 UI 线程未处理异常，
            // 用来验证 CrashAction 三档（静默重启 / 不处理 / 弹窗）是否按用户设置执行。
            // 这是可重复的回归测试开关，不是给老师用的功能；不带这个参数时完全不生效。
            if (e.Args.Contains("--test-crash"))
            {
                LogHelper.WriteLogToFile("[D-03自测] 收到 --test-crash，3 秒后触发模拟崩溃", LogHelper.LogType.Warning);
                _ = Task.Delay(3000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        LogHelper.WriteLogToFile("[D-03自测] 现在抛出 UI 线程未处理异常", LogHelper.LogType.Warning);
                        throw new InvalidOperationException("[D-03自测] --test-crash 触发的模拟崩溃");
                    }));
                });
            }

            // ===== LTSC 临时探针（验证完毕后整块删除）=====
            // 用途：ICC 的设置窗是 WPF 自绘，外部 UI Automation 读不到控件树，
            //   --test-crash 之外还需要一条能验证「页面能否真实构造」的路径。
            //   --dump-settings-pages 会把 36 个设置页逐个 new 出来并调用 Loaded，
            //   任何构造期异常（XAML 绑定失败、找不到资源键、控件类型不匹配）
            //   都会被捕获并写进日志。
            // 删除方式：搜 "LTSC 临时探针" 把整段和下面那个方法一起删掉即可。
            // 暴力测试：--stress-settings <轮数>，默认 5 轮 × 36 页 = 180 次构造
            if (e.Args.Any(a => a.StartsWith("--stress-settings")))
            {
                int rounds = 5;
                var idx = Array.FindIndex(e.Args, a => a.StartsWith("--stress-settings"));
                if (idx >= 0 && idx + 1 < e.Args.Length) int.TryParse(e.Args[idx + 1], out rounds);
                if (rounds <= 0) rounds = 5;
                var rr = rounds;
                _ = Task.Delay(4000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { Ink_Canvas.Helpers.LtscProbe.StressSettingsPages(rr); }
                        catch (Exception ex) { LogHelper.WriteLogToFile("[探针] 压力测试失败: " + ex, LogHelper.LogType.Error); }
                    }));
                });
            }

            if (e.Args.Any(x => x.StartsWith("--ppt-e2e")))
            {
                try
                {
                    int di = Array.FindIndex(e.Args, x => x.StartsWith("--ppt-e2e"));
                    string path = (di + 1 < e.Args.Length) ? e.Args[di + 1] : "";
                    Ink_Canvas.Helpers.LtscProbe.PptEndToEnd(path);
                }
                catch (Exception ex) { LogHelper.WriteLogToFile("PPT 测试异常: " + ex, LogHelper.LogType.Error); }
                Shutdown();
                return;
            }

            if (e.Args.Contains("--smoke-test"))
            {
                try { Ink_Canvas.Helpers.LtscProbe.SmokeTest(); }
                catch (Exception ex) { LogHelper.WriteLogToFile("冒烟自测异常: " + ex, LogHelper.LogType.Error); }
                Shutdown();
                return;
            }

            if (e.Args.Contains("--dump-settings-pages"))
            {
                _ = Task.Delay(4000).ContinueWith(_ =>
                {
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { Ink_Canvas.Helpers.LtscProbe.DumpSettingsPages(); }
                        catch (Exception probeEx)
                        {
                            LogHelper.WriteLogToFile("[探针] 整体失败: " + probeEx, LogHelper.LogType.Error);
                        }
                    }));
                });
            }
            // ===== LTSC 临时探针结束 =====

            // 处理启动时的URI参数
            string startupUriArg = e.Args.FirstOrDefault(a => a.StartsWith("icc:", StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(startupUriArg))
            {
                LogHelper.WriteLogToFile($"App | 处理启动URI参数: {startupUriArg}", LogHelper.LogType.Event);
                // 延迟一点执行，确保窗口初始化完成
                _ = Task.Delay(1000).ContinueWith(_ =>
                {
                    mainWindow.Dispatcher.Invoke(() =>
                    {
                        mainWindow.HandleUriCommand(startupUriArg);
                    });
                });
            }

            _ = RunDeferredStartupTasksAsync();
        }

        private async Task RunFastStartupPostRenderTasksAsync(MainWindow mainWindow)
        {
            try
            {
                await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ContextIdle);
                await Task.Delay(1000);

                WindowTopmostManager.Initialize(mainWindow, skipScan: true);
                LogHelper.WriteLogToFile("App | 快速启动模式的应用级延迟任务已开始");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"App | 快速启动模式延迟初始化失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private async Task RunDeferredStartupTasksAsync()
        {
            try
            {
                await Task.Delay(IsFastStartupEnabled ? 1200 : 400);

                try
                {
                    await IACoreDllExtractor.ExtractIACoreDllsAsync();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"释放IACore DLL时出错: {ex.Message}", LogHelper.LogType.Error);
                }

                try
                {
                    // LTSC（2026-11-12）：WinRT 后端已移除，形状识别只走 IACore IPC。
                    if (IpcIACoreClient.Instance.IsHelperExecutableAvailable)
                    {
                        LogHelper.WriteLogToFile("启动 IACore IPC 辅助进程");
                        bool ipcStarted = IpcIACoreClient.Instance.Start();
                        LogHelper.WriteLogToFile($"IACore IPC 辅助进程{(ipcStarted ? "启动成功" : "启动失败")}");
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"启动 IACore IPC 辅助进程时出错: {ex.Message}", LogHelper.LogType.Error);
                }

                try
                {
                    LogHelper.WriteLogToFile("开始注册.icstk文件关联");
                    FileAssociationManager.RegisterFileAssociation();
                    FileAssociationManager.ShowFileAssociationStatus();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"注册文件关联时出错: {ex.Message}", LogHelper.LogType.Error);
                }

                try
                {
                    LogHelper.WriteLogToFile("启动IPC监听器");
                    FileAssociationManager.StartIpcListener();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"启动IPC监听器时出错: {ex.Message}", LogHelper.LogType.Error);
                }

                try
                {
                    DeviceIdentifier.RecordAppLaunch();
                    var systemVersion = DeviceIdentifier.GetSystemVersion();
                    LogHelper.WriteLogToFile($"App | 系统版本: {systemVersion}");
                    LogHelper.WriteLogToFile($"App | 设备ID: {DeviceIdentifier.GetDeviceId()}");
                    LogHelper.WriteLogToFile($"App | 使用频率: {DeviceIdentifier.GetUsageFrequency()}");
                    LogHelper.WriteLogToFile($"App | 更新优先级: {DeviceIdentifier.GetUpdatePriority()}");
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"App | 初始化设备统计与遥测标签失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"启动阶段任务执行失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private static dynamic _cachedParsedSettings;
        /// <summary>
        /// 缓存 Settings.json 原始文本，供 LoadSettings 复用，避免启动阶段重复磁盘 I/O。
        /// </summary>
        internal static string CachedSettingsJson { get; private set; }

        /// <summary>
        /// 使用已规范化的设置内容更新启动缓存，确保同一启动流程中的后续设置加载不会再次使用旧配置。
        /// </summary>
        internal static void UpdateCachedSettingsJson(string json)
        {
            CachedSettingsJson = json;
            _cachedParsedSettings = JsonConvert.DeserializeObject(json);
        }

        /// <summary>
        /// 一次性读取并缓存 Settings.json 的解析结果，避免启动阶段重复 I/O + dynamic 反序列化。
        /// 同时缓存原始 JSON 文本供 LoadSettings 使用。
        /// </summary>
        private static dynamic ReadSettingsJsonOnce()
        {
            if (_cachedParsedSettings != null) return _cachedParsedSettings;
            try
            {
                var settingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Configs", "Settings.json");
                if (File.Exists(settingsPath))
                {
                    var json = File.ReadAllText(settingsPath);
                    UpdateCachedSettingsJson(json);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"读取 Settings.json 失败: {ex.Message}", LogHelper.LogType.Warning);
            }
            return _cachedParsedSettings;
        }

        private static bool IsFastStartupEnabledFromParsed(dynamic parsedSettings)
        {
            try
            {
                return parsedSettings?["startup"]?["enableFastStartup"] != null &&
                       (bool)parsedSettings["startup"]["enableFastStartup"];
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"检查快速启动设置失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        private void TryApplyPreferredLanguageFromParsedSettings(dynamic parsedSettings)
        {
            try
            {
                string preferredLanguage = parsedSettings?["appearance"]?["language"]?.ToString();
                if (!string.IsNullOrWhiteSpace(preferredLanguage))
                {
                    LocalizationHelper.TrySetCulture(preferredLanguage);
                    // TrySetCulture → CurrentCulture setter 内部已调用 SyncCommonResources()，无需重复调用
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"启动时预加载语言失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private static bool ShouldShowSplashScreenFromParsed(dynamic parsedSettings)
        {
            try
            {
                if (parsedSettings?["appearance"]?["enableSplashScreen"] != null)
                {
                    return (bool)parsedSettings["appearance"]["enableSplashScreen"];
                }
                return false;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"检查启动动画设置失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        private void ScrollViewer_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                if (SystemInformation.MouseWheelScrollLines == -1)
                    e.Handled = false;
                else
                    try
                    {
                        ScrollViewerEx SenderScrollViewer = (ScrollViewerEx)sender;
                        SenderScrollViewer.ScrollToVerticalOffset(SenderScrollViewer.VerticalOffset - e.Delta * 10 * SystemInformation.MouseWheelScrollLines / (double)120);
                        e.Handled = true;
                    }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        // 用于设置崩溃后操作类型
        public enum CrashActionType
        {
            SilentRestart,
            NoAction,
            ShowCrashWindow
        }

        /// <summary>
        /// 稳定运行多久才算「真启动成功」，允许清空熔断计数。
        /// 3 分钟：比任何崩溃循环的间隔都长，又不至于让正常用户等太久。
        /// </summary>
        private static readonly TimeSpan StableRunBeforeReset = TimeSpan.FromMinutes(3);

        /// <summary>
        /// LTSC P5 / C5·C7：延迟判断「这次启动算不算成功」。
        ///
        /// 不能像原来那样同步 Reset——那会让崩溃循环的计数永远停在 1
        /// （每次重启后窗口都能加载完，一加载就清零），熔断永远不触发。
        ///
        /// 判据分两段：
        ///   1) 启动那一刻先看「距上次重启多久」。如果不到 StableRunBeforeReset，
        ///      说明是刚崩完又起来了，这一轮不清零。
        ///   2) 再等 StableRunBeforeReset 后复查一次：如果这段时间内没有再次重启
        ///      （时间戳没被 Increment 更新过），才算真的跑稳了，清掉计数。
        /// 这样无论「起来就崩」还是「跑一会儿才崩」，计数都会累加，熔断能生效。
        /// </summary>
        private static async Task ResetStartupCountWhenStable()
        {
            try
            {
                int count = StartupCount.GetCount();
                if (count == 0) return; // 本来就没有计数，不用管

                var since = StartupCount.TimeSinceLastRestart();
                if (since.HasValue && since.Value < StableRunBeforeReset)
                {
                    // 刚重启完又走到这里 —— 崩溃循环进行中，保留计数。
                    LogHelper.WriteLogToFile(
                        $"启动后 {since.Value.TotalSeconds:F1} 秒内又发生了重启，保留熔断计数 {count}/5",
                        LogHelper.LogType.Warning);
                    await Task.Delay(StableRunBeforeReset);
                }

                // 等够时间后复查：期间有没有再次重启？
                var since2 = StartupCount.TimeSinceLastRestart();
                if (since2.HasValue && since2.Value < StableRunBeforeReset)
                {
                    LogHelper.WriteLogToFile(
                        $"稳定观察期内再次重启，保留熔断计数 {StartupCount.GetCount()}/5",
                        LogHelper.LogType.Warning);
                    return;
                }

                if (StartupCount.GetCount() > 0)
                {
                    LogHelper.WriteLogToFile(
                        $"稳定运行超过 {StableRunBeforeReset.TotalMinutes:F0} 分钟，清空熔断计数",
                        LogHelper.LogType.Info);
                    StartupCount.Reset();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"稳定启动判定失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 尝试通过熔断机制静默重启应用：先检查是否达到重启上限，未达则启动新进程并退出当前进程。
        /// 重启上限（5次）内启动新进程；达到上限时弹出提示、重置计数并以非零码退出。
        /// 重启前会通知看门狗退出（写入退出信号文件），避免看门狗二次触发导致双进程启动。
        /// </summary>
        /// <param name="restartReason">用于日志记录的重启原因描述。</param>
        private static void TryRestartWithBreaker(string restartReason)
        {
            StartupCount.Increment();
            int count = StartupCount.GetCount();
            LogHelper.WriteLogToFile($"熔断计数: {count}/5 — {restartReason}", LogHelper.LogType.Warning);

            if (count >= 5)
            {
                MessageBox.Show(
                    UpdateStrings.Msg_RestartLimit,
                    UpdateStrings.Msg_RestartLimitTitle,
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
                StartupCount.Reset();
                Environment.Exit(1);
                return;
            }

            try
            {
                // 通知看门狗退出，防止看门狗检测到进程退出后二次触发重启
                if (!string.IsNullOrEmpty(watchdogExitSignalFile))
                {
                    try { File.WriteAllText(watchdogExitSignalFile, "restart"); }
                    catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                }

                // 杀掉看门狗进程，避免竞态
                try
                {
                    if (watchdogProcess != null && !watchdogProcess.HasExited)
                    {
                        watchdogProcess.Kill();
                        watchdogProcess = null;
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

                string exePath = Process.GetCurrentProcess().MainModule.FileName;
                Process.Start(exePath);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"熔断重启启动新进程失败: {ex.Message}", LogHelper.LogType.Error);
            }

            Environment.Exit(1);
        }

        // 心跳相关
        private static DispatcherTimer heartbeatTimer;
        private static DateTime lastHeartbeat = DateTime.Now;
        private static Timer watchdogTimer;
        private static bool isStartupComplete = false;
        private static DateTime startupCompleteHeartbeat = DateTime.MinValue;
        private static DateTime splashScreenStartTime = DateTime.MinValue;
        private static DateTime appStartupStartTime = DateTime.MinValue;
        private static volatile bool isAppExiting = false;

        /// <summary>
        /// [调试用] 停止心跳计时器，模拟主线程无响应，下一次守护检查将触发心跳超时重启。
        /// </summary>
        internal static void DebugStopHeartbeat()
        {
            try
            {
                heartbeatTimer?.Stop();
                LogHelper.WriteLogToFile("[Debug] 心跳计时器已手动停止，等待守护检查检测超时", LogHelper.LogType.Warning);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[Debug] 停止心跳计时器失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 启动并管理应用的心跳与守护检查定时器，监测启动阶段与主线程是否无响应，并在符合配置的情况下尝试静默重启应用。
        /// </summary>
        /// <remarks>
        /// - 启动一个每秒更新心跳时间戳的调度定时器和一个每3秒运行的守护定时器。  
        /// - 守护定时器在首次运行的启动阶段若检测到超过两分钟未完成启动，会根据 CrashAction 配置尝试静默重启。  
        /// - 在启动完成后若检测到主线程超过10秒无响应，会根据 CrashAction 配置尝试静默重启。  
        /// - 对连续重启次数有保护：若重启计数达到或超过5次，会弹出提示并停止自动重启（重置重启计数并退出进程）。  
        /// - 在 OOBE（首次引导）展示期间不执行守护检查。  
        /// - 该方法会产生外部可观察的副作用：可能启动新进程并调用 Environment.Exit 终止当前进程，或显示消息框。
        /// - 重启前会等待1秒以确保旧进程资源已释放，避免多实例竞争条件。
        /// </remarks>
        private void StartHeartbeatMonitor()
        {
            UpdateCpuUsageSnapshot();

            heartbeatTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            heartbeatTimer.Tick += (_, __) =>
            {
                lastHeartbeat = DateTime.Now;
                UpdateCpuUsageSnapshot();
            };
            heartbeatTimer.Start();

            watchdogTimer = new Timer(_ =>
            {
                if (isAppExiting)
                    return;
                if (IsOobeShowing)
                    return;

                if (!isStartupComplete && appStartupStartTime != DateTime.MinValue)
                {
                    DateTime startTime = _isSplashScreenShown && splashScreenStartTime != DateTime.MinValue
                        ? splashScreenStartTime
                        : appStartupStartTime;
                    TimeSpan elapsedSinceStart = DateTime.Now - startTime;
                    if (elapsedSinceStart.TotalMinutes >= 2)
                    {
                        string timeType = _isSplashScreenShown ? "启动画面已显示" : "应用启动开始";
                        string restartReason = $"检测到启动假死：{timeType}{elapsedSinceStart.TotalMinutes:F2}分钟，但未收到启动完成心跳，自动重启。";
                        LogHelper.WriteLogToFile(restartReason, LogHelper.LogType.Error);
                        WriteCrashLog(restartReason);
                        SyncCrashActionFromSettings();
                        if (CrashAction == CrashActionType.SilentRestart)
                        {
                            TryRestartWithBreaker(restartReason);
                        }
                        return;
                    }
                }

                if (isStartupComplete)
                {
                    var now = DateTime.Now;
                    var sinceHeartbeat = now - lastHeartbeat;
                    var sinceStartupComplete = startupCompleteHeartbeat == DateTime.MinValue
                        ? TimeSpan.Zero
                        : now - startupCompleteHeartbeat;

                    if (sinceStartupComplete.TotalSeconds < 30)
                    {
                        return;
                    }

                    if (sinceHeartbeat.TotalSeconds > 10)
                    {
                        // UI 线程卡死与「普通崩溃」不是一回事，要分开处理。
                        //
                        // 普通崩溃：异常已经冒泡到 Dispatcher 之外，UI 线程还能再跑一次
                        //   ShowCrashWindow，所以弹窗档要弹窗、静默档要重启、不处理档只记录——
                        //   三档都成立，D-03 的映射是正确的。
                        //
                        // UI 线程卡死：线程本身已经不处理消息了。此时：
                        //   - 弹窗档的 ShowCrashWindow 排进消息队列也永远不会被执行；
                        //   - 不处理档等于让用户对着黑屏干等到自己关进程；
                        //   - 只有静默重启档本来就该做的事，恰好是对的。
                        // 也就是说，「按档位处置」在卡死场景下只有一档是有效动作。
                        //
                        // 所以卡死超过 ForceRestartAfterSeconds 秒后，不看档位一律强制重启。
                        // 这是「老师的白板必须能自己活过来」压过「尊重用户的崩溃处置偏好」。
                        // 阈值取得比 10 秒长，是因为短暂的 UI 抖动（拖大文件、开设置页）
                        // 也会让心跳慢一拍，那种情况不该直接重启。
                        SyncCrashActionFromSettings();
                        bool hardHang = sinceHeartbeat.TotalSeconds > ForceRestartAfterSeconds;
                        string restartReason = hardHang
                            ? $"检测到主线程卡死 {sinceHeartbeat.TotalSeconds:F1} 秒（超过 {ForceRestartAfterSeconds:F0} 秒阈值），强制重启。原处置方式={CrashAction}。"
                            : $"检测到主线程无响应，即将自动重启。心跳超时 {sinceHeartbeat.TotalSeconds:F1} 秒。";

                        LogHelper.NewLog(restartReason);
                        WriteCrashLog(restartReason);

                        // 卡死：无条件重启。未卡死但已超 10 秒：只有静默档重启。
                        if (hardHang || CrashAction == CrashActionType.SilentRestart)
                        {
                            TryRestartWithBreaker(restartReason);
                        }
                    }
                }
            }, null, 0, 3000);
        }

        // 看门狗进程
        public static void StartWatchdogIfNeeded()
        {
            // 避免递归启动
            if (Environment.GetCommandLineArgs().Contains("--watchdog")) return;
            // 启动看门狗进程
            string exePath = Process.GetCurrentProcess().MainModule.FileName;
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "--watchdog " + Process.GetCurrentProcess().Id + " \"" + watchdogExitSignalFile + "\"",
                CreateNoWindow = true,
                UseShellExecute = false,
                WindowStyle = ProcessWindowStyle.Hidden
            };
            watchdogProcess = Process.Start(psi);
        }

        /// <summary>
        /// 作为守护进程监视指定的主进程，并在主进程异常退出时根据配置执行重启或退出操作。
        /// </summary>
        /// <remarks>
        /// 该方法期望命令行参数格式为："--watchdog &lt;pid&gt; &lt;exitSignalFile&gt;"（args[1..3]）。
        /// - 每 2 秒检查一次指定的主进程是否仍在运行；同时检测退出信号文件，若存在则删除该文件并以代码 0 退出守护进程。  
        /// - 当主进程退出时，会同步崩溃处理设置（SyncCrashActionFromSettings）。若启用了 UIA 顶层访问（IsUIAccessTopMostEnabled），守护进程直接退出。  
        /// - 若崩溃动作为 SilentRestart，则增加启动计数并：当连续重启计数达到 5 次及以上时弹出错误对话框、重置计数并以代码 1 退出；否则启动新的主进程实例。  
        /// 方法对内部异常静默处理，并在完成后确保进程退出。
        /// </remarks>
        public static void RunWatchdogIfNeeded()
        {
            var args = Environment.GetCommandLineArgs();
            if (args.Length >= 4 && args[1] == "--watchdog")
            {
                int pid = int.Parse(args[2]);
                string exitSignalFile = args[3];
                try
                {
                    var proc = Process.GetProcessById(pid);
                    while (!proc.HasExited)
                    {
                        // 检查退出信号文件
                        if (File.Exists(exitSignalFile))
                        {
                            try { File.Delete(exitSignalFile); } catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                            Environment.Exit(0);
                        }
                        Thread.Sleep(2000);
                    }
                    // 主进程异常退出，自动重启前判断崩溃后操作
                    SyncCrashActionFromSettings(); // 同步设置

                    if (IsUIAccessTopMostEnabled)
                    {
                        Environment.Exit(0);
                    }

                    if (CrashAction == CrashActionType.SilentRestart)
                    {
                        TryRestartWithBreaker("看门狗检测到主进程异常退出");
                    }
                }
                catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                Environment.Exit(0);
            }
        }

        private void App_Exit(object sender, ExitEventArgs e)
        {
            isAppExiting = true;

            try { heartbeatTimer?.Stop(); } catch { }
            try { watchdogTimer?.Change(Timeout.Infinite, Timeout.Infinite); watchdogTimer?.Dispose(); } catch { }

            CleanupTerminationMonitoring();

            try
            {
                IpcIACoreClient.Instance.Dispose();
            }
            catch (Exception ex)
            {
                ExceptionHandler.HandleException(ex, "释放 IpcIACoreClient 失败", LogHelper.LogType.Warning);
            }

            try
            {
                if (mutex != null)
                {
                    mutex.ReleaseMutex();
                    mutex.Dispose();
                    mutex = null;
                }
            }
            catch { }

            // 仅在软件内主动退出时关闭看门狗，并写入退出信号
            try
            {
                // 记录应用退出状态
                string exitType = IsAppExitByUser ? "用户主动退出" : "应用程序退出";
                WriteCrashLog($"{exitType}，退出代码: {e.ApplicationExitCode}");

                // 停止性能监测并保存运行记录
                try
                {

                }
                catch (Exception perfEx)
                {
                    LogHelper.WriteLogToFile($"保存性能监测数据失败: {perfEx.Message}", LogHelper.LogType.Warning);
                }

                // 记录应用退出（设备标识符）
                try
                {
                    DeviceIdentifier.RecordAppExit();
                    LogHelper.WriteLogToFile($"App | 应用运行时长: {(DateTime.Now - appStartTime).TotalMinutes:F1}分钟");
                }
                catch (Exception deviceEx)
                {
                    LogHelper.WriteLogToFile($"记录设备标识符退出信息失败: {deviceEx.Message}", LogHelper.LogType.Error);
                }

                if (IsAppExitByUser)
                {
                    // 写入退出信号文件，通知看门狗正常退出
                    StartupCount.Reset();
                    File.WriteAllText(watchdogExitSignalFile, "exit");
                    if (watchdogProcess != null && !watchdogProcess.HasExited)
                    {
                        watchdogProcess.Kill();
                    }
                }
            }
            catch (Exception ex)
            {
                // 尝试记录最后的错误
                try
                {
                    LogHelper.WriteLogToFile($"退出处理时发生错误: {ex.Message}", LogHelper.LogType.Error);
                }
                catch (Exception innerEx) { System.Diagnostics.Debug.WriteLine(innerEx); }
            }
        }
    }
}
