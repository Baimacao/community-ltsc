using System;
using System.IO;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// LTSC D-01: 自动更新功能已整体移除，这里只留两件仍然必要的事。
    ///
    /// 原实现 2792 行：dev-api.dy.ci 与 GitHub Releases 两个来源 → 多线路下载
    /// 安装包 → 校验 → 静默时段判断 → 关掉主程序 → 起 update-mode 子进程替换文件。
    /// 所有者裁决「建议去掉整个更新功能」。
    ///
    /// 为什么不把这个类型整个删掉：
    ///   1. 老版本升级上来的机器上可能还留着 --update-mode 启动参数
    ///      和 AutoUpdate/ 目录，需要有人认得并清理掉，否则启动会异常或残留垃圾。
    ///   2. 这两件事都不联网，是纯本地兼容处理，留着不会「偷偷更新」。
    ///
    /// 其余成员（检查更新、线路组、下载、安装、静默时段等）已全部删除 ——
    /// 它们此前是恒 false / null 的空实现，留着只会让人误以为还有更新流程。
    /// </summary>
    internal static class AutoUpdateHelper
    {
        /// <summary>
        /// 旧版残留的 --update-mode 参数：认得它、记一条日志、按正常启动继续。
        /// </summary>
        /// <returns>恒 false —— 本版本不存在「更新模式」这种启动状态。</returns>
        internal static bool HandleUpdateModeStartup(string[] args)
        {
            if (args == null) return false;

            foreach (var a in args)
            {
                if (string.Equals(a, "--update-mode", StringComparison.OrdinalIgnoreCase))
                {
                    LogHelper.WriteLogToFile(
                        "AutoUpdate | 检测到旧版更新模式参数（--update-mode），本版本已移除更新功能，按正常启动继续",
                        LogHelper.LogType.Warning);
                    break;
                }
            }
            return false;
        }

        /// <summary>
        /// 清理旧版留下的 AutoUpdate/ 目录。纯本地文件操作，不联网。
        /// </summary>
        internal static void DeleteUpdatesFolder()
        {
            string dir = null;
            try
            {
                dir = Path.Combine(App.RootPath ?? AppDomain.CurrentDomain.BaseDirectory, "AutoUpdate");
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                    LogHelper.WriteLogToFile("AutoUpdate | 已清理旧版更新残留目录");
                }
            }
            catch (Exception ex)
            {
                // 删不掉不是错误 —— 目录里可能有被占用的文件，下次启动还会再试
                LogHelper.WriteLogToFile($"AutoUpdate | 清理旧版更新残留目录失败（{dir}）: {ex.Message}",
                    LogHelper.LogType.Warning);
            }
        }
    }
}
