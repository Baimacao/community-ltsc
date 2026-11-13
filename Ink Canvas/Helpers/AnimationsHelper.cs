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

        /// <summary>
        /// LTSC（2026-11-13）C1：把旧的"固定时长"映射成弹簧预设。
        ///
        /// 为什么不是直接删掉 duration：这些方法的公开签名被大量调用方依赖，
        /// 改成物理参数会波及十几处调用点。这里保留 duration 作为"期望到位时间"，
        /// 映射成 response（响应时间）——数值上取同一个量，手感比线性缓动更自然。
        ///
        /// 阻尼取 0.78：超过 1.0 会过冲（弹过头再回来），太低又会发木。
        /// 0.78 接近临界阻尼，几乎不过冲，但收尾有弹性。
        /// </summary>
        /// <summary>
        /// LTSC（2026-11-13）C1：无过冲预设，给缩放和弹窗用。
        ///
        /// 为什么这两类不能用普通弹簧：
        ///   - 缩放：ScaleTransform 的中心固定在元素原点，过冲时会先缩到 1 以下再弹回，
        ///     老师快速连点面板时，这种抖动比线性缓动更明显。
        ///   - 弹窗：Popup 过冲会让整个窗口边缘抖一下，很扎眼。
        /// 取 damping = 1.0（临界阻尼），数学上保证不过冲，收尾仍然带弹性。
        /// </summary>
        internal static SpringAnimator.SpringPreset NoOvershootPreset(double duration)
        {
            var response = (float)Math.Max(0.06, Math.Min(duration, 0.6));
            return new SpringAnimator.SpringPreset(response, 1.0f);
        }

        /// <summary>
        /// 弹簧动画的 key。AnimateScalar 内部是 Dictionary&lt;object, Value&gt;，
        /// 按 Equals/GetHashCode 查表，所以这里必须实现值相等：
        /// 同一个元素 + 同一个通道 => 同一把 key（打断时能接上速度）；
        /// 同一个元素 + 不同通道 => 不同 key（透明度和位移互不干扰）。
        /// </summary>
        private sealed class AnimKey : IEquatable<AnimKey>
        {
            private readonly object _owner;
            private readonly string _channel;

            public AnimKey(object owner, string channel)
            {
                _owner = owner;
                _channel = channel;
            }

            public bool Equals(AnimKey other)
                => other != null
                   && ReferenceEquals(_owner, other._owner)
                   && string.Equals(_channel, other._channel, StringComparison.Ordinal);

            public override bool Equals(object obj) => Equals(obj as AnimKey);

            public override int GetHashCode()
            {
                unchecked
                {
                    int h = _owner == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(_owner);
                    return (h * 397) ^ (_channel == null ? 0 : _channel.GetHashCode());
                }
            }
        }

        internal static SpringAnimator.SpringPreset PresetFromDuration(double duration)
        {
            // 夹在合理区间：太短会硬弹，太长会拖沓
            var response = (float)Math.Max(0.06, Math.Min(duration, 0.6));
            return new SpringAnimator.SpringPreset(response, 0.78f);
        }

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

            element.Visibility = Visibility.Visible;

            // C1：改用弹簧。key 用控件本身，重复触发会保留速度，不会从 0.5 重新起步。
            element.Opacity = 0.5;
            // key 必须稳定：用元素本身。AnimateScalar 的字典按引用查，
            // 传 new object[]{...} 每次都新建，既打断不了动画，还会在字典里越积越多。
            SpringAnimator.AnimateScalar(
                new AnimKey(element, "opacity"),
                1.0,
                v => element.Opacity = v,
                PresetFromDuration(duration));
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

                // C1：弹簧接管。透明度和位移各一条，key 都挂 target，
                // 重复触发时保留速度，不会从 20px 处重新起跳。
                var preset = PresetFromDuration(duration);
                SpringAnimator.AnimateScalar(
                    new AnimKey(target, "opacity"), 1.0,
                    v => target.Opacity = v, preset);
                SpringAnimator.AnimateScalar(
                    new AnimKey(target, "slideY"), 0.0,
                    v => initialTransform.Y = v, preset);
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

                // C1：弹簧。透明度从 0.5 起、位移从 -20 起，
                // 都挂同一个 translate，两条通道各用各的 key，互不覆盖。
                var translate = new TranslateTransform(-20, 0);
                element.Visibility = Visibility.Visible;
                element.RenderTransform = translate;
                element.Opacity = 0.5;

                var preset = PresetFromDuration(duration);
                SpringAnimator.AnimateScalar(
                    new AnimKey(element, "opacity"), 1.0,
                    v => element.Opacity = v, preset);
                SpringAnimator.AnimateScalar(
                    new AnimKey(element, "slideX"), 0.0,
                    v => translate.X = v, preset);
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

                // C1：弹簧缩放。用无过冲预设——
                // 缩放过冲会先缩到 1 以下再弹回，老师连点面板时比线性更抖。
                var scale = new ScaleTransform(0, 0);
                element.Visibility = Visibility.Visible;
                element.RenderTransformOrigin = new Point(0, 0.5); // 左侧中心点为基准
                element.RenderTransform = scale;

                var preset = NoOvershootPreset(duration);
                SpringAnimator.AnimateScalar(
                    new AnimKey(element, "scaleX"), 1.0, v => scale.ScaleX = v, preset);
                SpringAnimator.AnimateScalar(
                    new AnimKey(element, "scaleY"), 1.0, v => scale.ScaleY = v, preset);
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

                // C1：弹簧缩放。用无过冲预设——
                // 缩放过冲会先缩到 1 以下再弹回，老师连点面板时比线性更抖。
                var scale = new ScaleTransform(0, 0);
                element.Visibility = Visibility.Visible;
                element.RenderTransformOrigin = new Point(1, 0.5); // 右侧中心点为基准
                element.RenderTransform = scale;

                var preset = NoOvershootPreset(duration);
                SpringAnimator.AnimateScalar(
                    new AnimKey(element, "scaleX"), 1.0, v => scale.ScaleX = v, preset);
                SpringAnimator.AnimateScalar(
                    new AnimKey(element, "scaleY"), 1.0, v => scale.ScaleY = v, preset);
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

                // C1：弹簧淡出+下移。两条通道并行，到位后收起元素。
                var translate = new TranslateTransform(0, 0);
                target.RenderTransform = translate;

                var preset = PresetFromDuration(duration);
                SpringAnimator.AnimateScalar(
                    new AnimKey(target, "opacity"), 0.0,
                    v => target.Opacity = v, preset);
                SpringAnimator.AnimateScalar(
                    new AnimKey(target, "slideY"), 10.0,
                    v => translate.Y = v, preset,
                    () => element.Visibility = Visibility.Collapsed);
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        public static void HideWithFadeOut(UIElement element, double duration = 0.15)
        {
            if (element.Visibility == Visibility.Collapsed) return;

            if (element == null)
                throw new ArgumentNullException(nameof(element));

            // C1：弹簧淡出。到位后由 finished 回调收起元素——
            // 这个 Collapsed 收尾不能丢，丢了元素还在，只是透明。
            SpringAnimator.AnimateScalar(
                new AnimKey(element, "opacity"),
                0.0,
                v => element.Opacity = v,
                PresetFromDuration(duration),
                () => element.Visibility = Visibility.Collapsed);
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

                // C1：弹窗用无过冲预设——过冲会让窗口边缘抖一下，很扎眼。
                var translate = new TranslateTransform(0, 10);
                child.RenderTransform = translate;

                var preset = NoOvershootPreset(duration);
                SpringAnimator.AnimateScalar(
                    new AnimKey(child, "opacity"), targetOpacity,
                    v => child.Opacity = v, preset);
                SpringAnimator.AnimateScalar(
                    new AnimKey(child, "slideY"), 0.0,
                    v => translate.Y = v, preset);
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

                // C1：弹窗淡出。收尾必须把透明度和位移复位，
                // 否则下次打开会带着上次的残留状态（看起来像没弹出来）。
                var translate = new TranslateTransform(0, 0);
                child.RenderTransform = translate;

                var preset = NoOvershootPreset(duration);
                SpringAnimator.AnimateScalar(
                    new AnimKey(child, "opacity"), 0.0,
                    v => child.Opacity = v, preset);
                SpringAnimator.AnimateScalar(
                    new AnimKey(child, "slideY"), 10.0,
                    v => translate.Y = v, preset,
                    () =>
                    {
                        popup.IsOpen = false;
                        child.Opacity = targetOpacity;
                        child.RenderTransform = new TranslateTransform();
                    });
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

                // C1：改用弹簧驱动。原来的 Storyboard 版靠 From=currentX 实现"打断接续"，
                // 弹簧天生就带速度，打断时接着走、不会跳回起点，语义更准。
                // easing 参数保留在签名里是为了不破坏调用方，弹簧下不再使用。
                var preset = PresetFromDuration(seconds);
                SpringAnimator.Animate(tt, (float)targetDx, (float)targetDy, preset, preset);
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
