using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace Ink_Canvas.Helpers
{
    internal class AnimationsHelper
    {
        /// <summary>LTSC D-05: 全局动效抑制。为 true 时所有动画退化为瞬时完成。</summary>
        internal static bool AnimationsDisabled
        {
            get
            {
                try { return Ink_Canvas.Windows.SettingsViews.Helpers.SettingsManager.Settings?.Appearance?.DisableAllAnimations ?? false; }
                catch { return false; }
            }
        }

        /// <summary>
        /// 把动画时长归零（当全局动效被禁用时），否则原样返回。
        /// </summary>
        /// <remarks>
        /// 关于 TimeSpan.Zero 的安全性：WPF 的 Storyboard 在 Duration 为 Zero 时不会挂起，
        /// 而是立即把属性推到 From 值并同步完成、触发 Completed，随后 Clock 自然结束并解绑一次。
        /// 因此动画退化为"瞬时完成"，属性终值与原来的动画一致，
        /// 不会出现"动画卡住导致工具栏永远展不开"的情况。
        /// 另外 From/To 仍然显式给出，所以即使归零也一定落在正确的终值上，不会残留起始值。
        /// </remarks>
        internal static TimeSpan EffectiveDuration(TimeSpan duration)
            => AnimationsDisabled ? TimeSpan.Zero : duration;

        private static readonly IntPtr HWND_TOP = new IntPtr(0);

        /// <summary>
        /// 强制刷新 Popup 的实际窗口位置（终极方案）
        /// 通过 Win32 API 直接操作窗口句柄
        /// </summary>
        public static void ForceRefreshPopupPosition(Popup popup)
        {
            if (popup?.Child == null || !popup.IsOpen) return;

            try
            {
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var source = PresentationSource.FromVisual(popup.Child) as HwndSource;
                        if (source?.Handle == null) return;

                        var hwnd = source.Handle;

                        // 获取当前窗口位置
                        if (NativeWindowHelper.GetWindowRect(hwnd, out NativeWindowHelper.RECT rect))
                        {
                            // 使用相同的参数调用 SetWindowPos，但加上 SWP_SHOWWINDOW
                            // 这会强制窗口管理器重新评估并更新窗口位置
                            NativeWindowHelper.SetWindowPos(
                                hwnd,
                                HWND_TOP,
                                rect.Left, rect.Top,
                                rect.Right - rect.Left,
                                rect.Bottom - rect.Top,
                                NativeWindowHelper.SWP_NOACTIVATE | NativeWindowHelper.SWP_SHOWWINDOW);

                            System.Diagnostics.Debug.WriteLine($"[PopupZOrder] Force refreshed position: ({rect.Left}, {rect.Top})");
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PopupZOrder] ForceRefreshPopupPosition failed: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupZOrder] ForceRefreshPopupPosition error: {ex.Message}");
            }
        }

        /// <summary>
        /// 将 Popup 窗口提升到最顶层，确保不被其他控件遮挡
        /// 采用多重策略确保置顶生效
        /// </summary>
        private static void BringPopupToFront(Popup popup)
        {
            try
            {
                if (popup?.Child == null) return;

                Action bringToTopAction = () =>
                {
                    try
                    {
                        var source = PresentationSource.FromVisual(popup.Child) as HwndSource;
                        if (source?.Handle == null) return;

                        var hwnd = source.Handle;

                        // 策略1：直接设置为 TOPMOST（最高优先级）
                        NativeWindowHelper.SetWindowPos(hwnd, NativeWindowHelper.HWND_TOPMOST,
                            0, 0, 0, 0,
                            NativeWindowHelper.SWP_NOMOVE | NativeWindowHelper.SWP_NOSIZE | NativeWindowHelper.SWP_NOACTIVATE | NativeWindowHelper.SWP_SHOWWINDOW);

                        System.Diagnostics.Debug.WriteLine($"[PopupZOrder] Set TOPMOST for popup");
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PopupZOrder] BringPopupToFront failed: {ex.Message}");
                    }
                };

                // 立即执行第一次
                Application.Current.Dispatcher.BeginInvoke(bringToTopAction,
                    System.Windows.Threading.DispatcherPriority.Render);

                // 延迟 50ms 后再次执行（确保在其他窗口操作之后）
                Application.Current.Dispatcher.BeginInvoke(bringToTopAction,
                    System.Windows.Threading.DispatcherPriority.Normal);

                // 延迟 100ms 后第三次执行（最终确认）
                Application.Current.Dispatcher.BeginInvoke(bringToTopAction,
                    System.Windows.Threading.DispatcherPriority.Background);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupZOrder] BringPopupToFront error: {ex.Message}");
            }
        }

        /// <summary>
        /// 轻量级置顶方法（用于拖动时的实时跟随）
        /// 仅执行一次置顶，避免性能问题
        /// </summary>
        public static void BringPopupToFrontLight(Popup popup)
        {
            try
            {
                if (popup?.Child == null) return;

                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        var source = PresentationSource.FromVisual(popup.Child) as HwndSource;
                        if (source?.Handle == null) return;

                        var hwnd = source.Handle;

                        NativeWindowHelper.SetWindowPos(hwnd, NativeWindowHelper.HWND_TOPMOST,
                            0, 0, 0, 0,
                            NativeWindowHelper.SWP_NOMOVE | NativeWindowHelper.SWP_NOSIZE | NativeWindowHelper.SWP_NOACTIVATE | NativeWindowHelper.SWP_SHOWWINDOW);
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine($"[PopupZOrder] BringPopupToFrontLight failed: {ex.Message}");
                    }
                }), System.Windows.Threading.DispatcherPriority.Render);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupZOrder] BringPopupToFrontLight error: {ex.Message}");
            }
        }

        private static UIElement ResolveAnimationTarget(UIElement element)
        {
            return element;
        }

        public static void ShowWithFadeIn(UIElement element, double duration = 0.15)
        {
            if (element.Visibility == Visibility.Visible) return;

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            var sb = new Storyboard();

            // 渐变动画
            var fadeInAnimation = new DoubleAnimation
            {
                From = 0.5,
                To = 1,
                Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
            };
            Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath(UIElement.OpacityProperty));

            sb.Children.Add(fadeInAnimation);

            element.Visibility = Visibility.Visible;

            sb.Begin((FrameworkElement)element);
        }

        public static void ShowWithSlideFromBottomAndFade(UIElement element, double duration = 0.25)
        {
            try
            {
                if (element == null)
                    throw new ArgumentNullException(nameof(element));

                if (element.Visibility == Visibility.Visible) return;

                var target = ResolveAnimationTarget(element);

                // 初始化变换：设置起始位置（从下方20像素开始）
                var initialTransform = new TranslateTransform(0, 20);
                target.RenderTransform = initialTransform;
                target.Opacity = 0;

                element.Visibility = Visibility.Visible;

                var sb = new Storyboard();

                var fadeInAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                fadeInAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTarget(fadeInAnimation, target);
                Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath(UIElement.OpacityProperty));

                var slideAnimation = new DoubleAnimation
                {
                    From = 20,
                    To = 0,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                slideAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTarget(slideAnimation, target);
                Storyboard.SetTargetProperty(slideAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                sb.Children.Add(fadeInAnimation);
                sb.Children.Add(slideAnimation);

                sb.Begin((FrameworkElement)target);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void ShowWithSlideFromLeftAndFade(UIElement element, double duration = 0.25)
        {
            try
            {
                if (element.Visibility == Visibility.Visible) return;

                if (element == null)
                    throw new ArgumentNullException(nameof(element));

                var sb = new Storyboard();

                // 渐变动画
                var fadeInAnimation = new DoubleAnimation
                {
                    From = 0.5,
                    To = 1,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath(UIElement.OpacityProperty));

                // 滑动动画
                var slideAnimation = new DoubleAnimation
                {
                    From = element.RenderTransform.Value.OffsetX - 20, // 滑动距离
                    To = 0,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                Storyboard.SetTargetProperty(slideAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));

                sb.Children.Add(fadeInAnimation);
                sb.Children.Add(slideAnimation);

                element.Visibility = Visibility.Visible;
                element.RenderTransform = new TranslateTransform();

                sb.Begin((FrameworkElement)element);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void ShowWithScaleFromLeft(UIElement element, double duration = 0.2)
        {
            try
            {
                if (element.Visibility == Visibility.Visible) return;

                if (element == null)
                    throw new ArgumentNullException(nameof(element));

                var sb = new Storyboard();

                // 水平方向的缩放动画
                var scaleXAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));

                // 垂直方向的缩放动画
                var scaleYAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                scaleYAnimation.EasingFunction = new CubicEase();
                scaleXAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

                sb.Children.Add(scaleXAnimation);
                sb.Children.Add(scaleYAnimation);

                element.Visibility = Visibility.Visible;
                element.RenderTransformOrigin = new Point(0, 0.5); // 左侧中心点为基准
                element.RenderTransform = new ScaleTransform(0, 0);

                sb.Begin((FrameworkElement)element);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void ShowWithScaleFromRight(UIElement element, double duration = 0.2)
        {
            try
            {
                if (element.Visibility == Visibility.Visible) return;

                if (element == null)
                    throw new ArgumentNullException(nameof(element));

                var sb = new Storyboard();

                // 水平方向的缩放动画
                var scaleXAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                Storyboard.SetTargetProperty(scaleXAnimation, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));

                // 垂直方向的缩放动画
                var scaleYAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                Storyboard.SetTargetProperty(scaleYAnimation, new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));

                scaleYAnimation.EasingFunction = new CubicEase();
                scaleXAnimation.EasingFunction = new CubicEase();

                sb.Children.Add(scaleXAnimation);
                sb.Children.Add(scaleYAnimation);

                element.Visibility = Visibility.Visible;
                element.RenderTransformOrigin = new Point(1, 0.5); // 右侧中心点为基准
                element.RenderTransform = new ScaleTransform(0, 0);

                sb.Begin((FrameworkElement)element);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void HideWithSlideAndFade(UIElement element, double duration = 0.15)
        {
            try
            {
                if (element == null)
                    throw new ArgumentNullException(nameof(element));

                if (element.Visibility == Visibility.Collapsed) return;

                var target = ResolveAnimationTarget(element);

                var sb = new Storyboard();

                var fadeOutAnimation = new DoubleAnimation
                {
                    From = 1,
                    To = 0,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                fadeOutAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTargetProperty(fadeOutAnimation, new PropertyPath(UIElement.OpacityProperty));

                var slideAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 10,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                slideAnimation.EasingFunction = new CubicEase();

                Storyboard.SetTargetProperty(slideAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                sb.Children.Add(fadeOutAnimation);
                sb.Children.Add(slideAnimation);

                sb.Completed += (s, e) =>
                {
                    element.Visibility = Visibility.Collapsed;
                };

                target.RenderTransform = new TranslateTransform();
                sb.Begin((FrameworkElement)target);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void HideWithFadeOut(UIElement element, double duration = 0.15)
        {
            if (element.Visibility == Visibility.Collapsed) return;

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            var sb = new Storyboard();

            var fadeOutAnimation = new DoubleAnimation
            {
                From = 1,
                To = 0,
                Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
            };
            Storyboard.SetTargetProperty(fadeOutAnimation, new PropertyPath(UIElement.OpacityProperty));

            sb.Children.Add(fadeOutAnimation);

            sb.Completed += (s, e) =>
            {
                element.Visibility = Visibility.Collapsed;
            };

            sb.Begin((FrameworkElement)element);
        }

        public static void ShowPopupWithSlideAndFade(Popup popup, double duration = 0.15)
        {
            try
            {
                if (popup == null)
                    throw new ArgumentNullException(nameof(popup));

                if (popup.IsOpen) return;

                var child = popup.Child as FrameworkElement;
                if (child == null)
                {
                    popup.IsOpen = true;
                    return;
                }

                // 获取目标透明度（菜单透明度设置）
                double targetOpacity = GetPopupTargetOpacity(popup);

                child.Visibility = Visibility.Visible;
                child.Opacity = targetOpacity * 0.5;
                child.RenderTransform = new TranslateTransform(0, 10);

                popup.IsOpen = true;

                // 注意：置顶由 PopupManagerHelper 统一管理，
                // 此处不再调用 BringPopupToFront，避免重复置顶导致闪烁

                var sb = new Storyboard();

                var fadeInAnimation = new DoubleAnimation
                {
                    From = targetOpacity * 0.5,
                    To = targetOpacity,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                fadeInAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTargetProperty(fadeInAnimation, new PropertyPath(UIElement.OpacityProperty));

                var slideAnimation = new DoubleAnimation
                {
                    From = 10,
                    To = 0,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                slideAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTargetProperty(slideAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                sb.Children.Add(fadeInAnimation);
                sb.Children.Add(slideAnimation);

                sb.Begin(child);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void HidePopupWithSlideAndFade(Popup popup, double duration = 0.15)
        {
            try
            {
                if (popup == null)
                    throw new ArgumentNullException(nameof(popup));

                if (!popup.IsOpen) return;

                var child = popup.Child as FrameworkElement;
                if (child == null)
                {
                    popup.IsOpen = false;
                    return;
                }

                double targetOpacity = GetPopupTargetOpacity(popup);

                var sb = new Storyboard();

                var fadeOutAnimation = new DoubleAnimation
                {
                    From = targetOpacity,
                    To = 0,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                fadeOutAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTargetProperty(fadeOutAnimation, new PropertyPath(UIElement.OpacityProperty));

                var slideAnimation = new DoubleAnimation
                {
                    From = 0,
                    To = 10,
                    Duration = EffectiveDuration(TimeSpan.FromSeconds(duration))
                };
                slideAnimation.EasingFunction = new CubicEase();
                Storyboard.SetTargetProperty(slideAnimation, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                sb.Children.Add(fadeOutAnimation);
                sb.Children.Add(slideAnimation);

                sb.Completed += (s, e) =>
                {
                    popup.IsOpen = false;
                    child.Opacity = targetOpacity;
                    child.RenderTransform = new TranslateTransform();
                };

                child.RenderTransform = new TranslateTransform();
                sb.Begin(child);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        /// <summary>
        /// 根据 Popup 名称获取其目标透明度（菜单透明度设置）
        /// </summary>
        private static double GetPopupTargetOpacity(Popup popup)
        {
            var settings = Ink_Canvas.Windows.SettingsViews.Helpers.SettingsManager.Settings;
            var mw = Application.Current.MainWindow as Ink_Canvas.MainWindow;
            bool isPPTMode = mw != null && mw.IsInPPTPresentationMode;

            // 浮动栏菜单
            string name = popup.Name;
            if (name == "PenPalette" || name == "EraserSizePanel" || name == "BorderTools"
                || name == "BorderDrawShape" || name == "TwoFingerGestureBorder")
            {
                return isPPTMode
                    ? settings.Appearance.FloatingBarMenuOpacityInPPT
                    : settings.Appearance.FloatingBarMenuOpacity;
            }

            // 白板菜单
            if (name == "BoardTwoFingerGestureBorder" || name == "BackgroundPalette"
                || name == "BoardPenPalette" || name == "BoardEraserSizePanel"
                || name == "BoardBorderDrawShape" || name == "BoardImageOptionsPanel"
                || name == "BoardBorderToolsPopup")
            {
                return settings.Appearance.BoardMenuOpacity;
            }

            // 其他 Popup 默认不透明
            return 1.0;
        }

        #region LTSC D-05：布局动画改走合成层（RenderTransform）

        /// <summary>
        /// 取（或创建）目标控件上的 <see cref="TranslateTransform"/>，用于把位移动画放到合成层。
        /// </summary>
        /// <remarks>
        /// 只有当 RenderTransform 为空 / 恒等变换、或已经是 TranslateTransform 时才会写入。
        /// 若控件已经挂着别的变换（ScaleTransform / TransformGroup 等），直接返回 null，
        /// 由调用方回退到原来的 Margin 动画 —— 绝不允许覆盖别人已有的变换链。
        /// 注意：WPF 的 UIElement.RenderTransform 默认值是 Transform.Identity 而**不是** null
        /// （仓库里 MW_ElementsControls / MW_ImageInsert 也是按 Identity 判空的），
        /// 所以这里必须把 Identity 当成"空位"处理，否则所有控件都会被误判为已占用。
        /// 特别注意：ViewboxFloatingBar 的缩放挂在 LayoutTransform 上（ViewboxFloatingBarScaleTransform），
        /// 与 RenderTransform 是两条独立的链，所以这里不会把浮动栏缩放弄丢。
        /// </remarks>
        internal static TranslateTransform TryEnsureTranslateTransform(FrameworkElement target)
        {
            if (target == null) return null;

            if (target.RenderTransform is TranslateTransform existing) return existing;

            // Transform.Identity 是默认值，等价于"没有设置过"，可以安全接管。
            var current = target.RenderTransform;
            if (current != null && !current.Value.IsIdentity) return null;

            var tt = new TranslateTransform();
            target.RenderTransform = tt;
            return tt;
        }

        /// <summary>
        /// 取 Margin 变化在某个对齐方式下对应的"视觉位移量"。
        /// Left/Top 对齐时位移量与 Margin 同向；Right/Bottom 对齐时反向；Center 时取一半。
        /// </summary>
        internal static double MarginShiftX(Thickness from, Thickness to, HorizontalAlignment align)
        {
            switch (align)
            {
                case HorizontalAlignment.Right: return from.Right - to.Right;
                case HorizontalAlignment.Center: return (to.Left - from.Left - (to.Right - from.Right)) * 0.5;
                default: return to.Left - from.Left;
            }
        }

        internal static double MarginShiftY(Thickness from, Thickness to, VerticalAlignment align)
        {
            switch (align)
            {
                case VerticalAlignment.Bottom: return from.Bottom - to.Bottom;
                case VerticalAlignment.Center: return (to.Top - from.Top - (to.Bottom - from.Bottom)) * 0.5;
                default: return to.Top - from.Top;
            }
        }

        /// <summary>
        /// 用 RenderTransform.TranslateTransform 完成一次 Margin 位移动画。
        /// 动画期间 Margin 保持不变（也就不会触发任何布局传递），
        /// 结束后调用方必须调用 <see cref="LandMarginOnCompositionLayer"/> 落地到真实 Margin。
        /// </summary>
        /// <returns>true 表示已经改走合成层；false 表示控件不适合，调用方应回退到 ThicknessAnimation。</returns>
        internal static bool TryAnimateMarginOnCompositionLayer(
            FrameworkElement target, Thickness to, double seconds,
            out TimeSpan actualDuration, IEasingFunction easing = null)
        {
            actualDuration = TimeSpan.Zero;
            try
            {
                if (target == null) return false;

                var from = target.Margin;

                var tt = TryEnsureTranslateTransform(target);
                if (tt == null) return false;

                // 动画期间 Margin 保持在旧值，真正的画面位置 = Margin 位置 + 当前位移。
                // 读 tt.X / tt.Y 拿到的是**动画中的当前值**（WPF 中动画值会体现在属性读取上），
                // 这样即使用户快速连点、上一次动画还没跑完，也能从当前画面位置继续，不会跳变。
                double currentX = tt.X;
                double currentY = tt.Y;

                // 目标相对"Margin 起点"的总位移
                double targetDx = MarginShiftX(from, to, target.HorizontalAlignment);
                double targetDy = MarginShiftY(from, to, target.VerticalAlignment);

                // 还剩多少要走（用于判断是否值得起一个动画）
                double remainingX = targetDx - currentX;
                double remainingY = targetDy - currentY;

                if (Math.Abs(remainingX) <= 0.01 && Math.Abs(remainingY) <= 0.01) return true;

                var duration = EffectiveDuration(TimeSpan.FromSeconds(seconds));
                actualDuration = duration;
                var sb = new Storyboard();

                // 用与 ShowWithSlideFromBottomAndFade / HideWithSlideAndFade 一致的元素属性路径写法，
                // 这是本仓库已经在大量使用的成熟模式。
                // From 取当前位移而不是 0：动画被打断时能从画面当前位置平滑接上，不会先跳回起点。
                if (Math.Abs(remainingX) > 0.01)
                {
                    var ax = new DoubleAnimation { From = currentX, To = targetDx, Duration = duration };
                    if (easing != null) ax.EasingFunction = easing;
                    Storyboard.SetTarget(ax, target);
                    Storyboard.SetTargetProperty(ax, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.X)"));
                    sb.Children.Add(ax);
                }

                if (Math.Abs(remainingY) > 0.01)
                {
                    var ay = new DoubleAnimation { From = currentY, To = targetDy, Duration = duration };
                    if (easing != null) ay.EasingFunction = easing;
                    Storyboard.SetTarget(ay, target);
                    Storyboard.SetTargetProperty(ay, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));
                    sb.Children.Add(ay);
                }

                sb.Begin(target);
                return true;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
                return false;
            }
        }

        /// <summary>不关心动画时长的重载（等价于丢弃 out 参数）。</summary>
        internal static bool TryAnimateMarginOnCompositionLayer(
            FrameworkElement target, Thickness to, double seconds, IEasingFunction easing = null)
            => TryAnimateMarginOnCompositionLayer(target, to, seconds, out _, easing);

        /// <summary>
        /// 把位移动画落地：先解绑并归零 TranslateTransform，再硬设最终 Margin。
        /// 顺序很重要 —— 这样布局只在最后一帧算一次，而不是每一帧都算。
        /// </summary>
        internal static void LandMarginOnCompositionLayer(FrameworkElement target, Thickness finalMargin)
        {
            try
            {
                if (target == null) return;

                // 1) 先解绑并归零位移，让控件回到"旧 Margin"的基准位置
                if (target.RenderTransform is TranslateTransform tt)
                {
                    tt.BeginAnimation(TranslateTransform.XProperty, null);
                    tt.BeginAnimation(TranslateTransform.YProperty, null);
                    tt.X = 0;
                    tt.Y = 0;
                }

                // 2) 再写入最终 Margin
                target.Margin = finalMargin;

                // 3) 最后解绑 Margin 动画
                target.BeginAnimation(FrameworkElement.MarginProperty, null);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        #endregion

    }
}
