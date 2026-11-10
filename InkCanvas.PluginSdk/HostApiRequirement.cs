using System;
using System.Reflection;

namespace Ink_Canvas.Plugins
{
    /// <summary>
    /// 宿主的 API 兼容性要求，由 <see cref="PluginManager"/> 在加载前对所有插件生效。
    /// </summary>
    public static class HostApiRequirement
    {
        /// <summary>
        /// 当前宿主支持的最大 API 版本。主版本相同即为兼容。
        /// </summary>
        public static readonly string CurrentApiVersion = "1.0.0";

        /// <summary>
        /// 当前宿主最小受支持的插件宿主版本（例如 "1.7.18"）。低于该版本的插件会被拒绝。
        /// </summary>
        /// <remarks>
        /// 这是<b>兼容性下限</b>，不是当前版本号。升级宿主时不需要跟着改，
        /// 只有当宿主真的放弃对老插件的兼容时才提升它。
        /// </remarks>
        public static readonly string MinSupportedHostVersion = "1.7.18";

        /// <summary>
        /// 当前宿主编译版本号。
        /// </summary>
        /// <remarks>
        /// <para>
        /// 从程序集读取，<b>不再硬编码</b>。之前这里写死成 "1.7.18.7"，
        /// 而 version.json 是 1.7.19.7——两者不同步，写进插件配置文件的宿主版本
        /// 会一直是旧值，排查问题时按那个值对不上实际构建。
        /// </para>
        /// <para>
        /// 取值顺序：InformationalVersion（Nerdbank.GitVersioning 写进去的完整版本）→
        /// AssemblyVersion → 兜底 "unknown"。绝不返回空串，调用方可以直接比较。
        /// </para>
        /// </remarks>
        public static string HostVersion
        {
            get
            {
                if (_hostVersion != null) return _hostVersion;

                try
                {
                    var asm = typeof(HostApiRequirement).Assembly;

                    var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>();
                    if (info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion))
                    {
                        // 只取纯数字的版本部分，丢掉构建元数据。
                        // 实测 NB.GV 3.9.50 产出的是 "1.7.19.7.fce51b1"（点号），
                        // 而老写法只按 '+' 截断，遇到点号就完全没生效，
                        // 于是 hostVersion 里一直带着 sha。这里两种都覆盖：
                        //   "1.7.19.7.fce51b1"  → "1.7.19.7"
                        //   "1.7.19.7+fce51b1"  → "1.7.19.7"
                        //   "1.7.19.7"          → "1.7.19.7"
                        string v = ExtractNumericVersion(info.InformationalVersion);
                        if (!string.IsNullOrWhiteSpace(v))
                        {
                            _hostVersion = v;
                            return _hostVersion;
                        }
                    }

                    var ver = asm.GetName().Version;
                    if (ver != null)
                    {
                        _hostVersion = ver.ToString();
                        return _hostVersion;
                    }
                }
                catch
                {
                    // 反射失败不该让插件加载流程崩掉，落到下面的兜底值
                }

                _hostVersion = "unknown";
                return _hostVersion;
            }
        }


        /// <summary>
        /// 从 InformationalVersion 里取出纯数字的版本段，遇到第一个非纯数字的段就停。
        /// 保证 "1.7.19.7.fce51b1" / "1.7.19.7+fce51b1" / "1.7.19.7" 都得到 "1.7.19.7"。
        /// 一个纯数字段都取不到时返回 null，交由调用方回退到 AssemblyVersion。
        /// </summary>
        static string ExtractNumericVersion(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;

            // 先砍掉 '+' 之后的构建元数据（SemVer 规范里的 build metadata）
            string s = raw.Trim();
            int plus = s.IndexOf('+');
            if (plus > 0) s = s.Substring(0, plus);

            // 兼容 "v1.7.19.7" 这种前缀写法（PluginCompatibility.NormalizeVersion 也认它）
            s = s.TrimStart('v', 'V');

            var parts = s.Split('.');
            var kept = new System.Collections.Generic.List<string>();
            foreach (var p in parts)
            {
                // 纯数字才算版本段；"-beta" 这种预发布标记也会在这里被截掉
                if (p.Length == 0 || !char.IsDigit(p[0])) break;
                bool allDigits = true;
                for (int i = 0; i < p.Length; i++)
                {
                    if (!char.IsDigit(p[i])) { allDigits = false; break; }
                }
                if (!allDigits) break;
                kept.Add(p);
            }

            return kept.Count > 0 ? string.Join(".", kept) : null;
        }

        static string _hostVersion;
    }
}
