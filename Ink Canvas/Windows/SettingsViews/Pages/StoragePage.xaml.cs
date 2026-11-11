using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class StoragePage : Page
    {
        // 已知子目录定义。其余顶层文件归入“核心文件”或“其他”。
        private static readonly string[] LogDirs = { "Logs", "Crashes" };
        private static readonly string[] InkDirs = { "Saves" };
        private static readonly string[] BackupDirs = { "Backups" };
        private static readonly string[] CustomDirs = { "icons", "backgrounds" };
        private static readonly string[] PluginDirs = { "Plugins", "PluginPackages", "PluginConfigs", "PluginMarketCache", "PluginLogs" };
        private static readonly string[] UpdateDirs = { "AutoUpdate" };
        // 视为核心文件的目录（配置 / ppt-agent 联动组件 / 自动化工作流 / .NET 运行时等）
        private static readonly string[] ConfigDirs = { "Configs", "ppt-agent", "Automations", "runtimes" };

        // 视为核心文件的扩展名(位于应用根目录下)
        private static readonly string[] CoreFileExtensions =
        {
            ".exe", ".dll", ".json", ".enc", ".dat", ".config", ".pdb",
            ".xml", ".manifest", ".runtimeconfig.json", ".deps.json"
        };

        private bool _isLoaded = false;

        private long _coreSize, _logsSize, _inkSize, _backupsSize,
                     _customSize, _pluginsSize, _updateSize, _otherSize;

        public StoragePage()
        {
            InitializeComponent();
            Loaded += StoragePage_Loaded;
        }

        private async void StoragePage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadBackupSettings();
            await RefreshAsync();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            _ = RefreshAsync();
        }

        private void BtnOpenAppFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = App.RootPath,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"打开应用目录失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private async Task RefreshAsync()
        {
            SetAllSizesText(LocalizationHelper.GetString("Storage_Calculating"));
            try
            {
                await Task.Run(() => CalculateSizes());
                UpdateUI();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"计算存储占用失败: {ex.Message}", LogHelper.LogType.Error);
                SetAllSizesText(LocalizationHelper.GetString("Storage_CalculateFailed"));
            }
        }

        private void CalculateSizes()
        {
            _coreSize = _logsSize = _inkSize = _backupsSize =
                _customSize = _pluginsSize = _updateSize = _otherSize = 0;

            string root = App.RootPath;
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

            // 顶层目录归类
            foreach (var dir in SafeEnumerateDirectories(root))
            {
                string name = Path.GetFileName(dir);
                long size = GetDirectorySize(dir);

                if (LogDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _logsSize += size;
                else if (InkDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _inkSize += size;
                else if (BackupDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _backupsSize += size;
                else if (CustomDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _customSize += size;
                else if (PluginDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _pluginsSize += size;
                else if (UpdateDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _updateSize += size;
                else if (ConfigDirs.Any(n => string.Equals(n, name, StringComparison.OrdinalIgnoreCase)))
                    _coreSize += size;
                else
                    _otherSize += size;
            }

            // 顶层文件按扩展名归类
            foreach (var file in SafeEnumerateFiles(root))
            {
                long size = SafeFileLength(file);
                string ext = Path.GetExtension(file).ToLowerInvariant();
                if (CoreFileExtensions.Contains(ext))
                    _coreSize += size;
                else
                    _otherSize += size;
            }
        }

        private void UpdateUI()
        {
            long total = _coreSize + _logsSize + _inkSize + _backupsSize
                       + _customSize + _pluginsSize + _updateSize + _otherSize;

            TotalSizeTextBlock.Text = FormatSize(total);

            CoreSizeText.Text = FormatSize(_coreSize);
            LogsSizeText.Text = FormatSize(_logsSize);
            InkSizeText.Text = FormatSize(_inkSize);
            BackupsSizeText.Text = FormatSize(_backupsSize);
            CustomSizeText.Text = FormatSize(_customSize);
            PluginsSizeText.Text = FormatSize(_pluginsSize);
            OtherSizeText.Text = FormatSize(_otherSize);

            // 更新柱状图列宽
            if (total > 0)
            {
                BarCoreCol.Width = new GridLength(_coreSize, GridUnitType.Star);
                BarLogsCol.Width = new GridLength(_logsSize, GridUnitType.Star);
                BarInkCol.Width = new GridLength(_inkSize, GridUnitType.Star);
                BarBackupsCol.Width = new GridLength(_backupsSize, GridUnitType.Star);
                BarCustomCol.Width = new GridLength(_customSize, GridUnitType.Star);
                BarPluginsCol.Width = new GridLength(_pluginsSize, GridUnitType.Star);
                BarOtherCol.Width = new GridLength(_otherSize, GridUnitType.Star);
            }
            else
            {
                BarCoreCol.Width = BarLogsCol.Width = BarInkCol.Width =
                    BarBackupsCol.Width = BarCustomCol.Width =
                    BarPluginsCol.Width = BarOtherCol.Width =
                    new GridLength(0, GridUnitType.Star);
            }

            // 占磁盘比例
            try
            {
                var driveLetter = Path.GetPathRoot(App.RootPath);
                if (!string.IsNullOrEmpty(driveLetter))
                {
                    var drive = new DriveInfo(driveLetter);
                    if (drive.IsReady && drive.TotalSize > 0)
                    {
                        double pct = (double)total / drive.TotalSize * 100.0;
                        DiskPercentTextBlock.Text = pct < 0.01 ? "<0.01 %" : $"{pct:0.##} %";
                        return;
                    }
                }
            }
            catch { }
            DiskPercentTextBlock.Text = "—";
        }

        private void SetAllSizesText(string text)
        {
            TotalSizeTextBlock.Text = text;
            CoreSizeText.Text = text;
            LogsSizeText.Text = text;
            InkSizeText.Text = text;
            BackupsSizeText.Text = text;
            CustomSizeText.Text = text;
            PluginsSizeText.Text = text;
            OtherSizeText.Text = text;
        }

        #region 清理操作

        private void BtnCleanLogs_Click(object sender, RoutedEventArgs e)
        {
            CleanWithConfirm(LocalizationHelper.GetString("Storage_Logs_Header"), LogDirs, keepRoot: true);
        }

        private void BtnCleanInk_Click(object sender, RoutedEventArgs e)
        {
            CleanWithConfirm(LocalizationHelper.GetString("Storage_Ink_Header"), InkDirs, keepRoot: true);
        }

        private void BtnCleanBackups_Click(object sender, RoutedEventArgs e)
        {
            CleanWithConfirm(LocalizationHelper.GetString("Storage_Backups_Header"), BackupDirs, keepRoot: true);
        }


        private async void CleanWithConfirm(string displayName, string[] subDirs, bool keepRoot)
        {
            var result = MessageBox.Show(
                string.Format(LocalizationHelper.GetString("Storage_Confirm_Body"), displayName).Replace("\\n", "\n"),
                LocalizationHelper.GetString("Storage_Confirm_Title"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (result != MessageBoxResult.OK) return;

            var second = MessageBox.Show(
                string.Format(LocalizationHelper.GetString("Storage_Confirm_Second_Body"), displayName),
                LocalizationHelper.GetString("Storage_Confirm_Second_Title"),
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (second != MessageBoxResult.OK) return;

            try
            {
                await Task.Run(() =>
                {
                    foreach (var sub in subDirs)
                    {
                        string path = Path.Combine(App.RootPath, sub);
                        if (!Directory.Exists(path)) continue;

                        if (keepRoot)
                        {
                            foreach (var file in SafeEnumerateFiles(path, recursive: true))
                                TryDeleteFile(file);
                            foreach (var dir in SafeEnumerateDirectories(path).Reverse())
                                TryDeleteDirectory(dir);
                        }
                        else
                        {
                            TryDeleteDirectory(path);
                        }
                    }
                });
                LogHelper.WriteLogToFile($"已清理「{displayName}」分类");
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"清理「{displayName}」失败: {ex.Message}", LogHelper.LogType.Error);
                MessageBox.Show(
                    string.Format(LocalizationHelper.GetString("Storage_CleanFailed"), ex.Message),
                    LocalizationHelper.GetString("Storage_Confirm_Title"),
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }

            await RefreshAsync();
        }

        #endregion

        #region 工具方法

        private static long GetDirectorySize(string path)
        {
            long total = 0;
            try
            {
                foreach (var file in SafeEnumerateFiles(path, recursive: true))
                    total += SafeFileLength(file);
            }
            catch { }
            return total;
        }

        private static System.Collections.Generic.IEnumerable<string> SafeEnumerateDirectories(string path)
        {
            try { return Directory.EnumerateDirectories(path); }
            catch { return System.Linq.Enumerable.Empty<string>(); }
        }

        private static System.Collections.Generic.IEnumerable<string> SafeEnumerateFiles(string path, bool recursive = false)
        {
            try
            {
                return Directory.EnumerateFiles(path, "*",
                    recursive ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch { return System.Linq.Enumerable.Empty<string>(); }
        }

        private static long SafeFileLength(string path)
        {
            try { return new FileInfo(path).Length; }
            catch { return 0; }
        }

        private static void TryDeleteFile(string path)
        {
            try { File.Delete(path); } catch { }
        }

        private static void TryDeleteDirectory(string path)
        {
            try { Directory.Delete(path, true); } catch { }
        }

        private static string FormatSize(long bytes)
        {
            if (bytes <= 0) return "0 B";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
            int u = 0;
            while (size >= 1024 && u < units.Length - 1)
            {
                size /= 1024;
                u++;
            }
            return u == 0 ? $"{(long)size} {units[u]}" : $"{size:0.##} {units[u]}";
        }

        #endregion

private void LoadBackupSettings()
        {
            var settings = SettingsManager.Settings;
            if (settings?.Advanced == null) return;

            ToggleSwitchIsAutoBackupBeforeUpdate.IsOn = settings.Advanced.IsAutoBackupBeforeUpdate;
            ToggleSwitchIsAutoBackupEnabled.IsOn = settings.Advanced.IsAutoBackupEnabled;

            foreach (ComboBoxItem item in ComboBoxAutoBackupInterval.Items)
            {
                if (item.Tag != null && int.TryParse(item.Tag.ToString(), out int interval) && interval == settings.Advanced.AutoBackupIntervalDays)
                {
                    ComboBoxAutoBackupInterval.SelectedItem = item;
                    break;
                }
            }
        }

        private void ToggleSwitchIsAutoBackupBeforeUpdate_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsAutoBackupBeforeUpdate = ToggleSwitchIsAutoBackupBeforeUpdate.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchIsAutoBackupEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Advanced.IsAutoBackupEnabled = ToggleSwitchIsAutoBackupEnabled.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ComboBoxAutoBackupInterval_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboBoxAutoBackupInterval.SelectedItem is ComboBoxItem selectedItem && selectedItem.Tag != null)
            {
                if (int.TryParse(selectedItem.Tag.ToString(), out int interval))
                {
                    SettingsManager.Settings.Advanced.AutoBackupIntervalDays = interval;
                    SettingsManager.SaveSettingsToFile();
                }
            }
        }

        private void BtnManualBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                string backupDir = Path.Combine(App.RootPath, "Backups");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                    LogHelper.WriteLogToFile($"创建备份目录: {backupDir}");
                }

                string backupFileName = $"Settings_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.json";
                string backupPath = Path.Combine(backupDir, backupFileName);

                string settingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(SettingsManager.Settings, Newtonsoft.Json.Formatting.Indented);
                File.WriteAllText(backupPath, settingsJson);

                LogHelper.WriteLogToFile($"成功创建设置备份: {backupPath}");
                MessageBox.Show(string.Format(StorageStrings.Backup_SuccessMsg, backupPath), StorageStrings.Backup_SuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"创建设置备份时出错: {ex.Message}", LogHelper.LogType.Error);
                MessageBox.Show(string.Format(StorageStrings.Backup_CreateFailedMsg, ex.Message), StorageStrings.Backup_FailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnRestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            try
            {
                string backupDir = Path.Combine(App.RootPath, "Backups");
                if (!Directory.Exists(backupDir))
                {
                    Directory.CreateDirectory(backupDir);
                    LogHelper.WriteLogToFile($"创建备份目录: {backupDir}");
                    MessageBox.Show(StorageStrings.Restore_NoBackupFound, StorageStrings.Restore_FailedTitle, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dlg = new Microsoft.Win32.OpenFileDialog();
                dlg.InitialDirectory = backupDir;
                dlg.Filter = $"{StorageStrings.Restore_FilterLabel}|Settings_Backup_*.json|{StorageStrings.Restore_AllJsonFilter}|*.json";
                dlg.Title = StorageStrings.Restore_SelectFileTitle;

                if (dlg.ShowDialog() == true)
                {
                    string backupJson = File.ReadAllText(dlg.FileName);
                    Settings backupSettings = Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(backupJson);

                    if (backupSettings != null)
                    {
                        if (MessageBox.Show(StorageStrings.Restore_ConfirmMsg, StorageStrings.Restore_ConfirmTitle,
                                MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
                        {
                            string currentSettingsJson = Newtonsoft.Json.JsonConvert.SerializeObject(SettingsManager.Settings, Newtonsoft.Json.Formatting.Indented);
                            string tempBackupPath = Path.Combine(backupDir, $"Settings_Before_Restore_{DateTime.Now:yyyyMMdd_HHmmss}.json");
                            File.WriteAllText(tempBackupPath, currentSettingsJson);

                            SettingsManager.Settings = backupSettings;
                            SettingsManager.SaveSettingsToFile();

                            SettingsActionHub.OnReloadSettingsFromFile();

                            LogHelper.WriteLogToFile($"成功从备份还原设置: {dlg.FileName}");
                            MessageBox.Show(StorageStrings.Restore_SuccessMsg, StorageStrings.Restore_SuccessTitle, MessageBoxButton.OK, MessageBoxImage.Information);
                        }
                    }
                    else
                    {
                        MessageBox.Show(StorageStrings.Restore_ParseFailed, StorageStrings.Restore_FailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"还原设置备份时出错: {ex.Message}", LogHelper.LogType.Error);
                MessageBox.Show(string.Format(StorageStrings.Restore_FailedMsg, ex.Message), StorageStrings.Restore_FailedTitle, MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}