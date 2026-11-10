using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using System;
using System.Threading;
using System.Windows;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        // LTSC N-01：通知功能精简。
        // 保留下来的唯一展示路径是「朴素气泡」ShowLegacyNotification（纯 WPF，不联网、无队列、无提供商）。
        // 已整体移除：公告中心（联网拉取 + WebSocket）、通知提供商注册表、动态通知气泡系统、Windows Toast。
        // 公共契约 InkCanvas.PluginSdk/INotificationService 仍由静态 ShowNewMessage 承载，签名保持不变。

        private int lastNotificationShowTime;
        private int notificationShowTime = 2500;

        /// <summary>
        /// 插件公共契约 INotificationService 的实现入口（静态，可被任意位置调用）。
        /// 崩溃提示 App.xaml.cs 也走这里，因此除了记录日志外不得抛出。
        /// </summary>
        public static void ShowNewMessage(string notice, bool isShowImmediately = true)
        {
            if (string.IsNullOrEmpty(notice)) return;

            try
            {
                var mainWindow = ResolveMainWindow();
                if (mainWindow == null) return;

                if (!mainWindow.Dispatcher.CheckAccess())
                {
                    mainWindow.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        try { mainWindow.ShowNotification(notice, isShowImmediately); }
                        catch (Exception ex) { LogHelper.WriteLogToFile($"ShowNewMessage 异常: {ex.Message}", LogHelper.LogType.Warning); }
                    }));
                    return;
                }

                mainWindow.ShowNotification(notice, isShowImmediately);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"ShowNewMessage 异常: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 取当前主窗口。崩溃处置路径上 Application.Current.MainWindow 有可能尚未回填或被别的窗口占位，
        /// 因此再兜底扫一遍已打开的窗口，保证崩溃提示仍能找到宿主。
        /// </summary>
        private static MainWindow ResolveMainWindow()
        {
            var app = Application.Current;
            if (app == null) return null;

            if (app.MainWindow is MainWindow mainWindow) return mainWindow;

            foreach (Window window in app.Windows)
            {
                if (window is MainWindow candidate && candidate.IsLoaded) return candidate;
            }

            return app.MainWindow as MainWindow;
        }

        public void ShowNotification(string notice, bool isShowImmediately = true)
        {
            if (string.IsNullOrEmpty(notice)) return;

            if (IsNotificationSuppressedByDictationDoNotDisturb()) return;

            ShowLegacyNotification(notice, Math.Max(1, notificationShowTime / 1000));
        }

        /// <summary>
        /// 进入 PPT 放映/联动模式时的批注栏提示。原本经由动态通知队列展示，精简后走同一条朴素气泡路径。
        /// </summary>
        public void ShowPPTModePromptNotification()
        {
            if (Settings?.PowerPointSettings?.ShowPPTModePrompt != true) return;

            if (IsNotificationSuppressedByDictationDoNotDisturb()) return;

            ShowLegacyNotification(PPTStrings.PPT_ModePrompt_Title, 4);
        }

        private bool IsNotificationSuppressedByDictationDoNotDisturb()
        {
            var notification = Settings?.Notification;
            if (notification?.IsDictationDoNotDisturbEnabled != true) return false;

            if (notification.IsDictationDoNotDisturbInPPTEnabled && IsInPPTPresentationMode)
            {
                return true;
            }

            return notification.IsDictationDoNotDisturbInWhiteboardEnabled && currentMode == 1;
        }

        private void ShowLegacyNotification(string notice, int displaySeconds)
        {
            try
            {
                if (TextBlockNotice == null || GridNotifications == null) return;

                lastNotificationShowTime = Environment.TickCount;
                notificationShowTime = Math.Max(1, displaySeconds) * 1000;
                TextBlockNotice.Text = notice;
                AnimationsHelper.ShowWithSlideFromBottomAndFade(GridNotifications);

                new Thread(() =>
                {
                    Thread.Sleep(notificationShowTime + 300);
                    if (Environment.TickCount - lastNotificationShowTime >= notificationShowTime)
                    {
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            AnimationsHelper.HideWithSlideAndFade(GridNotifications);
                        });
                    }
                }).Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"ShowNotification 异常: {ex.Message}", LogHelper.LogType.Error);
            }
        }
    }
}
