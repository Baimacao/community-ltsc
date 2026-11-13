using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using Ink_Canvas.Properties;
using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Threading;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public class AvatarItem
    {
        public string AvatarPath { get; set; }
        public string Name { get; set; }
        public string Role { get; set; }
    }

    public partial class AboutPage : iNKORE.UI.WPF.Modern.Controls.Page
    {
        private DispatcherTimer _usageRefreshTimer;
        private long _savedTotalSeconds;
        // 显示用：历史累计总时长（从 usage_stats.enc 读出来的部分）
        // 实时增量另算：本次启动到现在，起点是 App.appStartTime，
        // 不是打开关于页的时刻——否则用户开着程序用两小时再点进来，
        // 这里会显示 0:00:00，和「总使用时长」这个标题对不上。

        public AboutPage()
        {
            InitializeComponent();
            Loaded += AboutPage_Loaded;
            Unloaded += AboutPage_Unloaded;
            InitializeAvatarData();
        }

        private void InitializeAvatarData()
        {
            var developers = new ObservableCollection<AvatarItem>
            {
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/DQYJUNIT.png", Name = "Baimacao", Role = LocalizationHelper.GetString("About_Dev_ICCLTSC") },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/Jingzai.png", Name = "鲸仔_JZ", Role = LocalizationHelper.GetString("About_Dev_AI") },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/CJKmkp.jpg", Name = "CJK_mkp", Role = LocalizationHelper.GetString("About_Dev_ICCCE") },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/dubi906w.jpg", Name = "Dubi906w", Role = LocalizationHelper.GetString("About_Dev_ICC") },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/ChangSakura.png", Name = "ChangSakura", Role = LocalizationHelper.GetString("About_Dev_ICA") },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/WXRIW.png", Name = "WXRIW", Role = LocalizationHelper.GetString("About_Dev_InkCanvas") },
            };

            var contributors = new ObservableCollection<AvatarItem>
            {
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/RaspberryKan.jpg", Name = "Raspberry Kan" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/kengwang.png", Name = "Kengwang" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/jiajiaxd.jpg", Name = "Charles Jia" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/clover-yan.png", Name = "clover_yan" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/NetheriteBowl.png", Name = "Netherite_Bowl" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/NotYoojun.png", Name = "Yoojun Zhou" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/yuwenhui2020.png", Name = "YuWenHui2020" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/STBBRD.png", Name = "ZongziTEK" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/aaaaaaccd.jpg", Name = "Aesthed" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/wwei.png", Name = "Wei" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/Alan-CRL.png", Name = "Alan-CRL" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/PrefacedCorg.jpg", Name = "PrefacedCorg" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/PANDA-JSR.jpg", Name = "PANDA-JSR" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/Super-Yyt.png", Name = "zhaishis(Super-Yyt)" },
                new AvatarItem { AvatarPath = "/Resources/DeveloperAvatars/CreeperAWA.png", Name = "CreeperAWA" }
            };

            DeveloperItemsControl.ItemsSource = developers;
            ContributorItemsControl.ItemsSource = contributors;
        }

        private void AboutPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();

            if (_usageRefreshTimer == null)
            {
                _usageRefreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                _usageRefreshTimer.Tick += UsageRefreshTimer_Tick;
            }
            _usageRefreshTimer.Start();
        }

        private void AboutPage_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_usageRefreshTimer != null)
            {
                _usageRefreshTimer.Stop();
                _usageRefreshTimer.Tick -= UsageRefreshTimer_Tick;
                _usageRefreshTimer = null;
            }
        }

        private void UsageRefreshTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                // 总使用时长 = 历史累计 + 本次启动至今。
                // 起点用 App.appStartTime（程序启动那一刻），
                // 不能用打开本页的时刻。
                var sinceLaunch = (long)(DateTime.Now - App.appStartTime).TotalSeconds;
                if (sinceLaunch < 0) sinceLaunch = 0;
                TotalUsageTextBlock.Text = DeviceIdentifier.FormatDuration(_savedTotalSeconds + sinceLaunch);
            }
            catch { }
        }

        private void LoadSettings()
        {
            try
            {
                var version = Assembly.GetExecutingAssembly().GetName().Version;
                AppVersionTextBlock.Text = version.Major + "." + version.Minor + "." + version.Build + "." + version.Revision;
                var informationalVersion = Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                if (informationalVersion != null)
                {
                    string infoVersion = informationalVersion.InformationalVersion;
                    int lastDotIndex = infoVersion.LastIndexOf('.');
                    if (lastDotIndex >= 0 && lastDotIndex < infoVersion.Length - 7)
                    {
                        AppVersionTextBlock.Text += " (" + infoVersion.Substring(lastDotIndex + 1) + ")";
                    }
                }
                RefreshDeviceInfo();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载关于页面设置时出错: {ex.Message}");
            }
        }

        private void RefreshDeviceInfo_Click(object sender, RoutedEventArgs e)
        {
            RefreshDeviceInfo();
        }

        private void RefreshDeviceInfo()
        {
            try
            {
                var (launchCount, totalSeconds, avgSessionSeconds, _) = DeviceIdentifier.GetUsageStats();
                _savedTotalSeconds = totalSeconds;
                LaunchCountTextBlock.Text = launchCount.ToString();
                // 立刻显示一次，别等计时器第一跳（否则会空一秒）
                var sinceLaunch = (long)(DateTime.Now - App.appStartTime).TotalSeconds;
                if (sinceLaunch < 0) sinceLaunch = 0;
                TotalUsageTextBlock.Text = DeviceIdentifier.FormatDuration(totalSeconds + sinceLaunch);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新设备信息失败: {ex.Message}", LogHelper.LogType.Error);
                LaunchCountTextBlock.Text = AboutStrings.DeviceInfo_Failed;
                TotalUsageTextBlock.Text = AboutStrings.DeviceInfo_Failed;
            }
        }
    
        private void ReRunOobe_Click(object sender, RoutedEventArgs e)
        {
            // 重新走一遍引导。OOBE 自己会从 Settings 读当前值，
            // 所以"重新引导"看到的就是现在的配置，改完直接存回去。
            try
            {
                var settings = SettingsManager.Settings;
                if (settings == null) return;

                var oobe = new OobeWindow(settings) { Owner = Application.Current.MainWindow };
                oobe.ShowDialog();

                // OOBE 内部已经写回 Settings，这里补一次落盘。
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }
}
}
