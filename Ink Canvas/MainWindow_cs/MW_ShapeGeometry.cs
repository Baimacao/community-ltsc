using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        // LTSC（2026-11-17）：从 MW_SimulatePressure&InkToShape.cs 拆出。
        //
        // 这个文件原来把两件事混在一起：
        //   1. 墨迹转形状 / 直线拉直 / 形状判断的主流程（留在原文件）
        //   2. 纯几何工具：距离、压感生成、中心点、分辨率缩放、矩形参考线（在这里）
        //
        // 工具段里大部分是纯函数，输入输出不碰状态。只搬方法，不改逻辑。

        /// <summary>
        /// 设置新的笔画备份
        /// </summary>
        /// <remarks>
        /// 设置新的笔画备份的流程：
        /// 1. 克隆当前墨水画布的笔画集合
        /// 2. 获取当前白板索引
        /// 3. 如果当前模式为0，则将白板索引设置为0
        /// 4. 将克隆的笔画集合存储到strokeCollections中对应索引的位置
        /// </remarks>
        private void SetNewBackupOfStroke()
        {
            lastTouchDownStrokeCollection = inkCanvas.Strokes.Clone();
        }

        /// <summary>
        /// 计算两点之间的距离
        /// </summary>
        /// <param name="point1">第一个点</param>
        /// <param name="point2">第二个点</param>
        /// <returns>返回两点之间的距离</returns>
        /// <remarks>
        /// 使用欧几里得距离公式计算两点之间的距离：
        /// distance = √[(x2 - x1)² + (y2 - y1)²]
        /// </remarks>
        public double GetDistance(Point point1, Point point2)
        {
            return Math.Sqrt((point1.X - point2.X) * (point1.X - point2.X) +
                             (point1.Y - point2.Y) * (point1.Y - point2.Y));
        }

        /// <summary>
        /// 计算点的速度
        /// </summary>
        /// <param name="point1">第一个点</param>
        /// <param name="point2">第二个点（当前点）</param>
        /// <param name="point3">第三个点</param>
        /// <returns>返回点的速度</returns>
        /// <remarks>
        /// 计算点速度的流程：
        /// 1. 计算第一个点到第二个点的距离
        /// 2. 计算第三个点到第二个点的距离
        /// 3. 将两个距离相加
        /// 4. 除以20，得到速度值
        /// </remarks>
        public double GetPointSpeed(Point point1, Point point2, Point point3)
        {
            return (Math.Sqrt((point1.X - point2.X) * (point1.X - point2.X) +
                              (point1.Y - point2.Y) * (point1.Y - point2.Y))
                    + Math.Sqrt((point3.X - point2.X) * (point3.X - point2.X) +
                                (point3.Y - point2.Y) * (point3.Y - point2.Y)))
                   / 20;
        }

        private static float RateBasedPressureFactorFromPointSpeed(double speed)
        {
            if (speed >= 0.25)
                return (float)(0.5 - 0.3 * (Math.Min(speed, 1.5) - 0.3) / 1.2);
            if (speed >= 0.05)
                return 0.5f;
            return (float)(0.5 + 0.4 * (0.05 - speed) / 0.05);
        }

        private static float RealtimeBrushTipMixRatePressureFromSpeed(double speed)
        {
            if (speed < 0) speed = 0;
            const double slowRef = 0.012;
            const double fastRef = 0.5;
            var t = (speed - slowRef) / (fastRef - slowRef);
            if (t < 0) t = 0;
            else if (t > 1) t = 1;
            t = t * t * (3.0 - 2.0 * t);
            const double pThick = 0.9;
            const double pThin = 0.22;
            var p = pThick + (pThin - pThick) * t;
            return (float)Math.Max(0.08, Math.Min(1.0, p));
        }

        private static bool IsStrokePressureApproximatelyConstant(StylusPointCollection pts, out float meanPf)
        {
            meanPf = 0.5f;
            if (pts == null || pts.Count == 0) return true;
            double sum = 0;
            foreach (StylusPoint p in pts) sum += p.PressureFactor;
            meanPf = (float)(sum / pts.Count);
            const float tol = 0.04f;
            foreach (StylusPoint p in pts)
            {
                if (Math.Abs(p.PressureFactor - meanPf) > tol)
                    return false;
            }

            return true;
        }

        private void ReplaceStrokesSafely(StrokeCollection strokesToRemove, Stroke replacementStroke, Stroke fallbackOriginalStroke = null)
        {
            if (replacementStroke == null) return;
            try
            {
                SetNewBackupOfStroke();
                _currentCommitType = CommitReason.ShapeRecognition;

                if (strokesToRemove != null && strokesToRemove.Count > 0)
                    inkCanvas.Strokes.Remove(strokesToRemove);
                if (fallbackOriginalStroke != null && inkCanvas.Strokes.Contains(fallbackOriginalStroke))
                    inkCanvas.Strokes.Remove(fallbackOriginalStroke);

                if (!inkCanvas.Strokes.Contains(replacementStroke))
                    inkCanvas.Strokes.Add(replacementStroke);
            }
            finally
            {
                _currentCommitType = CommitReason.UserInput;
            }
        }

        private void ReplaceStrokesSafely(StrokeCollection strokesToRemove, StrokeCollection replacementStrokes, Stroke fallbackOriginalStroke = null)
        {
            if (replacementStrokes == null || replacementStrokes.Count == 0) return;
            try
            {
                SetNewBackupOfStroke();
                _currentCommitType = CommitReason.ShapeRecognition;

                if (strokesToRemove != null && strokesToRemove.Count > 0)
                    inkCanvas.Strokes.Remove(strokesToRemove);
                if (fallbackOriginalStroke != null && inkCanvas.Strokes.Contains(fallbackOriginalStroke))
                    inkCanvas.Strokes.Remove(fallbackOriginalStroke);

                inkCanvas.Strokes.Add(replacementStrokes);
            }
            finally
            {
                _currentCommitType = CommitReason.UserInput;
            }
        }

        /// <summary>
        /// 将沿线速度映射为压感并与硬件压感混合，快写略细、慢写略粗；在落笔时（及手写笔移动时由调用方）统一施加。
        /// 无压感设备上系统可能将 <see cref="DrawingAttributes.IgnorePressure"/> 置为 true，此处强制关闭以便粗细随合成压感变化。
        /// 若 <see cref="Settings.Canvas.DisablePressure"/> 为 true，本方法直接返回且不修改 IgnorePressure。
        /// </summary>
        private void ApplyVelocityBrushTipFromSpeed(Stroke stroke)
        {
            try
            {
                if (Settings.Canvas.DisablePressure)
                    return;

                var mix = Settings.Canvas.VelocityBrushTipMix;
                if (mix <= 0 || stroke == null) return;
                if (mix > 1) mix = 1;

                if (stroke.DrawingAttributes != null)
                    stroke.DrawingAttributes.IgnorePressure = false;

                var pts = stroke.StylusPoints;
                if (pts.Count < 3) return;

                var effectiveMix = (float)mix;
                if (IsStrokePressureApproximatelyConstant(pts, out _))
                    effectiveMix = Math.Max(effectiveMix, 0.78f);

                var n = pts.Count - 1;
                var stylusPoints = new StylusPointCollection();

                for (var i = 0; i <= n; i++)
                {
                    var speed = GetPointSpeed(
                        pts[Math.Max(i - 1, 0)].ToPoint(),
                        pts[i].ToPoint(),
                        pts[Math.Min(i + 1, n)].ToPoint());

                    var speedPressure = RealtimeBrushTipMixRatePressureFromSpeed(speed);

                    var basePf = pts[i].PressureFactor;
                    var blended = (1.0f - effectiveMix) * basePf + effectiveMix * speedPressure;
                    blended = (float)Math.Max(0.08, Math.Min(1.0, blended));

                    var p = new StylusPoint(pts[i].X, pts[i].Y, blended);
                    stylusPoints.Add(p);
                }

                stroke.StylusPoints = stylusPoints;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex);
            }
        }

        public Point[] FixPointsDirection(Point p1, Point p2)
        {
            double deltaY = Math.Abs(p1.Y - p2.Y);
            double deltaX = Math.Abs(p1.X - p2.X);

            if (deltaY < 1e-10 || deltaX / deltaY > 8)
            {
                //水平
                var x = deltaY / 2;
                if (p1.Y > p2.Y)
                {
                    p1.Y -= x;
                    p2.Y += x;
                }
                else
                {
                    p1.Y += x;
                    p2.Y -= x;
                }
            }
            else if (deltaX < 1e-10 || deltaY / deltaX > 8)
            {
                //垂直
                var x = deltaX / 2;
                if (p1.X > p2.X)
                {
                    p1.X -= x;
                    p2.X += x;
                }
                else
                {
                    p1.X += x;
                    p2.X -= x;
                }
            }

            return new Point[2] { p1, p2 };
        }

        /// <summary>
        /// 用点集拟合出 IACore 风格椭圆参数（中心/长短半轴/方向/四个端点）。
        /// 解决 WinRT 返回热点顺序不稳定导致椭圆纠正角度翻转的问题。
        /// </summary>
        private static bool TryEstimateEllipseParamsFromStrokes(
            StrokeCollection strokes,
            out Point centroid,
            out double a,
            out double b,
            out double thetaRad,
            out Point major0,
            out Point major1,
            out Point minor0,
            out Point minor1)
        {
            centroid = default;
            a = b = 0;
            thetaRad = 0;
            major0 = major1 = minor0 = minor1 = default;

            if (strokes == null || strokes.Count == 0) return false;

            var pts = new List<Point>(256);
            foreach (var s in strokes)
            {
                if (s?.StylusPoints == null) continue;
                foreach (var sp in s.StylusPoints)
                    pts.Add(sp.ToPoint());
            }

            if (pts.Count < 12) return false;

            double mx = 0, my = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                mx += pts[i].X;
                my += pts[i].Y;
            }
            mx /= pts.Count;
            my /= pts.Count;
            centroid = new Point(mx, my);

            double sxx = 0, syy = 0, sxy = 0;
            for (int i = 0; i < pts.Count; i++)
            {
                var dx = pts[i].X - mx;
                var dy = pts[i].Y - my;
                sxx += dx * dx;
                syy += dy * dy;
                sxy += dx * dy;
            }

            if (sxx + syy < 1e-6) return false;

            thetaRad = 0.5 * Math.Atan2(2.0 * sxy, sxx - syy);
            if (double.IsNaN(thetaRad) || double.IsInfinity(thetaRad)) return false;

            // 主轴单位向量 v1=(cos,sin)，次轴 v2=(-sin,cos)
            var cos = Math.Cos(thetaRad);
            var sin = Math.Sin(thetaRad);

            // 投影收集，用分位数抑制离群点
            var us = new double[pts.Count];
            var vs = new double[pts.Count];
            double maxU = double.MinValue, minU = double.MaxValue;
            double maxV = double.MinValue, minV = double.MaxValue;
            for (int i = 0; i < pts.Count; i++)
            {
                var dx = pts[i].X - mx;
                var dy = pts[i].Y - my;
                var u = dx * cos + dy * sin;
                var v = -dx * sin + dy * cos;
                us[i] = u;
                vs[i] = v;
                if (u > maxU) maxU = u;
                if (u < minU) minU = u;
                if (v > maxV) maxV = v;
                if (v < minV) minV = v;
            }

            Array.Sort(us);
            Array.Sort(vs);

            int hi = (int)Math.Round((pts.Count - 1) * 0.98);
            int lo = (int)Math.Round((pts.Count - 1) * 0.02);
            hi = Math.Max(0, Math.Min(pts.Count - 1, hi));
            lo = Math.Max(0, Math.Min(pts.Count - 1, lo));

            var uHi = us[hi];
            var uLo = us[lo];
            var vHi = vs[hi];
            var vLo = vs[lo];

            var aCandidate = Math.Max(Math.Abs(uHi), Math.Abs(uLo));
            var bCandidate = Math.Max(Math.Abs(vHi), Math.Abs(vLo));
            if (aCandidate < 1e-3) aCandidate = Math.Max(Math.Abs(maxU), Math.Abs(minU));
            if (bCandidate < 1e-3) bCandidate = Math.Max(Math.Abs(maxV), Math.Abs(minV));

            a = aCandidate;
            b = bCandidate;

            // 保证 a 为长半轴
            if (b > a)
            {
                var t = a; a = b; b = t;
                thetaRad += Math.PI / 2;
                cos = Math.Cos(thetaRad);
                sin = Math.Sin(thetaRad);
            }

            major0 = new Point(mx - a * cos, my - a * sin);
            major1 = new Point(mx + a * cos, my + a * sin);
            minor0 = new Point(mx + b * sin, my - b * cos);
            minor1 = new Point(mx - b * sin, my + b * cos);

            return a > 1e-2 && b > 1e-2;
        }

        public StylusPointCollection GenerateFakePressureTriangle(StylusPointCollection points)
        {
            var newPoint = new StylusPointCollection();

            if (Settings.InkToShape.IsInkToShapeNoFakePressureTriangle || penType == 1)
            {
                if (points.Count >= 3)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Point start = points[i].ToPoint();
                        Point end = points[(i + 1) % 3].ToPoint();
                        var edgePoints = GeneratePointsBetween(start, end, 0.5f, 0.5f, 8.0);
                        if (i == 0)
                        {
                            foreach (var pt in edgePoints)
                            {
                                newPoint.Add(pt);
                            }
                        }
                        else
                        {
                            for (int j = 1; j < edgePoints.Count; j++)
                            {
                                newPoint.Add(edgePoints[j]);
                            }
                        }
                    }
                    Point lastPoint = points[0].ToPoint();
                    Point firstPoint = newPoint[0].ToPoint();
                    if (GetDistance(lastPoint, firstPoint) > 1.0)
                    {
                        newPoint.Add(new StylusPoint(lastPoint.X, lastPoint.Y, 0.5f));
                    }
                }
                else
                {
                    return points;
                }
                return newPoint;
            }
            else
            {
                if (points.Count >= 3)
                {
                    for (int i = 0; i < 3; i++)
                    {
                        Point start = points[i].ToPoint();
                        Point end = points[(i + 1) % 3].ToPoint();

                        Point midPoint = GetCenterPoint(start, end);

                        var startToMid = GeneratePointsBetween(start, midPoint, 0.4f, 0.8f, 8.0);
                        if (i == 0)
                        {
                            foreach (var pt in startToMid)
                            {
                                newPoint.Add(pt);
                            }
                        }
                        else
                        {
                            for (int j = 1; j < startToMid.Count; j++)
                            {
                                newPoint.Add(startToMid[j]);
                            }
                        }

                        var midToEnd = GeneratePointsBetween(midPoint, end, 0.8f, 0.4f, 8.0);
                        for (int j = 1; j < midToEnd.Count; j++)
                        {
                            newPoint.Add(midToEnd[j]);
                        }
                    }
                    Point lastPoint = points[0].ToPoint();
                    Point firstPoint = newPoint[0].ToPoint();
                    if (GetDistance(lastPoint, firstPoint) > 1.0)
                    {
                        newPoint.Add(new StylusPoint(lastPoint.X, lastPoint.Y, 0.4f));
                    }
                }
                else
                {
                    return points;
                }
                return newPoint;
            }
        }

        /// <summary>
        /// 在两点之间生成多个点，用于增加图形边缘的点密度
        /// </summary>
        private StylusPointCollection GeneratePointsBetween(Point start, Point end, float startPressure, float endPressure, double minPointInterval = 8.0)
        {
            var result = new StylusPointCollection();
            double distance = GetDistance(start, end);

            if (distance < minPointInterval)
            {
                result.Add(new StylusPoint(start.X, start.Y, startPressure));
                result.Add(new StylusPoint(end.X, end.Y, endPressure));
                return result;
            }

            int pointCount = Math.Max(2, (int)(distance / minPointInterval) + 1);

            result.Add(new StylusPoint(start.X, start.Y, startPressure));

            for (int i = 1; i < pointCount - 1; i++)
            {
                double ratio = (double)i / (pointCount - 1);
                double pressure = startPressure + (endPressure - startPressure) * ratio;
                double x = start.X + (end.X - start.X) * ratio;
                double y = start.Y + (end.Y - start.Y) * ratio;
                result.Add(new StylusPoint(x, y, (float)pressure));
            }

            result.Add(new StylusPoint(end.X, end.Y, endPressure));

            return result;
        }

        public StylusPointCollection GenerateFakePressureRectangle(StylusPointCollection points)
        {
            var newPoint = new StylusPointCollection();

            if (Settings.InkToShape.IsInkToShapeNoFakePressureRectangle || penType == 1)
            {
                if (points.Count >= 4)
                {
                    for (int i = 0; i < 4; i++)
                    {
                        Point start = points[i].ToPoint();
                        Point end = points[(i + 1) % 4].ToPoint();
                        var edgePoints = GeneratePointsBetween(start, end, 0.5f, 0.5f, 8.0);
                        if (i == 0)
                        {
                            foreach (var pt in edgePoints)
                            {
                                newPoint.Add(pt);
                            }
                        }
                        else
                        {
                            for (int j = 1; j < edgePoints.Count; j++)
                            {
                                newPoint.Add(edgePoints[j]);
                            }
                        }
                    }
                }
                else
                {
                    return points;
                }
                return newPoint;
            }

            if (points.Count >= 4)
            {
                for (int i = 0; i < 4; i++)
                {
                    Point start = points[i].ToPoint();
                    Point end = points[(i + 1) % 4].ToPoint();

                    Point midPoint = GetCenterPoint(start, end);

                    var startToMid = GeneratePointsBetween(start, midPoint, 0.4f, 0.8f, 8.0);
                    if (i == 0)
                    {
                        foreach (var pt in startToMid)
                        {
                            newPoint.Add(pt);
                        }
                    }
                    else
                    {
                        for (int j = 1; j < startToMid.Count; j++)
                        {
                            newPoint.Add(startToMid[j]);
                        }
                    }

                    var midToEnd = GeneratePointsBetween(midPoint, end, 0.8f, 0.4f, 8.0);
                    for (int j = 1; j < midToEnd.Count; j++)
                    {
                        newPoint.Add(midToEnd[j]);
                    }
                }
            }
            else
            {
                return points;
            }

            return newPoint;
        }

        public Point GetCenterPoint(Point point1, Point point2)
        {
            return new Point((point1.X + point2.X) / 2, (point1.Y + point2.Y) / 2);
        }

        public StylusPoint GetCenterPoint(StylusPoint point1, StylusPoint point2)
        {
            return new StylusPoint((point1.X + point2.X) / 2, (point1.Y + point2.Y) / 2);
        }

        // 分辨率自适应：以1080P为基准，返回当前分辨率下的阈值倍数
        private double GetResolutionScale()
        {
            // 以1920x1080为基准
            double baseWidth = 1920.0;
            double baseHeight = 1080.0;
            double screenWidth = SystemParameters.PrimaryScreenWidth;
            double screenHeight = SystemParameters.PrimaryScreenHeight;
            // 取宽高平均缩放，防止极端比例
            double scaleW = screenWidth / baseWidth;
            double scaleH = screenHeight / baseHeight;
            return (scaleW + scaleH) / 2.0;
        }

        #region 矩形参考线系统

        /// <summary>
        /// 处理矩形参考线系统
        /// </summary>
        private void ProcessRectangleGuideLines(Stroke newStroke)
        {
            // 只有启用矩形识别时才处理
            if (!Settings.InkToShape.IsInkToShapeRectangle) return;

            // 检查新笔画是否为直线
            if (!IsPotentialStraightLine(newStroke)) return;

            Point startPoint = newStroke.StylusPoints[0].ToPoint();
            Point endPoint = newStroke.StylusPoints[newStroke.StylusPoints.Count - 1].ToPoint();

            // 创建新的参考线
            var newGuideLine = new RectangleGuideLine(newStroke, startPoint, endPoint);

            // 清理过期的参考线（超过30秒的）
            CleanupExpiredGuideLines();

            // 添加新参考线
            rectangleGuideLines.Add(newGuideLine);

            // 检查是否可以构成矩形
            CheckForRectangleFormation();
        }

        /// <summary>
        /// 清理过期的参考线
        /// </summary>
        private void CleanupExpiredGuideLines()
        {
            var expireTime = DateTime.Now.AddSeconds(-30); // 30秒过期
            for (int i = rectangleGuideLines.Count - 1; i >= 0; i--)
            {
                var guideLine = rectangleGuideLines[i];
                if (guideLine.CreatedTime < expireTime || !inkCanvas.Strokes.Contains(guideLine.OriginalStroke))
                {
                    rectangleGuideLines.RemoveAt(i);
                }
            }
        }

        /// <summary>
        /// 检查是否可以构成矩形
        /// </summary>
        private void CheckForRectangleFormation()
        {
            if (rectangleGuideLines.Count < 4) return;

            // 尝试找到四条能构成矩形的直线
            var rectangleLines = FindRectangleLines();
            if (rectangleLines != null && rectangleLines.Count == 4)
            {
                // 创建矩形并替换原有直线
                CreateRectangleFromLines(rectangleLines);
            }
        }

        /// <summary>
        /// 寻找能构成矩形的四条直线
        /// </summary>
        private List<RectangleGuideLine> FindRectangleLines()
        {
            // 按时间排序，优先考虑最近绘制的直线
            var sortedLines = rectangleGuideLines.OrderByDescending(l => l.CreatedTime).ToList();

            // 尝试不同的四条直线组合
            for (int i = 0; i < sortedLines.Count - 3; i++)
            {
                for (int j = i + 1; j < sortedLines.Count - 2; j++)
                {
                    for (int k = j + 1; k < sortedLines.Count - 1; k++)
                    {
                        for (int l = k + 1; l < sortedLines.Count; l++)
                        {
                            var lines = new List<RectangleGuideLine> { sortedLines[i], sortedLines[j], sortedLines[k], sortedLines[l] };
                            if (CanFormRectangle(lines))
                            {
                                return lines;
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// 判断四条直线是否能构成矩形
        /// </summary>
        private bool CanFormRectangle(List<RectangleGuideLine> lines)
        {
            if (lines.Count != 4) return false;

            // 分类水平线和垂直线
            var horizontalLines = lines.Where(l => l.IsHorizontal).ToList();
            var verticalLines = lines.Where(l => l.IsVertical).ToList();

            // 必须有2条水平线和2条垂直线
            if (horizontalLines.Count != 2 || verticalLines.Count != 2) return false;

            // 检查端点相交关系
            return CheckEndpointConnections(horizontalLines, verticalLines);
        }

        /// <summary>
        /// 检查端点相交关系
        /// </summary>
        private bool CheckEndpointConnections(List<RectangleGuideLine> horizontalLines, List<RectangleGuideLine> verticalLines)
        {
            // 收集所有端点
            var allEndpoints = new List<Point>();
            foreach (var line in horizontalLines.Concat(verticalLines))
            {
                allEndpoints.Add(line.StartPoint);
                allEndpoints.Add(line.EndPoint);
            }

            // 检查是否有4个相交点（允许一定误差）
            var intersectionPoints = new List<Point>();

            foreach (var hLine in horizontalLines)
            {
                foreach (var vLine in verticalLines)
                {
                    var intersection = GetLineIntersection(hLine, vLine);
                    if (intersection.HasValue)
                    {
                        // 检查交点是否在两条线段的端点附近
                        if (IsPointNearLineEndpoints(intersection.Value, hLine) &&
                            IsPointNearLineEndpoints(intersection.Value, vLine))
                        {
                            intersectionPoints.Add(intersection.Value);
                        }
                    }
                }
            }

            // 需要有4个交点才能构成矩形
            return intersectionPoints.Count >= 4;
        }

        /// <summary>
        /// 计算两条直线的交点
        /// </summary>
        private Point? GetLineIntersection(RectangleGuideLine line1, RectangleGuideLine line2)
        {
            double x1 = line1.StartPoint.X, y1 = line1.StartPoint.Y;
            double x2 = line1.EndPoint.X, y2 = line1.EndPoint.Y;
            double x3 = line2.StartPoint.X, y3 = line2.StartPoint.Y;
            double x4 = line2.EndPoint.X, y4 = line2.EndPoint.Y;

            double denom = (x1 - x2) * (y3 - y4) - (y1 - y2) * (x3 - x4);
            if (Math.Abs(denom) < 1e-10) return null; // 平行线

            double t = ((x1 - x3) * (y3 - y4) - (y1 - y3) * (x3 - x4)) / denom;
            double u = -((x1 - x2) * (y1 - y3) - (y1 - y2) * (x1 - x3)) / denom;

            double intersectionX = x1 + t * (x2 - x1);
            double intersectionY = y1 + t * (y2 - y1);

            return new Point(intersectionX, intersectionY);
        }

        /// <summary>
        /// 检查点是否在直线端点附近
        /// </summary>
        private bool IsPointNearLineEndpoints(Point point, RectangleGuideLine line)
        {
            double distToStart = GetDistance(point, line.StartPoint);
            double distToEnd = GetDistance(point, line.EndPoint);

            return distToStart <= RECTANGLE_ENDPOINT_THRESHOLD || distToEnd <= RECTANGLE_ENDPOINT_THRESHOLD;
        }

        /// <summary>
        /// 从四条直线创建矩形
        /// </summary>
        private void CreateRectangleFromLines(List<RectangleGuideLine> lines)
        {
            try
            {
                // 计算矩形的四个角点
                var corners = CalculateRectangleCorners(lines);
                if (corners == null || corners.Count != 4) return;

                // 创建矩形笔画
                var pointList = new List<Point>(corners) { corners[0] }; // 闭合矩形
                var point = new StylusPointCollection(pointList);
                var rectangleStroke = new Stroke(GenerateFakePressureRectangle(point))
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                rectangleStroke.AddPropertyData(ShapeStrokePropertyGuid, true);

                // 移除原有的四条直线
                SetNewBackupOfStroke();
                _currentCommitType = CommitReason.ShapeRecognition;

                foreach (var line in lines)
                {
                    if (inkCanvas.Strokes.Contains(line.OriginalStroke))
                    {
                        inkCanvas.Strokes.Remove(line.OriginalStroke);
                    }
                }

                // 添加新的矩形
                inkCanvas.Strokes.Add(rectangleStroke);
                _currentCommitType = CommitReason.UserInput;

                // 清理参考线
                foreach (var line in lines)
                {
                    rectangleGuideLines.Remove(line);
                }

                // 清空新笔画集合，避免重复处理
                newStrokes = new StrokeCollection();

                Debug.WriteLine("成功创建矩形参考线矩形");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"创建矩形时出错: {ex.Message}");
            }
        }

        /// <summary>
        /// 计算矩形的四个角点
        /// </summary>
        private List<Point> CalculateRectangleCorners(List<RectangleGuideLine> lines)
        {
            var horizontalLines = lines.Where(l => l.IsHorizontal).ToList();
            var verticalLines = lines.Where(l => l.IsVertical).ToList();

            if (horizontalLines.Count != 2 || verticalLines.Count != 2) return null;

            var corners = new List<Point>();

            // 计算四个交点
            foreach (var hLine in horizontalLines)
            {
                foreach (var vLine in verticalLines)
                {
                    var intersection = GetLineIntersection(hLine, vLine);
                    if (intersection.HasValue)
                    {
                        corners.Add(intersection.Value);
                    }
                }
            }

            if (corners.Count != 4) return null;

            // 按顺序排列角点（顺时针或逆时针）
            return SortRectangleCorners(corners);
        }

        /// <summary>
        /// 按顺序排列矩形角点
        /// </summary>
        private List<Point> SortRectangleCorners(List<Point> corners)
        {
            if (corners.Count != 4) return corners;

            // 计算中心点
            double centerX = corners.Average(p => p.X);
            double centerY = corners.Average(p => p.Y);
            var center = new Point(centerX, centerY);

            // 按角度排序
            return corners.OrderBy(p => Math.Atan2(p.Y - center.Y, p.X - center.X)).ToList();
        }

        /// <summary>
        /// 异步墨迹平滑将画布上的 <paramref name="fromStroke"/> 替换为 <paramref name="toStroke"/> 后，把手写字形替换批次里的画布引用一并迁移，使识别仍命中「原始点快照」字典项。
        /// </summary>
        private void MigrateHandwritingBeautifyCanvasStrokeReference(Stroke fromStroke, Stroke toStroke)
        {
            if (fromStroke == null || toStroke == null || ReferenceEquals(fromStroke, toStroke))
                return;
            

            if (_handwritingBeautifyInkInputByCanvasStroke.TryGetValue(fromStroke, out var inkInput))
            {
                _handwritingBeautifyInkInputByCanvasStroke.Remove(fromStroke);
                _handwritingBeautifyInkInputByCanvasStroke[toStroke] = inkInput;
            }

            for (var i = 0; i < _handwritingRecentStrokesForBeautify.Count; i++)
            {
                if (ReferenceEquals(_handwritingRecentStrokesForBeautify[i], fromStroke))
                    _handwritingRecentStrokesForBeautify[i] = toStroke;
            }
        }

        /// <summary>
        /// 收笔后：在墨迹转形状（若启用）完成之后，将笔画并入批次并启动/重置停笔防抖计时器，再于延迟后多笔合并矫正。
        /// </summary>
        private void PruneHandwritingBeautifyBatch()
        {
            for (var i = _handwritingRecentStrokesForBeautify.Count - 1; i >= 0; i--)
            {
                var s = _handwritingRecentStrokesForBeautify[i];
                if (!inkCanvas.Strokes.Contains(s))
                {
                    _handwritingBeautifyInkInputByCanvasStroke.Remove(s);
                    _handwritingRecentStrokesForBeautify.RemoveAt(i);
                }
            }
        }

        private void EnsureHandwritingBeautifyDebounceTimer()
        {
            if (_handwritingBeautifyDebounceTimer != null)
                return;
            _handwritingBeautifyDebounceTimer = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
            {
                Interval = TimeSpan.FromMilliseconds(HandwritingBeautifyDebounceMs)
            };
            _handwritingBeautifyDebounceTimer.Tick += HandwritingBeautifyDebounceTimer_Tick;
        }

        /// <summary>深拷贝点集，供手写纠正识别输入（与笔锋/二次压感合成前的画布数据一致）。</summary>
        private static StylusPointCollection CloneStylusPointCollectionForHandwritingInput(StylusPointCollection source)
        {
            if (source == null || source.Count == 0)
                return null;
            var copy = new StylusPointCollection();
            foreach (StylusPoint p in source)
                copy.Add(new StylusPoint(p.X, p.Y, p.PressureFactor));
            return copy;
        }

        /// <summary>并入批次并重置 1s 计时器（多笔需停笔满延迟后才矫正）。</summary>
        /// <param name="preBrushHandwritingPoints">笔锋与后续 InkStyle 压感合成前的点集；为 null 时识别输入与画布笔画一致（兼容旧行为）。</param>
        private void ScheduleHandwritingGlyphReplaceAfterStrokeCollected(
            Stroke strokeForBeautify,
            StylusPointCollection preBrushHandwritingPoints = null)
        {
            
            if (strokeForBeautify == null || strokeForBeautify.DrawingAttributes?.IsHighlighter == true)
                return;
            if (!inkCanvas.Strokes.Contains(strokeForBeautify))
                return;

            _handwritingBeautifyScheduleRevision++;

            if (preBrushHandwritingPoints != null && preBrushHandwritingPoints.Count > 0)
            {
                // 再拷贝一份给识别专用 Stroke，避免与外部 StylusPointCollection 或 WPF Stroke 内部共享后被改写。
                var ptsForRecognizer = CloneStylusPointCollectionForHandwritingInput(preBrushHandwritingPoints);
                if (ptsForRecognizer != null && ptsForRecognizer.Count > 0)
                {
                    _handwritingBeautifyInkInputByCanvasStroke[strokeForBeautify] = new Stroke(ptsForRecognizer)
                    {
                        DrawingAttributes = strokeForBeautify.DrawingAttributes.Clone()
                    };
                }
                else
                {
                    _handwritingBeautifyInkInputByCanvasStroke.Remove(strokeForBeautify);
                }
            }
            else
            {
                _handwritingBeautifyInkInputByCanvasStroke.Remove(strokeForBeautify);
            }

            var alreadyInBatch = false;
            foreach (Stroke x in _handwritingRecentStrokesForBeautify)
            {
                if (ReferenceEquals(x, strokeForBeautify))
                {
                    alreadyInBatch = true;
                    break;
                }
            }

            if (!alreadyInBatch)
                _handwritingRecentStrokesForBeautify.Add(strokeForBeautify);

            PruneHandwritingBeautifyBatch();
            while (_handwritingRecentStrokesForBeautify.Count > HandwritingBeautifyMaxRecentStrokes)
            {
                var evicted = _handwritingRecentStrokesForBeautify[0];
                _handwritingRecentStrokesForBeautify.RemoveAt(0);
                _handwritingBeautifyInkInputByCanvasStroke.Remove(evicted);
            }

            EnsureHandwritingBeautifyDebounceTimer();
            _handwritingBeautifyDebounceTimer.Stop();
            _handwritingBeautifyDebounceTimer.Interval = TimeSpan.FromMilliseconds(HandwritingBeautifyDebounceMs);
            _handwritingBeautifyDebounceTimer.Start();
        }

        private async void HandwritingBeautifyDebounceTimer_Tick(object sender, EventArgs e)
        {
            if (_handwritingBeautifyDebounceTimer != null)
                _handwritingBeautifyDebounceTimer.Stop();

            var revisionWhenIdle = _handwritingBeautifyScheduleRevision;
            try
            {
                await HandwritingGlyphReplaceFromBatchCoreAsync(revisionWhenIdle).ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("[手写体] 停笔防抖矫正异常: " + ex.Message, LogHelper.LogType.Warning);
                Debug.WriteLine(ex);
            }
        }

        // LTSC（2026-11-12）：手写美化（WinRT 手写体识别 + 字体轮廓转墨迹）已随 WinRT 后端一并移除。
        // 保留空方法以维持调用点结构，实际不做任何事。
        private Task HandwritingGlyphReplaceFromBatchCoreAsync(ulong revisionWhenIdle)
        {
            return Task.CompletedTask;
        }

        #endregion
    }
}
