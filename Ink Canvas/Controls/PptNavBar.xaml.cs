using System.Windows.Threading;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using Ink_Canvas.Helpers;

namespace Ink_Canvas.Controls
{
    /// <summary>
    /// PPT 翻页 + 增强预览一体化控件。
    /// 通过 <see cref="Direction"/> 切换底部条 (LB/RB) 与侧边条 (LS/RS) 布局,
    /// 预览列表内嵌于同一个 Border,展开时占据按钮组之外的剩余空间。
    /// </summary>
    public partial class PPTNavBar : UserControl
    {
        public sealed class PreviewItem
        {
            public int SlideNumber { get; set; }
            public BitmapImage Thumbnail { get; set; }
        }

        public enum NavDirection
        {
            LeftBottom,
            RightBottom,
            LeftSide,
            RightSide
        }

        public static readonly DependencyProperty DirectionProperty = DependencyProperty.Register(
            nameof(Direction), typeof(NavDirection), typeof(PPTNavBar),
            new PropertyMetadata(NavDirection.LeftBottom, OnDirectionChanged));

        public static readonly DependencyProperty CurrentSlideProperty = DependencyProperty.Register(
            nameof(CurrentSlide), typeof(int), typeof(PPTNavBar),
            new PropertyMetadata(0, OnPageChanged));

        public static readonly DependencyProperty TotalSlidesProperty = DependencyProperty.Register(
            nameof(TotalSlides), typeof(int), typeof(PPTNavBar),
            new PropertyMetadata(0, OnPageChanged));

        public static readonly DependencyProperty PreviewItemsProperty = DependencyProperty.Register(
            nameof(PreviewItems), typeof(IList<PreviewItem>), typeof(PPTNavBar),
            new PropertyMetadata(null, OnPreviewItemsChanged));

        public static readonly DependencyProperty IsPreviewExpandedProperty = DependencyProperty.Register(
            nameof(IsPreviewExpanded), typeof(bool), typeof(PPTNavBar),
            new PropertyMetadata(false, OnIsPreviewExpandedChanged));

        public NavDirection Direction
        {
            get => (NavDirection)GetValue(DirectionProperty);
            set => SetValue(DirectionProperty, value);
        }

        public int CurrentSlide
        {
            get => (int)GetValue(CurrentSlideProperty);
            set => SetValue(CurrentSlideProperty, value);
        }

        public int TotalSlides
        {
            get => (int)GetValue(TotalSlidesProperty);
            set => SetValue(TotalSlidesProperty, value);
        }

        public IList<PreviewItem> PreviewItems
        {
            get => (IList<PreviewItem>)GetValue(PreviewItemsProperty);
            set => SetValue(PreviewItemsProperty, value);
        }

        public bool IsPreviewExpanded
        {
            get => (bool)GetValue(IsPreviewExpandedProperty);
            set => SetValue(IsPreviewExpandedProperty, value);
        }

        public event EventHandler PreviousClick;
        public event EventHandler NextClick;
        public event EventHandler PageClick;
        public event EventHandler<int> SlideSelected;
        public event EventHandler PreviousPressedDown;
        public event EventHandler NextPressedDown;
        public event EventHandler PressEnded;
        public event EventHandler<bool> PreviewExpandedChanged;

        // 静态几何（左下/右下：水平箭头；左侧/右侧：垂直箭头）
        private static readonly Geometry HArrowLeft = Geometry.Parse("F0 M24,24z M0,0z M3.3994,12.9642C2.86687,12.4317,2.86687,11.5683,3.3994,11.0358L9.94485,4.49031C10.4774,3.95777 11.3408,3.95777 11.8733,4.49031 12.4059,5.02284 12.4059,5.88625 11.8733,6.41878L7.65575,10.6364 19.6364,10.6364C20.3895,10.6364 21,11.2469 21,12 21,12.7531 20.3895,13.3636 19.6364,13.3636L7.65575,13.3636 11.8733,17.5812C12.4059,18.1137 12.4059,18.9772 11.8733,19.5097 11.3408,20.0422 10.4774,20.0422 9.94485,19.5097L3.3994,12.9642z");
        private static readonly Geometry HArrowRight = Geometry.Parse("F0 M24,24z M0,0z M20.6006,12.9642C21.1331,12.4317,21.1331,11.5683,20.6006,11.0358L14.0551,4.49031C13.5226,3.95777 12.6592,3.95777 12.1267,4.49031 11.5941,5.02284 11.5941,5.88625 12.1267,6.41878L16.3443,10.6364 4.36364,10.6364C3.61052,10.6364 3,11.2469 3,12 3,12.7531 3.61052,13.3636 4.36364,13.3636L16.3443,13.3636 12.1267,17.5812C11.5941,18.1137 11.5941,18.9772 12.1267,19.5097 12.6592,20.0422 13.5226,20.0422 14.0551,19.5097L20.6006,12.9642z");
        private static readonly Geometry VArrowUp = Geometry.Parse("F0 M24,24z M0,0z M11.0357,3.3994C11.5682,2.86687,12.4316,2.86687,12.9641,3.3994L19.5096,9.94485C20.0421,10.4774 20.0421,11.3408 19.5096,11.8733 18.9771,12.4059 18.1137,12.4059 17.5811,11.8733L13.3635,7.65575 13.3635,19.6364C13.3635,20.3895 12.753,21 11.9999,21 11.2468,21 10.6363,20.3895 10.6363,19.6364L10.6363,7.65575 6.41869,11.8733C5.88616,12.4059 5.02275,12.4059 4.49022,11.8733 3.95769,11.3408 3.95769,10.4774 4.49022,9.94485L11.0357,3.3994z");
        private static readonly Geometry VArrowDown = Geometry.Parse("F0 M24,24z M0,0z M11.0357,20.6006C11.5682,21.1331,12.4316,21.1331,12.9641,20.6006L19.5096,14.0551C20.0421,13.5226 20.0421,12.6592 19.5096,12.1267 18.9771,11.5941 18.1137,11.5941 17.5811,12.1267L13.3635,16.3443 13.3635,4.36364C13.3635,3.61052 12.753,3 11.9999,3 11.2468,3 10.6363,3.61052 10.6363,4.36364L10.6363,16.3443 6.41869,12.1267C5.88616,11.5941 5.02275,11.5941 4.49022,12.1267 3.95769,12.6592 3.95769,13.5226 4.49022,14.0551L11.0357,20.6006z");

        public PPTNavBar()
        {
            InitializeComponent();
            ApplyDirection(Direction);
        }

        private static void OnDirectionChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PPTNavBar bar) bar.ApplyDirection((NavDirection)e.NewValue);
        }

        private static void OnPageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PPTNavBar bar) bar.RefreshPageText();
        }

        private static void OnPreviewItemsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PPTNavBar bar)
            {
                bar.PreviewList.ItemsSource = e.NewValue as IList<PreviewItem>;
                bar.SyncPreviewSelection();
            }
        }

        private static void OnIsPreviewExpandedChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is PPTNavBar bar)
            {
                bool expanded = (bool)e.NewValue;
                bar.PreviewList.Visibility = expanded ? Visibility.Visible : Visibility.Collapsed;
                bar.ApplyRootOpacity();
                bar.ApplyLayout();
                if (expanded)
                {
                    bar.SyncPreviewSelection();
                    bar.HookOutsideClick();
                }
                else
                {
                    bar.UnhookOutsideClick();
                }
                bar.PreviewExpandedChanged?.Invoke(bar, expanded);
            }
        }

        private Window _hookedWindow;
        private void HookOutsideClick()
        {
            if (_hookedWindow != null) return;
            _hookedWindow = Window.GetWindow(this);
            if (_hookedWindow != null)
            {
                _hookedWindow.PreviewMouseDown += OnWindowPreviewMouseDown;
            }
        }
        private void UnhookOutsideClick()
        {
            if (_hookedWindow != null)
            {
                _hookedWindow.PreviewMouseDown -= OnWindowPreviewMouseDown;
                _hookedWindow = null;
            }
        }
        private void OnWindowPreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && !IsDescendantOf(d, this))
            {
                IsPreviewExpanded = false;
            }
        }
        private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
        {
            while (child != null)
            {
                if (ReferenceEquals(child, ancestor)) return true;
                child = System.Windows.Media.VisualTreeHelper.GetParent(child)
                        ?? System.Windows.LogicalTreeHelper.GetParent(child);
            }
            return false;
        }

        private void ApplyDirection(NavDirection dir) => ApplyLayout();

        private void ApplyLayout()
        {
            var dir = Direction;
            bool expanded = IsPreviewExpanded;

            // 重置可能在不同状态下被设置的属性
            ButtonRow.ClearValue(WidthProperty);
            ButtonRow.ClearValue(HeightProperty);
            ButtonRow.ClearValue(HorizontalAlignmentProperty);
            PreviewList.ClearValue(WidthProperty);
            PreviewList.ClearValue(HeightProperty);
            PreviewList.ClearValue(MaxHeightProperty);
            PreviewList.ClearValue(MaxWidthProperty);
            PreviewList.ClearValue(HorizontalAlignmentProperty);
            ClearValue(HeightProperty);
            ClearValue(MaxHeightProperty);

            double availableHeight = ComputeAvailableHeight();
            double scale = _currentScale;

            switch (dir)
            {
                case NavDirection.LeftBottom:
                case NavDirection.RightBottom:
                    DockPanel.SetDock(PreviewList, Dock.Top);
                    DockPanel.SetDock(ButtonRow, Dock.Bottom);
                    ButtonRow.Orientation = Orientation.Horizontal;
                    ButtonRow.Height = 50;
                    if (expanded)
                    {
                        // 预览面板拉宽到 280,贴向同侧角落
                        PreviewList.Width = 280;
                        PreviewList.MaxHeight = Math.Max(200, availableHeight - 50 * scale) / scale;
                        PreviewList.HorizontalAlignment = dir == NavDirection.LeftBottom
                            ? HorizontalAlignment.Left
                            : HorizontalAlignment.Right;
                        // 按钮组宽度限制为原始内容宽度,并贴向同侧,保持按钮位置不变
                        ButtonRow.HorizontalAlignment = dir == NavDirection.LeftBottom
                            ? HorizontalAlignment.Left
                            : HorizontalAlignment.Right;
                    }
                    else
                    {
                        PreviewList.SetBinding(WidthProperty, new System.Windows.Data.Binding(nameof(ButtonRow.ActualWidth)) { Source = ButtonRow });
                        PreviewList.MaxHeight = 380 / scale;
                    }
                    PreviousButtonGeometry.Geometry = HArrowLeft;
                    NextButtonGeometry.Geometry = HArrowRight;
                    break;

                case NavDirection.LeftSide:
                    DockPanel.SetDock(PreviewList, Dock.Right);
                    DockPanel.SetDock(ButtonRow, Dock.Left);
                    ButtonRow.Orientation = Orientation.Vertical;
                    ButtonRow.Width = 50;
                    PreviewList.Width = 240;
                    PreviewList.MaxHeight = 480 / scale;
                    PreviousButtonGeometry.Geometry = VArrowUp;
                    NextButtonGeometry.Geometry = VArrowDown;
                    break;
                case NavDirection.RightSide:
                    DockPanel.SetDock(PreviewList, Dock.Left);
                    DockPanel.SetDock(ButtonRow, Dock.Right);
                    ButtonRow.Orientation = Orientation.Vertical;
                    ButtonRow.Width = 50;
                    PreviewList.Width = 240;
                    PreviewList.MaxHeight = 480 / scale;
                    PreviousButtonGeometry.Geometry = VArrowUp;
                    NextButtonGeometry.Geometry = VArrowDown;
                    break;
            }
        }

        private double ComputeAvailableHeight()
        {
            var window = Window.GetWindow(this);
            double h = window != null ? window.ActualHeight : SystemParameters.PrimaryScreenHeight;
            return Math.Max(240, h - 12);
        }

        private void RefreshPageText()
        {
            if (CurrentSlide > 0 && TotalSlides > 0)
            {
                PageNowText.Text = CurrentSlide.ToString();
                PageTotalText.Text = $"/ {TotalSlides}";
            }
            else
            {
                PageNowText.Text = "?";
                PageTotalText.Text = "/ ?";
            }
            SyncPreviewSelection();
        }

        private void SyncPreviewSelection()
        {
            if (PreviewItems == null || CurrentSlide <= 0) return;
            foreach (var item in PreviewItems)
            {
                if (item.SlideNumber == CurrentSlide)
                {
                    PreviewList.SelectedItem = item;
                    PreviewList.ScrollIntoView(item);
                    return;
                }
            }
        }

        private void SetFeedback(Border feedback, double opacity)
        {
            // opacity 为 0 表示松手/离开，永远清零。
            // 非 0 表示按下，若开启了加强反馈则用配置值，否则维持原表现。
            if (opacity > 0 && _pressFeedbackEnabled)
                opacity = _pressFeedbackOpacity;
            feedback.Opacity = opacity;
        }

        private object _lastDown;

        /// <summary>
        /// 本次按下期间，长按定时器是否真的翻过页。
        /// 长按翻页和单击翻页走的是两条独立的路：定时器在按住时反复翻页，
        /// 松手时 MouseUp 又会补一次 Click。结果是长按松手总要多翻一页。
        /// 这个标记让 MouseUp 知道「这一次已经被长按处理过了」，不要再补一次。
        /// </summary>
        private bool _longPressConsumed;

        // ── LTSC P1c 外观增强 ──────────────────────────────────────────
        private DispatcherTimer _autoHideTimer;
        private double _autoHideOpacity = 0.08;
        private bool _autoHideEnabled;
        /// <summary>自动淡出时被压低的容器，AllBars 共用。</summary>
        private FrameworkElement _autoHideTarget;

        private void PreviousButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastDown = sender;
            _longPressConsumed = false;
            SetFeedback(PreviousButtonFeedbackBorder, 0.15);
            PreviousPressedDown?.Invoke(this, EventArgs.Empty);
        }

        private void PreviousButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            SetFeedback(PreviousButtonFeedbackBorder, 0);
            PressEnded?.Invoke(this, EventArgs.Empty);
            if (_lastDown != sender) return;
            _lastDown = null;
            // 长按期间已经翻过页，松手不再补这一下，否则长按永远多翻一页。
            if (_longPressConsumed) { _longPressConsumed = false; return; }
            PreviousClick?.Invoke(this, EventArgs.Empty);
        }

        private void PreviousButton_MouseLeave(object sender, MouseEventArgs e)
        {
            SetFeedback(PreviousButtonFeedbackBorder, 0);
            _lastDown = null;
            _longPressConsumed = false;
            PressEnded?.Invoke(this, EventArgs.Empty);
        }

        private void NextButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastDown = sender;
            _longPressConsumed = false;
            SetFeedback(NextButtonFeedbackBorder, 0.15);
            NextPressedDown?.Invoke(this, EventArgs.Empty);
        }

        private void NextButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            SetFeedback(NextButtonFeedbackBorder, 0);
            PressEnded?.Invoke(this, EventArgs.Empty);
            if (_lastDown != sender) return;
            _lastDown = null;
            // 同上：长按已经翻过页，松手不再补。
            if (_longPressConsumed) { _longPressConsumed = false; return; }
            NextClick?.Invoke(this, EventArgs.Empty);
        }

        private void NextButton_MouseLeave(object sender, MouseEventArgs e)
        {
            SetFeedback(NextButtonFeedbackBorder, 0);
            _lastDown = null;
            _longPressConsumed = false;
            PressEnded?.Invoke(this, EventArgs.Empty);
        }

        private void PageButton_MouseDown(object sender, MouseButtonEventArgs e)
        {
            _lastDown = sender;
            SetFeedback(PageButtonFeedbackBorder, 0.15);
        }

        private void PageButton_MouseUp(object sender, MouseButtonEventArgs e)
        {
            SetFeedback(PageButtonFeedbackBorder, 0);
            if (_lastDown != sender) return;
            _lastDown = null;
            PageClick?.Invoke(this, EventArgs.Empty);
        }

        private void PageButton_MouseLeave(object sender, MouseEventArgs e)
        {
            SetFeedback(PageButtonFeedbackBorder, 0);
            _lastDown = null;
        }

        private void PreviewList_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (PreviewList.SelectedItem is PreviewItem item)
            {
                SlideSelected?.Invoke(this, item.SlideNumber);
            }
        }

        public void ApplyTheme(bool isDark)
        {
            var fgBrush = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(39, 39, 42));
            var feedbackBrush = isDark ? Brushes.White : new SolidColorBrush(Color.FromRgb(24, 24, 27));
            var bgBrush = isDark
                ? new SolidColorBrush(Color.FromRgb(39, 39, 42))
                : new SolidColorBrush(Color.FromRgb(244, 244, 245));
            var borderBrush = isDark
                ? new SolidColorBrush(Color.FromRgb(82, 82, 91))
                : new SolidColorBrush(Color.FromRgb(161, 161, 170));

            PreviousButtonGeometry.Brush = fgBrush;
            NextButtonGeometry.Brush = fgBrush;
            PreviousButtonFeedbackBorder.Background = feedbackBrush;
            NextButtonFeedbackBorder.Background = feedbackBrush;
            PageButtonFeedbackBorder.Background = feedbackBrush;
            PageNowText.Foreground = fgBrush;
            PageTotalText.Foreground = fgBrush;
            RootBorder.Background = bgBrush;
            RootBorder.BorderBrush = borderBrush;
            Resources["PPTNavBarItemForeground"] = fgBrush;
        }

        public void SetPageButtonVisibility(Visibility v) => PageButtonBorder.Visibility = v;

        /// <summary>
        /// 设置页码字号。投影/大屏上 17px 坐在后排看不清，这里允许放大。
        /// </summary>
        public void SetPageNumberFontSize(double now, double total)
        {
            if (now   > 0) PageNowText.FontSize   = now;
            if (total > 0) PageTotalText.FontSize = total;

            // 容器宽度必须跟着字号走，否则字号一大，数字就被外层裁掉。
            // 原设计固定 MinWidth=50、内边距 12，只够 17px 用；
            // 40px 时「53」这种两位数会溢出。
            // 估算：两位数字宽 ≈ 字号 × 1.2，再加左右各 12 的内边距和一点余量。
            try
            {
                double needW = Math.Max(50, Math.Ceiling(Math.Max(now, total * 1.4) * 1.2) + 26);
                PageButtonBorder.MinWidth  = needW;
                PageNumberStack.Margin     = new Thickness(6, 0, 6, 0);
            }
            catch { }
        }

        /// <summary>
        /// 按下反馈：除原有半透明高亮外，再加一层更明显的底色，投影下能看出「点到了」。
        /// </summary>
        public void SetPressFeedback(bool enabled, double opacity)
        {
            _pressFeedbackEnabled = enabled;
            _pressFeedbackOpacity = Math.Max(0.05, Math.Min(0.9, opacity));
        }

        /// <summary>
        /// 自动淡出：静止 <paramref name="delaySeconds"/> 秒后，整条翻页栏降到
        /// <paramref name="fadedOpacity"/>，不再挡住板书；鼠标进入或按下立刻恢复。
        /// </summary>
        public void SetAutoHide(bool enabled, double delaySeconds, double fadedOpacity)
        {
            _autoHideEnabled = enabled;
            _autoHideOpacity = Math.Max(0.0, Math.Min(1.0, fadedOpacity));

            if (_autoHideTimer == null)
            {
                _autoHideTimer = new DispatcherTimer();
                _autoHideTimer.Tick += (s, e) =>
                {
                    _autoHideTimer.Stop();
                    if (_autoHideEnabled) ApplyFade(_autoHideOpacity);
                };
            }

            _autoHideTimer.Stop();

            if (!enabled)
            {
                // 关掉时立刻恢复到正常不透明度，避免停留在淡出状态。
                // 这里用无动画版本：功能已经关了，不该还留一段淡入动画在跑。
                ApplyFadeImmediate(1.0);
                return;
            }

            var sec = Math.Max(0.5, delaySeconds);
            _autoHideTimer.Interval = TimeSpan.FromSeconds(sec);

            // 鼠标进入 / 按下 / 展开预览 → 立刻恢复并重新计时。
            if (_autoHideTarget == null)
            {
                _autoHideTarget = this;
                _autoHideTarget.MouseEnter += (s, e) => WakeAutoHide();
                _autoHideTarget.MouseMove  += (s, e) => WakeAutoHide();
                _autoHideTarget.MouseLeave += (s, e) => ArmAutoHideTimer();
            }

            ArmAutoHideTimer();
        }

        private void ArmAutoHideTimer()
        {
            if (!_autoHideEnabled || _autoHideTimer == null) return;
            _autoHideTimer.Stop();
            _autoHideTimer.Start();
        }

        /// <summary>用户又碰到翻页栏了：立刻恢复可见，并重新开始计时。</summary>
        public void WakeAutoHide()
        {
            if (!_autoHideEnabled) return;
            ApplyFade(1.0);
            ArmAutoHideTimer();
        }

        /// <summary>
        /// LTSC D-12：空闲减淡的淡入淡出。
        ///
        /// 原来是把 Opacity 直接赋目标值，降下来/弹回去都是硬切，
        /// 静止一会突然整条消失、鼠标一扫又突然蹦出来，很跳。
        /// 现在改成补间过渡：
        ///   - 淡出（回 1.0 → 变淡）：慢一点，160ms，不要让人注意到"它在动"
        ///   - 淡入（变淡 → 回 1.0）：快一点，110ms，鼠标来了要立刻跟手
        ///
        /// 只动 RootBorder.Opacity，不去碰外层容器，免得和「透明度」设置项打架。
        /// </summary>
        private void ApplyFade(double opacity)
        {
            try
            {
                if (RootBorder == null) return;
        
                // LTSC D-25：改走全应用统一的弹簧引擎。
                //
                // 原来用 DoubleAnimation + CubicEase，按时长插值：淡出 160ms、淡入 110ms。
                // 问题是打断时速度断掉——鼠标扫进扫出，淡到一半改目标会从静止重新起步，
                // 观感是"顿一下"。
                //
                // 现在用弹簧：
                //   - 淡入用 Snappy（0.31/0.8），鼠标来了要跟手
                //   - 淡出用 Smooth（0.39/0.85），不要让人注意到它在动
                //   - 打断时保留速度，扫进扫出是连续的
                bool fadingOut = opacity < RootBorder.Opacity;
                var preset = fadingOut ? SpringAnimator.Smooth : SpringAnimator.Snappy;
        
                SpringAnimator.AnimateScalar(
                    RootBorder,
                    opacity,
                    v => RootBorder.Opacity = v,
                    preset);
            }
            catch { }
        }

        /// <summary>
        /// 立刻把不透明度拍到位（不走动画）。用在初始化、关闭功能、以及
        /// 需要瞬时同步的场景，避免动画排队导致状态落后一步。
        /// </summary>
        private void ApplyFadeImmediate(double opacity)
        {
            try
            {
                if (RootBorder == null) return;
                RootBorder.BeginAnimation(UIElement.OpacityProperty, null);
                RootBorder.Opacity = opacity;
                SpringAnimator.SyncScalar(RootBorder, opacity);
            }
            catch { }
        }

        private bool _pressFeedbackEnabled = true;
        private double _pressFeedbackOpacity = 0.35;

        /// <summary>
        /// 由 MainWindow 在长按定时器真的翻页后调用，标记「本次按下已被长按消费」，
        /// 让紧接着的 MouseUp 不再补一次单击翻页。
        /// </summary>
        public void MarkLongPressConsumed() => _longPressConsumed = true;
        public void SetBarOpacity(double opacity)
        {
            _collapsedOpacity = opacity;
            ApplyRootOpacity();
        }

        private void ApplyRootOpacity() => ApplyFadeImmediate(IsPreviewExpanded ? 1 : _collapsedOpacity);

        public void SetBarScale(double scale)
        {
            _currentScale = scale;
            NavBarScaleTransform.ScaleX = scale;
            NavBarScaleTransform.ScaleY = scale;
        }

        private double _currentScale = 1.0;
        private double _collapsedOpacity = 1.0;
    }
}