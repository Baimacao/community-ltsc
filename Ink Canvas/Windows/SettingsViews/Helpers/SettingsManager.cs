using Ink_Canvas.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using ProcessProtectionManager = Ink_Canvas.Helpers.ProcessProtectionManager;

namespace Ink_Canvas.Windows.SettingsViews.Helpers
{
    public static class SettingsManager
    {
        public static Settings Settings { get; set; } = new Settings();

        public static string SettingsFileName { get; } = Path.Combine("Configs", "Settings.json");

        public static void SaveSettingsToFile()
        {
            var text = JsonConvert.SerializeObject(Settings, Formatting.Indented);
            try
            {
                string configsDir = Path.Combine(App.RootPath, "Configs");
                if (!Directory.Exists(configsDir))
                {
                    ProcessProtectionManager.WithWriteAccess(configsDir, () => Directory.CreateDirectory(configsDir));
                }

                var path = App.RootPath + SettingsFileName;
                ProcessProtectionManager.WithWriteAccess(path, () => File.WriteAllText(path, text));

                // 写盘成功就必须同步更新启动缓存。
                //
                // 不更新的后果（2026-11-18 班测实证，OOBE 反复重弹）：
                // LoadSettings 读的是 App.CachedSettingsJson ?? 磁盘，
                // 而这份缓存是 App_Startup 里 ReadSettingsJsonOnce() 抓的一次快照。
                // 只要本次运行写过设置，缓存就和磁盘不一致了，之后任何一次
                // LoadSettings 都会把【新写的值】覆盖回【旧快照的值】。
                //
                // 实际链路：OnOobeCompleted 先把 HasShownOobe 写成 true 并保存，
                // 紧接着 LoadSettings(false) —— 读到的是启动时的旧快照（false），
                // 内存被打回 false，下次启动又弹 OOBE。日志里两次启动都是
                // HasShownOobe=False，而磁盘上明明是 true。
                App.UpdateCachedSettingsJson(text);
            }
            catch (Exception ex)
            {
                // LTSC（2026-11-17）：这里原来是 Debug.WriteLine，Release 下被编译掉。
                // 后果是「改设置写盘失败」完全无声——用户看到界面变了，重开却回滚，
                // 而且永远查不出原因。全项目 281 个地方调这个方法，一条静默等于 281 条。
                // 判据：这个错误发生时有没有人能知道？原来答不上，现在有日志。
                LogHelper.WriteLogToFile(
                    "保存设置到文件失败：" + ex, LogHelper.LogType.Error);
            }
        }

        public static void MigrateChickenSoupSettings()
        {
            if (Settings?.Appearance == null) return;

            var appearance = Settings.Appearance;
            if ((appearance.EnabledPresetTipsSources == null || appearance.EnabledPresetTipsSources.Count == 0)
                && appearance.ChickenSoupSource >= 0)
            {
                string presetId = null;
                switch (appearance.ChickenSoupSource)
                {
                    case 0: presetId = "osu"; break;
                    case 1: presetId = "mottos"; break;
                    case 2: presetId = "gaokao"; break;
                    case 4: presetId = "phigros"; break;
                }
                if (presetId != null)
                {
                    appearance.EnabledPresetTipsSources = new List<string> { presetId };
                }
            }
        }
    }
}
