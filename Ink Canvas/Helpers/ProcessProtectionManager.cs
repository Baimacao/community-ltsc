using System;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// LTSC 1.7.19.7 / 决策 D-04：进程保护已移除。
    ///
    /// 原实现用 CreateFile(FILE_SHARE_READ) 与 FileStream(FileShare.Read) 长期锁住
    /// 程序目录下的所有子目录和 .exe/.dll/.config/.manifest/.dat/.enc 文件，
    /// 并在每次写文件前执行 GC.Collect(GC.MaxGeneration, Forced, true)
    /// 与 GC.WaitForPendingFinalizers() 强制释放句柄。
    ///
    /// 该机制在本项目的最高频写入路径上（LogHelper 每次写日志、SettingsManager 每次
    /// 保存配置都要过 WithWriteAccess），已在 1.7.19.7 中造成以下可观测行为：
    /// - 一次普通写入 = 一次全代强制 GC + 一次句柄表扫描；
    /// - 目录被长期占用，杀软扫描 / IT 替换文件 / 卸载残留都会被拒。
    ///
    /// LTSC 决策：整块移除，只保留同名同签名的直通 API，使 30 个调用点无需改动。
    /// 若将来需要恢复真实实现，git revert 本次 commit 即可。
    /// </summary>
    internal static class ProcessProtectionManager
    {
        /// <summary>
        /// 已停用。保留属性以兼容 <c>if (ProcessProtectionManager.Enabled)</c> 形式的调用。
        /// </summary>
        public static bool Enabled => false;

        /// <summary>
        /// 空操作。保留以兼容设置加载流程。
        /// </summary>
        public static void ApplyFromSettings()
        {
            // LTSC: 进程保护已移除，设置项不再有对应实现。
        }

        /// <summary>
        /// 空操作。保留以兼容设置页开关（该开关在 LTSC 中已隐藏）。
        /// </summary>
        public static void SetEnabled(bool enabled)
        {
            // LTSC: 进程保护已移除。
        }

        /// <summary>
        /// 空操作。原先用于立即释放指定路径的锁句柄；已无锁可释放。
        /// </summary>
        public static void ReleaseLocksForPath(string targetPath)
        {
            // LTSC: 进程保护已移除。
        }

        /// <summary>
        /// 直接执行写入委托，不再做加锁/解锁/强制 GC。
        /// 行为与旧实现在 <c>Enabled == false</c> 时完全一致。
        /// </summary>
        public static void WithWriteAccess(string targetPath, Action action)
        {
            if (action == null) return;
            action();
        }

        /// <summary>
        /// 空操作。保留以兼容 <c>App_Exit</c> 中的调用。
        /// </summary>
        public static void Shutdown()
        {
            // LTSC: 进程保护已移除。
        }
    }
}
