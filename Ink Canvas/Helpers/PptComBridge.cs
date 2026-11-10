using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// PPT COM 取对象桥。三级降级，每级都能独立失败而不影响下一级：
    ///
    ///   1. ROT（Running Object Table）—— 按 PowerPoint 在 ROT 里注册的 <b>演示文稿文件路径</b> 找到它。
    ///      这是唯一能绕开权限隔离的办法：不要求调用方与 PowerPoint 同完整性级别。
    ///   2. GetActiveObject —— 正常环境最快，但要求同一 logon session 且不被 UIPI 阻挡。
    ///   3. 返回 null —— 由上层的进程扫描兜底（ROTPPTManager 那套），或者就当没连上。
    ///
    /// 关键实测事实（Office 16.0.20326，本机 2026-11-10）：
    ///   · ROT 里 PowerPoint 注册的 display name 是 **完整 .pptx 路径**，
    ///     形如 "E:\path\deck.pptx"，**不是** "PowerPoint.Application"。
    ///     所以按 ProgID 去 ROT 里找是找不到的。
    ///   · ROT 条目数在提权进程里会变成 0（ROT 按 logon session 隔离 + UIPI），
    ///     而在普通权限进程里正常。所以这个类拿不到东西 <b>不一定</b> 是环境问题，
    ///     也可能是调用方被提权了——<see cref="LastFailureReason"/> 会把这个说清楚。
    /// </summary>
    internal static class PptComBridge
    {
        /// <summary>上一次取对象失败的原因，用于把「为什么没连上」写进日志。</summary>
        public enum FailureReason
        {
            None = 0,
            /// <summary>ROT 和 GetActiveObject 都失败，但没拿到更具体的信息。</summary>
            Unknown,
            /// <summary>ROT 枚举成功但里面没有任何 PowerPoint 条目（多半是没开 PPT）。</summary>
            NoPresentationInRot,
            /// <summary>ROT 为空表且 GetActiveObject 也报 MK_E_UNAVAILABLE——几乎可以断定是权限隔离。</summary>
            LikelyElevationMismatch,
            /// <summary>Office COM 工厂本身起不来（CO_E_SERVER_EXEC_FAILURE），通常是首启未完成或安装损坏。</summary>
            ComServerExecFailed,
        }

        public static FailureReason LastFailureReason { get; private set; } = FailureReason.None;

        const int MK_E_UNAVAILABLE = unchecked((int)0x800401E3);
        const int CO_E_SERVER_EXEC_FAILURE = unchecked((int)0x80080005);
        const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
        const int RPC_E_SERVERFAULT = unchecked((int)0x80010105);

        [DllImport("ole32.dll")]
        static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);

        [DllImport("ole32.dll")]
        static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);

        [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);

        [DllImport("oleaut32.dll", PreserveSig = true)]
        static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        /// <summary>演示文稿扩展名。PowerPoint / WPS 都把放映文件以全路径注册进 ROT。</summary>
        static readonly string[] s_presentationExtensions =
        {
            ".pptx", ".ppt", ".ppsx", ".pps", ".pptm", ".ppsm", ".potx", ".pot", ".potm",
        };

        /// <summary>
        /// 按优先级取 PowerPoint Application。拿不到返回 null，原因见 <see cref="LastFailureReason"/>。
        /// 调用方负责 ReleaseComObject。
        /// </summary>
        public static object TryAcquireApplication()
        {
            LastFailureReason = FailureReason.Unknown;

            var fromRot = TryAcquireFromRot(out var rotFailedEmpty);
            if (fromRot != null)
            {
                LastFailureReason = FailureReason.None;
                return fromRot;
            }

            var fromActive = TryAcquireFromActiveObject(out var activeHr);
            if (fromActive != null)
            {
                LastFailureReason = FailureReason.None;
                return fromActive;
            }

            // 两级都失败，归类原因。
            if (activeHr == CO_E_SERVER_EXEC_FAILURE)
            {
                LastFailureReason = FailureReason.ComServerExecFailed;
            }
            else if (rotFailedEmpty && activeHr == MK_E_UNAVAILABLE)
            {
                // ROT 一条都没有 + GetActiveObject 说「没有可用对象」= 提权隔离的典型特征。
                LastFailureReason = FailureReason.LikelyElevationMismatch;
            }
            else if (rotFailedEmpty)
            {
                LastFailureReason = FailureReason.NoPresentationInRot;
            }
            return null;
        }

        /// <summary>
        /// 从 ROT 里按演示文稿扩展名找 PowerPoint。
        /// 找到的不是 Application 本身而是 Presentation，所以还要往上取 .Application。
        /// </summary>
        static object TryAcquireFromRot(out bool rotWasEmpty)
        {
            rotWasEmpty = false;
            IRunningObjectTable rot = null;
            IBindCtx bindCtx = null;
            IEnumMoniker enumMoniker = null;

            try
            {
                if (GetRunningObjectTable(0, out rot) != 0 || rot == null) return null;
                if (CreateBindCtx(0, out bindCtx) != 0 || bindCtx == null) return null;

                rot.EnumRunning(out enumMoniker);
                if (enumMoniker == null) return null;
                enumMoniker.Reset();

                var monikers = new IMoniker[1];
                IntPtr fetched = IntPtr.Zero;
                int seen = 0;

                while (enumMoniker.Next(1, monikers, fetched) == 0)
                {
                    seen++;
                    var moniker = monikers[0];
                    if (moniker == null) continue;

                    string displayName = null;
                    try { moniker.GetDisplayName(bindCtx, null, out displayName); }
                    catch { /* 单条取名字失败不影响继续枚举 */ }

                    if (string.IsNullOrEmpty(displayName)) continue;
                    if (!LooksLikePresentation(displayName)) continue;

                    object comObject = null;
                    try
                    {
                        rot.GetObject(moniker, out comObject);
                    }
                    catch
                    {
                        continue;
                    }

                    if (comObject == null) continue;

                    // ROT 里注册的通常是 Presentation（有完整路径），取其 .Application。
                    var app = TryGetApplicationFrom(comObject);
                    if (app != null) return app;

                    SafeRelease(comObject);
                }

                rotWasEmpty = seen == 0;
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                SafeRelease(enumMoniker);
                SafeRelease(bindCtx);
                SafeRelease(rot);
            }
        }

        /// <summary>
        /// 从 Presentation 或 Application 上取 Application。用 late-bound（dynamic）调用，
        /// 因为 WPS 混装时 ROT 拿到的 RCW 未必能转成 Microsoft.Office.Interop.PowerPoint.Application。
        /// </summary>
        static object TryGetApplicationFrom(object comObject)
        {
            try
            {
                dynamic d = comObject;

                // 先试直接当 Presentation：取 .Application
                try
                {
                    object app = d.Application;
                    if (app != null) return app;
                }
                catch { }

                // 再试它本身就是 Application：摸一下 .Presentations
                try
                {
                    var presentations = d.Presentations;
                    if (presentations != null) return comObject;
                }
                catch { }

                return null;
            }
            catch
            {
                return null;
            }
        }

        static object TryAcquireFromActiveObject(out int hr)
        {
            hr = 0;
            try
            {
                hr = CLSIDFromProgID("PowerPoint.Application", out Guid clsid);
                if (hr != 0) return null;

                hr = GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
                if (hr != 0 || obj == null) return null;

                // 摸一下 .Name 确认对象是活的（COM 死对象在这里就会抛）。
                dynamic probe = obj;
                var _ = probe.Name;
                return obj;
            }
            catch (COMException ex)
            {
                hr = ex.HResult;
                return null;
            }
            catch
            {
                return null;
            }
        }

        static bool LooksLikePresentation(string displayName)
        {
            foreach (var ext in s_presentationExtensions)
            {
                if (displayName.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>把 LastFailureReason 翻译成给人看的一句话，写日志用。</summary>
        public static string DescribeFailure()
        {
            switch (LastFailureReason)
            {
                case FailureReason.None:
                    return "已连接";
                case FailureReason.NoPresentationInRot:
                    return "ROT 中没有放映中的演示文稿（PowerPoint 可能未打开文件）";
                case FailureReason.LikelyElevationMismatch:
                    return "ROT 为空且 GetActiveObject 返回 MK_E_UNAVAILABLE——" +
                           "几乎可以断定是本程序与 PowerPoint 的权限级别不同（一方以管理员运行）。" +
                           "Windows 的 UIPI 会阻断跨完整性级别的 COM。";
                case FailureReason.ComServerExecFailed:
                    return "PowerPoint COM 服务器无法启动（CO_E_SERVER_EXEC_FAILURE）——" +
                           "通常是 Office 首次运行向导未完成，或安装损坏";
                default:
                    return "未知原因";
            }
        }

        static void SafeRelease(object comObject)
        {
            if (comObject == null) return;
            try
            {
                if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
            }
            catch { }
        }
    }
}
