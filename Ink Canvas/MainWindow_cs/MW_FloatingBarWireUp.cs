using Ink_Canvas.Controls;
using Ink_Canvas.Controls.Toolbar.FloatingToolbar;
using Ink_Canvas.Helpers;
using Ink_Canvas.Models;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows;
using Ink_Canvas.Windows.SettingsViews;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using Ink_Canvas.WorkflowAutomation;
using iNKORE.UI.WPF.Modern;
using iNKORE.UI.WPF.Modern.Controls;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
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
using System.Windows.Threading;
using Application = System.Windows.Application;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using Cursor = System.Windows.Input.Cursor;
using Cursors = System.Windows.Input.Cursors;
using DpiChangedEventArgs = System.Windows.DpiChangedEventArgs;
using File = System.IO.File;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Point = System.Windows.Point;
using VerticalAlignment = System.Windows.VerticalAlignment;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        // LTSC（2026-11-17）：从 MainWindow.xaml.cs 拆出。
        // 这 11 个 WireUp* 方法只做一件事：把 XAML 里的 Popup 控件和事件处理绑起来。
        // 它们不参与运行时状态机，是主窗口文件里耦合最低的一块。
        // 只搬方法，不改逻辑。
        private void WireUpToolsPopupContentEvents()
        {
            if (_toolsPopupEventsWired) return;
            _toolsPopupEventsWired = true;

            WireUpSingleToolsPopupContent(BoardToolsPopupContent);
            WireUpSingleToolsPopupContent(MainToolsPopupContent);
        }

        private void WireUpSingleToolsPopupContent(ToolsPopupContent content)
        {
            if (content == null) return;

            if (content.TimerBtn != null)
                content.TimerBtn.ButtonMouseUp += ImageCountdownTimer_MouseUp;
            if (content.SaveBtn != null)
            {
                content.SaveBtn.ButtonMouseDown += Border_MouseDown;
                content.SaveBtn.ButtonMouseUp += SymbolIconSaveStrokes_MouseUp;
            }
            if (content.OpenBtn != null)
            {
                content.OpenBtn.ButtonMouseDown += Border_MouseDown;
                content.OpenBtn.ButtonMouseUp += SymbolIconOpenStrokes_MouseUp;
            }
            if (content.ReplayBtn != null)
                content.ReplayBtn.ButtonMouseUp += GridInkReplayButton_MouseUp;
            if (content.ScreenshotBtn != null)
                content.ScreenshotBtn.ButtonMouseUp += SymbolIconScreenshot_MouseUp;
            if (content.ShapeDrawBtn != null)
                content.ShapeDrawBtn.ButtonMouseUp += ImageDrawShape_MouseUp;
            if (content.RedoBtn != null)
                content.RedoBtn.ButtonMouseUp += SymbolIconRedo_MouseUp;
            if (content.SettingsBtn != null)
                content.SettingsBtn.ButtonMouseUp += SymbolIconSettings_Click;
            content.CloseButtonControl.Click += CloseBordertools_Click;
        }

        private void WireUpBackgroundPaletteEvents()
        {
            if (_backgroundPaletteEventsWired) return;
            _backgroundPaletteEventsWired = true;

            if (BackgroundPalettePopupContent == null) return;

            var content = BackgroundPalettePopupContent;
            content.WhiteboardBtn.MouseUp += WhiteboardModeBtn_MouseUp;
            content.BlackboardBtn.MouseUp += BlackboardModeBtn_MouseUp;
            content.DarkModeBtnControl.MouseUp += DarkModeBtn_MouseUp;
            content.RSlider.ValueChanged += BackgroundRSlider_ValueChanged;
            content.GSlider.ValueChanged += BackgroundGSlider_ValueChanged;
            content.BSlider.ValueChanged += BackgroundBSlider_ValueChanged;
            content.ApplyBtn.Click += ApplyBackgroundColorBtn_Click;
            content.CloseButtonControl.Click += CloseBordertools_Click;
        }

        private void WireUpBoardShapeDrawPopupContentEvents()
        {
            if (_boardShapeDrawPopupEventsWired) return;
            _boardShapeDrawPopupEventsWired = true;

            var content = BoardShapeDrawPopupContent;
            if (content == null) return;

            content.DrawLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawLineBtn.ButtonMouseUp += BtnDrawLine_Click;
            content.DrawDashedLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawDashedLineBtn.ButtonMouseUp += BtnDrawDashedLine_Click;
            content.DrawDotLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawDotLineBtn.ButtonMouseUp += BtnDrawDotLine_Click;
            content.DrawArrowBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawArrowBtn.ButtonMouseUp += BtnDrawArrow_Click;
            content.DrawParallelLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawParallelLineBtn.ButtonMouseUp += BtnDrawParallelLine_Click;
            content.DrawRectangleCenterBtn.ButtonMouseUp += BtnDrawRectangleCenter_Click;
            content.DrawCircleBtn.ButtonMouseUp += BtnDrawCircle_Click;
            content.DrawDashedCircleBtn.ButtonMouseUp += BtnDrawDashedCircle_Click;
            content.DrawEllipseCenterBtn.ButtonMouseUp += BtnDrawCenterEllipse_Click;
            content.DrawEllipseCenterWithFocalPointBtn.ButtonMouseUp += BtnDrawCenterEllipseWithFocalPoint_Click;
            content.DrawCuboidBtn.ButtonMouseUp += BtnDrawCuboid_Click;
            content.DrawRectangleBtn.ButtonMouseUp += BtnDrawRectangle_Click;
            content.DrawCylinderBtn.ButtonMouseUp += BtnDrawCylinder_Click;
            content.DrawConeBtn.ButtonMouseUp += BtnDrawCone_Click;
            content.DrawCoordinate1Btn.ButtonMouseUp += BtnDrawCoordinate1_Click;
            content.DrawCoordinate2Btn.ButtonMouseUp += BtnDrawCoordinate2_Click;
            content.DrawCoordinate3Btn.ButtonMouseUp += BtnDrawCoordinate3_Click;
            content.DrawCoordinate4Btn.ButtonMouseUp += BtnDrawCoordinate4_Click;
            content.DrawCoordinate5Btn.ButtonMouseUp += BtnDrawCoordinate5_Click;
            content.DrawHyperbolaBtn.ButtonMouseUp += BtnDrawHyperbola_Click;
            content.DrawHyperbolaWithFocalPointBtn.ButtonMouseUp += BtnDrawHyperbolaWithFocalPoint_Click;
            content.DrawParabola1Btn.ButtonMouseUp += BtnDrawParabola1_Click;
            content.DrawParabolaWithFocalPointBtn.ButtonMouseUp += BtnDrawParabolaWithFocalPoint_Click;
            content.DrawParabola2Btn.ButtonMouseUp += BtnDrawParabola2_Click;
            content.CloseButtonControl.Click += CloseBordertools_Click;
            content.ShowCircleCenterToggle.Toggled += ToggleSwitchShowCircleCenter_Toggled;
        }

        private bool _penPaletteEventsWired;
        private bool _eraserPopupEventsWired;
        private bool _gesturePopupEventsWired;
        private bool _isUpdatingSliders;

        private void WireUpPenPaletteEvents()
        {
            if (_penPaletteEventsWired) return;
            _penPaletteEventsWired = true;

            WireUpSinglePenPaletteEvents(PenPalettePopupContent);
            WireUpSinglePenPaletteEvents(BoardPenPalettePopupContent);
        }

        private void WireUpSinglePenPaletteEvents(PenPalettePopupContent content)
        {
            if (content == null) return;

            content.PenStyleComboBox.SelectionChanged += ComboBoxPenStyle_SelectionChanged;
            content.NibModeToggle.Toggled += ToggleSwitchEnableNibMode_Toggled;
            content.InkToShapeToggle.Toggled += ToggleSwitchEnableInkToShape_Toggled;
            content.PenWidthSlider.ValueChanged += PenWidthSlider_ValueChanged;
            content.PenAlphaSlider.ValueChanged += PenAlphaSlider_ValueChanged;
            content.LaserPenFadeTimeSlider.ValueChanged += LaserPenFadeTimeSlider_ValueChanged;
            content.LaserPenFadeSpeedSlider.ValueChanged += LaserPenFadeSpeedSlider_ValueChanged;
            content.HighlighterOverlapToggle.Toggled += HighlighterOverlapToggle_Toggled;

            content.TabBar.SelectedIndexChanged += (s, idx) =>
            {
                if (idx == 0) SwitchToDefaultPen(s, null);
                else if (idx == 1) SwitchToHighlighterPen(s, null);
                else if (idx == 2) SwitchToLaserPen(s, null);
            };

            content.DefaultPenColorBlack.ButtonMouseUp += BtnColorBlack_Click;
            content.DefaultPenColorWhite.ButtonMouseUp += BtnColorWhite_Click;
            content.DefaultPenColorRed.ButtonMouseUp += BtnColorRed_Click;
            content.DefaultPenColorYellow.ButtonMouseUp += BtnColorYellow_Click;
            content.DefaultPenColorGreen.ButtonMouseUp += BtnColorGreen_Click;
            content.DefaultPenColorBlue.ButtonMouseUp += BtnColorBlue_Click;
            content.DefaultPenColorPink.ButtonMouseUp += BtnColorPink_Click;
            content.DefaultPenColorTeal.ButtonMouseUp += BtnColorTeal_Click;
            content.DefaultPenColorOrange.ButtonMouseUp += BtnColorOrange_Click;

            content.HighlighterPenColorBlack.ButtonMouseUp += BtnHighlighterColorBlack_Click;
            content.HighlighterPenColorWhite.ButtonMouseUp += BtnHighlighterColorWhite_Click;
            content.HighlighterPenColorRed.ButtonMouseUp += BtnHighlighterColorRed_Click;
            content.HighlighterPenColorYellow.ButtonMouseUp += BtnHighlighterColorYellow_Click;
            content.HighlighterPenColorGreen.ButtonMouseUp += BtnHighlighterColorGreen_Click;
            content.HighlighterPenColorZinc.ButtonMouseUp += BtnHighlighterColorZinc_Click;
            content.HighlighterPenColorBlue.ButtonMouseUp += BtnHighlighterColorBlue_Click;
            content.HighlighterPenPenColorPurple.ButtonMouseUp += BtnHighlighterColorPurple_Click;
            content.HighlighterPenColorTeal.ButtonMouseUp += BtnHighlighterColorTeal_Click;
            content.HighlighterPenColorOrange.ButtonMouseUp += BtnHighlighterColorOrange_Click;

            content.LaserPenColorBlack.ButtonMouseUp += BtnLaserPenColorBlack_Click;
            content.LaserPenColorWhite.ButtonMouseUp += BtnLaserPenColorWhite_Click;
            content.LaserPenColorRed.ButtonMouseUp += BtnLaserPenColorRed_Click;
            content.LaserPenColorYellow.ButtonMouseUp += BtnLaserPenColorYellow_Click;
            content.LaserPenColorGreen.ButtonMouseUp += BtnLaserPenColorGreen_Click;
            content.LaserPenColorBlue.ButtonMouseUp += BtnLaserPenColorBlue_Click;
            content.LaserPenColorPink.ButtonMouseUp += BtnLaserPenColorPink_Click;
            content.LaserPenColorTeal.ButtonMouseUp += BtnLaserPenColorTeal_Click;
            content.LaserPenColorOrange.ButtonMouseUp += BtnLaserPenColorOrange_Click;

            content.ColorThemeSwitch.MouseUp += ColorThemeSwitch_MouseUp;
            content.LaserPenColorThemeSwitch.MouseUp += ColorThemeSwitch_MouseUp;
            content.CloseButtonControl.Click += CloseBordertools_Click;
        }

        private void WireUpEraserPopupContentEvents()
        {
            if (_eraserPopupEventsWired) return;
            _eraserPopupEventsWired = true;

            WireUpSingleEraserPopupContentEvents(EraserPopupContent);
            WireUpSingleEraserPopupContentEvents(BoardEraserPopupContent);
        }

        private void WireUpSingleEraserPopupContentEvents(EraserPopupContent content)
        {
            if (content == null) return;

            content.EraserSizeComboBox.SelectionChanged += ComboBoxEraserSizeFloatingBar_SelectionChanged;
            content.EraserTypeTab.SelectionChanged += EraserTypeTab_SelectionChanged;
            content.ClearInkBtn.Click += EraserPanelSymbolIconDelete_MouseUp;
            content.ClearInkAndHistoryBtn.Click += BoardSymbolIconDeleteInkAndHistories_MouseUp;
            content.CloseButtonControl.Click += CloseBordertools_Click;
        }

        private void WireUpGesturePopupContentEvents()
        {
            if (_gesturePopupEventsWired) return;
            _gesturePopupEventsWired = true;

            WireUpSingleGesturePopupContentEvents(FloatingBarGesturePopupContent);
            WireUpSingleGesturePopupContentEvents(BoardGesturePopupContent);
        }

        private void WireUpSingleGesturePopupContentEvents(GesturePopupContent content)
        {
            if (content == null) return;

            content.MultiTouchToggle.Toggled += ToggleSwitchEnableMultiTouchMode_Toggled;
            content.TwoFingerTranslateToggle.Toggled += ToggleSwitchEnableTwoFingerTranslate_Toggled;
            content.TwoFingerZoomToggle.Toggled += ToggleSwitchEnableTwoFingerZoom_Toggled;
            content.TwoFingerRotationToggle.Toggled += ToggleSwitchEnableTwoFingerRotation_Toggled;
            content.CloseButtonControl.Click += CloseBordertools_Click;
        }

        private bool _imageOptionsPopupEventsWired;

        private void WireUpImageOptionsPopupContentEvents()
        {
            if (_imageOptionsPopupEventsWired) return;
            _imageOptionsPopupEventsWired = true;

            var content = BoardImageOptionsPopupContent;
            if (content == null) return;

            content.ScreenshotOption.MouseUp += ImageOptionScreenshot_MouseUp;
            content.SelectFileOption.MouseUp += ImageOptionSelectFile_MouseUp;
            content.CloseButtonControl.Click += CloseBordertools_Click;
        }

        private bool _shapeDrawPopupEventsWired;
        private bool _boardShapeDrawPopupEventsWired;

        private void WireUpShapeDrawPopupContentEvents()
        {
            if (_shapeDrawPopupEventsWired) return;
            _shapeDrawPopupEventsWired = true;

            var content = ShapeDrawPopupContent;
            if (content == null) return;

            content.DrawLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawLineBtn.ButtonMouseUp += BtnDrawLine_Click;
            content.DrawDashedLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawDashedLineBtn.ButtonMouseUp += BtnDrawDashedLine_Click;
            content.DrawDotLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawDotLineBtn.ButtonMouseUp += BtnDrawDotLine_Click;
            content.DrawArrowBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawArrowBtn.ButtonMouseUp += BtnDrawArrow_Click;
            content.DrawParallelLineBtn.ButtonMouseDown += Image_MouseDown;
            content.DrawParallelLineBtn.ButtonMouseUp += BtnDrawParallelLine_Click;
            content.DrawRectangleCenterBtn.ButtonMouseUp += BtnDrawRectangleCenter_Click;
            content.DrawCircleBtn.ButtonMouseUp += BtnDrawCircle_Click;
            content.DrawDashedCircleBtn.ButtonMouseUp += BtnDrawDashedCircle_Click;
            content.DrawEllipseCenterBtn.ButtonMouseUp += BtnDrawCenterEllipse_Click;
            content.DrawEllipseCenterWithFocalPointBtn.ButtonMouseUp += BtnDrawCenterEllipseWithFocalPoint_Click;
            content.DrawCuboidBtn.ButtonMouseUp += BtnDrawCuboid_Click;
            content.DrawRectangleBtn.ButtonMouseUp += BtnDrawRectangle_Click;
            content.DrawCylinderBtn.ButtonMouseUp += BtnDrawCylinder_Click;
            content.DrawConeBtn.ButtonMouseUp += BtnDrawCone_Click;
            content.DrawCoordinate1Btn.ButtonMouseUp += BtnDrawCoordinate1_Click;
            content.DrawCoordinate2Btn.ButtonMouseUp += BtnDrawCoordinate2_Click;
            content.DrawCoordinate3Btn.ButtonMouseUp += BtnDrawCoordinate3_Click;
            content.DrawCoordinate4Btn.ButtonMouseUp += BtnDrawCoordinate4_Click;
            content.DrawCoordinate5Btn.ButtonMouseUp += BtnDrawCoordinate5_Click;
            content.DrawHyperbolaBtn.ButtonMouseUp += BtnDrawHyperbola_Click;
            content.DrawHyperbolaWithFocalPointBtn.ButtonMouseUp += BtnDrawHyperbolaWithFocalPoint_Click;
            content.DrawParabola1Btn.ButtonMouseUp += BtnDrawParabola1_Click;
            content.DrawParabolaWithFocalPointBtn.ButtonMouseUp += BtnDrawParabolaWithFocalPoint_Click;
            content.DrawParabola2Btn.ButtonMouseUp += BtnDrawParabola2_Click;
            content.CloseButtonControl.Click += CloseBordertools_Click;
            content.ShowCircleCenterToggle.Toggled += ToggleSwitchShowCircleCenter_Toggled;
        }
    }
}
