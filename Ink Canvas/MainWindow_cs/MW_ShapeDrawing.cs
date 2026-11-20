using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        #region Floating Bar Control

        /// <summary>
        /// 处理形状绘制按钮的鼠标抬起事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当形状绘制按钮被点击时：
        /// 1. 重置之前按下的面板背景
        /// 2. 检查是否是浮动栏按钮且不是当前按下的对象
        /// 3. 如果形状绘制面板可见，则隐藏它
        /// 4. 如果形状绘制面板不可见，则显示它
        /// </remarks>
        internal void ImageDrawShape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("打开几何工具")) return;

            // 如果当前不在批注模式，先切换到批注模式
            if (!IsAnnotating)
            {
                PenIcon_Click(sender, e);
            }

            if (BorderDrawShape.IsOpen || BoardBorderDrawShape.IsOpen)
            {
                AnimationsHelper.HidePopupWithSlideAndFade(BorderDrawShape);
                AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderDrawShape);
            }
            else
            {
                HideSubPanels();
                if (currentMode == 0)
                {
                    // 如果几何按钮不在浮动栏中，PlacementTarget 可能为 null，
                    // 回退到工具按钮作为定位目标
                    if (BorderDrawShape.PlacementTarget == null ||
                        !IsPlacementTargetVisible(BorderDrawShape.PlacementTarget))
                    {
                        BorderDrawShape.PlacementTarget = ToolsFloatingBarBtn ?? ShapeDrawFloatingBarBtn;
                    }
                    // 同步圆心显示开关状态
                    if (ShapeDrawPopupContent?.ShowCircleCenterToggle != null)
                        ShapeDrawPopupContent.ShowCircleCenterToggle.IsOn = Settings.Canvas.ShowCircleCenter;
                    AnimationsHelper.ShowPopupWithSlideAndFade(BorderDrawShape);
                    _popupManager?.BringToFront(BorderDrawShape);
                }
                else
                {
                    // 白板模式：如果几何按钮不在白板工具栏中，回退到白板工具按钮
                    if (BoardBorderDrawShape.PlacementTarget == null ||
                        !IsPlacementTargetVisible(BoardBorderDrawShape.PlacementTarget))
                    {
                        var boardToolsBtn = FindView("board.tools");
                        if (boardToolsBtn != null)
                            BoardBorderDrawShape.PlacementTarget = boardToolsBtn;
                    }
                    // 同步圆心显示开关状态
                    if (BoardShapeDrawPopupContent?.ShowCircleCenterToggle != null)
                        BoardShapeDrawPopupContent.ShowCircleCenterToggle.IsOn = Settings.Canvas.ShowCircleCenter;
                    AnimationsHelper.ShowPopupWithSlideAndFade(BoardBorderDrawShape);
                    _popupManager?.BringToFront(BoardBorderDrawShape);
                }
            }
        }

        #endregion Floating Bar Control

        /// <summary>
        /// 检查 Popup 的 PlacementTarget 是否在可视树中且可见
        /// </summary>
        private bool IsPlacementTargetVisible(UIElement target)
        {
            if (target == null) return false;
            // 检查元素是否连接到实际的渲染窗口（PresentationSource）
            if (PresentationSource.FromVisual(target) == null) return false;
            if (target is FrameworkElement fe)
            {
                // 检查元素是否可见
                if (fe.Visibility != Visibility.Visible) return false;
                // 检查元素是否在屏幕上有实际尺寸
                if (fe.ActualWidth <= 0 && fe.ActualHeight <= 0) return false;
            }
            return true;
        }

        /// <summary>
        /// 形状绘制模式
        /// </summary>
        /// <remarks>
        /// 不同的值表示不同的形状绘制模式：
        /// 1: 直线
        /// 2: 箭头
        /// 3: 矩形
        /// 4: 椭圆
        /// 5: 圆形
        /// 6: 圆柱
        /// 7: 圆锥
        /// 8: 虚线
        /// 9: 长方体
        /// 10: 虚线圆形
        /// 11: 坐标系1
        /// 12: 坐标系2
        /// 13: 坐标系3
        /// 14: 坐标系4
        /// 15: 平行线
        /// 16: 中心椭圆
        /// 17: 坐标系5
        /// 18: 点线
        /// 19: 中心矩形
        /// 20: 抛物线1
        /// 21: 抛物线2
        /// 22: 带焦点的抛物线
        /// 23: 带焦点的中心椭圆
        /// 24: 双曲线
        /// 25: 带焦点的双曲线
        /// </remarks>
        private int drawingShapeMode;

        /// <summary>
        /// 用于存储是否是"选中"状态，便于后期抬笔后不做切换到笔的处理
        /// </summary>
        private bool isLongPressSelected;

        /// <summary>
        /// 处理墨水画布的触摸移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">触摸事件参数</param>
        /// <remarks>
        /// 当在墨水画布上发生触摸移动时：
        /// 1. 如果是单指拖拽模式，直接返回
        /// 2. 如果是形状绘制模式：
        ///    - 如果等待下一次触摸按下，直接返回
        ///    - 如果触摸点数量大于1，设置等待标志并清理临时笔画
        ///    - 确保墨水画布编辑模式为无
        /// 3. 调用MouseTouchMove方法处理触摸点位置
        /// </remarks>
        private void inkCanvas_TouchMove(object sender, TouchEventArgs e)
        {
            if (isSingleFingerDragMode) return;
            if (drawingShapeMode != 0)
            {
                //EraserContainer.Background = null;
                //ImageEraser.Visibility = Visibility.Visible;
                if (isWaitUntilNextTouchDown) return;
                if (dec.Count > 1)
                {
                    isWaitUntilNextTouchDown = true;
                    try
                    {
                        inkCanvas.Strokes.Remove(lastTempStroke);
                        inkCanvas.Strokes.Remove(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }

                    return;
                }

                if (inkCanvas.EditingMode != InkCanvasEditingMode.None)
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
            }

            MouseTouchMove(e.GetTouchPoint(inkCanvas).Position);
        }

        /// <summary>
        /// 多笔完成的图形 当前所处在的笔画步骤
        /// </summary>
        private int drawMultiStepShapeCurrentStep;

        /// <summary>
        /// 多笔完成的图形 当前所处在的笔画集合
        /// </summary>
        private StrokeCollection drawMultiStepShapeSpecialStrokeCollection = new StrokeCollection();

        //double drawMultiStepShapeSpecialParameter1 = 0.0; //多笔完成的图形 特殊参数 通常用于表示a
        //double drawMultiStepShapeSpecialParameter2 = 0.0; //多笔完成的图形 特殊参数 通常用于表示b

        /// <summary>
        /// 多笔完成的图形 特殊参数 通常用于表示k
        /// </summary>
        private double drawMultiStepShapeSpecialParameter3;

        /// <summary>
        /// 长方体绘制的首次触摸标志
        /// </summary>
        /// <remarks>
        /// 用于标识是否是长方体绘制的第一次触摸，第一次触摸绘制正面矩形，第二次触摸绘制深度
        /// </remarks>
        private bool isFirstTouchCuboid = true;

        /// <summary>
        /// 长方体正面矩形的起始点
        /// </summary>
        private Point CuboidFrontRectIniP;

        /// <summary>
        /// 长方体正面矩形的结束点
        /// </summary>
        private Point CuboidFrontRectEndP;

        /// <summary>
        /// 上一次的临时笔画
        /// </summary>
        /// <remarks>
        /// 用于存储当前正在绘制的临时笔画，在绘制过程中实时更新
        /// </remarks>
        private Stroke lastTempStroke;

        /// <summary>
        /// 上一次的临时笔画集合
        /// </summary>
        /// <remarks>
        /// 用于存储当前正在绘制的临时笔画集合，在绘制复杂形状时使用
        /// </remarks>
        private StrokeCollection lastTempStrokeCollection = new StrokeCollection();

        /// <summary>
        /// 是否等待下一次触摸按下
        /// </summary>
        /// <remarks>
        /// 当触摸点数量大于1时，设置此标志并清理临时笔画
        /// </remarks>
        private bool isWaitUntilNextTouchDown;

        // 添加节流机制，减少更新频率
        /// <summary>
        /// 上一次更新时间
        /// </summary>
        private DateTime lastUpdateTime = DateTime.MinValue;

        /// <summary>
        /// 更新节流时间（毫秒）
        /// </summary>
        /// <remarks>
        /// 约60fps的更新频率，用于限制UI更新频率，提高性能
        /// </remarks>
        private const int UpdateThrottleMs = 16;

        /// <summary>
        /// 安全地更新临时笔画，减少预览闪烁
        /// </summary>
        /// <param name="newStroke">新的临时笔画</param>
        private void UpdateTempStrokeSafely(Stroke newStroke)
        {
            // 节流机制：限制更新频率
            var now = DateTime.Now;
            if ((now - lastUpdateTime).TotalMilliseconds < UpdateThrottleMs)
            {
                return;
            }
            lastUpdateTime = now;

            try
            {
                // 使用Dispatcher.BeginInvoke确保UI更新在UI线程上执行
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 先添加新笔画，再删除旧笔画，减少视觉闪烁
                        inkCanvas.Strokes.Add(newStroke);

                        if (lastTempStroke != null && inkCanvas.Strokes.Contains(lastTempStroke))
                        {
                            inkCanvas.Strokes.Remove(lastTempStroke);
                        }

                        lastTempStroke = newStroke;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UpdateTempStrokeSafely 失败: {ex.Message}");
                        // 如果更新失败，确保清理状态
                        if (lastTempStroke != null && inkCanvas.Strokes.Contains(lastTempStroke))
                        {
                            try { inkCanvas.Strokes.Remove(lastTempStroke); } catch (Exception innerEx) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + innerEx.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                        }
                        lastTempStroke = newStroke;
                        try { inkCanvas.Strokes.Add(newStroke); } catch (Exception innerEx) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + innerEx.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                    }
                }), DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateTempStrokeSafely Dispatcher 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 安全地更新临时笔画集合，减少预览闪烁
        /// </summary>
        /// <param name="newStrokeCollection">新的临时笔画集合</param>
        private void UpdateTempStrokeCollectionSafely(StrokeCollection newStrokeCollection)
        {
            // 节流机制：限制更新频率
            var now = DateTime.Now;
            if ((now - lastUpdateTime).TotalMilliseconds < UpdateThrottleMs)
            {
                return;
            }
            lastUpdateTime = now;

            try
            {
                // 使用Dispatcher.BeginInvoke确保UI更新在UI线程上执行
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        // 先添加新笔画集合，再删除旧笔画集合，减少视觉闪烁
                        inkCanvas.Strokes.Add(newStrokeCollection);

                        if (lastTempStrokeCollection != null && lastTempStrokeCollection.Count > 0)
                        {
                            foreach (var stroke in lastTempStrokeCollection)
                            {
                                if (inkCanvas.Strokes.Contains(stroke))
                                {
                                    inkCanvas.Strokes.Remove(stroke);
                                }
                            }
                        }

                        lastTempStrokeCollection = newStrokeCollection;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"UpdateTempStrokeCollectionSafely 失败: {ex.Message}");
                        // 如果更新失败，确保清理状态
                        if (lastTempStrokeCollection != null && lastTempStrokeCollection.Count > 0)
                        {
                            foreach (var stroke in lastTempStrokeCollection)
                            {
                                try { inkCanvas.Strokes.Remove(stroke); } catch (Exception innerEx) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + innerEx.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                            }
                        }
                        lastTempStrokeCollection = newStrokeCollection;
                        try { inkCanvas.Strokes.Add(newStrokeCollection); } catch (Exception innerEx) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + innerEx.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                    }
                }), DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"UpdateTempStrokeCollectionSafely Dispatcher 失败: {ex.Message}");
            }
        }

        /// <summary>
        /// 生成椭圆几何图形的点列表
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <param name="isDrawTop">是否绘制上半部分</param>
        /// <param name="isDrawBottom">是否绘制下半部分</param>
        /// <returns>返回椭圆的点列表</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成椭圆的点列表：
        /// 1. 计算椭圆的半长轴和半短轴
        /// 2. 根据参数决定是否绘制上半部分和下半部分
        /// 3. 使用参数方程生成椭圆上的点
        /// </remarks>
        private List<Point> GenerateEllipseGeometry(Point st, Point ed, bool isDrawTop = true,
            bool isDrawBottom = true)
        {
            var a = 0.5 * (ed.X - st.X);
            var b = 0.5 * (ed.Y - st.Y);
            var pointList = new List<Point>();
            if (isDrawTop && isDrawBottom)
            {
                for (double r = 0; r <= 2 * Math.PI; r = r + 0.01)
                    pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                        0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
            }
            else
            {
                if (isDrawBottom)
                    for (double r = 0; r <= Math.PI; r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                if (isDrawTop)
                    for (var r = Math.PI; r <= 2 * Math.PI; r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
            }

            return pointList;
        }

        /// <summary>
        /// 生成虚线椭圆的笔画集合
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <param name="isDrawTop">是否绘制上半部分</param>
        /// <param name="isDrawBottom">是否绘制下半部分</param>
        /// <returns>返回虚线椭圆的笔画集合</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成虚线椭圆的笔画集合：
        /// 1. 计算椭圆的半长轴和半短轴
        /// 2. 根据参数决定是否绘制上半部分和下半部分
        /// 3. 使用参数方程生成椭圆上的点，并将其分割为虚线段
        /// 4. 为每个虚线段创建一个笔画
        /// </remarks>
        private StrokeCollection GenerateDashedLineEllipseStrokeCollection(Point st, Point ed, bool isDrawTop = true,
            bool isDrawBottom = true)
        {
            var a = 0.5 * (ed.X - st.X);
            var b = 0.5 * (ed.Y - st.Y);
            var step = 0.05;
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            var strokes = new StrokeCollection();
            if (isDrawBottom)
                for (var i = 0.0; i < 1.0; i += step * 1.66)
                {
                    pointList = new List<Point>();
                    for (var r = Math.PI * i; r <= Math.PI * (i + step); r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                }

            if (isDrawTop)
                for (var i = 1.0; i < 2.0; i += step * 1.66)
                {
                    pointList = new List<Point>();
                    for (var r = Math.PI * i; r <= Math.PI * (i + step); r = r + 0.01)
                        pointList.Add(new Point(0.5 * (st.X + ed.X) + a * Math.Cos(r),
                            0.5 * (st.Y + ed.Y) + b * Math.Sin(r)));
                    point = new StylusPointCollection(pointList);
                    stroke = new Stroke(point)
                    {
                        DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                    };
                    strokes.Add(stroke.Clone());
                }

            return strokes;
        }

        private const double CoordinateCentimeterUnitLength = 37.7952755906;

        private void AddCoordinateUnitMarks(StrokeCollection strokes, Point origin, Point xAxisStart, Point xAxisEnd,
            Point yAxisStart, Point yAxisEnd, Point? zAxisEnd = null)
        {
            if (!Settings.Canvas.ShowCoordinateUnitMarks) return;

            AddCoordinateAxisUnitMarks(strokes, origin, xAxisStart, xAxisEnd, CoordinateCentimeterUnitLength, new Vector(0, 1));
            AddCoordinateAxisUnitMarks(strokes, origin, yAxisStart, yAxisEnd, CoordinateCentimeterUnitLength, new Vector(1, 0));
            if (zAxisEnd is Point zEnd)
                AddCoordinateAxisUnitMarks(strokes, origin, origin, zEnd, CoordinateCentimeterUnitLength, new Vector(1, 1));
        }

        private void AddCoordinateAxisUnitMarks(StrokeCollection strokes, Point origin, Point axisStart, Point axisEnd,
            double unitLength, Vector tickDirection)
        {
            var axisVector = axisEnd - origin;
            var axisLength = axisVector.Length;
            if (axisLength < 0.01) return;

            axisVector.Normalize();
            if (tickDirection.Length < 0.01)
                tickDirection = new Vector(-axisVector.Y, axisVector.X);
            tickDirection.Normalize();

            for (var distance = unitLength; distance <= axisLength; distance += unitLength)
            {
                var positiveUnitPoint = origin + axisVector * distance;
                if (IsPointOnSegment(positiveUnitPoint, axisStart, axisEnd))
                    strokes.Add(GenerateLineStroke(positiveUnitPoint - tickDirection * 5, positiveUnitPoint + tickDirection * 5));

                var negativeUnitPoint = origin - axisVector * distance;
                if (IsPointOnSegment(negativeUnitPoint, axisStart, axisEnd))
                    strokes.Add(GenerateLineStroke(negativeUnitPoint - tickDirection * 5, negativeUnitPoint + tickDirection * 5));
            }
        }

        private bool IsPointOnSegment(Point point, Point segmentStart, Point segmentEnd)
        {
            const double tolerance = 0.5;
            return point.X >= Math.Min(segmentStart.X, segmentEnd.X) - tolerance &&
                   point.X <= Math.Max(segmentStart.X, segmentEnd.X) + tolerance &&
                   point.Y >= Math.Min(segmentStart.Y, segmentEnd.Y) - tolerance &&
                   point.Y <= Math.Max(segmentStart.Y, segmentEnd.Y) + tolerance;
        }

        /// <summary>
        /// 生成直线笔画
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回直线笔画</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成直线笔画：
        /// 1. 创建包含起始点和结束点的点列表
        /// 2. 将点列表转换为StylusPointCollection
        /// 3. 创建并返回带有默认绘图属性的笔画
        /// </remarks>
        private Stroke GenerateLineStroke(Point st, Point ed)
        {
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            pointList = new List<Point> {
                new Point(st.X, st.Y),
                new Point(ed.X, ed.Y)
            };
            point = new StylusPointCollection(pointList);
            stroke = new Stroke(point)
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            return stroke;
        }

        /// <summary>
        /// 生成带箭头的直线笔画
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回带箭头的直线笔画</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成带箭头的直线笔画：
        /// 1. 计算箭头的角度和方向
        /// 2. 创建包含起始点、结束点和箭头尖端的点列表
        /// 3. 将点列表转换为StylusPointCollection
        /// 4. 创建并返回带有默认绘图属性的笔画
        /// </remarks>
        private Stroke GenerateArrowLineStroke(Point st, Point ed)
        {
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;

            double w = 20, h = 7;
            var theta = Math.Atan2(st.Y - ed.Y, st.X - ed.X);
            var sint = Math.Sin(theta);
            var cost = Math.Cos(theta);

            pointList = new List<Point> {
                new Point(st.X, st.Y),
                new Point(ed.X, ed.Y),
                new Point(ed.X + (w * cost - h * sint), ed.Y + (w * sint + h * cost)),
                new Point(ed.X, ed.Y),
                new Point(ed.X + (w * cost + h * sint), ed.Y - (h * cost - w * sint))
            };
            point = new StylusPointCollection(pointList);
            stroke = new Stroke(point)
            {
                DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
            };
            return stroke;
        }


        /// <summary>
        /// 生成虚线笔画集合
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回虚线笔画集合</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成虚线笔画集合：
        /// 1. 计算两点之间的距离和方向
        /// 2. 按照指定的步长将直线分割为虚线段
        /// 3. 为每个虚线段创建一个笔画
        /// 4. 返回包含所有虚线段笔画的集合
        /// </remarks>
        private StrokeCollection GenerateDashedLineStrokeCollection(Point st, Point ed)
        {
            double step = 5;
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            var strokes = new StrokeCollection();
            var d = GetDistance(st, ed);
            var sinTheta = (ed.Y - st.Y) / d;
            var cosTheta = (ed.X - st.X) / d;
            for (var i = 0.0; i < d; i += step * 2.76)
            {
                pointList = new List<Point> {
                    new Point(st.X + i * cosTheta, st.Y + i * sinTheta),
                    new Point(st.X + Math.Min(i + step, d) * cosTheta, st.Y + Math.Min(i + step, d) * sinTheta)
                };
                point = new StylusPointCollection(pointList);
                stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                strokes.Add(stroke.Clone());
            }

            return strokes;
        }

        /// <summary>
        /// 生成点线笔画集合
        /// </summary>
        /// <param name="st">起始点坐标</param>
        /// <param name="ed">结束点坐标</param>
        /// <returns>返回点线笔画集合</returns>
        /// <remarks>
        /// 根据给定的起始点和结束点，生成点线笔画集合：
        /// 1. 计算两点之间的距离和方向
        /// 2. 按照指定的步长在直线上生成点
        /// 3. 为每个点创建一个笔画
        /// 4. 返回包含所有点笔画的集合
        /// </remarks>
        private StrokeCollection GenerateDotLineStrokeCollection(Point st, Point ed)
        {
            double step = 3;
            var pointList = new List<Point>();
            StylusPointCollection point;
            Stroke stroke;
            var strokes = new StrokeCollection();
            var d = GetDistance(st, ed);
            var sinTheta = (ed.Y - st.Y) / d;
            var cosTheta = (ed.X - st.X) / d;
            for (var i = 0.0; i < d; i += step * 2.76)
            {
                var stylusPoint = new StylusPoint(st.X + i * cosTheta, st.Y + i * sinTheta, (float)0.8);
                point = new StylusPointCollection();
                point.Add(stylusPoint);
                stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };
                strokes.Add(stroke.Clone());
            }

            return strokes;
        }

        /// <summary>
        /// 鼠标按下状态标志
        /// </summary>
        /// <remarks>
        /// 用于标识鼠标是否处于按下状态，在绘制过程中使用
        /// </remarks>
        private bool isMouseDown;
        private bool _isMouseRealtimeInking;

        /// <summary>
        /// 触摸按下状态标志
        /// </summary>
        /// <remarks>
        /// 用于标识触摸是否处于按下状态，在绘制过程中使用
        /// </remarks>
        private bool isTouchDown;

        /// <summary>
        /// 处理墨水画布的鼠标按下事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当在墨水画布上按下鼠标时：
        /// 1. 捕获鼠标输入
        /// 2. 禁用浮动栏和黑板UI的命中测试，避免干扰绘制
        /// 3. 设置鼠标按下状态标志
        /// 4. 如果需要更新起始点，则更新起始点为当前鼠标位置
        /// </remarks>
        private void inkCanvas_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // LTSC（2026-11-20 第十轮）：班测机是远程控制（注入鼠标事件），
            // 不走 Touch 那条路。所以两个入口都要留痕，才能知道实际命中哪条。
            try
            {
                LogHelper.WriteLogToFile(
                    $"[鼠标入口] inkCanvas_MouseDown：EditingMode={inkCanvas.EditingMode} " +
                    $"currentToolMode={_currentToolMode} 命中={inkCanvas.IsHitTestVisible} " +
                    $"可见={inkCanvas.Visibility} 实时笔刷={ShouldUseRealtimeVelocityBrushTipForMouse()}",
                    LogHelper.LogType.Event);
            }
            catch { }

            if (TryBlockInkInputOverFloatingBar(e.GetPosition(this), e))
                return;

            if (e.ChangedButton == MouseButton.Left && ShouldUseRealtimeVelocityBrushTipForMouse() && drawingShapeMode == 0)
            {
                _isMouseRealtimeInking = true;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                var p = e.GetPosition(inkCanvas);
                CancelPauseStraightenTimer(MouseRealtimeStrokeId);
                InitializeRealtimeBrushTipStateFromPoint(MouseRealtimeStrokeId, p);
                var sv = GetStrokeVisual(MouseRealtimeStrokeId);
                TryAppendRealtimeVelocityBrushTipPoint(sv, MouseRealtimeStrokeId, p);
                sv.ForceRedraw();
            }

            inkCanvas.CaptureMouse();
            ViewboxFloatingBar.IsHitTestVisible = false;
            BlackboardUIGridForInkReplay.IsHitTestVisible = false;

            isMouseDown = true;
            if (NeedUpdateIniP()) iniP = e.GetPosition(inkCanvas);
        }

        /// <summary>
        /// 处理墨水画布的鼠标移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标事件参数</param>
        /// <remarks>
        /// 当在墨水画布上移动鼠标时：
        /// 1. 如果鼠标处于按下状态，调用MouseTouchMove方法处理移动
        /// 2. 如果启用了光标显示，根据编辑模式设置光标
        /// </remarks>
        private void inkCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isMouseRealtimeInking && isMouseDown)
            {
                var sv = GetStrokeVisual(MouseRealtimeStrokeId);
                if (TryAppendRealtimeVelocityBrushTipPoint(sv, MouseRealtimeStrokeId, e.GetPosition(inkCanvas)))
                {
                    sv.ForceRedraw();
                    ResetPauseStraightenTimer(MouseRealtimeStrokeId);
                }
                else
                {
                    _isMouseRealtimeInking = false;
                    MouseTouchMove(e.GetPosition(inkCanvas));
                }
            }
            else if (isMouseDown)
            {
                MouseTouchMove(e.GetPosition(inkCanvas));
            }

            if (Settings.Canvas.IsShowCursor)
            {
                SetCursorBasedOnEditingMode(inkCanvas);
            }
        }

        /// <summary>
        /// 处理墨水画布的鼠标抬起事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当在墨水画布上抬起鼠标时：
        /// 1. 释放鼠标捕获
        /// 2. 启用浮动栏和黑板UI的命中测试
        /// 3. 根据不同的绘制模式进行处理：
        ///    - 圆形模式：创建圆形对象并添加到圆形集合
        ///    - 长方体模式：处理长方体绘制的两个步骤
        ///    - 双曲线模式：处理多步绘制的步骤切换
        /// 4. 还原到笔模式（除特殊情况外）
        /// 5. 提交笔画历史记录
        /// 6. 清理临时笔画和状态
        /// 7. 恢复FitToCurve设置
        /// </remarks>
        private void inkCanvas_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isMouseRealtimeInking)
            {
                try
                {
                    var sv = GetStrokeVisual(MouseRealtimeStrokeId);
                    sv?.ForceRedraw();
                    var stroke = sv?.Stroke;
                    if (stroke != null)
                    {
                        if (!stroke.ContainsPropertyData(RealtimeVelocityBrushTipAppliedGuid))
                            stroke.AddPropertyData(RealtimeVelocityBrushTipAppliedGuid, true);
                        inkCanvas.Strokes.Add(stroke);
                        inkCanvas_StrokeCollected(inkCanvas, new InkCanvasStrokeCollectedEventArgs(stroke));
                    }
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                finally
                {
                    if (VisualCanvasList.TryGetValue(MouseRealtimeStrokeId, out var vc) && inkCanvas.Children.Contains(vc))
                        inkCanvas.Children.Remove(vc);
                    StrokeVisualList.Remove(MouseRealtimeStrokeId);
                    VisualCanvasList.Remove(MouseRealtimeStrokeId);
                    TouchDownPointsList.Remove(MouseRealtimeStrokeId);
                    CleanupRealtimeBrushTipState(MouseRealtimeStrokeId);
                    CancelPauseStraightenTimer(MouseRealtimeStrokeId);
                    _isMouseRealtimeInking = false;
                }
            }

            HandleEraserOperationEnded();
            inkCanvas.ReleaseMouseCapture();
            ViewboxFloatingBar.IsHitTestVisible = true;
            BlackboardUIGridForInkReplay.IsHitTestVisible = true;

            if (drawingShapeMode == 5)
            {
                if (lastTempStroke != null)
                {
                    var circle = new Circle(new Point(), 0, lastTempStroke);
                    circle.R = GetDistance(circle.Stroke.StylusPoints[0].ToPoint(),
                        circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].ToPoint()) / 2;
                    circle.Centroid = new Point(
                        (circle.Stroke.StylusPoints[0].X +
                         circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].X) / 2,
                        (circle.Stroke.StylusPoints[0].Y +
                         circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].Y) / 2);
                    circles.Add(circle);
                }

                if (lastIsInMultiTouchMode)
                {
                    ToggleSwitchEnableMultiTouchMode.IsOn = true;
                    lastIsInMultiTouchMode = false;
                }
            }

            if (drawingShapeMode != 9 && drawingShapeMode != 0 && drawingShapeMode != 24 && drawingShapeMode != 25)
            {
                if (isLongPressSelected) { }
                else
                {
                    BtnPen_Click(null, null); //画完一次还原到笔模式
                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }
                }
            }

            if (drawingShapeMode == 9)
            {
                if (isFirstTouchCuboid)
                {
                    if (CuboidStrokeCollection == null) CuboidStrokeCollection = new StrokeCollection();
                    isFirstTouchCuboid = false;
                    var newIniP = new Point(Math.Min(CuboidFrontRectIniP.X, CuboidFrontRectEndP.X),
                        Math.Min(CuboidFrontRectIniP.Y, CuboidFrontRectEndP.Y));
                    var newEndP = new Point(Math.Max(CuboidFrontRectIniP.X, CuboidFrontRectEndP.X),
                        Math.Max(CuboidFrontRectIniP.Y, CuboidFrontRectEndP.Y));
                    CuboidFrontRectIniP = newIniP;
                    CuboidFrontRectEndP = newEndP;
                    try
                    {
                        CuboidStrokeCollection.Add(lastTempStrokeCollection);
                    }
                    catch
                    {
                        Trace.WriteLine("lastTempStrokeCollection failed.");
                    }
                }
                else
                {
                    BtnPen_Click(null, null); //画完还原到笔模式
                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }

                    if (_currentCommitType == CommitReason.ShapeDrawing)
                    {
                        try
                        {
                            CuboidStrokeCollection.Add(lastTempStrokeCollection);
                        }
                        catch
                        {
                            Trace.WriteLine("lastTempStrokeCollection failed.");
                        }

                        _currentCommitType = CommitReason.UserInput;
                        timeMachine.CommitStrokeUserInputHistory(CuboidStrokeCollection);
                        CuboidStrokeCollection = null;
                    }
                }
            }

            if (drawingShapeMode == 24 || drawingShapeMode == 25)
            {
                if (drawMultiStepShapeCurrentStep == 0)
                {
                    drawMultiStepShapeCurrentStep = 1;
                }
                else
                {
                    drawMultiStepShapeCurrentStep = 0;
                    if (drawMultiStepShapeSpecialStrokeCollection != null)
                    {
                        var opFlag = false;
                        switch (Settings.Canvas.HyperbolaAsymptoteOption)
                        {
                            case OptionalOperation.Yes:
                                opFlag = true;
                                break;
                            case OptionalOperation.No:
                                opFlag = false;
                                break;
                            case OptionalOperation.Ask:
                                opFlag = MessageBox.Show(Properties.CanvasStrings.Shape_RemoveAsymptote, "Ink Canvas", MessageBoxButton.YesNo) !=
                                         MessageBoxResult.Yes;
                                break;
                        }

                        ;
                        if (!opFlag) inkCanvas.Strokes.Remove(drawMultiStepShapeSpecialStrokeCollection);
                    }

                    BtnPen_Click(null, null); //画完还原到笔模式
                    if (lastIsInMultiTouchMode)
                    {
                        ToggleSwitchEnableMultiTouchMode.IsOn = true;
                        lastIsInMultiTouchMode = false;
                    }
                }
            }

            isMouseDown = false;
            if (ReplacedStroke != null || AddedStroke != null)
            {
                timeMachine.CommitStrokeEraseHistory(ReplacedStroke, AddedStroke);
                AddedStroke = null;
                ReplacedStroke = null;
            }

            if (_currentCommitType == CommitReason.ShapeDrawing && drawingShapeMode != 9)
            {
                _currentCommitType = CommitReason.UserInput;
                StrokeCollection collection = null;
                if (lastTempStrokeCollection != null && lastTempStrokeCollection.Count > 0)
                    collection = lastTempStrokeCollection;
                else if (lastTempStroke != null) collection = new StrokeCollection { lastTempStroke };
                if (collection != null) timeMachine.CommitStrokeUserInputHistory(collection);
            }

            lastTempStroke = null;
            lastTempStrokeCollection = null;

            if (StrokeManipulationHistory?.Count > 0)
            {
                timeMachine.CommitStrokeManipulationHistory(StrokeManipulationHistory);
                foreach (var item in StrokeManipulationHistory)
                {
                    StrokeInitialHistory[item.Key] = item.Value.Item2;
                }
                StrokeManipulationHistory = null;
            }

            if (DrawingAttributesHistory.Count > 0)
            {
                timeMachine.CommitStrokeDrawingAttributesHistory(DrawingAttributesHistory);
                DrawingAttributesHistory = new Dictionary<Stroke, Tuple<DrawingAttributes, DrawingAttributes>>();
                foreach (var item in DrawingAttributesHistoryFlag)
                {
                    item.Value.Clear();
                }
            }

            if (Settings.Canvas.FitToCurve == true) drawingAttributes.FitToCurve = true;
        }

        /// <summary>
        /// 检查是否需要更新起始点
        /// </summary>
        /// <returns>返回是否需要更新起始点</returns>
        /// <remarks>
        /// 检查当前绘制模式和步骤，判断是否需要更新起始点：
        /// 1. 对于双曲线模式（24和25），如果是第二笔（步骤1），则不更新起点
        /// 2. 其他情况都需要更新起点
        /// </remarks>
        private bool NeedUpdateIniP()
        {
            if (drawingShapeMode == 24 || drawingShapeMode == 25)
            {
                if (drawMultiStepShapeCurrentStep == 1)
                    return false; // 第二笔不更新起点
            }
            return true;
        }
        /// <summary>
        /// 绘制圆心标记
        /// </summary>
        /// <param name="centerPoint">圆心位置</param>
        private void DrawCircleCenter(Point centerPoint)
        {
            try
            {
                // 创建一个点作为圆心标记
                var centerSize = 0.5; // 圆心标记的大小

                // 创建一个小圆作为圆心标记
                var circlePoints = new List<Point>();
                for (double angle = 0; angle <= 2 * Math.PI; angle += 0.1)
                {
                    circlePoints.Add(new Point(
                        centerPoint.X + centerSize * Math.Cos(angle),
                        centerPoint.Y + centerSize * Math.Sin(angle)
                    ));
                }

                // 绘制圆心点
                var point = new StylusPointCollection(circlePoints);
                var stroke = new Stroke(point)
                {
                    DrawingAttributes = inkCanvas.DefaultDrawingAttributes.Clone()
                };

                // 设置圆心点的样式
                stroke.DrawingAttributes.Width = 2.0;
                stroke.DrawingAttributes.Height = 2.0;

                // 添加到画布
                inkCanvas.Strokes.Add(stroke);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"绘制圆心标记失败: {ex.Message}");
            }
        }
        /// <summary>
        /// 处理主窗口的鼠标移动事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标事件参数</param>
        /// <remarks>
        /// 当在主窗口上移动鼠标时：
        /// 1. 如果启用了光标显示，则显示光标并根据编辑模式设置光标
        /// 2. 如果禁用了光标显示：
        ///    - 如果没有触笔设备，则显示光标
        ///    - 如果有触笔设备，则隐藏光标
        /// </remarks>
        private void MainWindow_OnMouseMove(object sender, MouseEventArgs e)
        {
            if (Settings.Canvas.IsShowCursor)
            {
                System.Windows.Forms.Cursor.Show();
                SetCursorBasedOnEditingMode(inkCanvas);
            }
            else
            {
                if (e.StylusDevice == null)
                {
                    System.Windows.Forms.Cursor.Show();
                }
                else
                {
                    System.Windows.Forms.Cursor.Hide();
                }
            }
        }
    }
}
