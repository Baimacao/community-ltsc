using Ink_Canvas.Helpers;
using iNKORE.UI.WPF.Modern;
using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        /// <summary>
        /// 浮动栏是否折叠的标志。
        /// </summary>
        public bool isFloatingBarFolded;

        /// <summary>
        /// 浮动栏正在改变隐藏模式的标志，用于防止重复操作。
        /// </summary>
        private bool isFloatingBarChangingHideMode;

        private int _sidePanelAnimVersion = 0;

        /// <summary>
        /// 侧边栏展开时距屏幕边缘的边距。
        ///
        /// 这个值必须按**外观类型**区分，因为两种外观的内部结构完全不同：
        ///
        ///   极简外观（UnFoldButtonImageType == 2）
        ///     PanelBorder 宽 24，带 Margin=15 的左内边距（左侧），
        ///     Grid 宽 80 → 抓手实际从 15 到 39。
        ///     要让抓手贴边，Margin 得设 -15 把内边距抵掉。
        ///
        ///   经典外观（其他值，默认）
        ///     ClassicViewbox 宽 32，HorizontalAlignment=Left，没有内边距。
        ///     Margin 设 0 时 viewbox 从 0 到 32，已经贴边。
        ///     再设 -15 反而会把它推出屏幕 15px。
        ///
        /// 之前我把它硬编码成 -15，结果经典外观下露出来的是 32+15=47 宽，
        /// 看起来"多弹出一部分"。现在按外观取值。
        /// </summary>
        private int GetUnfoldedSideMargin()
        {
            // 极简外观抵掉 15px 内边距；经典外观贴 0。
            return Settings.Appearance.UnFoldButtonImageType == 2 ? -15 : 0;
        }

        /// <summary>侧边栏完全折叠时的边距（藏到屏幕外）。</summary>
        private const int FoldedSideMargin = -50;

        /// <summary>
        /// 自动收起侧栏计时器
        /// </summary>
        private DispatcherTimer _autoCollapseTimer;

        /// <summary>
        /// 立即关闭白板模式，恢复到批注模式。
        /// </summary>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查是否正在显示或隐藏黑板，如果是则直接返回
        /// 2. 设置显示/隐藏黑板的标志为true
        /// 3. 立即隐藏所有子面板
        /// 4. 如果启用了自动切换多指手势，则关闭多指平移
        /// 5. 隐藏所有水印
        /// 6. 切换到批注模式
        /// 7. 设置退出按钮前景色为白色
        /// 8. 设置应用主题为深色
        /// 9. 200毫秒后重置显示/隐藏黑板的标志为false
        /// </remarks>
        private void CloseWhiteboardImmediately()
        {
            if (isDisplayingOrHidingBlackboard) return;
            isDisplayingOrHidingBlackboard = true;
            HideSubPanelsImmediately();
            WaterMarkTime.Visibility = Visibility.Collapsed;
            WaterMarkDate.Visibility = Visibility.Collapsed;
            BlackBoardWaterMark.Visibility = Visibility.Collapsed;
            ICCWaterMarkDark.Visibility = Visibility.Collapsed;
            ICCWaterMarkWhite.Visibility = Visibility.Collapsed;
            SwitchBackground(null, null);
            { /* Old UI removed */ }
            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Dark;
            new Thread(() =>
            {
                Thread.Sleep(200);
                Application.Current.Dispatcher.Invoke(() => { isDisplayingOrHidingBlackboard = false; });
            }).Start();
        }

        /// <summary>
        /// 处理折叠浮动栏的鼠标点击事件。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <param name="e">路由事件参数。</param>
        public async void FoldFloatingBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            await FoldFloatingBar(sender);
        }

        /// <summary>
        /// 折叠浮动栏，将其收纳到侧边栏。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <param name="isAutoFoldCommand">是否为自动折叠命令。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查是否应该拒绝操作（如点击了折叠图标但上次鼠标按下的对象不是折叠图标）
        /// 2. 设置折叠/展开标志
        /// 3. 检查浮动栏是否已经折叠或正在改变隐藏模式，如果是则直接返回
        /// 4. 处理墨迹重放相关的UI元素
        /// 5. 设置浮动栏状态标志，关闭白板模式（如果当前在白板模式）
        /// 6. 如果是用户手动折叠且画布上有较多墨迹，显示通知
        /// 7. 清空画布墨迹
        /// 8. 隐藏PPT导航面板和浮动栏拖动网格
        /// 9. 执行浮动栏和侧边栏的动画
        /// 10. 如果开启了彻底隐藏，则隐藏主窗口
        /// </remarks>
        private static System.Diagnostics.Stopwatch _foldSw;

        public async Task FoldFloatingBar(object sender, bool isAutoFoldCommand = false)
        {
            _foldSw = System.Diagnostics.Stopwatch.StartNew();
            // LTSC（2026-11-20）：这行原来在最前面，于是每次进方法都打一条。
            // 自动收纳定时器每 1 秒叫一次，折叠期间会连着进来 5 次，
            // 日志上看着像「折叠了 5 次」，实际只有第一次生效、后 4 次被下面的
            // alreadyFolded 挡掉。挪到判断之后，只记真正执行的折叠。

            // LTSC（2026-11-19 班测崩溃）：整个方法必须跑在 UI 线程上。
            //
            // 崩溃日志：
            //   [T28] [自动收纳] 全屏=True 需收纳=True ...
            //   [T28] [折叠] 开始
            //   [T28] [Warning] 检测到DynamicRenderer线程访问异常:
            //          调用线程无法访问此对象，因为另一个线程拥有该对象。
            //
            // 调用链：timerCheckAutoFold 是 System.Timers.Timer 的 Elapsed，
            // 跑在【线程池线程】上（日志里的 T28/T16 就是），它直接调
            // FoldFloatingBar_MouseUp → FoldFloatingBar。
            //
            // 而本方法原来的第一段（下面这几行）在第一个 await 之前，
            // 也就是【还在调用方的线程上】就执行了：
            //   · 读 isFloatingBarFolded / isFloatingBarChangingHideMode（UI 状态）
            //   · 读 IsInPPTPresentationMode（走 SetPowerPointMode 的 getter）
            //   · 读 ViewboxFloatingBar.Margin（UI 元素）
            //   · 调 isFloatingBarFolded 相关的一系列标志
            // 其中 ViewboxFloatingBar.Margin 是对 DependencyObject 的跨线程访问，
            // 直接抛 InvalidOperationException。
            //
            // async 方法在第一个 await 之前是同步跑的 —— 这是关键：
            // 加 async 不等于自动切线程。所以必须显式把整段搬进 Dispatcher。
            await Dispatcher.InvokeAsync(() =>
            {
                // FloatingBarIcons_MouseUp_New(sender);
                if (sender == null)
                    foldFloatingBarByUser = false;
                else
                    foldFloatingBarByUser = true;
                unfoldFloatingBarByUser = false;

                // IsInPPTPresentationMode 的 getter 会进 SetPowerPointMode 侧，
                // 而 ViewboxFloatingBar.Margin 是 DependencyObject —— 两者都必须在 UI 线程读。
                // 原来这两句在第一个 await 之前，等于跑在调用方线程（Timer 线程池线程）上，
                // 就是班测崩溃的那一行。
                if (!IsInPPTPresentationMode)
                {
                    pointDesktop = new Point(ViewboxFloatingBar.Margin.Left, ViewboxFloatingBar.Margin.Top);
                }
            });

            // 只读判断放在调度之后重新取一次，别用在别的线程上读到的旧值。
            bool alreadyFolded = false;
            bool changingHideMode = false;
            await Dispatcher.InvokeAsync(() =>
            {
                alreadyFolded = isFloatingBarFolded;
                changingHideMode = isFloatingBarChangingHideMode;
            });

            if (alreadyFolded) return;

            if (changingHideMode)
            {
                return;
            }

            // 走到这里才是真正要折叠。日志放在这，一次折叠只留一条。
            LogHelper.WriteLogToFile("[折叠] 开始（真正执行）", LogHelper.LogType.Event);

            await Dispatcher.InvokeAsync(() =>
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
            });

            // 折叠前先把当前的【正常位置】记下来，供下次展开时恢复。
            //
            // 为什么在这里：折叠会把浮窗移到屏幕外（y≈1110），
            // 这个隐藏位置会被 SaveFloatingBarPositionPoint 之类的地方
            // 存进 pointDesktop。等下次展开时，定位逻辑看到
            // pointDesktop 与目标位置差 >50，就把 pos 覆盖成那个隐藏位置，
            // 结果浮窗出现在"默认位置上面"——就是它折叠时的那个高度。
            // 现在在移动之前先把真实位置存好，覆盖掉脏数据。
            // 同上：这段原来在两个 await 之间，跑在调用方线程（可能是 Timer 线程）上，
            // 读 ViewboxFloatingBar.Margin 是跨线程访问 DependencyObject，
            // 读 IsInPPTPresentationMode 也会进 SetPowerPointMode 的 getter。
            await Dispatcher.InvokeAsync(() =>
            {
                if (!IsInPPTPresentationMode)
                {
                    pointDesktop = new Point(ViewboxFloatingBar.Margin.Left, ViewboxFloatingBar.Margin.Top);
                }
            });

            await Dispatcher.InvokeAsync(() =>
            {
                isFloatingBarChangingHideMode = true;
                isFloatingBarFolded = true;
                if (currentMode != 0) CloseWhiteboardImmediately();
                if (IsAnnotating)
                    if (foldFloatingBarByUser && inkCanvas.Strokes.Count > 2)
                        ShowNotification(Properties.MainWindowStrings.Main_AutoFold_ClearingAndFolding);
                lastBorderMouseDownObject = sender;
                CursorWithDelIcon_Click(sender, null);
            });
            await Task.Delay(300);

            await Dispatcher.InvokeAsync(() =>
            {
                LeftBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                RightBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                LeftSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                RightSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
                ViewboxFloatingBarMarginAnimation(-60);
                HideSubPanels("cursor");
                SidePannelMarginAnimation(GetUnfoldedSideMargin());
            });

            // 新增：如果开启了彻底隐藏，则隐藏主窗口
            if (Settings.Automation.ThoroughlyHideWhenFolded)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    this.Visibility = Visibility.Hidden;
                });
            }
        }

        /// <summary>
        /// 处理左侧展开按钮显示快捷面板的鼠标点击事件。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <param name="e">鼠标按钮事件参数。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查是否显示快捷面板
        /// 2. 如果显示快捷面板，则隐藏右侧快捷面板，显示左侧快捷面板并执行动画
        /// 3. 否则，调用展开浮动栏的方法
        /// </remarks>
        private async void LeftUnFoldButtonDisplayQuickPanel_MouseUp(object sender, RoutedEventArgs e)
        {
            if (Settings.Appearance.IsShowQuickPanel)
            {
                SidePannelMarginAnimation(FoldedSideMargin);
                HideRightQuickPanel();
                LeftUnFoldButtonQuickPanel.Visibility = Visibility.Visible;
                await Dispatcher.InvokeAsync(() =>
                {
                    // LTSC D-05：改走合成层，动画期间不动 Margin（否则每帧触发布局传递）
                    //
                    // 时长原来是 0.1 秒——太快了，视觉上等于「直接出现」，没有滑出感。
                    // 现在 0.28 秒 + QuinticEase(EaseOut)，前段快、尾段缓，滑出来看得出动。
                    var targetMargin = new Thickness(QuickPanelUnfoldedMargin, 0, 0, Settings.Appearance.QuickPanelBottomOffset);
                    if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                            LeftUnFoldButtonQuickPanel, targetMargin, 0.28, new QuinticEase { EasingMode = EasingMode.EaseOut }))
                    {
                        var marginAnimation = new ThicknessAnimation
                        {
                            Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(0.28)),
                            From = new Thickness(QuickPanelFoldedMargin, 0, 0, Settings.Appearance.QuickPanelBottomOffset),
                            To = targetMargin,
                            EasingFunction = new QuinticEase { EasingMode = EasingMode.EaseOut }
                        };
                        LeftUnFoldButtonQuickPanel.BeginAnimation(MarginProperty, marginAnimation);
                    }
                });
                await Task.Delay(280);

                await Dispatcher.InvokeAsync(() =>
                {
                    AnimationsHelper.LandMarginOnCompositionLayer(LeftUnFoldButtonQuickPanel,
                        new Thickness(QuickPanelUnfoldedMargin, 0, 0, Settings.Appearance.QuickPanelBottomOffset));
                });

                StartAutoCollapseQuickPanelTimer();
            }
            else
            {
                var mouseEventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
                UnFoldFloatingBar_MouseUp(sender, mouseEventArgs);
            }
        }

        /// <summary>
        /// 处理右侧展开按钮显示快捷面板的鼠标点击事件。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <param name="e">路由事件参数。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查是否显示快捷面板
        /// 2. 如果显示快捷面板，则隐藏左侧快捷面板，显示右侧快捷面板并执行动画
        /// 3. 否则，调用展开浮动栏的方法
        /// </remarks>
        private async void RightUnFoldButtonDisplayQuickPanel_MouseUp(object sender, RoutedEventArgs e)
        {
            if (Settings.Appearance.IsShowQuickPanel)
            {
                SidePannelMarginAnimation(FoldedSideMargin);
                HideLeftQuickPanel();
                RightUnFoldButtonQuickPanel.Visibility = Visibility.Visible;
                await Dispatcher.InvokeAsync(() =>
                {
                    // LTSC D-05：改走合成层，动画期间不动 Margin（否则每帧触发布局传递）
                    var targetMargin = new Thickness(0, 0, QuickPanelUnfoldedMargin, Settings.Appearance.QuickPanelBottomOffset);
                    if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                            RightUnFoldButtonQuickPanel, targetMargin, 0.22, new CubicEase { EasingMode = EasingMode.EaseIn }))
                    {
                        var marginAnimation = new ThicknessAnimation
                        {
                            Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(0.22)),
                            From = new Thickness(0, 0, QuickPanelFoldedMargin, Settings.Appearance.QuickPanelBottomOffset),
                            To = targetMargin
                        };
                        marginAnimation.EasingFunction = new CubicEase();
                        RightUnFoldButtonQuickPanel.BeginAnimation(MarginProperty, marginAnimation);
                    }
                });
                await Task.Delay(220);

                await Dispatcher.InvokeAsync(() =>
                {
                    AnimationsHelper.LandMarginOnCompositionLayer(RightUnFoldButtonQuickPanel,
                        new Thickness(0, 0, QuickPanelUnfoldedMargin, Settings.Appearance.QuickPanelBottomOffset));
                });

                StartAutoCollapseQuickPanelTimer();
            }
            else
            {
                var mouseEventArgs = new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left);
                UnFoldFloatingBar_MouseUp(sender, mouseEventArgs);
            }
        }

        /// <summary>
        /// 隐藏左侧快捷面板。
        /// </summary>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查左侧快捷面板是否可见，如果不可见则直接返回
        /// 2. 执行左侧快捷面板的隐藏动画
        /// 3. 等待动画完成后，设置左侧快捷面板的边距并将其折叠
        /// </remarks>
        private async void HideLeftQuickPanel()
        {
            if (LeftUnFoldButtonQuickPanel.Visibility == Visibility.Visible)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    // LTSC D-05：改走合成层，动画期间不动 Margin（否则每帧触发布局传递）
                    var targetMargin = new Thickness(QuickPanelFoldedMargin, 0, 0, Settings.Appearance.QuickPanelBottomOffset);
                    if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                            LeftUnFoldButtonQuickPanel, targetMargin, 0.22, new CubicEase { EasingMode = EasingMode.EaseIn }))
                    {
                        var marginAnimation = new ThicknessAnimation
                        {
                            Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(0.22)),
                            From = new Thickness(QuickPanelUnfoldedMargin, 0, 0, Settings.Appearance.QuickPanelBottomOffset),
                            To = targetMargin
                        };
                        marginAnimation.EasingFunction = new CubicEase();
                        LeftUnFoldButtonQuickPanel.BeginAnimation(MarginProperty, marginAnimation);
                    }
                });
                await Task.Delay(220);

                await Dispatcher.InvokeAsync(() =>
                {
                    AnimationsHelper.LandMarginOnCompositionLayer(LeftUnFoldButtonQuickPanel,
                        new Thickness(QuickPanelFoldedMargin, 0, 0, Settings.Appearance.QuickPanelBottomOffset));
                    LeftUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
                });
            }
        }

        /// <summary>
        /// 隐藏右侧快捷面板。
        /// </summary>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查右侧快捷面板是否可见，如果不可见则直接返回
        /// 2. 执行右侧快捷面板的隐藏动画
        /// 3. 等待动画完成后，设置右侧快捷面板的边距并将其折叠
        /// </remarks>
        private async void HideRightQuickPanel()
        {
            if (RightUnFoldButtonQuickPanel.Visibility == Visibility.Visible)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    // LTSC D-05：改走合成层，动画期间不动 Margin（否则每帧触发布局传递）
                    var targetMargin = new Thickness(0, 0, QuickPanelFoldedMargin, Settings.Appearance.QuickPanelBottomOffset);
                    if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                            RightUnFoldButtonQuickPanel, targetMargin, 0.22, new CubicEase { EasingMode = EasingMode.EaseIn }))
                    {
                        var marginAnimation = new ThicknessAnimation
                        {
                            Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(0.22)),
                            From = new Thickness(0, 0, QuickPanelUnfoldedMargin, Settings.Appearance.QuickPanelBottomOffset),
                            To = targetMargin
                        };
                        marginAnimation.EasingFunction = new CubicEase();
                        RightUnFoldButtonQuickPanel.BeginAnimation(MarginProperty, marginAnimation);
                    }
                });
                await Task.Delay(220);

                await Dispatcher.InvokeAsync(() =>
                {
                    AnimationsHelper.LandMarginOnCompositionLayer(RightUnFoldButtonQuickPanel,
                        new Thickness(0, 0, QuickPanelFoldedMargin, Settings.Appearance.QuickPanelBottomOffset));
                    RightUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
                });
            }
        }

        /// <summary>
        /// 处理隐藏快捷面板的鼠标点击事件。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <param name="e">鼠标按钮事件参数。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 隐藏左侧快捷面板
        /// 2. 隐藏右侧快捷面板
        /// </remarks>
        private void HideQuickPanel_MouseUp(object sender, MouseButtonEventArgs e)
        {
            StopAutoCollapseQuickPanelTimer();
            HideLeftQuickPanel();
            HideRightQuickPanel();
            SidePannelMarginAnimation(GetUnfoldedSideMargin());
        }

        /// <summary>
        /// 处理展开浮动栏的鼠标点击事件。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <param name="e">路由事件参数。</param>
        public async void UnFoldFloatingBar_MouseUp(object sender, MouseButtonEventArgs e)
        {
            await UnFoldFloatingBar(sender);
        }

        /// <summary>
        /// 展开浮动栏，将其从侧边栏恢复到正常状态。
        /// </summary>
        /// <param name="sender">事件发送者。</param>
        /// <returns>表示异步操作的任务。</returns>
        /// <remarks>
        /// 操作包括：
        /// 1. 如果之前彻底隐藏了主窗口，先恢复显示
        /// 2. 隐藏左右侧快捷面板
        /// 3. 设置展开/折叠标志
        /// 4. 检查浮动栏是否正在改变隐藏模式，如果是则直接返回
        /// 5. 设置浮动栏状态标志，标记为未折叠
        /// 6. 根据设置决定是否自动切换至批注模式
        /// 7. 根据PPT放映模式和设置显示或隐藏翻页按钮
        /// 8. 在屏幕模式下显示浮动栏并执行动画
        /// 9. 执行侧边栏动画
        /// 10. 等待UI完全更新后，重新设置当前选中模式的按钮高亮状态
        /// </remarks>
        public async Task UnFoldFloatingBar(object sender)
        {
            // 新增：如果之前彻底隐藏了，先恢复显示
            if (this.Visibility != Visibility.Visible)
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    this.Visibility = Visibility.Visible;
                });
            }

            await Dispatcher.InvokeAsync(() =>
            {
                LeftUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
                RightUnFoldButtonQuickPanel.Visibility = Visibility.Collapsed;
            });
            if (sender == null || ArePPTControlsVisible)
                unfoldFloatingBarByUser = false;
            else
                unfoldFloatingBarByUser = true;
            foldFloatingBarByUser = false;

            if (isFloatingBarChangingHideMode)
            {
                return;
            }


            await Dispatcher.InvokeAsync(() =>
            {
                isFloatingBarChangingHideMode = true;
                isFloatingBarFolded = false;
            });

            await Task.Delay(0);

            await Dispatcher.InvokeAsync(() =>
            {
                // 根据设置决定是否自动切换至批注模式
                if (Settings.Automation.IsAutoEnterAnnotationModeWhenExitFoldMode && currentMode == 0)
                {
                    // 切换至批注模式
                    PenIcon_Click(null, null);
                }

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
                    // 如果条件不满足，确保隐藏翻页按钮
                    LeftBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    RightBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    LeftSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    RightSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                }

                // 新只在屏幕模式下显示浮动栏
                if (currentMode == 0)
                {
                    // 强制更新布局以确保ActualWidth正确
                    ViewboxFloatingBar.UpdateLayout();

                    // 等待一小段时间让布局完全更新
                    Task.Delay(50);

                    // 再次强制更新布局
                    ViewboxFloatingBar.UpdateLayout();

                    // 强制重新测量和排列
                    ViewboxFloatingBar.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                    ViewboxFloatingBar.Arrange(new Rect(ViewboxFloatingBar.DesiredSize));

                    if (IsInPPTPresentationMode)
                    {
                        ViewboxFloatingBarMarginAnimation(60);
                    }
                    else
                    {
                        PureViewboxFloatingBarMarginAnimationInDesktopMode();
                    }
                }
                SidePannelMarginAnimation(FoldedSideMargin, !unfoldFloatingBarByUser);
            });

            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // 等待UI完全更新
                    await Task.Delay(220);

                    // 获取当前选中的模式并重新设置高光位置
                    string selectedToolMode = GetCurrentSelectedMode();
                    if (!string.IsNullOrEmpty(selectedToolMode))
                    {
                        SetFloatingBarHighlightPosition(selectedToolMode);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"浮动栏展开后重新设置按钮高亮状态失败: {ex.Message}", LogHelper.LogType.Error);
                }
            });

        }

        /// <summary>
        /// 执行侧边栏边距动画，用于折叠或展开侧边栏。
        /// </summary>
        /// <param name="MarginFromEdge">侧边栏距边缘的边距值。目前是 FoldedSideMargin（完全折叠）或 UnfoldedSideMargin（展开贴边）</param>
        /// <param name="isNoAnimation">是否禁用动画效果。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 如果边距值为 UnfoldedSideMargin（展开），则显示左侧边栏
        /// 2. 创建并执行左侧边栏的边距动画
        /// 3. 创建并执行右侧边栏的边距动画
        /// 4. 等待600毫秒让动画完成
        /// 5. 直接设置侧边栏的最终边距值
        /// 6. 如果边距值为-50（完全折叠），则隐藏左侧边栏
        /// 7. 重置浮动栏正在改变隐藏模式的标志为false
        /// </remarks>
        private async void SidePannelMarginAnimation(int MarginFromEdge, bool isNoAnimation = false)
        {
            int thisVersion = ++_sidePanelAnimVersion;

            // LTSC C-4：isFloatingBarChangingHideMode 在这里、Fold/UnFold 里被置 true，
            // 但整条路径上只有一个复位点（本方法末尾）。两处会跳过它：
            //   ① :570 的 if (_sidePanelAnimVersion != thisVersion) return;  —— 动画被更新版本顶掉
            //   ② 两处 await Dispatcher.InvokeAsync 抛异常 → async void 把异常甩到
            //      DispatcherUnhandledException，末尾那句永远不执行
            // 结果是这个标志永久 true，MW_AutoFold:108 / :416 和 MW_Timer:1218 全部短路，
            // 浮动栏再也折不起来——只能重启软件。
            // 现在把复位放进 finally，两个出口都能落回来。
            try
            {
            await Dispatcher.InvokeAsync(() =>
            {
                if (MarginFromEdge == GetUnfoldedSideMargin())
                {
                    if (isFloatingBarFolded)
                        LeftSidePanel.Visibility = Visibility.Visible;
                    else
                        LeftSidePanel.Visibility = Visibility.Collapsed;
                }

                // LTSC D-05：左右侧栏改走合成层，动画期间不动 Margin（否则每帧触发布局传递）
                var leftTarget = new Thickness(MarginFromEdge, 0, 0, Settings.Appearance.QuickPanelBottomOffset);
                var rightTarget = new Thickness(0, 0, MarginFromEdge, Settings.Appearance.QuickPanelBottomOffset);
                // LTSC（2026-11-17）：0.175 秒原来和浮动栏动画对不上。
                //
                // 用户反馈：「隐藏功能与侧边快捷面板的动画不连贯」。
                //
                // 实测两条时长：
                //   浮动栏折叠（弹簧）  约 0.35 秒
                //   侧边面板（CubicEase）0.175 秒
                //
                // 而且这两个是**同时启动**的（FoldFloatingBar 里调
                // ViewboxFloatingBarMarginAnimation 是 async void，没 await，
                // 下一句就调 SidePannelMarginAnimation）。
                //
                // 结果：侧边栏飞快滑走收完，浮动栏还在慢慢收——
                // 两条动画各走各的节奏，看起来就是「不连贯」。
                //
                // 改成和浮动栏同量级。不用完全相等：侧边栏是简单的平移，
                // 比弹簧收尾快一点更自然；但差一倍太多。
                double sidePanelSeconds = isNoAnimation ? 0 : 0.30;

                if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                        LeftSidePanel, leftTarget, sidePanelSeconds, new CubicEase()))
                {
                    var leftAnimation = new ThicknessAnimation
                    {
                        Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(sidePanelSeconds)),
                        From = LeftSidePanel.Margin,
                        To = leftTarget
                    };
                    leftAnimation.EasingFunction = new CubicEase();
                    LeftSidePanel.BeginAnimation(MarginProperty, leftAnimation);
                }

                if (!AnimationsHelper.TryAnimateMarginOnCompositionLayer(
                        RightSidePanel, rightTarget, sidePanelSeconds, new CubicEase()))
                {
                    var rightAnimation = new ThicknessAnimation
                    {
                        Duration = AnimationsHelper.EffectiveDuration(TimeSpan.FromSeconds(sidePanelSeconds)),
                        From = RightSidePanel.Margin,
                        To = rightTarget
                    };
                    rightAnimation.EasingFunction = new CubicEase();
                    RightSidePanel.BeginAnimation(MarginProperty, rightAnimation);
                }
            });

            await Task.Delay(600);

            if (_sidePanelAnimVersion != thisVersion) return;

            await Dispatcher.InvokeAsync(() =>
            {
                AnimationsHelper.LandMarginOnCompositionLayer(LeftSidePanel,
                    new Thickness(MarginFromEdge, 0, 0, Settings.Appearance.QuickPanelBottomOffset));
                AnimationsHelper.LandMarginOnCompositionLayer(RightSidePanel,
                    new Thickness(0, 0, MarginFromEdge, Settings.Appearance.QuickPanelBottomOffset));

                if (MarginFromEdge == FoldedSideMargin) LeftSidePanel.Visibility = Visibility.Collapsed;
            });
            }
            finally
            {
                isFloatingBarChangingHideMode = false;
            }
        }

        /// <summary>
        /// 启动自动收起侧栏计时器
        /// </summary>
        internal void StartAutoCollapseQuickPanelTimer()
        {
            if (!Settings.Appearance.IsAutoCollapseQuickPanel) return;

            StopAutoCollapseQuickPanelTimer();

            _autoCollapseTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(Settings.Appearance.AutoCollapseQuickPanelDelay)
            };
            _autoCollapseTimer.Tick += AutoCollapseTimer_Tick;
            _autoCollapseTimer.Start();
        }

        /// <summary>
        /// 停止自动收起侧栏计时器
        /// </summary>
        internal void StopAutoCollapseQuickPanelTimer()
        {
            if (_autoCollapseTimer != null)
            {
                _autoCollapseTimer.Stop();
                _autoCollapseTimer.Tick -= AutoCollapseTimer_Tick;
                _autoCollapseTimer = null;
            }
        }

        /// <summary>
        /// 自动收起侧栏计时器到期时执行
        /// </summary>
        private void AutoCollapseTimer_Tick(object sender, EventArgs e)
        {
            StopAutoCollapseQuickPanelTimer();
            HideQuickPanelAndResetMargin();
        }

        /// <summary>
        /// 更新自动收起侧栏计时器（根据当前设置）
        /// </summary>
        internal void UpdateAutoCollapseQuickPanelTimer()
        {
            if (!Settings.Appearance.IsAutoCollapseQuickPanel)
            {
                StopAutoCollapseQuickPanelTimer();
            }
        }

        /// <summary>
        /// 隐藏快捷面板并重置侧边栏边距
        /// </summary>
        private void HideQuickPanelAndResetMargin()
        {
            if (LeftUnFoldButtonQuickPanel.Visibility == Visibility.Visible ||
                RightUnFoldButtonQuickPanel.Visibility == Visibility.Visible)
            {
                HideLeftQuickPanel();
                HideRightQuickPanel();
                SidePannelMarginAnimation(GetUnfoldedSideMargin());
            }
        }

        /// <summary>
        /// 处理主窗口上的鼠标按下事件，用于检测点击其他区域时自动收起侧栏
        /// </summary>
        internal void HandleAutoCollapseOnMouseDown(MouseButtonEventArgs e)
        {
            if (!Settings.Appearance.IsAutoCollapseQuickPanel) return;
            if (!isFloatingBarFolded) return;

            bool isQuickPanelVisible = LeftUnFoldButtonQuickPanel.Visibility == Visibility.Visible ||
                                       RightUnFoldButtonQuickPanel.Visibility == Visibility.Visible;

            if (!isQuickPanelVisible) return;

            // 检查点击目标是否在快捷面板或侧边栏按钮内
            var hitTarget = e.OriginalSource as DependencyObject;
            if (hitTarget != null)
            {
                // 如果点击在快捷面板或侧边栏按钮内，不收起
                if (IsDescendantOf(hitTarget, LeftUnFoldButtonQuickPanel) ||
                    IsDescendantOf(hitTarget, RightUnFoldButtonQuickPanel) ||
                    IsDescendantOf(hitTarget, LeftSidePanel) ||
                    IsDescendantOf(hitTarget, RightSidePanel))
                {
                    // 重置计时器
                    StartAutoCollapseQuickPanelTimer();
                    return;
                }
            }

            // 点击了其他区域，立即收起
            StopAutoCollapseQuickPanelTimer();
            HideQuickPanelAndResetMargin();
        }

        /// <summary>
        /// 检查一个元素是否是另一个元素的子元素
        /// </summary>
        private static bool IsDescendantOf(DependencyObject child, FrameworkElement parent)
        {
            if (parent == null) return false;
            var current = child;
            while (current != null)
            {
                if (current == parent) return true;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        /// <summary>
        /// 主窗口鼠标按下预览事件，用于检测点击其他区域时自动收起侧栏
        /// </summary>
        private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            // LTSC（2026-11-20 第十一轮）：这是窗口级最外层入口。
            // 前几轮在 inkCanvas 的两个入口加日志，一条都不出 ——
            // 事件要么没进 ICC，要么在半路被吃了。
            // 这里无条件记，先确认鼠标到底有没有进进程。
            try
            {
                // LTSC（2026-11-20 十六轮）：源=Grid 说明命中的不是 inkCanvas。
                // 直接在这条路径上问 WPF「这个点命中谁」，比读代码可靠。
                var pt = e.GetPosition(Main_Grid);
                var hit = Main_Grid.InputHitTest(pt) as System.Windows.DependencyObject;
                string hitChain = "";
                var node = hit;
                for (int i = 0; i < 5 && node != null; i++)
                {
                    var fe = node as System.Windows.FrameworkElement;
                    hitChain += (fe?.Name ?? node.GetType().Name) + " < ";
                    node = System.Windows.Media.VisualTreeHelper.GetParent(node);
                }

                // 同时记坐标和画布范围，才能判断「是不是划在画布上」。
                // 前面几轮一直假设用户划在画布中央，但日志从没验证过这一点。
                var inInk = pt.X >= 0 && pt.Y >= 0
                            && pt.X <= inkCanvas.ActualWidth && pt.Y <= inkCanvas.ActualHeight;
                LogHelper.WriteLogToFile(
                    $"[窗口输入] 点=({pt.X:F0},{pt.Y:F0}) 画布={inkCanvas.ActualWidth:F0}x{inkCanvas.ActualHeight:F0} " +
                    $"在画布内={inInk} EditingMode={inkCanvas.EditingMode} " +
                    $"currentToolMode={_currentToolMode} 命中={inkCanvas.IsHitTestVisible} " +
                    $"源={e.OriginalSource?.GetType().Name} || 命中链={hitChain}",
                    LogHelper.LogType.Event);
            }
            catch { }

            HandleAutoCollapseOnMouseDown(e);
        }

        private bool IsFloatingBarUiAbsentFromScreens()
        {
            try
            {
                if (ViewboxFloatingBar == null) return true;
                if (Settings?.Automation?.ThoroughlyHideWhenFolded == true &&
                    (!IsVisible || Visibility != Visibility.Visible))
                    return true;
                if (ViewboxFloatingBar.Visibility != Visibility.Visible)
                    return true;
                return IsOutsideOfScreenHelper.IsOutsideOfScreen(ViewboxFloatingBar);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"启动收纳校验：检测浮动栏可见性时出错: {ex.Message}", LogHelper.LogType.Warning);
                return true;
            }
        }

        private async Task WaitUntilFloatingBarHideModeIdleAsync(TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                bool busy = await Dispatcher.InvokeAsync(() => isFloatingBarChangingHideMode);
                if (!busy) return;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }


        private async Task WaitForStartupFoldSettledAsync(TimeSpan timeout)
        {
            var start = DateTime.UtcNow;
            while (DateTime.UtcNow - start < timeout)
            {
                var settled = await Dispatcher.InvokeAsync(() => isFloatingBarFolded && !isFloatingBarChangingHideMode);
                if (settled) return;
                await Task.Delay(50).ConfigureAwait(false);
            }
        }


        private async Task VerifyStartupFoldAbsenceAfterDelayAsync()
        {
            try
            {
                if (!Settings.Startup.IsFoldAtStartup || App.StartWithBoardMode || App.StartWithShowMode)
                    return;

                // 第一次校验：等待初始收纳稳定后，确认折叠状态是否真正落到不可见
                await RunStartupFoldVerificationPassAsync();

                // 第二次校验：15s 后再跑一遍，覆盖启动阶段后续可能出现的浮动栏异常重排/泄漏
                await Task.Delay(15000).ConfigureAwait(false);
                await RunStartupFoldVerificationPassAsync();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"启动收纳校验失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 执行单次启动收纳校验：等待浮动栏折叠稳定 3s 后检查是否真的不可见，
        /// 若仍可见则展开并重新收纳一次。
        /// </summary>
        private async Task RunStartupFoldVerificationPassAsync()
        {
            await WaitForStartupFoldSettledAsync(TimeSpan.FromSeconds(30)).ConfigureAwait(false);
            await Task.Delay(3000).ConfigureAwait(false);

            bool needRetry = await Dispatcher.InvokeAsync(() =>
            {
                if (!isFloatingBarFolded) return false;
                if (currentMode != 0) return false;
                return !IsFloatingBarUiAbsentFromScreens();
            });

            if (!needRetry) return;

            LogHelper.WriteLogToFile("启动收纳校验：检测到浮动栏仍在屏幕上，将展开后等待 0.2s 再次收纳", LogHelper.LogType.Event);

            await UnFoldFloatingBar(null);
            await WaitUntilFloatingBarHideModeIdleAsync(TimeSpan.FromSeconds(15)).ConfigureAwait(false);
            await Task.Delay(200).ConfigureAwait(false);
            await FoldFloatingBar(new object()).ConfigureAwait(false);
        }

        private void ScheduleStartupFoldAbsenceVerification()
        {
            _ = VerifyStartupFoldAbsenceAfterDelayAsync();
        }

    }
}
