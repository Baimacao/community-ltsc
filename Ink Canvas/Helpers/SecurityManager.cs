using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using iNKORE.UI.WPF.Controls;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;

namespace Ink_Canvas.Helpers
{
    internal static class SecurityManager
    {
        private const int Pbkdf2Iterations = 120_000;
        private const int SaltSizeBytes = 16;
        private const int HashSizeBytes = 32;

        /// <summary>
        /// 检查设置中是否启用了密码安全功能。
        /// </summary>
        /// <param name="settings">应用程序设置对象（可能为 null）。</param>
        /// <returns>`true` 当 settings 非 null 且其 Security 部分存在且已启用密码功能；`false` 否则。</returns>
        public static bool IsPasswordFeatureEnabled(Settings settings)
        => settings?.Security != null && settings.Security.PasswordEnabled;

        /// <summary>
        /// 确定给定设置中是否已配置密码（存在非空的密码盐和密码哈希）。
        /// </summary>
        /// <param name="settings">应用的设置；为 null 或未包含 Security 部分时视为未配置密码。</param>
        /// <returns>`true` 如果设置包含非空的 PasswordSalt 和 PasswordHash，否则 `false`。</returns>
        public static bool HasPasswordConfigured(Settings settings)
            => settings?.Security != null
                && !string.IsNullOrWhiteSpace(settings.Security.PasswordSalt)
                && !string.IsNullOrWhiteSpace(settings.Security.PasswordHash);

        public static bool IsSecurityFeatureEnabled(Settings settings)
            => IsPasswordFeatureEnabled(settings);

        public static bool IsSecurityConfigured(Settings settings)
            => HasPasswordConfigured(settings);

        /// <summary>
        /// 确定在退出应用时是否需要输入密码或 TOTP 验证码。
        /// </summary>
        public static bool IsPasswordRequiredForExit(Settings settings)
            => IsSecurityFeatureEnabled(settings) && IsSecurityConfigured(settings) && settings.Security.RequirePasswordOnExit;

        /// <summary>
        /// 确定在进入设置界面时是否需要输入密码或 TOTP 验证码。
        /// </summary>
        public static bool IsPasswordRequiredForEnterSettings(Settings settings)
            => IsSecurityFeatureEnabled(settings) && IsSecurityConfigured(settings) && settings.Security.RequirePasswordOnEnterSettings;

        /// <summary>
        /// 指示在重置配置时是否需要输入密码或 TOTP 验证码。
        /// </summary>
        public static bool IsPasswordRequiredForResetConfig(Settings settings)
            => IsSecurityFeatureEnabled(settings) && IsSecurityConfigured(settings) && settings.Security.RequirePasswordOnResetConfig;

        /// <summary>
        /// 指示在修改或清空点名名单前是否需要输入安全密码或 TOTP 验证码。
        /// </summary>
        public static bool IsPasswordRequiredForModifyOrClearNameList(Settings settings)
            => IsSecurityFeatureEnabled(settings)
               && IsSecurityConfigured(settings)
               && settings.Security.RequirePasswordOnModifyOrClearNameList;

        /// <summary>
        /// 将提供的明文密码与 Settings 中存储的密码散列进行比对以验证密码是否正确。
        /// </summary>
        /// <param name="settings">包含存储的密码盐和哈希的设置对象（使用 Base64 编码的 PasswordSalt 和 PasswordHash）。</param>
        /// <param name="password">要验证的明文密码。</param>
        /// <returns>`true` 如果密码与存储的哈希匹配，`false` 否则（包括未配置密码、password 为 null 或在解析/派生过程中发生错误）。</returns>
        public static bool VerifyPassword(Settings settings, string password)
        {
            if (!HasPasswordConfigured(settings)) return false;
            if (password == null) return false;

            try
            {
                var salt = Convert.FromBase64String(settings.Security.PasswordSalt);
                var expected = Convert.FromBase64String(settings.Security.PasswordHash);

                var actual = DeriveKey(password, salt, expected.Length);
                return FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 如果已配置密码，显示一个对话框提示用户输入密码并验证；如果未配置密码则直接允许通过。
        /// </summary>
        /// <returns>`true` 如果未配置密码或用户确认并输入了正确的密码，`false` 如果用户取消或验证失败。</returns>
        public static async Task<bool> PromptAndVerifyAsync(Settings settings, Window owner, string title, string message)
        {
            if (!HasPasswordConfigured(settings)) return true;

            var dialog = new ContentDialog
            {
                Title = title,
                PrimaryButtonText = CommonStrings.Common_OK,
                SecondaryButtonText = CommonStrings.Common_Cancel
            };

            var panel = new SimpleStackPanel
            {
                Spacing = 12,
                Margin = new Thickness(0, 10, 0, 0)
            };

            string finalMessage = message;

            var textBlock = new TextBlock
            {
                Text = finalMessage,
                TextWrapping = TextWrapping.Wrap
            };

            var passwordBox = new PasswordBox
            {
                Height = 32
            };

            panel.Children.Add(textBlock);
            panel.Children.Add(passwordBox);
            dialog.Content = panel;

            bool noFocusModeWasTemporarilyDisabled = false;
            if (owner != null && owner.IsVisible && settings?.Advanced?.IsNoFocusMode == true)
            {
                WindowSettingsHelper.IsTemporarilyDisablingNoFocusMode = true;
                WindowSettingsHelper.ApplyNoFocusMode(owner);
                noFocusModeWasTemporarilyDisabled = true;
            }

            try
            {
                dialog.Opened += (s, e) =>
                {
                    passwordBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        passwordBox.Focus();
                        Keyboard.Focus(passwordBox);
                    }), DispatcherPriority.Input);
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return false;

                return VerifyPassword(settings, passwordBox.Password);
            }
            finally
            {
                if (noFocusModeWasTemporarilyDisabled)
                {
                    WindowSettingsHelper.IsTemporarilyDisablingNoFocusMode = false;
                    WindowSettingsHelper.ApplyNoFocusMode(owner);
                }
            }
        }

        /// <summary>
        /// 显示一个对话框让用户输入并确认新密码，成功时返回该密码。
        /// </summary>
        /// <param name="owner">对话框的所属窗口（用于指定父窗口）。</param>
        /// <returns>用户输入的新密码；如果用户取消或输入无效（长度不足或两次不匹配），则返回 <c>null</c>。</returns>
        public static async Task<string> PromptSetNewPasswordAsync(Window owner)
        {
            var dialog = new ContentDialog
            {
                Title = MainWindowStrings.Main_Security_SetPasswordTitle,
                PrimaryButtonText = CommonStrings.Common_OK,
                SecondaryButtonText = CommonStrings.Common_Cancel
            };

            var panel = new SimpleStackPanel
            {
                Spacing = 12,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var tipText = new TextBlock
            {
                Text = MainWindowStrings.Main_Security_EnterNewPassword,
                TextWrapping = TextWrapping.Wrap
            };

            var newPwdBox = new PasswordBox { Height = 32, Margin = new Thickness(0, 4, 0, 0) };
            var confirmPwdBox = new PasswordBox { Height = 32, Margin = new Thickness(0, 4, 0, 0) };

            panel.Children.Add(tipText);
            panel.Children.Add(new TextBlock { Text = MainWindowStrings.Main_Security_NewPasswordLabel, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(newPwdBox);
            panel.Children.Add(new TextBlock { Text = MainWindowStrings.Main_Security_ConfirmNewPasswordLabel, Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(confirmPwdBox);
            dialog.Content = panel;

            bool noFocusModeWasTemporarilyDisabled = false;
            if (owner != null && owner.IsVisible)
            {
                var settings = MainWindow.Settings;
                if (settings?.Advanced?.IsNoFocusMode == true)
                {
                    WindowSettingsHelper.IsTemporarilyDisablingNoFocusMode = true;
                    WindowSettingsHelper.ApplyNoFocusMode(owner);
                    noFocusModeWasTemporarilyDisabled = true;
                }
            }

            try
            {
                dialog.Opened += (s, e) =>
                {
                    newPwdBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        newPwdBox.Focus();
                        Keyboard.Focus(newPwdBox);
                    }), DispatcherPriority.Input);
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return null;

                var pwd = newPwdBox.Password ?? "";
                var confirm = confirmPwdBox.Password ?? "";

                if (string.IsNullOrWhiteSpace(pwd) || pwd.Length < 4)
                {
                    MessageBox.Show(MainWindowStrings.Main_Security_PasswordTooShort, MainWindowStrings.Main_Security_Tip, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                if (!string.Equals(pwd, confirm, StringComparison.Ordinal))
                {
                    MessageBox.Show(MainWindowStrings.Main_Security_PasswordMismatch, MainWindowStrings.Main_Security_Tip, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                return pwd;
            }
            finally
            {
                if (noFocusModeWasTemporarilyDisabled)
                {
                    WindowSettingsHelper.IsTemporarilyDisablingNoFocusMode = false;
                    WindowSettingsHelper.ApplyNoFocusMode(owner);
                }
            }
        }

        /// <summary>
        /// 弹出对话框以更改已配置的安全密码；如果尚未配置密码则转而提示设置新密码。
        /// </summary>
        /// <param name="settings">应用配置对象，包含当前存储的密码信息。</param>
        /// <param name="owner">对话框的父窗口（用于定位/所有权）。</param>
        /// <returns>用户成功更改后返回新的密码字符串；当用户取消、验证失败或校验不通过时返回 <c>null</c>。</returns>
        public static async Task<string> PromptChangePasswordAsync(Settings settings, Window owner)
        {
            if (!HasPasswordConfigured(settings))
            {
                return await PromptSetNewPasswordAsync(owner);
            }

            var dialog = new ContentDialog
            {
                Title = MainWindowStrings.Main_Security_ChangePasswordTitle,
                PrimaryButtonText = CommonStrings.Common_OK,
                SecondaryButtonText = CommonStrings.Common_Cancel
            };

            var panel = new SimpleStackPanel
            {
                Spacing = 12,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var tipText = new TextBlock
            {
                Text = MainWindowStrings.Main_Security_ChangePasswordHint,
                TextWrapping = TextWrapping.Wrap
            };

            var currentBox = new PasswordBox { Height = 32, Margin = new Thickness(0, 4, 0, 0) };
            var newPwdBox = new PasswordBox { Height = 32, Margin = new Thickness(0, 4, 0, 0) };
            var confirmPwdBox = new PasswordBox { Height = 32, Margin = new Thickness(0, 4, 0, 0) };

            panel.Children.Add(tipText);
            panel.Children.Add(new TextBlock { Text = MainWindowStrings.Main_Security_CurrentPasswordLabel, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(currentBox);
            panel.Children.Add(new TextBlock { Text = MainWindowStrings.Main_Security_NewPasswordLabel, Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(newPwdBox);
            panel.Children.Add(new TextBlock { Text = MainWindowStrings.Main_Security_ConfirmNewPasswordLabel, Margin = new Thickness(0, 8, 0, 0) });
            panel.Children.Add(confirmPwdBox);
            dialog.Content = panel;

            bool noFocusModeWasTemporarilyDisabled = false;
            if (owner != null && owner.IsVisible && settings?.Advanced?.IsNoFocusMode == true)
            {
                WindowSettingsHelper.IsTemporarilyDisablingNoFocusMode = true;
                WindowSettingsHelper.ApplyNoFocusMode(owner);
                noFocusModeWasTemporarilyDisabled = true;
            }

            try
            {
                dialog.Opened += (s, e) =>
                {
                    currentBox.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        currentBox.Focus();
                        Keyboard.Focus(currentBox);
                    }), DispatcherPriority.Input);
                };

                var result = await dialog.ShowAsync();
                if (result != ContentDialogResult.Primary) return null;

                var current = currentBox.Password ?? "";
                var newPwd = newPwdBox.Password ?? "";
                var confirm = confirmPwdBox.Password ?? "";

                if (!VerifyPassword(settings, current))
                {
                    MessageBox.Show(MainWindowStrings.Main_Security_CurrentPasswordWrong, MainWindowStrings.Main_Security_Tip, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                if (string.IsNullOrWhiteSpace(newPwd) || newPwd.Length < 4)
                {
                    MessageBox.Show(MainWindowStrings.Main_Security_NewPasswordTooShort, MainWindowStrings.Main_Security_Tip, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                if (!string.Equals(newPwd, confirm, StringComparison.Ordinal))
                {
                    MessageBox.Show(MainWindowStrings.Main_Security_NewPasswordMismatch, MainWindowStrings.Main_Security_Tip, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                return newPwd;
            }
            finally
            {
                if (noFocusModeWasTemporarilyDisabled)
                {
                    WindowSettingsHelper.IsTemporarilyDisablingNoFocusMode = false;
                    WindowSettingsHelper.ApplyNoFocusMode(owner);
                }
            }
        }

        /// <summary>
        /// 为指定 Settings 生成并存储新的密码盐与哈希到 settings.Security 中。
        /// </summary>
        /// <param name="settings">要更新的设置对象；如果为 null 或其 Security 为 null 则不执行任何操作。</param>
        /// <param name="password">用于派生哈希的原始密码字符串。</param>
        public static void SetPassword(Settings settings, string password)
        {
            if (settings?.Security == null) return;

            var salt = new byte[SaltSizeBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }
            var hash = DeriveKey(password, salt, HashSizeBytes);

            settings.Security.PasswordSalt = Convert.ToBase64String(salt);
            settings.Security.PasswordHash = Convert.ToBase64String(hash);
        }

        /// <summary>
        /// 清除设置中存储的密码信息。
        /// </summary>
        /// <param name="settings">要更新的设置对象；将把其 Security.PasswordSalt 和 Security.PasswordHash 设为空字符串。若 <paramref name="settings"/> 为 null 或其 Security 为 null 则不执行任何操作。</param>
        public static void ClearPassword(Settings settings)
        {
            if (settings?.Security == null) return;
            settings.Security.PasswordSalt = "";
            settings.Security.PasswordHash = "";
        }

        /// <summary>
        /// 使用 PBKDF2（Rfc2898）从给定的密码和盐派生指定长度的密钥字节。
        /// </summary>
        /// <param name="password">用于派生的密码字符串。</param>
        /// <param name="salt">用于派生的盐字节数组（不可为 null）。</param>
        /// <param name="keyBytes">要返回的密钥字节长度（以字节为单位）。</param>
        /// <returns>派生出的密钥字节数组，长度等于 <paramref name="keyBytes"/>。</returns>
        private static byte[] DeriveKey(string password, byte[] salt, int keyBytes)
        {
            // LTSC（2026-11-18）：从过时的构造函数换成静态 Pbkdf2 方法。
            //
            // 【为什么必须显式传 SHA1】
            // 旧构造函数 Rfc2898DeriveBytes(pw, salt, iter) 用的是 HMACSHA1。
            // 如果顺手改成 SHA256，已有用户的密码会全部验证失败——
            // 同一密码同一盐，两种算法算出来的哈希完全不同。
            //
            // 这不是推理，是实测过的（.NET 11，密码 test123，固定盐，迭代 120000，取前 8 字节）：
            //   旧写法          B4-7B-60-FD-E9-CD-C9-B6
            //   新 API + SHA1   B4-7B-60-FD-E9-CD-C9-B6   一致
            //   新 API + SHA256 4F-87-E8-3F-D3-46-8C-04   不同
            //
            // 要升级到 SHA256 是另一个任务：需要密码迁移——用旧算法验证一次，
            // 通过后用新算法重新存。直接改这一行会把所有用户锁在外面。
            return Rfc2898DeriveBytes.Pbkdf2(
                password, salt, Pbkdf2Iterations,
                HashAlgorithmName.SHA1,
                keyBytes);
        }

        /// <summary>
        /// 以固定时间方式比较两个字节数组的内容是否完全相同，防止基于时序的比对攻击。
        /// </summary>
        /// <param name="a">要比较的第一个字节数组。</param>
        /// <param name="b">要比较的第二个字节数组。</param>
        /// <returns>`true` 如果两个数组长度相同且所有字节相等，`false` 否则。</returns>
        private static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null) return false;
            if (a.Length != b.Length) return false;
            var diff = 0;
            for (int i = 0; i < a.Length; i++)
            {
                diff |= a[i] ^ b[i];
            }
            return diff == 0;
        }
}
}