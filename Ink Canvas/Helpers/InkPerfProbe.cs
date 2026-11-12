using System;
using System.Diagnostics;
using System.Text;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// P2 笔迹性能与触摸诊断探针（只观测，不改行为）。
    /// </summary>
    /// <remarks>
    /// LTSC P2：用户反馈「刚启动稳 → 用久了卡 → 笔画多更卡 → 多指更卡 →
    /// 此时双指缩放失效」。这个组合有两种完全不同的成因：
    ///   甲：性能退化（GC 频繁 / Dispatcher 队列积压），触摸事件被丢
    ///   乙：触摸状态机被污染（dec 列表残留过期 ID），双指分支进不去
    /// 修法完全不同，必须先分清。所以这里只记数据，不动一行逻辑。
    ///
    /// 输出限流：每 5 秒一条汇总，前 3 条用 Event 级别（保证日志里看得见），
    /// 之后降到 Trace，避免把日志刷爆。
    /// </remarks>
    internal static class InkPerfProbe
    {
        static readonly Stopwatch _since = Stopwatch.StartNew();
        static long _lastFlushMs;

        internal static int TickCount;

        // 笔迹路径
        internal static int StrokeCount;
        internal static long StrokeTotalMs;
        internal static long StrokeMaxMs;

        /// <summary>
        /// LTSC P2（后期迭代）：多指卡顿现场快照。
        ///
        /// 背景：用户反馈「多指比单指卡，五指更卡，写着写着越来越卡」，但在班级
        /// 实机上「卡顿不明显」，无法复现。本地没有可复现压力，所以不能靠猜改触摸层。
        ///
        /// 做法：只在单笔耗时超过阈值时抓一次现场 —— 那一刻有几根手指、dec 里挂了
        /// 几个 ID、有没有僵尸残留、GC 刚好走过几轮。下次现场真卡了，日志直接给出
        /// 是「多指并发」还是「状态机残留」还是「GC 抖动」，不用再靠猜。
        ///
        /// 代价：只有超阈值才写一行，正常书写不产生任何额外 I/O。
        /// </summary>
        internal const long SlowStrokeThresholdMs = 120;  // 超过它就记一条现场
        internal static int SlowStrokeCount;

        /// <summary>抓现场时的外部状态提供者，由 MainWindow 注入（避免探针反向依赖触摸层）。</summary>
        internal static Func<string> ContextProvider;

        /// <summary>最近一次慢笔的现场快照（只在被启动时抓）。</summary>
        internal static string LastSlowContext;

        /// <summary>现场快照条数上限，防止长时间运行把日志刷爆。</summary>
        internal const int MaxSlowSnapshots = 40;

        // 触摸状态
        internal static int DecCountNow;
        internal static int DecCountMax;
        internal static int SilentReleaseCount;
        internal static int TouchUpFallback;

        /// <summary>
        /// P2 编号闸门：手势链路上每个 return 各占一个槽。
        /// 目的不是"看总数"，是看**增量**——哪个闸门在涨，哪一段就是拦路的。
        /// 双指失效排查专用，稳定后可整块删掉。
        /// </summary>
        internal static readonly int[] Gate = new int[12];

        internal static void Hit(int i)
        {
            if (i >= 0 && i < Gate.Length) Gate[i]++;
        }

        internal static readonly string[] GateName =
        {
            "0 ManipulationDelta进入",
            "1 冻结页拦",
            "2 橡皮点拦",
            "3 多点模式/总开关关",
            "4 到达判定行",
            "5 dec>=2",
            "6 Manipulators>=2",
            "7 PPT面板挡住",
            "8 PreviewTouchMove到达双指分支",
            "9 PreviewTouchDown捕获触摸",
            "10 手势已执行",
            "11 多指定时器投递"
        };
        internal static int StalePruned;

        // 双指
        internal static int TwoFingerMatched;
        internal static int TwoFingerMissed;

        // GC
        static int _gc0AtStart;
        static int _gc1AtStart;
        static int _gc2AtStart;

        internal static void TouchDown(int decCount, int touchId)
        {
            DecCountNow = decCount;
            if (decCount > DecCountMax) DecCountMax = decCount;
        }

        internal static void TouchUp(int decCount, int touchId, int releasedCaptures)
        {
            DecCountNow = decCount;
            // ReleaseAllTouchCaptures() 一次放掉全部捕获，但调用方只 Remove 了当前指的 ID。
            // 如果 dec 里还有别的 ID，那些手指再也收不到 TouchUp —— 残留就从这里来。
            if (releasedCaptures > 1 && decCount > 0) SilentReleaseCount++;
        }

        internal static void TwoFinger(bool matched)
        {
            if (matched) TwoFingerMatched++;
            else TwoFingerMissed++;
        }

        internal static void StrokeDone(long ms)
        {
            StrokeCount++;
            StrokeTotalMs += ms;
            if (ms > StrokeMaxMs) StrokeMaxMs = ms;

            // 只在明显卡顿时抓现场，正常书写零开销
            if (ms >= SlowStrokeThresholdMs)
            {
                SlowStrokeCount++;
                if (SlowStrokeCount <= MaxSlowSnapshots) CaptureSlowStroke(ms);
            }
        }

        /// <summary>
        /// 抓一次卡顿现场并立刻落盘。
        /// 不用等 5 秒汇总 —— 卡顿发生的那一刻状态才准，等 5 秒手指都抬完了。
        /// </summary>
        static void CaptureSlowStroke(long ms)
        {
            try
            {
                string ctx = null;
                try { ctx = ContextProvider?.Invoke(); }
                catch (Exception ex) { ctx = "上下文采集异常: " + ex.Message; }

                LastSlowContext = ctx;

                LogHelper.WriteLogToFile(
                    $"[P2卡顿] 第 {SlowStrokeCount} 次慢笔 {ms}ms | 笔序={StrokeCount} 均={(StrokeTotalMs / Math.Max(1, StrokeCount))}ms | {ctx}",
                    LogHelper.LogType.Warning);
            }
            catch
            {
                // 探针自身绝不能抛
            }
        }

        /// <summary>每 5 秒最多打一条汇总。异常自吞并记 Error，不让探针本身拖垮程序。</summary>
        internal static void Tick(Dispatcher dispatcher = null)
        {
            try { TickCore(); }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[P2诊断] Tick 异常: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        static void TickCore()
        {
            long now = _since.ElapsedMilliseconds;
            if (now - _lastFlushMs < 5000) return;
            _lastFlushMs = now;

            int gc0 = GC.CollectionCount(0) - _gc0AtStart;
            int gc1 = GC.CollectionCount(1) - _gc1AtStart;
            int gc2 = GC.CollectionCount(2) - _gc2AtStart;

            long ws = 0;
            try { ws = Process.GetCurrentProcess().WorkingSet64 / (1024 * 1024); } catch { }

            var sb = new StringBuilder(240);
            sb.Append("[P2诊断] 运行=").Append(now / 1000).Append("s");
            sb.Append(" 笔=").Append(StrokeCount);
            if (StrokeCount > 0)
            {
                sb.Append(" 均=").Append(StrokeTotalMs / Math.Max(1, StrokeCount)).Append("ms");
                sb.Append(" 峰=").Append(StrokeMaxMs).Append("ms");
            }
            sb.Append(" | dec=").Append(DecCountNow).Append(" 峰=").Append(DecCountMax);
            if (SilentReleaseCount > 0) sb.Append(" 静默释放=").Append(SilentReleaseCount);
            // 只打非零的闸门，避免刷屏
            {
                var gsb = new System.Text.StringBuilder();
                for (int i = 0; i < Gate.Length; i++)
                {
                    if (Gate[i] > 0) gsb.Append(' ').Append(GateName[i]).Append('=').Append(Gate[i]);
                }
                if (gsb.Length > 0) sb.Append(" | 闸").Append(gsb);
            }
            if (TouchUpFallback > 0) sb.Append(" 释放降级=").Append(TouchUpFallback);
            if (StalePruned > 0) sb.Append(" 清理僵尸=").Append(StalePruned);
            if (SlowStrokeCount > 0) sb.Append(" 慢笔=").Append(SlowStrokeCount);
            sb.Append(" | 双指 中=").Append(TwoFingerMatched).Append(" 漏=").Append(TwoFingerMissed);
            sb.Append(" | GC ").Append(gc0).Append("/").Append(gc1).Append("/").Append(gc2);
            sb.Append(" 工作集=").Append(ws).Append("MB");

            // LTSC P2-10：内存与句柄稳态。
            // 只看工作集不够——Win32 句柄和 GDI/USER 对象泄漏不会立刻反映到托管堆，
            // 必须单独看这三项，否则"没涨"只是没测到。
            try
            {
                var proc = Process.GetCurrentProcess();
                proc.Refresh();
                sb.Append(" 句柄=").Append(proc.HandleCount);
            }
            catch { }
            try { sb.Append(" 托管堆=").Append(GC.GetTotalMemory(false) / (1024 * 1024)).Append("MB"); } catch { }
            try
            {
                sb.Append(" GDI=").Append(GetGuiResources(GetCurrentProcess(), 0));
                sb.Append(" USER=").Append(GetGuiResources(GetCurrentProcess(), 1));
            }
            catch { }

            TickCount++;
            LogHelper.WriteLogToFile(sb.ToString(),
                TickCount <= 3 ? LogHelper.LogType.Event : LogHelper.LogType.Trace);
        }

        internal static void Reset()
        {
            StrokeCount = 0; StrokeTotalMs = 0; StrokeMaxMs = 0;
            DecCountNow = 0; DecCountMax = 0; SilentReleaseCount = 0;
            TouchUpFallback = 0; StalePruned = 0;
            for (int i = 0; i < Gate.Length; i++) Gate[i] = 0;
            TwoFingerMatched = 0; TwoFingerMissed = 0;
            SlowStrokeCount = 0; LastSlowContext = null;
            TickCount = 0;
            _gc0AtStart = GC.CollectionCount(0);
            _gc1AtStart = GC.CollectionCount(1);
            _gc2AtStart = GC.CollectionCount(2);
            _since.Restart();
            _lastFlushMs = 0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int GetGuiResources(IntPtr hProcess, int uiFlags);

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();
    }

}
