using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Diagnostics;
using System.Security.Principal;
using System.Windows;

namespace Ink_Canvas.Helpers
{
    public static class AppRestartHelper
    {
        public static bool IsRunningAsAdmin()
        {
            try
            {
                var identity = WindowsIdentity.GetCurrent();
                var principal = new WindowsPrincipal(identity);
                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
            catch
            {
                return false;
            }
        }

        public static void RestartApp(bool asAdmin)
        {
            try
            {
                App.IsAppExitByUser = true;

                (Application.Current as App)?.ReleaseMutexForRestart();

                string exePath = Process.GetCurrentProcess().MainModule.FileName;

                if (asAdmin)
                {
                    var psi = new ProcessStartInfo(exePath) { UseShellExecute = true, Verb = "runas" };
                    Process.Start(psi);
                }
                else
                {
                    // 当前已是管理员时，直接通过用户令牌降权启动，避免经由 explorer 中转的延迟
                    if (IsRunningAsAdmin())
                    {
                        if (UIAccessHelper.RestartAsNormalUser())
                        {
                            LogHelper.WriteLogToFile("重启 | 已用普通权限令牌启动新进程", LogHelper.LogType.Event);
                            Application.Current.Shutdown();
                            return;
                        }

                        // 令牌方案失败，退回 explorer 中转。
                        // 这条路径会丢掉命令行参数，但至少能起来一个普通权限的实例。
                        LogHelper.WriteLogToFile(
                            "重启 | 普通权限令牌启动失败，退回 explorer 中转（会丢掉命令行参数）",
                            LogHelper.LogType.Warning);
                    }

                    Process.Start("explorer.exe", "\"" + exePath + "\"");
                    LogHelper.WriteLogToFile($"重启 | 已通过 explorer 启动: {exePath}", LogHelper.LogType.Event);
                }

                Application.Current.Shutdown();
            }
            catch (Exception ex)
            {
                // 原来是 Debug.WriteLine —— 只在调试器里看得见。
                // 用户点了「以普通权限重启」却什么都没发生的现场，必须留痕。
                LogHelper.WriteLogToFile($"重启应用失败: {ex}", LogHelper.LogType.Error);

                // 失败时不能静默退出：复位标记，让当前进程继续跑，
                // 至少老师手上的白板还在，而不是点一下就没了。
                App.IsAppExitByUser = false;

                try
                {
                    MessageBox.Show(
                        "重启失败，程序会继续以当前权限运行。\n\n" +
                        "如果你想以普通权限启动，关掉程序后从桌面图标重新打开即可。",
                        "无法重启",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                }
                catch (Exception showEx)
                {
                    LogHelper.WriteLogToFile($"重启失败后提示用户也失败了: {showEx.Message}", LogHelper.LogType.Warning);
                }
            }
        }

        public static void RestartWithCurrentPrivileges()
        {
            RestartApp(IsRunningAsAdmin());
        }

        public static void RestartAsAdmin()
        {
            RestartApp(true);
        }

        public static void RestartAsNormal()
        {
            RestartApp(false);
        }

        public static void SwitchToUIATopMostAndRestart()
        {
            try
            {
                SettingsManager.Settings.Advanced.EnableUIAccessTopMost = true;

                if (!SettingsManager.Settings.Advanced.IsAlwaysOnTop)
                {
                    SettingsManager.Settings.Advanced.IsAlwaysOnTop = true;
                }

                SettingsManager.SaveSettingsToFile();

                App.IsUIAccessTopMostEnabled = true;
                App.IsAppExitByUser = true;
                (Application.Current as App)?.ReleaseMutexForRestart();

                bool started;
                bool useProcessToken = SettingsManager.Settings.Advanced.UIAMode == UIAMode.ProcessToken;

                if (IsRunningAsAdmin())
                {
                    if (useProcessToken)
                    {
                        started = UIAccessHelper.RestartAsNormalUserWithUIAccess_ProcessToken(sourcePid: (uint)Process.GetCurrentProcess().Id);
                    }
                    else
                    {
                        started = UIAccessHelper.RestartAsNormalUserWithUIAccess();
                    }
                }
                else
                {
                    string exePath = Process.GetCurrentProcess().MainModule.FileName;
                    ProcessStartInfo psi;

                    if (useProcessToken)
                    {
                        int currentPid = Process.GetCurrentProcess().Id;
                        psi = new ProcessStartInfo(exePath)
                        {
                            Arguments = $"--enable-uia-topmost-helper --uia-source-pid {currentPid}",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                        // 保持原进程短暂存活，确保提升的 helper 可以复制当前进程令牌。
                        System.Threading.Thread.Sleep(2000);
                    }
                    else
                    {
                        psi = new ProcessStartInfo(exePath)
                        {
                            Arguments = "--enable-uia-topmost-helper",
                            UseShellExecute = true,
                            Verb = "runas"
                        };
                        Process.Start(psi);
                    }
                    started = true;
                }

                if (started)
                {
                    Application.Current.Shutdown();
                }
                else
                {
                    App.IsAppExitByUser = false;
                    App.IsUIAccessTopMostEnabled = false;
                }
            }
            catch (Exception ex)
            {
                App.IsAppExitByUser = false;
                App.IsUIAccessTopMostEnabled = false;
                Debug.WriteLine($"切换到UIA置顶模式时出错: {ex.Message}");
            }
        }

        public static void SwitchToNormalTopMostAndRestart()
        {
            try
            {
                SettingsManager.Settings.Advanced.EnableUIAccessTopMost = false;
                SettingsManager.SaveSettingsToFile();

                App.IsUIAccessTopMostEnabled = false;
                RestartApp(false);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"切换到普通置顶模式时出错: {ex.Message}");
            }
        }
    }
}
