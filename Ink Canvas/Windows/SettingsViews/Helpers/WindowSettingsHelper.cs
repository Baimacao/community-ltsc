using Ink_Canvas.Helpers;
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Windows;
using System.Windows.Threading;

namespace Ink_Canvas.Windows.SettingsViews.Helpers
{
    public static class WindowSettingsHelper
    {
        #region Timer Callbacks

        public static Action OnStopKillProcessTimer { get; set; }
        public static Action OnStartKillProcessTimer { get; set; }

        #endregion

        #region PPT Only Mode

        private static DispatcherTimer _pptOnlyVisibilityProbeTimer;
        private static Window _pptModeWindow;
        private const int PPTOnlyVisibilityProbeIntervalMs = 800;

        public static Action<bool> OnPPTOnlyModeChanged { get; set; }

        public static void ApplyPPTOnlyMode(Window window, bool isEnabled)
        {
            try
            {
                SettingsManager.Settings.ModeSettings.IsPPTOnlyMode = isEnabled;
                SettingsManager.SaveSettingsToFile();

                if (isEnabled)
                {
                    window.Hide();
                    LogHelper.WriteLogToFile("已切换到仅PPT模式，主窗口已隐藏", LogHelper.LogType.Event);
                    EnsurePPTOnlyVisibilityProbeTimer(window);
                }
                else
                {
                    StopPPTOnlyVisibilityProbeTimer();
                    window.Show();
                    LogHelper.WriteLogToFile("已切换到正常模式，主窗口已显示", LogHelper.LogType.Event);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"切换模式时出错: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private static void EnsurePPTOnlyVisibilityProbeTimer(Window window)
        {
            try
            {
                if (!SettingsManager.Settings.ModeSettings.IsPPTOnlyMode)
                {
                    StopPPTOnlyVisibilityProbeTimer();
                    return;
                }

                _pptModeWindow = window;

                if (_pptOnlyVisibilityProbeTimer == null)
                {
                    _pptOnlyVisibilityProbeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(PPTOnlyVisibilityProbeIntervalMs)
                    };
                    _pptOnlyVisibilityProbeTimer.Tick += PPTOnlyVisibilityProbeTimer_Tick;
                }

                if (!_pptOnlyVisibilityProbeTimer.IsEnabled)
                    _pptOnlyVisibilityProbeTimer.Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"仅PPT可见性探测计时器启动失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private static void StopPPTOnlyVisibilityProbeTimer()
        {
            try
            {
                _pptOnlyVisibilityProbeTimer?.Stop();
            }
            catch { }
        }

        private static void PPTOnlyVisibilityProbeTimer_Tick(object sender, EventArgs e)
        {
            OnPPTOnlyModeChanged?.Invoke(true);
        }

        #endregion

        #region Window Settings Methods

        public static bool IsTemporarilyDisablingNoFocusMode { get; set; }

        public static void ApplyNoFocusMode(Window window)
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
            int exStyle = NativeWindowHelper.GetWindowLong(hwnd, NativeWindowHelper.GWL_EXSTYLE);

            bool shouldBeNoFocus = !IsTemporarilyDisablingNoFocusMode && SettingsManager.Settings.Advanced.IsNoFocusMode;

            if (shouldBeNoFocus)
            {
                NativeWindowHelper.SetWindowLong(hwnd, NativeWindowHelper.GWL_EXSTYLE, exStyle | NativeWindowHelper.WS_EX_NOACTIVATE);
            }
            else
            {
                NativeWindowHelper.SetWindowLong(hwnd, NativeWindowHelper.GWL_EXSTYLE, exStyle & ~NativeWindowHelper.WS_EX_NOACTIVATE);
            }
        }

        public static void SetWindowMode(Window window)
        {
            if (SettingsManager.Settings.Advanced.WindowMode)
            {
                window.WindowState = WindowState.Normal;

                // LTSC D-11：铺满「工作区」而不是「整块屏幕」。
                //
                // PrimaryScreenWidth/Height 是物理屏幕尺寸，含任务栏；
                // 用它设窗口，底部会被任务栏压住（用户实测反馈：ICC 底边被挡住）。
                //
                // SystemParameters.WorkArea 已经扣掉任务栏和停靠的工具条。
                // 但它是 DIP 单位，而 Window.Left/Top/Width/Height 也是 DIP，
                // 所以直接可用；多屏时取窗口当前所在屏的工作区，避免主副屏混用。
                Rect workArea;
                if (!TryGetCurrentScreenWorkArea(window, out workArea))
                {
                    workArea = SystemParameters.WorkArea;
                }

                window.Left = workArea.Left;
                window.Top = workArea.Top;
                window.Width = workArea.Width;
                window.Height = workArea.Height;
            }
            else
            {
                window.WindowState = WindowState.Maximized;
            }
        }

        /// <summary>
        /// 取窗口当前所在那块屏幕的工作区（DIP）。
        /// 多屏且缩放不同时，主屏的 WorkArea 不适用于副屏，必须按窗口实际位置找。
        /// 拿不到就返回 false，调用方回退到主屏工作区。
        /// </summary>
        private static bool TryGetCurrentScreenWorkArea(Window window, out Rect workArea)
        {
            workArea = default(Rect);

            try
            {
                var hwnd = new System.Windows.Interop.WindowInteropHelper(window).Handle;
                if (hwnd == IntPtr.Zero) return false;

                IntPtr monitor = MonitorFromWindow(hwnd, MONITOR_DEFAULTTONEAREST);
                if (monitor == IntPtr.Zero) return false;

                var info = new MONITORINFO();
                info.cbSize = Marshal.SizeOf(typeof(MONITORINFO));
                if (!GetMonitorInfo(monitor, ref info)) return false;

                // rcWork / rcMonitor 都是物理像素，需要换算成 DIP
                double scale = 1.0;
                try
                {
                    var source = System.Windows.PresentationSource.FromVisual(window);
                    if (source?.CompositionTarget != null)
                    {
                        scale = source.CompositionTarget.TransformToDevice.M11;
                        if (scale <= 0) scale = 1.0;
                    }
                }
                catch { }

                RECT rc = info.rcWork;
                workArea = new Rect(
                    rc.Left / scale,
                    rc.Top / scale,
                    Math.Max(1, (rc.Right - rc.Left) / scale),
                    Math.Max(1, (rc.Bottom - rc.Top) / scale));
                return true;
            }
            catch
            {
                return false;
            }
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MONITORINFO
        {
            public int cbSize;
            public RECT rcMonitor;
            public RECT rcWork;
            public int dwFlags;
        }

        private const int MONITOR_DEFAULTTONEAREST = 0x00000002;

        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromWindow(IntPtr hwnd, int flags);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

        public static void ApplyAlwaysOnTop(Window window)
        {
            try
            {
                WindowTopmostManager.ApplyMainWindowTopmost(window, SettingsManager.Settings.Advanced.IsAlwaysOnTop);

                if (SettingsManager.Settings.Advanced.IsAlwaysOnTop &&
                    SettingsManager.Settings.Advanced.IsNoFocusMode &&
                    !SettingsManager.Settings.Advanced.EnableUIAccessTopMost)
                {
                    StartTopmostMaintenance(window);
                }
                else
                {
                    StopTopmostMaintenance();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用窗口置顶失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        public static void ApplyUIAccessTopMost(Window window)
        {
            try
            {
                if (SettingsManager.Settings.Advanced.EnableUIAccessTopMost && SettingsManager.Settings.Advanced.IsAlwaysOnTop)
                {
                    var identity = WindowsIdentity.GetCurrent();
                    var principal = new WindowsPrincipal(identity);

                    if (principal.IsInRole(WindowsBuiltInRole.Administrator))
                    {
                        try
                        {
                            // 已具有 UIAccess 时无需重启
                            if (UIAccessHelper.HasUIAccess())
                            {
                                LogHelper.WriteLogToFile("UIAccess | 当前进程已具有 UIAccess 权限");
                                App.IsUIAccessTopMostEnabled = true;
                                return;
                            }

                            OnStopKillProcessTimer?.Invoke();

                            if (App.watchdogProcess != null && !App.watchdogProcess.HasExited)
                            {
                                App.watchdogProcess.Kill();
                                App.watchdogProcess = null;
                            }

                            App.IsUIAccessTopMostEnabled = true;
                            App.IsAppExitByUser = true;
                            (Application.Current as App)?.ReleaseMutexForRestart();

                            bool useProcessToken = SettingsManager.Settings.Advanced.UIAMode == UIAMode.ProcessToken;
                            bool started;

                            if (useProcessToken)
                            {
                                started = UIAccessHelper.RestartAsNormalUserWithUIAccess_ProcessToken(sourcePid: (uint)Process.GetCurrentProcess().Id);
                            }
                            else
                            {
                                started = UIAccessHelper.RestartAsNormalUserWithUIAccess();
                            }
                            if (started)
                            {
                                Application.Current.Shutdown();
                            }
                            else
                            {
                                LogHelper.WriteLogToFile("UIAccess | 启动失败，回退到普通管理员模式", LogHelper.LogType.Warning);
                                App.IsUIAccessTopMostEnabled = false;
                                App.IsAppExitByUser = false;
                                App.StartWatchdogIfNeeded();
                                OnStartKillProcessTimer?.Invoke();
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"启用UIA置顶功能时出错: {ex.Message}", LogHelper.LogType.Error);
                        }
                    }
                    else if (UIAccessHelper.HasUIAccess())
                    {
                        LogHelper.WriteLogToFile("UIAccess | 当前普通用户进程已具有 UIAccess 权限");
                        App.IsUIAccessTopMostEnabled = true;
                    }
                    else
                    {
                        LogHelper.WriteLogToFile("UIA置顶功能需要管理员权限，正在申请管理员权限重启");
                        OnStopKillProcessTimer?.Invoke();

                        if (App.watchdogProcess != null && !App.watchdogProcess.HasExited)
                        {
                            App.watchdogProcess.Kill();
                            App.watchdogProcess = null;
                        }

                        AppRestartHelper.SwitchToUIATopMostAndRestart();
                    }
                }
                else
                {
                    LogHelper.WriteLogToFile("UIA置顶功能已禁用", LogHelper.LogType.Trace);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用UIA置顶功能时出错: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        public static void SetTopmostBasedOnSettings(Window window, bool shouldBeTopmost)
        {
            if (SettingsManager.Settings.Advanced.IsAlwaysOnTop)
            {
                WindowTopmostManager.ApplyMainWindowTopmost(window, true);
                ApplyAlwaysOnTop(window);
            }
            else
            {
                WindowTopmostManager.ApplyMainWindowTopmost(window, shouldBeTopmost);
                if (!shouldBeTopmost)
                {
                    ApplyAlwaysOnTop(window);
                }
            }
        }

        public static void PauseTopmostMaintenance()
        {
            WindowTopmostManager.PauseTopmostMaintenance();
        }

        public static void ResumeTopmostMaintenance(Window window)
        {
            if (SettingsManager.Settings.Advanced.IsAlwaysOnTop &&
                SettingsManager.Settings.Advanced.IsNoFocusMode &&
                !SettingsManager.Settings.Advanced.EnableUIAccessTopMost)
            {
                WindowTopmostManager.ResumeTopmostMaintenance(window);
            }
        }

        private static void StartTopmostMaintenance(Window window)
        {
            if (SettingsManager.Settings.Advanced.EnableUIAccessTopMost) return;

            WindowTopmostManager.StartTopmostMaintenance(window);
            LogHelper.WriteLogToFile("启动置顶维护定时器", LogHelper.LogType.Trace);
        }

        private static void StopTopmostMaintenance()
        {
            WindowTopmostManager.StopTopmostMaintenance();
            LogHelper.WriteLogToFile("停止置顶维护定时器", LogHelper.LogType.Trace);
        }

        #endregion
    }
}
