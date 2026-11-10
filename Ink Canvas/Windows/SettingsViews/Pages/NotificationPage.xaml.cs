using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    /// <summary>
    /// LTSC N-01：通知功能精简后的设置页。
    /// 公告中心、通知提供商管理、动态通知 / Windows Toast 开关（均依赖联网或注册表提供商）已移除，
    /// 本页只保留「勿扰」与「测试」两项——都不联网。
    /// </summary>
    public partial class NotificationPage : Page
    {
        private bool _isLoaded;

        public NotificationPage()
        {
            InitializeComponent();
            Loaded += NotificationPage_Loaded;
            Unloaded += NotificationPage_Unloaded;
        }

        private void NotificationPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void NotificationPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void LoadSettings()
        {
            _isLoaded = false;
            var notification = SettingsManager.Settings.Notification;

            ToggleSwitchDictationDoNotDisturb.IsOn = notification.IsDictationDoNotDisturbEnabled;
            CheckBoxDictationDoNotDisturbPPT.IsChecked = notification.IsDictationDoNotDisturbInPPTEnabled;
            CheckBoxDictationDoNotDisturbWhiteboard.IsChecked = notification.IsDictationDoNotDisturbInWhiteboardEnabled;
        }

        private void SaveSettings()
        {
            if (!_isLoaded) return;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchDictationDoNotDisturb_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Notification.IsDictationDoNotDisturbEnabled = ToggleSwitchDictationDoNotDisturb.IsOn;
            SaveSettings();
        }

        private void DictationDoNotDisturbMode_CheckChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Notification.IsDictationDoNotDisturbInPPTEnabled = CheckBoxDictationDoNotDisturbPPT.IsChecked == true;
            SettingsManager.Settings.Notification.IsDictationDoNotDisturbInWhiteboardEnabled = CheckBoxDictationDoNotDisturbWhiteboard.IsChecked == true;
            SaveSettings();
        }

        private void TestNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            // 走主窗口那条朴素气泡路径，不再经过已删除的通知队列。
            var mw = Application.Current?.MainWindow as MainWindow;
            if (mw != null) mw.ShowNotification(NotificationStrings.TestText);
        }
    }
}
