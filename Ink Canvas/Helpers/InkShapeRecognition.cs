using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>墨迹形状识别后端。当前只有 IACore 一种实现。</summary>
    public enum ShapeRecognitionEngineMode
    {
        /// <summary>IACore（通过 x86 辅助进程的 IPC 调用）。</summary>
        IACore = 1,
    }

    public static class ShapeRecognitionRouter
    {
        /// <summary>
        /// 形状识别是否需要运行。
        ///
        /// LTSC（2026-11-12）：WinRT 后端已整体移除。
        /// 原因是 WinRT 路径每画一笔会净漏一个内核 Event 句柄，实测：
        ///   WinRT  ：基线 994 → 画 150 笔后 1155，Δ=+161
        ///   IACore ：基线 1003 → 画 150 笔后 986， Δ=-17
        /// 且静置 80 秒后句柄不回落，确认是真泄漏而非延迟释放。
        /// 泄漏点在 `AnalyzeAsync().AsTask()`：WinRT 的 IAsyncOperation 转 Task 时
        /// 每次都会带出一个内核对象，下游无法回收。IACore 走 IPC 辅助进程，不碰 COM，无此问题。
        /// 手写美化（WinRtHandwritingRecognizer）依赖同一个 WinRT 栈，已一并移除。
        /// </summary>
        /// <summary>形状识别是否需要运行。现在只看开关本身。</summary>
        public static bool ShouldRunShapeRecognition(bool inkToShapeEnabled)
        {
            return inkToShapeEnabled;
        }
    }

    /// <summary>与具体识别后端无关的形状识别结果，供统一纠正模块消费。</summary>
    public sealed class InkShapeRecognitionResult
    {
        public static readonly InkShapeRecognitionResult Empty = new InkShapeRecognitionResult();

        private InkShapeRecognitionResult()
        {
            IsSuccess = false;
            ShapeName = string.Empty;
            Centroid = new Point();
            HotPoints = new PointCollection();
            StrokesToRemove = new StrokeCollection();
        }

        public InkShapeRecognitionResult(
            string shapeName,
            Point centroid,
            PointCollection hotPoints,
            double shapeWidth,
            double shapeHeight,
            StrokeCollection strokesToRemove)
        {
            ShapeName = shapeName ?? string.Empty;
            Centroid = centroid;
            HotPoints = hotPoints ?? new PointCollection();
            ShapeWidth = shapeWidth;
            ShapeHeight = shapeHeight;
            StrokesToRemove = strokesToRemove ?? new StrokeCollection();
            IsSuccess = StrokesToRemove.Count > 0
                        && !string.IsNullOrEmpty(ShapeName)
                        && ShapeName != "Drawing";
        }

        public bool IsSuccess { get; }
        public string ShapeName { get; }
        public Point Centroid { get; set; }
        public PointCollection HotPoints { get; }
        public double ShapeWidth { get; }
        public double ShapeHeight { get; }
        public StrokeCollection StrokesToRemove { get; }
    }
}
