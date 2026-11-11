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

        // 触摸状态
        internal static int DecCountNow;
        internal static int DecCountMax;
        internal static int SilentReleaseCount;

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
            sb.Append(" | 双指 中=").Append(TwoFingerMatched).Append(" 漏=").Append(TwoFingerMissed);
            sb.Append(" | GC ").Append(gc0).Append("/").Append(gc1).Append("/").Append(gc2);
            sb.Append(" 工作集=").Append(ws).Append("MB");

            TickCount++;
            LogHelper.WriteLogToFile(sb.ToString(),
                TickCount <= 3 ? LogHelper.LogType.Event : LogHelper.LogType.Trace);
        }

        internal static void Reset()
        {
            StrokeCount = 0; StrokeTotalMs = 0; StrokeMaxMs = 0;
            DecCountNow = 0; DecCountMax = 0; SilentReleaseCount = 0;
            TwoFingerMatched = 0; TwoFingerMissed = 0;
            TickCount = 0;
            _gc0AtStart = GC.CollectionCount(0);
            _gc1AtStart = GC.CollectionCount(1);
            _gc2AtStart = GC.CollectionCount(2);
            _since.Restart();
            _lastFlushMs = 0;
        }
    }
}
