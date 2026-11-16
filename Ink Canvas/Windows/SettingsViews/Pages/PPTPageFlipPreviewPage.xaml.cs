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
        private bool _isLoaded = false;
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

            _isLoaded = false;
            TabControlPositionSelect.SelectedIndex = 0; // Trigger load for Global tab

            _isLoaded = true;

            LoadSelectedPositionSettings();
            LoadGlobalAppearance();
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

        private void TabControlPositionSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (e.OriginalSource != TabControlPositionSelect) return;
            if (!_isLoaded) return;
            LoadSelectedPositionSettings();
            UpdateInlinePreview();
        }

        private int GetSelectedPositionIndex()
        {
            if (TabControlPositionSelect == null) return -1;
            return TabControlPositionSelect.SelectedIndex - 1; // 0=全局, 1-4=位置(0-3)
        }

        private bool IsGlobalTabSelected => TabControlPositionSelect?.SelectedIndex == 0;

        private void LoadSelectedPositionSettings()
        {
            if (TabControlPositionSelect == null) return;

            bool wasLoaded = _isLoaded;
            _isLoaded = false;
            LoadPositionSettings();
            _isLoaded = wasLoaded;
        }

        private void LoadPositionSettings()
        {
            var ppt = SettingsManager.Settings.PowerPointSettings;
            bool isGlobal = IsGlobalTabSelected;
            int selectedIndex = GetSelectedPositionIndex(); // -1 for global, 0-3 for position

            // CardUseGlobalSettings: only visible on position tabs
            CardUseGlobalSettings.Visibility = isGlobal ? Visibility.Collapsed : Visibility.Visible;

            bool useGlobal = false;
            if (!isGlobal)
            {
                useGlobal = GetUseGlobalSettings(selectedIndex, ppt);
                ToggleSwitchUseGlobalSettings.IsOn = useGlobal;
            }

            // PositionSettingsPanel: enabled when global tab, or position tab with UseGlobalSettings off
            PositionSettingsPanel.IsEnabled = isGlobal || !useGlobal;

            // Header
            string posName = isGlobal ? "全局" : GetPositionName(selectedIndex);
            CardPositionEnabled.Header = "启用" + posName + "按钮";

            // 1. Position enabled ToggleSwitch
            if (isGlobal)
            {
                ToggleSwitchPositionEnabled.IsOn = ppt.PPTGlobalButtonEnabled;
            }
            else
            {
                bool effectiveEnabled = useGlobal ? ppt.PPTGlobalButtonEnabled : IsPositionDisplayEnabled(selectedIndex, ppt);
                ToggleSwitchPositionEnabled.IsOn = effectiveEnabled;
            }

            // 2. Show Page Number ToggleSwitch
            ToggleSwitchShowPageNumber.IsOn = isGlobal ? ppt.PPTGlobalShowPageNumber
                : (useGlobal ? ppt.PPTGlobalShowPageNumber : GetPositionShowPageNumber(selectedIndex, ppt));

            // 4. Offset Sliders (Side + Bottom)
            //    全局 tab: 两个都显示；侧边位置: 仅 CardOffset；底部位置: 仅 CardOffsetBottom
            bool isSideContext = isGlobal || selectedIndex == 0 || selectedIndex == 1;
            bool isBottomContext = isGlobal || selectedIndex == 2 || selectedIndex == 3;

            CardOffset.Visibility = isSideContext ? Visibility.Visible : Visibility.Collapsed;
            CardOffsetBottom.Visibility = isBottomContext ? Visibility.Visible : Visibility.Collapsed;

            CardOffset.Header = isGlobal ? "偏移（侧边）" : "偏移";
            CardOffsetBottom.Header = isGlobal ? "偏移（底部）" : "偏移";

            SliderOffset.Minimum = -500;
            int sideOffset = isGlobal ? ppt.PPTGlobalSideButtonPosition
                : (useGlobal ? ppt.PPTGlobalSideButtonPosition : GetPositionOffset(selectedIndex, ppt));
            SliderOffset.Value = sideOffset;
            UpdateSliderText(SliderOffset, TextBlockOffsetValue, "{0:F0}");

            SliderOffsetBottom.Minimum = -100;
            int bottomOffset = isGlobal ? ppt.PPTGlobalBottomButtonPosition
                : (useGlobal ? ppt.PPTGlobalBottomButtonPosition : GetPositionOffset(selectedIndex, ppt));
            SliderOffsetBottom.Value = bottomOffset;
            UpdateSliderText(SliderOffsetBottom, TextBlockOffsetBottomValue, "{0:F0}");

            // 5. Opacity Slider
            double effectiveOpacity = isGlobal ? ppt.PPTGlobalButtonOpacity
                : (useGlobal ? ppt.PPTGlobalButtonOpacity : GetPositionOpacity(selectedIndex, ppt));
            SliderOpacity.Value = effectiveOpacity;
            UpdateSliderText(SliderOpacity, TextBlockOpacityValue, "{0:P0}");

            // 6. Scale Slider
            double effectiveScale = isGlobal ? ppt.PPTNavBarScale
                : (useGlobal ? ppt.PPTNavBarScale : GetPositionScale(selectedIndex, ppt));
            SliderScale.Value = effectiveScale;
            UpdateSliderText(SliderScale, TextBlockScaleValue, "{0:F2}");
        }

        private string GetPositionName(int index)
        {
            switch (index)
            {
                case 0: return "左侧";
                case 1: return "右侧";
                case 2: return "左下";
                case 3: return "右下";
                default: return "自定义";
            }
        }

        private int MapComboIndexToDisplayOptionIndex(int comboIndex)
        {
            switch (comboIndex)
            {
                case 0: return 2; // 左侧 (Left Side) -> display option index 2
                case 1: return 3; // 右侧 (Right Side) -> display option index 3
                case 2: return 0; // 左下 (Left Bottom) -> display option index 0
                case 3: return 1; // 右下 (Right Bottom) -> display option index 1
                default: return 0;
            }
        }

        private bool GetPositionShowPageNumber(int index, PowerPointSettings ppt)
        {
            switch (index)
            {
                case 0: return ppt.PPTLSShowPageNumber;
                case 1: return ppt.PPTRSShowPageNumber;
                case 2: return ppt.PPTLBShowPageNumber;
                case 3: return ppt.PPTRBShowPageNumber;
                default: return true;
            }
        }

        private void SetPositionShowPageNumber(int index, PowerPointSettings ppt, bool val)
        {
            switch (index)
            {
                case 0: ppt.PPTLSShowPageNumber = val; break;
                case 1: ppt.PPTRSShowPageNumber = val; break;
                case 2: ppt.PPTLBShowPageNumber = val; break;
                case 3: ppt.PPTRBShowPageNumber = val; break;
            }
        }

        private int GetPositionOffset(int index, PowerPointSettings ppt)
        {
            switch (index)
            {
                case 0: return ppt.PPTLSButtonPosition;
                case 1: return ppt.PPTRSButtonPosition;
                case 2: return ppt.PPTLBButtonPosition;
                case 3: return ppt.PPTRBButtonPosition;
                default: return 0;
            }
        }

        private void SetPositionOffset(int index, PowerPointSettings ppt, int val)
        {
            switch (index)
            {
                case 0: ppt.PPTLSButtonPosition = val; break;
                case 1: ppt.PPTRSButtonPosition = val; break;
                case 2: ppt.PPTLBButtonPosition = val; break;
                case 3: ppt.PPTRBButtonPosition = val; break;
            }
        }

        private double GetPositionOpacity(int index, PowerPointSettings ppt)
        {
            switch (index)
            {
                case 0: return ppt.PPTLSButtonOpacity;
                case 1: return ppt.PPTRSButtonOpacity;
                case 2: return ppt.PPTLBButtonOpacity;
                case 3: return ppt.PPTRBButtonOpacity;
                default: return 0.5;
            }
        }

        private void SetPositionOpacity(int index, PowerPointSettings ppt, double val)
        {
            switch (index)
            {
                case 0: ppt.PPTLSButtonOpacity = val; break;
                case 1: ppt.PPTRSButtonOpacity = val; break;
                case 2: ppt.PPTLBButtonOpacity = val; break;
                case 3: ppt.PPTRBButtonOpacity = val; break;
            }
        }

        private bool GetUseGlobalSettings(int index, PowerPointSettings ppt)
        {
            switch (index)
            {
                case 0: return ppt.PPTLSUseGlobalSettings;
                case 1: return ppt.PPTRSUseGlobalSettings;
                case 2: return ppt.PPTLBUseGlobalSettings;
                case 3: return ppt.PPTRBUseGlobalSettings;
                default: return true;
            }
        }

        private void SetUseGlobalSettings(int index, PowerPointSettings ppt, bool val)
        {
            switch (index)
            {
                case 0: ppt.PPTLSUseGlobalSettings = val; break;
                case 1: ppt.PPTRSUseGlobalSettings = val; break;
                case 2: ppt.PPTLBUseGlobalSettings = val; break;
                case 3: ppt.PPTRBUseGlobalSettings = val; break;
            }
        }

        private bool IsPositionDisplayEnabled(int index, PowerPointSettings ppt)
        {
            string str = ppt.PPTButtonsDisplayOption.ToString("D4");
            if (str.Length < 4) str = "2222";
            int displayIndex = MapComboIndexToDisplayOptionIndex(index);
            return str[displayIndex] == '2';
        }

        private double GetPositionScale(int index, PowerPointSettings ppt)
        {
            switch (index)
            {
                case 0: return ppt.PPTLSButtonScale;
                case 1: return ppt.PPTRSButtonScale;
                case 2: return ppt.PPTLBButtonScale;
                case 3: return ppt.PPTRBButtonScale;
                default: return 1.0;
            }
        }

        private void SetPositionScale(int index, PowerPointSettings ppt, double val)
        {
            switch (index)
            {
                case 0: ppt.PPTLSButtonScale = val; break;
                case 1: ppt.PPTRSButtonScale = val; break;
                case 2: ppt.PPTLBButtonScale = val; break;
                case 3: ppt.PPTRBButtonScale = val; break;
            }
        }

        #region Inline Preview

        /// <summary>
        /// 更新左侧 16:9 预览中 4 个翻页按钮的状态（可见性、缩放、偏移、透明度、页码、主题）。
        /// </summary>
        private void UpdateInlinePreview()
        {
            if (PreviewCanvas == null || PreviewLS == null) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;

            // 有效值：位置 i 若 UseGlobalSettings=true，则采用全局字段值，否则采用位置自身字段值
            double lsScale = ppt.PPTLSUseGlobalSettings ? ppt.PPTNavBarScale : ppt.PPTLSButtonScale;
            double rsScale = ppt.PPTRSUseGlobalSettings ? ppt.PPTNavBarScale : ppt.PPTRSButtonScale;
            double lbScale = ppt.PPTLBUseGlobalSettings ? ppt.PPTNavBarScale : ppt.PPTLBButtonScale;
            double rbScale = ppt.PPTRBUseGlobalSettings ? ppt.PPTNavBarScale : ppt.PPTRBButtonScale;

            double lsOpacity = ppt.PPTLSUseGlobalSettings ? ppt.PPTGlobalButtonOpacity : ppt.PPTLSButtonOpacity;
            double rsOpacity = ppt.PPTRSUseGlobalSettings ? ppt.PPTGlobalButtonOpacity : ppt.PPTRSButtonOpacity;
            double lbOpacity = ppt.PPTLBUseGlobalSettings ? ppt.PPTGlobalButtonOpacity : ppt.PPTLBButtonOpacity;
            double rbOpacity = ppt.PPTRBUseGlobalSettings ? ppt.PPTGlobalButtonOpacity : ppt.PPTRBButtonOpacity;

            bool lsShowPage = ppt.PPTLSUseGlobalSettings ? ppt.PPTGlobalShowPageNumber : ppt.PPTLSShowPageNumber;
            bool rsShowPage = ppt.PPTRSUseGlobalSettings ? ppt.PPTGlobalShowPageNumber : ppt.PPTRSShowPageNumber;
            bool lbShowPage = ppt.PPTLBUseGlobalSettings ? ppt.PPTGlobalShowPageNumber : ppt.PPTLBShowPageNumber;
            bool rbShowPage = ppt.PPTRBUseGlobalSettings ? ppt.PPTGlobalShowPageNumber : ppt.PPTRBShowPageNumber;

            // 翻页条深浅统一跟随应用主题，四个位置一样
            bool navDark = MainWindow.Settings?.Appearance?.Theme == 1
                || (MainWindow.Settings?.Appearance?.Theme == 2 && !Ink_Canvas.Helpers.ThemeHelper.IsSystemThemeLight());
            bool lsBlackBg = navDark;
            bool rsBlackBg = navDark;
            bool lbBlackBg = navDark;
            bool rbBlackBg = navDark;

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

            // 3. Set enabled/disabled visibility (UseGlobalSettings 的位由 PPTGlobalButtonEnabled 决定)
            string displayOption = ppt.PPTButtonsDisplayOption.ToString("D4");
            if (displayOption.Length < 4) displayOption = "2222";
            char[] c = displayOption.ToCharArray();
            // LeftBottom = [0], RightBottom = [1], LeftSide = [2], RightSide = [3]
            if (ppt.PPTLBUseGlobalSettings) c[0] = ppt.PPTGlobalButtonEnabled ? '2' : '1';
            if (ppt.PPTRBUseGlobalSettings) c[1] = ppt.PPTGlobalButtonEnabled ? '2' : '1';
            if (ppt.PPTLSUseGlobalSettings) c[2] = ppt.PPTGlobalButtonEnabled ? '2' : '1';
            if (ppt.PPTRSUseGlobalSettings) c[3] = ppt.PPTGlobalButtonEnabled ? '2' : '1';
            PreviewLBBorder.Visibility = c[0] == '2' ? Visibility.Visible : Visibility.Collapsed;
            PreviewRBBorder.Visibility = c[1] == '2' ? Visibility.Visible : Visibility.Collapsed;
            PreviewLSBorder.Visibility = c[2] == '2' ? Visibility.Visible : Visibility.Collapsed;
            PreviewRSBorder.Visibility = c[3] == '2' ? Visibility.Visible : Visibility.Collapsed;

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
            PreviewLS.ApplyTheme(lsBlackBg);
            PreviewRS.ApplyTheme(rsBlackBg);
            PreviewLB.ApplyTheme(lbBlackBg);
            PreviewRB.ApplyTheme(rbBlackBg);
        }

        private void PreviewLS_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TabControlPositionSelect.SelectedIndex = 1; // 左侧
        }

        private void PreviewRS_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TabControlPositionSelect.SelectedIndex = 2; // 右侧
        }

        private void PreviewLB_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TabControlPositionSelect.SelectedIndex = 3; // 左下
        }

        private void PreviewRB_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            TabControlPositionSelect.SelectedIndex = 4; // 右下
        }

        #endregion

        private void ToggleSwitchPositionEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;

            if (IsGlobalTabSelected)
            {
                ppt.PPTGlobalButtonEnabled = ToggleSwitchPositionEnabled.IsOn;
                SettingsManager.SaveSettingsToFile();
                SettingsActionHub.OnPPTGlobalSettingsChanged();
                UpdatePreviews();
                return;
            }

            int selectedIndex = GetSelectedPositionIndex();
            int displayIndex = MapComboIndexToDisplayOptionIndex(selectedIndex);

            string str = ppt.PPTButtonsDisplayOption.ToString("D4");
            char[] c = str.ToCharArray();
            c[displayIndex] = ToggleSwitchPositionEnabled.IsOn ? '2' : '1';

            ppt.PPTButtonsDisplayOption = int.Parse(new string(c));
            SettingsManager.SaveSettingsToFile();

            // Notify other managers and preview
            SettingsActionHub.OnPPTButtonsDisplayOptionChanged();
            UpdatePreviews();
        }

        private void ToggleSwitchShowPageNumber_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;

            if (IsGlobalTabSelected)
            {
                ppt.PPTGlobalShowPageNumber = ToggleSwitchShowPageNumber.IsOn;
                SettingsManager.SaveSettingsToFile();
                SettingsActionHub.OnPPTGlobalSettingsChanged();
                UpdatePreviews();
                return;
            }

            int selectedIndex = GetSelectedPositionIndex();
            bool isOn = ToggleSwitchShowPageNumber.IsOn;

            SetPositionShowPageNumber(selectedIndex, ppt, isOn);
            SettingsManager.SaveSettingsToFile();

            // Trigger UI and preview refresh
            SettingsActionHub.OnPPTButtonPositionChanged();
            UpdatePreviews();
        }

        private void ToggleSwitchUseGlobalSettings_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            int selectedIndex = GetSelectedPositionIndex();

            SetUseGlobalSettings(selectedIndex, ppt, ToggleSwitchUseGlobalSettings.IsOn);
            SettingsManager.SaveSettingsToFile();

            // Reload UI to apply IsEnabled state and effective values
            bool wasLoaded = _isLoaded;
            _isLoaded = false;
            LoadPositionSettings();
            _isLoaded = wasLoaded;

            // Notify runtime + preview (effective values may have changed)
            SettingsActionHub.OnPPTGlobalSettingsChanged();
            UpdatePreviews();
        }

        private void SliderOffset_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(SliderOffset, TextBlockOffsetValue, "{0:F0}");
            if (!_isLoaded) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            int offsetVal = (int)SliderOffset.Value;

            if (IsGlobalTabSelected)
            {
                ppt.PPTGlobalSideButtonPosition = offsetVal;
                SettingsActionHub.OnPPTGlobalSettingsChanged();
                UpdatePreviews();
                _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
                return;
            }

            int selectedIndex = GetSelectedPositionIndex();
            SetPositionOffset(selectedIndex, ppt, offsetVal);
            SettingsActionHub.OnPPTButtonPositionChanged();
            UpdatePreviews();

            _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
        }

        private void ButtonResetOffset_Click(object sender, RoutedEventArgs e)
        {
            SliderOffset.Value = 0;
        }

        private void SliderOffsetBottom_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(SliderOffsetBottom, TextBlockOffsetBottomValue, "{0:F0}");
            if (!_isLoaded) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            int offsetVal = (int)SliderOffsetBottom.Value;

            if (IsGlobalTabSelected)
            {
                ppt.PPTGlobalBottomButtonPosition = offsetVal;
                SettingsActionHub.OnPPTGlobalSettingsChanged();
                UpdatePreviews();
                _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
                return;
            }

            int selectedIndex = GetSelectedPositionIndex();
            SetPositionOffset(selectedIndex, ppt, offsetVal);
            SettingsActionHub.OnPPTButtonPositionChanged();
            UpdatePreviews();

            _sliderDelayAction.DebounceAction(2000, null, () => SettingsManager.SaveSettingsToFile());
        }

        private void ButtonResetOffsetBottom_Click(object sender, RoutedEventArgs e)
        {
            SliderOffsetBottom.Value = 0;
        }

        private void SliderOpacity_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(SliderOpacity, TextBlockOpacityValue, "{0:P0}");
            if (!_isLoaded) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            double roundedValue = Math.Round(SliderOpacity.Value, 1);

            SliderOpacity.ValueChanged -= SliderOpacity_ValueChanged;
            SliderOpacity.Value = roundedValue;
            SliderOpacity.ValueChanged += SliderOpacity_ValueChanged;

            if (IsGlobalTabSelected)
            {
                ppt.PPTGlobalButtonOpacity = roundedValue;
                SettingsManager.SaveSettingsToFile();
                SettingsActionHub.OnPPTGlobalSettingsChanged();
                UpdatePreviews();
                return;
            }

            int selectedIndex = GetSelectedPositionIndex();
            SetPositionOpacity(selectedIndex, ppt, roundedValue);

            string buttonKey = "";
            switch (selectedIndex)
            {
                case 0: buttonKey = "LS"; break;
                case 1: buttonKey = "RS"; break;
                case 2: buttonKey = "LB"; break;
                case 3: buttonKey = "RB"; break;
            }

            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTButtonOpacityChanged(buttonKey, roundedValue);
            UpdatePreviews();
        }

        private void ButtonResetOpacity_Click(object sender, RoutedEventArgs e)
        {
            SliderOpacity.Value = 0.5;
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private void SliderScale_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(SliderScale, TextBlockScaleValue, "{0:F2}");
            if (!_isLoaded) return;

            var ppt = SettingsManager.Settings.PowerPointSettings;
            double roundedValue = Math.Round(SliderScale.Value, 2);

            SliderScale.ValueChanged -= SliderScale_ValueChanged;
            SliderScale.Value = roundedValue;
            SliderScale.ValueChanged += SliderScale_ValueChanged;

            if (IsGlobalTabSelected)
            {
                ppt.PPTNavBarScale = roundedValue;
                SettingsManager.SaveSettingsToFile();
                SettingsActionHub.OnPPTNavBarScaleChanged(roundedValue);
                UpdatePreviews();
                return;
            }

            int selectedIndex = GetSelectedPositionIndex();
            SetPositionScale(selectedIndex, ppt, roundedValue);
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTGlobalSettingsChanged();
            UpdatePreviews();
        }

        // ── LTSC P1c：全局外观（2026-11-11）────────────────────────────
        // 这三项不分位置，四条导航栏共用。

        /// <summary>把当前设置填进控件。加载期间屏蔽事件，避免回写覆盖配置。</summary>
        private void LoadGlobalAppearance()
        {
            bool wasLoaded = _isLoaded;
            _isLoaded = false;
            try
            {
                var ppt = SettingsManager.Settings.PowerPointSettings;

                SliderPageFont.Value = Math.Max(10, Math.Min(40, ppt.PPTPageNumberFontSize));
                TextBlockPageFontValue.Text = $"{ppt.PPTPageNumberFontSize:0}px";

                // 总页数字号按 10/17 比例跟随，这里只做展示
                ToggleAutoHide.IsOn = ppt.PPTNavBarAutoHide;

                SliderAutoHideDelay.Value = Math.Max(1, Math.Min(15, ppt.PPTNavBarAutoHideDelaySeconds));
                TextBlockAutoHideDelayValue.Text = $"{ppt.PPTNavBarAutoHideDelaySeconds:0}s";

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
                _isLoaded = wasLoaded;
            }
        }

        // 这三项不分位置，四条导航栏共用。

        private void SliderPageFont_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockPageFontValue != null)
                TextBlockPageFontValue.Text = $"{e.NewValue:0}px";

            if (!_isLoaded) return;
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
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.PPTNavBarAutoHide = ToggleAutoHide.IsOn;
            SettingsManager.SaveSettingsToFile();
            global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
        }

        private void SliderAutoHideDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockAutoHideDelayValue != null)
                TextBlockAutoHideDelayValue.Text = $"{e.NewValue:0}s";

            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.PPTNavBarAutoHideDelaySeconds =
                Math.Round(SliderAutoHideDelay.Value, 0);
            _sliderDelayAction.DebounceAction(400, null, () =>
            {
                SettingsManager.SaveSettingsToFile();
                global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
            });
        }

        // ===== LTSC D-13：主悬浮窗空闲减淡 =====

        private void ToggleFloatingBarIdleFade_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.FloatingBarIdleFade = ToggleFloatingBarIdleFade.IsOn;
            SettingsManager.SaveSettingsToFile();
            NotifyFloatingBarIdleFadeChanged();
        }

        private void SliderFloatingBarFadeDelay_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TextBlockFloatingBarFadeDelayValue != null)
                TextBlockFloatingBarFadeDelayValue.Text = $"{e.NewValue:0}s";

            if (!_isLoaded) return;
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

            if (!_isLoaded) return;
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
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.PPTButtonPressFeedback = TogglePressFeedback.IsOn;
            SettingsManager.SaveSettingsToFile();
            global::Ink_Canvas.Helpers.PPTUIManager.NotifyAppearanceChanged();
        }
    }
}
