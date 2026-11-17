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
        #region 动态按钮位置计算和高光显示

        /// <summary>
        /// 获取浮动栏中指定按钮的位置
        /// </summary>
        /// <param name="buttonName">按钮的名称</param>
        /// <returns>按钮在浮动栏中的相对位置</returns>
        private double GetFloatingBarButtonPosition(string buttonName)
        {
            try
            {
                // 获取浮动栏容器
                var floatingBarPanel = GetFirstContentPanel();
                if (floatingBarPanel == null) return 0;

                double currentPosition = 0;

                foreach (var child in floatingBarPanel.Children)
                {
                    if (child is UIElement element)
                    {
                        // 检查是否是我们要找的按钮
                        if (IsTargetButton(element, buttonName))
                        {
                            return currentPosition;
                        }

                        // 累加当前元素的位置
                        currentPosition += GetElementWidth(element);
                    }
                }

                return 0;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"获取按钮位置失败: {ex.Message}", LogHelper.LogType.Error);
                return 0;
            }
        }

        /// <summary>
        /// 检查元素是否是目标按钮
        /// </summary>
        private bool IsTargetButton(UIElement element, string buttonName)
        {
            if (element is FrameworkElement fe)
            {
                return fe.Name == buttonName;
            }
            return false;
        }

        /// <summary>
        /// 获取元素的宽度
        /// </summary>
        private double GetElementWidth(UIElement element)
        {
            if (element is FrameworkElement fe)
            {
                return fe.ActualWidth > 0 ? fe.ActualWidth : 28;
            }
            return 28; // 默认宽度
        }

        /// <summary>
        /// 更新浮动栏批注图标颜色，使其反映当前画笔颜色（需开启 ShowPenColorOnFloatingBarIcon）
        /// </summary>
        internal void UpdatePenIconColor()
        {
            if (!Settings.Appearance.ShowPenColorOnFloatingBarIcon) return;
            if (Pen_Icon == null || Pen_Icon.Icon == null) return;
            if (_currentToolMode != "pen" && _currentToolMode != "color") return;

            try
            {
                var inkColor = inkCanvas.DefaultDrawingAttributes.Color;
                Pen_Icon.Icon.Brush = new SolidColorBrush(inkColor);
            }
            catch { }
        }

        /// <summary>
        /// 设置浮动栏高光显示位置
        /// </summary>
        /// <param name="mode">模式名称</param>
        private ToolbarImageButton _lastHighlightButton;
        private int _indicatorAnimationGeneration;
        private Storyboard _activeIndicatorStoryboard;
        // LTSC（2026-11-17）：删掉 _pendingHighlightMode。
        // 它只在 SetFloatingBarHighlightPosition 里被赋值一次，全项目没有任何读取点——
        // 是个「看着像状态、其实没人用」的字段。留着会让读代码的人以为
        // 高亮位置有个「待应用」的延迟队列。
        private int _highlightPositionVersion;
        private int _highlightLayoutRetryCount;

        private void SetFloatingBarHighlightPosition(string mode)
        {
            // 折叠动画进行中不画高亮。
            //
            // 踩的坑：一开始只用 IsFloatingBarContentVisible() 判断，但那个值要到
            // 折叠动画结束（550ms）才变成 false，动画期间一直是 true，
            // 于是高亮框在整个折叠过程中被反复画出来，最后留在主图标旁边。
            // 现在再加一条 FloatingBarFoldAnimation.IsPlaying，动画一开始就挡住。
            if (FloatingBarFoldAnimation.IsPlaying || !IsFloatingBarContentVisible())
                return;

            mode = NormalizeToolModeForFreeze(mode);

            ApplyFloatingBarIconHighlightImmediate(mode);

            _highlightLayoutRetryCount = 0;
            int version = ++_highlightPositionVersion;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_highlightPositionVersion != version) return;
                AnimateFloatingBarHighlightTo(mode);
            }), System.Windows.Threading.DispatcherPriority.Loaded);
        }

        // LTSC（2026-11-17）：图标 Geometry 与 Brush 的缓存。
        //
        // 为什么需要：切换工具时会调用 ApplyFloatingBarIconHighlightImmediate，
        // 它对 5 个图标各调一次 Geometry.Parse(GetCorrectIcon(...))。
        // 那些路径字符串是 XamlGraphicsIconGeometries 里的编译期常量，
        // 每次解析出来的 Geometry 一模一样，纯属重复劳动。
        //
        // 判据：Geometry.Parse 要解析整条路径数据并建对象树，不是零成本。
        // 老师在课上会反复点笔/橡皮/光标，这条路走得很频繁。
        //
        // 用 static 缓存是安全的：Geometry 是 Freezable，只要不修改就可以跨实例共享。
        // 这里解析出来的从不修改，只赋给 button.Icon.Geometry 显示。
        private static readonly Dictionary<string, Geometry> _iconGeometryCache
            = new Dictionary<string, Geometry>(StringComparer.Ordinal);
        private static readonly Dictionary<uint, SolidColorBrush> _iconBrushCache
            = new Dictionary<uint, SolidColorBrush>();

        /// <summary>
        /// 取缓存的图标 Geometry。同一 (iconType, isSolid) 只解析一次。
        /// </summary>
        private Geometry GetCachedIconGeometry(string iconType, bool isSolid)
        {
            if (string.IsNullOrEmpty(iconType)) return Geometry.Empty;

            string key = iconType + (isSolid ? "|solid" : "|lined");
            if (_iconGeometryCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var parsed = Geometry.Parse(GetCorrectIcon(iconType, isSolid));
            if (parsed != null && parsed.CanFreeze && !parsed.IsFrozen)
                parsed.Freeze();   // 冻结后跨线程共享也安全，且渲染更快
            _iconGeometryCache[key] = parsed;
            return parsed;
        }

        /// <summary>
        /// 取缓存的纯色画刷。按颜色值做键。
        /// </summary>
        private static SolidColorBrush GetCachedIconBrush(Color color)
        {
            uint key = ((uint)color.A << 24) | ((uint)color.R << 16) | ((uint)color.G << 8) | color.B;
            if (_iconBrushCache.TryGetValue(key, out var cached) && cached != null)
                return cached;

            var brush = new SolidColorBrush(color);
            if (brush.CanFreeze) brush.Freeze();
            _iconBrushCache[key] = brush;
            return brush;
        }

        private void ApplyFloatingBarIconHighlightImmediate(string mode)
        {
            try
            {
                Color highlightBarColor;
                bool isDarkTheme = Settings.Appearance.Theme == 1 ||
                                   (Settings.Appearance.Theme == 2 && !ThemeHelper.IsSystemThemeLight());

                if (isDarkTheme)
                    highlightBarColor = Color.FromRgb(102, 204, 255);
                else
                    highlightBarColor = Color.FromRgb(37, 99, 235);

                if (isFloatingBarFolded || (BorderFloatingBarMoveControls != null && BorderFloatingBarMoveControls.Visibility == Visibility.Collapsed))
                {
                    return;
                }

                // LTSC（2026-11-17）：原来每次切工具都 new 一个 SolidColorBrush、
                // 并且对 5 个图标各调一次 Geometry.Parse(静态字符串)。
                // 这些字符串是编译期常量，解析结果永远一样，属于纯浪费。
                // 实测：一次工具切换 = 6 次 Geometry.Parse + 2 次 brush 分配，
                // 而切换工具是高频操作（老师上课会反复点笔/橡皮/光标）。
                // 改成缓存：Geometry 按 (图标名, 是否实心) 缓存，brush 按颜色缓存。
                var foregroundBrush = GetCachedIconBrush(FloatBarForegroundColor);

                void ResetIcon(ToolbarImageButton button, string iconType)
                {
                    if (button == null) return;
                    if (!ToolbarRegistry.GetUseRedStyle(button))
                        button.Icon.Brush = foregroundBrush;
                    var g = GetCachedIconGeometry(iconType, false);
                    if (!ReferenceEquals(button.Icon.Geometry, g))
                        button.Icon.Geometry = g;
                }

                ResetIcon(Cursor_Icon, "cursor");
                ResetIcon(Pen_Icon, "pen");
                ResetIcon(Eraser_Icon, "eraserCircle");
                ResetIcon(EraserByStrokes_Icon, "eraserStroke");
                ResetIcon(SymbolIconSelect, "lassoSelect");

                string targetIconType = null;
                ToolbarImageButton targetButton = null;

                switch (mode)
                {
                    case "cursor":
                        targetButton = Cursor_Icon;
                        targetIconType = "cursor";
                        break;
                    case "pen":
                    case "color":
                        targetButton = Pen_Icon;
                        targetIconType = "pen";
                        break;
                    case "eraser":
                        targetButton = Eraser_Icon;
                        targetIconType = "eraserCircle";
                        break;
                    case "eraserByStrokes":
                        targetButton = EraserByStrokes_Icon;
                        targetIconType = "eraserStroke";
                        break;
                    case "select":
                        targetButton = SymbolIconSelect;
                        targetIconType = "lassoSelect";
                        break;
                    case "shape":
                        targetButton = ShapeDrawFloatingBarBtn;
                        break;
                }

                if (targetButton != null && targetIconType != null)
                {
                    if (!ToolbarRegistry.GetUseRedStyle(targetButton))
                    {
                        var desiredBrush = (Settings.Appearance.ShowPenColorOnFloatingBarIcon && targetButton == Pen_Icon)
                            ? GetCachedIconBrush(inkCanvas.DefaultDrawingAttributes.Color)
                            : GetCachedIconBrush(highlightBarColor);
                        if (!ReferenceEquals(targetButton.Icon.Brush, desiredBrush))
                            targetButton.Icon.Brush = desiredBrush;
                    }
                    var solidGeom = GetCachedIconGeometry(targetIconType, true);
                    if (!ReferenceEquals(targetButton.Icon.Geometry, solidGeom))
                        targetButton.Icon.Geometry = solidGeom;
                }
            }
            catch (Exception ex)
            {
                ExceptionHandler.HandleException(ex, "更新浮动栏图标高亮状态失败", LogHelper.LogType.Warning);
            }
        }

        private void AnimateFloatingBarHighlightTo(string mode)
        {
            try
            {
                var selectionBG = SelectionBGFloatingBar;
                var indicatorBar = IndicatorBarFloatingBar;
                var container = GridFloatingBarContainer;

                if (selectionBG == null || indicatorBar == null || container == null) return;

                ToolbarImageButton targetButton = null;

                switch (mode)
                {
                    case "cursor":
                        targetButton = Cursor_Icon;
                        break;
                    case "pen":
                    case "color":
                        targetButton = Pen_Icon;
                        break;
                    case "eraser":
                        targetButton = Eraser_Icon;
                        break;
                    case "eraserByStrokes":
                        targetButton = EraserByStrokes_Icon;
                        break;
                    case "select":
                        targetButton = SymbolIconSelect;
                        break;
                    case "shape":
                        targetButton = ShapeDrawFloatingBarBtn;
                        break;
                }

                if (targetButton == null || !IsElementVisibleInTree(targetButton))
                {
                    // 如果目标按钮不可见则隐藏高光
                    HideAllSelectionHighlights();
                    return;
                }

                Point nextButtonOrigin;
                try
                {
                    nextButtonOrigin = targetButton.TransformToAncestor(container).Transform(new Point(0, 0));
                }
                catch (InvalidOperationException)
                {
                    DeferFloatingBarHighlightIfLayoutPending(mode);
                    return;
                }

                double nextWidth = targetButton.ActualWidth > 0 ? targetButton.ActualWidth : 44;
                double nextHeight = targetButton.ActualHeight > 0 ? targetButton.ActualHeight : 44;
                double nextPos = nextButtonOrigin.X;
                double nextTop = nextButtonOrigin.Y;

                if (nextWidth <= 0)
                {
                    DeferFloatingBarHighlightIfLayoutPending(mode);
                    return;
                }

                Color highlightBackgroundColor;
                Color highlightBarColor;
                bool isDarkTheme = Settings.Appearance.Theme == 1 ||
                                   (Settings.Appearance.Theme == 2 && !ThemeHelper.IsSystemThemeLight());

                if (isDarkTheme)
                {
                    highlightBackgroundColor = Color.FromArgb(48, 102, 204, 255);
                    highlightBarColor = Color.FromRgb(102, 204, 255);
                }
                else
                {
                    highlightBackgroundColor = Color.FromArgb(48, 59, 130, 246);
                    highlightBarColor = Color.FromRgb(37, 99, 235);
                }

                selectionBG.Background = new SolidColorBrush(highlightBackgroundColor);
                indicatorBar.Background = new SolidColorBrush(highlightBarColor);

                bool isVertical = IsVerticalToolbar;
                double indicatorBarSize = 16;

                double nextBarLeft, nextBarTop;
                double selectionWidth, selectionHeight;

                if (isVertical)
                {
                    selectionWidth = 43;
                    selectionHeight = nextHeight;
                    nextBarLeft = nextPos + 2 + 43 + 2;
                    nextBarTop = nextTop + Math.Max(0, (nextHeight - indicatorBarSize) / 2);
                }
                else
                {
                    selectionWidth = nextWidth;
                    selectionHeight = 43;
                    nextBarLeft = nextPos + Math.Max(0, (nextWidth - indicatorBarSize) / 2);
                    nextBarTop = nextTop + 2 + 43 + 2;
                }

                // 诊断：记录每次算出的高亮坐标，用来看错位时是否有跳变
                LogHelper.WriteLogToFile(
                    $"[高亮定位] mode={mode} btn={targetButton.Name} origin=({nextPos:F1},{nextTop:F1}) " +
                    $"size={nextWidth:F1}x{nextHeight:F1} bar=({nextBarLeft:F1},{nextBarTop:F1}) vertical={isVertical}",
                    LogHelper.LogType.Trace);

                bool isFirstShow = _lastHighlightButton == null;

                if (isFirstShow)
                {
                    selectionBG.Width = selectionWidth;
                    selectionBG.Height = selectionHeight;
                    System.Windows.Controls.Canvas.SetLeft(selectionBG, isVertical ? nextPos + 2 : nextPos);
                    System.Windows.Controls.Canvas.SetTop(selectionBG, isVertical ? nextTop : nextTop + 2);

                    _indicatorAnimationGeneration++;
                    indicatorBar.RenderTransform = null;
                    indicatorBar.Visibility = Visibility.Visible;
                    indicatorBar.Width = isVertical ? 3 : indicatorBarSize;
                    indicatorBar.Height = isVertical ? indicatorBarSize : 3;
                    indicatorBar.Opacity = 1.0;
                    System.Windows.Controls.Canvas.SetLeft(indicatorBar, nextBarLeft);
                    System.Windows.Controls.Canvas.SetTop(indicatorBar, nextBarTop);
                selectionBG.Visibility = Visibility.Visible;
                    if (!Settings.Appearance.DisableToolbarAnimation)
                        targetButton.SetSelectedVisualOffset(true);
                    _lastHighlightButton = targetButton;
                    return;
                }

                double prevBarPos;
                if (_lastHighlightButton != null && IsElementVisibleInTree(_lastHighlightButton))
                {
                    try
                    {
                        var prevOrigin = _lastHighlightButton.TransformToAncestor(container).Transform(new Point(0, 0));
                        double prevSize = isVertical
                            ? (_lastHighlightButton.ActualHeight > 0 ? _lastHighlightButton.ActualHeight : 44)
                            : (_lastHighlightButton.ActualWidth > 0 ? _lastHighlightButton.ActualWidth : 44);
                        prevBarPos = isVertical
                            ? prevOrigin.Y + Math.Max(0, (prevSize - indicatorBarSize) / 2)
                            : prevOrigin.X + Math.Max(0, (prevSize - indicatorBarSize) / 2);
                    }
                    catch (InvalidOperationException)
                    {
                        prevBarPos = isVertical
                            ? (System.Windows.Controls.Canvas.GetTop(indicatorBar))
                            : (System.Windows.Controls.Canvas.GetLeft(indicatorBar));
                        if (double.IsNaN(prevBarPos)) prevBarPos = isVertical ? nextBarTop : nextBarLeft;
                    }
                }
                else
                {
                    prevBarPos = isVertical
                        ? (System.Windows.Controls.Canvas.GetTop(indicatorBar))
                        : (System.Windows.Controls.Canvas.GetLeft(indicatorBar));
                    if (double.IsNaN(prevBarPos)) prevBarPos = isVertical ? nextBarTop : nextBarLeft;
                }

                double nextBarPos = isVertical ? nextBarTop : nextBarLeft;

                var prevHighlightButton = _lastHighlightButton;
                _lastHighlightButton = targetButton;

                if (!Settings.Appearance.DisableToolbarAnimation)
                {
                    if (prevHighlightButton != null && prevHighlightButton != targetButton)
                        prevHighlightButton.SetSelectedVisualOffset(false);
                    targetButton.SetSelectedVisualOffset(true);
                }

                selectionBG.Width = selectionWidth;
                selectionBG.Height = selectionHeight;
                System.Windows.Controls.Canvas.SetLeft(selectionBG, isVertical ? nextPos + 2 : nextPos);
                System.Windows.Controls.Canvas.SetTop(selectionBG, isVertical ? nextTop : nextTop + 2);
                selectionBG.Visibility = Visibility.Visible;

                indicatorBar.Visibility = Visibility.Visible;

                double distance = Math.Abs(nextBarPos - prevBarPos);

                if (distance < 0.5)
                {
                    _indicatorAnimationGeneration++;
                    indicatorBar.RenderTransform = null;
                    indicatorBar.Width = isVertical ? 3 : indicatorBarSize;
                    indicatorBar.Height = isVertical ? indicatorBarSize : 3;
                    System.Windows.Controls.Canvas.SetLeft(indicatorBar, nextBarLeft);
                    System.Windows.Controls.Canvas.SetTop(indicatorBar, nextBarTop);
                    return;
                }

                if (_activeIndicatorStoryboard != null)
                {
                    var oldStoryboard = _activeIndicatorStoryboard;
                    _activeIndicatorStoryboard = null;
                    try { oldStoryboard.Stop(indicatorBar); } catch { }
                    indicatorBar.RenderTransform = null;
                    indicatorBar.Opacity = 1.0;
                }

                _indicatorAnimationGeneration++;
                indicatorBar.RenderTransform = null;

                double from = prevBarPos - nextBarPos;
                double to = 0;
                double dimension = indicatorBarSize;
                double stretchScale = distance / dimension + 1.0;

                System.Windows.Controls.Canvas.SetLeft(indicatorBar, nextBarLeft);
                System.Windows.Controls.Canvas.SetTop(indicatorBar, nextBarTop);
                indicatorBar.Width = isVertical ? 3 : indicatorBarSize;
                indicatorBar.Height = isVertical ? indicatorBarSize : 3;

                indicatorBar.RenderTransform = new TransformGroup
                {
                    Children =
                    {
                        new ScaleTransform(),
                        new TranslateTransform()
                    }
                };

                var storyboard = new Storyboard { FillBehavior = FillBehavior.Stop };

                var posAnim = new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new DiscreteDoubleKeyFrame(from < to ? from : (from + (dimension * (1.0 - 1))), KeyTime.FromPercent(0.0)),
                        new DiscreteDoubleKeyFrame(from < to ? (to + (dimension * (1.0 - 1))) : to, KeyTime.FromPercent(0.333)),
                    },
                    Duration = TimeSpan.FromMilliseconds(600)
                };

                var scaleAnim = new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new DiscreteDoubleKeyFrame(1.0, KeyTime.FromPercent(0.0)),
                        new SplineDoubleKeyFrame(stretchScale, KeyTime.FromPercent(0.333), new KeySpline(new Point(0.9, 0.1), new Point(1.0, 0.2))),
                        new SplineDoubleKeyFrame(1.0, KeyTime.FromPercent(1.0), new KeySpline(new Point(0.1, 0.9), new Point(0.2, 1.0)))
                    },
                    Duration = TimeSpan.FromMilliseconds(600)
                };

                var centerAnim = new DoubleAnimationUsingKeyFrames
                {
                    KeyFrames =
                    {
                        new DiscreteDoubleKeyFrame(from < to ? 0.0 : dimension, KeyTime.FromPercent(0.0)),
                        new DiscreteDoubleKeyFrame(from < to ? dimension : 0.0, KeyTime.FromPercent(0.333)),
                    },
                    Duration = TimeSpan.FromMilliseconds(600)
                };

                Storyboard.SetTarget(posAnim, indicatorBar);
                Storyboard.SetTarget(scaleAnim, indicatorBar);
                Storyboard.SetTarget(centerAnim, indicatorBar);

                if (isVertical)
                {
                    Storyboard.SetTargetProperty(posAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.Y)"));
                    Storyboard.SetTargetProperty(scaleAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleY)"));
                    Storyboard.SetTargetProperty(centerAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.CenterY)"));
                }
                else
                {
                    Storyboard.SetTargetProperty(posAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[1].(TranslateTransform.X)"));
                    Storyboard.SetTargetProperty(scaleAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.ScaleX)"));
                    Storyboard.SetTargetProperty(centerAnim, new PropertyPath("(UIElement.RenderTransform).(TransformGroup.Children)[0].(ScaleTransform.CenterX)"));
                }

                storyboard.Children.Add(posAnim);
                storyboard.Children.Add(scaleAnim);
                storyboard.Children.Add(centerAnim);

                int currentGeneration = _indicatorAnimationGeneration;
                _activeIndicatorStoryboard = storyboard;
                storyboard.Completed += (s, e) =>
                {
                    if (currentGeneration != _indicatorAnimationGeneration) return;
                    _activeIndicatorStoryboard = null;
                    indicatorBar.RenderTransform = null;
                };

                storyboard.Begin(indicatorBar, true);
                storyboard.Pause(indicatorBar);
                storyboard.SeekAlignedToLastTick(indicatorBar, TimeSpan.Zero, TimeSeekOrigin.BeginTime);
                Dispatcher.BeginInvoke(() =>
                {
                    storyboard.Resume(indicatorBar);
                }, System.Windows.Threading.DispatcherPriority.Loaded);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"设置高光位置失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 通用子面板位置更新方法：根据触发按钮的位置，动态调整子面板的水平位置，
        /// 使面板水平中心对齐按钮中心。不改变面板大小，不改变上下边距。
        /// </summary>
        /// <param name="button">触发按钮元素</param>
        /// <param name="panel">需要定位的子面板</param>
        /// <param name="defaultPanelWidth">面板默认宽度（当无法从Margin计算时使用）</param>
        private void UpdateSubPanelPosition(FrameworkElement button, FrameworkElement panel, double defaultPanelWidth)
        {
            try
            {
                if (button == null || panel == null) return;

                if (panel is Popup popup)
                {
                    if (popup.PlacementTarget == null)
                    {
                        popup.PlacementTarget = button;
                    }

                    if (popup.IsOpen)
                    {
                        _popupManager?.UpdatePosition(popup);
                    }

                    return;
                }

                if (!(panel.Parent is FrameworkElement panelContainer)) return;

                var ancestor = StackPanelFloatingBarRoot;
                if (ancestor == null) return;

                var buttonTransform = button.TransformToAncestor(ancestor);
                var buttonOrigin = buttonTransform.Transform(new Point(0, 0));
                double buttonCenterX = buttonOrigin.X + button.ActualWidth / 2.0;

                var containerTransform = panelContainer.TransformToAncestor(ancestor);
                var containerOrigin = containerTransform.Transform(new Point(0, 0));
                double containerX = containerOrigin.X;

                // 计算当前面板宽度（保持不变）：panelWidth = -Margin.Left - Margin.Right
                double currentLeft = panel.Margin.Left;
                double currentRight = panel.Margin.Right;
                double panelWidth = -currentLeft - currentRight;
                if (panelWidth <= 0) panelWidth = defaultPanelWidth;

                // 计算新的左边距，使面板水平中心对齐按钮：
                //   panel_center = containerX + newLeft + panelWidth/2 = buttonCenterX
                //   => newLeft = buttonCenterX - containerX - panelWidth/2
                double newLeft = buttonCenterX - containerX - panelWidth / 2.0;

                // 保持面板宽度不变：-newLeft - newRight = panelWidth
                //   => newRight = -panelWidth - newLeft
                double newRight = -panelWidth - newLeft;

                // 清除可能残留的 Margin 动画（HoldEnd 会阻止本地值生效）
                panel.BeginAnimation(FrameworkElement.MarginProperty, null);

                // 更新边距，仅调整Left/Right，保持Top/Bottom不变
                var margin = panel.Margin;
                panel.Margin = new Thickness(newLeft, margin.Top, newRight, margin.Bottom);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"更新子面板位置失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 更新批注子面板（PenPalette）的弹出位置，使其水平中心对齐笔按钮。
        /// </summary>
        private void UpdatePenPalettePosition()
        {
            UpdateSubPanelPosition(Pen_Icon, PenPalette, 193);
        }

        /// <summary>
        /// 更新工具面板（BorderTools）的弹出位置，使其水平中心对齐工具按钮。
        /// </summary>
        private void UpdateBorderToolsPosition()
        {
            UpdateSubPanelPosition(ToolsFloatingBarBtn, BorderTools, 119);
        }

        /// <summary>
        /// 更新橡皮擦尺寸面板（EraserSizePanel）的弹出位置，使其水平中心对齐橡皮擦按钮。
        /// </summary>
        private void UpdateEraserSizePanelPosition()
        {
            UpdateSubPanelPosition(Eraser_Icon, EraserSizePanel, 120);
        }

        /// <summary>
        /// 更新形状绘制面板（BorderDrawShape）的弹出位置，使其水平中心对齐形状按钮。
        /// </summary>
        private void UpdateBorderDrawShapePosition()
        {
        }

        /// <summary>
        /// 更新手势面板（TwoFingerGestureBorder）的弹出位置，使其水平中心对齐手势按钮。
        /// </summary>
        private void UpdateTwoFingerGestureBorderPosition()
        {
            UpdateSubPanelPosition(Gesture_Icon, TwoFingerGestureBorder, 119);
        }

        /// <summary>
        /// 隐藏浮动栏高光显示
        /// </summary>
        private void HideFloatingBarHighlight()
        {
            HideAllSelectionHighlights();
            _lastHighlightButton = null;
        }

        private void DeferFloatingBarHighlightIfLayoutPending(string mode)
        {
            if (string.IsNullOrEmpty(mode) || _highlightLayoutRetryCount >= 3)
            {
                HideAllSelectionHighlights();
                return;
            }

            _highlightLayoutRetryCount++;
            int version = ++_highlightPositionVersion;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_highlightPositionVersion != version) return;
                AnimateFloatingBarHighlightTo(mode);
            }), DispatcherPriority.ContextIdle);
        }

        private void HideAllSelectionHighlights()
        {
            if (_lastHighlightButton != null)
                _lastHighlightButton.SetSelectedVisualOffset(false);
            if (SelectionBGFloatingBar != null)
            {
                SelectionBGFloatingBar.Visibility = Visibility.Hidden;
            }
            if (IndicatorBarFloatingBar != null)
            {
                IndicatorBarFloatingBar.BeginAnimation(System.Windows.Controls.Canvas.LeftProperty, null);
                IndicatorBarFloatingBar.RenderTransform = null;
                IndicatorBarFloatingBar.Visibility = Visibility.Hidden;
            }
            _lastHighlightButton = null;
        }

        private (Border selectionBG, Border indicatorBar, StackPanel contentPanel) FindSelectionElementsForMode(string mode)
        {
            ToolbarImageButton targetButton = null;
            switch (mode)
            {
                case "cursor": targetButton = Cursor_Icon; break;
                case "pen":
                case "color": targetButton = Pen_Icon; break;
                case "eraser": targetButton = Eraser_Icon; break;
                case "eraserByStrokes": targetButton = EraserByStrokes_Icon; break;
                case "select": targetButton = SymbolIconSelect; break;
                case "shape": targetButton = ShapeDrawFloatingBarBtn; break;
            }
            return FindSelectionElementsForButton(targetButton);
        }

        private (Border selectionBG, Border indicatorBar, StackPanel contentPanel) FindSelectionElementsForButton(ToolbarImageButton button)
        {
            if (button == null || FloatingBarRootPanel == null) return (null, null, null);

            foreach (var border in FloatingBarRootPanel.Children.OfType<Border>())
            {
                if (border.Tag as string != ToolbarRegistry.ContentBorderTag || !(border.Child is Grid grid)) continue;

                StackPanel contentPanel = null;
                System.Windows.Controls.Canvas selectionCanvas = null;

                foreach (var gridChild in grid.Children.OfType<FrameworkElement>())
                {
                    if (gridChild is StackPanel sp && sp.Tag as string == ToolbarRegistry.ContentPanelTag)
                        contentPanel = sp;
                    else if (gridChild is System.Windows.Controls.Canvas canvas && canvas.Tag as string == ToolbarRegistry.SelectionCanvasTag)
                        selectionCanvas = canvas;
                }

                if (contentPanel == null) continue;

                bool containsButton = ContainsButton(contentPanel, button);
                if (containsButton)
                {
                    Border selectionBG = null;
                    Border indicatorBar = null;
                    if (selectionCanvas != null)
                    {
                        foreach (var canvasChild in selectionCanvas.Children.OfType<Border>())
                        {
                            if (canvasChild.Tag as string == ToolbarRegistry.SelectionBGTag)
                                selectionBG = canvasChild;
                            else if (canvasChild.Tag as string == ToolbarRegistry.IndicatorBarTag)
                                indicatorBar = canvasChild;
                        }
                    }
                    return (selectionBG, indicatorBar, contentPanel);
                }
            }

            var firstResult = GetFirstContentBorderElements();
            return firstResult;
        }

        private static bool ContainsButton(Panel panel, ToolbarImageButton button)
        {
            foreach (var child in panel.Children)
            {
                if (child == button) return true;
                if (child is Panel innerPanel && ContainsButton(innerPanel, button)) return true;
                if (child is ContentControl cc && cc.Content == button) return true;
                if (child is Decorator decorator && decorator.Child == button) return true;
            }
            return false;
        }

        private StackPanel FindContentPanelForButton(ToolbarImageButton button)
        {
            if (button == null || FloatingBarRootPanel == null) return null;

            foreach (var border in FloatingBarRootPanel.Children.OfType<Border>())
            {
                if (border.Tag as string != ToolbarRegistry.ContentBorderTag || !(border.Child is Grid grid)) continue;

                foreach (var gridChild in grid.Children.OfType<StackPanel>())
                {
                    if (gridChild.Tag as string == ToolbarRegistry.ContentPanelTag && ContainsButton(gridChild, button))
                        return gridChild;
                }
            }
            return null;
        }

        private (Border, Border, StackPanel) GetFirstContentBorderElements()
        {
            if (FloatingBarRootPanel == null) return (null, null, null);

            foreach (var border in FloatingBarRootPanel.Children.OfType<Border>())
            {
                if (border.Tag as string != ToolbarRegistry.ContentBorderTag || !(border.Child is Grid grid)) continue;

                Border selectionBG = null;
                Border indicatorBar = null;
                StackPanel contentPanel = null;

                foreach (var gridChild in grid.Children.OfType<FrameworkElement>())
                {
                    if (gridChild is StackPanel sp && sp.Tag as string == ToolbarRegistry.ContentPanelTag)
                        contentPanel = sp;
                    else if (gridChild is System.Windows.Controls.Canvas canvas && canvas.Tag as string == ToolbarRegistry.SelectionCanvasTag)
                    {
                        foreach (var canvasChild in canvas.Children.OfType<Border>())
                        {
                            if (canvasChild.Tag as string == ToolbarRegistry.SelectionBGTag)
                                selectionBG = canvasChild;
                            else if (canvasChild.Tag as string == ToolbarRegistry.IndicatorBarTag)
                                indicatorBar = canvasChild;
                        }
                    }
                }

                if (contentPanel != null)
                    return (selectionBG, indicatorBar, contentPanel);
            }
            return (null, null, null);
        }

        private static bool IsDescendantOf(DependencyObject child, DependencyObject parent)
        {
            if (child == null || parent == null) return false;
            var current = LogicalTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current == parent) return true;
                current = LogicalTreeHelper.GetParent(current);
            }
            current = System.Windows.Media.VisualTreeHelper.GetParent(child);
            while (current != null)
            {
                if (current == parent) return true;
                current = System.Windows.Media.VisualTreeHelper.GetParent(current);
            }
            return false;
        }

        private bool IsElementVisibleInTree(FrameworkElement element)
        {
            if (element == null || element.Visibility != Visibility.Visible) return false;
            var parent = VisualTreeHelper.GetParent(element);
            while (parent != null)
            {
                if (parent is FrameworkElement fe && fe.Visibility != Visibility.Visible) return false;
                parent = VisualTreeHelper.GetParent(parent);
            }
            return true;
        }

        /// <summary>
        /// 获取当前选中的模式
        /// </summary>
        /// <returns>当前选中的模式名称</returns>
        public string GetCurrentSelectedMode()
        {
            try
            {
                // 优先使用缓存的模式，避免在浮动栏刷新时返回过时的模式信息
                if (!string.IsNullOrEmpty(_currentToolMode))
                {
                    return _currentToolMode;
                }

                // 如果缓存为空，则从inkCanvas状态推断模式
                if (inkCanvas.EditingMode == InkCanvasEditingMode.Select)
                {
                    return "select";
                }

                if (inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                {
                    // 检查是否是荧光笔模式
                    if (drawingAttributes != null && drawingAttributes.IsHighlighter)
                    {
                        return "color";
                    }

                    return "pen";
                }

                if (inkCanvas.EditingMode == InkCanvasEditingMode.EraseByPoint)
                {
                    // 检查是面积擦还是线擦
                    if (Eraser_Icon != null && Eraser_Icon.Visibility == Visibility.Visible)
                    {
                        return "eraser";
                    }

                    if (EraserByStrokes_Icon != null && EraserByStrokes_Icon.Visibility == Visibility.Visible)
                    {
                        return "eraserByStrokes";
                    }
                }
                else if (inkCanvas.EditingMode == InkCanvasEditingMode.None)
                {
                    return "cursor";
                }
                else if (drawingShapeMode != 0)
                {
                    return "shape";
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"获取当前选中模式失败: {ex.Message}", LogHelper.LogType.Error);
            }

            return "cursor"; // 默认返回鼠标模式
        }

        /// <summary>
        /// 更新当前工具模式缓存
        /// </summary>
        /// <param name="mode">模式名称</param>
        private void UpdateCurrentToolMode(string mode)
        {
            var oldMode = _currentToolMode;
            _currentToolMode = NormalizeToolModeForFreeze(mode);

            // 模式变了就刷新工具栏可见性。
            //
            // 为什么加这里：IsAnnotating 是 _currentToolMode != "cursor" 算出来的，
            // 而「批注」那组按钮（橡皮/选择/形状/撤销/重做）挂的是 AnnotationOnly 规则。
            // 原来 UpdateCurrentToolMode 只改字段、不刷新 UI，
            // 于是点「批注」后 _currentToolMode 已经是 "pen"，
            // 但那一组按钮仍然是 Collapsed —— 表现出来就是「点批注不展开」。
            //
            // 刷新放在这里而不是每个调用点：所有工具模式切换都会走这个方法，
            // 一处补齐比在每个 onClick 里各补一次可靠。
            if (!string.Equals(oldMode, _currentToolMode, StringComparison.Ordinal))
            {
                UpdateToolbarComponentVisibility();

                // 折叠态下角标也要跟着换。
                // 之前只在折叠/展开时更新角标，切工具模式不更新——
                // 所以折叠着的时候切到面积擦/圈套，角标还是旧的。
                if (!IsFloatingBarContentVisible())
                {
                    UpdateFloatingBarModeBadge(true);
                }
            }

            // 动态切换 EnablePointerSupport：仅在画笔批注模式下启用 WM_POINTER 触摸栈，
            // 其他模式下禁用以避免 DragMove/DoDragDrop 在模拟触摸屏下假死。
            try
            {
                AppContext.SetSwitch("Switch.System.Windows.Input.Stylus.EnablePointerSupport", _currentToolMode == "pen");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Failed to toggle EnablePointerSupport: {ex}");
            }
        }

        #endregion
    }
}
