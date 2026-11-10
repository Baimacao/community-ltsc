using Ink_Canvas.Helpers;
using System;
using System.Windows;

namespace Ink_Canvas
{
    public partial class MainWindow
    {
        // LTSC D-06: 「智能模式」（视频区域鼠标穿透）已移除。
        //
        // 原实现（MW_WindowChromeHitTest.cs 前 ~242 行 + MW_PPT.cs 的
        // RefreshSmartModeRegions / GetVideoRegionsViaCom）在 PPT 放映时
        // 通过 COM/VSTO 取当前页视频控件的坐标，在鼠标进入该区域时临时切换
        // 到鼠标模式、退出时切回批注模式，并用一个 16ms 的 DispatcherTimer 轮询。
        //
        // 移除原因（所有者裁决「此功能十分不稳定」）：
        //   1. 依赖 PPT COM 实时读取 slide 上的 shape 坐标，三条 fallback 路径
        //      （COM → _pptManager → 硬编码 750x421.875）说明坐标来源不可靠；
        //   2. 每次开关都调用 PenIcon_Click / CursorIcon_Click，会真实改变
        //      全局绘图模式，一旦坐标判断错误，老师会突然写不了字；
        //   3. 16ms 轮询在放映期间常驻。
        //
        // 保留部分：ShouldHandleWindowChromeHitTest / ContainsPoint —— 它们是
        // WindowChrome 渲染路径的可选框判定，与智能模式无关，删了会让窗口
        // 在 WindowChrome 模式下整个不可点。

        protected override bool ShouldHandleWindowChromeHitTest(Point windowPoint)
        {
            return ContainsPoint(ViewboxFloatingBar, windowPoint)
                   || ContainsPoint(LeftSidePanel, windowPoint)
                   || ContainsPoint(RightSidePanel, windowPoint)
                   || ContainsPoint(LeftUnFoldButtonQuickPanel, windowPoint)
                   || ContainsPoint(RightUnFoldButtonQuickPanel, windowPoint)
                   || ContainsPoint(LeftBottomPanelForPPTNavigation, windowPoint)
                   || ContainsPoint(RightBottomPanelForPPTNavigation, windowPoint)
                   || ContainsPoint(LeftSidePanelForPPTNavigation, windowPoint)
                   || ContainsPoint(RightSidePanelForPPTNavigation, windowPoint)
                   || ContainsPoint(ViewboxBlackboardLeftSide, windowPoint)
                   || ContainsPoint(BlackboardCenterSide, windowPoint)
                   || ContainsPoint(ViewboxBlackboardRightSide, windowPoint)
                   || ContainsPoint(BorderStrokeSelectionControl, windowPoint)
                   || ContainsPoint(BorderImageSelectionControl, windowPoint)
                   || ContainsPoint(BorderPdfPageSidebar, windowPoint)
                   || ContainsPoint(ImageSelectionOverlay, windowPoint)
                   || ContainsPoint(QuickDrawFloatingButton, windowPoint)
                   || ContainsPoint(BorderInkReplayToolBox, windowPoint)
                   || ContainsPoint(PPTTimeCapsuleContainer, windowPoint)
                   || ContainsPoint(PPTQuickPanelContainer, windowPoint)
                   || ContainsPoint(VideoPresenterSidebar, windowPoint);
        }

        private bool ContainsPoint(FrameworkElement element, Point windowPoint)
        {
            if (element == null || !element.IsVisible || !element.IsHitTestVisible)
                return false;

            if (element.ActualWidth <= 0 || element.ActualHeight <= 0)
                return false;

            try
            {
                var topLeft = element.TransformToAncestor(this).Transform(new Point(0, 0));
                var bounds = new Rect(topLeft, new Size(element.ActualWidth, element.ActualHeight));
                return bounds.Contains(windowPoint);
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }
    }
}
