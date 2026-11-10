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
                        // NB.GV 会追加上 "+<sha>" 或 "+<sha>.<date>"，只取版本部分
                        string v = info.InformationalVersion;
                        int plus = v.IndexOf('+');
                        if (plus > 0) v = v.Substring(0, plus);
                        if (!string.IsNullOrWhiteSpace(v))
                        {
                            _hostVersion = v.Trim();
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

        static string _hostVersion;
    }
}
