using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using Microsoft.Win32;
using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;

namespace Ink_Canvas.Windows.SettingsViews.Pages
{
    public partial class CanvasPage : iNKORE.UI.WPF.Modern.Controls.Page
    {
        private bool _isLoaded = false;

        public CanvasPage()
        {
            InitializeComponent();
            Loaded += CanvasPage_Loaded;
        }

        private void CanvasPage_Loaded(object sender, RoutedEventArgs e)
        {
            _isLoaded = false;

            try
            {
                var settings = SettingsManager.Settings;

                if (settings.Canvas != null)
                {
                    CardEnablePressureTouchMode.IsOn = settings.Canvas.EnablePressureTouchMode;
                    CardDisablePressure.IsOn = settings.Canvas.DisablePressure;

                    int curveMode = 0;
                    if (settings.Canvas.UseAdvancedBezierSmoothing) curveMode = 2;
                    else if (settings.Canvas.FitToCurve) curveMode = 1;
                    ComboBoxCurveSmoothingMode.SelectedIndex = curveMode;

                    BrushAutoRestoreTimesTextBox.Text = settings.Canvas.BrushAutoRestoreTimes ?? string.Empty;
                    LoadBrushAutoRestoreColor(settings.Canvas.BrushAutoRestoreColor);

                    // 加载画笔光标类型设置
                    ComboBoxPenCursorType.SelectedIndex = settings.Canvas.PenCursorType;
                    CustomPenCursorPathText.Text = settings.Canvas.CustomPenCursorPath ?? string.Empty;
                    UpdateCustomPenCursorPathVisibility();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载画板设置时出错: {ex.Message}");
            }

            LoadInkRecognitionSettings();
            UpdateInkRecognitionSliderTexts();

            _isLoaded = true;
            SliderTouchHelper.AddTouchSupportToAllSliders(this);
        }

        private void LoadBrushAutoRestoreColor(string hex)
        {
            try
            {
                foreach (var item in ComboBoxBrushAutoRestoreColor.Items)
                {
                    if (item is ComboBoxItem cbi && cbi.Tag != null &&
                        string.Equals(cbi.Tag.ToString(), hex, StringComparison.OrdinalIgnoreCase))
                    {
                        ComboBoxBrushAutoRestoreColor.SelectedItem = cbi;
                        return;
                    }
                }
                ComboBoxBrushAutoRestoreColor.SelectedIndex = 0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载画笔恢复颜色时出错: {ex.Message}");
            }
        }

        private void ToggleSwitchEnablePressureTouchMode_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.EnablePressureTouchMode = CardEnablePressureTouchMode.IsOn;
            SettingsActionHub.OnEnablePressureTouchModeChanged(CardEnablePressureTouchMode.IsOn);
            if (!CardEnablePressureTouchMode.IsOn || !SettingsManager.Settings.Canvas.DisablePressure)
                CardDisablePressure.IsOn = SettingsManager.Settings.Canvas.DisablePressure;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchDisablePressure_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.DisablePressure = CardDisablePressure.IsOn;
            SettingsActionHub.OnDisablePressureChanged(CardDisablePressure.IsOn);
            if (!CardDisablePressure.IsOn || !SettingsManager.Settings.Canvas.EnablePressureTouchMode)
                CardEnablePressureTouchMode.IsOn = SettingsManager.Settings.Canvas.EnablePressureTouchMode;
            SettingsManager.SaveSettingsToFile();
        }

        private void ComboBoxCurveSmoothingMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            var item = ComboBoxCurveSmoothingMode?.SelectedItem as ComboBoxItem;
            if (item == null) return;
            var tag = item.Tag?.ToString() ?? "0";
            switch (tag)
            {
                case "1":
                    SettingsManager.Settings.Canvas.FitToCurve = true;
                    SettingsManager.Settings.Canvas.UseAdvancedBezierSmoothing = false;
                    break;
                case "2":
                    SettingsManager.Settings.Canvas.FitToCurve = false;
                    SettingsManager.Settings.Canvas.UseAdvancedBezierSmoothing = true;
                    break;
                default:
                    SettingsManager.Settings.Canvas.FitToCurve = false;
                    SettingsManager.Settings.Canvas.UseAdvancedBezierSmoothing = false;
                    break;
            }
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnCurveSmoothingModeChanged(
                SettingsManager.Settings.Canvas.FitToCurve,
                SettingsManager.Settings.Canvas.UseAdvancedBezierSmoothing);
        }

        private void BrushAutoRestoreTimesTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.BrushAutoRestoreTimes = BrushAutoRestoreTimesTextBox.Text ?? string.Empty;
            SettingsManager.SaveSettingsToFile();
        }

        private void ComboBoxBrushAutoRestoreColor_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            if (ComboBoxBrushAutoRestoreColor.SelectedItem is ComboBoxItem item)
            {
                string hex = item.Tag as string ?? string.Empty;
                SettingsManager.Settings.Canvas.BrushAutoRestoreColor = hex;
                SettingsManager.SaveSettingsToFile();
            }
        }

        private void ComboBoxPenCursorType_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded) return;
            UpdateCustomPenCursorPathVisibility();
        }

        private void UpdateCustomPenCursorPathVisibility()
        {
            if (CardCustomPenCursorPath == null) return;
            CardCustomPenCursorPath.Visibility =
                ComboBoxPenCursorType.SelectedIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SelectCustomPenCursor_Click(object sender, RoutedEventArgs e)
        {
            var filter = CanvasStrings.Canvas_CustomPenCursorFilter;
            if (string.IsNullOrWhiteSpace(filter))
                filter = "Cursor files|*.cur;*.ani";

            var dialog = new OpenFileDialog
            {
                Filter = filter,
                Title = CanvasStrings.Canvas_SelectCustomPenCursor
            };

            if (dialog.ShowDialog() == true)
            {
                var path = dialog.FileName;
                SettingsManager.Settings.Canvas.CustomPenCursorPath = path;
                CustomPenCursorPathText.Text = path;
                SettingsManager.SaveSettingsToFile();
                SettingsActionHub.OnCustomPenCursorPathChanged();
            }
        }
        private void UpdateInkRecognitionSliderTexts()
        {
            UpdateSliderText(AutoStraightenLineThresholdSlider, AutoStraightenLineThresholdText, "{0:0}");
            UpdateSliderText(LineStraightenSensitivitySlider, LineStraightenSensitivityText, "{0:F2}");
            UpdateSliderText(PauseStraightenDelaySlider, PauseStraightenDelayText, "{0:0} ms");
            UpdateSliderText(LineEndpointSnappingThresholdSlider, LineEndpointSnappingThresholdText, "{0:0}");
        }

        private void UpdateSliderText(Slider slider, TextBlock textBlock, string format)
        {
            if (slider == null || textBlock == null) return;
            textBlock.Text = string.Format(format, slider.Value);
        }

        private void LoadInkRecognitionSettings()
        {
            try
            {
                var settings = SettingsManager.Settings;

                if (settings.InkToShape != null)
                {
                    CardEnableInkToShape.IsOn = settings.InkToShape.IsInkToShapeEnabled;
                    int eng = settings.InkToShape.ShapeRecognitionEngine;
                    if (eng < 0) eng = 0;
                    if (eng > 2) eng = 2;
                    ComboBoxShapeRecognitionEngine.SelectedIndex = eng;
                    CardEnableWinRtHandwritingStrokeBeautify.IsOn = settings.InkToShape.EnableWinRtHandwritingStrokeBeautify;
                    SelectHandwritingFontByValue(settings.InkToShape.HandwritingCorrectionFontFamily);
                    CardEnableInkToShapeNoFakePressureRectangle.IsOn = settings.InkToShape.IsInkToShapeNoFakePressureRectangle;
                    CardEnableInkToShapeNoFakePressureTriangle.IsOn = settings.InkToShape.IsInkToShapeNoFakePressureTriangle;
                    ToggleCheckboxEnableInkToShapeTriangle.IsChecked = settings.InkToShape.IsInkToShapeTriangle;
                    ToggleCheckboxEnableInkToShapeRectangle.IsChecked = settings.InkToShape.IsInkToShapeRectangle;
                    ToggleCheckboxEnableInkToShapeRounded.IsChecked = settings.InkToShape.IsInkToShapeRounded;
                    LineStraightenSensitivitySlider.Value = settings.InkToShape.LineStraightenSensitivity;
                }

                if (settings.Canvas != null)
                {
                    ToggleSwitchAutoStraightenLine.IsOn = settings.Canvas.AutoStraightenLine;
                    AutoStraightenLineThresholdSlider.Value = settings.Canvas.AutoStraightenLineThreshold;
                    ToggleSwitchHighPrecisionLineStraighten.IsOn = settings.Canvas.HighPrecisionLineStraighten;
                    ToggleSwitchPauseStraightenLine.IsOn = settings.Canvas.PauseStraightenLine;
                    PauseStraightenDelaySlider.Value = settings.Canvas.PauseStraightenDelay;
                    ToggleSwitchLineEndpointSnapping.IsOn = settings.Canvas.LineEndpointSnapping;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"加载墨迹纠正设置时出错: {ex.Message}");
            }

            ExpanderAutoStraightenLine.IsExpanded = ToggleSwitchAutoStraightenLine.IsOn;
            ExpanderLineEndpointSnapping.IsExpanded = ToggleSwitchLineEndpointSnapping.IsOn;
        }

        private void ToggleSwitchEnableInkToShape_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.InkToShape.IsInkToShapeEnabled = CardEnableInkToShape.IsOn;
            SettingsManager.SaveSettingsToFile();
            SettingsActionHub.OnInkToShapeEnabledChanged(CardEnableInkToShape.IsOn);
        }

        private void ComboBoxShapeRecognitionEngine_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ComboBoxShapeRecognitionEngine == null) return;
            int idx = ComboBoxShapeRecognitionEngine.SelectedIndex;
            if (idx < 0) idx = 0;
            if (idx > 2) idx = 2;
            SettingsManager.Settings.InkToShape.ShapeRecognitionEngine = idx;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnableWinRtHandwritingStrokeBeautify_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.InkToShape.EnableWinRtHandwritingStrokeBeautify = CardEnableWinRtHandwritingStrokeBeautify.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void SelectHandwritingFontByValue(string value)
        {
            if (ComboBoxHandwritingCorrectionFont == null) return;
            value = (value ?? string.Empty).Trim();
            int matchIndex = -1;
            for (int i = 0; i < ComboBoxHandwritingCorrectionFont.Items.Count; i++)
            {
                var item = ComboBoxHandwritingCorrectionFont.Items[i] as ComboBoxItem;
                var tag = item?.Tag as string;
                if (!string.IsNullOrEmpty(tag) && string.Equals(tag, value, StringComparison.OrdinalIgnoreCase))
                {
                    matchIndex = i;
                    break;
                }
            }
            ComboBoxHandwritingCorrectionFont.SelectedIndex = matchIndex >= 0 ? matchIndex : 0;
        }

        private void ComboBoxHandwritingCorrectionFont_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (!_isLoaded || ComboBoxHandwritingCorrectionFont == null) return;
            var item = ComboBoxHandwritingCorrectionFont.SelectedItem as ComboBoxItem;
            var tag = item?.Tag as string;
            if (string.IsNullOrWhiteSpace(tag)) return;
            SettingsManager.Settings.InkToShape.HandwritingCorrectionFontFamily = tag;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnableInkToShapeNoFakePressureRectangle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.InkToShape.IsInkToShapeNoFakePressureRectangle = CardEnableInkToShapeNoFakePressureRectangle.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchEnableInkToShapeNoFakePressureTriangle_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.InkToShape.IsInkToShapeNoFakePressureTriangle = CardEnableInkToShapeNoFakePressureTriangle.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleCheckboxEnableInkToShapeTriangle_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.InkToShape.IsInkToShapeTriangle = (bool)ToggleCheckboxEnableInkToShapeTriangle.IsChecked;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleCheckboxEnableInkToShapeRectangle_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.InkToShape.IsInkToShapeRectangle = (bool)ToggleCheckboxEnableInkToShapeRectangle.IsChecked;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleCheckboxEnableInkToShapeRounded_CheckedChanged(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.InkToShape.IsInkToShapeRounded = (bool)ToggleCheckboxEnableInkToShapeRounded.IsChecked;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchAutoStraightenLine_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.AutoStraightenLine = ToggleSwitchAutoStraightenLine.IsOn;
            ExpanderAutoStraightenLine.IsExpanded = ToggleSwitchAutoStraightenLine.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void AutoStraightenLineThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(AutoStraightenLineThresholdSlider, AutoStraightenLineThresholdText, "{0:0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.AutoStraightenLineThreshold = (int)e.NewValue;
            SettingsManager.SaveSettingsToFile();
        }

        private void LineStraightenSensitivitySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(LineStraightenSensitivitySlider, LineStraightenSensitivityText, "{0:F2}");
            if (!_isLoaded) return;
            var val = Math.Round(LineStraightenSensitivitySlider.Value, 2);
            LineStraightenSensitivitySlider.Value = val;
            SettingsManager.Settings.InkToShape.LineStraightenSensitivity = val;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchHighPrecisionLineStraighten_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.HighPrecisionLineStraighten = ToggleSwitchHighPrecisionLineStraighten.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchPauseStraightenLine_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.PauseStraightenLine = ToggleSwitchPauseStraightenLine.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void PauseStraightenDelaySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(PauseStraightenDelaySlider, PauseStraightenDelayText, "{0:0} ms");
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.PauseStraightenDelay = (int)PauseStraightenDelaySlider.Value;
            SettingsManager.SaveSettingsToFile();
        }

        private void ToggleSwitchLineEndpointSnapping_Toggled(object sender, RoutedEventArgs e)
        {
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.LineEndpointSnapping = ToggleSwitchLineEndpointSnapping.IsOn;
            ExpanderLineEndpointSnapping.IsExpanded = ToggleSwitchLineEndpointSnapping.IsOn;
            SettingsManager.SaveSettingsToFile();
        }

        private void LineEndpointSnappingThresholdSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSliderText(LineEndpointSnappingThresholdSlider, LineEndpointSnappingThresholdText, "{0:0}");
            if (!_isLoaded) return;
            SettingsManager.Settings.Canvas.LineEndpointSnappingThreshold = (int)e.NewValue;
            SettingsManager.SaveSettingsToFile();
        }
    }
}
