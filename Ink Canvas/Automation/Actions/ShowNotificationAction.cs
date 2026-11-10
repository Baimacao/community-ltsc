using Ink_Canvas.Helpers;
using Ink_Canvas.WorkflowAutomation.Models;
using System;
using System.Windows;

namespace Ink_Canvas.WorkflowAutomation.Actions
{
    /// <summary>
    /// 显示通知行动的设置。
    /// LTSC N-01: 通知消息模型（NotificationMessageType 等）随动态通知系统一并移除，
    /// 这里只保留「操作要显示的一条文本」，用最朴素的应用内提示显示。
    /// </summary>
    public class ShowNotificationActionSettings
    {
        /// <summary>
        /// 通知内容
        /// </summary>
        public string Message { get; set; } = "";
    }

    /// <summary>
    /// 显示通知的行动注册。
    /// </summary>
    public static class ShowNotificationAction
    {
        public const string ActionId = "inkcanvas.shownotification";

        public static ActionRegistryInfo Register()
        {
            var info = new ActionRegistryInfo(ActionId, "显示通知", "Message")
            {
                SettingsType = typeof(ShowNotificationActionSettings)
            };

            info.Handle = (settings, guid) =>
            {
                var s = settings as ShowNotificationActionSettings;
                if (s == null || string.IsNullOrEmpty(s.Message)) return;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    ShowAutomationNotification(s);
                });
            };

            // 显示通知不支持恢复
            info.RevertHandle = null;

            return info;
        }

        /// <summary>
        /// LTSC N-01: 改为调用主窗口的极简提示（原 ShowNewMessage 路径），
        /// 不再经过已移除的 NotificationCenterService。
        /// </summary>
        public static void ShowAutomationNotification(ShowNotificationActionSettings settings)
        {
            if (settings == null || string.IsNullOrEmpty(settings.Message)) return;

            try
            {
                MainWindow.ShowNewMessage(settings.Message);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"自动化通知显示失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }
    }
}
