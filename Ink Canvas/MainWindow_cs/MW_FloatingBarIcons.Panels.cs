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
        // LTSC（2026-11-17）：从 MW_FloatingBarIcons.cs 拆出的若干小段。
        // 这些段各自独立、共享字段少（每段 ≤1 个自有字段），合在一个文件里便于查找。
        // 只搬代码，不改逻辑。using 区整块复制（原文有 7 条别名）。
        #region Left Side Panel

        /// <summary>
        /// 手指拖动模式切换按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        public void ToggleFingerDragMode(object sender, RoutedEventArgs e)
        {
            if (isSingleFingerDragMode)
            {
                isSingleFingerDragMode = false;
                { /* Old UI removed */ }
            }
            else
            {
                isSingleFingerDragMode = true;
                { /* Old UI removed */ }
            }
        }

        /// <summary>
        /// 撤销按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void BtnUndo_Click(object sender, RoutedEventArgs e)
        {
            if (inkCanvas.GetSelectedStrokes().Count != 0)
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                inkCanvas.Select(new StrokeCollection());
            }

            var item = timeMachine.Undo();
            ApplyHistoryToCanvas(item);
        }

        /// <summary>
        /// 重做按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void BtnRedo_Click(object sender, RoutedEventArgs e)
        {
            if (inkCanvas.GetSelectedStrokes().Count != 0)
            {
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
                inkCanvas.Select(new StrokeCollection());
            }

            var item = timeMachine.Redo();
            ApplyHistoryToCanvas(item);
        }

        /// <summary>
        /// 按钮启用状态变更事件处理，用于更新按钮内容的透明度
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">依赖属性变更事件参数</param>
        private void Btn_IsEnabledChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (!isLoaded) return;
            try
            {
                if (((Button)sender).IsEnabled)
                    ((UIElement)((Button)sender).Content).Opacity = 1;
                else
                    ((UIElement)((Button)sender).Content).Opacity = 0.25;
            }
            catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
        }

        #endregion Left Side Panel

        #region 清空畫布按鈕

        /// <summary>
        /// 清空画布按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        internal void SymbolIconDelete_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("清除冻结页面内容")) return;
            if (inkCanvas.GetSelectedStrokes().Count > 0)
            {
                inkCanvas.Strokes.Remove(inkCanvas.GetSelectedStrokes());
                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;
            }
            else if (inkCanvas.Strokes.Count > 0)
            {
                if (Settings.Automation.IsAutoSaveScreenshotAtClear &&
                    inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber)
                {
                    if (IsInPPTPresentationMode)
                    {
                        var currentSlide = Math.Max(0, _pptManager?.GetCurrentSlideNumber() ?? 0);
                        var presentationName = _pptManager?.GetPresentationName() ?? "";
                        CaptureAndEnqueueScreenshotSave(true, $"{presentationName}/{currentSlide}_{DateTime.Now:HH-mm-ss}");
                    }
                    else
                        CaptureAndEnqueueScreenshotSave(true);
                }

                BtnClear_Click(null, null);
            }
        }

        #endregion

        #region 白板按鈕和退出白板模式按鈕

        /// <summary>
        /// 是否正在显示或隐藏黑板
        /// </summary>
        private bool isDisplayingOrHidingBlackboard;

        /// <summary>
        /// 白板按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        internal void ImageBlackboard_MouseUp(object sender, MouseButtonEventArgs e)
        {

            LeftUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
            RightUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
            if (isDisplayingOrHidingBlackboard) return;
            isDisplayingOrHidingBlackboard = true;

            UnFoldFloatingBar_MouseUp(null, null);

            if (inkCanvas.EditingMode == InkCanvasEditingMode.Select) PenIcon_Click(null, null);

            if (currentMode == 0)
            {
                LeftBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                RightBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                LeftSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                RightSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                //進入黑板

                /*
                if (Not_Enter_Blackboard_fir_Mouse_Click) {// BUG-Fixed_tmp：程序启动后直接进入白板会导致后续撤销功能、退出白板无法恢复墨迹
                    BtnColorRed_Click(BorderPenColorRed, null);
                    await Task.Delay(200);
                    SimulateMouseClick.SimulateMouseClickAtTopLeft();
                    await Task.Delay(10);
                    Not_Enter_Blackboard_fir_Mouse_Click = false;
                }
                */
                new Thread(() =>
                {
                    Thread.Sleep(100);
                    Application.Current.Dispatcher.Invoke(() => { ViewboxFloatingBarMarginAnimation(60); });
                }).Start();

                HideSubPanels();

                if (GridTransparencyFakeBackground.Background == Brushes.Transparent)
                {
                    if (currentMode == 1)
                    {
                        currentMode = 0;
                        AutomationBootstrap.Monitor?.NotifyInternalStateChanged();
                        GridBackgroundCover.Visibility = Visibility.Collapsed;
                        AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);
                    }

                    BtnHideInkCanvas_Click(null, null);
                }

                if (Settings.Appearance.EnableTimeDisplayInWhiteboardMode)
                {
                    WaterMarkTime.Visibility = Visibility.Visible;
                    WaterMarkDate.Visibility = Visibility.Visible;
                }
                else
                {
                    WaterMarkTime.Visibility = Visibility.Collapsed;
                    WaterMarkDate.Visibility = Visibility.Collapsed;
                }

                if (Settings.Appearance.EnableChickenSoupInWhiteboardMode)
                {
                    ApplyChickenSoupPosition();
                    BlackBoardWaterMark.Visibility = Visibility.Visible;
                }
                else
                {
                    BlackBoardWaterMark.Visibility = Visibility.Collapsed;
                }

                _ = UpdateChickenSoupTextAsync().ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        try
                        {
                            LogHelper.WriteLogToFile($"进入白板模式时更新名言失败: {t.Exception?.GetBaseException().Message}", LogHelper.LogType.Warning);
                        }
                        catch
                        {
                        }
                        if (Settings.Appearance.EnableChickenSoupInWhiteboardMode && Settings.Appearance.ChickenSoupSource != 3)
                        {
                            try
                            {
                                if (Settings.Appearance.ChickenSoupSource == 0)
                                {
                                    int randChickenSoupIndex = new Random().Next(ChickenSoup.OSUPlayerYuLu.Length);
                                    BlackBoardWaterMark.Text = ChickenSoup.OSUPlayerYuLu[randChickenSoupIndex];
                                }
                                else if (Settings.Appearance.ChickenSoupSource == 1)
                                {
                                    int randChickenSoupIndex = new Random().Next(ChickenSoup.MingYanJingJu.Length);
                                    BlackBoardWaterMark.Text = ChickenSoup.MingYanJingJu[randChickenSoupIndex];
                                }
                                else if (Settings.Appearance.ChickenSoupSource == 2)
                                {
                                    int randChickenSoupIndex = new Random().Next(ChickenSoup.GaoKaoPhrases.Length);
                                    BlackBoardWaterMark.Text = ChickenSoup.GaoKaoPhrases[randChickenSoupIndex];
                                }
                                else if (Settings.Appearance.ChickenSoupSource == 4)
                                {
                                    int randChickenSoupIndex = new Random().Next(ChickenSoup.PhigrosTips.Length);
                                    BlackBoardWaterMark.Text = ChickenSoup.PhigrosTips[randChickenSoupIndex];
                                }
                            }
                            catch
                            {
                                BlackBoardWaterMark.Visibility = Visibility.Collapsed;
                            }
                        }
                        else if (Settings.Appearance.EnableChickenSoupInWhiteboardMode && Settings.Appearance.ChickenSoupSource == 3)
                        {
                            BlackBoardWaterMark.Text = Properties.MainWindowStrings.Main_Hitokoto_Unavailable;
                        }
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
                StartChickenSoupAutoRotation();

                if (Settings.Canvas.UsingWhiteboard)
                {
                    ICCWaterMarkDark.Visibility = Visibility.Visible;
                    ICCWaterMarkWhite.Visibility = Visibility.Collapsed;
                }
                else
                {
                    ICCWaterMarkWhite.Visibility = Visibility.Visible;
                    ICCWaterMarkDark.Visibility = Visibility.Collapsed;
                }

                ViewboxFloatingBar.Visibility = Visibility.Collapsed;
            }
            else
            {
                //关闭黑板
                PauseAllCanvasMediaPlayback();
                HideSubPanelsImmediately();

                // 只有在PPT放映模式下且页数有效时才显示翻页按钮
                if (ArePPTControlsVisible &&
                    IsInPPTPresentationMode &&
                    PPTManager?.IsInSlideShow == true &&
                    PPTManager?.SlidesCount > 0)
                {
                    var dops = Settings.PowerPointSettings.PPTButtonsDisplayOption.ToString();
                    var dopsc = dops.ToCharArray();
                    if (dopsc[0] == '2' && !isDisplayingOrHidingBlackboard) AnimationsHelper.ShowWithFadeIn(LeftBottomPanelForPPTNavigation);
                    if (dopsc[1] == '2' && !isDisplayingOrHidingBlackboard) AnimationsHelper.ShowWithFadeIn(RightBottomPanelForPPTNavigation);
                    if (dopsc[2] == '2' && !isDisplayingOrHidingBlackboard) AnimationsHelper.ShowWithFadeIn(LeftSidePanelForPPTNavigation);
                    if (dopsc[3] == '2' && !isDisplayingOrHidingBlackboard) AnimationsHelper.ShowWithFadeIn(RightSidePanelForPPTNavigation);
                }
                else
                {
                    // 如果不在放映模式或页数无效，隐藏所有翻页按钮
                    LeftBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    RightBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    LeftSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    RightSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                }

                // 使用PPT UI管理器来正确更新翻页按钮显示状态，确保遵循用户设置
                _pptUIManager?.UpdateNavigationPanelsVisibility();

                if (Settings.Automation.IsAutoSaveScreenshotAtClear &&
                    inkCanvas.Strokes.Count > Settings.Automation.MinimumAutomationStrokeNumber) CaptureAndEnqueueScreenshotSave(true);

                if (!IsInPPTPresentationMode)
                    new Thread(() =>
                    {
                        Thread.Sleep(300);
                        Application.Current.Dispatcher.Invoke(() => { ViewboxFloatingBarMarginAnimation(100, true); });
                    }).Start();
                else
                    new Thread(() =>
                    {
                        Thread.Sleep(300);
                        Application.Current.Dispatcher.Invoke(() => { ViewboxFloatingBarMarginAnimation(60); });
                    }).Start();

                if (GetSelectionBGLeft() != 28) PenIcon_Click(null, null);

                StopChickenSoupAutoRotation();
                WaterMarkTime.Visibility = Visibility.Collapsed;
                WaterMarkDate.Visibility = Visibility.Collapsed;
                BlackBoardWaterMark.Visibility = Visibility.Collapsed;
                ICCWaterMarkDark.Visibility = Visibility.Collapsed;
                ICCWaterMarkWhite.Visibility = Visibility.Collapsed;

                // 新增：退出白板模式时恢复基础浮动栏的显示
                ViewboxFloatingBar.Visibility = Visibility.Visible;
            }

            SwitchBackground(null, null);

            if (currentMode == 0)
            {
                // 根据当前编辑模式正确设置工具模式和高光位置
                if (inkCanvas.EditingMode == InkCanvasEditingMode.None)
                {
                    UpdateCurrentToolMode("cursor");
                    SetFloatingBarHighlightPosition("cursor");
                }
                else if (inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                {
                    UpdateCurrentToolMode("pen");
                    SetFloatingBarHighlightPosition("pen");
                }
                else if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint)
                {
                    UpdateCurrentToolMode("eraser");
                    SetFloatingBarHighlightPosition("eraser");
                }
                else if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByStroke)
                {
                    UpdateCurrentToolMode("eraserByStrokes");
                    SetFloatingBarHighlightPosition("eraserByStrokes");
                }
                else if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
                {
                    UpdateCurrentToolMode("select");
                    SetFloatingBarHighlightPosition("select");
                }
            }

            if (currentMode == 0 && inkCanvas.Strokes.Count == 0 && !IsInPPTPresentationMode)
                CursorIcon_Click(null, null);

            { /* Old UI removed */ }
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;

            new Thread(() =>
            {
                Thread.Sleep(200);
                Application.Current.Dispatcher.Invoke(() => { isDisplayingOrHidingBlackboard = false; });
            }).Start();

            SwitchToDefaultPen(null, null);
            CheckColorTheme(true);
        }

        #endregion

        #region 撤銷重做按鈕

        /// <summary>
        /// 撤销按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        internal void SymbolIconUndo_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("撤销冻结页面内容")) return;
            if (!IsUndoEnabled) return;
            BtnUndo_Click(null, null);
            HideSubPanels();
        }

        /// <summary>
        /// 重做按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        internal void SymbolIconRedo_MouseUp(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("重做冻结页面内容")) return;
            if (!IsRedoEnabled) return;
            BtnRedo_Click(null, null);
            HideSubPanels();
        }

        #endregion

        #region 隱藏子面板和按鈕背景高亮

        /// <summary>
        /// 隐藏形状绘制面板
        /// </summary>
        private void CollapseBorderDrawShape()
        {
            AnimationsHelper.HidePopupWithSlideAndFade(BorderDrawShape);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderDrawShape);
        }

        /// <summary>
        /// HideSubPanels的简化版，立即隐藏所有子面板，无动画效果
        /// </summary>
        private void HideSubPanelsImmediately()
        {
            BorderTools.IsOpen = false;
            BoardBorderToolsPopup.IsOpen = false;
            PenPalette.IsOpen = false;
            BoardPenPalette.IsOpen = false;
            BoardEraserSizePanel.IsOpen = false;
            EraserSizePanel.IsOpen = false;
            var leftBorder = FindView("board.pageList.leftBorder") as Border;
            var rightBorder = FindView("board.pageList.rightBorder") as Border;
            if (leftBorder != null) leftBorder.Visibility = Visibility.Collapsed;
            if (rightBorder != null) rightBorder.Visibility = Visibility.Collapsed;
            BoardImageOptionsPanel.IsOpen = false;
            TwoFingerGestureBorder.IsOpen = false;
            BoardTwoFingerGestureBorder.IsOpen = false;
            // 添加隐藏图形工具的二级菜单面板
            BorderDrawShape.IsOpen = false;
            BoardBorderDrawShape.IsOpen = false;

            BackgroundPalette.IsOpen = false;
        }

        /// <summary>
        ///     <para>
        ///         易嚴定真，這個多功能函數包括了以下的內容：
        ///     </para>
        ///     <list type="number">
        ///         <item>
        ///             隱藏浮動工具欄和白板模式下的"更多功能"面板
        ///         </item>
        ///         <item>
        ///             隱藏白板模式下和浮動工具欄的畫筆調色盤
        ///         </item>
        ///         <item>
        ///             隱藏白板模式下的"清屏"按鈕（已作廢）
        ///         </item>
        ///         <item>
        ///             負責給Settings設置面板做隱藏動畫
        ///         </item>
        ///         <item>
        ///             隱藏白板模式下和浮動工具欄的"手勢"面板
        ///         </item>
        ///         <item>
        ///             當<c>ToggleSwitchDrawShapeBorderAutoHide</c>開啟時，會自動隱藏白板模式下和浮動工具欄的"形狀"面板
        ///         </item>
        ///         <item>
        ///             按需高亮指定的浮動工具欄和白板工具欄中的按鈕，通過param：<paramref name="mode"/> 來指定
        ///         </item>
        ///         <item>
        ///             將浮動工具欄自動居中，通過param：<paramref name="autoAlignCenter"/>
        ///         </item>
        ///     </list>
        /// </summary>
        /// <param name="mode">
        ///     <para>
        ///         按需高亮指定的浮動工具欄和白板工具欄中的按鈕，有下面幾種情況：
        ///     </para>
        ///     <list type="number">
        ///         <item>
        ///             當<c><paramref name="mode"/>==null</c>時，不會執行任何有關操作
        ///         </item>
        ///         <item>
        ///             當<c><paramref name="mode"/>!="clear"</c>時，會先取消高亮所有工具欄按鈕，然後根據下面的情況進行高亮處理
        ///         </item>
        ///         <item>
        ///             當<c><paramref name="mode"/>=="color" || <paramref name="mode"/>=="pen"</c>時，會高亮浮動工具欄和白板工具欄中的"批註"，"筆"按鈕
        ///         </item>
        ///         <item>
        ///             當<c><paramref name="mode"/>=="eraser"</c>時，會高亮白板工具欄中的"橡皮"和浮動工具欄中的"面積擦"按鈕
        ///         </item>
        ///         <item>
        ///             當<c><paramref name="mode"/>=="eraserByStrokes"</c>時，會高亮白板工具欄中的"橡皮"和浮動工具欄中的"墨跡擦"按鈕
        ///         </item>
        ///         <item>
        ///             當<c><paramref name="mode"/>=="select"</c>時，會高亮浮動工具欄和白板工具欄中的"選擇"，"套索選"按鈕
        ///         </item>
        ///     </list>
        /// </param>
        /// <param name="autoAlignCenter">
        ///     是否自動居中浮動工具欄
        /// </param>
        internal async void HideSubPanels(string mode = null, bool autoAlignCenter = false)
        {
            mode = NormalizeToolModeForFreeze(mode);

            var boardPen = FindView("board.pen") as BoardToolbarButton;
            var boardEraser = FindView("board.eraser") as BoardToolbarButton;
            var boardStrokeEraser = FindView("board.strokeEraser") as BoardToolbarButton;
            var boardSelect = FindView("board.select") as BoardToolbarButton;

            AnimationsHelper.HidePopupWithSlideAndFade(BorderTools);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderToolsPopup);
            AnimationsHelper.HidePopupWithSlideAndFade(PenPalette);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardPenPalette);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardEraserSizePanel);
            AnimationsHelper.HidePopupWithSlideAndFade(EraserSizePanel);
            AnimationsHelper.HidePopupWithSlideAndFade(BorderDrawShape);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderDrawShape);
            var leftBorderAnim = FindView("board.pageList.leftBorder") as Border;
            var rightBorderAnim = FindView("board.pageList.rightBorder") as Border;
            if (leftBorderAnim != null) AnimationsHelper.HideWithSlideAndFade(leftBorderAnim);
            if (rightBorderAnim != null) AnimationsHelper.HideWithSlideAndFade(rightBorderAnim);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardImageOptionsPanel);
            AnimationsHelper.HidePopupWithSlideAndFade(TwoFingerGestureBorder);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardTwoFingerGestureBorder);

            AnimationsHelper.HidePopupWithSlideAndFade(BackgroundPalette);

            if (mode != null)
            {
                if (mode != "clear")
                {
                    if (Cursor_Icon != null) { if (!ToolbarRegistry.GetUseRedStyle(Cursor_Icon)) Cursor_Icon.Icon.Brush = GetCachedIconBrush(FloatBarForegroundColor); Cursor_Icon.Icon.Geometry = GetCachedIconGeometry("cursor", false); }
                    if (Pen_Icon != null) { if (!ToolbarRegistry.GetUseRedStyle(Pen_Icon)) Pen_Icon.Icon.Brush = GetCachedIconBrush(FloatBarForegroundColor); Pen_Icon.Icon.Geometry = GetCachedIconGeometry("pen", false); }
                    if (EraserByStrokes_Icon != null) { if (!ToolbarRegistry.GetUseRedStyle(EraserByStrokes_Icon)) EraserByStrokes_Icon.Icon.Brush = GetCachedIconBrush(FloatBarForegroundColor); EraserByStrokes_Icon.Icon.Geometry = GetCachedIconGeometry("eraserStroke", false); }
                    if (Eraser_Icon != null) { if (!ToolbarRegistry.GetUseRedStyle(Eraser_Icon)) Eraser_Icon.Icon.Brush = GetCachedIconBrush(FloatBarForegroundColor); Eraser_Icon.Icon.Geometry = GetCachedIconGeometry("eraserCircle", false); }
                    if (SymbolIconSelect != null) { if (!ToolbarRegistry.GetUseRedStyle(SymbolIconSelect)) SymbolIconSelect.Icon.Brush = GetCachedIconBrush(FloatBarForegroundColor); SymbolIconSelect.Icon.Geometry = GetCachedIconGeometry("lassoSelect", false); }

                    bool isDarkThemeForButtons = Settings.Appearance.Theme == 1 ||
                                                 (Settings.Appearance.Theme == 2 && !ThemeHelper.IsSystemThemeLight());
                    if (isDarkThemeForButtons)
                    {
                        if (boardPen != null) { boardPen.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardPen.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardPen.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardPen.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); }
                        if (boardSelect != null) { boardSelect.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardSelect.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardSelect.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardSelect.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); }
                        if (boardEraser != null) { boardEraser.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardEraser.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); }
                        if (boardStrokeEraser != null) { boardStrokeEraser.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardStrokeEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardStrokeEraser.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardStrokeEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); }
                    }
                    else
                    {
                        if (boardPen != null) { boardPen.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardPen.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardPen.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardPen.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); }
                        if (boardSelect != null) { boardSelect.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardSelect.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardSelect.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardSelect.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); }
                        if (boardEraser != null) { boardEraser.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardEraser.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); }
                        if (boardStrokeEraser != null) { boardStrokeEraser.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardStrokeEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardStrokeEraser.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardStrokeEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); }
                    }
                }

                // 根据主题选择高光颜色
                Color highlightColor;
                bool isDarkTheme = Settings.Appearance.Theme == 1 ||
                                   (Settings.Appearance.Theme == 2 && !ThemeHelper.IsSystemThemeLight());

                if (isDarkTheme)
                {
                    highlightColor = Color.FromRgb(102, 204, 255); // #66ccff for dark theme
                }
                else
                {
                    highlightColor = Color.FromRgb(30, 58, 138); // Keep current color for light theme
                }

                switch (mode)
                {
                    case "pen":
                    case "color":
                        {
                            if (Pen_Icon != null && Pen_Icon.Icon != null)
                            {
                                if (!ToolbarRegistry.GetUseRedStyle(Pen_Icon))
                                {
                                    if (Settings.Appearance.ShowPenColorOnFloatingBarIcon)
                                        Pen_Icon.Icon.Brush = new SolidColorBrush(inkCanvas.DefaultDrawingAttributes.Color);
                                    else
                                        Pen_Icon.Icon.Brush = new SolidColorBrush(highlightColor);
                                }
                                Pen_Icon.Icon.Geometry = GetCachedIconGeometry("pen", true);
                            }
                            if (boardPen != null)
                            {
                                boardPen.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardPen.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardPen.IconGeometryDrawing.Brush = new SolidColorBrush(Colors.GhostWhite);
                                boardPen.Foreground = new SolidColorBrush(Colors.GhostWhite);
                            }

                            SetFloatingBarHighlightPosition("pen");
                            break;
                        }
                    case "eraser":
                        {
                            if (Eraser_Icon != null && Eraser_Icon.Icon != null)
                            {
                                if (!ToolbarRegistry.GetUseRedStyle(Eraser_Icon))
                                    Eraser_Icon.Icon.Brush = new SolidColorBrush(highlightColor);
                                Eraser_Icon.Icon.Geometry =
                                    GetCachedIconGeometry("eraserCircle", true);
                            }
                            if (boardEraser != null)
                            {
                                boardEraser.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Colors.GhostWhite);
                                boardEraser.Foreground = new SolidColorBrush(Colors.GhostWhite);
                            }

                            SetFloatingBarHighlightPosition("eraser");
                            break;
                        }
                    case "eraserByStrokes":
                        {
                            if (EraserByStrokes_Icon != null && EraserByStrokes_Icon.Icon != null)
                            {
                                if (!ToolbarRegistry.GetUseRedStyle(EraserByStrokes_Icon))
                                    EraserByStrokes_Icon.Icon.Brush = new SolidColorBrush(highlightColor);
                                EraserByStrokes_Icon.Icon.Geometry =
                                    GetCachedIconGeometry("eraserStroke", true);
                            }
                            if (boardStrokeEraser != null)
                            {
                                boardStrokeEraser.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardStrokeEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardStrokeEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Colors.GhostWhite);
                                boardStrokeEraser.Foreground = new SolidColorBrush(Colors.GhostWhite);
                            }

                            SetFloatingBarHighlightPosition("eraserByStrokes");
                            break;
                        }
                    case "select":
                        {
                            if (SymbolIconSelect != null && SymbolIconSelect.Icon != null)
                            {
                                if (!ToolbarRegistry.GetUseRedStyle(SymbolIconSelect))
                                    SymbolIconSelect.Icon.Brush = new SolidColorBrush(highlightColor);
                                SymbolIconSelect.Icon.Geometry =
                                    GetCachedIconGeometry("lassoSelect", true);
                            }
                            if (boardSelect != null)
                            {
                                boardSelect.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardSelect.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                                boardSelect.IconGeometryDrawing.Brush = new SolidColorBrush(Colors.GhostWhite);
                                boardSelect.Foreground = new SolidColorBrush(Colors.GhostWhite);
                            }

                            SetFloatingBarHighlightPosition("select");
                            break;
                        }
                    case "cursor":
                        {
                            if (Cursor_Icon != null && Cursor_Icon.Icon != null)
                            {
                                if (!ToolbarRegistry.GetUseRedStyle(Cursor_Icon))
                                    Cursor_Icon.Icon.Brush = new SolidColorBrush(highlightColor);
                                Cursor_Icon.Icon.Geometry =
                                    GetCachedIconGeometry("cursor", true);
                            }
                            bool isDarkThemeForCursor = Settings.Appearance.Theme == 1 ||
                                                        (Settings.Appearance.Theme == 2 && !ThemeHelper.IsSystemThemeLight());
                            if (isDarkThemeForCursor)
                            {
                                if (boardPen != null) { boardPen.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardPen.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); boardPen.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardPen.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); }
                                if (boardEraser != null) { boardEraser.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); boardEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardEraser.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); }
                                if (boardStrokeEraser != null) { boardStrokeEraser.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardStrokeEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); boardStrokeEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardStrokeEraser.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); }
                                if (boardSelect != null) { boardSelect.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42)); boardSelect.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85)); boardSelect.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(255, 255, 255)); boardSelect.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255)); }

                                if (BoardInkFreezeBtn != null)
                                {
                                    BoardInkFreezeBtn.Background = new SolidColorBrush(Color.FromRgb(42, 42, 42));
                                    BoardInkFreezeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(85, 85, 85));
                                    BoardInkFreezeBtn.IconBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                                    BoardInkFreezeBtn.Foreground = new SolidColorBrush(Color.FromRgb(255, 255, 255));
                                }
                            }
                            else
                            {
                                if (boardPen != null) { boardPen.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardPen.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); boardPen.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardPen.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); }
                                if (boardEraser != null) { boardEraser.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); boardEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardEraser.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); }
                                if (boardStrokeEraser != null) { boardStrokeEraser.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardStrokeEraser.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); boardStrokeEraser.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardStrokeEraser.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); }
                                if (boardSelect != null) { boardSelect.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245)); boardSelect.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170)); boardSelect.IconGeometryDrawing.Brush = new SolidColorBrush(Color.FromRgb(24, 24, 27)); boardSelect.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27)); }

                                if (BoardInkFreezeBtn != null)
                                {
                                    BoardInkFreezeBtn.Background = new SolidColorBrush(Color.FromRgb(244, 244, 245));
                                    BoardInkFreezeBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(161, 161, 170));
                                    BoardInkFreezeBtn.IconBrush = new SolidColorBrush(Color.FromRgb(24, 24, 27));
                                    BoardInkFreezeBtn.Foreground = new SolidColorBrush(Color.FromRgb(24, 24, 27));
                                }
                            }

                            SetFloatingBarHighlightPosition("cursor");
                            break;
                        }
                    case "shape":
                        {
                            break;
                        }
                }


                if (autoAlignCenter) // 控制居中
                {
                    if (IsInPPTPresentationMode)
                    {
                        await Task.Delay(50);
                        ViewboxFloatingBarMarginAnimation(60);
                    }
                    else if (Topmost) //非黑板
                    {
                        await Task.Delay(50);
                        ViewboxFloatingBarMarginAnimation(100, true);
                    }
                    else //黑板
                    {
                        await Task.Delay(50);
                        ViewboxFloatingBarMarginAnimation(60);
                    }
                }
            }

            await Task.Delay(150);
            isHidingSubPanelsWhenInking = false;
        }

        #endregion

        #region "手勢"按鈕

        /// <summary>
        /// 用於浮動工具欄的"手勢"按鈕和白板工具欄的"手勢"按鈕的點擊事件
        /// </summary>
        internal void TwoFingerGestureBorder_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TwoFingerGestureBorder.IsOpen || BoardTwoFingerGestureBorder.IsOpen)
            {
                AnimationsHelper.HidePopupWithSlideAndFade(TwoFingerGestureBorder);
                AnimationsHelper.HidePopupWithSlideAndFade(BoardTwoFingerGestureBorder);
            }
            else
            {
                HideSubPanels();
                if (currentMode == 0)
                {
                    AnimationsHelper.ShowPopupWithSlideAndFade(TwoFingerGestureBorder);
                    _popupManager?.BringToFront(TwoFingerGestureBorder);
                }
                else
                {
                    AnimationsHelper.ShowPopupWithSlideAndFade(BoardTwoFingerGestureBorder);
                    _popupManager?.BringToFront(BoardTwoFingerGestureBorder);
                }
            }
        }

        /// <summary>
        /// 用於更新浮動工具欄的"手勢"按鈕和白板工具欄的"手勢"按鈕的樣式（開啟和關閉狀態）
        /// </summary>
        private void CheckEnableTwoFingerGestureBtnColorPrompt()
        {
            // 根据主题选择手势图标和颜色
            bool isDarkTheme = Settings.Appearance.Theme == 1 ||
                               (Settings.Appearance.Theme == 2 && !ThemeHelper.IsSystemThemeLight());
            bool isLightTheme = !isDarkTheme;
            string gestureIconPath = isLightTheme ? "/Resources/new-icons/gesture.png" : "/Resources/new-icons/gesture_white.png";

            // 根据主题设置白板模式下的颜色
            Color boardBgColor, boardIconColor, boardTextColor, boardBorderColor;
            if (isLightTheme)
            {
                boardBgColor = Color.FromRgb(244, 244, 245);
                boardIconColor = Color.FromRgb(24, 24, 27);
                boardTextColor = Color.FromRgb(24, 24, 27);
                boardBorderColor = Color.FromRgb(161, 161, 170);
            }
            else
            {
                boardBgColor = Color.FromRgb(39, 39, 42);
                boardIconColor = Color.FromRgb(244, 244, 245);
                boardTextColor = Color.FromRgb(244, 244, 245);
                boardBorderColor = Color.FromRgb(113, 113, 122);
            }

            bool floatingBarAnyOn = Settings.Gesture.IsEnableMultiTouchMode
                || Settings.Gesture.IsEnableTwoFingerZoom
                || Settings.Gesture.IsEnableTwoFingerTranslate
                || Settings.Gesture.IsEnableTwoFingerRotation;
            bool boardAnyOn = Settings.Gesture.IsEnableMultiTouchModeBoard
                || Settings.Gesture.IsEnableTwoFingerZoomBoard
                || Settings.Gesture.IsEnableTwoFingerTranslateBoard
                || Settings.Gesture.IsEnableTwoFingerRotationBoard;

            TwoFingerGestureSimpleStackPanel.Opacity = 1;
            TwoFingerGestureSimpleStackPanel.IsHitTestVisible = true;

            if (floatingBarAnyOn)
            {
                if (Gesture_Icon != null)
                {
                    Gesture_Icon.Icon.Geometry = Geometry.Parse(XamlGraphicsIconGeometries.EnabledGestureIcon);
                    Gesture_Icon.Badge.Geometry = Geometry.Parse("F0 M24,24z M0,0z " + XamlGraphicsIconGeometries.EnabledGestureIconBadgeCheck);
                    if (!ToolbarRegistry.GetUseRedStyle(Gesture_Icon))
                    {
                        Gesture_Icon.IconBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                        Gesture_Icon.Badge.Brush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    }
                }
            }
            else
            {
                if (Gesture_Icon != null)
                {
                    Gesture_Icon.Icon.Geometry = Geometry.Parse(XamlGraphicsIconGeometries.DisabledGestureIcon);
                    Gesture_Icon.Badge.Geometry = Geometry.Parse("F0 M24,24z M0,0z");
                    if (!ToolbarRegistry.GetUseRedStyle(Gesture_Icon))
                    {
                        Gesture_Icon.IconBrush = isDarkTheme
                            ? new SolidColorBrush(Color.FromRgb(244, 244, 245))
                            : new SolidColorBrush(Color.FromRgb(24, 24, 27));
                    }
                }
            }

            var boardGestureBtn = FindView("board.gesture") as BoardToolbarButton;
            if (boardGestureBtn != null)
            {
                if (boardAnyOn)
                {
                    boardGestureBtn.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    boardGestureBtn.IconGeometryDrawing.Brush = new SolidColorBrush(Colors.GhostWhite);
                    boardGestureBtn.IconGeometryDrawing2.Brush = new SolidColorBrush(Colors.GhostWhite);
                    boardGestureBtn.Foreground = new SolidColorBrush(Colors.GhostWhite);
                    boardGestureBtn.BorderBrush = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                    boardGestureBtn.IconGeometryDrawing.Geometry = Geometry.Parse(XamlGraphicsIconGeometries.EnabledGestureIcon);
                    boardGestureBtn.IconGeometryDrawing2.Geometry = Geometry.Parse("F0 M24,24z M0,0z " + XamlGraphicsIconGeometries.EnabledGestureIconBadgeCheck);
                }
                else
                {
                    boardGestureBtn.Background = new SolidColorBrush(boardBgColor);
                    boardGestureBtn.IconGeometryDrawing.Brush = new SolidColorBrush(boardIconColor);
                    boardGestureBtn.IconGeometryDrawing2.Brush = new SolidColorBrush(boardIconColor);
                    boardGestureBtn.Foreground = new SolidColorBrush(boardTextColor);
                    boardGestureBtn.BorderBrush = new SolidColorBrush(boardBorderColor);
                    boardGestureBtn.IconGeometryDrawing.Geometry = Geometry.Parse(XamlGraphicsIconGeometries.DisabledGestureIcon);
                    boardGestureBtn.IconGeometryDrawing2.Geometry = Geometry.Parse("F0 M24,24z M0,0z");
                }
            }
        }

        /// <summary>
        /// 控制是否顯示浮動工具欄的"手勢"按鈕
        /// </summary>
        private void CheckEnableTwoFingerGestureBtnVisibility(bool isVisible)
        {
            UpdateToolbarComponentVisibility();
        }

        #endregion "手勢"按鈕

    }
}
