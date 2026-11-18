using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using NavigationViewPaneDisplayMode = iNKORE.UI.WPF.Modern.Controls.NavigationViewPaneDisplayMode;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class PPTPageFlipPreviewPage : Page
    {
        private NavigationViewPaneDisplayMode _originalPaneDisplayMode;
        private bool _originalIsInPPTPresentationMode;
        private ToolbarPosition _originalToolbarPosition;
        private DelayAction _sliderDelayAction = new DelayAction();
        private int _originalMainWindowExStyle;
        private bool _originalSettingsWindowTopmost;

        public PPTPageFlipPreviewPage()
        {
            InitializeComponent();
        }

        private void Page_Loaded(object sender, RoutedEventArgs e)
        {
            // 根据 Debug 设置决定是否显示左侧内嵌预览，并同步调整设置区左边距
            bool showPreview = SettingsManager.Settings.Advanced.IsPPTPageFlipPreviewVisible;
            PreviewPanel.Visibility = showPreview ? Visibility.Visible : Visibility.Collapsed;
            // 显示预览时左右两列等宽各占一半；隐藏预览时左列收缩不占位
            PreviewColumn.Width = showPreview ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
            // 显示预览时顶部提示与设置区左右边距收窄为 12；隐藏时保持 59 与其他页面一致
            double sideMargin = showPreview ? 12 : 59;
            TopInfoPanel.Margin = new Thickness(sideMargin, 12, 0, 12);
            SettingsContentGrid.Margin = new Thickness(sideMargin, 0, sideMargin, 0);

            var settingsWindow = Window.GetWindow(this) as SettingsWindow;
            if (settingsWindow != null)
            {
                _originalPaneDisplayMode = settingsWindow.NavigationViewControl.PaneDisplayMode;
                settingsWindow.NavigationViewControl.PaneDisplayMode = NavigationViewPaneDisplayMode.LeftMinimal;
                settingsWindow.Closed += SettingsWindow_Closed;

                // Temporarily set SettingsWindow topmost to ensure it stays in front of MainWindow
                _originalSettingsWindowTopmost = settingsWindow.Topmost;
                settingsWindow.Topmost = true;
            }

            // Force main window toolbar into PPT mode & bottom center position
            var mw = Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                _originalIsInPPTPresentationMode = mw.IsInPPTPresentationMode;
                _originalToolbarPosition = SettingsManager.Settings.Appearance.ToolbarPosition;

                // Disable AvoidFullScreenHelper hook temporarily without overriding settings
                AvoidFullScreenHelper.SetBoardMode(true);
                AvoidFullScreenHelper.StopAvoidFullScreen(mw);

                mw.IsInPPTPresentationMode = true;
                SettingsManager.Settings.Appearance.ToolbarPosition = ToolbarPosition.Right; // Bottom center layout in PPT mode

                mw.UpdateToolbarComponentVisibility();
                mw.UpdateToolbarPosition();

                // Move MainWindow to fullscreen size so it overlays the whole screen
                var screen = System.Windows.Forms.Screen.PrimaryScreen;
                var mwHwnd = new WindowInteropHelper(mw).Handle;
                MainWindow.MoveWindow(mwHwnd, 0, 0, screen.Bounds.Width, screen.Bounds.Height, true);

                // Set WS_EX_NOACTIVATE on MainWindow so clicking it does not take focus away from SettingsWindow
                _originalMainWindowExStyle = NativeWindowHelper.GetWindowLong(mwHwnd, NativeWindowHelper.GWL_EXSTYLE);
                NativeWindowHelper.SetWindowLong(mwHwnd, NativeWindowHelper.GWL_EXSTYLE, _originalMainWindowExStyle | NativeWindowHelper.WS_EX_NOACTIVATE);

                // Block/intercept preview input events to disable standard clicks on MainWindow without grey-out and without click-through
                mw.PreviewMouseDown += BlockPreviewInput;
                mw.PreviewMouseUp += BlockPreviewInput;
                mw.PreviewTouchDown += BlockPreviewInput;
                mw.PreviewStylusDown += BlockPreviewInput;
            }

            // Create and show fullscreen preview windows:
            // 1) 背景窗口（位于 SettingsWindow 之下、MainWindow 之上）
            //    注册到 WindowTopmostManager：WS_EX_NOACTIVATE 保证不被激活，ZOrder 固定在
            //    MainWindow 之上、SettingsWindow（激活后 ZOrder 增大）之下。
            // 2) 顶层 Overlay 窗口（承载 4 个翻页按钮，浮在 SettingsWindow 之上）
            //    不注册 manager（SettingsWindow 激活会超越其 ZOrder），自行维持 Z 序。
            var previewWin = new PPTPageFlipPreviewWindow();
            previewWin.Show();
            WindowTopmostManager.RegisterWindow(previewWin);

            var overlayWin = new PPTPageFlipPreviewOverlayWindow(settingsWindow);
            overlayWin.Show();

            // Re-activate settings window so it remains in front
            settingsWindow?.Activate();

            // 填控件期间屏蔽事件；_suppressEvents 的初始值就是 true，
            // 这里先显式收一次，再在填完之后放开。
            // 必须 try/finally：中途抛异常若把开关留在 true，
            // 整页控件都会变成只显示、不响应，而且没有任何报错。
            _suppressEvents = true;
            try
            {
                LoadAllSettings();
                LoadGlobalAppearance();
            }
            finally
            {
                _suppressEvents = false;
            }

            UpdateInlinePreview();
            previewWin.UpdatePreview();

            SliderTouchHelper.AddTouchSupportToAllSliders(this);

        }

        private void Page_Unloaded(object sender, RoutedEventArgs e)
        {
            var settingsWindow = Window.GetWindow(this) as SettingsWindow;
            if (settingsWindow != null)
            {
                settingsWindow.NavigationViewControl.PaneDisplayMode = _originalPaneDisplayMode;
                settingsWindow.Closed -= SettingsWindow_Closed;
                settingsWindow.Topmost = _originalSettingsWindowTopmost;
            }

            // Restore main window toolbar mode & position & AvoidFullScreenHelper
            var mw = Application.Current.MainWindow as MainWindow;
            if (mw != null)
            {
                // Restore original styles
                var mwHwnd = new WindowInteropHelper(mw).Handle;
                NativeWindowHelper.SetWindowLong(mwHwnd, NativeWindowHelper.GWL_EXSTYLE, _originalMainWindowExStyle);

                mw.IsInPPTPresentationMode = _originalIsInPPTPresentationMode;
                SettingsManager.Settings.Appearance.ToolbarPosition = _originalToolbarPosition;

                // Restore AvoidFullScreenHelper based directly on the user's active setting
                AvoidFullScreenHelper.SetBoardMode(false);
                if (SettingsManager.Settings.Advanced.IsEnableAvoidFullScreenHelper)
                {
                    AvoidFullScreenHelper.StartAvoidFullScreen(mw);
                }

                mw.UpdateToolbarComponentVisibility();
                mw.UpdateToolbarPosition();

                // Restore MainWindow to working area size
                var workingArea = System.Windows.Forms.Screen.PrimaryScreen.WorkingArea;
                MainWindow.MoveWindow(mwHwnd, workingArea.X, workingArea.Y, workingArea.Width, workingArea.Height, true);

                // Unsubscribe input blocking events
                mw.PreviewMouseDown -= BlockPreviewInput;
                mw.PreviewMouseUp -= BlockPreviewInput;
                mw.PreviewTouchDown -= BlockPreviewInput;
                mw.PreviewStylusDown -= BlockPreviewInput;
            }

            ClosePreviewWindow();
        }

        private void SettingsWindow_Closed(object sender, EventArgs e)
        {
            ClosePreviewWindow();
        }

        private void BlockPreviewInput(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
        }

        private void ClosePreviewWindow()
        {
            if (PPTPageFlipPreviewOverlayWindow.ActiveInstance != null)
            {
                try
                {
                    PPTPageFlipPreviewOverlayWindow.ActiveInstance.Close();
                }
                catch { }
            }
            if (PPTPageFlipPreviewWindow.ActiveInstance != null)
            {
                try
                {
                    WindowTopmostManager.UnregisterWindow(PPTPageFlipPreviewWindow.ActiveInstance);
                    PPTPageFlipPreviewWindow.ActiveInstance.Close();
                }
                catch { }
            }
        }

        private void Page_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateInlinePreview();
        }

        /// <summary>
        /// PreviewCanvas 尺寸变化（FixedAspectRatioPanel 完成排列）后重算 4 个 Border 的 Margin。
        /// </summary>
        private void PreviewCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateInlinePreview();
        }

        /// <summary>
        /// 同时刷新左侧内嵌预览与全屏预览窗口。
        /// </summary>
        private void UpdatePreviews()
        {
            UpdateInlinePreview();
            PPTPageFlipPreviewWindow.ActiveInstance?.UpdatePreview();
        }
        #region 位置表与位序

        // ────────────────────────────────────────────────────────────────
        // PPTButtonsDisplayOption 是**四位数编码**，每一位是 '0'/'1'/'2'，
        // 其中 '2' 表示显示。位序（从左到右，和字符串下标一致）：
        //
        //   [0] = LeftBottom   左下
        //   [1] = RightBottom  右下
        //   [2] = LeftSide     左侧
        //   [3] = RightSide    右侧
        //
        // 这个顺序不是本页面自己定的，下游有三处按同一顺序读：
        //   MainWindow_cs/MW_AutoFold.cs           dopsc[0..3]
        //   MainWindow_cs/MW_FloatingBarIcons.Panels.cs
        //   Helpers/PPTUIManager.cs                options[0..3]
        // 写反了编译不会报错，只会表现为「点了左下、右下跟着变」。
        // 改这五个常量之前，先把上面三处读一遍。
        // ────────────────────────────────────────────────────────────────
        private const int BitLeftBottom = 0;
        private const int BitRightBottom = 1;
        private const int BitLeftSide = 2;
        private const int BitRightSide = 3;

        /// <summary>
        /// 一个位置的全部控件与它对应的设置字段。
        /// 四行结构完全一样，只是绑的字段不同，所以用一张表驱动，
        /// 而不是写四套几乎相同的事件处理器——那样改一处漏三处是必然的。
        /// </summary>
        private sealed class PositionUi
        {
            /// <summary>通知下游时用的键（LS/RS/LB/RB），和 SettingsActionHub 的约定一致。</summary>
            public string Key;
            /// <summary>页面标题和日志里显示的名字。</summary>
            public string Name;
            /// <summary>在 PPTButtonsDisplayOption 里的字符下标（0..3）。</summary>
            public int DisplayBit;
            /// <summary>true=底部位置（左下/右下），偏移用底部那组；false=侧边位置。</summary>
            public bool IsBottom;

            public Func<PowerPointSettings, bool> GetUseGlobal;
            public Action<PowerPointSettings, bool> SetUseGlobal;
            public Func<PowerPointSettings, bool> GetShowPageNumber;
            public Action<PowerPointSettings, bool> SetShowPageNumber;
            public Func<PowerPointSettings, int> GetOffset;
            public Action<PowerPointSettings, int> SetOffset;
            public Func<PowerPointSettings, double> GetOpacity;
            public Action<PowerPointSettings, double> SetOpacity;
            public Func<PowerPointSettings, double> GetScale;
            public Action<PowerPointSettings, double> SetScale;

            public iNKORE.UI.WPF.Modern.Controls.ToggleSwitch EnabledToggle;
            public iNKORE.UI.WPF.Modern.Controls.ToggleSwitch ShowPageNumberToggle;
            public Slider OffsetSlider;
            public TextBlock OffsetText;
            public Slider OpacitySlider;
            public TextBlock OpacityText;
            public Slider ScaleSlider;
            public TextBlock ScaleText;
            public Button ResetOffsetButton;
            public Button ResetOpacityButton;
            public iNKORE.UI.WPF.Modern.Controls.SettingsExpander Expander;
        }

        private PositionUi[] _positions;
        private bool _suppressEvents = true;

        /// <summary>
        /// 建立四行位置表。顺序只影响页面上的排列顺序，
        /// 真正决定配置落点的是 DisplayBit —— 两者不要混为一谈。
        /// </summary>
        private void BuildPositionTable()
        {
            _positions = new[]
            {
                new PositionUi
                {
                    Key = "LB", Name = "左下", DisplayBit = BitLeftBottom, IsBottom = true,
                    GetUseGlobal = s => s.PPTLBUseGlobalSettings, SetUseGlobal = (s, v) => s.PPTLBUseGlobalSettings = v,
                    GetShowPageNumber = s => s.PPTLBShowPageNumber, SetShowPageNumber = (s, v) => s.PPTLBShowPageNumber = v,
                    GetOffset = s => s.PPTLBButtonPosition, SetOffset = (s, v) => s.PPTLBButtonPosition = v,
                    GetOpacity = s => s.PPTLBButtonOpacity, SetOpacity = (s, v) => s.PPTLBButtonOpacity = v,
                    GetScale = s => s.PPTLBButtonScale, SetScale = (s, v) => s.PPTLBButtonScale = v,
                    EnabledToggle = ToggleSwitchLBEnabled,
                    ShowPageNumberToggle = ToggleSwitchLBShowPageNumber,
                    OffsetSlider = SliderLBOffset, OffsetText = TextBlockLBOffsetValue, ResetOffsetButton = ButtonResetLBOffset,
                    OpacitySlider = SliderLBOpacity, OpacityText = TextBlockLBOpacityValue, ResetOpacityButton = ButtonResetLBOpacity,
                    ScaleSlider = SliderLBScale, ScaleText = TextBlockLBScaleValue,
                    Expander = ExpanderLeftBottom,
                },
                new PositionUi
                {
                    Key = "RB", Name = "右下", DisplayBit = BitRightBottom, IsBottom = true,
                    GetUseGlobal = s => s.PPTRBUseGlobalSettings, SetUseGlobal = (s, v) => s.PPTRBUseGlobalSettings = v,
                    GetShowPageNumber = s => s.PPTRBShowPageNumber, SetShowPageNumber = (s, v) => s.PPTRBShowPageNumber = v,
                    GetOffset = s => s.PPTRBButtonPosition, SetOffset = (s, v) => s.PPTRBButtonPosition = v,
                    GetOpacity = s => s.PPTRBButtonOpacity, SetOpacity = (s, v) => s.PPTRBButtonOpacity = v,
                    GetScale = s => s.PPTRBButtonScale, SetScale = (s, v) => s.PPTRBButtonScale = v,
                    EnabledToggle = ToggleSwitchRBEnabled,
                    ShowPageNumberToggle = ToggleSwitchRBShowPageNumber,
                    OffsetSlider = SliderRBOffset, OffsetText = TextBlockRBOffsetValue, ResetOffsetButton = ButtonResetRBOffset,
                    OpacitySlider = SliderRBOpacity, OpacityText = TextBlockRBOpacityValue, ResetOpacityButton = ButtonResetRBOpacity,
                    ScaleSlider = SliderRBScale, ScaleText = TextBlockRBScaleValue,
                    Expander = ExpanderRightBottom,
                },
                new PositionUi
                {
                    Key = "LS", Name = "左侧", DisplayBit = BitLeftSide, IsBottom = false,
                    GetUseGlobal = s => s.PPTLSUseGlobalSettings, SetUseGlobal = (s, v) => s.PPTLSUseGlobalSettings = v,
                    GetShowPageNumber = s => s.PPTLSShowPageNumber, SetShowPageNumber = (s, v) => s.PPTLSShowPageNumber = v,
                    GetOffset = s => s.PPTLSButtonPosition, SetOffset = (s, v) => s.PPTLSButtonPosition = v,
                    GetOpacity = s => s.PPTLSButtonOpacity, SetOpacity = (s, v) => s.PPTLSButtonOpacity = v,
                    GetScale = s => s.PPTLSButtonScale, SetScale = (s, v) => s.PPTLSButtonScale = v,
                    EnabledToggle = ToggleSwitchLSEnabled,
                    ShowPageNumberToggle = ToggleSwitchLSShowPageNumber,
                    OffsetSlider = SliderLSOffset, OffsetText = TextBlockLSOffsetValue, ResetOffsetButton = ButtonResetLSOffset,
                    OpacitySlider = SliderLSOpacity, OpacityText = TextBlockLSOpacityValue, ResetOpacityButton = ButtonResetLSOpacity,
                    ScaleSlider = SliderLSScale, ScaleText = TextBlockLSScaleValue,
                    Expander = ExpanderLeftSide,
                },
                new PositionUi
                {
                    Key = "RS", Name = "右侧", DisplayBit = BitRightSide, IsBottom = false,
                    GetUseGlobal = s => s.PPTRSUseGlobalSettings, SetUseGlobal = (s, v) => s.PPTRSUseGlobalSettings = v,
                    GetShowPageNumber = s => s.PPTRSShowPageNumber, SetShowPageNumber = (s, v) => s.PPTRSShowPageNumber = v,
                    GetOffset = s => s.PPTRSButtonPosition, SetOffset = (s, v) => s.PPTRSButtonPosition = v,
                    GetOpacity = s => s.PPTRSButtonOpacity, SetOpacity = (s, v) => s.PPTRSButtonOpacity = v,
                    GetScale = s => s.PPTRSButtonScale, SetScale = (s, v) => s.PPTRSButtonScale = v,
                    EnabledToggle = ToggleSwitchRSEnabled,
                    ShowPageNumberToggle = ToggleSwitchRSShowPageNumber,
                    OffsetSlider = SliderRSOffset, OffsetText = TextBlockRSOffsetValue, ResetOffsetButton = ButtonResetRSOffset,
                    OpacitySlider = SliderRSOpacity, OpacityText = TextBlockRSOpacityValue, ResetOpacityButton = ButtonResetRSOpacity,
                    ScaleSlider = SliderRSScale, ScaleText = TextBlockRSScaleValue,
                    Expander = ExpanderRightSide,
                },
            };

            // 偏移滑块的量程本来就按位置类型区分：底部水平挪动范围小，侧边竖直挪动范围大。
            foreach (var pos in _positions)
            {
                pos.OffsetSlider.Minimum = pos.IsBottom ? -100 : -500;
                pos.OffsetSlider.Maximum = 500;
            }
        }

        /// <summary>
        /// 把 PPTButtonsDisplayOption 读成四位字符串，并顺手修掉坏值。
        /// 不做这层规范化的话，后面按下标取字符会拿到非预期结果：
        /// 负数 ToString("D4") 是 "-001"（5 个字符），位数不足会左补齐。
        /// </summary>
        private static string ReadDisplayOption(PowerPointSettings ppt)
        {
            string raw = ppt.PPTButtonsDisplayOption.ToString("D4");
            if (raw.Length == 4 && IsAllDisplayDigits(raw)) return raw;

            // 值不合法（旧配置、手改、负数）：退回全显示，但必须让用户知道发生过什么。
            LogFlip($"配置里的 PPTButtonsDisplayOption 不合法（原值={ppt.PPTButtonsDisplayOption}），已按 2222 处理");
            return "2222";
        }

        private static bool IsAllDisplayDigits(string s)
        {
            foreach (char ch in s)
            {
                if (ch != '0' && ch != '1' && ch != '2') return false;
            }
            return s.Length == 4;
        }

        private static void LogFlip(string message)
        {
            LogHelper.WriteLogToFile("[翻页设置] " + message, LogHelper.LogType.Event);
        }

        #endregion

        #region 加载与保存

        /// <summary>
        /// 读配置填控件。整段屏蔽事件，避免「填值」被当成「用户操作」回写。
        /// </summary>
        private void LoadAllSettings()
        {
            bool wasLoaded = _suppressEvents;
            _suppressEvents = true;
            try
            {
                var ppt = SettingsManager.Settings.PowerPointSettings;
                BuildPositionTableIfNeeded();

                string displayOption = ReadDisplayOption(ppt);
                var inheritedNames = new System.Collections.Generic.List<string>();

                foreach (var pos in _positions)
                {
                    bool inherit = pos.GetUseGlobal(ppt);
                    if (inherit) inheritedNames.Add(pos.Name);

                    // 生效值：位置若仍在「使用全局设置」状态，就用全局值，
                    // 否则用位置自己的值。和 MW_Settings.UpdatePPTUIManagerSettings
                    // 用的是同一条规则，改动要同步 —— 包括启用这一位：
                    // 该位置还在跟随全局时，真正决定显隐的是 PPTGlobalButtonEnabled，
                    // displayOption 的那一位会被它盖掉。这里必须显示盖掉之后的实际状态，
                    // 否则页面显示"开"、屏幕上却是"关"。
                    pos.EnabledToggle.IsOn = inherit
                        ? ppt.PPTGlobalButtonEnabled
                        : displayOption[pos.DisplayBit] == '2';
                    pos.ShowPageNumberToggle.IsOn = inherit ? ppt.PPTGlobalShowPageNumber : pos.GetShowPageNumber(ppt);

                    int offset = inherit
                        ? (pos.IsBottom ? ppt.PPTGlobalBottomButtonPosition : ppt.PPTGlobalSideButtonPosition)
                        : pos.GetOffset(ppt);
                    pos.OffsetSlider.Value = offset;
                    UpdateSliderText(pos.OffsetSlider, pos.OffsetText, "{0:F0}");

                    pos.OpacitySlider.Value = inherit ? ppt.PPTGlobalButtonOpacity : pos.GetOpacity(ppt);
                    UpdateSliderText(pos.OpacitySlider, pos.OpacityText, "{0:P0}");

                    pos.ScaleSlider.Value = inherit ? ppt.PPTNavBarScale : pos.GetScale(ppt);
                    UpdateSliderText(pos.ScaleSlider, pos.ScaleText, "{0:F2}");
                }

                // 一行日志说明这次读到的是什么，排查「重启后值跑位」时直接看这句。
                LogFlip($"加载 displayOption={displayOption} 位序[0]=左下 [1]=右下 [2]=左侧 [3]=右侧"
                        + (inheritedNames.Count > 0
                            ? $"；仍在跟随全局的老配置位置: {string.Join("、", inheritedNames)}"
                            : "；四个位置均为独立配置"));
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载翻页按钮设置失败: {ex}", LogHelper.LogType.Error);
            }
            finally
            {
                _suppressEvents = wasLoaded;
            }
        }

        private void BuildPositionTableIfNeeded()
        {
            if (_positions == null) BuildPositionTable();
        }

        /// <summary>
        /// 关闭某个位置的「跟随全局」，让位置自己的字段真正成为生效值。
        ///
        /// 为什么必须关：MW_Settings.UpdatePPTUIManagerSettings 发现位置还在跟随全局时，
        /// 会用全局值覆盖该位置自己的字段（第 398-538 行）。用户在页面上改了却看着没反应，
        /// 就是因为写进去的值下一轮同步被全局值盖掉了。
        /// </summary>
        private bool ReleaseInheritance(PowerPointSettings ppt, PositionUi pos)
        {
            if (!pos.GetUseGlobal(ppt)) return false;

            // 关键：先把「此刻屏幕上生效的值」抄进该位置自己的字段，再关开关。
            //
            // 顺序反过来会出错：位置自己的字段上次被用户设置过之后，
            // 又因为重新打开继承而长期没人维护，里面是旧值。
            // 直接关开关的话，那一瞬间显示页码/偏移/透明度/缩放会集体跳回旧值，
            // 用户只动了一个滑块却看到四样东西一起变。
            pos.SetShowPageNumber(ppt, EffectiveShowPageNumber(ppt, pos));
            pos.SetOffset(ppt, pos.IsBottom ? ppt.PPTGlobalBottomButtonPosition : ppt.PPTGlobalSideButtonPosition);
            pos.SetOpacity(ppt, ppt.PPTGlobalButtonOpacity);
            pos.SetScale(ppt, ppt.PPTNavBarScale);

            // 启用这一位：继承时由 PPTGlobalButtonEnabled 决定，落成实际的 '2'/'1'。
            string current = ReadDisplayOption(ppt);
            char[] c = current.ToCharArray();
            c[pos.DisplayBit] = ppt.PPTGlobalButtonEnabled ? '2' : '1';
            ppt.PPTButtonsDisplayOption = int.Parse(new string(c));

            pos.SetUseGlobal(ppt, false);
            LogFlip($"{pos.Name} 从全局继承转为独立配置（PPT{pos.Key}UseGlobalSettings true->false，"
                    + $"继承期间生效值已落盘 displayOption={new string(c)}）");
            return true;
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private static bool TryGetPosition(PositionUi[] positions, object sender, out PositionUi pos)
        {
            foreach (var p in positions)
            {
                if (ReferenceEquals(sender, p.EnabledToggle)
                    || ReferenceEquals(sender, p.ShowPageNumberToggle)
                    || ReferenceEquals(sender, p.OffsetSlider)
                    || ReferenceEquals(sender, p.OpacitySlider)
                    || ReferenceEquals(sender, p.ScaleSlider))
                {
                    pos = p;
                    return true;
                }
            }
            pos = null;
            return false;
        }

        #endregion

        #region 四行各自的控件事件

        /// <summary>
        /// 启用开关。真正落盘的是 PPTButtonsDisplayOption 的那一位，
        /// 不是 PPTXxxButtonPosition（那个是位置偏移）。
        /// </summary>
        private void ToggleSwitchPositionEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents || _positions == null) return;
            if (!TryGetPosition(_positions, sender, out var pos)) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            bool show = pos.EnabledToggle.IsOn;

            // 用户看到的旧状态是「生效值」：该位置还在跟随全局时，
            // 决定显隐的是 PPTGlobalButtonEnabled，不是那一位。拿位元比较会误判成「没变化」。
            bool before = pos.GetUseGlobal(ppt) ? ppt.PPTGlobalButtonEnabled : ReadDisplayOption(ppt)[pos.DisplayBit] == '2';
            if (before == show) return;

            // 先把该位置的其它生效值落盘（ReleaseInheritance 里做的事），
            // 再单独写这一位——顺序不能反，ReleaseInheritance 会重算整串。
            bool released = ReleaseInheritance(ppt, pos);

            string current = ReadDisplayOption(ppt);
            char oldBit = current[pos.DisplayBit];
            char newBit = show ? '2' : '1';
            char[] c = current.ToCharArray();
            c[pos.DisplayBit] = newBit;
            string after = new string(c);
            ppt.PPTButtonsDisplayOption = int.Parse(after);

            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonsDisplayOptionChanged();
            LogFlip($"保存 displayOption={after} 来源={pos.Name} 位[{pos.DisplayBit}] {oldBit}->{newBit}"
                    + (released ? "（该位置已转为独立配置）" : ""));
            UpdatePreviews();
        }

        private void ToggleSwitchShowPageNumber_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents || _positions == null) return;
            if (!TryGetPosition(_positions, sender, out var pos)) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            bool isOn = pos.ShowPageNumberToggle.IsOn;

            pos.SetShowPageNumber(ppt, isOn);
            ReleaseInheritance(ppt, pos);
            SettingsManager.SaveSettingsToFile();

            SettingsActionHub.OnPPTButtonPositionChanged();
            LogFlip($"保存 {pos.Name} 显示页码={isOn}");
            UpdatePreviews();
        }

        private void SliderOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_positions == null) return;
            if (!TryGetPosition(_positions, sender, out var pos)) return;

            UpdateSliderText(pos.OffsetSlider, pos.OffsetText, "{0:F0}");
            if (_suppressEvents) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            int offsetVal = (int)pos.OffsetSlider.Value;

            pos.SetOffset(ppt, offsetVal);
            ReleaseInheritance(ppt, pos);
            SettingsActionHub.OnPPTButtonPositionChanged();
            LogFlip($"保存 {pos.Name} 偏移={offsetVal}");
            UpdatePreviews();

            _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
        }

        private void ButtonResetOffset_Click(object sender, RoutedEventArgs e)
        {
            if (_positions == null) return;
            if (!TryGetPositionByResetButton(_positions, sender, out var pos)) return;
            pos.OffsetSlider.Value = 0;
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_positions == null) return;
            if (!TryGetPosition(_positions, sender, out var pos)) return;

            UpdateSliderText(pos.OpacitySlider, pos.OpacityText, "{0:P0}");
            if (_suppressEvents) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            double roundedValue = Math.Round(pos.OpacitySlider.Value, 1);

            // 吸附到 0.1 的整数倍：不然滑到 0.4999 会写成 0.4999，重启后显示成 50%。
            pos.OpacitySlider.ValueChanged -= SliderOpacity_ValueChanged;
            pos.OpacitySlider.Value = roundedValue;
            pos.OpacitySlider.ValueChanged += SliderOpacity_ValueChanged;

            pos.SetOpacity(ppt, roundedValue);
            ReleaseInheritance(ppt, pos);
            SettingsManager.SaveSettingsToFile();

            SettingsActionHub.OnPPTButtonOpacityChanged(pos.Key, roundedValue);
            LogFlip($"保存 {pos.Name} 透明度={roundedValue:0.0}");
            UpdatePreviews();
        }

        private void ButtonResetOpacity_Click(object sender, RoutedEventArgs e)
        {
            if (_positions == null) return;
            if (!TryGetPositionByResetButton(_positions, sender, out var pos)) return;
            pos.OpacitySlider.Value = 0.5;
        }

        private void SliderScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_positions == null) return;
            if (!TryGetPosition(_positions, sender, out var pos)) return;

            UpdateSliderText(pos.ScaleSlider, pos.ScaleText, "{0:F2}");
            if (_suppressEvents) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            double roundedValue = Math.Round(pos.ScaleSlider.Value, 2);

            pos.ScaleSlider.ValueChanged -= SliderScale_ValueChanged;
            pos.ScaleSlider.Value = roundedValue;
            pos.ScaleSlider.ValueChanged += SliderScale_ValueChanged;

            pos.SetScale(ppt, roundedValue);
            ReleaseInheritance(ppt, pos);
            SettingsManager.SaveSettingsToFile();

            SettingsActionHub.OnPPTGlobalSettingsChanged();
            LogFlip($"保存 {pos.Name} 缩放={roundedValue:0.00}");
            UpdatePreviews();
        }

        /// <summary>
        /// 反查重置按钮属于哪一行。8 个按钮共用 2 个处理器，
        /// 靠发送方引用认行，避免写 8 份一模一样的代码。
        /// </summary>
        private static bool TryGetPositionByResetButton(PositionUi[] positions, object sender, out PositionUi pos)
        {
            foreach (var p in positions)
            {
                if (ReferenceEquals(sender, p.ResetOffsetButton) || ReferenceEquals(sender, p.ResetOpacityButton))
                {
                    pos = p;
                    return true;
                }
            }
            pos = null;
            return false;
        }

        #endregion

        #region Inline Preview

        /// <summary>
        /// 更新左侧 16:9 预览中 4 个翻页按钮的状态（可见性、缩放、偏移、透明度、页码、主题）。
        /// 有效值和页面上的控件走同一套规则：位置仍在跟随全局时用全局值。
        /// </summary>
        private void UpdateInlinePreview()
        {
            if (PreviewCanvas == null || PreviewLS == null) return;
            BuildPositionTableIfNeeded();

            var ppt = SettingsManager.Settings.PowerPointSettings;
            string displayOption = ReadDisplayOption(ppt);

            double lsScale = EffectiveScale(ppt, FindPosition("LS"));
            double rsScale = EffectiveScale(ppt, FindPosition("RS"));
            double lbScale = EffectiveScale(ppt, FindPosition("LB"));
            double rbScale = EffectiveScale(ppt, FindPosition("RB"));

            double lsOpacity = EffectiveOpacity(ppt, FindPosition("LS"));
            double rsOpacity = EffectiveOpacity(ppt, FindPosition("RS"));
            double lbOpacity = EffectiveOpacity(ppt, FindPosition("LB"));
            double rbOpacity = EffectiveOpacity(ppt, FindPosition("RB"));

            bool lsShowPage = EffectiveShowPageNumber(ppt, FindPosition("LS"));
            bool rsShowPage = EffectiveShowPageNumber(ppt, FindPosition("RS"));
            bool lbShowPage = EffectiveShowPageNumber(ppt, FindPosition("LB"));
            bool rbShowPage = EffectiveShowPageNumber(ppt, FindPosition("RB"));

            // 翻页条深浅统一跟随应用主题，四个位置一样
            bool navDark = MainWindow.Settings?.Appearance?.Theme == 1
                || (MainWindow.Settings?.Appearance?.Theme == 2 && !Ink_Canvas.Helpers.ThemeHelper.IsSystemThemeLight());

            // 1. Set scale for all 4 bars
            PreviewLS.SetBarScale(lsScale);
            PreviewRS.SetBarScale(rsScale);
            PreviewLB.SetBarScale(lbScale);
            PreviewRB.SetBarScale(rbScale);

            // 2. Set margins — 内嵌预览不应用位置偏移，仅使用基础边距（按预览宽度比例缩放）
            double viewScale = (PreviewCanvas.ActualWidth > 0) ? PreviewCanvas.ActualWidth / 1600.0 : 1.0;

            PreviewLSBorder.Margin = new Thickness(6 * viewScale, 0, 0, 0);
            PreviewRSBorder.Margin = new Thickness(0, 0, 6 * viewScale, 0);
            PreviewLBBorder.Margin = new Thickness(6 * viewScale, 0, 0, 6 * viewScale);
            PreviewRBBorder.Margin = new Thickness(0, 0, 6 * viewScale, 6 * viewScale);

            // 3. 可见性按位序取：0=左下 1=右下 2=左侧 3=右侧
            PreviewLBBorder.Visibility = displayOption[BitLeftBottom] == '2' ? Visibility.Visible : Visibility.Collapsed;
            PreviewRBBorder.Visibility = displayOption[BitRightBottom] == '2' ? Visibility.Visible : Visibility.Collapsed;
            PreviewLSBorder.Visibility = displayOption[BitLeftSide] == '2' ? Visibility.Visible : Visibility.Collapsed;
            PreviewRSBorder.Visibility = displayOption[BitRightSide] == '2' ? Visibility.Visible : Visibility.Collapsed;

            // 4. Set page button visibility (Show Page Number)
            PreviewLS.SetPageButtonVisibility(lsShowPage ? Visibility.Visible : Visibility.Collapsed);
            PreviewRS.SetPageButtonVisibility(rsShowPage ? Visibility.Visible : Visibility.Collapsed);
            PreviewLB.SetPageButtonVisibility(lbShowPage ? Visibility.Visible : Visibility.Collapsed);
            PreviewRB.SetPageButtonVisibility(rbShowPage ? Visibility.Visible : Visibility.Collapsed);

            // 5. Set opacity
            PreviewLS.SetBarOpacity(lsOpacity);
            PreviewRS.SetBarOpacity(rsOpacity);
            PreviewLB.SetBarOpacity(lbOpacity);
            PreviewRB.SetBarOpacity(rbOpacity);

            // 6. Set theme (Black Background)
            PreviewLS.ApplyTheme(navDark);
            PreviewRS.ApplyTheme(navDark);
            PreviewLB.ApplyTheme(navDark);
            PreviewRB.ApplyTheme(navDark);
        }

        private PositionUi FindPosition(string key)
        {
            foreach (var pos in _positions)
            {
                if (pos.Key == key) return pos;
            }
            return null;
        }

        private static double EffectiveScale(PowerPointSettings ppt, PositionUi pos)
            => pos.GetUseGlobal(ppt) ? ppt.PPTNavBarScale : pos.GetScale(ppt);

        private static double EffectiveOpacity(PowerPointSettings ppt, PositionUi pos)
            => pos.GetUseGlobal(ppt) ? ppt.PPTGlobalButtonOpacity : pos.GetOpacity(ppt);

        private static bool EffectiveShowPageNumber(PowerPointSettings ppt, PositionUi pos)
            => pos.GetUseGlobal(ppt) ? ppt.PPTGlobalShowPageNumber : pos.GetShowPageNumber(ppt);

        // 点击预览里的翻页按钮 → 展开对应那一行的设置。
        private void PreviewLS_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ScrollToPosition("LS");

        private void PreviewRS_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ScrollToPosition("RS");

        private void PreviewLB_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ScrollToPosition("LB");

        private void PreviewRB_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => ScrollToPosition("RB");

        /// <summary>
        /// 展开某个位置的折叠面板并滚到可见区域。
        /// 以前这里是切 Tab；没有 Tab 之后，点击预览要落到对应那一行，
        /// 否则用户点了预览屏幕毫无反应。
        /// </summary>
        private void ScrollToPosition(string key)
        {
            BuildPositionTableIfNeeded();
            var pos = FindPosition(key);
            if (pos?.Expander == null) return;

            pos.Expander.IsExpanded = true;
            pos.Expander.BringIntoView();
        }

        #endregion

        // ── LTSC P1c：全局外观（2026-11-11）────────────────────────────
        // 这三项不分位置，四条导航栏共用。

        /// <summary>把当前设置填进控件。加载期间屏蔽事件，避免回写覆盖配置。</summary>
        private void LoadGlobalAppearance()
        {
            bool wasSuppressed = _suppressEvents;
            _suppressEvents = true;
            try
            {
                var ppt = SettingsManager.Settings.PowerPointSettings;

                SliderPageFont.Value = Math.Max(10, Math.Min(40, ppt.PPTPageNumberFontSize));
                TextBlockPageFontValue.Text = $"{ppt.PPTPageNumberFontSize:0}px";

                // 总页数字号按 10/17 比例跟随，这里只做展示
                ToggleAutoHide.IsOn = ppt.PPTNavBarAutoHide;

                SliderAutoHideDelay.Value = Math.Max(1, Math.Min(15, ppt.PPTNavBarAutoHideDelaySeconds));
                TextBlockAutoHideDelayValue.Text = $"{ppt.PPTNavBarAutoHideDelaySeconds:0}s";

                // LTSC（2026-11-18）：淡出透明度。这个值一直存在也一直在用，
                // 只是从来没暴露到界面上。
                SliderAutoHideOpacity.Value = Math.Max(0.0, Math.Min(1.0, ppt.PPTNavBarAutoHideOpacity));
                TextBlockAutoHideOpacityValue.Text = $"{ppt.PPTNavBarAutoHideOpacity:0.00}";

                // LTSC D-13：主悬浮窗空闲减淡
                ToggleFloatingBarIdleFade.IsOn = ppt.FloatingBarIdleFade;
                SliderFloatingBarFadeDelay.Value = Math.Max(2, Math.Min(20, ppt.FloatingBarIdleFadeDelaySeconds));
                TextBlockFloatingBarFadeDelayValue.Text = $"{ppt.FloatingBarIdleFadeDelaySeconds:0}s";
                SliderFloatingBarFadeOpacity.Value = Math.Max(0.2, Math.Min(1.0, ppt.FloatingBarIdleFadeOpacity));
                TextBlockFloatingBarFadeOpacityValue.Text = $"{ppt.FloatingBarIdleFadeOpacity:0.00}";

                TogglePressFeedback.IsOn = ppt.PPTButtonPressFeedback;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载翻页栏外观设置失败: {ex.Message}", LogHelper.LogType.Warning);
            }
            finally
            {
                _suppressEvents = wasSuppressed;
            }
        }

        private void SliderPageFont_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockPageFontValue != null)
                TextBlockPageFontValue.Text = $"{e.NewValue:0}px";

            if (_suppressEvents) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            double v = Math.Round(SliderPageFont.Value, 0);
            ppt.PPTPageNumberFontSize = v;
            // 总页数字号按比例走，保持 17:10 的原始视觉比例。
            ppt.PPTPageTotalFontSize = Math.Max(8, Math.Round(v * 10.0 / 17.0, 0));
            _sliderDelayAction.DebounceAction(400, null, () =>
            {
                SettingsManager.SaveSettingsToFile();
                global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
            });
        }

        private void ButtonResetPageFont_Click(object sender, RoutedEventArgs e)
        {
            SliderPageFont.Value = 17;
            SettingsManager.Settings.PowerPointSettings.PPTPageNumberFontSize = 17;
            SettingsManager.Settings.PowerPointSettings.PPTPageTotalFontSize = 10;
            TextBlockPageFontValue.Text = "17px";
            SettingsManager.SaveSettingsToFile();
            global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
        }

        private void ToggleAutoHide_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            SettingsManager.Settings.PowerPointSettings.PPTNavBarAutoHide = ToggleAutoHide.IsOn;
            SettingsManager.SaveSettingsToFile();
            global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
        }

        private void SliderAutoHideDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockAutoHideDelayValue != null)
                TextBlockAutoHideDelayValue.Text = $"{e.NewValue:0}s";

            if (_suppressEvents) return;
            SettingsManager.Settings.PowerPointSettings.PPTNavBarAutoHideDelaySeconds =
                Math.Round(SliderAutoHideDelay.Value, 0);
            _sliderDelayAction.DebounceAction(400, null, () =>
            {
                SettingsManager.SaveSettingsToFile();
                global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
            });
        }

        private void SliderAutoHideOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockAutoHideOpacityValue != null)
                TextBlockAutoHideOpacityValue.Text = $"{e.NewValue:0.00}";

            if (_suppressEvents) return;
            SettingsManager.Settings.PowerPointSettings.PPTNavBarAutoHideOpacity =
                Math.Round(SliderAutoHideOpacity.Value, 2);
            _sliderDelayAction.DebounceAction(400, null, () =>
            {
                SettingsManager.SaveSettingsToFile();
                global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
            });
        }

        // ===== LTSC D-13：主悬浮窗空闲减淡 =====

        private void ToggleFloatingBarIdleFade_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            SettingsManager.Settings.PowerPointSettings.FloatingBarIdleFade = ToggleFloatingBarIdleFade.IsOn;
            SettingsManager.SaveSettingsToFile();
            NotifyFloatingBarIdleFadeChanged();
        }

        private void SliderFloatingBarFadeDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockFloatingBarFadeDelayValue != null)
                TextBlockFloatingBarFadeDelayValue.Text = $"{e.NewValue:0}s";

            if (_suppressEvents) return;
            SettingsManager.Settings.PowerPointSettings.FloatingBarIdleFadeDelaySeconds =
                Math.Round(SliderFloatingBarFadeDelay.Value, 0);
            _sliderDelayAction.DebounceAction(400, null, () =>
            {
                SettingsManager.SaveSettingsToFile();
                NotifyFloatingBarIdleFadeChanged();
            });
        }

        private void SliderFloatingBarFadeOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockFloatingBarFadeOpacityValue != null)
                TextBlockFloatingBarFadeOpacityValue.Text = $"{e.NewValue:0.00}";

            if (_suppressEvents) return;
            SettingsManager.Settings.PowerPointSettings.FloatingBarIdleFadeOpacity =
                Math.Round(SliderFloatingBarFadeOpacity.Value, 2);
            _sliderDelayAction.DebounceAction(400, null, () =>
            {
                SettingsManager.SaveSettingsToFile();
                NotifyFloatingBarIdleFadeChanged();
            });
        }

        /// <summary>
        /// 设置改了之后让 MainWindow 重新读一遍。走全局静态事件，
        /// 免得这个页面反向依赖 MainWindow 实例。
        /// </summary>
        private static void NotifyFloatingBarIdleFadeChanged()
        {
            try { Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged(); } catch { }
            try
            {
                if (global::Ink_Canvas.App.Current?.MainWindow is MainWindow mw)
                    mw.ApplyFloatingBarIdleFadeSettings();
            }
            catch { }
        }

        private void TogglePressFeedback_Toggled(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents) return;
            SettingsManager.Settings.PowerPointSettings.PPTButtonPressFeedback = TogglePressFeedback.IsOn;
            SettingsManager.SaveSettingsToFile();
            global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
        }
    }
}
