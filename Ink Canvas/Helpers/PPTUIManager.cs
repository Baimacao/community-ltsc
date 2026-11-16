using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// PPT UI管理器 - 统一管理PPT相关的UI更新和样式设置
    /// </summary>
    public class PPTUIManager
    {
        #region Properties
        public bool ShowPPTButton { get; set; } = true;
        public int PPTButtonsDisplayOption { get; set; } = 2222;
        public int PPTSButtonsOption { get; set; } = 221;
        public int PPTBButtonsOption { get; set; } = 121;
        public int PPTLSButtonPosition { get; set; } = 0;
        public int PPTRSButtonPosition { get; set; } = 0;
        public int PPTLBButtonPosition { get; set; } = 0;
        public int PPTRBButtonPosition { get; set; } = 0;
        public bool EnablePPTButtonPageClickable { get; set; } = true;

        // ── LTSC P1c 外观增强（2026-11-11）────────────────────────────
        public double PageNumberFontSize { get; set; } = 17;
        public double PageTotalFontSize { get; set; } = 10;
        public bool NavBarAutoHide { get; set; } = false;
        public double NavBarAutoHideDelaySeconds { get; set; } = 3.0;
        public double NavBarAutoHideOpacity { get; set; } = 0.08;
        public bool ButtonPressFeedback { get; set; } = true;
        public double ButtonPressFeedbackOpacity { get; set; } = 0.35;

        /// <summary>
        /// 外观设置变化通知。设置页改完立刻触发，让主窗口重新读配置并下发到四条导航栏。
        /// 没有这条的话，改完要等下一次进入放映才看得到效果——用户会以为没生效。
        /// </summary>
        public static event Action AppearanceChanged;

        /// <summary>设置页调用：声明外观已变，请主窗口立刻刷新。</summary>
        public static void NotifyAppearanceChanged()
        {
            try { AppearanceChanged?.Invoke(); }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"通知翻页栏外观变化失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 把外观设置下发到四条导航栏。设置页改动后调用，放映开始时也调一次。
        /// </summary>
        public void ApplyAppearanceToAllBars()
        {
            // LTSC P5：这条路径有两个来源——
            //   ① 主窗口初始化 / 进入放映（本来就在 UI 线程）
            //   ② 设置页改完外观后通过静态事件 AppearanceChanged 回调（**设置窗口是独立线程**）
            // ② 会直接碰 WPF 元素，抛「调用线程无法访问此对象，因为另一个线程拥有该对象」。
            // 这里统一转回主窗口 UI 线程执行，两条来源都不用各自小心。
            if (!_dispatcher.CheckAccess())
            {
                _dispatcher.InvokeAsync(ApplyAppearanceToAllBars);
                return;
            }

            var bars = new[]
            {
                _mainWindow.LeftBottomPanelForPPTNavigation,
                _mainWindow.RightBottomPanelForPPTNavigation,
                _mainWindow.LeftSidePanelForPPTNavigation,
                _mainWindow.RightSidePanelForPPTNavigation,
            };
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                try
                {
                    bar.SetPageNumberFontSize(PageNumberFontSize, PageTotalFontSize);
                    bar.SetPressFeedback(ButtonPressFeedback, ButtonPressFeedbackOpacity);
                    bar.SetAutoHide(NavBarAutoHide, NavBarAutoHideDelaySeconds, NavBarAutoHideOpacity);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"下发翻页栏外观设置失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }
        }
        public bool EnablePPTButtonLongPressPageTurn { get; set; } = true;
        public double PPTLSButtonOpacity { get; set; } = 0.5;
        public double PPTRSButtonOpacity { get; set; } = 0.5;
        public double PPTLBButtonOpacity { get; set; } = 0.5;
        public double PPTRBButtonOpacity { get; set; } = 0.5;
        public double PPTNavBarScale { get; set; } = 1.0;
        public double PPTLSButtonScale { get; set; } = 1.0;
        public double PPTRSButtonScale { get; set; } = 1.0;
        public double PPTLBButtonScale { get; set; } = 1.0;
        public double PPTRBButtonScale { get; set; } = 1.0;
        #endregion

        #region Private Fields
        private readonly MainWindow _mainWindow;
        private readonly Dispatcher _dispatcher;
        #endregion

        #region Constructor
        public PPTUIManager(MainWindow mainWindow)
        {
            _mainWindow = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
            _dispatcher = _mainWindow.Dispatcher;
        }
        #endregion

        #region Public Methods
        /// <summary>
        /// 更新PPT连接状态UI
        /// </summary>
        public void UpdateConnectionStatus(bool isConnected)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (isConnected)
                    {
                        _mainWindow.ArePPTControlsVisible = true;
                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Visible;
                    }
                    else
                    {
                        _mainWindow.ArePPTControlsVisible = false;
                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                        _mainWindow.IsInPPTPresentationMode = false;
                        _mainWindow.UpdateToolbarComponentVisibility();
                        HideAllNavigationPanels();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新PPT连接状态UI失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 更新幻灯片放映状态UI
        /// </summary>
        public void UpdateSlideShowStatus(bool isInSlideShow, int currentSlide = 0, int totalSlides = 0)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (isInSlideShow)
                    {
                        bool wasInSlideShow = _mainWindow.IsInPPTPresentationMode;

                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Collapsed;
                        _mainWindow.IsInPPTPresentationMode = true;
                        _mainWindow.UpdateToolbarComponentVisibility();

                        // 同步页码到所有翻页条 + 兼容旧绑定的隐藏 placeholder
                        SetPageNumberOnAllBars(currentSlide, totalSlides);

                        UpdateNavigationPanelsVisibility();
                        UpdateNavigationButtonStyles();
                        _mainWindow.UpdatePPTQuickPanelVisibility();
                        if (!wasInSlideShow)
                        {
                            _mainWindow.ShowPPTModePromptNotification();
                        }
                        {
                            // LTSC D-17：进放映【无条件】铺满整屏。
                            //
                            // 原来这段被 IsEnableAvoidFullScreenHelper 闸着，那个开关默认关，
                            // 所以进放映时窗口根本不移动，一直停在启动时的 1050。
                            // 而放映时任务栏被隐藏、屏幕实际是 1080，于是底部露出 30px 空带，
                            // 翻页组件悬在那条空带上方 —— 就是用户看到的那条透明条。
                            //
                            // "避免全屏辅助" 和 "放映时铺满屏幕" 是两件事，不该共用一个开关。
                            AvoidFullScreenHelper.SetBoardMode(true);
                            _dispatcher.BeginInvoke(new Action(() =>
                            {
                                var bounds = System.Windows.Forms.Screen.PrimaryScreen.Bounds;
                                //
                                // 上一轮我误用了 WorkingArea(1050)，结果放映时任务栏被隐藏、
                                // ICC 只铺到 1050，底下露出 30px 空带，翻页组件离屏幕底边悬空。
                                // 放映是独占全屏场景，任务栏不在，就必须铺满 1080。
                                // 非放映场景（桌面/白板）才用 WorkingArea，那条另算。
                                MainWindow.MoveWindow(new WindowInteropHelper(_mainWindow).Handle,
                                    bounds.X, bounds.Y, bounds.Width, bounds.Height, true);

                                // MoveWindow 触发的 WM_WINDOWPOSCHANGING + 重绘会打断面板的 ShowWithFadeIn 动画，
                                // 在窗口尺寸最终确定后重新评估一次翻页面板的可见性。
                                UpdateNavigationPanelsVisibility();
                                UpdateNavigationButtonStyles();
                            }), DispatcherPriority.ApplicationIdle);

                            _mainWindow.isFullScreenApplied = true; // 标记已应用全屏处理
                            // LTSC D-14：进放映后重新评估悬浮窗空闲淡出。
                            // 定时器只在条件满足时才启动，条件从「不满足→满足」这一刻
                            // 必须有人叫一声，否则表永远不走（这是第一版的 bug）。
                            _mainWindow.ReArmFloatingBarIdleFade();
                        }
                    }
                    else
                    {
                        // Old UI removed:                         _mainWindow.BtnPPTSlideShow.Visibility = Visibility.Visible;
                        _mainWindow.IsInPPTPresentationMode = false;
                        _mainWindow.UpdateToolbarComponentVisibility();
                        HideAllNavigationPanels();
                        _mainWindow.UpdatePPTQuickPanelVisibility();
                        if (MainWindow.Settings.Advanced.IsEnableAvoidFullScreenHelper)
                        {
                            // 恢复为非画板模式，重新启用全屏限制
                            AvoidFullScreenHelper.SetBoardMode(false);

                            _dispatcher.BeginInvoke(new Action(() =>
                            {
                                // 退出PPT放映模式，恢复到工作区域大小
                                var workingArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                                MainWindow.MoveWindow(new WindowInteropHelper(_mainWindow).Handle,
                                    workingArea.X, workingArea.Y,
                                    workingArea.Width, workingArea.Height, true);
                            }), DispatcherPriority.ApplicationIdle);

                            _mainWindow.isFullScreenApplied = false; // 标记全屏处理已还原
                            // LTSC D-14：退出放映后条件不再满足，恢复清晰并停表
                            _mainWindow.ReArmFloatingBarIdleFade();
                        }
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新幻灯片放映状态UI失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 更新当前页码显示
        /// </summary>
        public void UpdateCurrentSlideNumber(int currentSlide, int totalSlides)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    SetPageNumberOnAllBars(currentSlide, totalSlides);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新页码显示失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        private void SetPageNumberOnAllBars(int currentSlide, int totalSlides)
        {
            var bars = new[]
            {
                _mainWindow.LeftBottomPanelForPPTNavigation,
                _mainWindow.RightBottomPanelForPPTNavigation,
                _mainWindow.LeftSidePanelForPPTNavigation,
                _mainWindow.RightSidePanelForPPTNavigation,
            };
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                bar.CurrentSlide = currentSlide;
                bar.TotalSlides = totalSlides;
            }
            // 兼容旧绑定（其它界面通过 ElementName 引用 PPTBtnPageNow / PPTBtnPageTotal）
            if (currentSlide > 0 && totalSlides > 0)
            {
                _mainWindow.PPTBtnPageNow.Text = currentSlide.ToString();
                _mainWindow.PPTBtnPageTotal.Text = $"/ {totalSlides}";
            }
            else
            {
                _mainWindow.PPTBtnPageNow.Text = "?";
                _mainWindow.PPTBtnPageTotal.Text = "/ ?";
            }
        }

        /// <summary>
        /// 处理PPT放映状态变化
        /// </summary>
        public void OnSlideShowStateChanged(bool isInSlideShow)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    if (!isInSlideShow)
                    {
                        // 如果不在放映模式，隐藏所有导航面板
                        HideAllNavigationPanels();
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"处理PPT放映状态变化失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 更新导航面板显示状态
        /// </summary>
        public void UpdateNavigationPanelsVisibility()
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // 检查是否应该显示PPT按钮
                    // 不仅要检查按钮设置，还要确保确实在PPT放映模式下且页数有效
                    bool isInSlideShow = _mainWindow.PPTManager?.IsInSlideShow == true;
                    int slidesCount = _mainWindow.PPTManager?.SlidesCount ?? 0;
                    bool hasValidPageCount = slidesCount > 0;

                    bool shouldShowButtons = ShowPPTButton &&
                                          _mainWindow.IsInPPTPresentationMode &&
                                          isInSlideShow &&
                                          hasValidPageCount &&
                                          !MainWindow.Settings.Automation.IsAutoFoldInPPTSlideShow;

                    if (!shouldShowButtons)
                    {
                        HideAllNavigationPanels();
                        return;
                    }

                    // 设置侧边按钮位置
                    _mainWindow.LeftSidePanelForPPTNavigation.Margin = new Thickness(0, 0, 0, PPTLSButtonPosition * 2);
                    _mainWindow.RightSidePanelForPPTNavigation.Margin = new Thickness(0, 0, 0, PPTRSButtonPosition * 2);

                    // 设置底部按钮水平位置
                    _mainWindow.LeftBottomPanelForPPTNavigation.Margin = new Thickness(6 + PPTLBButtonPosition, 0, 0, 6);
                    _mainWindow.RightBottomPanelForPPTNavigation.Margin = new Thickness(0, 0, 6 + PPTRBButtonPosition, 6);

                    // 根据显示选项设置面板可见性
                    var displayOption = PPTButtonsDisplayOption.ToString();
                    if (displayOption.Length >= 4)
                    {
                        var options = displayOption.ToCharArray();

                        // 左下角面板
                        if (options[0] == '2')
                            AnimationsHelper.ShowWithFadeIn(_mainWindow.LeftBottomPanelForPPTNavigation);
                        else
                            _mainWindow.LeftBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;

                        // 右下角面板
                        if (options[1] == '2')
                            AnimationsHelper.ShowWithFadeIn(_mainWindow.RightBottomPanelForPPTNavigation);
                        else
                            _mainWindow.RightBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;

                        // 左侧面板
                        if (options[2] == '2')
                            AnimationsHelper.ShowWithFadeIn(_mainWindow.LeftSidePanelForPPTNavigation);
                        else
                            _mainWindow.LeftSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;

                        // 右侧面板
                        if (options[3] == '2')
                            AnimationsHelper.ShowWithFadeIn(_mainWindow.RightSidePanelForPPTNavigation);
                        else
                            _mainWindow.RightSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新导航面板显示状态失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 更新导航按钮样式
        /// </summary>
        public void UpdateNavigationButtonStyles()
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    UpdateSideButtonStyles();
                    UpdateBottomButtonStyles();
                    ApplyNavBarScale();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新导航按钮样式失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 隐藏所有导航面板
        /// </summary>
        public void HideAllNavigationPanels()
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _mainWindow.LeftBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    _mainWindow.RightBottomPanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    _mainWindow.LeftSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                    _mainWindow.RightSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"隐藏导航面板失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 显示/隐藏侧边栏退出按钮
        /// </summary>
        public void UpdateSidebarExitButtons(bool show)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    var visibility = show ? Visibility.Visible : Visibility.Collapsed;

                    if (_mainWindow.BtnExitPPTFromSidebarLeft != null)
                        _mainWindow.BtnExitPPTFromSidebarLeft.Visibility = visibility;

                    if (_mainWindow.BtnExitPPTFromSidebarRight != null)
                        _mainWindow.BtnExitPPTFromSidebarRight.Visibility = visibility;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"更新侧边栏退出按钮失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 设置浮动栏透明度
        /// </summary>
        public void SetFloatingBarOpacity(double opacity)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    _mainWindow.ViewboxFloatingBar.Opacity = opacity;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"设置浮动栏透明度失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }

        /// <summary>
        /// 设置主面板边距
        /// </summary>
        public void SetMainPanelMargin(Thickness margin)
        {
            _dispatcher.InvokeAsync(() =>
            {
                try
                {
                    // Old UI removed:                     _mainWindow.ViewBoxStackPanelMain.Margin = margin;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"设置主面板边距失败: {ex}", LogHelper.LogType.Error);
                }
            });
        }
        #endregion


        /// <summary>
        /// 翻页条深浅跟随**应用主题**，不再读用户各自的「黑色背景」开关。
        ///
        /// 上游这里是 ppt.XXXBlackBackground（四个位置各一个复选框），
        /// 结果是主题变了翻页条不跟着变，投影上和浮动栏撞色。
        ///
        /// 原来的实现直接读 ThemeHelper.IsSystemThemeLight()，只看了系统主题 ——
        /// 应用设成深色、系统是浅色时，浮动栏深、翻页条浅，两个撞在一起。
        /// 现在改成读应用的实际主题，和浮动栏、按钮图标用同一个判定源。
        /// </summary>
        private bool IsDarkThemeForNavBar()
        {
            try
            {
                var wall = _mainWindow as MainWindow;
                return wall?.IsCurrentThemeDark() ?? !ThemeHelper.IsSystemThemeLight();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"翻页条深浅判定失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        #region Private Methods
        private void UpdateSideButtonStyles()
        {
            try
            {
                var ppt = MainWindow.Settings.PowerPointSettings;

                // 左侧
                var leftPageButtonVisibility = ppt.PPTLSShowPageNumber ? Visibility.Visible : Visibility.Collapsed;
                _mainWindow.LeftSidePanelForPPTNavigation.SetPageButtonVisibility(leftPageButtonVisibility);
                _mainWindow.LeftSidePanelForPPTNavigation.SetBarOpacity(ppt.PPTLSButtonOpacity);
                _mainWindow.LeftSidePanelForPPTNavigation.ApplyTheme(IsDarkThemeForNavBar());

                // 右侧
                var rightPageButtonVisibility = ppt.PPTRSShowPageNumber ? Visibility.Visible : Visibility.Collapsed;
                _mainWindow.RightSidePanelForPPTNavigation.SetPageButtonVisibility(rightPageButtonVisibility);
                _mainWindow.RightSidePanelForPPTNavigation.SetBarOpacity(ppt.PPTRSButtonOpacity);
                _mainWindow.RightSidePanelForPPTNavigation.ApplyTheme(IsDarkThemeForNavBar());
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新侧边按钮样式失败: {ex}", LogHelper.LogType.Error);
            }
        }

        private void UpdateBottomButtonStyles()
        {
            try
            {
                var ppt = MainWindow.Settings.PowerPointSettings;

                // 左下
                var leftBottomPageButtonVisibility = ppt.PPTLBShowPageNumber ? Visibility.Visible : Visibility.Collapsed;
                _mainWindow.LeftBottomPanelForPPTNavigation.SetPageButtonVisibility(leftBottomPageButtonVisibility);
                _mainWindow.LeftBottomPanelForPPTNavigation.SetBarOpacity(ppt.PPTLBButtonOpacity);
                _mainWindow.LeftBottomPanelForPPTNavigation.ApplyTheme(IsDarkThemeForNavBar());

                // 右下
                var rightBottomPageButtonVisibility = ppt.PPTRBShowPageNumber ? Visibility.Visible : Visibility.Collapsed;
                _mainWindow.RightBottomPanelForPPTNavigation.SetPageButtonVisibility(rightBottomPageButtonVisibility);
                _mainWindow.RightBottomPanelForPPTNavigation.SetBarOpacity(ppt.PPTRBButtonOpacity);
                _mainWindow.RightBottomPanelForPPTNavigation.ApplyTheme(IsDarkThemeForNavBar());
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新底部按钮样式失败: {ex}", LogHelper.LogType.Error);
            }
        }

        private void ApplyNavBarScale()
        {
            try
            {
                _mainWindow.LeftBottomPanelForPPTNavigation.SetBarScale(PPTLBButtonScale);
                _mainWindow.RightBottomPanelForPPTNavigation.SetBarScale(PPTRBButtonScale);
                _mainWindow.LeftSidePanelForPPTNavigation.SetBarScale(PPTLSButtonScale);
                _mainWindow.RightSidePanelForPPTNavigation.SetBarScale(PPTRSButtonScale);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用翻页按钮缩放失败: {ex}", LogHelper.LogType.Error);
            }
        }
        #endregion
    }
}
