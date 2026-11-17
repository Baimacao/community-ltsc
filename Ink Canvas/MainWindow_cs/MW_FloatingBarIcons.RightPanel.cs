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
        // LTSC（2026-11-17）：从 MW_FloatingBarIcons.cs 按 #region 拆出。
        // 只搬代码，不改逻辑。using 区整块复制（原文有 7 条别名）。
        #region Right Side Panel

        public static bool CloseIsFromButton;

        /// <summary>
        /// 退出按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        public void ExitApplication(object sender, RoutedEventArgs e)
        {
            _forceCloseFromExitOrRestartButton = true;
            App.IsAppExitByUser = true;
            Close();
        }

        /// <summary>
        /// 重启按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        public void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            if (Settings.Advanced.IsSecondConfirmWhenShutdownApp)
            {
                if (MessageBox.Show(Properties.MainWindowStrings.Main_CloseConfirm_Level1, "ICC CE Lite",
                        MessageBoxButton.OKCancel, MessageBoxImage.Warning) == MessageBoxResult.Cancel) return;
                if (MessageBox.Show(Properties.MainWindowStrings.Main_CloseConfirm_Level2, "ICC CE Lite",
                        MessageBoxButton.OKCancel, MessageBoxImage.Error) == MessageBoxResult.Cancel) return;
                if (MessageBox.Show(Properties.MainWindowStrings.Main_CloseConfirm_Level3, "ICC CE Lite",
                        MessageBoxButton.OKCancel, MessageBoxImage.Question) == MessageBoxResult.Cancel) return;
            }

            Process.Start(System.Windows.Forms.Application.ExecutablePath, "-m");
            _forceCloseFromExitOrRestartButton = true;
            App.IsAppExitByUser = true;
            CloseIsFromButton = true;
            Close();
        }

        /// <summary>
        /// 切换并打开设置面板；在需要时先进行安全密码校验，然后显示设置面板并启动打开动画，同时根据设置暂时调整无焦点模式与遮罩交互状态。
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal async void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            if (_settingsWindow != null)
            {
                if (_settingsWindow.WindowState == System.Windows.WindowState.Minimized)
                    _settingsWindow.WindowState = System.Windows.WindowState.Normal;
                _settingsWindow.Activate();
                _settingsWindow.Focus();
                return;
            }

            try
            {
                if (Ink_Canvas.Helpers.SecurityManager.IsPasswordRequiredForEnterSettings(Settings))
                {
                    bool ok = await Ink_Canvas.Helpers.SecurityManager.PromptAndVerifyAsync(Settings, this, Properties.MainWindowStrings.Main_EnterSettings, Properties.MainWindowStrings.Main_EnterSettings_Message);
                    if (!ok) return;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"安全密码校验失败: {ex}", LogHelper.LogType.Error);
                return;
            }

            HideSubPanels();
            // 打开设置前退出白板模式并切换到鼠标模式
            if (currentMode != 0) CloseWhiteboardImmediately();
            CursorIcon_Click(null, null);
            _settingsWindow = new Windows.SettingsViews.SettingsWindow();
            _settingsWindow.Owner = this;
            _settingsWindow.Topmost = this.Topmost;
            _settingsWindow.Closed += (s, args) => _settingsWindow = null;
            _settingsWindow.Show();
        }
        private bool forceEraser;


        private void BtnClear_Click(object sender, RoutedEventArgs e)
        {
            if (TryBlockFrozenPageMutation("清空冻结页面内容")) return;
            forceEraser = false;
            //BorderClearInDelete.Visibility = Visibility.Collapsed;

            if (currentMode == 0)
            {
                // 先回到画笔再清屏，避免 TimeMachine 的相关 bug 影响
                if (Pen_Icon.Background == null && IsAnnotating)
                    PenIcon_Click(null, null);
            }
            else
            {
                if (Pen_Icon.Background == null) PenIcon_Click(null, null);
            }

            if (inkCanvas.Strokes.Count != 0)
            {
                // 注入历史记录以便支持撤销
                var whiteboardIndex = CurrentWhiteboardIndex;
                if (currentMode == 0) whiteboardIndex = 0;
            }

            ClearStrokes(false);
            // 保存非笔画元素（如图片）
            var preservedElements = PreserveNonStrokeElements();
            inkCanvas.Children.Clear();
            // 恢复非笔画元素
            RestoreNonStrokeElements(preservedElements);

            if (Settings.Canvas.ClearCanvasAndClearTimeMachine) timeMachine.ClearStrokeHistory();

            CancelSingleFingerDragMode();

        }

        private bool lastIsInMultiTouchMode;

        private void CancelSingleFingerDragMode()
        {
            if (ToggleSwitchDrawShapeBorderAutoHide.IsOn) CollapseBorderDrawShape();

            GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

            if (isSingleFingerDragMode) ToggleFingerDragMode(null, null);
            isLongPressSelected = false;
        }

        /// <summary>
        /// 重置所有触摸相关状态，
        /// </summary>
        private void ResetTouchStates()
        {
            try
            {
                // 清空触摸点计数器
                dec.Clear();

                if (isPalmEraserActive)
                    isPalmEraserActive = false;

                // 确保触摸事件能正常响应
                inkCanvas.IsHitTestVisible = true;
                inkCanvas.IsManipulationEnabled = true;

                // 释放所有触摸捕获
                inkCanvas.ReleaseAllTouchCaptures();

                // 恢复UI元素的触摸响应
                ViewboxFloatingBar.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;


            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重置触摸状态失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }


        // 退出批注模式时的全屏还原处理
        private void RestoreFullScreenOnExitAnnotationMode()
        {
            if (Settings.Advanced.IsEnableAvoidFullScreenHelper &&
                isFullScreenApplied &&
                currentMode == 0 && // 不在白板模式
                !IsInPPTPresentationMode) // 不在PPT放映模式
            {
                // 恢复为非画板模式，重新启用全屏限制
                AvoidFullScreenHelper.SetBoardMode(false);

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    // 退出批注模式，恢复到工作区域大小
                    var workingArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                    MainWindow.MoveWindow(new WindowInteropHelper(this).Handle,
                        workingArea.Left, workingArea.Top,
                        workingArea.Width, workingArea.Height, true);
                }), DispatcherPriority.ApplicationIdle);

                isFullScreenApplied = false; // 标记全屏处理已还原
            }
        }

        /// <summary>
        /// 在屏幕模式、白板与黑板模式之间切换并同步相关的 UI 状态与资源处理。
        /// </summary>
        /// <remarks>
        /// 切换过程中会保存/清理/恢复画笔轨迹，显示或隐藏白板/黑板面板、手势面板与 PPT 控件，调整主题与悬浮工具栏可见性，处理全屏/工作区尺寸恢复或进入全屏，以及在进入白板时检查剪贴板并显示粘贴提示。该方法还会触发隐藏/显示墨迹画布的逻辑（通过调用 BtnHideInkCanvas_Click）。
        /// </remarks>
        private void SwitchBackground(object sender, RoutedEventArgs e)
        {
            if (GridTransparencyFakeBackground.Background == Brushes.Transparent)
            {
                if (currentMode == 0)
                {
                    currentMode++;
                    AutomationBootstrap.Monitor?.NotifyInternalStateChanged();
                    GridBackgroundCover.Visibility = Visibility.Collapsed;
                    AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                    AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                    AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);

                    // 在PPT模式下隐藏手势面板和手势按钮
                    AnimationsHelper.HideWithSlideAndFade(TwoFingerGestureBorder);
                    AnimationsHelper.HideWithSlideAndFade(BoardTwoFingerGestureBorder);
                    UpdateToolbarComponentVisibility();

                    SaveStrokes(true);
                    ClearStrokes(true);
                    RestoreStrokes(true);


                    if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Dark)
                    {
                        { /* Old UI removed */ }
                        { /* Old UI removed */ }
                    }
                    else
                    {
                        { /* Old UI removed */ }
                        if (isPresentationHaveBlackSpace)
                        {
                            { /* Old UI removed */ }
                            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                        }
                        else
                        {
                            { /* Old UI removed */ }
                            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                        }
                    }

                    { /* Old UI removed */ }

                    CheckClipboardImageAndShowPasteNotificationWhenEnteringBoard();
                }

                Topmost = true;
                BtnHideInkCanvas_Click(null, e);
            }
            else
            {
                switch (++currentMode % 2)
                {
                    case 0: //屏幕模式
                        VideoPresenter_OnExitWhiteboardMode();
                        currentMode = 0;
                        AutomationBootstrap.Monitor?.NotifyInternalStateChanged();
                        GridBackgroundCover.Visibility = Visibility.Collapsed;
                        AnimationsHelper.HideWithSlideAndFade(BlackboardLeftSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardCenterSide);
                        AnimationsHelper.HideWithSlideAndFade(BlackboardRightSide);

                        // 在PPT模式下隐藏手势面板和手势按钮
                        AnimationsHelper.HideWithSlideAndFade(TwoFingerGestureBorder);
                        AnimationsHelper.HideWithSlideAndFade(BoardTwoFingerGestureBorder);
                        UpdateToolbarComponentVisibility();

                        SaveStrokes();
                        ClearStrokes(true);
                        RestoreStrokes(true);

                        // 退出白板模式时取消全屏（仅在非PPT模式下）
                        if (Settings.Advanced.IsEnableAvoidFullScreenHelper &&
                            !IsInPPTPresentationMode) // 不在PPT放映模式
                        {
                            // 恢复为非画板模式，重新启用全屏限制
                            AvoidFullScreenHelper.SetBoardMode(false);

                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                // 退出白板模式，恢复到工作区域大小
                                var workingArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                                MainWindow.MoveWindow(new WindowInteropHelper(this).Handle,
                                    workingArea.Left, workingArea.Top,
                                    workingArea.Width, workingArea.Height, true);
                            }), DispatcherPriority.ApplicationIdle);

                            isFullScreenApplied = false; // 标记全屏处理已还原
                        }

                        // 在屏幕模式下恢复基础浮动栏的显示
                        ViewboxFloatingBar.Visibility = Visibility.Visible;

                        // 退出白板时自动收纳功能 - 等待浮动栏完全展开后再收纳
                        // 当处于PPT放映模式时，不自动收纳
                        if (Settings.Automation.IsAutoFoldWhenExitWhiteboard && !isFloatingBarFolded &&
                            !IsInPPTPresentationMode)
                        {
                            // 使用异步延迟，等待浮动栏展开动画完成后再收纳
                            Task.Run(async () =>
                            {
                                await Task.Delay(700);
                                await Dispatcher.InvokeAsync(() =>
                                {
                                    FoldFloatingBar_MouseUp(new object(), null);
                                });
                            });
                        }

                        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Dark)
                        {
                            { /* Old UI removed */ }
                            { /* Old UI removed */ }
                            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                        }
                        else
                        {
                            { /* Old UI removed */ }
                            if (isPresentationHaveBlackSpace)
                            {
                                { /* Old UI removed */ }
                                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                            }
                            else
                            {
                                { /* Old UI removed */ }
                                ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                            }
                        }

                        { /* Old UI removed */ }
                        Topmost = true;
                        break;
                    case 1: //黑板或白板模式
                        currentMode = 1;
                        AutomationBootstrap.Monitor?.NotifyInternalStateChanged();
                        GridBackgroundCover.Visibility = Visibility.Visible;
                        AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardLeftSide);
                        AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardCenterSide);
                        AnimationsHelper.ShowWithSlideFromBottomAndFade(BlackboardRightSide);

                        SaveStrokes(true);
                        ClearStrokes(true);

                        RestoreStrokes();

                        // 进入白板模式时全屏（仅在非PPT模式下）
                        if (Settings.Advanced.IsEnableAvoidFullScreenHelper &&
                            !IsInPPTPresentationMode) // 不在PPT放映模式
                        {
                            // 设置为画板模式，允许全屏操作
                            AvoidFullScreenHelper.SetBoardMode(true);
                            Dispatcher.BeginInvoke(new Action(() =>
                            {
                                MainWindow.MoveWindow(new WindowInteropHelper(this).Handle, 0, 0,
                                    System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width,
                                    System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height, true);
                            }), DispatcherPriority.ApplicationIdle);

                            isFullScreenApplied = true; // 标记已应用全屏处理
                        }

                        ViewboxFloatingBar.Visibility = Visibility.Collapsed;

                        { /* Old UI removed */ }
                        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Dark)
                        {
                            { /* Old UI removed */ }
                            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
                        }
                        else
                        {
                            { /* Old UI removed */ }
                            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                        }

                        if (Settings.Canvas.UsingWhiteboard)
                        {
                            // 如果有自定义背景色并且是白板模式，应用自定义背景色
                            if (CustomBackgroundColor.HasValue)
                            {
                                GridBackgroundCover.Background = new SolidColorBrush(CustomBackgroundColor.Value);
                            }
                            // 白板模式下设置墨迹颜色为黑色
                            CheckLastColor(0);
                            forceEraser = false;
                            ColorSwitchCheck();
                        }
                        else
                        {
                            // 黑板模式下设置墨迹颜色为白色
                            CheckLastColor(5);
                            forceEraser = false;
                            ColorSwitchCheck();
                        }

                        { /* Old UI removed */ }

                        if (Settings.Advanced.EnableUIAccessTopMost)
                        {
                            Topmost = true;
                        }
                        else
                        {
                            Topmost = false;
                        }

                        CheckClipboardImageAndShowPasteNotificationWhenEnteringBoard();
                        break;
                }
            }

            SyncPdfPageSidebarWithCanvas();
        }

        public int BoundsWidth = 5;
        private bool _isToolbarOnRightSide = true;

        private void BtnHideInkCanvas_Click(object sender, RoutedEventArgs e)
        {
            if (GridTransparencyFakeBackground.Background == Brushes.Transparent)
            {
                // 进入批注模式
                GridTransparencyFakeBackground.Opacity = 1;
                GridTransparencyFakeBackground.Background = new SolidColorBrush(StringToColor("#01FFFFFF"));
                SetTransparentNotHitThrough();
                inkCanvas.IsHitTestVisible = true;
                inkCanvas.Visibility = Visibility.Visible;

                GridBackgroundCoverHolder.Visibility = Visibility.Visible;

                GridInkCanvasSelectionCover.Visibility = Visibility.Collapsed;

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
                        MainWindow.MoveWindow(new WindowInteropHelper(this).Handle, 0, 0,
                            System.Windows.Forms.Screen.PrimaryScreen.Bounds.Width,
                            System.Windows.Forms.Screen.PrimaryScreen.Bounds.Height, true);
                    }), DispatcherPriority.ApplicationIdle);

                    isFullScreenApplied = true; // 标记已应用全屏处理
                }
            }
            else
            {
                // Auto-clear Strokes 要等待截图完成再清理笔记
                if (!IsInPPTPresentationMode)
                {
                    if (isLoaded && Settings.Automation.IsAutoClearWhenExitingWritingMode)
                        if (inkCanvas.Strokes.Count > 0)
                        {
                            if (Settings.Automation.IsAutoSaveScreenshotAtClear && inkCanvas.Strokes.Count >
                                Settings.Automation.MinimumAutomationStrokeNumber)
                                CaptureAndEnqueueScreenshotSave(true);

                            //BtnClear_Click(null, null);
                        }

                    inkCanvas.IsHitTestVisible = true;
                    inkCanvas.Visibility = Visibility.Visible;
                }
                else
                {
                    if (isLoaded && Settings.Automation.IsAutoClearWhenExitingWritingMode &&
                        !Settings.PowerPointSettings.IsNoClearStrokeOnSelectWhenInPowerPoint)
                        if (inkCanvas.Strokes.Count > 0)
                        {
                            if (Settings.Automation.IsAutoSaveScreenshotAtClear && inkCanvas.Strokes.Count >
                                Settings.Automation.MinimumAutomationStrokeNumber)
                                CaptureAndEnqueueScreenshotSave(true);

                            //BtnClear_Click(null, null);
                        }


                    if (Settings.PowerPointSettings.IsShowStrokeOnSelectInPowerPoint)
                    {
                        inkCanvas.Visibility = Visibility.Visible;
                        inkCanvas.IsHitTestVisible = true;
                    }
                    else
                    {
                        inkCanvas.IsHitTestVisible = true;
                        inkCanvas.Visibility = Visibility.Visible;
                    }
                }

                GridTransparencyFakeBackground.Opacity = 0;
                GridTransparencyFakeBackground.Background = Brushes.Transparent;
                SetTransparentHitThrough();

                GridBackgroundCoverHolder.Visibility = Visibility.Collapsed;

                // 退出批注模式时的全屏还原
                RestoreFullScreenOnExitAnnotationMode();

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
            }

            if (GridTransparencyFakeBackground.Background == Brushes.Transparent)
            {
                UpdateToolbarComponentVisibility();
                HideSubPanels("cursor");

                if (currentMode == 0)
                {
                    ViewboxFloatingBar.Visibility = Visibility.Visible;
                }
            }
            else
            {
                UpdateToolbarComponentVisibility();

                if (currentMode == 0)
                {
                    ViewboxFloatingBar.Visibility = Visibility.Visible;
                }
            }
        }

        private void BtnSwitchSide_Click(object sender, RoutedEventArgs e)
        {
            if (_isToolbarOnRightSide)
            {
                { /* Old UI removed */ }
                { /* Old UI removed */ }
            }
            else
            {
                { /* Old UI removed */ }
                { /* Old UI removed */ }
            }
        }

        private void StackPanel_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (((StackPanel)sender).Visibility == Visibility.Visible)
            { /* Old UI removed */ }
            else
            { /* Old UI removed */ }
        }

        #endregion
    }
}
