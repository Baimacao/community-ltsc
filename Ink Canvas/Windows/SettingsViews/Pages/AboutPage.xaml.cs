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

        public AboutPage()
        {
            InitializeComponent();
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

        }



        private void LoadSettings()
        {
            try
            {
                // 统一走 App.GetDisplayVersion()：返回 "0.1a" 这种给人看的版本，
                // 不再自己拼四段数字（NBGV 会覆盖 AssemblyVersion，拼出来对不上）。
                AppVersionTextBlock.Text = App.GetDisplayVersion();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载关于页面设置时出错: {ex.Message}");
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

                // 和首次引导一样标上"OOBE 正在显示"。
                //
                // OnLanguageChanged 靠这个标志判断"现在是不是在引导里"：
                // 在那儿换语言意味着要清掉"已看过引导"的标记，重启后自动再弹一遍。
                // 不标的话，从设置页重跑引导时换语言、重启后就进不了引导了。
                var languageBeforeOobe = settings.Appearance?.Language;

                var oobe = new OobeWindow(settings) { Owner = Application.Current.MainWindow };
                try
                {
                    App.IsOobeShowing = true;
                    oobe.ShowDialog();
                }
                finally
                {
                    App.IsOobeShowing = false;
                }

                // OOBE 内部已经写回 Settings，这里补一次落盘。
                SettingsManager.SaveSettingsToFile();

                // 引导里换过语言但用户点了"稍后"，这里再问一次。
                _ = SettingsActionHub.PromptRestartIfLanguageChangedDuringOobeAsync(languageBeforeOobe);
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }
}
}
