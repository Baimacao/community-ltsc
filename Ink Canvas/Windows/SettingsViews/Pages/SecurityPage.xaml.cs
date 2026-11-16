using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;

using System.Reflection;
using System.IO;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class SecurityPage : Page
    {
        private bool _isLoaded = false;

        public SecurityPage()
        {
            InitializeComponent();
            Loaded += Page_Loaded;
            Unloaded += Page_Unloaded;
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void LoadSettings()
        {
            _isLoaded = false;
            try
            {
                var settings = SettingsManager.Settings;
                if (settings == null) return;
                if (settings.Security == null) settings.Security = new Security();

                var sec = settings.Security;
                CardPasswordEnabled.IsOn = sec.PasswordEnabled;
                CardRequirePasswordOnExit.IsOn = sec.RequirePasswordOnExit;
                CardRequirePasswordOnEnterSettings.IsOn = sec.RequirePasswordOnEnterSettings;
                CardRequirePasswordOnResetConfig.IsOn = sec.RequirePasswordOnResetConfig;
                CardRequirePasswordOnModifyOrClearNameList.IsOn = sec.RequirePasswordOnModifyOrClearNameList;
                // LTSC D-04: 进程保护已移除，设置项不再绑定 UI。

                UpdatePasswordUiState();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载安全页面设置时出错: {ex.Message}");
            }
            _isLoaded = true;
        }

        private void UpdatePasswordUiState()
        {
            var sec = SettingsManager.Settings?.Security;
            var passwordEnabled = sec != null && sec.PasswordEnabled;

            if (BtnSetOrChangePassword != null) BtnSetOrChangePassword.IsEnabled = passwordEnabled;

            CardRequirePasswordOnExit.IsEnabled = passwordEnabled;
            CardRequirePasswordOnEnterSettings.IsEnabled = passwordEnabled;
            CardRequirePasswordOnResetConfig.IsEnabled = passwordEnabled;
            CardRequirePasswordOnModifyOrClearNameList.IsEnabled = passwordEnabled;
        }

        private void SetCardIsOnSilently(Ink_Canvas.Controls.LabeledSettingsCard card, bool value)
        {
            var prev = _isLoaded;
            _isLoaded = false;
            try { card.IsOn = value; }
            finally { _isLoaded = prev; }
        }

        private async void ToggleSwitchPasswordEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var settings = SettingsManager.Settings;
            if (settings == null) return;
            if (settings.Security == null) settings.Security = new Security();
            var sec = settings.Security;

            bool newState = CardPasswordEnabled.IsOn;
            var owner = Window.GetWindow(this);

            if (newState)
            {
                var havePassword = SecurityManager.HasPasswordConfigured(settings);
                if (!havePassword)
                {
                    var pwd = await SecurityManager.PromptSetNewPasswordAsync(owner);
                    if (string.IsNullOrEmpty(pwd))
                    {
                        SetCardIsOnSilently(CardPasswordEnabled, false);
                        return;
                    }
                    SecurityManager.SetPassword(settings, pwd);
                }

                sec.PasswordEnabled = true;
                SettingsManager.SaveSettingsToFile();
                UpdatePasswordUiState();
            }
            else
            {
                if (SecurityManager.HasPasswordConfigured(settings))
                {
                    bool ok = await SecurityManager.PromptAndVerifyAsync(settings, owner,
                        SecurityStrings.Security_DisablePasswordTitle, SecurityStrings.Security_DisablePasswordMessage);
                    if (!ok)
                    {
                        SetCardIsOnSilently(CardPasswordEnabled, true);
                        return;
                    }
                }

                sec.PasswordEnabled = false;
                SecurityManager.ClearPassword(settings);
                SettingsManager.SaveSettingsToFile();
                UpdatePasswordUiState();
            }
        }

        private async void BtnSetOrChangePassword_Click(object sender, RoutedEventArgs e)
        {
            var settings = SettingsManager.Settings;
            if (settings == null) return;
            if (settings.Security == null) settings.Security = new Security();

            var owner = Window.GetWindow(this);
            var newPwd = await SecurityManager.PromptChangePasswordAsync(settings, owner);
            if (!string.IsNullOrEmpty(newPwd))
            {
                SecurityManager.SetPassword(settings, newPwd);
                settings.Security.PasswordEnabled = true;
                SettingsManager.SaveSettingsToFile();

                SetCardIsOnSilently(CardPasswordEnabled, true);
                UpdatePasswordUiState();
            }
        }

        private void ToggleSwitchRequirePasswordOnExit_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Security.RequirePasswordOnExit = CardRequirePasswordOnExit.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchRequirePasswordOnEnterSettings_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Security.RequirePasswordOnEnterSettings = CardRequirePasswordOnEnterSettings.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchRequirePasswordOnResetConfig_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Security.RequirePasswordOnResetConfig = CardRequirePasswordOnResetConfig.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchRequirePasswordOnModifyOrClearNameList_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Security.RequirePasswordOnModifyOrClearNameList = CardRequirePasswordOnModifyOrClearNameList.IsOn;
            SettingsManager.SaveSettingsToFile();
        }
    }
}
