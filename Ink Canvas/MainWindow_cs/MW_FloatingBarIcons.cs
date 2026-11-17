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
using Ink_Canvas.Windows;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        /// <summary>
        /// 当前工具模式
        /// </summary>
        private string _currentToolMode = "cursor";

        private static Windows.SettingsViews.SettingsWindow _settingsWindow = null;

        /// <summary>
        /// 光标图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private async void SymbolIconCursor_Click(object sender, RoutedEventArgs e)
        {
            if (currentMode != 0)
            {
                ImageBlackboard_MouseUp(null, null);
            }
            else
            {
                BtnHideInkCanvas_Click(null, null);

                if (IsInPPTPresentationMode)
                {
                    await Task.Delay(100);
                    ViewboxFloatingBarMarginAnimation(60);
                }
            }
        }

        /// <summary>
        /// 面积擦子面板的清空墨迹按钮事件处理
        /// </summary>
        /// <param name="sender">sender</param>
        /// <param name="e">RoutedEventArgs</param>
        private void EraserPanelSymbolIconDelete_MouseUp(object sender, RoutedEventArgs e)
        {
            PenIcon_Click(null, null);
            SymbolIconDelete_MouseUp(null, null);
        }

        #region 主要的工具按鈕事件

        /// <summary>
        /// 浮动工具栏的"套索选"按钮事件，重定向到旧UI的BtnSelect_Click方法
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        internal void SymbolIconSelect_MouseUp(object sender, MouseButtonEventArgs e)
        {

            if (lastBorderMouseDownObject is Panel panel)
                panel.Background = new SolidColorBrush(Colors.Transparent);

            // 如果当前不在批注模式，先切换到批注模式
            if (!IsAnnotating)
            {
                PenIcon_Click(sender, e);
            }

            BtnSelect_Click(null, null);

            // 更新模式缓存
            UpdateCurrentToolMode("select");

            HideSubPanels("select");

        }

        #endregion

        /// <summary>
        /// 浮动工具栏按钮鼠标按下反馈效果处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void FloatingBarToolBtnMouseDownFeedback_Panel(object sender, MouseButtonEventArgs e)
        {
            if (sender is Panel panel)
            {
                lastBorderMouseDownObject = sender;
                panel.Background = new SolidColorBrush(Color.FromArgb(28, 24, 24, 27));
            }
            else if (sender is Border border)
            {
                lastBorderMouseDownObject = sender;
                if (border.Name?.StartsWith("QuickColor") == true)
                {
                    if (border.Background is SolidColorBrush originalColor)
                    {
                        border.Background = new SolidColorBrush(Color.FromArgb(180, originalColor.Color.R, originalColor.Color.G, originalColor.Color.B));
                    }
                }
                else
                {
                    border.Background = new SolidColorBrush(Color.FromArgb(28, 24, 24, 27));
                }
            }
            else if (sender is Ink_Canvas.Controls.ColorPickerButton colorPicker)
            {
                lastBorderMouseDownObject = sender;
            }
        }

        /// <summary>
        /// 浮动工具栏按钮鼠标离开反馈效果处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标事件参数</param>
        private void FloatingBarToolBtnMouseLeaveFeedback_Panel(object sender, MouseEventArgs e)
        {
            if (sender is Panel panel)
            {
                lastBorderMouseDownObject = null;
                panel.Background = new SolidColorBrush(Colors.Transparent);
            }
            else if (sender is Border border)
            {
                lastBorderMouseDownObject = null;
                // 对于快捷调色板的颜色球，恢复原始颜色
                if (border.Name?.StartsWith("QuickColor") == true)
                {
                    // 根据颜色球名称恢复对应的颜色
                    switch (border.Name)
                    {
                        case "QuickColorWhite":
                        case "QuickColorWhiteSingle":
                            border.Background = new SolidColorBrush(Colors.White);
                            break;
                        case "QuickColorOrange":
                        case "QuickColorOrangeSingle":
                            border.Background = new SolidColorBrush(Color.FromRgb(251, 150, 80));
                            break;
                        case "QuickColorYellow":
                        case "QuickColorYellowSingle":
                            border.Background = new SolidColorBrush(Colors.Yellow);
                            break;
                        case "QuickColorBlack":
                        case "QuickColorBlackSingle":
                            border.Background = new SolidColorBrush(Colors.Black);
                            break;
                        case "QuickColorBlue":
                            border.Background = new SolidColorBrush(Color.FromRgb(37, 99, 235));
                            break;
                        case "QuickColorRed":
                        case "QuickColorRedSingle":
                            border.Background = new SolidColorBrush(Colors.Red);
                            break;
                        case "QuickColorGreen":
                        case "QuickColorGreenSingle":
                            border.Background = new SolidColorBrush(Color.FromRgb(22, 163, 74));
                            break;
                        case "QuickColorPurple":
                            border.Background = new SolidColorBrush(Color.FromRgb(147, 51, 234));
                            break;
                    }
                }
                else
                {
                    border.Background = new SolidColorBrush(Colors.Transparent);
                }
            }
            else if (sender is Ink_Canvas.Controls.ColorPickerButton colorPicker)
            {
                lastBorderMouseDownObject = null;
            }
        }

        /// <summary>
        /// 设置图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void SymbolIconSettings_Click(object sender, MouseButtonEventArgs e)
        {
            HideSubPanels();
            BtnSettings_Click(null, null);
        }
        /// <summary>
        /// 截图图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal async void SymbolIconScreenshot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            HideSubPanelsImmediately();
            await Task.Delay(50);

            // 白板模式下默认全屏截图到桌面；其余模式默认调用可选区截图
            if (currentMode == 1)
            {
                SaveScreenShotToDesktop();
            }
            else
            {
                await SaveAreaScreenShotToDesktop();
            }
        }

        /// <summary>
        /// 倒计时计时器图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        internal void ImageCountdownTimer_MouseUp(object sender, MouseButtonEventArgs e)
        {
            LeftUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
            RightUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
            SidePannelMarginAnimation(-10);
            AnimationsHelper.HidePopupWithSlideAndFade(BorderTools);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderToolsPopup);
            AnimationsHelper.HideWithSlideAndFade(BoardImageOptionsPanel);

            var timerWindow = new Windows.NewStyleTimerWindow
            {
                Owner = this
            };

            timerWindow.Show();
        }

        /// <summary>
        /// 操作指南窗口图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void OperatingGuideWindowIcon_MouseUp(object sender, MouseButtonEventArgs e)
        {
            AnimationsHelper.HidePopupWithSlideAndFade(BorderTools);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderToolsPopup);
            AnimationsHelper.HideWithSlideAndFade(BoardImageOptionsPanel);

            new OperatingGuideWindow().Show();
        }


        /// <summary>
        /// 检查并更新橡皮擦类型标签的状态
        /// </summary>
        public void CheckEraserTypeTab()
        {
            if (EraserTypeTab != null)
                EraserTypeTab.SelectedIndex = Settings.Canvas.EraserShapeType;
            if (BoardEraserTypeTab != null)
                BoardEraserTypeTab.SelectedIndex = Settings.Canvas.EraserShapeType;
        }


        /// <summary>
        /// 墨迹重播按钮点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">路由事件参数</param>
        private void GridInkReplayButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("重播冻结页面内容")) return;
            //if (lastBorderMouseDownObject != sender) return;

            AnimationsHelper.HidePopupWithSlideAndFade(BorderTools);
            AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderToolsPopup);
            AnimationsHelper.HideWithSlideAndFade(BoardImageOptionsPanel);

            CollapseBorderDrawShape();

            InkCanvasForInkReplay.Visibility = Visibility.Visible;
            InkCanvasGridForInkReplay.Visibility = Visibility.Hidden;
            InkCanvasGridForInkReplay.IsHitTestVisible = false;
            FloatingbarUIForInkReplay.Visibility = Visibility.Hidden;
            FloatingbarUIForInkReplay.IsHitTestVisible = false;
            BlackboardUIGridForInkReplay.Visibility = Visibility.Hidden;
            BlackboardUIGridForInkReplay.IsHitTestVisible = false;

            AnimationsHelper.ShowWithFadeIn(BorderInkReplayToolBox);
            InkReplayPanelStatusText.Text = Properties.MainWindowStrings.Main_InkReplayPlaying;
            InkReplayPlayPauseBorder.Background = new SolidColorBrush(Colors.Transparent);
            InkReplayPlayButtonImage.Visibility = Visibility.Collapsed;
            InkReplayPauseButtonImage.Visibility = Visibility.Visible;

            isStopInkReplay = false;
            isPauseInkReplay = false;
            isRestartInkReplay = false;
            inkReplaySpeed = 1;
            InkCanvasForInkReplay.Strokes.Clear();
            var strokes = inkCanvas.Strokes.Clone();
            if (inkCanvas.GetSelectedStrokes().Count != 0) strokes = inkCanvas.GetSelectedStrokes().Clone();
            int k = 1, i = 0;
            new Thread(() =>
            {
                isRestartInkReplay = true;
                while (isRestartInkReplay)
                {
                    isRestartInkReplay = false;
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        InkCanvasForInkReplay.Strokes.Clear();
                    });
                    foreach (var stroke in strokes)
                    {

                        if (isRestartInkReplay) break;

                        var stylusPoints = new StylusPointCollection();
                        if (stroke.StylusPoints.Count == 629) //圆或椭圆
                        {
                            Stroke s = null;
                            foreach (var stylusPoint in stroke.StylusPoints)
                            {

                                if (isRestartInkReplay) break;

                                while (isPauseInkReplay)
                                {
                                    Thread.Sleep(10);
                                }

                                if (i++ >= 50)
                                {
                                    i = 0;
                                    Thread.Sleep((int)(10 / inkReplaySpeed));
                                    if (isStopInkReplay) return;
                                }

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    try
                                    {
                                        InkCanvasForInkReplay.Strokes.Remove(s);
                                    }
                                    catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }

                                    stylusPoints.Add(stylusPoint);
                                    s = new Stroke(stylusPoints.Clone())
                                    {
                                        DrawingAttributes = stroke.DrawingAttributes
                                    };
                                    InkCanvasForInkReplay.Strokes.Add(s);
                                });
                            }
                        }
                        else
                        {
                            Stroke s = null;
                            foreach (var stylusPoint in stroke.StylusPoints)
                            {

                                if (isRestartInkReplay) break;

                                while (isPauseInkReplay)
                                {
                                    Thread.Sleep(10);
                                }

                                if (i++ >= k)
                                {
                                    i = 0;
                                    Thread.Sleep((int)(10 / inkReplaySpeed));
                                    if (isStopInkReplay) return;
                                }

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    try
                                    {
                                        InkCanvasForInkReplay.Strokes.Remove(s);
                                    }
                                    catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }

                                    stylusPoints.Add(stylusPoint);
                                    s = new Stroke(stylusPoints.Clone())
                                    {
                                        DrawingAttributes = stroke.DrawingAttributes
                                    };
                                    InkCanvasForInkReplay.Strokes.Add(s);
                                });
                            }
                        }
                    }
                }

                Thread.Sleep(100);
                Application.Current.Dispatcher.Invoke(() =>
                {
                    InkCanvasForInkReplay.Visibility = Visibility.Collapsed;
                    InkCanvasGridForInkReplay.Visibility = Visibility.Visible;
                    InkCanvasGridForInkReplay.IsHitTestVisible = true;
                    AnimationsHelper.HideWithFadeOut(BorderInkReplayToolBox);
                    FloatingbarUIForInkReplay.Visibility = Visibility.Visible;
                    FloatingbarUIForInkReplay.IsHitTestVisible = true;
                    BlackboardUIGridForInkReplay.Visibility = Visibility.Visible;
                    BlackboardUIGridForInkReplay.IsHitTestVisible = true;
                    inkCanvas.IsHitTestVisible = true;
                    inkCanvas.IsManipulationEnabled = true;

                    if (inkCanvas.EditingMode == InkCanvasEditingMode.None)
                    {
                        inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                    }

                    ResetTouchStates();
                });
            }).Start();
        }

        /// <summary>
        /// 是否停止墨迹重播
        /// </summary>
        private bool isStopInkReplay;
        /// <summary>
        /// 是否暂停墨迹重播
        /// </summary>
        private bool isPauseInkReplay;
        /// <summary>
        /// 是否重新开始墨迹重播
        /// </summary>
        private bool isRestartInkReplay;
        /// <summary>
        /// 墨迹重播速度
        /// </summary>
        private double inkReplaySpeed = 1;

        /// <summary>
        /// 墨迹重播画布鼠标按下事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkCanvasForInkReplay_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                InkCanvasForInkReplay.Visibility = Visibility.Collapsed;
                InkCanvasGridForInkReplay.Visibility = Visibility.Visible;
                InkCanvasGridForInkReplay.IsHitTestVisible = true;
                FloatingbarUIForInkReplay.Visibility = Visibility.Visible;
                FloatingbarUIForInkReplay.IsHitTestVisible = true;
                BlackboardUIGridForInkReplay.Visibility = Visibility.Visible;
                BlackboardUIGridForInkReplay.IsHitTestVisible = true;
                AnimationsHelper.HideWithFadeOut(BorderInkReplayToolBox);
                isStopInkReplay = true;
                inkCanvas.IsHitTestVisible = true;
                inkCanvas.IsManipulationEnabled = true;

                if (inkCanvas.EditingMode == InkCanvasEditingMode.None)
                {
                    inkCanvas.EditingMode = InkCanvasEditingMode.Ink;
                }

                ResetTouchStates();
            }
        }

        /// <summary>
        /// 墨迹重播播放/暂停按钮鼠标按下事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplayPlayPauseBorder_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            InkReplayPlayPauseBorder.Background = new SolidColorBrush(Color.FromArgb(34, 9, 9, 11));
        }

        /// <summary>
        /// 墨迹重播播放/暂停按钮鼠标释放事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplayPlayPauseBorder_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            InkReplayPlayPauseBorder.Background = new SolidColorBrush(Colors.Transparent);
            isPauseInkReplay = !isPauseInkReplay;
            InkReplayPanelStatusText.Text = isPauseInkReplay ? Properties.MainWindowStrings.Main_InkReplay_Paused : Properties.MainWindowStrings.Main_InkReplayPlaying;
            InkReplayPlayButtonImage.Visibility = isPauseInkReplay ? Visibility.Visible : Visibility.Collapsed;
            InkReplayPauseButtonImage.Visibility = !isPauseInkReplay ? Visibility.Visible : Visibility.Collapsed;
        }

        /// <summary>
        /// 墨迹重播停止按钮鼠标按下事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplayStopButtonBorder_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            InkReplayStopButtonBorder.Background = new SolidColorBrush(Color.FromArgb(34, 9, 9, 11));
        }

        /// <summary>
        /// 墨迹重播停止按钮鼠标释放事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplayStopButtonBorder_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            InkReplayStopButtonBorder.Background = new SolidColorBrush(Colors.Transparent);
            InkCanvasForInkReplay.Visibility = Visibility.Collapsed;
            InkCanvasGridForInkReplay.Visibility = Visibility.Visible;
            InkCanvasGridForInkReplay.IsHitTestVisible = true;
            FloatingbarUIForInkReplay.Visibility = Visibility.Visible;
            FloatingbarUIForInkReplay.IsHitTestVisible = true;
            BlackboardUIGridForInkReplay.Visibility = Visibility.Visible;
            BlackboardUIGridForInkReplay.IsHitTestVisible = true;
            AnimationsHelper.HideWithFadeOut(BorderInkReplayToolBox);
            isStopInkReplay = true;
        }

        /// <summary>
        /// 墨迹重播重新开始按钮鼠标按下事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplayReplayButtonBorder_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            InkReplayReplayButtonBorder.Background = new SolidColorBrush(Color.FromArgb(34, 9, 9, 11));
        }

        /// <summary>
        /// 墨迹重播重新开始按钮鼠标释放事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplayReplayButtonBorder_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            InkReplayReplayButtonBorder.Background = new SolidColorBrush(Colors.Transparent);
            isRestartInkReplay = true;
            isPauseInkReplay = false;
            InkReplayPanelStatusText.Text = Properties.MainWindowStrings.Main_InkReplayPlaying;
            InkReplayPlayButtonImage.Visibility = Visibility.Collapsed;
            InkReplayPauseButtonImage.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 墨迹重播速度按钮鼠标按下事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplaySpeedButtonBorder_OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            InkReplaySpeedButtonBorder.Background = new SolidColorBrush(Color.FromArgb(34, 9, 9, 11));
        }

        /// <summary>
        /// 墨迹重播速度按钮鼠标释放事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        private void InkReplaySpeedButtonBorder_OnMouseUp(object sender, MouseButtonEventArgs e)
        {
            InkReplaySpeedButtonBorder.Background = new SolidColorBrush(Colors.Transparent);
            inkReplaySpeed = inkReplaySpeed == 0.5 ? 1 :
                inkReplaySpeed == 1 ? 2 :
                inkReplaySpeed == 2 ? 4 :
                inkReplaySpeed == 4 ? 8 : 0.5;
            InkReplaySpeedTextBlock.Text = inkReplaySpeed + "x";
        }

        /// <summary>
        /// 工具图标点击事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        internal void SymbolIconTools_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (BorderTools.IsOpen || BoardBorderToolsPopup.IsOpen)
            {
                AnimationsHelper.HidePopupWithSlideAndFade(BorderTools);
                AnimationsHelper.HidePopupWithSlideAndFade(BoardBorderToolsPopup);
            }
            else
            {
                HideSubPanels();
                if (currentMode == 0)
                {
                    MainToolsPopupContent?.ApplyMenuLayout();
                    AnimationsHelper.ShowPopupWithSlideAndFade(BorderTools);
                    _popupManager?.BringToFront(BorderTools);
                }
                else
                {
                    BoardToolsPopupContent?.ApplyMenuLayout();
                    AnimationsHelper.ShowPopupWithSlideAndFade(BoardBorderToolsPopup);
                    _popupManager?.BringToFront(BoardBorderToolsPopup);
                }
            }
        }

        /// <summary>
        /// 浮动工具栏边距动画是否正在运行
        /// </summary>
        private bool isViewboxFloatingBarMarginAnimationRunning;
        private bool isFloatingBarHeadOnRight;
        private bool isFloatingBarHeadOnBottom;
        private double _cachedFloatingBarHeight;
        private double _cachedFloatingBarHeadHeight;
        private double _cachedScreenHeight;

        private bool IsVerticalToolbar =>
            Settings.Appearance.ToolbarPosition == ToolbarPosition.Top ||
            Settings.Appearance.ToolbarPosition == ToolbarPosition.Bottom;

        private double GetFloatingBarScaleX()
        {
            var scale = ViewboxFloatingBarScaleTransform?.ScaleX ?? 1;
            return scale > 0 && !double.IsNaN(scale) && !double.IsInfinity(scale) ? scale : 1;
        }

        private double GetElementWidthForFloatingBar(FrameworkElement element, double fallbackWidth)
        {
            if (element == null) return fallbackWidth;

            var width = element.ActualWidth;
            if (width <= 0 || double.IsNaN(width)) width = element.DesiredSize.Width;
            if (width <= 0 || double.IsNaN(width)) width = element.RenderSize.Width;
            if (width <= 0 || double.IsNaN(width)) width = element.Width;

            return width > 0 && !double.IsNaN(width) && !double.IsInfinity(width) ? width : fallbackWidth;
        }

        private double GetElementHeightForFloatingBar(FrameworkElement element, double fallbackHeight)
        {
            if (element == null) return fallbackHeight;

            var height = element.ActualHeight;
            if (height <= 0 || double.IsNaN(height)) height = element.DesiredSize.Height;
            if (height <= 0 || double.IsNaN(height)) height = element.RenderSize.Height;
            if (height <= 0 || double.IsNaN(height)) height = element.Height;

            return height > 0 && !double.IsNaN(height) && !double.IsInfinity(height) ? height : fallbackHeight;
        }

        private double GetFloatingBarScaledWidth()
        {
            var baseWidth = GetElementWidthForFloatingBar(ViewboxFloatingBar, 200);
            return baseWidth * GetFloatingBarScaleX();
        }

        private double GetFloatingBarHeadScaledWidth()
        {
            var dragElement = FindDragHandleInRoot();
            return GetElementWidthForFloatingBar(dragElement, 50) * GetFloatingBarScaleX();
        }

        private double GetFloatingBarScaledHeight()
        {
            var baseHeight = GetElementHeightForFloatingBar(ViewboxFloatingBar, 58);
            return baseHeight * GetFloatingBarScaleX();
        }

        private double GetFloatingBarHeadScaledHeight()
        {
            var dragElement = FindDragHandleInRoot();
            return GetElementHeightForFloatingBar(dragElement, 50) * GetFloatingBarScaleX();
        }

        private double GetFloatingBarScreenHeight(bool useWorkingArea)
        {
            double dpiScaleY = 1;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            var screen = GetFloatingBarTargetScreen();
            return (useWorkingArea ? screen.WorkingArea.Height : screen.Bounds.Height) / dpiScaleY;
        }

        private double GetSelectionBGLeft()
        {
            var (_, _, contentPanel) = GetFirstContentBorderElements();
            if (contentPanel == null) return 0;
            foreach (var border in FloatingBarRootPanel.Children.OfType<Border>())
            {
                if (border.Tag as string == ToolbarRegistry.ContentBorderTag && border.Child is Grid grid)
                {
                    foreach (var gridChild in grid.Children.OfType<System.Windows.Controls.Canvas>())
                    {
                        if (gridChild.Tag as string == ToolbarRegistry.SelectionCanvasTag)
                        {
                            foreach (var canvasChild in gridChild.Children.OfType<Border>())
                            {
                                if (canvasChild.Tag as string == ToolbarRegistry.SelectionBGTag)
                                {
                                    var left = System.Windows.Controls.Canvas.GetLeft(canvasChild);
                                    return double.IsNaN(left) ? 0 : left;
                                }
                            }
                        }
                    }
                }
            }
            return 0;
        }

        private StackPanel GetFirstContentPanel()
        {
            if (FloatingBarRootPanel == null) return null;
            foreach (var border in FloatingBarRootPanel.Children.OfType<Border>())
            {
                if (border.Tag as string == ToolbarRegistry.ContentBorderTag && border.Child is Grid grid)
                {
                    foreach (var gridChild in grid.Children.OfType<StackPanel>())
                    {
                        if (gridChild.Tag as string == ToolbarRegistry.ContentPanelTag)
                            return gridChild;
                    }
                }
            }
            return null;
        }

        private FrameworkElement FindDragHandleInRoot()
        {
            if (FloatingBarRootPanel == null) return null;
            if (BorderFloatingBarMoveControls != null &&
                FloatingBarRootPanel.Children.Contains(BorderFloatingBarMoveControls))
                return BorderFloatingBarMoveControls;
            foreach (var child in FloatingBarRootPanel.Children.OfType<FrameworkElement>())
            {
                if (IsDragHandleElement(child))
                    return child;
            }
            return null;
        }

        private double GetFloatingBarScreenWidth(bool useWorkingArea)
        {
            double dpiScaleX = 1;
            var source = PresentationSource.FromVisual(this);
            if (source?.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
            }

            var screen = GetFloatingBarTargetScreen();
            return (useWorkingArea ? screen.WorkingArea.Width : screen.Bounds.Width) / dpiScaleX;
        }

        private void SetFloatingBarHeadPlacement(bool headOnRight)
        {
            if (FloatingBarRootPanel == null) return;

            if (IsVerticalToolbar)
            {
                SetFloatingBarHeadPlacementVertical(headOnRight);
                return;
            }

            if (isFloatingBarHeadOnRight == headOnRight) return;

            var rootChildren = FloatingBarRootPanel.Children;
            var rootList = rootChildren.OfType<FrameworkElement>().ToList();

            var dragElement = FindDragHandleInRoot();
            var otherElements = rootList.Where(c => c != dragElement).ToList();

            rootChildren.Clear();

            var flipContentOnAutoFlip = Settings.Appearance.FlipContentOnAutoFlip;

            if (headOnRight)
            {
                if (flipContentOnAutoFlip)
                {
                    foreach (var elem in otherElements)
                    {
                        rootChildren.Add(elem);
                    }
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(3, 0, 0, 0);
                        rootChildren.Add(dragElement);
                    }
                }
                else
                {
                    foreach (var elem in otherElements.AsEnumerable().Reverse())
                    {
                        rootChildren.Add(elem);
                    }
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(3, 0, 0, 0);
                        rootChildren.Add(dragElement);
                    }

                    ReverseAllContentPanels();
                }
            }
            else
            {
                if (flipContentOnAutoFlip)
                {
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0);
                        rootChildren.Add(dragElement);
                    }
                    foreach (var elem in otherElements)
                    {
                        rootChildren.Add(elem);
                    }
                }
                else
                {
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0);
                        rootChildren.Add(dragElement);
                    }
                    foreach (var elem in otherElements.AsEnumerable().Reverse())
                    {
                        rootChildren.Add(elem);
                    }

                    RestoreAllContentPanels();
                }
            }

            isFloatingBarHeadOnRight = headOnRight;

            SetFloatingBarHighlightPosition(_currentToolMode);
        }

        private void SetFloatingBarHeadPlacementVertical(bool headOnBottom)
        {
            if (FloatingBarRootPanel == null) return;
            if (isFloatingBarHeadOnBottom == headOnBottom) return;

            var rootChildren = FloatingBarRootPanel.Children;
            var rootList = rootChildren.OfType<FrameworkElement>().ToList();

            var dragElement = FindDragHandleInRoot();
            var otherElements = rootList.Where(c => c != dragElement).ToList();

            rootChildren.Clear();

            var flipContentOnAutoFlip = Settings.Appearance.FlipContentOnAutoFlip;

            if (headOnBottom)
            {
                if (flipContentOnAutoFlip)
                {
                    foreach (var elem in otherElements)
                    {
                        rootChildren.Add(elem);
                    }
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0, 3, 0, 0);
                        rootChildren.Add(dragElement);
                    }
                }
                else
                {
                    foreach (var elem in otherElements.AsEnumerable().Reverse())
                    {
                        rootChildren.Add(elem);
                    }
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0, 3, 0, 0);
                        rootChildren.Add(dragElement);
                    }

                    ReverseAllContentPanels();
                }
            }
            else
            {
                if (flipContentOnAutoFlip)
                {
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0);
                        rootChildren.Add(dragElement);
                    }
                    foreach (var elem in otherElements)
                    {
                        rootChildren.Add(elem);
                    }
                }
                else
                {
                    if (dragElement != null)
                    {
                        dragElement.Margin = new Thickness(0);
                        rootChildren.Add(dragElement);
                    }
                    foreach (var elem in otherElements.AsEnumerable().Reverse())
                    {
                        rootChildren.Add(elem);
                    }

                    RestoreAllContentPanels();
                }
            }

            isFloatingBarHeadOnBottom = headOnBottom;

            SetFloatingBarHighlightPosition(_currentToolMode);
        }

        internal void UpdateToolbarPosition()
        {
            _userHasDraggedFloatingBar = false;
            pointDesktop = new Point(-1, -1);
            pointPPT = new Point(-1, -1);

            RebuildToolbar();

            // 更新工具栏位置
            if (IsInPPTPresentationMode)
                ViewboxFloatingBarMarginAnimation(60);
            else
                PureViewboxFloatingBarMarginAnimationInDesktopMode();
        }

        private bool IsDragHandleElement(FrameworkElement element)
        {
            if (element is Border border)
            {
                if (border.Name == "BorderFloatingBarMoveControls") return true;
                var child = border.Child;
                if (child is Image) return true;
                if (child is StackPanel panel && panel.Children.Count > 0 && panel.Children[0] is Image)
                    return true;
            }
            return false;
        }

        private IEnumerable<StackPanel> GetAllPanelsWithContent(DependencyObject root)
        {
            if (root == null) yield break;

            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var child = VisualTreeHelper.GetChild(root, i);

                if (child is StackPanel panel && (panel.Orientation == System.Windows.Controls.Orientation.Horizontal ||
                                                  panel.Orientation == System.Windows.Controls.Orientation.Vertical))
                {
                    yield return panel;
                }

                foreach (var nestedPanel in GetAllPanelsWithContent(child))
                {
                    yield return nestedPanel;
                }
            }
        }

        private Dictionary<StackPanel, List<FrameworkElement>> _normalContentOrders;

        private void ReverseAllContentPanels()
        {
            _normalContentOrders = new Dictionary<StackPanel, List<FrameworkElement>>();
            foreach (var panel in GetAllPanelsWithContent(FloatingBarRootPanel))
            {
                _normalContentOrders[panel] = panel.Children.OfType<FrameworkElement>().ToList();
                var reversed = panel.Children.OfType<FrameworkElement>().Reverse().ToList();
                panel.Children.Clear();
                foreach (var child in reversed)
                    panel.Children.Add(child);
            }
        }

        private void RestoreAllContentPanels()
        {
            if (_normalContentOrders == null) return;
            foreach (var kvp in _normalContentOrders)
            {
                var panel = kvp.Key;
                var normalOrder = kvp.Value;
                var current = panel.Children.OfType<FrameworkElement>().ToList();
                if (current.SequenceEqual(normalOrder)) continue;
                panel.Children.Clear();
                foreach (var child in normalOrder)
                {
                    if (child.Parent != null && child.Parent != panel)
                        continue;
                    if (!panel.Children.Contains(child))
                        panel.Children.Add(child);
                }
            }
            _normalContentOrders = null;
        }

        private bool IsFloatingBarContentVisible()
        {
            return !ToolbarRegistry.IsContentCollapsedByUser;
        }

        private void SetFloatingBarContentVisibility(bool visible)
        {
            ToolbarRegistry.IsContentCollapsedByUser = !visible;
            ToolbarRegistry.UpdateVisibilityByMode(
                FloatingBarRootPanel,
                IsAnnotating,
                IsInPPTPresentationMode);

            UpdateFloatingBarModeBadge(!visible);
        }

        /// <summary>
        /// 折叠态下在主图标右下角显示当前工具模式的角标。
        ///
        /// 为什么要这个：内容收起后只剩一个主图标，用户看不出自己现在在笔/橡皮/光标。
        /// 展开时不需要——每个按钮自己会亮，再叠个角标是重复。
        /// </summary>
        private void UpdateFloatingBarModeBadge(bool show)
        {
            try
            {
                if (BorderFloatingBarModeBadge == null || PathFloatingBarModeBadge == null) return;

                if (!show)
                {
                    BorderFloatingBarModeBadge.Visibility = Visibility.Collapsed;
                    return;
                }

                // 注意：GetCorrectIcon 只认这五个名字——
                //   cursor / pen / eraserStroke / eraserCircle / lassoSelect
                // 传别的名字它会 return ""，几何串空，Path.Data 设不上，
                // 角标就变成「Visibility=Visible 但什么也看不见」。
                // 这正是一开始面积擦/线擦/圈套看不到角标的原因。
                string iconType = null;
                switch (_currentToolMode)
                {
                    case "pen":
                    case "color":
                        iconType = "pen";
                        break;
                    case "eraser":
                        // 面积擦 = 圆擦
                        iconType = "eraserCircle";
                        break;
                    case "eraserByStrokes":
                        // 线擦 = 笔画擦（沿笔画擦）
                        iconType = "eraserStroke";
                        break;
                    case "select":
                        // 圈套选 = 套索
                        iconType = "lassoSelect";
                        break;
                    case "cursor":
                    case "shape":
                        // 形状没有独立几何，用 cursor 顶上，
                        // 至少能看出「当前不是笔/橡皮/选择」。
                        iconType = "cursor";
                        break;
                }

                if (string.IsNullOrEmpty(iconType))
                {
                    BorderFloatingBarModeBadge.Visibility = Visibility.Collapsed;
                    return;
                }

                var geometry = GetCorrectIcon(iconType, false);
                if (string.IsNullOrEmpty(geometry))
                {
                    BorderFloatingBarModeBadge.Visibility = Visibility.Collapsed;
                    return;
                }

                PathFloatingBarModeBadge.Data = Geometry.Parse(geometry);
                BorderFloatingBarModeBadge.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新模式角标失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private double ClampFloatingBarLeft(double left, double floatingBarWidth, double screenWidth)
        {
            var maxLeft = Math.Max(0, screenWidth - floatingBarWidth);
            return Math.Max(0, Math.Min(left, maxLeft));
        }

        private double ClampFloatingBarTop(double top, double floatingBarHeight, double screenHeight)
        {
            var maxTop = Math.Max(0, screenHeight - floatingBarHeight);
            return Math.Max(0, Math.Min(top, maxTop));
        }

        private void PlaceFloatingBarAfterHeadToggle(double headLeft, bool isExpanding)
        {
            if (IsVerticalToolbar)
            {
                PlaceFloatingBarAfterHeadToggleVertical(headLeft, isExpanding);
                return;
            }

            var screenWidth = GetFloatingBarScreenWidth(Settings.Advanced.IsEnableAvoidFullScreenHelper);
            ViewboxFloatingBar.UpdateLayout();

            if (!isExpanding)
            {
                var fullCollapsedWidth = GetFloatingBarScaledWidth();
                var collapsedHeadWidth = GetFloatingBarHeadScaledWidth();

                var useFullWidth = fullCollapsedWidth > collapsedHeadWidth * 1.5;
                var collapsedWidth = useFullWidth ? fullCollapsedWidth : collapsedHeadWidth;

                var shouldFlipOnCollapse = !Settings.Appearance.AutoFlipWhenSpaceInsufficient ? isFloatingBarHeadOnRight :
                    (Settings.Appearance.ToolbarPosition == ToolbarPosition.Left
                    ? headLeft - Math.Max(0, collapsedWidth - collapsedHeadWidth) < 0
                    : headLeft + collapsedWidth > screenWidth);
                var wasCollapsedHeadOnRight = isFloatingBarHeadOnRight;
                SetFloatingBarHeadPlacement(shouldFlipOnCollapse);

                ViewboxFloatingBar.UpdateLayout();

                fullCollapsedWidth = GetFloatingBarScaledWidth();
                collapsedHeadWidth = GetFloatingBarHeadScaledWidth();
                useFullWidth = fullCollapsedWidth > collapsedHeadWidth * 1.5;
                collapsedWidth = useFullWidth ? fullCollapsedWidth : collapsedHeadWidth;

                var nextCollapsedLeft = shouldFlipOnCollapse
                    ? headLeft - Math.Max(0, collapsedWidth - collapsedHeadWidth)
                    : headLeft;

                pos.X = ClampFloatingBarLeft(nextCollapsedLeft, collapsedWidth, screenWidth);
                ViewboxFloatingBar.Margin = new Thickness(pos.X, ViewboxFloatingBar.Margin.Top, -2000, -200);

                if (shouldFlipOnCollapse != wasCollapsedHeadOnRight)
                {
                    var actualHeadLeft = ViewboxFloatingBar.Margin.Left + (isFloatingBarHeadOnRight ? Math.Max(0, collapsedWidth - collapsedHeadWidth) : 0);
                    var correction = headLeft - actualHeadLeft;
                    if (Math.Abs(correction) > 0.5)
                    {
                        pos.X += correction;
                        pos.X = ClampFloatingBarLeft(pos.X, collapsedWidth, screenWidth);
                        ViewboxFloatingBar.Margin = new Thickness(pos.X, ViewboxFloatingBar.Margin.Top, -2000, -200);
                    }
                }
                SaveFloatingBarPositionPoint();
                return;
            }

            ViewboxFloatingBar.UpdateLayout();

            var floatingBarWidth = GetFloatingBarScaledWidth();
            var expandedHeadWidth = GetFloatingBarHeadScaledWidth();
            var shouldPlaceToolsOnLeft = !Settings.Appearance.AutoFlipWhenSpaceInsufficient ? isFloatingBarHeadOnRight :
                (Settings.Appearance.ToolbarPosition == ToolbarPosition.Left
                ? headLeft - Math.Max(0, floatingBarWidth - expandedHeadWidth) >= 0
                : headLeft + floatingBarWidth > screenWidth);
            var wasHeadOnRight = isFloatingBarHeadOnRight;

            SetFloatingBarHeadPlacement(shouldPlaceToolsOnLeft);

            ViewboxFloatingBar.UpdateLayout();

            floatingBarWidth = GetFloatingBarScaledWidth();
            expandedHeadWidth = GetFloatingBarHeadScaledWidth();

            var nextLeft = shouldPlaceToolsOnLeft
                ? headLeft - Math.Max(0, floatingBarWidth - expandedHeadWidth)
                : headLeft;

            pos.X = ClampFloatingBarLeft(nextLeft, floatingBarWidth, screenWidth);
            ViewboxFloatingBar.Margin = new Thickness(pos.X, ViewboxFloatingBar.Margin.Top, -2000, -200);

            if (shouldPlaceToolsOnLeft != wasHeadOnRight)
            {
                var actualHeadLeft = ViewboxFloatingBar.Margin.Left + (isFloatingBarHeadOnRight ? Math.Max(0, floatingBarWidth - expandedHeadWidth) : 0);
                var correction = headLeft - actualHeadLeft;
                if (Math.Abs(correction) > 0.5)
                {
                    pos.X += correction;
                    pos.X = ClampFloatingBarLeft(pos.X, GetFloatingBarScaledWidth(), screenWidth);
                    ViewboxFloatingBar.Margin = new Thickness(pos.X, ViewboxFloatingBar.Margin.Top, -2000, -200);
                }
            }
            SaveFloatingBarPositionPoint();
        }

        private void PlaceFloatingBarAfterHeadToggleVertical(double headTop, bool isExpanding)
        {
            var screenHeight = GetFloatingBarScreenHeight(Settings.Advanced.IsEnableAvoidFullScreenHelper);
            ViewboxFloatingBar.UpdateLayout();

            if (!isExpanding)
            {
                var fullCollapsedHeight = GetFloatingBarScaledHeight();
                var collapsedHeadHeight = GetFloatingBarHeadScaledHeight();

                var useFullHeight = fullCollapsedHeight > collapsedHeadHeight * 1.5;
                var collapsedHeight = useFullHeight ? fullCollapsedHeight : collapsedHeadHeight;

                var shouldFlipOnCollapse = !Settings.Appearance.AutoFlipWhenSpaceInsufficient ? isFloatingBarHeadOnBottom :
                    (Settings.Appearance.ToolbarPosition == ToolbarPosition.Top
                    ? headTop - Math.Max(0, collapsedHeight - collapsedHeadHeight) < 0
                    : headTop + collapsedHeight > screenHeight);
                var wasCollapsedHeadOnBottom = isFloatingBarHeadOnBottom;
                SetFloatingBarHeadPlacementVertical(shouldFlipOnCollapse);

                ViewboxFloatingBar.UpdateLayout();

                fullCollapsedHeight = GetFloatingBarScaledHeight();
                collapsedHeadHeight = GetFloatingBarHeadScaledHeight();
                useFullHeight = fullCollapsedHeight > collapsedHeadHeight * 1.5;
                collapsedHeight = useFullHeight ? fullCollapsedHeight : collapsedHeadHeight;

                var nextCollapsedTop = shouldFlipOnCollapse
                    ? headTop - Math.Max(0, collapsedHeight - collapsedHeadHeight)
                    : headTop;

                pos.Y = ClampFloatingBarTop(nextCollapsedTop, collapsedHeight, screenHeight);
                ViewboxFloatingBar.Margin = new Thickness(ViewboxFloatingBar.Margin.Left, pos.Y, -2000, -200);

                if (shouldFlipOnCollapse != wasCollapsedHeadOnBottom)
                {
                    var actualHeadTop = ViewboxFloatingBar.Margin.Top + (isFloatingBarHeadOnBottom ? Math.Max(0, collapsedHeight - collapsedHeadHeight) : 0);
                    var correction = headTop - actualHeadTop;
                    if (Math.Abs(correction) > 0.5)
                    {
                        pos.Y += correction;
                        pos.Y = ClampFloatingBarTop(pos.Y, collapsedHeight, screenHeight);
                        ViewboxFloatingBar.Margin = new Thickness(ViewboxFloatingBar.Margin.Left, pos.Y, -2000, -200);
                    }
                }
                SaveFloatingBarPositionPoint();
                return;
            }

            ViewboxFloatingBar.UpdateLayout();

            var floatingBarHeight = GetFloatingBarScaledHeight();
            var expandedHeadHeight = GetFloatingBarHeadScaledHeight();
            var shouldPlaceToolsOnTop = !Settings.Appearance.AutoFlipWhenSpaceInsufficient ? isFloatingBarHeadOnBottom :
                (Settings.Appearance.ToolbarPosition == ToolbarPosition.Top
                ? headTop - Math.Max(0, floatingBarHeight - expandedHeadHeight) >= 0
                : headTop + floatingBarHeight > screenHeight);
            var wasHeadOnBottom = isFloatingBarHeadOnBottom;

            SetFloatingBarHeadPlacementVertical(shouldPlaceToolsOnTop);

            ViewboxFloatingBar.UpdateLayout();

            floatingBarHeight = GetFloatingBarScaledHeight();
            expandedHeadHeight = GetFloatingBarHeadScaledHeight();

            var nextTop = shouldPlaceToolsOnTop
                ? headTop - Math.Max(0, floatingBarHeight - expandedHeadHeight)
                : headTop;

            pos.Y = ClampFloatingBarTop(nextTop, floatingBarHeight, screenHeight);
            ViewboxFloatingBar.Margin = new Thickness(ViewboxFloatingBar.Margin.Left, pos.Y, -2000, -200);

            if (shouldPlaceToolsOnTop != wasHeadOnBottom)
            {
                var actualHeadTop = ViewboxFloatingBar.Margin.Top + (isFloatingBarHeadOnBottom ? Math.Max(0, floatingBarHeight - expandedHeadHeight) : 0);
                var correction = headTop - actualHeadTop;
                if (Math.Abs(correction) > 0.5)
                {
                    pos.Y += correction;
                    pos.Y = ClampFloatingBarTop(pos.Y, GetFloatingBarScaledHeight(), screenHeight);
                    ViewboxFloatingBar.Margin = new Thickness(ViewboxFloatingBar.Margin.Left, pos.Y, -2000, -200);
                }
            }
            SaveFloatingBarPositionPoint();
        }

        private double NormalizeFloatingBarLeftForScreen(double requestedLeft, double floatingBarWidth,
            double screenWidth)
        {
            if (IsVerticalToolbar)
            {
                return ClampFloatingBarLeft(requestedLeft, floatingBarWidth, screenWidth);
            }

            var headWidth = GetFloatingBarHeadScaledWidth();
            var nextLeft = requestedLeft;
            var shouldPlaceToolsOnLeft = isFloatingBarHeadOnRight;
            var wasHeadOnRight = isFloatingBarHeadOnRight;

            if (Settings.Appearance.AutoFlipWhenSpaceInsufficient)
            {
                var requestedHeadLeft = isFloatingBarHeadOnRight
                    ? requestedLeft + Math.Max(0, floatingBarWidth - headWidth)
                    : requestedLeft;

                if (Settings.Appearance.ToolbarPosition == ToolbarPosition.Right)
                {
                    if (!isFloatingBarHeadOnRight && requestedHeadLeft + floatingBarWidth > screenWidth)
                    {
                        shouldPlaceToolsOnLeft = true;
                        nextLeft = requestedHeadLeft - Math.Max(0, floatingBarWidth - headWidth);
                    }
                    else if (isFloatingBarHeadOnRight && requestedHeadLeft + floatingBarWidth <= screenWidth)
                    {
                        shouldPlaceToolsOnLeft = false;
                    }
                }
                else
                {
                    var toolsLeftWhenUnflipped = requestedHeadLeft - Math.Max(0, floatingBarWidth - headWidth);
                    if (isFloatingBarHeadOnRight && toolsLeftWhenUnflipped < 0)
                    {
                        shouldPlaceToolsOnLeft = false;
                    }
                    else if (!isFloatingBarHeadOnRight && toolsLeftWhenUnflipped >= 0)
                    {
                        shouldPlaceToolsOnLeft = true;
                    }
                }
            }

            SetFloatingBarHeadPlacement(shouldPlaceToolsOnLeft);

            if (shouldPlaceToolsOnLeft != wasHeadOnRight)
            {
                floatingBarWidth = GetFloatingBarScaledWidth();
            }

            return ClampFloatingBarLeft(nextLeft, floatingBarWidth, screenWidth);
        }

        private double NormalizeFloatingBarTopForScreen(double requestedTop, double floatingBarHeight,
            double screenHeight)
        {
            var headHeight = GetFloatingBarHeadScaledHeight();
            var nextTop = requestedTop;
            var shouldPlaceToolsOnTop = isFloatingBarHeadOnBottom;
            var wasHeadOnBottom = isFloatingBarHeadOnBottom;

            if (Settings.Appearance.AutoFlipWhenSpaceInsufficient)
            {
                var requestedHeadTop = isFloatingBarHeadOnBottom
                    ? requestedTop + Math.Max(0, floatingBarHeight - headHeight)
                    : requestedTop;

                if (Settings.Appearance.ToolbarPosition == ToolbarPosition.Bottom)
                {
                    if (!isFloatingBarHeadOnBottom && requestedHeadTop + floatingBarHeight > screenHeight)
                    {
                        shouldPlaceToolsOnTop = true;
                        nextTop = requestedHeadTop - Math.Max(0, floatingBarHeight - headHeight);
                    }
                    else if (isFloatingBarHeadOnBottom && requestedHeadTop + floatingBarHeight <= screenHeight)
                    {
                        shouldPlaceToolsOnTop = false;
                    }
                }
                else
                {
                    var toolsTopWhenUnflipped = requestedHeadTop - Math.Max(0, floatingBarHeight - headHeight);
                    if (isFloatingBarHeadOnBottom && toolsTopWhenUnflipped < 0)
                    {
                        shouldPlaceToolsOnTop = false;
                    }
                    else if (!isFloatingBarHeadOnBottom && toolsTopWhenUnflipped >= 0)
                    {
                        shouldPlaceToolsOnTop = true;
                    }
                }
            }

            SetFloatingBarHeadPlacementVertical(shouldPlaceToolsOnTop);

            if (shouldPlaceToolsOnTop != wasHeadOnBottom)
            {
                floatingBarHeight = GetFloatingBarScaledHeight();
            }

            return ClampFloatingBarTop(nextTop, floatingBarHeight, screenHeight);
        }

        private double GetCurrentFloatingBarHeadLeft()
        {
            if (IsVerticalToolbar)
            {
                return ViewboxFloatingBar.Margin.Left;
            }

            var floatingBarWidth = GetFloatingBarScaledWidth();
            var headWidth = GetFloatingBarHeadScaledWidth();
            var headOffset = isFloatingBarHeadOnRight
                ? Math.Max(0, floatingBarWidth - headWidth)
                : 0;
            return ViewboxFloatingBar.Margin.Left + headOffset;
        }

        private double GetCurrentFloatingBarHeadTop()
        {
            if (!IsVerticalToolbar)
            {
                return ViewboxFloatingBar.Margin.Top;
            }

            var floatingBarHeight = GetFloatingBarScaledHeight();
            var headHeight = GetFloatingBarHeadScaledHeight();
            var headOffset = isFloatingBarHeadOnBottom
                ? Math.Max(0, floatingBarHeight - headHeight)
                : 0;
            return ViewboxFloatingBar.Margin.Top + headOffset;
        }

        private void SaveFloatingBarPositionPoint()
        {
            var currentPoint = new Point(ViewboxFloatingBar.Margin.Left, ViewboxFloatingBar.Margin.Top);
            if (IsInPPTPresentationMode)
                pointPPT = currentPoint;
            else
                pointDesktop = currentPoint;
        }

        /// <summary>
        /// 浮动工具栏边距动画处理
        /// </summary>
        /// <param name="MarginFromEdge">边缘边距</param>
        /// <param name="PosXCaculatedWithTaskbarHeight">是否考虑任务栏高度计算位置</param>
        /// <param name="skipAnimation">是否跳过动画直接定位（用于启动时快速恢复位置）</param>
        public async void ViewboxFloatingBarMarginAnimation(int MarginFromEdge,
            bool PosXCaculatedWithTaskbarHeight = false, bool skipAnimation = false)
        {
            if (currentMode == 1)
            {
                return;
            }

            if (MarginFromEdge == 60) MarginFromEdge = 55;

            // LTSC D-21：等值改成"按真实动画时长等"，不再猜 200ms。
            //
            // 原来是 `await Task.Delay(200)`，但这条路径上的动画实际是 0.35 秒（350ms）：
            // 200ms 一到就硬落地，动画最后 150ms 被截断，画面看起来是"跳"到终点的。
            // 反过来 MW_AutoFold 那条 0.1 秒的路径又要白等 100ms。
            //
            // 现在由 ViewboxFloatingBarMarginAnimationCore 回传它真正起的动画时长，
            // 按那个等；再留 50ms 余量吸收合成层的最后一帧。
            TimeSpan animationDuration = TimeSpan.Zero;
            bool usedSpring = false;

            await Dispatcher.InvokeAsync(() =>
            {
                animationDuration = ViewboxFloatingBarMarginAnimationCore(
                    MarginFromEdge, PosXCaculatedWithTaskbarHeight, !skipAnimation, out usedSpring);
            });

            // LTSC D-24：走弹簧时由它自己的 finished 回调负责落地。
            // 这里必须直接返回——否则会在弹簧还在跑的时候抢先落地一次：
            // Margin 被推到目标、tt 被归零，弹簧下一秒又把 tt 推回去，
            // 视觉位置 = 目标 + 位移，直接冲过头，收尾再弹回来，就是"抽一下"。
            if (usedSpring) return;

            if (!skipAnimation && animationDuration > TimeSpan.Zero)
            {
                // 上限 1 秒：万一某个分支给出了异常大的时长，不至于把界面卡住。
                var wait = animationDuration + TimeSpan.FromMilliseconds(50);
                if (wait > TimeSpan.FromSeconds(1)) wait = TimeSpan.FromSeconds(1);
                await Task.Delay(wait);
            }

            await Dispatcher.InvokeAsync(() =>
            {
                AnimationsHelper.LandMarginOnCompositionLayer(ViewboxFloatingBar,
                    new Thickness(pos.X, pos.Y, -2000, -200));
                isViewboxFloatingBarMarginAnimationRunning = false;
                if (!Topmost) ViewboxFloatingBar.Visibility = Visibility.Hidden;

                if (!string.IsNullOrEmpty(_currentToolMode))
                {
                    SetFloatingBarHighlightPosition(_currentToolMode);
                }
            });
        }

        /// <returns>本次真正启动的位移动画时长；没有启动动画（纯瞬移）时返回 TimeSpan.Zero。</returns>
        private TimeSpan ViewboxFloatingBarMarginAnimationCore(int MarginFromEdge,
            bool PosXCaculatedWithTaskbarHeight, bool animate, out bool usedSpring)
        {
            usedSpring = false;
            if (!Topmost)
                MarginFromEdge = -60;
            else
            {
                ViewboxFloatingBar.Visibility = Visibility.Visible;
                ViewboxFloatingBar.UpdateLayout();
            }
            isViewboxFloatingBarMarginAnimationRunning = true;

            double dpiScaleX = 1, dpiScaleY = 1;
            var source = PresentationSource.FromVisual(this);
            if (source != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            var screen = GetFloatingBarTargetScreen();
            double screenWidth, screenHeight;
            double toolbarHeight;
            double screenBoundsWidth = screen.Bounds.Width / dpiScaleX;
            double screenBoundsHeight = screen.Bounds.Height / dpiScaleY;
            if (Settings.Advanced.IsEnableAvoidFullScreenHelper && PosXCaculatedWithTaskbarHeight)
            {
                screenWidth = screen.WorkingArea.Width / dpiScaleX;
                screenHeight = screen.WorkingArea.Height / dpiScaleY;
                toolbarHeight = 0;
            }
            else
            {
                screenWidth = screen.Bounds.Width / dpiScaleX;
                screenHeight = screen.Bounds.Height / dpiScaleY;
                toolbarHeight = ForegroundWindowInfo.GetTaskbarHeight(screen, dpiScaleY);
            }

            double baseWidth = ViewboxFloatingBar.ActualWidth;

            if (baseWidth <= 0)
            {
                baseWidth = ViewboxFloatingBar.DesiredSize.Width;
            }

            if (baseWidth <= 0)
            {
                baseWidth = ViewboxFloatingBar.RenderSize.Width;
            }

            if (baseWidth <= 0)
            {
                baseWidth = 200;
                LogHelper.WriteLogToFile($"浮动栏宽度无法获取，使用估算值: {baseWidth}");
            }

            double floatingBarWidth = baseWidth * ViewboxFloatingBarScaleTransform.ScaleX;

            double baseHeight = ViewboxFloatingBar.ActualHeight;
            if (baseHeight <= 0)
            {
                baseHeight = ViewboxFloatingBar.DesiredSize.Height;
            }
            if (baseHeight <= 0)
            {
                baseHeight = ViewboxFloatingBar.RenderSize.Height;
            }
            if (baseHeight <= 0)
            {
                baseHeight = 58;
            }
            double floatingBarHeight = baseHeight * ViewboxFloatingBarScaleTransform.ScaleY;


            if (!IsVerticalToolbar && QuickColorPalette != null &&
                (QuickColorPalette.QuickColorPalettePanel.Visibility == Visibility.Visible ||
                 QuickColorPalette.QuickColorPaletteSingleRowPanel.Visibility == Visibility.Visible))
            {
                if (Settings.Appearance.QuickColorPaletteDisplayMode == 0)
                {
                    floatingBarWidth = Math.Max(floatingBarWidth, 120 * ViewboxFloatingBarScaleTransform.ScaleX);
                }
                else
                {
                    floatingBarWidth = Math.Max(floatingBarWidth, 68 * ViewboxFloatingBarScaleTransform.ScaleX);
                }
            }

            pos.X = (screenWidth - floatingBarWidth) / 2;

            if (IsVerticalToolbar)
            {
                pos.Y = (screenHeight - floatingBarHeight) / 2;
            }
            else
            {
                if (!PosXCaculatedWithTaskbarHeight)
                {
                    if (toolbarHeight == 0)
                    {
                        pos.Y = screenHeight - MarginFromEdge * ViewboxFloatingBarScaleTransform.ScaleY;
                    }
                    else
                    {
                        pos.Y = screenHeight - MarginFromEdge * ViewboxFloatingBarScaleTransform.ScaleY - toolbarHeight;
                    }

                    baseWidth = GetElementWidthForFloatingBar(ViewboxFloatingBar, 200);
                    if (baseWidth <= 0)
                    {
                        pos.Y = screenHeight - floatingBarHeight -
                               3 * ViewboxFloatingBarScaleTransform.ScaleY;
                    }
                    floatingBarWidth = baseWidth * ViewboxFloatingBarScaleTransform.ScaleX;

                    baseHeight = GetElementHeightForFloatingBar(ViewboxFloatingBar, 58);
                    if (baseHeight <= 0)
                    {
                        baseHeight = 58;
                    }
                }
            }

            if (MarginFromEdge != -60)
            {
                if (!IsVerticalToolbar && QuickColorPalette?.Visibility == Visibility.Visible)
                {
                    if (Settings.Appearance.QuickColorPaletteDisplayMode == 0)
                    {
                        floatingBarWidth = Math.Max(floatingBarWidth, 200 * ViewboxFloatingBarScaleTransform.ScaleX);
                    }
                    else
                    {
                        floatingBarWidth = Math.Max(floatingBarWidth, 108 * ViewboxFloatingBarScaleTransform.ScaleX);
                    }
                }

                var toolbarPosition = Settings.Appearance.ToolbarPosition;
                switch (toolbarPosition)
                {
                    case ToolbarPosition.Right:
                    case ToolbarPosition.Left:
                        pos.X = (screenWidth - floatingBarWidth) / 2;
                        if (toolbarHeight == 0)
                        {
                            pos.Y = screenHeight - floatingBarHeight -
                                   3 * ViewboxFloatingBarScaleTransform.ScaleY;
                        }
                        else
                        {
                            pos.Y = screenHeight - floatingBarHeight -
                                   toolbarHeight - ViewboxFloatingBarScaleTransform.ScaleY * 3;
                        }
                        break;

                    case ToolbarPosition.Top:
                    case ToolbarPosition.Bottom:
                        pos.X = screenBoundsWidth - floatingBarWidth -
                               3 * ViewboxFloatingBarScaleTransform.ScaleX;
                        pos.Y = (screenHeight - floatingBarHeight) / 2;
                        break;
                }

                if (MarginFromEdge < 0)
                {
                    if (IsVerticalToolbar)
                        pos.Y = screenHeight - MarginFromEdge * ViewboxFloatingBarScaleTransform.ScaleY;
                    else
                        pos.X = screenWidth - MarginFromEdge * ViewboxFloatingBarScaleTransform.ScaleX;
                }
                else if (IsInPPTPresentationMode)
                {
                    switch (toolbarPosition)
                    {
                        case ToolbarPosition.Right:
                        case ToolbarPosition.Left:
                            pos.X = (screenWidth - floatingBarWidth) / 2;
                            pos.Y = screenHeight - floatingBarHeight +
                                   2 * ViewboxFloatingBarScaleTransform.ScaleY;
                            break;

                        case ToolbarPosition.Top:
                        case ToolbarPosition.Bottom:
                            pos.X = screenBoundsWidth - floatingBarWidth -
                                   3 * ViewboxFloatingBarScaleTransform.ScaleX;
                            pos.Y = (screenHeight - floatingBarHeight) / 2;
                            break;
                    }
                }

                if (IsVerticalToolbar)
                {
                    pos.Y = NormalizeFloatingBarTopForScreen(pos.Y, floatingBarHeight, screenHeight);
                }
                else
                {
                    pos.X = NormalizeFloatingBarLeftForScreen(pos.X, floatingBarWidth, screenWidth);
                }

                if (IsInPPTPresentationMode)
                {
                    if (pointPPT.X != -1 || pointPPT.Y != -1)
                    {
                        if (Math.Abs(pointPPT.Y - pos.Y) > 50)
                            pos = pointPPT;
                        else
                            pointPPT = pos;
                    }
                }
                else
                {
                    // 同另一处：屏幕外的 pointDesktop（折叠时的隐藏位）不能用来恢复位置。
                    if ((pointDesktop.X != -1 || pointDesktop.Y != -1) &&
                        pointDesktop.Y >= 0 && pointDesktop.Y < screenHeight &&
                        pointDesktop.X >= 0 && pointDesktop.X < screenWidth)
                    {
                        if (Math.Abs(pointDesktop.Y - pos.Y) > 50)
                            pos = pointDesktop;
                        else
                            pointDesktop = pos;
                    }
                }
            }
            else if (IsVerticalToolbar)
            {
                pos.X = screenWidth - MarginFromEdge * ViewboxFloatingBarScaleTransform.ScaleX;
            }

            int myVersion = ++_floatingBarPosVersion;

            TimeSpan startedDuration = TimeSpan.Zero;

            // 入场动画期间不做位移动画。
            //
            // 两者抢同一个 TranslateTransform：入场动画用它做从下往上滑，
            // 定位动画用它做从当前位置弹到新位置。同时跑会互相覆盖，
            // 视觉上就是"动了两次"。实测启动瞬间定位动画要落地 6 次，
            // 全压在入场动画那 1.6 秒里。
            //
            // 直接瞬移：入场动画负责"出现"这个观感，定位只需要位置正确。
            if (animate && FloatingBarIntroAnimation.IsPlaying)
            {
                animate = false;
            }

            if (animate)
            {
                // LTSC D-22：改走【每帧积分的弹簧动画】，替换原来的 Storyboard + CircleEase。
                //
                // 原来的问题：Storyboard 是按固定时长插值，位移曲线是死的；
                // 拖到一半改目标会从静止重新起步，速度断掉，观感是"一顿"。
                //
                // 现在交给 SpringAnimator：
                //   - 跑在 CompositionTarget.Rendering 上，帧率跟着显示器刷新率走
                //   - 每次改目标都保留当前速度，拖拽中反手一甩是带着惯性走的
                //   - 有轻微过冲和回弹，是弹簧的自然手感
                //   - 全程只动 TranslateTransform，不触发布局传递
                var from = ViewboxFloatingBar.Margin;
                var targetMargin = new Thickness(pos.X, pos.Y, 0, -20);

                var tt = AnimationsHelper.TryEnsureTranslateTransform(ViewboxFloatingBar);
                if (tt != null)
                {
                    usedSpring = true;
                    // 位移量 = 目标 Margin 相对当前 Margin 的差（考虑对齐方向）
                    double dx = AnimationsHelper.MarginShiftX(from, targetMargin, ViewboxFloatingBar.HorizontalAlignment);
                    double dy = AnimationsHelper.MarginShiftY(from, targetMargin, ViewboxFloatingBar.VerticalAlignment);

                    // 动画期间 Margin 不动，靠 TranslateTransform 视觉位移；
                    // 弹簧把位移推到目标后，再一次性落地到真实 Margin（只算一次布局）。
                    // 预设用 SpringAnimator.Default（行与布局位移），带一点轻过冲。
                    SpringAnimator.Animate(
                        tt,
                        (float)dx,
                        (float)dy,
                        finished: () =>
                        {
                            // 不是最新一代就丢弃：旧动画的回调不能覆盖新动画已经落好的位置。
                            if (myVersion != _floatingBarPosVersion)
                            {
                                LogHelper.WriteLogToFile(
                                    $"[定位] 丢弃过期落地 v{myVersion}（当前 v{_floatingBarPosVersion}）targetMargin={targetMargin}",
                                    LogHelper.LogType.Info);
                                return;
                            }
                            AnimationsHelper.LandMarginOnCompositionLayer(ViewboxFloatingBar, targetMargin);
                            // 落地把 tt 归零了，弹簧内部缓存也必须跟着归零，
                            // 否则下次复用时它会从旧值继续推，画面会跳。
                            SpringAnimator.SyncToTransform(
                                ViewboxFloatingBar.RenderTransform as TranslateTransform);

                            isViewboxFloatingBarMarginAnimationRunning = false;
                            if (!Topmost) ViewboxFloatingBar.Visibility = Visibility.Hidden;
                            if (!string.IsNullOrEmpty(_currentToolMode))
                            {
                                SetFloatingBarHighlightPosition(_currentToolMode);
                            }
                        });

                    // 弹簧没有固定时长，告诉调用方"别按时间等，等 finished 回调"
                    startedDuration = TimeSpan.Zero;
                }
                else
                {
                    // 控件不适合走合成层（RenderTransform 已被别人占用），退回插值动画
                    var duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(0.35));
                    if (AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                            ViewboxFloatingBar, targetMargin, 0.35, out startedDuration, new CircleEase()))
                    {
                    }
                    else
                    {
                        var marginAnimation = new ThicknessAnimation
                        {
                            Duration = duration,
                            From = from,
                            To = targetMargin,
                            EasingFunction = new CircleEase()
                        };
                        ViewboxFloatingBar.BeginAnimation(MarginProperty, marginAnimation);
                        startedDuration = duration;
                    }
                }
            }
            else
            {
                // LTSC D-05：直接定位时也要清掉可能残留的合成层位移，
                // 否则上一次动画没跑完就走到这里，translate 会和 Margin 叠加导致偏移。
                AnimationsHelper.LandMarginOnCompositionLayer(ViewboxFloatingBar,
                    new Thickness(pos.X, pos.Y, 0, -20));
            }

            if (!animate) isViewboxFloatingBarMarginAnimationRunning = false;
            if (!Topmost) ViewboxFloatingBar.Visibility = Visibility.Hidden;

            return startedDuration;
        }

        /// <summary>
        /// 桌面模式下的浮动工具栏边距动画处理
        /// </summary>
        public async void PureViewboxFloatingBarMarginAnimationInDesktopMode()
        {
            // 在白板模式下不执行浮动栏动画
            if (currentMode == 1)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ViewboxFloatingBar.Visibility = Visibility.Visible;
                ViewboxFloatingBar.UpdateLayout();
                isViewboxFloatingBarMarginAnimationRunning = true;

                double dpiScaleX = 1, dpiScaleY = 1;
                var source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                var screen = GetFloatingBarTargetScreen();
                double screenWidth, screenHeight;
                double toolbarHeight;
                double screenBoundsWidth = screen.Bounds.Width / dpiScaleX;
                if (Settings.Advanced.IsEnableAvoidFullScreenHelper)
                {
                    screenWidth = screen.WorkingArea.Width / dpiScaleX;
                    screenHeight = screen.WorkingArea.Height / dpiScaleY;
                    toolbarHeight = 0;
                }
                else
                {
                    screenWidth = screen.Bounds.Width / dpiScaleX;
                    screenHeight = screen.Bounds.Height / dpiScaleY;
                    toolbarHeight = ForegroundWindowInfo.GetTaskbarHeight(screen, dpiScaleY);
                }

                double baseWidth = GetElementWidthForFloatingBar(ViewboxFloatingBar, 200);
                if (baseWidth <= 0)
                {
                    baseWidth = 200;
                    LogHelper.WriteLogToFile($"浮动栏宽度无法获取，使用估算值: {baseWidth}");
                }
                double floatingBarWidth = baseWidth * ViewboxFloatingBarScaleTransform.ScaleX;

                double baseHeight = GetElementHeightForFloatingBar(ViewboxFloatingBar, 58);
                if (baseHeight <= 0)
                {
                    baseHeight = 58;
                }
                double floatingBarHeight = baseHeight * ViewboxFloatingBarScaleTransform.ScaleY;


                if (!IsVerticalToolbar && QuickColorPalette?.Visibility == Visibility.Visible)
                {
                    if (Settings.Appearance.QuickColorPaletteDisplayMode == 0)
                    {
                        floatingBarWidth = Math.Max(floatingBarWidth, 140 * ViewboxFloatingBarScaleTransform.ScaleX);
                    }
                    else
                    {
                        floatingBarWidth = Math.Max(floatingBarWidth, 86 * ViewboxFloatingBarScaleTransform.ScaleX);
                    }
                }

                var toolbarPosition = Settings.Appearance.ToolbarPosition;
                switch (toolbarPosition)
                {
                    case ToolbarPosition.Right:
                    case ToolbarPosition.Left:
                        pos.X = (screenWidth - floatingBarWidth) / 2;
                        if (toolbarHeight == 0)
                        {
                            pos.Y = screenHeight - floatingBarHeight -
                                   3 * ViewboxFloatingBarScaleTransform.ScaleY;
                        }
                        else
                        {
                            pos.Y = screenHeight - floatingBarHeight -
                                   toolbarHeight - ViewboxFloatingBarScaleTransform.ScaleY * 3;
                        }
                        break;

                    case ToolbarPosition.Top:
                    case ToolbarPosition.Bottom:
                        pos.X = screenBoundsWidth - floatingBarWidth -
                               3 * ViewboxFloatingBarScaleTransform.ScaleX;
                        pos.Y = (screenHeight - floatingBarHeight) / 2;
                        break;
                }

                if (IsVerticalToolbar)
                {
                    pos.Y = NormalizeFloatingBarTopForScreen(pos.Y, floatingBarHeight, screenHeight);
                }
                else
                {
                    pos.X = NormalizeFloatingBarLeftForScreen(pos.X, floatingBarWidth, screenWidth);
                }

                // 只有 pointDesktop 落在屏幕内才用它恢复位置。
                //
                // 原来是无条件比：只要和算出来的 pos 差超过 50 就用 pointDesktop。
                // 但浮窗折叠时会被移到屏幕外（y 大于屏幕高度），那个隐藏位置
                // 如果被存进 pointDesktop，展开时就会把浮窗放回隐藏位置 ——
                // 表现出来就是「从侧栏展开后，浮窗出现在默认位置上面」。
                // 加一条范围检查：屏幕外的坐标直接不认。
                if ((pointDesktop.X != -1 || pointDesktop.Y != -1) &&
                    pointDesktop.Y >= 0 && pointDesktop.Y < screenHeight &&
                    pointDesktop.X >= 0 && pointDesktop.X < screenWidth)
                {
                    if (Math.Abs(pointDesktop.Y - pos.Y) > 50)
                        pos = pointDesktop;
                    else
                        pointDesktop = pos;
                }

                // LTSC D-05：改走合成层（缩放在 LayoutTransform 上，不会受影响）
                var targetMargin = new Thickness(pos.X, pos.Y, 0, -20);
                if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                        ViewboxFloatingBar, targetMargin, 0.35, new CircleEase()))
                {
                    var marginAnimation = new ThicknessAnimation
                    {
                        Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(0.35)),
                        From = ViewboxFloatingBar.Margin,
                        To = targetMargin,
                        EasingFunction = new CircleEase()
                    };
                    ViewboxFloatingBar.BeginAnimation(MarginProperty, marginAnimation);
                }
            });

            await Task.Delay(349);

            await Dispatcher.InvokeAsync(() =>
            {
                AnimationsHelper.LandMarginOnCompositionLayer(ViewboxFloatingBar,
                    new Thickness(pos.X, pos.Y, -2000, -200));

                if (!string.IsNullOrEmpty(_currentToolMode))
                {
                    SetFloatingBarHighlightPosition(_currentToolMode);
                }
            });
        }

        /// <summary>
        /// PPT模式下的浮动工具栏边距动画处理
        /// </summary>
        /// <param name="isRetry">是否为重试操作</param>
        public async void PureViewboxFloatingBarMarginAnimationInPPTMode(bool isRetry = false)
        {
            // 新增：在白板模式下不执行浮动栏动画
            if (currentMode == 1)
            {
                return;
            }

            await Dispatcher.InvokeAsync(() =>
            {
                ViewboxFloatingBar.Visibility = Visibility.Visible;
                ViewboxFloatingBar.UpdateLayout();
                isViewboxFloatingBarMarginAnimationRunning = true;

                double dpiScaleX = 1, dpiScaleY = 1;
                var source = PresentationSource.FromVisual(this);
                if (source != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                var screen = GetFloatingBarTargetScreen();
                double screenWidth = screen.Bounds.Width / dpiScaleX, screenHeight = screen.Bounds.Height / dpiScaleY;
                // 仅计算Windows任务栏高度，不考虑其他程序对工作区的影响
                var toolbarHeight = ForegroundWindowInfo.GetTaskbarHeight(screen, dpiScaleY);

                // 计算浮动栏位置，考虑快捷调色盘的显示状态
                // 使用更可靠的方法获取浮动栏宽度
                double baseWidth = GetElementWidthForFloatingBar(ViewboxFloatingBar, 200);
                if (baseWidth <= 0)
                {
                    baseWidth = 200;
                    LogHelper.WriteLogToFile($"浮动栏宽度无法获取，使用估算值: {baseWidth}");
                }
                double floatingBarWidth = baseWidth * ViewboxFloatingBarScaleTransform.ScaleX;

                double baseHeight = GetElementHeightForFloatingBar(ViewboxFloatingBar, 58);
                if (baseHeight <= 0) baseHeight = 58;
                double floatingBarHeight = baseHeight * ViewboxFloatingBarScaleTransform.ScaleY;


                if (!IsVerticalToolbar && QuickColorPalette?.Visibility == Visibility.Visible)
                {
                    if (Settings.Appearance.QuickColorPaletteDisplayMode == 0)
                    {
                        floatingBarWidth = Math.Max(floatingBarWidth, 140 * ViewboxFloatingBarScaleTransform.ScaleX);
                    }
                    else
                    {
                        floatingBarWidth = Math.Max(floatingBarWidth, 86 * ViewboxFloatingBarScaleTransform.ScaleX);
                    }
                }

                var toolbarPosition = Settings.Appearance.ToolbarPosition;
                switch (toolbarPosition)
                {
                    case ToolbarPosition.Right:
                    case ToolbarPosition.Left:
                        pos.X = (screenWidth - floatingBarWidth) / 2;
                        pos.Y = screenHeight - floatingBarHeight +
                               2 * ViewboxFloatingBarScaleTransform.ScaleY;
                        break;

                    case ToolbarPosition.Top:
                    case ToolbarPosition.Bottom:
                        pos.X = screenWidth - floatingBarWidth -
                               3 * ViewboxFloatingBarScaleTransform.ScaleX;
                        pos.Y = (screenHeight - floatingBarHeight) / 2;
                        break;
                }

                if (IsVerticalToolbar)
                {
                    pos.Y = NormalizeFloatingBarTopForScreen(pos.Y, floatingBarHeight, screenHeight);
                }
                else
                {
                    pos.X = NormalizeFloatingBarLeftForScreen(pos.X, floatingBarWidth, screenWidth);
                }

                if (pointPPT.X != -1 || pointPPT.Y != -1)
                {
                    if (Math.Abs(pointPPT.Y - pos.Y) > 50)
                        pos = pointPPT;
                    else
                        pointPPT = pos;
                }

                // LTSC D-05：改走合成层（缩放在 LayoutTransform 上，不会受影响）
                var targetMargin = new Thickness(pos.X, pos.Y, 0, -20);
                if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                        ViewboxFloatingBar, targetMargin, 0.35, new CircleEase()))
                {
                    var marginAnimation = new ThicknessAnimation
                    {
                        Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(0.35)),
                        From = ViewboxFloatingBar.Margin,
                        To = targetMargin,
                        EasingFunction = new CircleEase()
                    };
                    ViewboxFloatingBar.BeginAnimation(MarginProperty, marginAnimation);
                }
            });

            await Task.Delay(349);

            await Dispatcher.InvokeAsync(() =>
            {
                AnimationsHelper.LandMarginOnCompositionLayer(ViewboxFloatingBar,
                    new Thickness(pos.X, pos.Y, -2000, -200));

                if (!string.IsNullOrEmpty(_currentToolMode))
                {
                    SetFloatingBarHighlightPosition(_currentToolMode);
                }
            });

            if (Settings.ModeSettings.IsPPTOnlyMode && !isRetry)
            {
                await Task.Delay(2000); // 等待动画完成后再检查

                bool isFloatingBarVisible = false;
                await Dispatcher.InvokeAsync(() =>
                {
                    // 检查浮动栏是否真的显示了
                    isFloatingBarVisible = ViewboxFloatingBar.Visibility == Visibility.Visible &&
                                          ViewboxFloatingBar.Margin.Left >= 0 &&
                                          ViewboxFloatingBar.Margin.Top >= 0;
                });

                if (!isFloatingBarVisible)
                {
                    PureViewboxFloatingBarMarginAnimationInPPTMode(true);
                }
            }
        }

        private Screen GetFloatingBarTargetScreen()
        {
            try
            {
                if (Settings.Advanced.EnableMultiScreenSupport &&
                    Settings.Advanced.FollowMouseForScreenSelection &&
                    ScreenDetectionHelper.HasMultipleScreens())
                {
                    var mouseScreen = Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
                    if (mouseScreen != null)
                    {
                        return mouseScreen;
                    }
                }

                var windowHandle = new WindowInteropHelper(this).Handle;
                return Screen.FromHandle(windowHandle);
            }
            catch
            {
                return Screen.PrimaryScreen;
            }
        }

        private Screen GetCurrentFloatingBarScreen()
        {
            try
            {
                if (ViewboxFloatingBar == null || !IsLoaded)
                {
                    return null;
                }

                var center = ViewboxFloatingBar.PointToScreen(new Point(
                    Math.Max(0, ViewboxFloatingBar.ActualWidth / 2),
                    Math.Max(0, ViewboxFloatingBar.ActualHeight / 2)));
                return Screen.FromPoint(new System.Drawing.Point((int)center.X, (int)center.Y));
            }
            catch
            {
                return null;
            }
        }

        internal void RefreshFloatingBarScreenFollowState()
        {
            try
            {
                var enableFollow = Settings.Advanced.EnableMultiScreenSupport &&
                                   Settings.Advanced.FollowMouseForScreenSelection &&
                                   ScreenDetectionHelper.HasMultipleScreens();

                if (!enableFollow)
                {
                    _floatingBarScreenFollowTimer?.Stop();
                    _lastFloatingBarScreenDeviceName = null;
                    return;
                }

                if (_floatingBarScreenFollowTimer == null)
                {
                    _floatingBarScreenFollowTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(350)
                    };
                    _floatingBarScreenFollowTimer.Tick += FloatingBarScreenFollowTimer_Tick;
                }

                _lastFloatingBarScreenDeviceName = GetCurrentFloatingBarScreen()?.DeviceName;
                _lastCanvasScreenDeviceName = _lastFloatingBarScreenDeviceName;

                if (!_floatingBarScreenFollowTimer.IsEnabled)
                {
                    _floatingBarScreenFollowTimer.Start();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"刷新浮动栏多屏跟随状态失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void FloatingBarScreenFollowTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                if (!Settings.Advanced.EnableMultiScreenSupport ||
                    !Settings.Advanced.FollowMouseForScreenSelection ||
                    !ScreenDetectionHelper.HasMultipleScreens())
                {
                    _floatingBarScreenFollowTimer?.Stop();
                    _lastFloatingBarScreenDeviceName = null;
                    return;
                }

                if (currentMode == 1 || isDragDropInEffect || ViewboxFloatingBar.Visibility != Visibility.Visible)
                {
                    return;
                }

                var mouseScreen = Screen.FromPoint(System.Windows.Forms.Control.MousePosition);
                var currentFloatingBarScreen = GetCurrentFloatingBarScreen();

                if (mouseScreen == null || currentFloatingBarScreen == null)
                {
                    return;
                }

                if (mouseScreen.DeviceName == currentFloatingBarScreen.DeviceName)
                {
                    _lastFloatingBarScreenDeviceName = currentFloatingBarScreen.DeviceName;
                    return;
                }

                if (mouseScreen.DeviceName == _lastFloatingBarScreenDeviceName)
                {
                    return;
                }

                _lastFloatingBarScreenDeviceName = mouseScreen.DeviceName;
                RebuildCanvasOnTargetScreen(mouseScreen);

                if (IsInPPTPresentationMode)
                {
                    PureViewboxFloatingBarMarginAnimationInPPTMode();
                }
                else
                {
                    PureViewboxFloatingBarMarginAnimationInDesktopMode();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"浮动栏跨屏跟随失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void RebuildCanvasOnTargetScreen(Screen targetScreen)
        {
            try
            {
                if (targetScreen == null || _isRebuildingCanvasForScreen)
                {
                    return;
                }

                if (_lastCanvasScreenDeviceName == targetScreen.DeviceName)
                {
                    return;
                }

                _isRebuildingCanvasForScreen = true;

                double dpiScaleX = 1, dpiScaleY = 1;
                var source = PresentationSource.FromVisual(this);
                if (source?.CompositionTarget != null)
                {
                    dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                    dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
                }

                // 先移动主窗口到目标屏，确保画布承载区域切换到新屏幕。
                //
                // LTSC D-14：这里原来用 Bounds（整块屏，含任务栏）。
                // 造成两个症状，其实是同一个根因：
                //   1. 不在 PPT 模式时窗口被撑到 1080，底边被任务栏压住
                //   2. 画布高度也被设成 1080，比窗口多出 30px，底部空出一条
                // 统一改用 WorkingArea，窗口和画布都用同一个高度。
                var workArea = targetScreen.WorkingArea;
                MainWindow.MoveWindow(
                    new WindowInteropHelper(this).Handle,
                    workArea.X,
                    workArea.Y,
                    workArea.Width,
                    workArea.Height,
                    true);

                // 重新铺设画布尺寸，强制触发布局刷新。
                inkCanvas.Width = workArea.Width / dpiScaleX;
                inkCanvas.Height = workArea.Height / dpiScaleY;
                inkCanvas.InvalidateMeasure();
                inkCanvas.InvalidateArrange();
                inkCanvas.UpdateLayout();

                if (GridInkCanvasSelectionCover != null)
                {
                    GridInkCanvasSelectionCover.Width = inkCanvas.Width;
                    GridInkCanvasSelectionCover.Height = inkCanvas.Height;
                    GridInkCanvasSelectionCover.InvalidateMeasure();
                    GridInkCanvasSelectionCover.InvalidateArrange();
                }

                _lastCanvasScreenDeviceName = targetScreen.DeviceName;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"在新屏重建画布失败: {ex.Message}", LogHelper.LogType.Warning);
            }
            finally
            {
                _isRebuildingCanvasForScreen = false;
            }
        }

        private void InsertImageOptions_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation(FloatingBarStrings.Board_InsertImage)) return;
            // Check if the image options panel is currently visible
            bool isImagePanelVisible = BoardImageOptionsPanel.IsOpen;

            // Toggle the image options panel
            if (isImagePanelVisible)
            {
                // Panel was visible, so hide it with animation
                AnimationsHelper.HidePopupWithSlideAndFade(BoardImageOptionsPanel);
            }
            else
            {
                // Panel was hidden, so hide other panels and show this one
                HideSubPanels();
                AnimationsHelper.ShowPopupWithSlideAndFade(BoardImageOptionsPanel);
                _popupManager?.BringToFront(BoardImageOptionsPanel);
            }
        }

        private void CloseImageOptionsPanel_MouseUp(object sender, MouseButtonEventArgs e)
        {
            AnimationsHelper.HidePopupWithSlideAndFade(BoardImageOptionsPanel);
        }

        private async void ImageOptionScreenshot_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("插入截图")) return;
            // Hide the options panel
            AnimationsHelper.HidePopupWithSlideAndFade(BoardImageOptionsPanel);

            // Wait a bit for the panel to hide
            await Task.Delay(100);

            // Capture screenshot and insert to canvas
            await CaptureScreenshotAndInsert();
        }

        private async void ImageOptionSelectFile_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation(FloatingBarStrings.Board_InsertImage)) return;
            // Hide the options panel
            AnimationsHelper.HideWithSlideAndFade(BoardImageOptionsPanel);

            // Open file dialog to select image
            var dialog = new OpenFileDialog
            {
                Filter = MainWindowStrings.Main_FileInsert_OpenDialogFilter
            };
            if (dialog.ShowDialog() == true)
            {
                string filePath = dialog.FileName;
                string extension = System.IO.Path.GetExtension(filePath);
                FrameworkElement element = IsSupportedMediaExtension(extension)
                    ? await CreateMediaElementAsync(filePath)
                    : await CreateAndCompressImageAsync(filePath);
                if (element != null)
                {
                    string timestamp = IsCanvasMediaElement(element)
                        ? "media_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
                        : "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                    if (string.IsNullOrEmpty(element.Name)) element.Name = timestamp;

                    // 初始化TransformGroup
                    var transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform(1, 1));
                    transformGroup.Children.Add(new TranslateTransform(0, 0));
                    transformGroup.Children.Add(new RotateTransform(0));
                    element.RenderTransform = transformGroup;

                    CenterAndScaleElement(element);

                    // 设置图片属性，避免被InkCanvas选择系统处理
                    element.IsHitTestVisible = true;
                    element.Focusable = false;

                    // 初始化InkCanvas选择设置
                    if (inkCanvas != null)
                    {
                        // 清除当前选择，避免显示控制点
                        inkCanvas.Select(new StrokeCollection());
                        // 同时通过图片的IsHitTestVisible和Focusable属性来避免InkCanvas选择系统的干扰
                        inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    }

                    inkCanvas.Children.Add(element);

                    // 绑定事件处理器
                    BindElementEvents(element);

                    timeMachine.CommitElementInsertHistory(element);

                    // 插入图片后切换到选择模式并刷新浮动栏高光显示
                    SetCurrentToolMode(InkCanvasEditingMode.Select);
                    UpdateCurrentToolMode("select");
                    HideSubPanels("select");
                    if (element is PdfEmbeddedView)
                        _pdfSidebarNextPositionUseHostTransform = true;
                    SyncPdfPageSidebarWithCanvas();
                }
            }
        }

        // 插入图片方法
        private async void InsertImage_MouseUp_New(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation("插入图片")) return;
            var dialog = new OpenFileDialog
            {
                Filter = MainWindowStrings.Main_FileInsert_OpenDialogFilter
            };
            if (dialog.ShowDialog() == true)
            {
                string filePath = dialog.FileName;
                string extension = System.IO.Path.GetExtension(filePath);
                FrameworkElement element = IsSupportedMediaExtension(extension)
                    ? await CreateMediaElementAsync(filePath)
                    : await CreateAndCompressImageAsync(filePath);
                if (element != null)
                {
                    string timestamp = IsCanvasMediaElement(element)
                        ? "media_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
                        : "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                    if (string.IsNullOrEmpty(element.Name)) element.Name = timestamp;

                    // 初始化TransformGroup
                    var transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform(1, 1));
                    transformGroup.Children.Add(new TranslateTransform(0, 0));
                    transformGroup.Children.Add(new RotateTransform(0));
                    element.RenderTransform = transformGroup;

                    CenterAndScaleElement(element);

                    // 设置图片属性，避免被InkCanvas选择系统处理
                    element.IsHitTestVisible = true;
                    element.Focusable = false;

                    // 初始化InkCanvas选择设置
                    if (inkCanvas != null)
                    {
                        // 清除当前选择，避免显示控制点
                        inkCanvas.Select(new StrokeCollection());
                        // 设置编辑模式为非选择模式
                        inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    }

                    inkCanvas.Children.Add(element);

                    // 绑定事件处理器
                    BindElementEvents(element);

                    timeMachine.CommitElementInsertHistory(element);

                    // 插入图片后切换到选择模式并刷新浮动栏高光显示
                    SetCurrentToolMode(InkCanvasEditingMode.Select);
                    UpdateCurrentToolMode("select");
                    HideSubPanels("select");
                    if (element is PdfEmbeddedView)
                        _pdfSidebarNextPositionUseHostTransform = true;
                    SyncPdfPageSidebarWithCanvas();
                }
            }
        }

        // Keep the old method for backward compatibility
        private async void InsertImage_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (TryBlockFrozenPageMutation(FloatingBarStrings.Board_InsertImage)) return;
            var dialog = new OpenFileDialog
            {
                Filter = MainWindowStrings.Main_FileInsert_OpenDialogFilter
            };
            if (dialog.ShowDialog() == true)
            {
                string filePath = dialog.FileName;
                string extension = System.IO.Path.GetExtension(filePath);
                FrameworkElement element = IsSupportedMediaExtension(extension)
                    ? await CreateMediaElementAsync(filePath)
                    : await CreateAndCompressImageAsync(filePath);
                if (element != null)
                {
                    string timestamp = IsCanvasMediaElement(element)
                        ? "media_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff")
                        : "img_" + DateTime.Now.ToString("yyyyMMdd_HH_mm_ss_fff");
                    if (string.IsNullOrEmpty(element.Name)) element.Name = timestamp;

                    // 初始化TransformGroup
                    var transformGroup = new TransformGroup();
                    transformGroup.Children.Add(new ScaleTransform(1, 1));
                    transformGroup.Children.Add(new TranslateTransform(0, 0));
                    transformGroup.Children.Add(new RotateTransform(0));
                    element.RenderTransform = transformGroup;

                    CenterAndScaleElement(element);

                    // 设置图片属性，避免被InkCanvas选择系统处理
                    element.IsHitTestVisible = true;
                    element.Focusable = false;

                    // 初始化InkCanvas选择设置
                    if (inkCanvas != null)
                    {
                        // 清除当前选择，避免显示控制点
                        inkCanvas.Select(new StrokeCollection());
                        // 设置编辑模式为非选择模式
                        inkCanvas.EditingMode = InkCanvasEditingMode.None;
                    }

                    inkCanvas.Children.Add(element);

                    // 绑定事件处理器
                    BindElementEvents(element);

                    timeMachine.CommitElementInsertHistory(element);

                    // 插入图片后切换到选择模式并刷新浮动栏高光显示
                    SetCurrentToolMode(InkCanvasEditingMode.Select);
                    UpdateCurrentToolMode("select");
                    HideSubPanels("select");
                    if (element is PdfEmbeddedView)
                        _pdfSidebarNextPositionUseHostTransform = true;
                    SyncPdfPageSidebarWithCanvas();
                }
            }
        }

        /// <summary>
        /// 强制禁用所有双指手势功能（当多指书写模式启用时）
        /// </summary>
        private void ForceDisableTwoFingerGestures()
        {
            // 强制关闭所有双指手势设置
            Settings.Gesture.IsEnableTwoFingerTranslate = false;
            Settings.Gesture.IsEnableTwoFingerZoom = false;
            Settings.Gesture.IsEnableTwoFingerRotation = false;

            // 更新UI开关状态
            if (ToggleSwitchEnableTwoFingerTranslate != null)
                ToggleSwitchEnableTwoFingerTranslate.IsOn = false;
            if (ToggleSwitchEnableTwoFingerZoom != null)
                ToggleSwitchEnableTwoFingerZoom.IsOn = false;
            if (ToggleSwitchEnableTwoFingerRotation != null)
                ToggleSwitchEnableTwoFingerRotation.IsOn = false;

            // 更新设置窗口中的开关状态
            if (BoardToggleSwitchEnableTwoFingerTranslate != null)
                BoardToggleSwitchEnableTwoFingerTranslate.IsOn = false;
            if (BoardToggleSwitchEnableTwoFingerZoom != null)
                BoardToggleSwitchEnableTwoFingerZoom.IsOn = false;
            if (BoardToggleSwitchEnableTwoFingerRotation != null)
                BoardToggleSwitchEnableTwoFingerRotation.IsOn = false;
        }

    }
}
