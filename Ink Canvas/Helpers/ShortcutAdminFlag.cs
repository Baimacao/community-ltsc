using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 读/改快捷方式（.lnk）里的「以管理员身份运行」标记。
    ///
    /// 为什么需要它：本程序的 app.manifest 写的是 asInvoker（默认不请求提权），
    /// 所以「以管理员运行」只可能来自两种地方：
    ///   1. 用户右键快捷方式 → 属性 → 兼容性 → 勾了「以管理员身份运行」
    ///   2. 兼容性疑难解答自动加的 AppCompatFlags\Layers 注册表项
    ///
    /// 这两种都会让 ICC 提权，而 PowerPoint 通常不提权 —— Windows 的 UIPI
    /// 直接切断跨完整性级别的 COM，PPT 联动整体失效（2026-11-18 班测）。
    ///
    /// 程序自己没法阻止被提权启动，但可以【告诉用户到底是谁把它提权的】，
    /// 并且把那个标记清掉。这比让老师自己去翻右键属性可靠得多。
    /// </summary>
    internal static class ShortcutAdminFlag
    {
        #region ShellLink COM

        [ComImport, Guid("00021401-0000-0000-C000-000000000046")]
        private class ShellLinkCoClass { }

        [ComImport, Guid("000214F9-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkW
        {
            void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile,
                         int cch, IntPtr pfd, uint fFlags);
            void GetIDList(out IntPtr ppidl);
            void SetIDList(IntPtr pidl);
            void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cch);
            void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cch);
            void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
            void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cch);
            void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
            void GetHotkey(out short pwHotkey);
            void SetHotkey(short wHotkey);
            void GetShowCmd(out int piShowCmd);
            void SetShowCmd(int iShowCmd);
            void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath,
                                 int cch, out int piIcon);
            void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
            void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
            void Resolve(IntPtr hwnd, uint fFlags);
            void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
        }

        [ComImport, Guid("0000010b-0000-0000-C000-000000000046"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPersistFile
        {
            void GetClassID(out Guid pClassID);
            [PreserveSig] int IsDirty();
            void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
            void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
            void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
            void GetCurFile([MarshalAs(UnmanagedType.LPWStr)] out string ppszFileName);
        }

        [ComImport, Guid("45e2b4ae-b1c3-11d0-b92f-00a0c90312e1"),
         InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IShellLinkDataList
        {
            void AddDataBlock(IntPtr pDataBlock);
            void CopyDataBlock(uint dwSig, out IntPtr ppDataBlock);
            void RemoveDataBlock(uint dwSig);
            void GetFlags(out uint dwFlags);
            void SetFlags(uint dwFlags);
        }

        // 快捷方式属性里的「以管理员身份运行」就是这个位。
        // 定义见 shellapi.h / 微软 SHLinkFlags 文档。
        private const uint SLDF_RUNAS_USER = 0x00002000;

        #endregion

        /// <summary>
        /// 读一个 .lnk 是否被勾了「以管理员身份运行」。
        /// 文件不存在、不是快捷方式、读不出来 → 一律返回 false（不阻塞主流程）。
        /// </summary>
        internal static bool IsRunAsAdmin(string shortcutPath)
        {
            object link = null;
            try
            {
                if (string.IsNullOrEmpty(shortcutPath) || !File.Exists(shortcutPath)) return false;

                link = new ShellLinkCoClass();
                ((IPersistFile)link).Load(shortcutPath, 0 /* STGM_READ */);

                var dataList = link as IShellLinkDataList;
                if (dataList == null) return false;

                dataList.GetFlags(out uint flags);
                return (flags & SLDF_RUNAS_USER) != 0;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(
                    $"读快捷方式管理员标记失败（{shortcutPath}）：{ex.Message}",
                    LogHelper.LogType.Warning);
                return false;
            }
            finally
            {
                ReleaseCom(link);
            }
        }

        /// <summary>
        /// 清掉 .lnk 里的「以管理员身份运行」标记，然后保存。
        /// 返回 true 表示确实改成了（原本有标记、现在没了）。
        /// </summary>
        internal static bool ClearRunAsAdmin(string shortcutPath)
        {
            object link = null;
            try
            {
                if (string.IsNullOrEmpty(shortcutPath) || !File.Exists(shortcutPath)) return false;

                link = new ShellLinkCoClass();
                ((IPersistFile)link).Load(shortcutPath, 0);

                var dataList = link as IShellLinkDataList;
                if (dataList == null) return false;

                dataList.GetFlags(out uint flags);
                if ((flags & SLDF_RUNAS_USER) == 0) return false; // 本来就没有，不算改动

                dataList.SetFlags(flags & ~SLDF_RUNAS_USER);
                ((IPersistFile)link).Save(shortcutPath, true);

                LogHelper.WriteLogToFile(
                    $"已清掉快捷方式的管理员标记：{shortcutPath}",
                    LogHelper.LogType.Event);
                return true;
            }
            catch (Exception ex)
            {
                // 改不动要留痕：用户在设置页点了「修复」，结果没修好，
                // 必须能在日志里看到是因为没权限还是文件被占用。
                LogHelper.WriteLogToFile(
                    $"清快捷方式管理员标记失败（{shortcutPath}）：{ex.Message}",
                    LogHelper.LogType.Error);
                return false;
            }
            finally
            {
                ReleaseCom(link);
            }
        }

        /// <summary>
        /// 找出「指向本程序、且勾了以管理员运行」的快捷方式。
        /// 扫桌面 + 开始菜单的当前用户和公共目录。找不到返回 null。
        /// </summary>
        internal static string FindRunAsAdminShortcutForSelf()
        {
            try
            {
                string exePath = System.Diagnostics.Process.GetCurrentProcess().MainModule?.FileName;
                if (string.IsNullOrEmpty(exePath)) return null;

                var folders = new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
                    Environment.GetFolderPath(Environment.SpecialFolder.Programs),
                    Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
                };

                foreach (var folder in folders)
                {
                    if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) continue;
                    foreach (var lnk in EnumerateShortcuts(folder))
                    {
                        if (!PointsToSelf(lnk, exePath)) continue;
                        if (IsRunAsAdmin(lnk)) return lnk;
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"查找管理员快捷方式失败：{ex.Message}", LogHelper.LogType.Warning);
            }
            return null;
        }

        /// <summary>递归找 .lnk，深度和数量都设上限——开始菜单下可能有几百个项。</summary>
        private static System.Collections.Generic.IEnumerable<string> EnumerateShortcuts(string root)
        {
            var result = new System.Collections.Generic.List<string>();
            try
            {
                var stack = new System.Collections.Generic.Stack<string>();
                stack.Push(root);
                int guard = 0;

                while (stack.Count > 0 && guard < 2000)
                {
                    guard++;
                    var dir = stack.Pop();
                    try
                    {
                        foreach (var f in Directory.GetFiles(dir, "*.lnk"))
                        {
                            result.Add(f);
                            if (result.Count >= 500) return result;
                        }
                        foreach (var d in Directory.GetDirectories(dir)) stack.Push(d);
                    }
                    catch { /* 单个目录读不动就跳过，不能因为一个目录失败整个流程就没了 */ }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"枚举快捷方式失败（{root}）：{ex.Message}", LogHelper.LogType.Warning);
            }
            return result;
        }

        private static bool PointsToSelf(string lnkPath, string exePath)
        {
            object link = null;
            try
            {
                link = new ShellLinkCoClass();
                ((IPersistFile)link).Load(lnkPath, 0);

                var sb = new StringBuilder(1024);
                ((IShellLinkW)link).GetPath(sb, sb.Capacity, IntPtr.Zero, 0);
                var target = sb.ToString();

                if (string.IsNullOrEmpty(target)) return false;
                // 去掉末尾分隔符再比，避免 "C:\\a\\b" 和带尾斜杠的判成不同。
                var targetFull = Path.GetFullPath(target).TrimEnd(Path.DirectorySeparatorChar);
                var selfFull = Path.GetFullPath(exePath).TrimEnd(Path.DirectorySeparatorChar);
                return string.Equals(targetFull, selfFull, StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
            finally { ReleaseCom(link); }
        }

        private static void ReleaseCom(object obj)
        {
            try
            {
                if (obj != null && Marshal.IsComObject(obj))
                    Marshal.ReleaseComObject(obj);
            }
            catch { }
        }
    }
}
