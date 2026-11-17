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
        // LTSC（2026-11-17）：从 MW_ShapeDrawing.cs 拆出。
        // 这一整段是形状工具栏的按钮点击处理器（BtnDrawXxx_Click），
        // 只负责「点了什么按钮 → 设成什么绘制模式」，真正的绘制在 Core 那个文件里。
        #region Buttons

        /// <summary>
        /// 处理形状绘制面板固定按钮的鼠标抬起事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当形状绘制面板固定按钮被点击时：
        /// 1. 检查是否是当前按下的对象
        /// 2. 切换自动隐藏开关的状态
        /// 3. 根据开关状态更新图标为固定或未固定状态
        /// </remarks>
        private void SymbolIconPinBorderDrawShape_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (lastBorderMouseDownObject != sender) return;

            ToggleSwitchDrawShapeBorderAutoHide.IsOn = !ToggleSwitchDrawShapeBorderAutoHide.IsOn;

            if (ToggleSwitchDrawShapeBorderAutoHide.IsOn)
                ((FontIcon)sender).Icon = SegoeFluentIcons.Pin;
            else
                ((FontIcon)sender).Icon = SegoeFluentIcons.Unpin;
        }

        /// <summary>
        /// 上一次鼠标按下的发送者
        /// </summary>
        private object lastMouseDownSender;

        /// <summary>
        /// 上一次鼠标按下的时间
        /// </summary>
        private DateTime lastMouseDownTime = DateTime.MinValue;

        /// <summary>
        /// 处理形状绘制按钮的鼠标按下事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当形状绘制按钮被长按（500毫秒）时：
        /// 1. 记录当前按下的对象和时间
        /// 2. 等待500毫秒检查是否仍然是同一个对象
        /// 3. 如果是同一个对象，设置透明度动画
        /// 4. 禁用橡皮擦覆盖
        /// 5. 设置强制橡皮擦模式
        /// 6. 根据不同的按钮设置不同的形状绘制模式
        /// 7. 更新工具模式缓存
        /// 8. 设置长按选中标志
        /// 9. 如果是单指拖拽模式，取消该模式
        /// </remarks>
        private async void Image_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("绘制几何图形"))
            {
                e.Handled = true;
                return;
            }
            lastMouseDownSender = sender;
            lastMouseDownTime = DateTime.Now;

            await Task.Delay(500);

            if (lastMouseDownSender == sender)
            {
                lastMouseDownSender = null;
                var dA = new DoubleAnimation(1, 0.3, new Duration(TimeSpan.FromMilliseconds(100)));
                ((UIElement)sender).BeginAnimation(OpacityProperty, dA);

                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                if (sender == ImageDrawLine || sender == BoardImageDrawLine)
                    drawingShapeMode = 1;
                else if (sender == ImageDrawDashedLine || sender == BoardImageDrawDashedLine)
                    drawingShapeMode = 8;
                else if (sender == ImageDrawDotLine || sender == BoardImageDrawDotLine)
                    drawingShapeMode = 18;
                else if (sender == ImageDrawArrow || sender == BoardImageDrawArrow)
                    drawingShapeMode = 2;
                else if (sender == ImageDrawParallelLine || sender == BoardImageDrawParallelLine) drawingShapeMode = 15;

                // 更新模式缓存
                UpdateCurrentToolMode("shape");

                isLongPressSelected = true;
                if (isSingleFingerDragMode) ToggleFingerDragMode(null, null);
            }
        }

        /// <summary>
        /// 处理画笔按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 当画笔按钮被点击时：
        /// 1. 禁用强制橡皮擦模式
        /// 2. 重置形状绘制模式
        /// 3. 设置墨水画布编辑模式为墨迹
        /// 4. 启用墨水画布的操作功能
        /// 5. 取消单指拖拽模式
        /// 6. 重置长按选中标志
        /// </remarks>
        private void BtnPen_Click(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到画笔")) return;
            forceEraser = false;
            drawingShapeMode = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            isLongPressSelected = false;
        }

        /// <summary>
        /// 检查是否在多点触控模式下绘制形状
        /// </summary>
        /// <returns>返回一个表示操作成功的Task<bool></returns>
        /// <remarks>
        /// 检查多点触控模式并在需要时禁用：
        /// 1. 如果当前在多点触控模式下
        /// 2. 禁用多点触控模式
        /// 3. 记录之前的多点触控模式状态
        /// 4. 返回成功的任务
        /// </remarks>
        private Task<bool> CheckIsDrawingShapesInMultiTouchMode()
        {
            if (TryBlockFrozenPageMutation("绘制几何图形"))
                return Task.FromResult(false);

            if (isInMultiTouchMode)
            {
                ToggleSwitchEnableMultiTouchMode.IsOn = false;
                lastIsInMultiTouchMode = true;
            }

            return Task.FromResult(true);
        }

        /// <summary>
        /// 处理绘制直线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制直线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为直线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        public async void BtnDrawLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 1;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制虚线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制虚线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为虚线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawDashedLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 8;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制点线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制点线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为点线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawDotLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 18;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制箭头按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制箭头按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为箭头
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawArrow_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 2;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制平行线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制平行线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 如果是长按操作，设置绘制模式为平行线
        /// 3. 重置鼠标按下发送者
        /// 4. 如果是长按选中状态，处理相关逻辑
        /// 5. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParallelLine_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            if (lastMouseDownSender == sender)
            {
                forcePointEraser = false;
                DisableEraserOverlay();

                forceEraser = true;
                drawingShapeMode = 15;
                inkCanvas.EditingMode = InkCanvasEditingMode.None;
                inkCanvas.IsManipulationEnabled = true;
                CancelSingleFingerDragMode();
            }

            lastMouseDownSender = null;
            if (isLongPressSelected)
            {
                if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();
                if (sender is UIElement ui)
                {
                    var dA = new DoubleAnimation(1, 1, new Duration(TimeSpan.FromMilliseconds(0)));
                    ui.BeginAnimation(OpacityProperty, dA);
                }
            }

            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制坐标系1按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制坐标系1按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为坐标系1
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate1_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 11;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制坐标系2按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制坐标系2按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为坐标系2
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate2_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 12;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制坐标系3按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制坐标系3按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为坐标系3
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate3_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 13;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制坐标系4按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制坐标系4按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为坐标系4
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate4_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 14;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制坐标系5按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制坐标系5按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为坐标系5
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCoordinate5_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 17;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制矩形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制矩形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为矩形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawRectangle_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 3;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制中心矩形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制中心矩形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为中心矩形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawRectangleCenter_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 19;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制椭圆按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制椭圆按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为椭圆
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 4;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制圆形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制圆形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为圆形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCircle_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 5;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制中心椭圆按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制中心椭圆按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为中心椭圆
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCenterEllipse_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 16;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制带焦点的中心椭圆按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制带焦点的中心椭圆按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为带焦点的中心椭圆
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCenterEllipseWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 23;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制虚线圆形按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制虚线圆形按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为虚线圆形
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawDashedCircle_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 10;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制双曲线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制双曲线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为双曲线
        /// 6. 重置多步形状绘制当前步骤
        /// 7. 设置墨水画布编辑模式为无
        /// 8. 启用墨水画布的操作功能
        /// 9. 取消单指拖拽模式
        /// 10. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawHyperbola_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 24;
            drawMultiStepShapeCurrentStep = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制带焦点的双曲线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制带焦点的双曲线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为带焦点的双曲线
        /// 6. 重置多步形状绘制当前步骤
        /// 7. 设置墨水画布编辑模式为无
        /// 8. 启用墨水画布的操作功能
        /// 9. 取消单指拖拽模式
        /// 10. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawHyperbolaWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 25;
            drawMultiStepShapeCurrentStep = 0;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制抛物线1按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制抛物线1按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为抛物线1
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParabola1_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 20;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制带焦点的抛物线按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制带焦点的抛物线按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为带焦点的抛物线
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParabolaWithFocalPoint_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 22;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制抛物线2按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制抛物线2按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为抛物线2
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawParabola2_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 21;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制圆柱按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制圆柱按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为圆柱
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCylinder_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 6;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制圆锥按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制圆锥按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为圆锥
        /// 6. 设置墨水画布编辑模式为无
        /// 7. 启用墨水画布的操作功能
        /// 8. 取消单指拖拽模式
        /// 9. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCone_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 7;
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        /// <summary>
        /// 处理绘制长方体按钮点击事件
        /// </summary>
        /// <param name="sender">事件发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 当绘制长方体按钮被点击时：
        /// 1. 检查是否在多点触控模式下
        /// 2. 禁用点橡皮擦模式
        /// 3. 禁用橡皮擦覆盖
        /// 4. 设置强制橡皮擦模式
        /// 5. 设置绘制模式为长方体
        /// 6. 重置长方体绘制的首次触摸标志
        /// 7. 初始化长方体前方面矩形的起始点和结束点
        /// 8. 设置墨水画布编辑模式为无
        /// 9. 启用墨水画布的操作功能
        /// 10. 取消单指拖拽模式
        /// 11. 提示切换到画笔模式
        /// </remarks>
        private async void BtnDrawCuboid_Click(object sender, MouseButtonEventArgs e)
        {
            if (!await CheckIsDrawingShapesInMultiTouchMode()) return;
            forcePointEraser = false;
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 9;
            isFirstTouchCuboid = true;
            CuboidFrontRectIniP = new Point();
            CuboidFrontRectEndP = new Point();
            inkCanvas.EditingMode = InkCanvasEditingMode.None;
            inkCanvas.IsManipulationEnabled = true;
            CancelSingleFingerDragMode();
            DrawShapePromptToPen();
        }

        #endregion
    }
}
