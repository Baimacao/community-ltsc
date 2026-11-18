using Ink_Canvas.Controls;
using Ink_Canvas.Controls.Toolbar.FloatingToolbar;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.WorkflowAutomation;
using iNKORE.UI.WPF.Modern;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        // LTSC（2026-11-17）：从 MW_FloatingBarIcons.cs 拆出。
        // 这一段是工具按钮的 Click 事件处理，原本夹在 6281 行的文件中间，
        // 没有 #region 标出边界（占文件 51% 的代码都在 region 外）。
        // 拆分只搬方法，不改任何逻辑。
        //
        // using 区整块复制，因为原文有 7 条别名
        // （Button/MessageBox/Point/Panel/Application/OpenFileDialog/MouseEventArgs），
        // 漏一条就是歧义编译错误。
        /// <summary>
        /// 光标图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal async void CursorIcon_Click(object sender, MouseButtonEventArgs e)
        {

            if (lastBorderMouseDownObject is Panel panel)
                panel.Background = new SolidColorBrush(Colors.Transparent);

            // 禁用高级橡皮擦系统
            DisableEraserOverlay();
            SetCurrentToolMode(InkCanvasEditingMode.None);

            UpdateCurrentToolMode("cursor");

            SetFloatingBarHighlightPosition("cursor");

            // 切换前自动截图保存墨迹
            if (inkCanvas.Strokes.Count > 0 &&
                inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
            {
                if (IsInPPTPresentationMode)
                {
                    var currentSlide = Math.Max(0, _pptManager?.GetCurrentSlideNumber() ?? 0);
                    var presentationName = _pptManager?.GetPresentationName() ?? "";
                    CaptureAndEnqueueScreenshotSave(true, $"{presentationName}/{currentSlide}_{DateTime.Now:HH-mm-ss}");
                }
                else CaptureAndEnqueueScreenshotSave(true);
            }

            // 从「点页码进导航」回来后的这一次穿透，不隐藏笔迹。
            //
            // 需求原话：再次点击鼠标进入 ppt 鼠标穿透模式不会隐藏笔迹。
            // 范围是一次性的（用户裁决：不是改 PPT 模式的通用行为）：
            // 页码点击那次隐藏笔迹是为了看清缩略图，等用户自己点鼠标退出时，
            // 他要的只是去操作 PPT，笔迹该留着。后续再点鼠标仍按设置走。
            bool skipHideStrokeOnce = _pptNavSkipHideStrokeOnce;
            _pptNavSkipHideStrokeOnce = false;

            if (skipHideStrokeOnce)
            {
                // 一次性豁免：笔迹留着且仍可见。
                // 注意 IsHitTestVisible 要设 false —— 不然穿透模式下点击会被
                // InkCanvas 吃掉，反而去不了 PPT。
                inkCanvas.IsHitTestVisible = false;
                inkCanvas.Visibility = Visibility.Visible;
            }
            else if (!IsInPPTPresentationMode)
            {
                if (Settings.Canvas.HideStrokeWhenSelecting)
                {
                    inkCanvas.Visibility = Visibility.Collapsed;
                }
                else
                {
                    inkCanvas.IsHitTestVisible = false;
                    inkCanvas.Visibility = Visibility.Visible;
                }
            }
            else
            {
                if (Settings.PowerPointSettings.IsShowStrokeOnSelectInPowerPoint)
                {
                    inkCanvas.Visibility = Visibility.Visible;
                    inkCanvas.IsHitTestVisible = true;
                }
                else
                {
                    if (Settings.Canvas.HideStrokeWhenSelecting)
                    {
                        inkCanvas.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        inkCanvas.IsHitTestVisible = false;
                        inkCanvas.Visibility = Visibility.Visible;
                    }
                }
            }

            GridTransparencyFakeBackground.Opacity = 0;
            GridTransparencyFakeBackground.Background = Brushes.Transparent;
            SetTransparentHitThrough();

            GridBackgroundCoverHolder.Visibility = Visibility.Collapsed;

            // 点击鼠标按钮退出批注模式时的全屏还原
            RestoreFullScreenOnExitAnnotationMode();

            inkCanvas.Select(new StrokeCollection());
            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

            if (currentMode != 0)
            {
                SaveStrokes();
                RestoreStrokes(true);
            }

            if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Dark)
            { /* Old UI removed */ }
            else
            { /* Old UI removed */ }

            { /* Old UI removed */ }
            { /* Old UI removed */ }
            UpdateToolbarComponentVisibility();


            UpdateToolbarComponentVisibility();

            // 注意：快捷调色盘的可见性现在完全由工具栏规则集管理，不需要手动设置

            if (!isFloatingBarFolded)
            {
                HideSubPanels("cursor", true);
                await Task.Delay(50);

                if (IsInPPTPresentationMode)
                    ViewboxFloatingBarMarginAnimation(60);
                else
                    ViewboxFloatingBarMarginAnimation(100, true);
            }
        }

        /// <summary>
        /// 画笔图标点击事件处理，用于切换到批注模式或显示画笔调色盘
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal void PenIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到画笔")) return;

            // LTSC D-07：用户自己点了批注键 —— 作废「等切页后自动恢复」的挂起状态。
            // 恢复流程内部会先清零再调本方法，所以不会误伤自己。
            CancelPendingPptNavigationRestore();

            if (lastBorderMouseDownObject is Panel panel)
                panel.Background = new SolidColorBrush(Colors.Transparent);

            // 如果当前有选中的图片元素，先取消选中
            if (currentSelectedElement != null)
            {
                UnselectElement(currentSelectedElement);
                currentSelectedElement = null;
            }

            // 禁用高级橡皮擦系统
            DisableEraserOverlay();

            // 停止橡皮擦自动切换计时器（如果正在运行）
            StopEraserAutoSwitchBackTimer();

            bool isRealtimePenState = inkCanvas.EditingMode == InkCanvasEditingMode.None
                                      && ShouldUseRealtimeVelocityBrushTip()
                                      && string.Equals(GetCurrentSelectedMode(), "pen", StringComparison.OrdinalIgnoreCase);
            bool wasInInkMode = inkCanvas.EditingMode == InkCanvasEditingMode.Ink
                                || isRealtimePenState
                                || (Pen_Icon.Background != null
                                    && IsAnnotating
                                    && string.Equals(GetCurrentSelectedMode(), "pen", StringComparison.OrdinalIgnoreCase));
            bool wasHighlighter = drawingAttributes.IsHighlighter;

            if (drawingShapeMode != 0 && !isLongPressSelected)
            {
                return;
            }

            if (Pen_Icon.Background == null || !IsAnnotating)
            {
                if (isLongPressSelected)
                {
                    drawingShapeMode = 0;
                    isLongPressSelected = false;
                }

                // 使用集中化的工具模式切换方法
                SetCurrentToolMode(InkCanvasEditingMode.Ink);

                // 更新模式缓存
                UpdateCurrentToolMode("pen");

                GridTransparencyFakeBackground.Opacity = 1;
                GridTransparencyFakeBackground.Background = new SolidColorBrush(StringToColor("#01FFFFFF"));
                SetTransparentNotHitThrough();

                inkCanvas.IsHitTestVisible = true;
                inkCanvas.Visibility = Visibility.Visible;

                GridBackgroundCoverHolder.Visibility = Visibility.Visible;
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

                /*if (forceEraser && currentMode == 0)
                    BtnColorRed_Click(sender, null);*/

                if (GridBackgroundCover.Visibility == Visibility.Collapsed)
                {
                    if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Dark)
                    { /* Old UI removed */ }
                    else
                    { /* Old UI removed */ }
                    { /* Old UI removed */ }
                }
                else
                {
                    { /* Old UI removed */ }
                    { /* Old UI removed */ }
                }

                { /* Old UI removed */ }

                // 进入批注模式时的全屏处理（仅当未应用过全屏处理时）
                if (Settings.Advanced.IsEnableAvoidFullScreenHelper && !isFullScreenApplied && !IsInPPTPresentationMode)
                {
                    // 设置为画板模式，允许全屏操作
                    AvoidFullScreenHelper.SetBoardMode(true);
                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                        MainWindow.MoveWindow(new WindowInteropHelper(this).Handle,
                            bounds.Left, bounds.Top, bounds.Width, bounds.Height, true);
                    }), DispatcherPriority.ApplicationIdle);

                    isFullScreenApplied = true; // 标记已应用全屏处理
                }

                UpdateToolbarComponentVisibility();
                // 使用集中化的工具模式切换方法
                SetCurrentToolMode(InkCanvasEditingMode.Ink);

                UpdateCurrentToolMode("pen");

                // 注意：快捷调色盘的可见性和显示模式现在完全由工具栏系统管理
                // 不需要手动设置，UpdateToolbarComponentVisibility 会处理好

                SetFloatingBarHighlightPosition("pen");

                forceEraser = false;
                forcePointEraser = false;
                drawingShapeMode = 0;

                // 保持之前的笔类型状态，而不是强制重置
                if (!wasHighlighter && penType != 2)
                {
                    penType = 0;
                    drawingAttributes.IsHighlighter = false;
                    drawingAttributes.StylusTip = StylusTip.Ellipse;
                    Settings.Canvas.EnableInkFade = false;
                    if (_inkFadeManager != null)
                        _inkFadeManager.IsEnabled = false;
                }
                else if (penType == 1)
                {
                    drawingAttributes.IsHighlighter = !Settings.Canvas.HighlighterOverlapEnabled;
                    drawingAttributes.StylusTip = StylusTip.Rectangle;
                    drawingAttributes.Width = Settings.Canvas.HighlighterWidth / 2;
                    drawingAttributes.Height = Settings.Canvas.HighlighterWidth;
                    Settings.Canvas.EnableInkFade = false;
                    if (_inkFadeManager != null)
                        _inkFadeManager.IsEnabled = false;
                }
                // 如果之前是激光笔模式，则保持激光笔属性
                else if (penType == 2)
                {
                    drawingAttributes.IsHighlighter = false;
                    drawingAttributes.StylusTip = StylusTip.Ellipse;
                    drawingAttributes.Width = Settings.Canvas.LaserPenWidth;
                    drawingAttributes.Height = Settings.Canvas.LaserPenWidth;
                    Settings.Canvas.EnableInkFade = true;
                    if (_inkFadeManager != null)
                    {
                        _inkFadeManager.IsEnabled = true;
                        _inkFadeManager.UpdateFadeTime(Settings.Canvas.InkFadeTime);
                        _inkFadeManager.UpdateFadeSpeedMultiplier(Settings.Canvas.InkFadeSpeedMultiplier);
                    }
                }

                ColorSwitchCheck();
                HideSubPanels("pen", true);
            }
            else
            {
                if (wasInInkMode)
                {
                    if (forceEraser)
                    {
                        // 从橡皮擦模式切换过来，保持之前的笔类型状态
                        forceEraser = false;
                        forcePointEraser = false;
                        drawingShapeMode = 0;

                        // 保持之前的笔类型状态，而不是强制重置
                        if (!wasHighlighter && penType != 2)
                        {
                            penType = 0;
                            drawingAttributes.IsHighlighter = false;
                            drawingAttributes.StylusTip = StylusTip.Ellipse;
                            Settings.Canvas.EnableInkFade = false;
                            if (_inkFadeManager != null)
                                _inkFadeManager.IsEnabled = false;
                        }
                        else if (penType == 1)
                        {
                            drawingAttributes.IsHighlighter = !Settings.Canvas.HighlighterOverlapEnabled;
                            drawingAttributes.StylusTip = StylusTip.Rectangle;
                            drawingAttributes.Width = Settings.Canvas.HighlighterWidth / 2;
                            drawingAttributes.Height = Settings.Canvas.HighlighterWidth;
                            Settings.Canvas.EnableInkFade = false;
                            if (_inkFadeManager != null)
                                _inkFadeManager.IsEnabled = false;
                        }
                        // 如果之前是激光笔模式，则保持激光笔属性
                        else if (penType == 2)
                        {
                            drawingAttributes.IsHighlighter = false;
                            drawingAttributes.StylusTip = StylusTip.Ellipse;
                            drawingAttributes.Width = Settings.Canvas.LaserPenWidth;
                            drawingAttributes.Height = Settings.Canvas.LaserPenWidth;
                            Settings.Canvas.EnableInkFade = true;
                            if (_inkFadeManager != null)
                            {
                                _inkFadeManager.IsEnabled = true;
                                _inkFadeManager.UpdateFadeTime(Settings.Canvas.InkFadeTime);
                                _inkFadeManager.UpdateFadeSpeedMultiplier(Settings.Canvas.InkFadeSpeedMultiplier);
                            }
                        }

                        // 在非白板模式下，从线擦切换到批注时不直接弹出子面板
                        if (currentMode != 1)
                        {
                            HideSubPanels("pen", true);
                            return;
                        }
                    }

                    if (PenPalette.IsOpen || BoardPenPalette.IsOpen)
                    {
                        AnimationsHelper.HidePopupWithSlideAndFade(PenPalette);
                        AnimationsHelper.HidePopupWithSlideAndFade(BoardPenPalette);
                    }
                    else
                    {
                        HideSubPanels();
                        if (currentMode == 0)
                        {
                            AnimationsHelper.ShowPopupWithSlideAndFade(PenPalette);
                            _popupManager?.BringToFront(PenPalette);
                        }
                        else
                        {
                            AnimationsHelper.ShowPopupWithSlideAndFade(BoardPenPalette);
                            _popupManager?.BringToFront(BoardPenPalette);
                        }
                    }
                }
                else
                {
                    // 切换到批注模式时，确保保存当前图片信息
                    if (currentMode != 0)
                    {
                        SaveStrokes();
                    }
                    // 使用集中化的工具模式切换方法
                    SetCurrentToolMode(InkCanvasEditingMode.Ink);

                    // 更新模式缓存
                    UpdateCurrentToolMode("pen");

                    forceEraser = false;
                    forcePointEraser = false;
                    drawingShapeMode = 0;

                    // 保持之前的笔类型状态，而不是强制重置
                    if (!wasHighlighter && penType != 2)
                    {
                        penType = 0;
                        drawingAttributes.IsHighlighter = false;
                        drawingAttributes.StylusTip = StylusTip.Ellipse;
                        Settings.Canvas.EnableInkFade = false;
                        if (_inkFadeManager != null)
                            _inkFadeManager.IsEnabled = false;
                    }
                    else if (penType == 1)
                    {
                        drawingAttributes.IsHighlighter = !Settings.Canvas.HighlighterOverlapEnabled;
                        drawingAttributes.StylusTip = StylusTip.Rectangle;
                        drawingAttributes.Width = Settings.Canvas.HighlighterWidth / 2;
                        drawingAttributes.Height = Settings.Canvas.HighlighterWidth;
                        Settings.Canvas.EnableInkFade = false;
                        if (_inkFadeManager != null)
                            _inkFadeManager.IsEnabled = false;
                    }
                    // 如果之前是激光笔模式，则保持激光笔属性
                    else if (penType == 2)
                    {
                        drawingAttributes.IsHighlighter = false;
                        drawingAttributes.StylusTip = StylusTip.Ellipse;
                        drawingAttributes.Width = Settings.Canvas.LaserPenWidth;
                        drawingAttributes.Height = Settings.Canvas.LaserPenWidth;
                        Settings.Canvas.EnableInkFade = true;
                        if (_inkFadeManager != null)
                        {
                            _inkFadeManager.IsEnabled = true;
                            _inkFadeManager.UpdateFadeTime(Settings.Canvas.InkFadeTime);
                            _inkFadeManager.UpdateFadeSpeedMultiplier(Settings.Canvas.InkFadeSpeedMultiplier);
                        }
                    }

                    ColorSwitchCheck();
                    HideSubPanels("pen", true);
                }
            }


            forceEraser = false;
            forcePointEraser = false;
            drawingShapeMode = 0;
            EnsureRealtimeStylusPipelineBinding();
        }

        /// <summary>
        /// 颜色主题切换鼠标释放事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void ColorThemeSwitch_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isUselightThemeColor = !isUselightThemeColor;
            if (currentMode == 0) isDesktopUselightThemeColor = isUselightThemeColor;
            CheckColorTheme();
        }

        /// <summary>
        /// 橡皮擦图标点击事件处理，用于切换到橡皮擦模式或显示橡皮擦尺寸面板
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal void EraserIcon_Click(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到橡皮擦")) return;

            bool isAlreadyEraser = inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint;
            forceEraser = false;
            forcePointEraser = true;
            drawingShapeMode = 0;

            // 如果当前不在批注模式，先切换到批注模式
            if (!IsAnnotating)
            {
                PenIcon_Click(sender, e);
            }

            // 切换到橡皮擦模式时，确保保存当前图片信息
            if (!isAlreadyEraser && currentMode != 0)
            {
                SaveStrokes();
            }

            if (!isAlreadyEraser)
            {
                ResetTouchStates();
            }

            // 启用新的高级橡皮擦系统
            EnableEraserOverlay();

            // 使用新的高级橡皮擦系统
            // 使用集中化的工具模式切换方法
            SetCurrentToolMode(InkCanvasEditingMode.EraseByPoint);

            // 更新模式缓存
            UpdateCurrentToolMode("eraser");

            ApplyAdvancedEraserShape(); // 使用新的橡皮擦形状应用方法
            SetCursorBasedOnEditingMode(inkCanvas);
            HideSubPanels("eraser"); // 高亮橡皮按钮
            Trace.WriteLine($"Eraser: Eraser button clicked, current size: {eraserWidth}, circle: {isEraserCircleShape}");

            // 如果启用了橡皮擦自动切换功能，停止之前的计时器（如果正在运行）
            if (Settings.Canvas.EnableEraserAutoSwitchBack)
            {
                StopEraserAutoSwitchBackTimer();
            }

            if (isAlreadyEraser)
            {
                if (EraserSizePanel.IsOpen == false && BoardEraserSizePanel?.IsOpen != true)
                {
                    if (currentMode == 0)
                    {
                        AnimationsHelper.ShowPopupWithSlideAndFade(EraserSizePanel);
                        _popupManager?.BringToFront(EraserSizePanel);
                    }
                    else
                    {
                        AnimationsHelper.ShowPopupWithSlideAndFade(BoardEraserSizePanel);
                        _popupManager?.BringToFront(BoardEraserSizePanel);
                    }
                }
                else
                {
                    AnimationsHelper.HidePopupWithSlideAndFade(EraserSizePanel);
                    if (BoardEraserSizePanel != null)
                        AnimationsHelper.HidePopupWithSlideAndFade(BoardEraserSizePanel);
                }
            }
        }

        /// <summary>
        /// 白板模式下的橡皮擦图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void BoardEraserIcon_Click(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到橡皮擦")) return;

            bool isAlreadyEraser = inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint;
            forceEraser = false;
            forcePointEraser = true;
            drawingShapeMode = 0;

            // 启用新的高级橡皮擦系统
            EnableEraserOverlay();

            // 使用新的高级橡皮擦系统
            // 使用集中化的工具模式切换方法
            SetCurrentToolMode(InkCanvasEditingMode.EraseByPoint);

            // 更新模式缓存
            UpdateCurrentToolMode("eraser");

            ApplyAdvancedEraserShape(); // 使用新的橡皮擦形状应用方法
            SetCursorBasedOnEditingMode(inkCanvas);
            HideSubPanels("eraser"); // 高亮橡皮按钮

            // 如果启用了橡皮擦自动切换功能，停止之前的计时器（如果正在运行）
            if (Settings.Canvas.EnableEraserAutoSwitchBack)
            {
                StopEraserAutoSwitchBackTimer();
            }

            if (isAlreadyEraser)
            {
                if (BoardEraserSizePanel?.IsOpen != true && EraserSizePanel.IsOpen == false)
                {
                    if (currentMode == 0)
                    {
                        AnimationsHelper.ShowPopupWithSlideAndFade(EraserSizePanel);
                        _popupManager?.BringToFront(EraserSizePanel);
                    }
                    else
                    {
                        AnimationsHelper.ShowPopupWithSlideAndFade(BoardEraserSizePanel);
                        _popupManager?.BringToFront(BoardEraserSizePanel);
                    }
                }
                else
                {
                    if (BoardEraserSizePanel != null)
                        AnimationsHelper.HidePopupWithSlideAndFade(BoardEraserSizePanel);
                    AnimationsHelper.HidePopupWithSlideAndFade(EraserSizePanel);
                }
            }
        }

        /// <summary>
        /// 墨迹擦除图标点击事件处理，用于切换到按笔画擦除模式
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal void EraserIconByStrokes_Click(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到线擦")) return;

            // 如果当前不在批注模式，先切换到批注模式
            if (!IsAnnotating)
            {
                PenIcon_Click(sender, e);
            }

            // 禁用高级橡皮擦系统
            DisableEraserOverlay();

            forceEraser = true;
            forcePointEraser = false;

            inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
            // 使用集中化的工具模式切换方法
            SetCurrentToolMode(InkCanvasEditingMode.EraseByStroke);

            // 更新模式缓存
            UpdateCurrentToolMode("eraserByStrokes");

            drawingShapeMode = 0;

            // 这样从线擦切换回批注时，可以恢复之前的荧光笔状态
            // penType 和 drawingAttributes 的状态将在 PenIcon_Click 中根据 wasHighlighter 来恢复

            inkCanvas_EditingModeChanged(inkCanvas, null);
            CancelSingleFingerDragMode();

            HideSubPanels("eraserByStrokes");

        }

        /// <summary>
        /// 白板模式下的墨迹擦除图标点击事件处理，用于切换到按笔画擦除模式
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal void BoardEraserIconByStrokes_Click(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到线擦")) return;

            // 禁用高级橡皮擦系统
            DisableEraserOverlay();

            forceEraser = true;
            forcePointEraser = false;

            inkCanvas.EraserShape = new EllipseStylusShape(5, 5);
            // 使用集中化的工具模式切换方法
            SetCurrentToolMode(InkCanvasEditingMode.EraseByStroke);

            // 更新模式缓存
            UpdateCurrentToolMode("eraserByStrokes");

            drawingShapeMode = 0;

            // 这样从线擦切换回批注时，可以恢复之前的荧光笔状态
            // penType 和 drawingAttributes 的状态将在 PenIcon_Click 中根据 wasHighlighter 来恢复

            inkCanvas_EditingModeChanged(inkCanvas, null);
            CancelSingleFingerDragMode();

            HideSubPanels("eraserByStrokes");
        }

        /// <summary>
        /// 光标删除图标点击事件处理，用于删除选中内容并切换到光标模式
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal void CursorWithDelIcon_Click(object sender, MouseButtonEventArgs e)
        {
            SymbolIconDelete_MouseUp(sender, null);
            CursorIcon_Click(null, null);
        }

        /// <summary>
        /// 将当前绘笔颜色设置为白色并安排在短时间后自动恢复到之前的笔刷。
        /// </summary>
        private void QuickColorWhite_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Colors.White);
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 将快速颜色设置为橙色，并安排稍后自动恢复到先前的画笔颜色。
        /// </summary>
        private void QuickColorOrange_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Color.FromRgb(251, 150, 80)); // 橙色
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 将画笔颜色切换为黄色并安排自动恢复为先前的画笔设置。
        /// </summary>
        private void QuickColorYellow_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Colors.Yellow);
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 将快速颜色设置为黑色并安排在稍后自动恢复为先前的画笔颜色。
        /// </summary>
        private void QuickColorBlack_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Colors.Black);
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 将当前画笔颜色设置为蓝色并安排在一段时间后自动恢复到之前的画笔颜色。
        /// </summary>
        private void QuickColorBlue_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Color.FromRgb(37, 99, 235)); // 蓝色
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 将快速颜色切换为红色，并安排稍后自动恢复为先前的画笔颜色。
        /// </summary>
        private void QuickColorRed_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Colors.Red);
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 将快速颜色切换为绿色并安排在一段时间后自动恢复先前画笔颜色。
        /// </summary>
        private void QuickColorGreen_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Color.FromRgb(22, 163, 74));
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 将当前画笔颜色切换为紫色快捷色并安排自动恢复先前画笔设置。
        /// </summary>
        private void QuickColorPurple_Click(object sender, RoutedEventArgs e)
        {
            SetQuickColor(Color.FromRgb(147, 51, 234));
            ScheduleBrushAutoRestore();
        }

        internal void ApplyQuickColorByName(string colorName)
        {
            var color = colorName switch
            {
                "Black" => Colors.Black,
                "White" => Colors.White,
                "Red" => Colors.Red,
                "Orange" => Color.FromRgb(251, 150, 80),
                "Yellow" => Colors.Yellow,
                "Green" => Color.FromRgb(22, 163, 74),
                "Blue" => Color.FromRgb(37, 99, 235),
                "Purple" => Color.FromRgb(147, 51, 234),
                _ => Colors.Black
            };
            SetQuickColor(color);
            ScheduleBrushAutoRestore();
        }

        /// <summary>
        /// 设置并应用快速颜色到当前画笔与相关状态，包括必要时切换到批注模式、更新荧光笔属性与颜色索引、记录桌面/白板的最近颜色，以及刷新调色盘指示器和颜色显示。
        /// </summary>
        /// <param name="color">要应用的颜色。</param>
        /// <remarks>
        /// 此方法会：
        /// - 在非批注模式时切换到绘制（Ink）模式；
        /// - 将指定颜色应用到绘图属性和 InkCanvas 的默认绘图属性；
        /// - 在荧光笔模式下更新荧光笔的内部颜色索引与绘图属性（宽度、笔尖形状、IsHighlighter 等）；
        /// - 根据当前模式（桌面或白板）记录最近使用的颜色索引；
        /// - 更新快速调色盘的选中指示器并刷新颜色显示状态。
        /// </remarks>
        private void SetQuickColor(Color color)
        {
            // 确保当前处于批注模式
            if (inkCanvas.EditingMode != InkCanvasEditingMode.Ink)
            {
                PenIcon_Click(null, null);
            }

            // 设置画笔颜色
            drawingAttributes.Color = color;
            inkCanvas.DefaultDrawingAttributes.Color = color;

            // 如果当前是荧光笔模式，同时更新荧光笔颜色和属性
            if (penType == 1)
            {
                // 根据颜色设置对应的荧光笔颜色索引
                if (color == Colors.White || IsColorSimilar(color, Color.FromRgb(250, 250, 250), 10))
                {
                    highlighterColor = 101; // 白色荧光笔
                }
                else if (color == Colors.Black)
                {
                    highlighterColor = 100; // 黑色荧光笔
                }
                else if (color == Colors.Yellow || IsColorSimilar(color, Color.FromRgb(234, 179, 8)) ||
                         IsColorSimilar(color, Color.FromRgb(250, 204, 21)) ||
                         IsColorSimilar(color, Color.FromRgb(253, 224, 71)))
                {
                    highlighterColor = 103; // 黄色荧光笔
                }
                else if (color == Color.FromRgb(255, 165, 0) || color == Color.FromRgb(251, 150, 80) || IsColorSimilar(color, Color.FromRgb(249, 115, 22), 20) ||
                         IsColorSimilar(color, Color.FromRgb(234, 88, 12), 20) ||
                         IsColorSimilar(color, Color.FromRgb(251, 146, 60), 20) ||
                         IsColorSimilar(color, Color.FromRgb(253, 126, 20), 20))
                {
                    highlighterColor = 109; // 橙色荧光笔
                }
                else if (color == Color.FromRgb(37, 99, 235))
                {
                    highlighterColor = 106; // 蓝色荧光笔
                }
                else if (color == Colors.Red || IsColorSimilar(color, Color.FromRgb(220, 38, 38)) ||
                         IsColorSimilar(color, Color.FromRgb(239, 68, 68)))
                {
                    highlighterColor = 102; // 红色荧光笔
                }
                else if (color == Colors.Green || IsColorSimilar(color, Color.FromRgb(22, 163, 74)))
                {
                    highlighterColor = 104; // 绿色荧光笔
                }
                else if (color == Color.FromRgb(147, 51, 234))
                {
                    highlighterColor = 107; // 紫色荧光笔
                }

                // 确保荧光笔属性正确设置
                drawingAttributes.Width = Settings.Canvas.HighlighterWidth / 2;
                drawingAttributes.Height = Settings.Canvas.HighlighterWidth;
                drawingAttributes.StylusTip = StylusTip.Rectangle;
                drawingAttributes.IsHighlighter = !Settings.Canvas.HighlighterOverlapEnabled;

                inkCanvas.DefaultDrawingAttributes.Width = Settings.Canvas.HighlighterWidth / 2;
                inkCanvas.DefaultDrawingAttributes.Height = Settings.Canvas.HighlighterWidth;
                inkCanvas.DefaultDrawingAttributes.StylusTip = StylusTip.Rectangle;
                inkCanvas.DefaultDrawingAttributes.IsHighlighter = !Settings.Canvas.HighlighterOverlapEnabled;

                // 确保荧光笔颜色索引正确更新
                inkCanvas.DefaultDrawingAttributes.Color = drawingAttributes.Color;
            }

            // 更新颜色状态
            if (currentMode == 0)
            {
                // 桌面模式
                if (color == Colors.White) lastDesktopInkColor = 5;
                else if (color == Color.FromRgb(251, 150, 80)) lastDesktopInkColor = 8; // 橙色
                else if (color == Colors.Yellow) lastDesktopInkColor = 4;
                else if (color == Colors.Black) lastDesktopInkColor = 0;
                else if (color == Color.FromRgb(37, 99, 235)) lastDesktopInkColor = 3; // 蓝色
                else if (color == Colors.Red) lastDesktopInkColor = 1;
                else if (color == Colors.Green || color == Color.FromRgb(22, 163, 74)) lastDesktopInkColor = 2;
                else if (color == Color.FromRgb(147, 51, 234)) lastDesktopInkColor = 6; // 紫色
            }
            else
            {
                // 白板模式
                if (color == Colors.White) lastBoardInkColor = 5;
                else if (color == Color.FromRgb(251, 150, 80)) lastBoardInkColor = 8; // 橙色
                else if (color == Colors.Yellow) lastBoardInkColor = 4;
                else if (color == Colors.Black) lastBoardInkColor = 0;
                else if (color == Color.FromRgb(37, 99, 235)) lastBoardInkColor = 3; // 蓝色
                else if (color == Colors.Red) lastBoardInkColor = 1;
                else if (color == Colors.Green || color == Color.FromRgb(22, 163, 74)) lastBoardInkColor = 2;
                else if (color == Color.FromRgb(147, 51, 234)) lastBoardInkColor = 6; // 紫色
            }

            // 更新快捷调色盘选择指示器
            UpdateQuickColorPaletteIndicator(color);

            // 更新颜色显示
            ColorSwitchCheck();

            // 如果当前是荧光笔模式，调用ColorSwitchCheck确保颜色索引正确更新
            if (penType == 1)
            {
                ColorSwitchCheck();
            }
        }

        /// <summary>
        /// 更新快速调色盘的选中指示器，根据当前选中的颜色显示对应的勾选图标
        /// </summary>
        /// <param name="selectedColor">当前选中的颜色</param>
        private void UpdateQuickColorPaletteIndicator(Color selectedColor)
        {
            var qcp = QuickColorPalette;
            if (qcp == null)
            {
                return;
            }

            int tolerance = (penType == 1) ? 25 : 15;
            qcp.ClearAllChecked();
            qcp.SetCheckedByColor(selectedColor, tolerance);
        }

        /// <summary>
        /// 检查两个颜色是否相似（允许一定的误差范围）
        /// </summary>
        private bool IsColorSimilar(Color color1, Color color2, int tolerance = 15)
        {
            int rDiff = Math.Abs(color1.R - color2.R);
            int gDiff = Math.Abs(color1.G - color2.G);
            int bDiff = Math.Abs(color1.B - color2.B);

            return rDiff <= tolerance && gDiff <= tolerance && bDiff <= tolerance;
        }

        /// <summary>
        /// 选择工具图标鼠标释放事件处理，用于切换到选择模式或选择所有墨迹
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void SelectIcon_MouseUp(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("切换到选择工具")) return;

            // 禁用高级橡皮擦系统
            DisableEraserOverlay();

            forceEraser = true;
            drawingShapeMode = 0;
            inkCanvas.IsManipulationEnabled = false;
            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
            {
                var selectedStrokes = new StrokeCollection();
                foreach (var stroke in inkCanvas.Strokes)
                    if (stroke.GetBounds().Width > 0 && stroke.GetBounds().Height > 0)
                        selectedStrokes.Add(stroke);
                inkCanvas.Select(selectedStrokes);
            }
            else
            {
                // 使用集中化的工具模式切换方法
                SetCurrentToolMode(InkCanvasEditingMode.Select);
            }
        }

        /// <summary>
        /// 从图形绘制模式切换到画笔模式的提示处理
        /// </summary>
        private void DrawShapePromptToPen()
        {
            if (isLongPressSelected)
            {
                // 如果是长按选中的状态，只隐藏面板，不切换到笔模式
                HideSubPanels("shape");
            }
            else
            {
                if (IsAnnotating)
                    HideSubPanels("pen");
                else
                    HideSubPanels("cursor");
            }
        }

        /// <summary>
        /// 关闭工具面板鼠标释放事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void CloseBordertools_MouseUp(object sender, MouseButtonEventArgs e)
        {
            HideSubPanels();
        }

        private void CloseBordertools_Click(object sender, RoutedEventArgs e)
        {
            HideSubPanels();
        }
    }
}
