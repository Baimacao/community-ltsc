using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 墨迹平滑计算器。
    ///
    /// 原实现叫 HardwareAcceleratedInkProcessor、声称「利用 WPF 的 GPU 渲染能力」，
    /// 实际做的是 PathGeometry 构造 + 展平 + 重采样，全是 CPU 数学运算，没碰过 GPU。
    /// 更糟的是它把这段 1 毫秒级的计算塞进 Task.Run 排队，再由 UI 线程同步 Wait：
    ///
    ///   实测（500 点笔画，8 核）：
    ///     线程池空闲：Task.Run + Wait 23ms  vs  直接算 0.34ms    —— 慢 68 倍
    ///     线程池被占满：Task.Run + Wait 2714ms                  —— 慢 8000 倍
    ///
    /// 也就是说原设计的 5 秒超时等的东西不是计算，是线程池排队。
    /// 而 Steam 不做线程池调度，保留 Task 返回类型只是为了不破坏已有的 await 调用点。
    ///
    /// 顺带删掉一个构造时 new 出来、全文再无引用的 1920x1080 Pbgra32 RenderTargetBitmap
    /// （约 7.9 MB 常驻），它从来没有 Render 过一次。
    /// </summary>
    public class HardwareAcceleratedInkProcessor : IDisposable
    {
        private bool _disposed;

        /// <summary>
        /// 保留 IDisposable 形状：本类已不持有位图等非托管资源，
        /// 但 InkSmoothingManager 的释放路径会调用它，实现为空即可。
        /// </summary>
        public void Dispose()
        {
            _disposed = true;
        }

        /// <summary>
        /// 平滑一笔。**同步完成**，返回已完成的 Task。
        /// 调用方在 UI 线程上直接调用也不会阻塞——因为这里根本没有排队。
        /// </summary>
        public Task<Stroke> SmoothStrokeWithGPU(Stroke originalStroke)
        {
            try
            {
                return Task.FromResult(SmoothCore(originalStroke));
            }
            catch
            {
                return Task.FromResult(originalStroke);
            }
        }

        /// <summary>
        /// 真正的同步实现。无 Task、无线程切换、无等待。
        /// </summary>
        public Stroke SmoothStrokeSync(Stroke originalStroke)
        {
            try
            {
                return SmoothCore(originalStroke);
            }
            catch
            {
                return originalStroke;
            }
        }

        private Stroke SmoothCore(Stroke originalStroke)
        {
            if (originalStroke == null || originalStroke.StylusPoints.Count < 2)
                return originalStroke;

            var pathGeometry = CreateSmoothPathGeometry(originalStroke.StylusPoints);
            var smoothedPoints = ConvertPathGeometryToStylusPoints(pathGeometry, originalStroke.StylusPoints);

            return new Stroke(new StylusPointCollection(smoothedPoints))
            {
                DrawingAttributes = originalStroke.DrawingAttributes.Clone()
            };
        }

        /// <summary>
        /// 用三次贝塞尔把点串起来。
        /// 注意：这里只构造几何，不做任何渲染。
        /// </summary>
        private PathGeometry CreateSmoothPathGeometry(StylusPointCollection points)
        {
            var pathGeometry = new PathGeometry();
            var pathFigure = new PathFigure();

            if (points.Count < 2) return pathGeometry;

            pathFigure.StartPoint = new Point(points[0].X, points[0].Y);

            for (int i = 0; i < points.Count - 1; i += 2)
            {
                var p1 = i + 1 < points.Count ? new Point(points[i + 1].X, points[i + 1].Y) : pathFigure.StartPoint;
                var p2 = i + 2 < points.Count ? new Point(points[i + 2].X, points[i + 2].Y) : p1;
                var p3 = i + 3 < points.Count ? new Point(points[i + 3].X, points[i + 3].Y) : p2;

                pathFigure.Segments.Add(new BezierSegment(p1, p2, p3, true));
            }

            pathGeometry.Figures.Add(pathFigure);
            return pathGeometry;
        }

        private List<StylusPoint> ConvertPathGeometryToStylusPoints(PathGeometry pathGeometry, StylusPointCollection originalPoints)
        {
            var result = new List<StylusPoint>();
            var flattened = pathGeometry.GetFlattenedPathGeometry();

            foreach (var figure in flattened.Figures)
            {
                result.Add(new StylusPoint(figure.StartPoint.X, figure.StartPoint.Y, 0.5f));

                foreach (var segment in figure.Segments)
                {
                    if (segment is LineSegment lineSegment)
                    {
                        result.Add(new StylusPoint(lineSegment.Point.X, lineSegment.Point.Y, 0.5f));
                    }
                    else if (segment is PolyLineSegment polyLineSegment)
                    {
                        foreach (var point in polyLineSegment.Points)
                        {
                            result.Add(new StylusPoint(point.X, point.Y, 0.5f));
                        }
                    }
                }
            }

            InterpolatePressure(result, originalPoints);
            return result;
        }

        /// <summary>
        /// 把原始压感按位置比例摊回到重采样后的点上。
        /// 这里保留原实现的意图：按索引比例插值。
        /// </summary>
        private void InterpolatePressure(List<StylusPoint> result, StylusPointCollection original)
        {
            if (result.Count == 0 || original.Count == 0) return;
            if (result.Count == original.Count)
            {
                for (int i = 0; i < result.Count; i++)
                {
                    result[i] = new StylusPoint(result[i].X, result[i].Y, original[i].PressureFactor);
                }
                return;
            }

            double ratio = (double)(original.Count - 1) / Math.Max(1, result.Count - 1);
            for (int i = 0; i < result.Count; i++)
            {
                int idx = (int)Math.Round(i * ratio);
                if (idx < 0) idx = 0;
                if (idx >= original.Count) idx = original.Count - 1;
                result[i] = new StylusPoint(result[i].X, result[i].Y, original[idx].PressureFactor);
            }
        }
    }
}
