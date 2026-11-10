using System;
using System.IO;
using System.IO.Compression;

namespace Ink_Canvas.Helpers
{
    public static class SafeZipExtractor
    {
        /// <summary>
        /// 保证路径以目录分隔符结尾，用于前缀比较。
        /// 不补的话 "C:\Plugins" 会匹配上 "C:\Plugins-evil\x.dll"。
        /// </summary>
        static string EnsureTrailingSeparator(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            char last = path[path.Length - 1];
            if (last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar)
                return path;
            return path + Path.DirectorySeparatorChar;
        }

        /// <param name="zipFilePath">ZIP 文件路径</param>
        /// <param name="extractPath">解压目标目录</param>
        /// <param name="overwrite">是否覆盖已存在文件</param>
        public static void ExtractZipSafely(string zipFilePath, string extractPath, bool overwrite = true)
        {
            if (string.IsNullOrWhiteSpace(zipFilePath))
                throw new ArgumentNullException(nameof(zipFilePath));
            if (string.IsNullOrWhiteSpace(extractPath))
                throw new ArgumentNullException(nameof(extractPath));

            var fullExtractPath = Path.GetFullPath(extractPath);
            Directory.CreateDirectory(fullExtractPath);

            using (var zip = ZipFile.OpenRead(zipFilePath))
            {
                foreach (var entry in zip.Entries)
                {
                    // 跳过空条目
                    if (string.IsNullOrEmpty(entry.FullName))
                        continue;

                    // 防止绝对路径和盘符前缀
                    if (Path.IsPathRooted(entry.FullName))
                        continue;

                    // 统一路径分隔符
                    var normalized = entry.FullName.Replace('/', Path.DirectorySeparatorChar);

                    // 拒绝包含 .. 的路径，防止目录穿越
                    if (normalized.Contains(".." + Path.DirectorySeparatorChar) ||
                        normalized.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var destinationPath = Path.GetFullPath(
                        Path.Combine(fullExtractPath, normalized));

                    // 再次确认仍然在目标目录下。
                    // 必须先补分隔符再比：直接用 StartsWith(fullExtractPath) 时，
                    // "C:\Plugins" 也会匹配上 "C:\Plugins-evil\x.dll"，
                    // 目录穿越拦不住这种同名前缀的兄弟目录。
                    if (!destinationPath.StartsWith(EnsureTrailingSeparator(fullExtractPath),
                            StringComparison.OrdinalIgnoreCase))
                        continue;

                    // 目录条目
                    if (entry.FullName.EndsWith("/", StringComparison.Ordinal) ||
                        entry.FullName.EndsWith("\\", StringComparison.Ordinal))
                    {
                        Directory.CreateDirectory(destinationPath);
                        continue;
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destinationPath) ?? fullExtractPath);

                    if (!overwrite && File.Exists(destinationPath))
                        continue;

                    using (var input = entry.Open())
                    using (var output = File.Create(destinationPath))
                    {
                        input.CopyTo(output);
                    }
                }
            }
        }
    }
}


