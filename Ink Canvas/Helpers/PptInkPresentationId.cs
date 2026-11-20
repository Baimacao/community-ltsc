using System;
using System.IO;
using Microsoft.Office.Interop.PowerPoint;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 演示文稿的自动保存标识 —— 读写两侧唯一的事实来源。
    ///
    /// LTSC（2026-11-20）：这里原来有两套逻辑，格式不一样，导致
    /// 「存进去的笔迹重启后读不出来」：
    ///   读侧 PPTInkManager.GeneratePresentationId 永远生成三段：
    ///       {名字}_{页数}_{路径哈希}   → test1.pptx_53_39041F03
    ///   写侧 MW_PPT.EventHandlers.GetPresentationStrokeFolderPath
    ///   拿不到 FullName 时退化成两段：
    ///       {名字}_{页数}              → test1.pptx_53
    /// 名字对不上，读的时候就找不到文件夹，日志里「存在=False 已存页数=0」。
    ///
    /// 现在的方案：**ID 里不带页数**。
    ///   {名字}_{路径哈希}   → test1.pptx_39041F03
    ///
    /// 为什么不带页数：页数是会变的（老师加一页、删一页），
    /// 一带进 ID，改一次就全部读不出来。而路径哈希是稳定的
    /// （HashHelper 算的是路径字符串的 MD5，不是文件内容）。
    ///
    /// 页数不丢 —— 它单独存进文件夹里的 <see cref="SlideCountFileName"/>，
    /// 打开时拿来和当前页数比对，不一致就弹窗问老师（见 PptInkSlideCountPrompt）。
    /// </summary>
    internal static class PptInkPresentationId
    {
        /// <summary>页数标记文件的文件名（不含扩展名的部分）。</summary>
        public const string SlideCountFileName = "SlideCount";

        /// <summary>上一个放映位置的文件名。</summary>
        public const string PositionFileName = "Position";

        /// <summary>自动保存的根目录名。</summary>
        public const string AutoSaveFolderName = "Auto Saved - Presentations";

        /// <summary>
        /// 生成文件夹名。两段：<c>{名字}_{路径哈希}</c>。
        ///
        /// 名字和路径都可以为空（COM 偶发），那时退化到只剩哈希或 unknown，
        /// 但不能因为没有名字就返回空串 —— 空串会让 Path.Combine 指到根目录上。
        /// </summary>
        public static string Create(string presentationName, string presentationFullName)
        {
            string name = SanitizeName(presentationName);
            string hash;

            if (!string.IsNullOrWhiteSpace(presentationFullName))
            {
                // 算的是路径字符串的 MD5，不是文件内容 —— 文件改了哈希也不变。
                hash = HashHelper.GetFileHash(presentationFullName);
            }
            else
            {
                // 拿不到路径时不能用路径哈希之外的东西凑，
                // 否则每次打开都算出不同的名字，等于每次都读不到。
                // 用名字自己的哈希：同一份文件名字一般不变。
                hash = string.IsNullOrEmpty(name) ? "unknown" : HashHelper.GetFileHash(name);
            }

            if (string.IsNullOrEmpty(name))
                return hash;

            return $"{name}_{hash}";
        }

        /// <summary>按演示文稿对象生成文件夹名。</summary>
        public static string Create(Presentation presentation)
        {
            if (presentation == null) return "unknown";

            string name = null;
            string fullName = null;

            try { name = presentation.Name; } catch { }
            try { fullName = presentation.FullName; } catch { }

            return Create(name, fullName);
        }

        /// <summary>
        /// 拼出完整文件夹路径。
        /// </summary>
        public static string GetFolderPath(string autoSaveLocation, string presentationId)
        {
            if (string.IsNullOrEmpty(autoSaveLocation)) return string.Empty;
            if (string.IsNullOrEmpty(presentationId)) return string.Empty;
            return Path.Combine(autoSaveLocation, AutoSaveFolderName, presentationId);
        }

        /// <summary>
        /// 读文件夹里记的页数。没有标记文件时返回 0（未知）。
        /// </summary>
        public static int ReadSlideCount(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath)) return 0;
            try
            {
                string file = Path.Combine(folderPath, SlideCountFileName);
                if (!File.Exists(file)) return 0;
                string text = File.ReadAllText(file).Trim();
                return int.TryParse(text, out int count) && count > 0 ? count : 0;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"读取笔迹页数标记失败: {ex.Message}", LogHelper.LogType.Warning);
                return 0;
            }
        }

        /// <summary>
        /// 写页数标记。失败只记日志，不影响笔迹保存本身。
        /// </summary>
        public static void WriteSlideCount(string folderPath, int slideCount)
        {
            if (string.IsNullOrEmpty(folderPath) || slideCount <= 0) return;
            try
            {
                if (!Directory.Exists(folderPath)) Directory.CreateDirectory(folderPath);
                File.WriteAllText(Path.Combine(folderPath, SlideCountFileName), slideCount.ToString());
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"写入笔迹页数标记失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 文件夹里有没有笔迹（任何一页的 .icstk）。
        /// 判「要不要弹窗问老师」时用得着：没有笔迹就不用问。
        /// </summary>
        public static bool HasAnyStrokes(string folderPath, string strokeFileExtension)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath)) return false;
            try
            {
                return Directory.GetFiles(folderPath, "*" + strokeFileExtension).Length > 0;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"检查笔迹文件失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        /// <summary>
        /// 找老格式的文件夹（历史数据迁移用）。
        ///
        /// 老格式有两种，都要能找到：
        ///   两段：{名字}_{页数}
        ///   三段：{名字}_{页数}_{哈希}
        ///
        /// 只按「名字 + 下划线」前缀匹配，不同时命中多个就返回第一个；
        /// 命中多个时优先页数和当前一致的（那最可能是同一份）。
        /// </summary>
        public static string FindLegacyFolder(string autoSaveLocation, string presentationName, string presentationFullName, int currentSlideCount)
        {
            try
            {
                if (string.IsNullOrEmpty(autoSaveLocation) || string.IsNullOrEmpty(presentationName))
                    return null;

                string basePath = Path.Combine(autoSaveLocation, AutoSaveFolderName);
                if (!Directory.Exists(basePath)) return null;

                string prefix = presentationName + "_";
                string hash = string.IsNullOrWhiteSpace(presentationFullName)
                    ? HashHelper.GetFileHash(presentationName)
                    : HashHelper.GetFileHash(presentationFullName);

                string bestTwoSegment = null;
                string bestThreeSegment = null;

                foreach (string dir in Directory.GetDirectories(basePath))
                {
                    string folderName = Path.GetFileName(dir);
                    if (!folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;

                    // 去掉前缀，剩下的应该是「页数」或「页数_哈希」
                    string rest = folderName.Substring(prefix.Length);
                    string[] parts = rest.Split('_');

                    if (parts.Length == 1 && int.TryParse(parts[0], out int slides2))
                    {
                        // 两段格式 {名字}_{页数} —— 优先页数一致的
                        if (slides2 == currentSlideCount) return dir;
                        if (bestTwoSegment == null) bestTwoSegment = dir;
                    }
                    else if (parts.Length == 2 && int.TryParse(parts[0], out int slides3)
                             && string.Equals(parts[1], hash, StringComparison.OrdinalIgnoreCase))
                    {
                        // 三段格式且哈希对得上 —— 同一份文件，最可信
                        if (slides3 == currentSlideCount) return dir;
                        if (bestThreeSegment == null) bestThreeSegment = dir;
                    }
                }

                return bestThreeSegment ?? bestTwoSegment;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"查找旧格式笔迹文件夹失败: {ex.Message}", LogHelper.LogType.Warning);
                return null;
            }
        }

        /// <summary>
        /// 把老格式文件夹里的笔迹搬到新文件夹。
        ///
        /// 只在三个条件都满足时搬：
        ///   1. 老文件夹存在且里面有 .icstk
        ///   2. 新文件夹里还没有笔迹（不覆盖已有的）
        ///   3. 拿到的是合法的字节（大于 8 字节，和读侧判据一致）
        ///
        /// 搬完不删老文件夹 —— 万一新的判错了还能回去找。留着不占多少地方。
        /// </summary>
        public static int MigrateLegacyStrokes(string legacyFolder, string newFolder, string strokeFileExtension)
        {
            if (string.IsNullOrEmpty(legacyFolder) || string.IsNullOrEmpty(newFolder)) return 0;
            if (string.Equals(legacyFolder, newFolder, StringComparison.OrdinalIgnoreCase)) return 0;
            if (!Directory.Exists(legacyFolder)) return 0;

            // 新文件夹已经有笔迹就不搬，避免覆盖用户现有的东西
            if (HasAnyStrokes(newFolder, strokeFileExtension)) return 0;

            int migrated = 0;
            try
            {
                if (!Directory.Exists(newFolder)) Directory.CreateDirectory(newFolder);

                foreach (string file in Directory.GetFiles(legacyFolder, "*" + strokeFileExtension))
                {
                    try
                    {
                        string fileName = Path.GetFileName(file);
                        if (!int.TryParse(Path.GetFileNameWithoutExtension(fileName), out int slideIndex) || slideIndex <= 0)
                            continue;

                        byte[] bytes = File.ReadAllBytes(file);
                        if (bytes.Length <= 8) continue;   // 和读侧判据一致

                        File.WriteAllBytes(Path.Combine(newFolder, fileName), bytes);
                        migrated++;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"迁移第 {Path.GetFileName(file)} 页笔迹失败: {ex.Message}",
                            LogHelper.LogType.Warning);
                    }
                }

                // 顺手把 Position 也带过去（上次放映位置）
                try
                {
                    string legacyPosition = Path.Combine(legacyFolder, PositionFileName);
                    string newPosition = Path.Combine(newFolder, PositionFileName);
                    if (File.Exists(legacyPosition) && !File.Exists(newPosition))
                        File.Copy(legacyPosition, newPosition);
                }
                catch { }

                if (migrated > 0)
                {
                    LogHelper.WriteLogToFile(
                        $"[PPT笔迹] 已从旧文件夹迁移 {migrated} 页笔迹：" +
                        $"{Path.GetFileName(legacyFolder)} → {Path.GetFileName(newFolder)}",
                        LogHelper.LogType.Event);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"迁移旧笔迹失败: {ex.Message}", LogHelper.LogType.Warning);
            }

            return migrated;
        }

        /// <summary>
        /// 文件夹名里不能有路径分隔符和非法字符 —— 演示文稿名可能带这些。
        /// </summary>
        private static string SanitizeName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return string.Empty;

            var invalid = Path.GetInvalidFileNameChars();
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
            }

            return sb.ToString().Trim();
        }
    }
}
