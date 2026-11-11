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
            LoadPrivacySettings();
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

        private bool _isChangingTelemetryInternally;
        private bool _isChangingTelemetryPrivacyInternally;

        private void LoadPrivacySettings()
        {
            _isLoaded = false;

            try
            {
                var settings = SettingsManager.Settings;
                if (settings?.Startup != null)
                {
                    int idx = 0;
                    switch (settings.Startup.TelemetryUploadLevel)
                    {
                        case TelemetryUploadLevel.None:
                            idx = 0;
                            break;
                        case TelemetryUploadLevel.Basic:
                            idx = 1;
                            break;
                        case TelemetryUploadLevel.Extended:
                            idx = 2;
                            break;
                        default:
                            idx = 0;
                            break;
                    }
                    ComboBoxTelemetryUploadLevel.SelectedIndex = idx;
                    CheckBoxTelemetryPrivacyAccepted.IsChecked = settings.Startup.HasAcceptedTelemetryPrivacy;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"加载隐私页面设置时出错: {ex.Message}");
            }

            _isLoaded = true;
        }

        private void ComboBoxTelemetryUploadLevel_SelectionChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            if (_isChangingTelemetryInternally) return;
            var oldLevel = SettingsManager.Settings.Startup.TelemetryUploadLevel;
            var item = ComboBoxTelemetryUploadLevel?.SelectedItem as ComboBoxItem;
            if (item == null) return;

            var tag = item.Tag?.ToString() ?? "0";
            var newLevel = TelemetryUploadLevel.None;
            switch (tag)
            {
                case "1":
                    newLevel = TelemetryUploadLevel.Basic;
                    break;
                case "2":
                    newLevel = TelemetryUploadLevel.Extended;
                    break;
                default:
                    newLevel = TelemetryUploadLevel.None;
                    break;
            }

            if (newLevel == TelemetryUploadLevel.None &&
                oldLevel != TelemetryUploadLevel.None &&
                SettingsManager.Settings.Startup.UpdateChannel != UpdateChannel.Release)
            {
                var result = MessageBox.Show(
                    AboutStrings.Privacy_ConfirmDisableTelemetry_Message,
                    AboutStrings.Privacy_ConfirmDisableTelemetry_Title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    _isChangingTelemetryInternally = true;
                    try
                    {
                        int idx = 0;
                        switch (oldLevel)
                        {
                            case TelemetryUploadLevel.Basic:
                                idx = 1;
                                break;
                            case TelemetryUploadLevel.Extended:
                                idx = 2;
                                break;
                            default:
                                idx = 0;
                                break;
                        }
                        ComboBoxTelemetryUploadLevel.SelectedIndex = idx;
                    }
                    finally
                    {
                        _isChangingTelemetryInternally = false;
                    }
                    return;
                }

                SettingsManager.Settings.Startup.UpdateChannel = UpdateChannel.Release;
                DeviceIdentifier.UpdateUsageChannel(UpdateChannel.Release);
            }

            if (newLevel != TelemetryUploadLevel.None && !SettingsManager.Settings.Startup.HasAcceptedTelemetryPrivacy)
            {
                MessageBox.Show(
                    AboutStrings.Privacy_PrivacyAgreementRequired_Message,
                    AboutStrings.Privacy_PrivacyAgreementRequired_Title,
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                _isChangingTelemetryInternally = true;
                try
                {
                    SettingsManager.Settings.Startup.TelemetryUploadLevel = TelemetryUploadLevel.None;
                    if (ComboBoxTelemetryUploadLevel != null)
                    {
                        ComboBoxTelemetryUploadLevel.SelectedIndex = 0;
                    }
                }
                finally
                {
                    _isChangingTelemetryInternally = false;
                }

                return;
            }

            SettingsManager.Settings.Startup.TelemetryUploadLevel = newLevel;
            SettingsManager.SaveSettingsToFile();
        }

        private void CheckBoxTelemetryPrivacyAccepted_Checked(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            if (_isChangingTelemetryPrivacyInternally) return;

            bool isChecked = CheckBoxTelemetryPrivacyAccepted.IsChecked == true;

            if (isChecked)
            {
                if (!PrivacyFileExists())
                {
                    MessageBox.Show(
                        AboutStrings.Privacy_PrivacyFileNotFound_Message,
                        AboutStrings.Privacy_PrivacyFileNotFound_Title,
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);

                    _isChangingTelemetryPrivacyInternally = true;
                    try
                    {
                        CheckBoxTelemetryPrivacyAccepted.IsChecked = false;
                    }
                    finally
                    {
                        _isChangingTelemetryPrivacyInternally = false;
                    }

                    SettingsManager.Settings.Startup.HasAcceptedTelemetryPrivacy = false;
                    SettingsManager.SaveSettingsToFile();
                    return;
                }

                var privacyWindow = new PrivacyAgreementWindow();
                bool? dialogResult = privacyWindow.ShowDialog();

                if (dialogResult == true && privacyWindow.UserAccepted)
                {
                    SettingsManager.Settings.Startup.HasAcceptedTelemetryPrivacy = true;
                    SettingsManager.SaveSettingsToFile();
                }
                else
                {
                    _isChangingTelemetryPrivacyInternally = true;
                    try
                    {
                        CheckBoxTelemetryPrivacyAccepted.IsChecked = false;
                    }
                    finally
                    {
                        _isChangingTelemetryPrivacyInternally = false;
                    }

                    SettingsManager.Settings.Startup.HasAcceptedTelemetryPrivacy = false;
                    SettingsManager.SaveSettingsToFile();
                }
            }
            else
            {
                var result = MessageBox.Show(
                    AboutStrings.Privacy_ConfirmRevokePrivacy_Message,
                    AboutStrings.Privacy_ConfirmRevokePrivacy_Title,
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning);

                if (result != MessageBoxResult.Yes)
                {
                    _isChangingTelemetryPrivacyInternally = true;
                    try
                    {
                        CheckBoxTelemetryPrivacyAccepted.IsChecked = true;
                    }
                    finally
                    {
                        _isChangingTelemetryPrivacyInternally = false;
                    }
                    return;
                }

                _isChangingTelemetryInternally = true;
                try
                {
                    SettingsManager.Settings.Startup.TelemetryUploadLevel = TelemetryUploadLevel.None;
                    if (ComboBoxTelemetryUploadLevel != null)
                    {
                        ComboBoxTelemetryUploadLevel.SelectedIndex = 0;
                    }
                }
                finally
                {
                    _isChangingTelemetryInternally = false;
                }

                if (SettingsManager.Settings.Startup.UpdateChannel != UpdateChannel.Release)
                {
                    SettingsManager.Settings.Startup.UpdateChannel = UpdateChannel.Release;
                    DeviceIdentifier.UpdateUsageChannel(UpdateChannel.Release);
                }

                SettingsManager.Settings.Startup.HasAcceptedTelemetryPrivacy = false;
                SettingsManager.SaveSettingsToFile();
            }
        }

        private static bool PrivacyFileExists()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "Ink_Canvas.privacy.txt";
                using (Stream stream = assembly.GetManifestResourceStream(resourceName))
                {
                    return stream != null;
                }
            }
            catch
            {
                return false;
            }
        }
    }
}
