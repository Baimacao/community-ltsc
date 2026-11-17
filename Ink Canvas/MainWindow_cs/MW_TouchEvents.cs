using Ink_Canvas.Controls;
using Ink_Canvas.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        #region Multi-Touch

        /// <summary>
        /// 是否处于多点触控模式
        /// </summary>
        private bool isInMultiTouchMode;
        /// <summary>
        /// 存储触摸设备ID的列表
        /// </summary>
        private List<int> dec = new List<int>();
        /// <summary>
        /// 是否处于单指拖动模式
        /// </summary>
        private bool isSingleFingerDragMode;
        /// <summary>
        /// 中心点坐标
        /// </summary>
        private Point centerPoint = new Point(0, 0);
        /// <summary>
        /// 上次的InkCanvas编辑模式
        /// </summary>
        private InkCanvasEditingMode lastInkCanvasEditingMode = InkCanvasEditingMode.Ink;
        /// <summary>
        /// 上次触摸按下的时间
        /// </summary>
        private DateTime lastTouchDownTime = DateTime.MinValue;
        /// <summary>
        /// 多点触控延迟时间（毫秒）
        /// </summary>
        private const double MULTI_TOUCH_DELAY_MS = 100;
        private bool isMultiTouchTimerActive;
        /// <summary>P2 诊断：上一次 ReleaseAllTouchCaptures() 放掉了几个捕获。</summary>
        private int _releasedCaptureCount = 0;

        /// <summary>
        /// P2：每个触摸 id 最后一次活动时间。
        /// 用来清掉收不到 TouchUp 的僵尸 id——那正是双指手势失效的根因。
        /// </summary>
        private readonly Dictionary<int, DateTime> _touchIdStamp = new Dictionary<int, DateTime>();

        /// <summary>超过这个秒数没动静的 id 视为僵尸，直接清掉。</summary>
        private const double StaleTouchSeconds = 15.0;
        private bool isPalmEraserActive;
        private bool palmEraserWasEnabledBeforeMultiTouch;
        private InkCanvasEditingMode palmEraserPreviousEditingMode = InkCanvasEditingMode.Ink;
        private readonly Dictionary<int, RealtimeBrushTipState> _realtimeBrushTipStates = new Dictionary<int, RealtimeBrushTipState>();
        private readonly Guid RealtimeVelocityBrushTipAppliedGuid = new Guid("74E57D95-945F-4A8C-B52A-7D3EF2D4FD5B");
        internal const int MouseRealtimeStrokeId = -100001;
        private readonly HashSet<int> _activeRealtimeTouchStrokeIds = new HashSet<int>();
        private readonly HashSet<int> _activeRealtimeStylusStrokeIds = new HashSet<int>();
        private readonly HashSet<int> _activeTouchStrokeIds = new HashSet<int>();
        private bool _isInkCanvasManipulationEnabledBeforeTouchInk;
        private bool _hasStoredInkCanvasManipulationStateForTouchInk;

        private readonly Dictionary<int, DispatcherTimer> _pauseStraightenTimers = new Dictionary<int, DispatcherTimer>();
        private const int PauseStraightenDelayMs = 300;

        private sealed class OneEuroFilter
        {
            private readonly float _minCutoff;
            private readonly float _beta;
            private readonly float _dCutoff;
            private bool _initialized;
            private float _xPrev;
            private float _dxPrev;

            public OneEuroFilter(float minCutoff, float beta, float dCutoff)
            {
                _minCutoff = minCutoff;
                _beta = beta;
                _dCutoff = dCutoff;
            }

            public float Filter(float value, float dt, float speed)
            {
                if (!_initialized)
                {
                    _initialized = true;
                    _xPrev = value;
                    _dxPrev = 0f;
                    return value;
                }

                var dx = (value - _xPrev) / Math.Max(1e-6f, dt);
                var aD = Alpha(_dCutoff, dt);
                var dxHat = Lerp(_dxPrev, dx, aD);
                var a = Alpha(_minCutoff + _beta * speed, dt);
                var xHat = Lerp(_xPrev, value, a);
                _xPrev = xHat;
                _dxPrev = dxHat;
                return xHat;
            }

            private static float Alpha(float cutoff, float dt)
            {
                var tau = 1f / (2f * (float)Math.PI * Math.Max(1e-3f, cutoff));
                return 1f / (1f + tau / Math.Max(1e-6f, dt));
            }

            private static float Lerp(float a, float b, float t) => a + (b - a) * t;
        }

        private sealed class RealtimeBrushTipState
        {
            public float LastRawX { get; set; }
            public float LastRawY { get; set; }
            public long LastTimestampMs { get; set; }
            public float SmoothedSampleRateHz { get; set; } = 120f;
            public bool SawPressureVariation { get; set; }
            public bool HasSeed { get; set; }
            public bool HasTouchPoint { get; set; }
            public Point PreviousTouchPoint { get; set; }
            public Point LastTouchPoint { get; set; }
            public bool HasTouchDirection { get; set; }
            public Vector LastTouchDirection { get; set; }
            public float LastSmoothX { get; set; }
            public float LastSmoothY { get; set; }
            public float LastSmoothPressure { get; set; } = 0.5f;
            public OneEuroFilter FilterX { get; } = new OneEuroFilter(1.2f, 0.015f, 1f);
            public OneEuroFilter FilterY { get; } = new OneEuroFilter(1.2f, 0.015f, 1f);
            public OneEuroFilter FilterPressure { get; } = new OneEuroFilter(1f, 0.02f, 1f);
        }

        private void BorderMultiTouchMode_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (isInMultiTouchMode)
            {
                inkCanvas.StylusDown -= MainWindow_StylusDown;
                inkCanvas.StylusMove -= MainWindow_StylusMove;
                inkCanvas.StylusUp -= MainWindow_StylusUp;
                inkCanvas.TouchDown -= MainWindow_TouchDown;
                inkCanvas.TouchDown += Main_Grid_TouchDown;
                if (inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                    && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }
                // 保存非笔画元素（如图片）
                var preservedElements = PreserveNonStrokeElements();
                inkCanvas.Children.Clear();
                // 恢复非笔画元素
                RestoreNonStrokeElements(preservedElements);
                isInMultiTouchMode = false;

                if (palmEraserWasEnabledBeforeMultiTouch)
                {
                    Settings.Canvas.EnablePalmEraser = true;
                    SaveSettingsToFile();
                }
            }
            else
            {

                inkCanvas.StylusDown += MainWindow_StylusDown;
                inkCanvas.StylusMove += MainWindow_StylusMove;
                inkCanvas.StylusUp += MainWindow_StylusUp;
                inkCanvas.TouchDown += MainWindow_TouchDown;
                inkCanvas.TouchDown -= Main_Grid_TouchDown;
                if (inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                    && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                }
                // 保存非笔画元素（如图片）
                var preservedElements = PreserveNonStrokeElements();
                inkCanvas.Children.Clear();
                // 恢复非笔画元素
                RestoreNonStrokeElements(preservedElements);
                isInMultiTouchMode = true;

                palmEraserWasEnabledBeforeMultiTouch = Settings.Canvas.EnablePalmEraser;
                Settings.Canvas.EnablePalmEraser = false;
                SaveSettingsToFile();
            }
        }

        /// <summary>
        /// 主窗口的触摸按下事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">触摸事件参数</param>
        /// <remarks>
        /// 处理触摸按下事件，包括以下逻辑：
        /// 1. 如果当前处于橡皮擦模式或选择模式，则直接返回
        /// 2. 如果当前没有隐藏子面板，则隐藏子面板
        /// 3. 如果当前处于图形绘制模式，则：
        ///    - 设置InkCanvas编辑模式为None
        ///    - 设置触摸状态为按下
        ///    - 禁用浮动栏和黑板UI网格的命中测试
        ///    - 设置起始点坐标
        ///    - 直接返回
        /// 4. 否则，设置触摸按下点的编辑模式为None
        /// 5. 如果当前不是橡皮擦模式，则设置InkCanvas编辑模式为None
        /// </remarks>
        private void MainWindow_TouchDown(object sender, TouchEventArgs e)
        {

            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

            if (!isHidingSubPanelsWhenInking)
            {
                isHidingSubPanelsWhenInking = true;
                HideSubPanels(); // 书写时自动隐藏二级菜单
            }

            if (drawingShapeMode != 0)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.None;

                isTouchDown = true;
                ViewboxFloatingBar.IsHitTestVisible = false;
                BlackboardUIGridForInkReplay.IsHitTestVisible = false;

                // 设置起始点
                if (NeedUpdateIniP()) iniP = e.GetTouchPoint(inkCanvas).Position;

                return;
            }

            // 只保留普通橡皮逻辑
            TouchDownPointsList[e.TouchDevice.Id] = InkCanvasEditingMode.None;
            if (inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
            }
        }

        /// <summary>
        /// 主窗口的手写笔按下事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">手写笔按下事件参数</param>
        /// <remarks>
        /// 处理手写笔按下事件，包括以下逻辑：
        /// 1. 检查手写笔点击是否发生在浮动栏区域，如果是则允许事件传播到浮动栏按钮并返回
        /// 2. 根据手写笔是否倒置自动切换橡皮擦/画笔模式：
        ///    - 如果手写笔倒置，设置编辑模式为EraseByPoint
        ///    - 如果手写笔正常：
        ///       - 如果当前处于图形绘制模式，设置编辑模式为None，设置触摸状态为按下，禁用浮动栏和黑板UI网格的命中测试，设置起始点坐标并返回
        ///       - 如果当前不是线擦模式，设置编辑模式为Ink
        ///       - 否则，保持当前线擦模式
        /// 3. 捕获手写笔输入
        /// 4. 禁用浮动栏和黑板UI网格的命中测试
        /// 5. 根据编辑模式设置光标
        /// 6. 如果当前处于橡皮擦模式或选择模式，则直接返回
        /// 7. 设置触摸按下点的编辑模式为None
        /// </remarks>
        private void MainWindow_StylusDown(object sender, StylusDownEventArgs e)
        {
            if (IsTouchStylusDevice(e.StylusDevice))
                return;

            // 检查手写笔点击是否发生在浮动栏区域，如果是则允许事件传播到浮动栏按钮
            var stylusPoint = e.GetPosition(this);
            if (TryBlockInkInputOverFloatingBar(stylusPoint, e))
                return;

            if (IsCurrentPageFrozen)
            {
                TryBlockFrozenPageMutation("书写或擦除");
                e.Handled = true;
                return;
            }


            // 根据是否为笔尾自动切换橡皮擦/画笔模式
            if (e.StylusDevice.Inverted)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
            }
            else
            {
                if (drawingShapeMode != 0)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;

                    isTouchDown = true;
                    ViewboxFloatingBar.IsHitTestVisible = false;
                    BlackboardUIGridForInkReplay.IsHitTestVisible = false;

                    // 设置起始点
                    if (NeedUpdateIniP()) iniP = e.GetPosition(inkCanvas);

                    return;
                }
                if (inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke)
                {
                    inkCanvas.EditingMode = ShouldUseRealtimeVelocityBrushTip()
                        ? InkCanvasEditingMode.None
                        : InkCanvasEditingMode.Ink;
                }
                else
                {
                    LogHelper.WriteLogToFile("保持当前线擦模式");
                }
            }
            inkCanvas.CaptureStylus();
            ViewboxFloatingBar.IsHitTestVisible = false;
            BlackboardUIGridForInkReplay.IsHitTestVisible = false;

            SetCursorBasedOnEditingMode(inkCanvas);

            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke
                || inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;

            var stylusId = e.StylusDevice.Id;
            if (ShouldUseRealtimeVelocityBrushTip()
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke
                && inkCanvas.EditingMode != InkCanvasEditingMode.Select)
            {
                if (VisualCanvasList.TryGetValue(stylusId, out var staleCanvas) && inkCanvas.Children.Contains(staleCanvas))
                    inkCanvas.Children.Remove(staleCanvas);
                StrokeVisualList.Remove(stylusId);
                VisualCanvasList.Remove(stylusId);
                TouchDownPointsList.Remove(stylusId);
                CleanupRealtimeBrushTipState(stylusId);

                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.CaptureStylus();
                ViewboxFloatingBar.IsHitTestVisible = false;
                BlackboardUIGridForInkReplay.IsHitTestVisible = false;
                SetCursorBasedOnEditingMode(inkCanvas);

                var p = e.GetPosition(inkCanvas);
                _activeRealtimeStylusStrokeIds.Add(stylusId);
                BeginTouchInkInput();
                CancelPauseStraightenTimer(stylusId);
                InitializeRealtimeBrushTipState(stylusId, e);
                var sv = GetStrokeVisual(stylusId);
                TryAppendRealtimeVelocityBrushTipInterpolatedPoints(sv, stylusId, p);
                sv.Redraw();
                _pauseStraightenInkModeStartPos = p;
                _pauseStraightenInkModeTracking = true;
                TouchDownPointsList[stylusId] = InkCanvasEditingMode.None;
                e.Handled = true;
                return;
            }

            InitializeRealtimeBrushTipState(e.StylusDevice.Id, e);
            CancelPauseStraightenTimer(e.StylusDevice.Id);
            _pauseStraightenInkModeStartPos = e.GetPosition(inkCanvas);
            _pauseStraightenInkModeTracking = true;
            TouchDownPointsList[e.StylusDevice.Id] = InkCanvasEditingMode.None;
        }

        /// <summary>
        /// 主窗口的手写笔抬起事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">手写笔事件参数</param>
        /// <remarks>
        /// 处理手写笔抬起事件，包括以下逻辑：
        /// 1. 如果当前处于图形绘制模式：
        ///    - 重置触摸状态
        ///    - 启用浮动栏和黑板UI网格的命中测试
        ///    - 对于双曲线等需要多步绘制的图形，根据当前步骤决定是进入下一步还是完成绘制
        ///    - 对于其他单步绘制的图形，直接完成绘制
        ///    - 直接返回
        /// 2. 否则，尝试获取并处理笔画：
        ///    - 获取笔画视觉对象的笔画
        ///    - 如果笔画不为空，将其添加到InkCanvas，移除视觉画布，并触发笔画收集事件
        ///    - 如果笔画为空，仅移除视觉画布
        /// 3. 清理相关资源：
        ///    - 从StrokeVisualList、VisualCanvasList和TouchDownPointsList中移除当前手写笔设备ID
        ///    - 如果列表为空，清除所有手写笔预览相关的Canvas并清空列表
        /// 4. 释放手写笔捕获
        /// 5. 启用浮动栏和黑板UI网格的命中测试
        /// 6. 根据编辑模式设置光标
        /// </remarks>
        private async void MainWindow_StylusUp(object sender, StylusEventArgs e)
        {
            if (IsTouchStylusDevice(e.StylusDevice))
                return;

            var stylusId = e.StylusDevice.Id;
            if (_activeRealtimeStylusStrokeIds.Contains(stylusId))
            {
                try
                {
                    var sv = GetStrokeVisual(stylusId);
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
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"MainWindow_StylusUp 实时笔锋出错: {ex}", LogHelper.LogType.Error);
                    Label.Content = ex.ToString();
                }
                finally
                {
                    if (VisualCanvasList.TryGetValue(stylusId, out var visualCanvas) && inkCanvas.Children.Contains(visualCanvas))
                        inkCanvas.Children.Remove(visualCanvas);
                    StrokeVisualList.Remove(stylusId);
                    VisualCanvasList.Remove(stylusId);
                    TouchDownPointsList.Remove(stylusId);
                    CleanupRealtimeBrushTipState(stylusId);
                    CancelPauseStraightenTimer(stylusId);
                    CancelPauseStraightenTimer(-200001);
                    _pauseStraightenInkModeTracking = false;
                    _activeRealtimeStylusStrokeIds.Remove(stylusId);
                    EndTouchInkInputIfIdle();
                    inkCanvas.ReleaseStylusCapture();
                    ViewboxFloatingBar.IsHitTestVisible = true;
                    BlackboardUIGridForInkReplay.IsHitTestVisible = true;
                    SetCursorBasedOnEditingMode(inkCanvas);
                    e.Handled = true;
                }
                return;
            }

            if (drawingShapeMode != 0)
            {
                // 重置触摸状态
                isTouchDown = false;
                ViewboxFloatingBar.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;

                // 对于双曲线等需要多步绘制的图形，手写笔抬起时应该进入下一步
                if (drawingShapeMode == 24 || drawingShapeMode == 25)
                {
                    if (drawMultiStepShapeCurrentStep == 0)
                    {
                        // 第一笔完成，进入第二笔
                        drawMultiStepShapeCurrentStep = 1;
                    }
                    else
                    {
                        // 第二笔完成，完成绘制
                        var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                        {
                            RoutedEvent = MouseLeftButtonUpEvent,
                            Source = inkCanvas
                        };
                        inkCanvas_MouseUp(inkCanvas, mouseArgs);
                    }
                }
                else
                {
                    // 其他单步绘制的图形，手写笔抬起时完成绘制
                    var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = MouseLeftButtonUpEvent,
                        Source = inkCanvas
                    };
                    inkCanvas_MouseUp(inkCanvas, mouseArgs);
                }

                return;
            }

            try
            {
                var stroke = GetStrokeVisual(e.StylusDevice.Id).Stroke;

                if (stroke != null)
                {
                    inkCanvas.Strokes.Add(stroke);
                    await Task.Delay(5);
                    inkCanvas.Children.Remove(GetVisualCanvas(e.StylusDevice.Id));

                    inkCanvas_StrokeCollected(inkCanvas,
                    new InkCanvasStrokeCollectedEventArgs(stroke));
                }
                else
                {
                    await Task.Delay(5);
                    inkCanvas.Children.Remove(GetVisualCanvas(e.StylusDevice.Id));
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"MainWindow_StylusUp 出错: {ex}", LogHelper.LogType.Error);
                Label.Content = ex.ToString();
            }

            try
            {
                StrokeVisualList.Remove(e.StylusDevice.Id);
                VisualCanvasList.Remove(e.StylusDevice.Id);
                TouchDownPointsList.Remove(e.StylusDevice.Id);
                CleanupRealtimeBrushTipState(e.StylusDevice.Id);
                CancelPauseStraightenTimer(e.StylusDevice.Id);
                CancelPauseStraightenTimer(-200001);
                _pauseStraightenInkModeTracking = false;
                if (StrokeVisualList.Count == 0 || VisualCanvasList.Count == 0 || TouchDownPointsList.Count == 0)
                {
                    // 只清除手写笔预览相关的Canvas，不清除所有子元素
                    foreach (var canvas in VisualCanvasList.Values.ToList())
                    {
                        if (inkCanvas.Children.Contains(canvas))
                        {
                            inkCanvas.Children.Remove(canvas);
                        }
                    }
                    StrokeVisualList.Clear();
                    VisualCanvasList.Clear();
                    TouchDownPointsList.Clear();
                }
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }

            inkCanvas.ReleaseStylusCapture();
            ViewboxFloatingBar.IsHitTestVisible = true;
            BlackboardUIGridForInkReplay.IsHitTestVisible = true;
            SetCursorBasedOnEditingMode(inkCanvas);
        }

        /// <summary>
        /// 主窗口的手写笔移动事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">手写笔事件参数</param>
        /// <remarks>
        /// 处理手写笔移动事件，包括以下逻辑：
        /// 1. 如果当前处于图形绘制模式且触摸状态为按下：
        ///    - 获取手写笔在InkCanvas上的位置
        ///    - 调用MouseTouchMove方法处理移动
        ///    - 直接返回
        /// 2. 如果触摸按下点的编辑模式不是None，则直接返回
        /// 3. 尝试检查手写笔按钮状态，如果第二个按钮被按下，则直接返回
        /// 4. 否则，获取笔画视觉对象，添加手写笔点，并重新绘制
        /// 5. 捕获并忽略所有异常
        /// </remarks>
        private void MainWindow_StylusMove(object sender, StylusEventArgs e)
        {
            try
            {
                if (IsTouchStylusDevice(e.StylusDevice))
                    return;

                if (drawingShapeMode != 0)
                {
                    if (isTouchDown)
                    {
                        Point stylusPoint = e.GetPosition(inkCanvas);
                        MouseTouchMove(stylusPoint);
                    }
                    return;
                }

                var stylusId = e.StylusDevice.Id;
                if (_activeRealtimeStylusStrokeIds.Contains(stylusId))
                {
                    try
                    {
                        var p = e.GetPosition(inkCanvas);
                        var sv = GetStrokeVisual(stylusId);
                        if (TryAppendRealtimeVelocityBrushTipInterpolatedPoints(sv, stylusId, p))
                            sv.Redraw();
                        ResetPauseStraightenTimer(stylusId);
                        e.Handled = true;
                    }
                    catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                    return;
                }

                if (GetTouchDownPointsList(e.StylusDevice.Id) != InkCanvasEditingMode.None)
                {
                    // Regular Ink mode — InkCanvas builds the stroke internally.
                    // Track position for pause-straighten.
                    if (inkCanvas.EditingMode == InkCanvasEditingMode.Ink && drawingShapeMode == 0)
                        ResetPauseStraightenTimerInkMode(e.GetPosition(inkCanvas));
                    return;
                }
                try
                {
                    if (e.StylusDevice.StylusButtons[1].StylusButtonState == StylusButtonState.Down) return;
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }


                var strokeVisual = GetStrokeVisual(e.StylusDevice.Id);
                var isHandledByRealtime = TryAppendRealtimeVelocityBrushTipPoints(strokeVisual, e);
                if (!isHandledByRealtime)
                {
                    var stylusPointCollection = e.GetStylusPoints(this);
                    foreach (var stylusPoint in stylusPointCollection)
                        strokeVisual.Add(new StylusPoint(stylusPoint.X, stylusPoint.Y, stylusPoint.PressureFactor));
                }

                ResetPauseStraightenTimer(e.StylusDevice.Id);

                strokeVisual.Redraw();
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }

        /// <summary>
        /// 获取笔画视觉对象方法
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns>返回笔画视觉对象</returns>
        /// <remarks>
        /// 根据设备ID获取笔画视觉对象，如果不存在则创建新的：
        /// 1. 尝试从StrokeVisualList中获取笔画视觉对象
        /// 2. 如果不存在，创建新的StrokeVisual实例，使用InkCanvas的默认绘制属性的克隆
        /// 3. 将新的笔画视觉对象添加到StrokeVisualList
        /// 4. 创建新的VisualCanvas实例，将其设置为笔画视觉对象的视觉画布
        /// 5. 将新的视觉画布添加到VisualCanvasList和InkCanvas的子元素中
        /// 6. 返回笔画视觉对象
        /// </remarks>
        private StrokeVisual GetStrokeVisual(int id)
        {
            if (StrokeVisualList.TryGetValue(id, out var visual)) return visual;

            var strokeVisual = new StrokeVisual(inkCanvas.DefaultDrawingAttributes.Clone());
            StrokeVisualList[id] = strokeVisual;
            var visualCanvas = new VisualCanvas();
            strokeVisual.SetVisualCanvas(visualCanvas);
            VisualCanvasList[id] = visualCanvas;
            inkCanvas.Children.Add(visualCanvas);

            return strokeVisual;
        }

        /// <summary>
        /// 获取视觉画布方法
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns>返回视觉画布对象，如果不存在则返回null</returns>
        /// <remarks>
        /// 根据设备ID从VisualCanvasList中获取视觉画布对象
        /// </remarks>
        private VisualCanvas GetVisualCanvas(int id)
        {
            return VisualCanvasList.TryGetValue(id, out var visualCanvas) ? visualCanvas : null;
        }

        private void ResetPauseStraightenTimer(int stylusId)
        {
            if (!Settings.Canvas.PauseStraightenLine) return;
            Debug.WriteLine($"ResetPauseStraightenTimer: id={stylusId}");
            if (_pauseStraightenTimers.TryGetValue(stylusId, out var existing))
            {
                existing.Stop();
                existing.Interval = TimeSpan.FromMilliseconds(Settings.Canvas.PauseStraightenDelay);
                existing.Start();
                return;
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Settings.Canvas.PauseStraightenDelay) };
            var capturedId = stylusId;
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _pauseStraightenTimers.Remove(capturedId);
                Debug.WriteLine($"PauseStraightenTimer fired: id={capturedId}");
                TryPauseStraighten(capturedId);
            };
            _pauseStraightenTimers[stylusId] = timer;
            timer.Start();
        }

        private void ResetPauseStraightenTimerInkMode(Point currentPos)
        {
            if (!Settings.Canvas.PauseStraightenLine) return;
            const int inkModeId = -200001;
            _pauseStraightenInkModeLastPos = currentPos;
            if (_pauseStraightenTimers.TryGetValue(inkModeId, out var existing))
            {
                existing.Stop();
                existing.Interval = TimeSpan.FromMilliseconds(Settings.Canvas.PauseStraightenDelay);
                existing.Start();
                return;
            }
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(Settings.Canvas.PauseStraightenDelay) };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                _pauseStraightenTimers.Remove(inkModeId);
                TryPauseStraightenInkMode();
            };
            _pauseStraightenTimers[inkModeId] = timer;
            timer.Start();
        }

        private Point _pauseStraightenInkModeLastPos;
        private Point _pauseStraightenInkModeStartPos;
        private bool _pauseStraightenInkModeTracking;

        private void TryPauseStraightenInkMode()
        {
            if (!Settings.Canvas.PauseStraightenLine) return;
            if (!_pauseStraightenInkModeTracking) return;
            if (inkCanvas.EditingMode != InkCanvasEditingMode.Ink) return;
            if (drawingShapeMode != 0) return;

            var start = _pauseStraightenInkModeStartPos;
            var end = _pauseStraightenInkModeLastPos;
            double lineLength = GetDistance(start, end);
            if (lineLength < 2) return;

            // Commit current stroke by briefly switching mode
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.EditingMode = InkCanvasEditingMode.Ink;

            // The just-committed stroke should now be last in inkCanvas.Strokes
            if (inkCanvas.Strokes.Count == 0) return;
            var stroke = inkCanvas.Strokes[inkCanvas.Strokes.Count - 1];
            if (stroke.StylusPoints.Count < 2) return;

            var newPoints = new StylusPointCollection();
            newPoints.Add(new StylusPoint(start.X, start.Y, 0.5f));
            if (lineLength > 100)
            {
                newPoints.Add(new StylusPoint(start.X + (end.X - start.X) / 3.0, start.Y + (end.Y - start.Y) / 3.0, 0.5f));
                newPoints.Add(new StylusPoint(start.X + (end.X - start.X) * 2.0 / 3.0, start.Y + (end.Y - start.Y) * 2.0 / 3.0, 0.5f));
            }
            newPoints.Add(new StylusPoint(end.X, end.Y, 0.5f));
            stroke.StylusPoints = newPoints;

            _pauseStraightenInkModeTracking = false;
        }

        private void CancelPauseStraightenTimer(int stylusId)
        {
            if (_pauseStraightenTimers.TryGetValue(stylusId, out var timer))
            {
                timer.Stop();
                _pauseStraightenTimers.Remove(stylusId);
            }
        }

        private void TryPauseStraighten(int stylusId)
        {
            if (!Settings.Canvas.PauseStraightenLine) { Debug.WriteLine("PauseStraighten: disabled"); return; }
            var strokeVisual = StrokeVisualList.TryGetValue(stylusId, out var sv) ? sv : null;
            if (strokeVisual?.Stroke == null) { Debug.WriteLine($"PauseStraighten: no stroke for id={stylusId}"); return; }
            var stroke = strokeVisual.Stroke;
            Debug.WriteLine($"PauseStraighten: points={stroke.StylusPoints.Count}");
            if (stroke.StylusPoints.Count < 2) return;

            var start = stroke.StylusPoints[0].ToPoint();
            var end = stroke.StylusPoints[stroke.StylusPoints.Count - 1].ToPoint();
            double lineLength = GetDistance(start, end);
            Debug.WriteLine($"PauseStraighten: length={lineLength:F1}, STRAIGHTENING!");

            var newPoints = new StylusPointCollection();
            newPoints.Add(new StylusPoint(start.X, start.Y, 0.5f));
            if (lineLength > 100)
            {
                newPoints.Add(new StylusPoint(start.X + (end.X - start.X) / 3.0, start.Y + (end.Y - start.Y) / 3.0, 0.5f));
                newPoints.Add(new StylusPoint(start.X + (end.X - start.X) * 2.0 / 3.0, start.Y + (end.Y - start.Y) * 2.0 / 3.0, 0.5f));
            }
            newPoints.Add(new StylusPoint(end.X, end.Y, 0.5f));
            stroke.StylusPoints = newPoints;
            strokeVisual.ForceRedraw();
        }

        /// <summary>
        /// 获取触摸按下点的编辑模式方法
        /// </summary>
        /// <param name="id">设备ID</param>
        /// <returns>返回触摸按下点的编辑模式，如果不存在则返回InkCanvas的当前编辑模式</returns>
        /// <remarks>
        /// 根据设备ID从TouchDownPointsList中获取触摸按下点的编辑模式
        /// </remarks>
        private InkCanvasEditingMode GetTouchDownPointsList(int id)
        {
            return TouchDownPointsList.TryGetValue(id, out var inkCanvasEditingMode) ? inkCanvasEditingMode : inkCanvas.EditingMode;
        }

        /// <summary>
        /// 触摸按下点的编辑模式字典，键为设备ID，值为编辑模式
        /// </summary>
        private Dictionary<int, InkCanvasEditingMode> TouchDownPointsList { get; } =
            new Dictionary<int, InkCanvasEditingMode>();

        /// <summary>
        /// 笔画视觉对象字典，键为设备ID，值为笔画视觉对象
        /// </summary>
        private Dictionary<int, StrokeVisual> StrokeVisualList { get; } = new Dictionary<int, StrokeVisual>();
        /// <summary>
        /// 视觉画布字典，键为设备ID，值为视觉画布对象
        /// </summary>
        private Dictionary<int, VisualCanvas> VisualCanvasList { get; } = new Dictionary<int, VisualCanvas>();

        #endregion




    }
}
