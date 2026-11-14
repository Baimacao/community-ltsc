using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class StartupPage : iNKORE.UI.WPF.Modern.Controls.Page
    {
        private bool _isLoaded = false;

        public StartupPage()
        {
            InitializeComponent();
            Loaded += StartupPage_Loaded;
        }

        private void StartupPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
        }

        private void LoadSettings()
        {
            _isLoaded = false;

            try
            {
                var settings = SettingsManager.Settings;

                bool runAtStartup = AutoStartHelper.IsAutoStartEnabled("Ink Canvas Annotation");
                ToggleSwitchRunAtStartup.IsOn = runAtStartup;

                ToggleSwitchExternalProtocol.IsOn = settings.Advanced.IsEnableUriScheme;

                if (settings.Startup != null)
                {
                    int crashAction = settings.Startup.CrashAction;
                    if (crashAction < 0 || crashAction > 2) crashAction = 0;
                    ComboBoxCrashAction.SelectedIndex = crashAction;

                    ToggleSwitchFoldAtStartup.IsOn = settings.Startup.IsFoldAtStartup;

                }

                ToggleSwitchEnableTrayIcon.IsOn = settings.Appearance.EnableTrayIcon;
                ComboBoxTrayLeftClickAction.SelectedIndex = (int)settings.Appearance.TrayLeftClickAction;
                ComboBoxTrayRightClickAction.SelectedIndex = (int)settings.Appearance.TrayRightClickAction;

            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载启动设置时出错: {ex.Message}");
            }

            _isLoaded = true;
        }

        #region 启动设置事件处理

        private void ToggleSwitchRunAtStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                bool newState = ToggleSwitchRunAtStartup.IsOn;

                if (newState)
                {
                    AutoStartHelper.StartAutomaticallyDel("InkCanvas");
                    AutoStartHelper.StartAutomaticallyCreate("Ink Canvas Annotation");
                }
                else
                {
                    AutoStartHelper.StartAutomaticallyDel("InkCanvas");
                    AutoStartHelper.StartAutomaticallyDel("Ink Canvas Annotation");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置开机启动时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchFoldAtStartup_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                SettingsManager.Settings.Startup.IsFoldAtStartup = ToggleSwitchFoldAtStartup.IsOn;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置启动时收纳出错: {ex.Message}");
            }
        }

        private void ToggleSwitchExternalProtocol_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                bool newState = ToggleSwitchExternalProtocol.IsOn;
                bool success = false;

                if (newState)
                {
                    if (!UriSchemeHelper.IsUriSchemeRegistered())
                    {
                        success = UriSchemeHelper.RegisterUriScheme();
                    }
                    else
                    {
                        success = true;
                    }
                }
                else
                {
                    if (UriSchemeHelper.IsUriSchemeRegistered())
                    {
                        success = UriSchemeHelper.UnregisterUriScheme();
                    }
                    else
                    {
                        success = true;
                    }
                }

                if (success)
                {
                    SettingsManager.Settings.Advanced.IsEnableUriScheme = newState;
                    SettingsManager.SaveSettingsToFile();
                }
                else
                {
                    _isLoaded = false;
                    ToggleSwitchExternalProtocol.IsOn = !newState;
                    _isLoaded = true;

                    LogHelper.WriteLogToFile("设置外部协议失败，请检查权限或日志", LogHelper.LogType.Error);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置外部协议时出错: {ex.Message}");
            }
        }

        #endregion

        #region 崩溃后操作事件处理

        private void ComboBoxCrashAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                var item = ComboBoxCrashAction?.SelectedItem as ComboBoxItem;
                if (item == null) return;
                var tag = item.Tag?.ToString() ?? "0";
                switch (tag)
                {
                    case "0":
                        App.CrashAction = App.CrashActionType.SilentRestart;
                        SettingsManager.Settings.Startup.CrashAction = 0;
                        break;
                    case "1":
                        App.CrashAction = App.CrashActionType.NoAction;
                        SettingsManager.Settings.Startup.CrashAction = 1;
                        break;
                    case "2":
                        App.CrashAction = App.CrashActionType.ShowCrashWindow;
                        SettingsManager.Settings.Startup.CrashAction = 2;
                        break;
                }
                SettingsManager.SaveSettingsToFile();
                App.SyncCrashActionFromSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"设置崩溃操作时出错: {ex.Message}");
            }
        }

        #endregion

        #region 托盘图标

        private void ToggleSwitchEnableTrayIcon_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.EnableTrayIcon = ToggleSwitchEnableTrayIcon.IsOn;
            SettingsManager.SaveSettingsToFile();
            try
            {
                var _taskbar = Application.Current.Resources["TaskbarTrayIcon"];
                if (_taskbar is FrameworkElement fe)
                    fe.Visibility = ToggleSwitchEnableTrayIcon.IsOn ? Visibility.Visible : Visibility.Collapsed;
            }
            catch (Exception ex)
            {
                ExceptionHandler.HandleException(ex, "更新托盘图标可见性失败", LogHelper.LogType.Warning);
            }
        }

        private void ComboBoxTrayLeftClickAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.TrayLeftClickAction = (TrayClickAction)ComboBoxTrayLeftClickAction.SelectedIndex;
            SettingsManager.SaveSettingsToFile();
        }

        private void ComboBoxTrayRightClickAction_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Appearance.TrayRightClickAction = (TrayClickAction)ComboBoxTrayRightClickAction.SelectedIndex;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

    }
}
