using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 把出厂默认配置写到 Configs/ 下。
    ///
    /// 语义界定（这条最容易搞混）：
    /// - **出厂默认**：程序第一次跑，Configs/ 是空的，这时候装什么。
    /// - **用户配置**：用户改过之后的样子，升级时绝不能覆盖。
    ///
    /// 所以这里只补缺失的文件，已存在的一律跳过。
    /// 判断依据是文件在不在，不是版本号、不是时间戳 —— 用户可能手动改过，
    /// 任何"看起来该更新"的推断都可能把他的设置吃掉。
    ///
    /// 默认配置以嵌入资源形式放在程序里（csproj 里 LogicalName = DefaultConfigs.*），
    /// 不放磁盘。理由：用户看不到就不会去改，也不会在换包时被旧目录盖住。
    /// </summary>
    internal static class DefaultConfigInstaller
    {
        /// <summary>
        /// 嵌入资源名 → 相对 Configs/ 的目标路径。
        /// 新增默认配置时在这里加一行，同时去 csproj 里加 LogicalName。
        /// </summary>
        private static readonly Dictionary<string, string> Map =
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                { "DefaultConfigs.Settings.json", "Settings.json" },
                { "DefaultConfigs.HotkeyConfig.json", "HotkeyConfig.json" },
                { "DefaultConfigs.RecentTimers.json", "RecentTimers.json" },
                { "DefaultConfigs.ToolsMenuConfigs.board.json", "ToolsMenuConfigs/board.json" },
                { "DefaultConfigs.ToolsMenuConfigs.floatingbar.json", "ToolsMenuConfigs/floatingbar.json" },
                { "DefaultConfigs.ToolbarConfigs.default.json", "ToolbarConfigs/default.json" },
                { "DefaultConfigs.BoardToolbarConfigs.default.json", "BoardToolbarConfigs/default.json" },
            };

        /// <summary>
        /// 补装缺失的默认配置文件。返回实际写入的文件数。
        ///
        /// 单个文件失败不影响其他文件 —— 一个文件写不进去（比如被杀软锁了）
        /// 不该让整个程序启动失败。
        /// </summary>
        public static int EnsureDefaults()
        {
            int written = 0;
            var asm = Assembly.GetExecutingAssembly();
            string configsRoot = Path.Combine(App.RootPath, "Configs");

            foreach (var pair in Map)
            {
                string resourceName = pair.Key;
                string relativeTarget = pair.Value.Replace('/', Path.DirectorySeparatorChar);
                string targetPath = Path.Combine(configsRoot, relativeTarget);

                try
                {
                    // 已存在就跳过。这是"不覆盖用户配置"的唯一判据。
                    if (File.Exists(targetPath)) continue;

                    using (Stream stream = asm.GetManifestResourceStream(resourceName))
                    {
                        if (stream == null)
                        {
                            // 资源名对不上（改过 csproj 忘了改这里，或反过来）。
                            // 这种情况必须留痕，否则表现是"默认配置没生效"，
                            // 排查时看不到任何线索。
                            LogHelper.WriteLogToFile(
                                $"出厂默认配置缺失: 嵌入资源 {resourceName} 找不到（目标 {relativeTarget}）",
                                LogHelper.LogType.Warning);
                            continue;
                        }

                        string dir = Path.GetDirectoryName(targetPath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        using (var fs = new FileStream(targetPath, FileMode.CreateNew, FileAccess.Write))
                        {
                            stream.CopyTo(fs);
                        }

                        written++;
                        LogHelper.WriteLogToFile(
                            $"写出出厂默认配置: {relativeTarget}",
                            LogHelper.LogType.Event);
                    }
                }
                catch (IOException ioEx) when (ioEx is IOException && File.Exists(targetPath))
                {
                    // CreateNew 撞上并发创建：说明别人刚写好了，按"已存在"处理，不算错。
                    LogHelper.WriteLogToFile(
                        $"出厂默认配置 {relativeTarget} 已被其他路径创建，跳过",
                        LogHelper.LogType.Trace);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile(
                        $"写出出厂默认配置失败 [{relativeTarget}]: {ex.Message}",
                        LogHelper.LogType.Warning);
                }
            }

            return written;
        }
    }
}
