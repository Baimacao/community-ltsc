using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;
using System.Windows.Controls;
using Page = iNKORE.UI.WPF.Modern.Controls.Page;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class PowerPointPage : Page
    {
        private bool _isLoaded = false;
        private DelayAction _sliderDelayAction = new DelayAction();

        public PowerPointPage()
        {
            InitializeComponent();
            Loaded += PowerPointPage_Loaded;
            Unloaded += PowerPointPage_Unloaded;
        }

        private void PowerPointPage_Loaded(object sender, RoutedEventArgs e)
        {
            LoadSettings();
            _isLoaded = true;
            UpdateAllSliderTexts();
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void UpdateAllSliderTexts()
        {
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private void PowerPointPage_Unloaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;
        }

        private void LoadSettings()
        {
            _isLoaded = false;
            var ppt = SettingsManager.Settings.PowerPointSettings;

            CardSupportPowerPoint.IsOn = ppt.PowerPointSupport;
            CardPowerPointEnhancement.IsOn = ppt.EnablePowerPointEnhancement;
            CardSkipAnimationsWhenGoNext.IsOn = ppt.SkipAnimationsWhenGoNext;
            CardSupportWPS.IsOn = ppt.IsSupportWPS;
            CardEnableWppProcessKill.IsOn = ppt.EnableWppProcessKill;



            CardEnablePPTButtonPageClickable.IsOn = ppt.EnablePPTButtonPageClickable;
            CardEnablePPTButtonLongPressPageTurn.IsOn = ppt.EnablePPTButtonLongPressPageTurn;

            CardShowCanvasAtNewSlideShow.IsOn = ppt.IsShowCanvasAtNewSlideShow;
            // LTSC D-06: 智能模式已移除，设置项不再绑定 UI。

            CardEnableTwoFingerGestureInPresentationMode.IsOn = ppt.IsEnableTwoFingerGestureInPresentationMode;
            CardEnableFingerGestureSlideShowControl.IsOn = ppt.IsEnableFingerGestureSlideShowControl;
            CardShowPPTSidebarByDefault.IsOn = ppt.ShowPPTSidebarByDefault;
            CardShowPPTModePrompt.IsOn = ppt.ShowPPTModePrompt;

            CardAutoSaveStrokesInPowerPoint.IsOn = ppt.IsAutoSaveStrokesInPowerPoint;

            CardNotifyPreviousPage.IsOn = ppt.IsNotifyPreviousPage;
            CardAlwaysGoToFirstPageOnReenter.IsOn = ppt.IsAlwaysGoToFirstPageOnReenter;
            CardNotifyHiddenPage.IsOn = ppt.IsNotifyHiddenPage;
            CardNotifyAutoPlayPresentation.IsOn = ppt.IsNotifyAutoPlayPresentation;
            CardKeepStrokeInPowerPoint.IsOn = ppt.IsShowStrokeOnSelectInPowerPoint;

            _isLoaded = true;
        }

        #region PPT Basic

        // LTSC P1：架构选择已移除，PPT 联动统一走 PptBridge（ROT）。
        // 原来的 UpdatePPTArchitectureDependentCards() 会按 PPTLinkMode 隐藏
        // 「增强 / WPS / 结束 WPP 进程」三张卡——老配置里 PPTLinkMode=Rot 的用户
        // 根本看不到增强开关，等于功能被藏了。现在三张卡永远可见。

        private void ToggleSwitchSupportPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            ppt.PowerPointSupport = CardSupportPowerPoint.IsOn;
            if (!ppt.PowerPointSupport && ppt.IsSupportWPS)
            {
                ppt.IsSupportWPS = false;
                CardSupportWPS.IsOn = false;
            }
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTSupportChanged(CardSupportPowerPoint.IsOn);
        }

        private void ToggleSwitchPowerPointEnhancement_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            ppt.EnablePowerPointEnhancement = CardPowerPointEnhancement.IsOn;
            if (ppt.EnablePowerPointEnhancement)
            {
                ppt.IsSupportWPS = false;
                CardSupportWPS.IsOn = false;
            }
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnPPTEnhancementChanged(CardPowerPointEnhancement.IsOn);
        }

        private void ToggleSwitchSkipAnimationsWhenGoNext_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.SkipAnimationsWhenGoNext = CardSkipAnimationsWhenGoNext.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnSkipAnimationsWhenGoNextChanged(CardSkipAnimationsWhenGoNext.IsOn);
        }

        private void ToggleSwitchSupportWPS_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            var ppt = SettingsManager.Settings.PowerPointSettings;
            ppt.IsSupportWPS = CardSupportWPS.IsOn;
            if (ppt.IsSupportWPS)
            {
                if (!ppt.PowerPointSupport)
                {
                    ppt.PowerPointSupport = true;
                    CardSupportPowerPoint.IsOn = true;
                }
                if (ppt.EnablePowerPointEnhancement)
                {
                    ppt.EnablePowerPointEnhancement = false;
                    CardPowerPointEnhancement.IsOn = false;
                }
            }
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnSupportWPSChanged();
        }

        private void ToggleSwitchEnableWppProcessKill_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.EnableWppProcessKill = CardEnableWppProcessKill.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region PPT Flip Buttons

        private void OnCardClicked(object sender, RoutedEventArgs e)
        {
            if (Window.GetWindow(this) is SettingsWindow settingsWindow)
            {
                settingsWindow.NavigateToPage("PPTPageFlipPreviewPage");
            }
        }

        private void ToggleSwitchShowPPTSidebarByDefault_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.ShowPPTSidebarByDefault = CardShowPPTSidebarByDefault.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnShowPPTSidebarByDefaultChanged();
        }

        private void ToggleSwitchShowPPTModePrompt_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.ShowPPTModePrompt = CardShowPPTModePrompt.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnablePPTButtonPageClickable_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.EnablePPTButtonPageClickable = CardEnablePPTButtonPageClickable.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnablePPTButtonLongPressPageTurn_OnToggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn = CardEnablePPTButtonLongPressPageTurn.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion



        #region PPT SlideShow Entry & Gesture

        private void ToggleSwitchShowCanvasAtNewSlideShow_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow = CardShowCanvasAtNewSlideShow.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnableTwoFingerGestureInPresentationMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsEnableTwoFingerGestureInPresentationMode = CardEnableTwoFingerGestureInPresentationMode.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnableFingerGestureSlideShowControl_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsEnableFingerGestureSlideShowControl = CardEnableFingerGestureSlideShowControl.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion

        #region PPT Auto Save & Notifications


        private void ToggleSwitchAutoSaveStrokesInPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint = CardAutoSaveStrokesInPowerPoint.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchNotifyPreviousPage_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsNotifyPreviousPage = CardNotifyPreviousPage.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchAlwaysGoToFirstPageOnReenter_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsAlwaysGoToFirstPageOnReenter = CardAlwaysGoToFirstPageOnReenter.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchNotifyHiddenPage_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsNotifyHiddenPage = CardNotifyHiddenPage.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchNotifyAutoPlayPresentation_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsNotifyAutoPlayPresentation = CardNotifyAutoPlayPresentation.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchKeepStrokeInPowerPoint_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.PowerPointSettings.IsShowStrokeOnSelectInPowerPoint = CardKeepStrokeInPowerPoint.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion
    
        #region Mini Whiteboard

        private void UpdateMiniWhiteboardSizeText()
        {
            if (MiniWhiteboardSizeText == null || MiniWhiteboardWidthSlider == null || MiniWhiteboardHeightSlider == null) return;
            MiniWhiteboardSizeText.Text = $"{(int)MiniWhiteboardWidthSlider.Value} × {(int)MiniWhiteboardHeightSlider.Value}";
        }

        private void UpdateMiniWhiteboardOpacityText()
        {
            if (MiniWhiteboardOpacityText == null || MiniWhiteboardOpacitySlider == null) return;
            MiniWhiteboardOpacityText.Text = $"{Math.Round(MiniWhiteboardOpacitySlider.Value * 100):0}%";
        }


        private void ToggleSwitchMiniWhiteboardEnabled_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.IsEnabled = ToggleSwitchMiniWhiteboardEnabled.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchMiniWhiteboardSyncPPT_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.SyncWithPPTPages = ToggleSwitchMiniWhiteboardSyncPPT.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void MiniWhiteboardWidthSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMiniWhiteboardSizeText();
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.DefaultWidth = MiniWhiteboardWidthSlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        private void MiniWhiteboardHeightSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMiniWhiteboardSizeText();
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.DefaultHeight = MiniWhiteboardHeightSlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        private void MiniWhiteboardOpacitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateMiniWhiteboardOpacityText();
            if (!_isLoaded) return;
            SettingsManager.Settings.MiniWhiteboard ??= new MiniWhiteboardSettings();
            SettingsManager.Settings.MiniWhiteboard.DefaultOpacity = MiniWhiteboardOpacitySlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        #endregion
}
}
