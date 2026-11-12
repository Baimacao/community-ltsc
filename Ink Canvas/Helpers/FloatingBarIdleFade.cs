using System;
using System.Windows;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// LTSC D-17：主悬浮窗的「画布空闲减淡」。
    ///
    /// 第三版重写。前两版都栽在同一个坑上：用一次性状态机（_isFaded 闸门 + 单纯 timer），
    /// 只要有一次状态没同步上（外部改过 Opacity、或恢复路径没走到），之后就会永久卡在
    /// 淡化状态、再也不回来。用户实测就是"整个工具栏一直是淡的"。
    ///
    /// 这一版彻底不用状态闸门，改成【每 500ms 自我纠正一次】：
    ///   算出现在距离上次动笔多久 → 该淡还是该亮 → 跟当前目标不同就补间过去
    ///
    /// 好处：任何一次算错，下一次 Tick 就自动纠正回来。没有能永久卡住的状态。
    ///
    /// 触发口径（按用户要求）：
    ///   - 只在非白板模式（currentMode == 0）生效
    ///   - 按「整个画布有没有动笔」计时，不是按「有没有碰到工具栏」——
    ///     触摸屏没有 hover，按后者算的话写一节课工具栏会一直淡着
    /// </summary>
    internal sealed class FloatingBarIdleFade
    {
        private readonly FrameworkElement _target;
        private readonly Func<bool> _shouldRun;
        private readonly DispatcherTimer _timer;

        /// <summary>停笔多久后开始变淡。</summary>
        private TimeSpan _idleDelay = TimeSpan.FromSeconds(4);

        /// <summary>淡到多少。</summary>
        private double _fadedOpacity = 0.45;

        private bool _enabled;
        private DateTime _lastActivity = DateTime.UtcNow;

        /// <summary>当前正在趋向的透明度。只用来避免每 500ms 重复启动同一个动画。</summary>
        private double _currentTarget = 1.0;

        /// <summary>基准（不淡时）的透明度。第一次读到时记下来。</summary>
        private double _baseOpacity = 1.0;
        private bool _hasBaseOpacity;

        internal FloatingBarIdleFade(FrameworkElement target, Func<bool> shouldRun)
        {
            _target = target;
            _shouldRun = shouldRun;

            // 500ms 一轮，足够跟手，又不会太费
            _timer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _timer.Tick += OnTick;
        }

        internal void Configure(bool enabled, double idleSeconds, double fadedOpacity)
        {
            _enabled = enabled;
            _idleDelay = TimeSpan.FromSeconds(Math.Max(1.0, idleSeconds));
            _fadedOpacity = Math.Max(0.0, Math.Min(1.0, fadedOpacity));

            if (!_enabled)
            {
                _timer.Stop();
                RestoreNow();
                return;
            }

            _lastActivity = DateTime.UtcNow;
            _currentTarget = 1.0;
            RestoreNow();

            if (!_timer.IsEnabled) _timer.Start();

            LogHelper.WriteLogToFile(
                $"[悬浮栏减淡] 已启用 | 停笔 {_idleDelay.TotalSeconds:0}s 后淡到 {_fadedOpacity:0.00}",
                LogHelper.LogType.Event);
        }

        /// <summary>
        /// 画布有任何操作时调用。只做一件事：把「上次活动时间」刷新到现在。
        /// 恢复动作交给 Tick 去做 —— 这样恢复路径只有一条，不会两边打架。
        /// </summary>
        internal void Touch()
        {
            _lastActivity = DateTime.UtcNow;
        }

        /// <summary>条件变化（进/出放映、切白板）时叫一声，下一轮 Tick 会重新评估。</summary>
        internal void ReArm()
        {
            _lastActivity = DateTime.UtcNow;
            if (_enabled && !_timer.IsEnabled) _timer.Start();
        }

        internal void Suspend()
        {
            _timer.Stop();
            RestoreNow();
        }

        internal void Dispose()
        {
            try
            {
                _timer.Stop();
                _timer.Tick -= OnTick;
            }
            catch { }
        }

        private void OnTick(object sender, EventArgs e)
        {
            if (!_enabled)
            {
                RestoreNow();
                return;
            }

            if (_target == null) return;

            // 条件不满足（白板模式等）→ 必须完全不透明，不能淡
            if (_shouldRun != null && !_shouldRun())
            {
                SetTarget(GetBaseOpacity());
                return;
            }

            var idle = DateTime.UtcNow - _lastActivity;
            double wanted = idle >= _idleDelay ? _fadedOpacity : GetBaseOpacity();
            SetTarget(wanted);
        }

        private void SetTarget(double opacity)
        {
            // 目标跟当前目标几乎一样 → 不动，避免每 500ms 重启动画
            if (Math.Abs(_currentTarget - opacity) < 0.01) return;

            _currentTarget = opacity;
            bool fadingOut = opacity < GetBaseOpacity();
            var duration = TimeSpan.FromMilliseconds(fadingOut ? 180 : 110);

            try
            {
                var anim = new DoubleAnimation
                {
                    To = opacity,
                    Duration = duration,
                    EasingFunction = fadingOut
                        ? (IEasingFunction)new CubicEase { EasingMode = EasingMode.EaseIn }
                        : new CubicEase { EasingMode = EasingMode.EaseOut },
                    FillBehavior = FillBehavior.HoldEnd
                };
                _target.BeginAnimation(UIElement.OpacityProperty, anim, HandoffBehavior.SnapshotAndReplace);
            }
            catch { }
        }

        private double GetBaseOpacity()
        {
            if (!_hasBaseOpacity)
            {
                try
                {
                    var raw = _target?.Opacity ?? 1.0;
                    _baseOpacity = raw > 0.05 ? raw : 1.0;
                }
                catch { _baseOpacity = 1.0; }
                _hasBaseOpacity = true;
            }
            return _baseOpacity;
        }

        private void RestoreNow()
        {
            try
            {
                if (_target == null) return;
                _currentTarget = GetBaseOpacity();
                _target.BeginAnimation(UIElement.OpacityProperty, null);
                _target.Opacity = GetBaseOpacity();
            }
            catch { }
        }
    }
}
