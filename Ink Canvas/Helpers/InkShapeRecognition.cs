using OSVersionExtension;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>墨迹形状识别后端：自动 / IACore / WinRT。</summary>
    public enum ShapeRecognitionEngineMode
    {
        Auto = 0,
        IACore = 1,
        WinRT = 2,
    }

    public static class ShapeRecognitionRouter
    {
        /// <summary>
        /// 自动模式：使用 IACore。
        ///
        /// LTSC P2（2026-11-12）：Auto 原来在 Windows 10 及以上选 WinRT，现已改为选 IACore。
        /// 原因是 WinRT 路径每画一笔会净漏一个内核 Event 句柄，实测：
        ///   WinRT  ：基线 994 → 画 150 笔后 1155，Δ=+161
        ///   IACore ：基线 1003 → 画 150 笔后 986， Δ=-17
        /// 且静置 80 秒后句柄不回落（1156 → 1164），确认是真泄漏而非延迟释放。
        /// 定位到 `_internalAnalyzer.AnalyzeAsync().AsTask()`：WinRT 的 IAsyncOperation
        /// 转 Task 时每次都会带出一个内核对象，这部分在下游无法回收。
        /// IACore 是纯 .NET 实现，不碰 COM，因此没有这个问题。
        /// 若用户显式选择 WinRT 仍照旧生效，只是不再作为 Auto 的默认。
        /// </summary>
        public static bool ResolveUseWinRt(ShapeRecognitionEngineMode mode)
        {
            if (mode == ShapeRecognitionEngineMode.WinRT) return true;
            if (mode == ShapeRecognitionEngineMode.IACore) return false;
            return false;
        }

        public static bool ShouldRunShapeRecognition(bool inkToShapeEnabled, ShapeRecognitionEngineMode mode)
        {
            if (!inkToShapeEnabled) return false;
            if (ResolveUseWinRt(mode))
                return OSVersion.GetOperatingSystem() >= OSVersionExtension.OperatingSystem.Windows10;
            return true;
        }

        public static ShapeRecognitionEngineMode FromSettingsInt(int value)
        {
            if (value == (int)ShapeRecognitionEngineMode.IACore) return ShapeRecognitionEngineMode.IACore;
            if (value == (int)ShapeRecognitionEngineMode.WinRT) return ShapeRecognitionEngineMode.WinRT;
            return ShapeRecognitionEngineMode.Auto;
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
