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
            // LTSC D-26：修一个真 bug，同时改走弹簧引擎。
            //
            // 原来这两件事被写在同一个判据里：
            //   if (Math.Abs(_currentTarget - opacity) < 0.01) return;
            // _currentTarget 是"上次设的目标"，不是"当前实际不透明度"。
            // 于是动画一旦被打断（ResetNow / 外部改了 Opacity），
            // 实际值已经偏了，但 _currentTarget 还记着老目标——
            // 目标再变回老值时判据认为"没变"，直接 return，
            // 淡出就永远恢复不过来了。这正是 11-12 那次"一直是淡的"的同一个形状。
            //
            // 现在拆成两条判据：
            //   1. 目标没变 → 不动（避免每 500ms 重启）
            //   2. 目标变了，但实际值已经到位 → 直接拍到位，不启动动画
            bool targetChanged = Math.Abs(_currentTarget - opacity) >= 0.01;
            if (!targetChanged) return;

            _currentTarget = opacity;

            double actual = 1.0;
            try { actual = _target?.Opacity ?? 1.0; } catch { }

            // 实际值已经到位就不折腾动画了
            if (Math.Abs(actual - opacity) < 0.005)
            {
                try
                {
                    _target.BeginAnimation(UIElement.OpacityProperty, null);
                    _target.Opacity = opacity;
                    SpringAnimator.SyncScalar(_target, opacity);
                }
                catch { }
                return;
            }

            // 淡出走 Smooth（别让人注意到它在动），淡入走 Snappy（要跟手）
            bool fadingOut = opacity < GetBaseOpacity();
            var preset = fadingOut ? SpringAnimator.Smooth : SpringAnimator.Snappy;

            try
            {
                // 先解绑旧的 DoubleAnimation，交给弹簧接管
                _target.BeginAnimation(UIElement.OpacityProperty, null);
                _target.Opacity = actual;
                SpringAnimator.SyncScalar(_target, actual);

                SpringAnimator.AnimateScalar(
                    _target,
                    opacity,
                    v => _target.Opacity = v,
                    preset);
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
