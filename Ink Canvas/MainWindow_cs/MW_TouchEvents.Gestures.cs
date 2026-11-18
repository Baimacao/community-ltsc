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
        // LTSC（2026-11-17）：从 MW_TouchEvents.cs 拆出。
        //
        // 这一整段是手势与画布变换：双指缩放、Manipulation 事件、
        // 以及对画布里图片/视频元素应用矩阵变换。
        // 原文件里它和单点触摸书写混在一起，但两者不会同时发生。
        private Point iniP = new Point(0, 0);

        /// <summary>
        /// 主网格的触摸按下事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">触摸事件参数</param>
        /// <remarks>
        /// 处理主网格的触摸按下事件，包括以下逻辑：
        /// 1. 检查触摸是否发生在浮动栏区域，如果是则允许事件传播到浮动栏按钮并返回
        /// 2. 根据编辑模式设置光标
        /// 3. 捕获触摸输入
        /// 4. 如果当前处于点擦模式，则直接返回
        /// 5. 如果当前处于图形绘制模式：
        ///    - 设置编辑模式为None
        ///    - 设置触摸状态为按下
        ///    - 禁用浮动栏和黑板UI网格的命中测试
        ///    - 设置起始点坐标
        ///    - 直接返回
        /// 6. 如果当前处于选择模式、墨水模式或线擦模式，则直接返回
        /// 7. 如果当前不是橡皮擦模式，则设置编辑模式为Ink
        /// </remarks>
        private void Main_Grid_TouchDown(object sender, TouchEventArgs e)
        {
            // 检查触摸是否发生在浮动栏区域，如果是则允许事件传播到浮动栏按钮
            var touchPoint = e.GetTouchPoint(this);
            if (TryBlockInkInputOverFloatingBar(touchPoint.Position, e))
                return;

            if (IsCurrentPageFrozen)
            {
                TryBlockFrozenPageMutation("修改冻结页面");
                e.Handled = true;
                return;
            }

            SetCursorBasedOnEditingMode(inkCanvas);
            inkCanvas.CaptureTouch(e.TouchDevice);

            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint)
            {
                return;
            }
            if (drawingShapeMode != 0)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.None;

                // 设置触摸状态，类似鼠标事件处理
                isTouchDown = true;
                ViewboxFloatingBar.IsHitTestVisible = false;
                BlackboardUIGridForInkReplay.IsHitTestVisible = false;

                // 设置起始点
                if (NeedUpdateIniP()) iniP = e.GetTouchPoint(inkCanvas).Position;

                return;
            }
            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                return;
            }
            if (inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
            {
                return;
            }
            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke)
            {
                return;
            }
            if (inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            }
        }

        /// <summary>
        /// 获取触摸边界宽度方法
        /// </summary>
        /// <param name="e">触摸事件参数</param>
        /// <returns>返回触摸边界宽度</returns>
        /// <remarks>
        /// 手掌擦阈值与特殊屏 <c>TouchMultiplier</c> 在激活逻辑中单独参与计算，此处仅返回几何接触尺寸。
        /// </remarks>
        public double GetTouchBoundWidth(TouchEventArgs e)
        {
            var args = e.GetTouchPoint(null).Bounds;
            if (!Settings.Advanced.IsQuadIR) return args.Width;
            return Math.Sqrt(args.Width * args.Height);
        }

        /// <summary>
        /// InkCanvas的预览触摸按下事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">触摸事件参数</param>
        /// <remarks>
        /// 处理InkCanvas的预览触摸按下事件，包括以下逻辑：
        /// 1. 捕获触摸输入
        /// 2. 禁用浮动栏和黑板UI网格的命中测试
        /// 3. 将触摸设备ID添加到dec列表中
        /// 4. 当只有一个触摸设备时：
        ///    - 记录中心点坐标
        ///    - 记录第一根手指点击时的StrokeCollection
        /// 5. 当有两个或以上触摸设备，或者处于单指拖动模式，或者禁用了双指手势时：
        ///    - 如果处于多点触控模式或禁用了双指手势，则直接返回
        ///    - 如果当前编辑模式为None或Select，则直接返回
        ///    - 记录当前的编辑模式
        ///    - 设置编辑模式为None，关闭画笔功能
        /// </remarks>
        private void InkCanvas_PreviewTouchDown(object sender, TouchEventArgs e)
        {
            var touchPointForBar = e.GetTouchPoint(this);
            if (TryBlockInkInputOverFloatingBar(touchPointForBar.Position, e))
                return;
            CaptureInkCanvasTouchIfNeeded(touchPointForBar.Position, e.TouchDevice);

            if (IsCurrentPageFrozen)
            {
                TryBlockFrozenPageMutation("修改冻结页面");
                e.Handled = true;
                return;
            }

            if ((inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint
                 || inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke)
                && !isPalmEraserActive)
            {
                return;
            }

            if (drawingShapeMode != 0)
            {
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                SetCursorBasedOnEditingMode(inkCanvas);
                inkCanvas.CaptureTouch(e.TouchDevice);
                ViewboxFloatingBar.IsHitTestVisible = false;
                BlackboardUIGridForInkReplay.IsHitTestVisible = false;

                isTouchDown = true;

                if (dec.Count == 0)
                {
                    var inkTouchPoint = e.GetTouchPoint(inkCanvas);
                    if (drawingShapeMode == 24 || drawingShapeMode == 25)
                    {
                        if (drawMultiStepShapeCurrentStep == 0)
                            iniP = inkTouchPoint.Position;
                    }
                    else
                    {
                        iniP = inkTouchPoint.Position;
                    }
                    lastTouchDownStrokeCollection = inkCanvas.Strokes.Clone();
                }
                dec.Add(e.TouchDevice.Id);
                _touchIdStamp[e.TouchDevice.Id] = DateTime.UtcNow;
                return;
            }

            SetCursorBasedOnEditingMode(inkCanvas);
            inkCanvas.CaptureTouch(e.TouchDevice);
            ViewboxFloatingBar.IsHitTestVisible = false;
            BlackboardUIGridForInkReplay.IsHitTestVisible = false;
            lastTouchDownTime = DateTime.Now;
            dec.Add(e.TouchDevice.Id);
            _touchIdStamp[e.TouchDevice.Id] = DateTime.UtcNow;
            // 进新触摸前先清一次僵尸 id，避免 dec 越滚越大。
            PruneStaleTouchIds();

            if (ShouldUseRealtimeVelocityBrushTipForTouch()
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke
                && inkCanvas.EditingMode != InkCanvasEditingMode.Select)
            {
                try
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    var touchId = e.TouchDevice.Id;
                    var p = e.GetTouchPoint(inkCanvas).Position;
                    _activeRealtimeTouchStrokeIds.Add(touchId);
                    BeginTouchInkInput();
                    CancelPauseStraightenTimer(touchId);
                    InitializeRealtimeBrushTipStateFromPoint(touchId, p);
                    var sv = GetStrokeVisual(touchId);
                    TryAppendRealtimeVelocityBrushTipInterpolatedPoints(sv, touchId, p);
                    sv.Redraw();
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                return;
            }

            if ((isInMultiTouchMode || (currentMode == 1 ? Settings.Gesture.IsEnableMultiTouchModeBoard : Settings.Gesture.IsEnableMultiTouchMode))
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke
                && inkCanvas.EditingMode != InkCanvasEditingMode.Select)
            {
                try
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    var touchId = e.TouchDevice.Id;
                    var p = e.GetTouchPoint(inkCanvas).Position;
                    _activeTouchStrokeIds.Add(touchId);
                    BeginTouchInkInput();
                    CancelPauseStraightenTimer(touchId);
                    var sv = GetStrokeVisual(touchId);
                    AppendInterpolatedTouchPoints(sv, touchId, p);
                    sv.Redraw();
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                return;
            }

            if (Settings.Canvas.EnablePalmEraser && !isPalmEraserActive && drawingShapeMode == 0)
            {
                var touchPoint = e.GetTouchPoint(inkCanvas);
                double boundWidth = GetTouchBoundWidth(e);

                if ((Settings.Advanced.TouchMultiplier != 0 || !Settings.Advanced.IsSpecialScreen)
                    && (boundWidth > BoundsWidth))
                {
                    double thresholdMultiplier;
                    switch (Settings.Canvas.PalmEraserSensitivity)
                    {
                        case 0:
                            thresholdMultiplier = 3.0;
                            break;
                        case 1:
                            thresholdMultiplier = 2.5;
                            break;
                        case 2:
                        default:
                            thresholdMultiplier = 2.0;
                            break;
                    }

                    double EraserThresholdValue = Settings.Startup.IsEnableNibMode
                        ? Settings.Advanced.NibModeBoundsWidthThresholdValue
                        : Settings.Advanced.FingerModeBoundsWidthThresholdValue;

                    if (boundWidth > BoundsWidth * EraserThresholdValue * thresholdMultiplier)
                    {
                        boundWidth *= Settings.Startup.IsEnableNibMode
                            ? Settings.Advanced.NibModeBoundsWidthEraserSize
                            : Settings.Advanced.FingerModeBoundsWidthEraserSize;

                        if (Settings.Advanced.IsSpecialScreen)
                            boundWidth *= Settings.Advanced.TouchMultiplier;
                        palmEraserPreviousEditingMode = inkCanvas.EditingMode;
                        inkCanvas.EditingMode = InkCanvasEditingMode.EraseByPoint;
                        isPalmEraserActive = true;

                        EnableEraserOverlay();
                        eraserWidth = boundWidth;
                        UpdateEraserStyle();
                        touchPoint = e.GetTouchPoint(inkCanvas);
                        EraserOverlay_PointerDown(sender);
                        EraserOverlay_PointerMove(sender, touchPoint.Position);
                        if (Settings.Canvas.IsShowCursor)
                        {
                            inkCanvas.ForceCursor = false;
                            inkCanvas.UseCustomCursor = false;
                        }
                    }
                }
            }

            if (dec.Count == 1)
            {
                var touchPoint = e.GetTouchPoint(inkCanvas);
                centerPoint = touchPoint.Position;
                lastTouchDownStrokeCollection = inkCanvas.Strokes.Clone();
            }

            if (dec.Count > 1 || isSingleFingerDragMode || !Settings.Gesture.IsEnableTwoFingerGesture)
            {
                if (isInMultiTouchMode || !Settings.Gesture.IsEnableTwoFingerGesture) return;
                if (inkCanvas.EditingMode == InkCanvasEditingMode.None ||
                    inkCanvas.EditingMode == InkCanvasEditingMode.Select) return;
                var timeSinceLastTouch = (DateTime.Now - lastTouchDownTime).TotalMilliseconds;
                if (timeSinceLastTouch < MULTI_TOUCH_DELAY_MS && inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                {
                    if (!isMultiTouchTimerActive)
                    {
                        isMultiTouchTimerActive = true;
                        var remainingTime = MULTI_TOUCH_DELAY_MS - timeSinceLastTouch;
                        // LTSC P2：这里原来是一个裸的 Task.Delay().ContinueWith()：
                        //   · 没有 try/catch，里面 Dispatcher.Invoke 抛了就直接死在线程池上
                        //   · 没有检查 Dispatcher.HasShutdownStarted，退出阶段 Invoke 必抛
                        //   · 同步 Invoke 会阻塞线程池线程，高负载连拍时线程池被占满
                        // 实测：多指书写时崩在 DynamicRenderer 那条
                        // "调用线程无法访问此对象，因为另一个线程拥有该对象"，
                        // 时间点正是这个 100ms 定时器该触发的窗口。
                        //
                        // 改成 BeginInvoke（异步，不阻塞线程池）+ 三重检查 + 全包 try。
                        Task.Delay((int)remainingTime).ContinueWith(_ =>
                        {
                            try
                            {
                                var d = Dispatcher;
                                if (d == null || d.HasShutdownStarted || d.HasShutdownFinished) return;
                                d.BeginInvoke(new Action(() =>
                                {
                                    try
                                    {
                                        if (dec.Count > 1 && inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                                            inkCanvas.EditingMode = InkCanvasEditingMode.None;
                                    }
                                    catch (Exception ex)
                                    {
                                        System.Diagnostics.Debug.WriteLine($"[P2] 多指定时器 UI 段失败: {ex.Message}");
                                    }
                                    finally
                                    {
                                        isMultiTouchTimerActive = false;
                                    }
                                }));
                            }
                            catch (Exception ex)
                            {
                                // BegInvoke 本身抛（Dispatcher 已关）：静默降级，不再往上报。
                                isMultiTouchTimerActive = false;
                                System.Diagnostics.Debug.WriteLine($"[P2] 多指定时器投递失败: {ex.Message}");
                            }
                        }, TaskScheduler.Default);
                    }
                    return;
                }

                lastInkCanvasEditingMode = inkCanvas.EditingMode;
                if (inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                    && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke
                    && drawingShapeMode == 0)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.None;
                }
            }
        }

        /// <summary>
        /// InkCanvas的预览触摸移动事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">触摸事件参数</param>
        /// <remarks>
        /// 空方法，预留用于处理InkCanvas的预览触摸移动事件
        /// </remarks>
        private void InkCanvas_PreviewTouchMove(object sender, TouchEventArgs e)
        {
            if (isPalmEraserActive)
            {
                var touchPoint = e.GetTouchPoint(inkCanvas);
                EraserOverlay_PointerMove(sender, touchPoint.Position);
                return;
            }

            var touchId = e.TouchDevice.Id;
            if (_activeRealtimeTouchStrokeIds.Contains(touchId))
            {
                try
                {
                    var p = e.GetTouchPoint(inkCanvas).Position;
                    var sv = GetStrokeVisual(touchId);
                    if (TryAppendRealtimeVelocityBrushTipInterpolatedPoints(sv, touchId, p))
                        sv.Redraw();
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                return;
            }

            if (_activeTouchStrokeIds.Contains(touchId))
            {
                try
                {
                    var p = e.GetTouchPoint(inkCanvas).Position;
                    var sv = GetStrokeVisual(touchId);
                    AppendInterpolatedTouchPoints(sv, touchId, p);
                    sv.Redraw();
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
            }
        }

        /// <summary>
        /// InkCanvas的预览触摸抬起事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">触摸事件参数</param>
        /// <remarks>
        /// 处理InkCanvas的预览触摸抬起事件，包括以下逻辑：
        /// 1. 释放所有触摸捕获
        /// 2. 启用浮动栏和黑板UI网格的命中测试
        /// 3. 如果有多个触摸设备且当前编辑模式为None，则切回之前的编辑模式
        /// 4. 从dec列表中移除当前触摸设备ID
        /// 5. 当没有触摸设备时：
        ///    - 重置单指拖动模式和等待下一次触摸按下的标志
        ///    - 如果当前不是图形绘制模式且编辑模式不是橡皮擦或选择模式，则切回之前的编辑模式
        /// 6. 如果当前处于图形绘制模式：
        ///    - 重置触摸状态
        ///    - 启用浮动栏和黑板UI网格的命中测试
        ///    - 对于双曲线等需要多步绘制的图形，根据当前步骤决定是进入下一步还是完成绘制
        ///    - 对于其他单步绘制的图形，直接完成绘制
        /// 7. 设置InkCanvas的透明度为1
        /// 8. 当没有触摸设备且笔画数量发生变化，且不是绘制长方体的第一次触摸时，保存笔画集合
        /// </remarks>
        private void InkCanvas_PreviewTouchUp(object sender, TouchEventArgs e)
        {
            var touchId = e.TouchDevice.Id;
            if (_activeRealtimeTouchStrokeIds.Contains(touchId))
            {
                try
                {
                    var sv = GetStrokeVisual(touchId);
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
                    if (VisualCanvasList.TryGetValue(touchId, out var visualCanvas) && inkCanvas.Children.Contains(visualCanvas))
                        inkCanvas.Children.Remove(visualCanvas);
                    StrokeVisualList.Remove(touchId);
                    VisualCanvasList.Remove(touchId);
                    TouchDownPointsList.Remove(touchId);
                    CleanupRealtimeBrushTipState(touchId);
                    CancelPauseStraightenTimer(touchId);
                    _activeRealtimeTouchStrokeIds.Remove(touchId);
                    EndTouchInkInputIfIdle();
                }
            }
            else if (_activeTouchStrokeIds.Contains(touchId))
            {
                try
                {
                    var sv = GetStrokeVisual(touchId);
                    sv?.Redraw();
                    var stroke = sv?.Stroke;
                    if (stroke != null)
                    {
                        inkCanvas.Strokes.Add(stroke);
                        inkCanvas_StrokeCollected(inkCanvas, new InkCanvasStrokeCollectedEventArgs(stroke));
                    }
                }
                catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                finally
                {
                    if (VisualCanvasList.TryGetValue(touchId, out var visualCanvas) && inkCanvas.Children.Contains(visualCanvas))
                        inkCanvas.Children.Remove(visualCanvas);
                    StrokeVisualList.Remove(touchId);
                    VisualCanvasList.Remove(touchId);
                    TouchDownPointsList.Remove(touchId);
                    CleanupRealtimeBrushTipState(touchId);
                    CancelPauseStraightenTimer(touchId);
                    _activeTouchStrokeIds.Remove(touchId);
                    EndTouchInkInputIfIdle();
                }
            }

            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint && !isPalmEraserActive)
            {
                return;
            }
            try { _releasedCaptureCount = inkCanvas.TouchesCaptured?.Count() ?? 0; } catch { _releasedCaptureCount = 0; }

            // LTSC P2 回滚记录（2026-11-11）：
            // 上一版我把这里改成 ReleaseTouchCapture(e.TouchDevice)，理由是
            // "只放当前这根，另一根收不到 TouchUp 会残留在 dec 里"。
            // 实测结果：双指手势从「中=1167」掉到「中=0」，一次都没成功过。
            //
            // 原因：WPF 的 manipulation 事件链路依赖多根手指同时被捕获。
            // 只放一根，另外那根虽然还在捕获集里，但 ManipulationDelta 再也拼不出
            // 完整的 Manipulators 集合，shouldUseTwoFingerGesture 的
            // dec.Count >= 2 && hasMultipleManipulators 后半截永远不成立。
            //
            // 结论：这里必须保持 ReleaseAllTouchCaptures()。
            // dec 残留改用 PruneStaleTouchIds() 从另一头收拾——那是纯清理，
            // 不碰捕获机制，不会把手势链打断。
            inkCanvas.ReleaseAllTouchCaptures();
            ViewboxFloatingBar.IsHitTestVisible = true;
            BlackboardUIGridForInkReplay.IsHitTestVisible = true;

            dec.Remove(e.TouchDevice.Id);
            _touchIdStamp.Remove(e.TouchDevice.Id);
            // P2 诊断：ReleaseAllTouchCaptures() 一次放掉全部捕获，
            // 但这里只移除当前指的 ID。若另一指仍在 dec 里，它再也收不到 TouchUp。

            if (dec.Count <= 1)
                isMultiTouchTimerActive = false;

            if (drawingShapeMode != 0)
            {
                isTouchDown = false;
                ViewboxFloatingBar.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;

                if (drawingShapeMode == 24 || drawingShapeMode == 25)
                {
                    if (drawMultiStepShapeCurrentStep == 0)
                    {
                        drawMultiStepShapeCurrentStep = 1;
                    }
                    else
                    {
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
                    var mouseArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left)
                    {
                        RoutedEvent = MouseLeftButtonUpEvent,
                        Source = inkCanvas
                    };
                    inkCanvas_MouseUp(inkCanvas, mouseArgs);
                }
            }

            if (drawingShapeMode == 0)
            {
                if (dec.Count > 1)
                {
                    if (inkCanvas.EditingMode == InkCanvasEditingMode.None)
                    {
                        if (lastInkCanvasEditingMode != InkCanvasEditingMode.EraseByPoint)
                            inkCanvas.EditingMode = lastInkCanvasEditingMode;
                    }
                }
                else if (dec.Count == 0)
                {
                    isSingleFingerDragMode = false;
                    isWaitUntilNextTouchDown = false;

                    if (inkCanvas.EditingMode == InkCanvasEditingMode.None &&
                        lastInkCanvasEditingMode != InkCanvasEditingMode.None &&
                        lastInkCanvasEditingMode != InkCanvasEditingMode.EraseByPoint)
                    {
                        inkCanvas.EditingMode = lastInkCanvasEditingMode;
                    }

                    if (isPalmEraserActive)
                    {
                        isPalmEraserActive = false;
                        DisableEraserOverlay();
                        if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint)
                        {
                            inkCanvas.EditingMode = palmEraserPreviousEditingMode;
                            SetCursorBasedOnEditingMode(inkCanvas);
                        }
                    }
                }
            }

            inkCanvas.Opacity = 1;

            if (dec.Count == 0)
                if (lastTouchDownStrokeCollection.Count() != inkCanvas.Strokes.Count() &&
                    !(drawingShapeMode == 9 && !isFirstTouchCuboid))
                {
                    // 笔画数量已变化（书写/擦除操作完成）
                }
        }

        /// <summary>
        /// InkCanvas的操作开始事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">操作开始事件参数</param>
        /// <remarks>
        /// 设置操作模式为所有模式
        /// </remarks>
        private void InkCanvas_ManipulationStarting(object sender, ManipulationStartingEventArgs e)
        {
            e.Mode = ManipulationModes.All;
        }

        /// <summary>
        /// InkCanvas的操作惯性开始事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">操作惯性开始事件参数</param>
        /// <remarks>
        /// 空方法，预留用于处理InkCanvas的操作惯性开始事件
        /// </remarks>
        private void InkCanvas_ManipulationInertiaStarting(object sender, ManipulationInertiaStartingEventArgs e) { }

        /// <summary>
        /// 主网格的操作完成事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">操作完成事件参数</param>
        /// <remarks>
        /// 处理主网格的操作完成事件，包括以下逻辑：
        /// 1. 当没有操作器时：
        ///    - 清除dec列表
        ///    - 重置单指拖动模式标志
        ///    - 如果当前不是图形绘制模式且编辑模式不是橡皮擦或选择模式，则设置编辑模式为Ink
        /// </remarks>
        private void Main_Grid_ManipulationCompleted(object sender, ManipulationCompletedEventArgs e)
        {
            if (e.Manipulators.Count() == 0)
            {
                if (dec.Count > 0)
                {
                    dec.Clear();
                    _touchIdStamp.Clear();
                }
                isSingleFingerDragMode = false;

                if (drawingShapeMode == 0
                    && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByPoint
                    && inkCanvas.EditingMode != InkCanvasEditingMode.EraseByStroke
                    && inkCanvas.EditingMode != InkCanvasEditingMode.Select)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    lastInkCanvasEditingMode = InkCanvasEditingMode.Ink;
                }
            }
        }

        /// <summary>
        /// 主网格的操作增量事件处理方法
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">操作增量事件参数</param>
        /// <remarks>
        /// 处理主网格的操作增量事件，包括以下逻辑：
        /// 1. 如果当前处于多点触控模式或禁用了双指手势，则直接返回
        /// 2. 检查是否有多个操作器
        /// 3. 检查是否应该使用双指手势
        /// 4. 如果应该使用双指手势：
        ///    - 获取位移矢量
        ///    - 创建矩阵变换
        ///    - 如果启用了双指平移，则应用平移变换
        ///    - 计算中心点（用于缩放和旋转）
        ///    - 如果启用了双指平移或旋转，则应用旋转变换
        ///    - 如果启用了双指缩放，则应用缩放变换
        ///    - 处理选中的笔画：
        ///       - 对每个选中的笔画应用变换
        ///       - 对圆形笔画更新半径和中心点
        ///       - 如果启用了双指缩放，更新笔画的宽度和高度
        ///    - 处理未选中的笔画：
        ///       - 对所有笔画应用变换
        ///       - 如果启用了双指缩放，更新笔画的宽度和高度
        ///       - 同时变换画布上的图片元素
        ///       - 对所有圆形笔画更新半径和中心点
        /// </remarks>
        private void Main_Grid_ManipulationDelta(object sender, ManipulationDeltaEventArgs e)
        {
            if (IsCurrentPageFrozen)
            {
                TryBlockFrozenPageMutation("移动或缩放内容");
                e.Handled = true;
                return;
            }

            if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint)
            {
                return;
            }

            if (isInMultiTouchMode || !Settings.Gesture.IsEnableTwoFingerGesture)
            {
                return;
            }

            PruneStaleTouchIds();
            bool hasMultipleManipulators = e.Manipulators.Count() >= 2;
            // LTSC P2 修复（2026-11-11，有闸门数据支撑）：
            // 原来第二项是 !ArePPTControlsVisible。
            // 但 PPTUIManager.UpdateConnectionStatus 在 isConnected 时把
            // ArePPTControlsVisible 置 true —— 那表示"PPT 窗口连着"，
            // 不是"正在放映"。于是只要开着 PowerPoint，这一项恒为 false，
            // 整个条件不成立，双指手势永久失效。
            //
            // 实测：闸7「PPT面板挡住」=208，与闸0「进入」=208 完全相等，
            // 说明每一次都倒在这一项上。
            //
            // 换成真正的"放映中"判据 IsInPPTPresentationMode：
            //   · 平时开着 PPT 没放映 → 双指正常
            //   · 放映中 → 遵守 IsEnableTwoFingerGestureInPresentationMode 开关
            bool inSlideShow = IsInPPTPresentationMode;
            bool isBoardMode = currentMode == 1;

            // 白板模式是一块独立画布，不该套用「放映中要不要禁手势」这条规则。
            //
            // 2026-11-18 班测：「PPT 联动模式下（PPT 处于放映状态）打开白板，
            // 双指手势完全不能用」。根因就是这里 —— 用户进了白板，但
            // IsInPPTPresentationMode 仍然是 true（他确实还在放映，只是叠了一层白板），
            // 于是 inSlideShow 恒为真，双指手势只能靠
            // IsEnableTwoFingerGestureInPresentationMode 那个开关放行，而这个开关
            // 默认是 false。白板自己的那三组手势开关（IsEnableTwoFinger*Board）
            // 根本没被读到。
            //
            // 判据用 currentMode == 1：白板/黑板模式下，手势归白板管。
            bool gestureAllowedByMode = isBoardMode
                ? true
                : (Settings.PowerPointSettings.IsEnableTwoFingerGestureInPresentationMode || !inSlideShow);

            bool shouldUseTwoFingerGesture = (dec.Count >= 2 && hasMultipleManipulators &&
                                             gestureAllowedByMode) ||
                                            isSingleFingerDragMode;

            if (shouldUseTwoFingerGesture)
            {
                var md = e.DeltaManipulation;
                var trans = md.Translation; // 获得位移矢量

                var m = new Matrix();

                // isBoardMode 在外层已经算过（手势闸门要用），这里复用，别重复声明。
                bool enableTranslate = isBoardMode ? Settings.Gesture.IsEnableTwoFingerTranslateBoard : Settings.Gesture.IsEnableTwoFingerTranslate;
                bool enableRotate = isBoardMode ? Settings.Gesture.IsEnableTwoFingerRotationBoard : Settings.Gesture.IsEnableTwoFingerRotation;
                bool enableZoom = isBoardMode ? Settings.Gesture.IsEnableTwoFingerZoomBoard : Settings.Gesture.IsEnableTwoFingerZoom;
                bool enableGestureTranslateOrRotate = (isBoardMode
                    ? (Settings.Gesture.IsEnableTwoFingerTranslateBoard || Settings.Gesture.IsEnableTwoFingerRotationBoard)
                    : (Settings.Gesture.IsEnableTwoFingerTranslate || Settings.Gesture.IsEnableTwoFingerRotation));

                if (enableTranslate)
                    m.Translate(trans.X, trans.Y); // 移动

                // 计算中心点（用于缩放和旋转）
                var fe = e.Source as FrameworkElement;
                var center = new Point(fe.ActualWidth / 2, fe.ActualHeight / 2);
                center = m.Transform(center); // 转换为矩阵缩放和旋转的中心点

                if (enableGestureTranslateOrRotate)
                {
                    var rotate = md.Rotation; // 获得旋转角度

                    if (enableRotate)
                        m.RotateAt(rotate, center.X, center.Y); // 旋转
                }

                if (enableZoom)
                {
                    var scale = md.Scale; // 获得缩放倍数
                    m.ScaleAt(scale.X, scale.Y, center.X, center.Y); // 缩放
                }

                var strokes = inkCanvas.GetSelectedStrokes();
                if (strokes.Count != 0)
                {
                    foreach (var stroke in strokes)
                    {
                        stroke.Transform(m, false);

                        foreach (var circle in circles)
                            if (stroke == circle.Stroke)
                            {
                                circle.R = GetDistance(circle.Stroke.StylusPoints[0].ToPoint(),
                                    circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].ToPoint()) / 2;
                                circle.Centroid = new Point(
                                    (circle.Stroke.StylusPoints[0].X +
                                     circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].X) / 2,
                                    (circle.Stroke.StylusPoints[0].Y +
                                     circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].Y) / 2);
                                break;
                            }

                        if (!enableZoom) continue;
                        try
                        {
                            stroke.DrawingAttributes.Width *= md.Scale.X;
                            stroke.DrawingAttributes.Height *= md.Scale.Y;
                        }
                        catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                    }
                }
                else
                {
                    if (enableZoom)
                    {
                        foreach (var stroke in inkCanvas.Strokes)
                        {
                            stroke.Transform(m, false);
                            try
                            {
                                stroke.DrawingAttributes.Width *= md.Scale.X;
                                stroke.DrawingAttributes.Height *= md.Scale.Y;
                            }
                            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
                        }

                        // 同时变换画布上的图片元素
                        TransformCanvasImages(m);
                    }
                    else
                    {
                        foreach (var stroke in inkCanvas.Strokes) stroke.Transform(m, false);

                        // 同时变换画布上的图片元素
                        TransformCanvasImages(m);
                    }

                    foreach (var circle in circles)
                    {
                        circle.R = GetDistance(circle.Stroke.StylusPoints[0].ToPoint(),
                            circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].ToPoint()) / 2;
                        circle.Centroid = new Point(
                            (circle.Stroke.StylusPoints[0].X +
                             circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].X) / 2,
                            (circle.Stroke.StylusPoints[0].Y +
                             circle.Stroke.StylusPoints[circle.Stroke.StylusPoints.Count / 2].Y) / 2
                        );
                    }
                }
            }
        }

        /// <summary>
        /// 变换画布上的图片元素，使其与墨迹同步移动
        /// </summary>
        private void TransformCanvasImages(Matrix matrix)
        {
            try
            {
                // 遍历inkCanvas的所有子元素，找到图片元素
                for (int i = inkCanvas.Children.Count - 1; i >= 0; i--)
                {
                    var child = inkCanvas.Children[i];

                    if (child is Image image)
                    {
                        // 应用矩阵变换到图片
                        ApplyMatrixTransformToImage(image, matrix);
                    }
                    else if (child is MediaElement mediaElement)
                    {
                        // 对媒体元素也应用变换
                        ApplyMatrixTransformToMediaElement(mediaElement, matrix);
                    }
                    else if (child is CanvasMediaControl mediaControl)
                    {
                        ApplyMatrixTransformToCanvasMediaControl(mediaControl, matrix);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"变换画布图片失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 对图片应用矩阵变换
        /// </summary>
        private void ApplyMatrixTransformToImage(Image image, Matrix matrix)
        {
            try
            {
                // 获取图片的RenderTransform，如果不存在则创建新的TransformGroup
                if (!(image.RenderTransform is TransformGroup transformGroup))
                {
                    transformGroup = new TransformGroup();
                    image.RenderTransform = transformGroup;
                }

                // 创建新的MatrixTransform并添加到变换组
                var matrixTransform = new MatrixTransform(matrix);
                transformGroup.Children.Add(matrixTransform);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用图片变换失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 对媒体元素应用矩阵变换
        /// </summary>
        private void ApplyMatrixTransformToMediaElement(MediaElement mediaElement, Matrix matrix)
        {
            try
            {
                // 获取媒体元素的RenderTransform，如果不存在则创建新的TransformGroup
                if (!(mediaElement.RenderTransform is TransformGroup transformGroup))
                {
                    transformGroup = new TransformGroup();
                    mediaElement.RenderTransform = transformGroup;
                }

                // 创建新的MatrixTransform并添加到变换组
                var matrixTransform = new MatrixTransform(matrix);
                transformGroup.Children.Add(matrixTransform);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用媒体元素变换失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        private void ApplyMatrixTransformToCanvasMediaControl(CanvasMediaControl mediaControl, Matrix matrix)
        {
            try
            {
                if (!(mediaControl.RenderTransform is TransformGroup transformGroup))
                {
                    transformGroup = new TransformGroup();
                    mediaControl.RenderTransform = transformGroup;
                }

                var matrixTransform = new MatrixTransform(matrix);
                transformGroup.Children.Add(matrixTransform);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用媒体控件变换失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }
    }
}
