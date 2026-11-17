using Ink_Canvas.WorkflowAutomation.Models;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Ink_Canvas.WorkflowAutomation.Rules
{
    /// <summary>
    /// 前台窗口进程名规则设置
    /// </summary>
    public class ForegroundWindowProcessRuleSettings
    {
        /// <summary>
        /// 要匹配的进程名称（不含.exe）
        /// </summary>
        public string ProcessName { get; set; } = "";

        /// <summary>
        /// 可选：要求的文件版本号前缀（例如 "5." 用来区分希沃白板 5 和 3）。
        /// 留空表示不检查版本。
        ///
        /// 为什么需要这个：希沃白板 5 / 3 / 3C 是同一个进程名 EasiNote，
        /// 只有版本号和产品名能区分。自动收纳如果只按进程名匹配，
        /// 老师勾了「白板 5」会把「白板 3」也一起触发 —— 课堂上就是误收纳。
        /// </summary>
        public string VersionPrefix { get; set; } = "";

        /// <summary>
        /// 可选：要求的产名包含串（例如 "3C"）。
        /// 同样是给同名不同代软件用的，留空表示不检查。
        /// </summary>
        public string ProductNameContains { get; set; } = "";
    }

    /// <summary>
    /// 判断前台窗口的进程名是否匹配指定名称的规则。
    ///
    /// LTSC N-01：在进程名之外增加了两个可选条件（版本号前缀、产品名包含串）。
    /// 起因是自动收纳的旧硬编码里，希沃白板 5 / 3 / 3C 共用一个进程名 EasiNote，
    /// 只能靠文件版本号和产品名区分。规则不认这两样，就没法把旧预设迁到新引擎。
    /// 两个字段默认空串，留空即跳过检查 —— 原有只填进程名的规则行为完全不变。
    /// </summary>
    public static class ForegroundWindowProcessRule
    {
        /// <summary>
        /// 版本信息缓存。同一个 exe 只读一次 —— 这个规则会被 1s 计时器反复求值，
        /// 每次都打开文件读版本资源的话，一节课就是上万次 IO。
        /// </summary>
        private static readonly System.Collections.Generic.Dictionary<string, FileVersionInfo> _versionCache
            = new System.Collections.Generic.Dictionary<string, FileVersionInfo>(StringComparer.OrdinalIgnoreCase);

        private static FileVersionInfo GetVersionInfoCached(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (_versionCache.TryGetValue(path, out var cached)) return cached;
            try
            {
                var info = FileVersionInfo.GetVersionInfo(path);
                _versionCache[path] = info;
                return info;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 统一匹配逻辑：进程名 → 版本前缀 → 产品名包含串。
        /// 三段都通过才算命中；后两段为空则跳过。
        /// </summary>
        private static bool Matches(ForegroundWindowProcessRuleSettings s, Process process)
        {
            if (s == null || process == null) return false;

            if (!string.Equals(process.ProcessName, s.ProcessName, StringComparison.OrdinalIgnoreCase))
                return false;

            // 没要求版本和产品名，进程名对上就够了 —— 绝大多数预设走这条路。
            bool needVersion = !string.IsNullOrEmpty(s.VersionPrefix);
            bool needProduct = !string.IsNullOrEmpty(s.ProductNameContains);
            if (!needVersion && !needProduct) return true;

            FileVersionInfo info;
            try
            {
                info = GetVersionInfoCached(process.MainModule?.FileName);
            }
            catch
            {
                // 读不到进程路径（权限不足、进程刚退出）时按「不匹配」处理：
                // 宁可这次不收纳，也不要在不该收纳的时候把工具栏收起来。
                return false;
            }

            if (info == null) return false;

            if (needVersion)
            {
                string ver = info.FileVersion ?? string.Empty;
                if (!ver.StartsWith(s.VersionPrefix, StringComparison.OrdinalIgnoreCase)) return false;
            }

            if (needProduct)
            {
                string prod = info.ProductName ?? string.Empty;
                if (prod.IndexOf(s.ProductNameContains, StringComparison.OrdinalIgnoreCase) < 0) return false;
            }

            return true;
        }

        public const string RuleId = "inkcanvas.foregroundwindowprocess";

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        public static bool Evaluate(object settings)
        {
            var s = settings as ForegroundWindowProcessRuleSettings;
            if (s == null || string.IsNullOrEmpty(s.ProcessName)) return false;
            try
            {
                var handle = GetForegroundWindow();
                if (handle == IntPtr.Zero) return false;
                uint pid;
                GetWindowThreadProcessId(handle, out pid);
                if (pid == 0) return false;
                var process = Process.GetProcessById((int)pid);
                return Matches(s, process);
            }
            catch (Win32Exception)
            {
                return false;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
