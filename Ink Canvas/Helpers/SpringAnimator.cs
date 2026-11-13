using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// LTSC D-22：全应用统一的弹簧动画引擎。
    ///
    /// 模型照搬 codmtune 的 crates/motion（它本身是从 SwiftUI 的 spring 模型来的）：
    /// 一个值用「response + damping」描述它怎么动，
    ///   response —— 无阻尼振荡的周期（秒）。越小越快。
    ///   damping  —— 临界阻尼的比例。1 = 不过冲，小于 1 = 有回弹。
    /// 没有时长、没有 easing 曲线。
    ///
    /// 这个参数化的价值在于：同一组预设可以同时驱动「按钮按下」「翻页面板」
    /// 「进度条」而不会看起来像各自调过的——因为它们的物理性质是同一套。
    ///
    /// 三条设计纪律（照抄原实现，不改）：
    ///   1. **没有东西是"一直在跑"的。** step 返回"还有没有在动"，
    ///      调用方据此决定要不要下一帧。全部停稳就摘掉 Rendering 钩子，
    ///      否则就是一个常驻的空转渲染循环，笔记本上会一直耗电。
    ///   2. **固定子步长积分。** 一个大 dt 会被拆成若干 1/240s 的小步；
    ///      整步积分会把加速度当常量，直接冲过目标——那不是弹簧，是故障。
    ///   3. **数值推进和 WPF 属性分离。** 这个类只管算数，
    ///      落到 TranslateTransform / Opacity / Margin 由各处自己决定。
    /// </summary>
    internal static class SpringAnimator
    {
        // ── 预设 ────────────────────────────────────────────────
        // 名字和参数都照 codmtune，方便以后两边对齐。

        /// <summary>行与布局的位移，带一点轻过冲。</summary>
        internal static readonly SpringPreset Default = new SpringPreset(0.29f, 0.75f);
        /// <summary>整屏进出。</summary>
        internal static readonly SpringPreset Smooth = new SpringPreset(0.39f, 0.85f);
        /// <summary>要紧跟目标的进度类。</summary>
        internal static readonly SpringPreset Snappy = new SpringPreset(0.31f, 0.8f);
        /// <summary>弹出、落地带"啪"的一下。</summary>
        internal static readonly SpringPreset Bouncy = new SpringPreset(0.25f, 0.45f);
        /// <summary>按键反馈：快，不回弹。</summary>
        internal static readonly SpringPreset Interactive = new SpringPreset(0.14f, 1.0f);
        /// <summary>指数缓出，完全不过冲。</summary>
        internal static readonly SpringPreset Ease = new SpringPreset(0.22f, 1.0f, true);

        /// <summary>积分子步长。固定值，保证掉帧时不会积分发散。</summary>
        private const float SubStep = 1.0f / 240.0f;

        /// <summary>单帧最多补多少子步，防止长时间挂起后一口气算几千步。</summary>
        private const int MaxSubStepsPerFrame = 240;

        /// <summary>一帧最多推进多少秒，超过就截断（切屏回来后 elapsed 会很大）。</summary>
        private const double MaxFrameSeconds = 0.25;

        // ── 单值弹簧 ────────────────────────────────────────────

        /// <summary>一个朝目标弹的标量值。零值即可用。</summary>
        internal sealed class Value
        {
            private float _current;
            private float _target;
            private float _velocity;
            private float _precision;
            private SpringPreset _preset = Default;

            internal Value() { }

            internal Value(float v) { _current = v; _target = v; }

            internal Value(float v, SpringPreset preset) { _current = v; _target = v; _preset = preset; }

            /// <summary>当前展示值。</summary>
            internal float Current => _current;

            /// <summary>目标值。</summary>
            internal float Target => _target;

            /// <summary>当前速度（px/s）。打断时它会被保留，这是手感的关键。</summary>
            internal float Velocity => _velocity;

            /// <summary>朝 x 弹过去。**不改速度**。</summary>
            internal void Set(float x) { _target = x; }

            /// <summary>立刻到 x 并停住。</summary>
            internal void Jump(float x) { _current = x; _target = x; _velocity = 0f; }

            /// <summary>额外给一个速度，用于"顶一下"的反馈。</summary>
            internal void Kick(float v) { _velocity += v; }

            /// <summary>停稳判据的精度。像素用默认 0.05，0..1 的进度值要更小。</summary>
            internal void SetPrecision(float p) { _precision = p; }

            internal void SetPreset(SpringPreset preset) { _preset = preset; }

            internal bool Settled => _current == _target && _velocity == 0f;

            /// <summary>推进 dt 秒。返回"是否还在动"。</summary>
            internal bool Step(float dt)
            {
                float x = _current - _target;
                float vel = _velocity;

                if (_preset.Ease)
                {
                    // 指数衰减：第一帧就是全速，没有惯性带过目标。
                    float decay = (float)Math.Exp(-_preset.Rate * dt);
                    x *= decay;
                    vel = 0f;
                }
                else
                {
                    _preset.Coefficients(out float k, out float c);

                    // 固定子步长。整步积分会把加速度当常量，冲过目标。
                    int n = (int)(dt / SubStep) + 1;
                    if (n > MaxSubStepsPerFrame) n = MaxSubStepsPerFrame;
                    float h = dt / n;
                    for (int i = 0; i < n; i++)
                    {
                        vel += (-k * x - c * vel) * h;
                        x += vel * h;
                    }
                }

                float p = _precision == 0f ? 0.05f : _precision;
                if (Math.Abs(x) < p && Math.Abs(vel) < p * 10f)
                {
                    _current = _target;
                    _velocity = 0f;
                    return false;
                }

                _current = _target + x;
                _velocity = vel;
                return true;
            }
        }

        /// <summary>弹簧预设：无阻尼振荡周期（秒）+ 临界阻尼比。</summary>
        internal struct SpringPreset
        {
            internal readonly float Response;
            internal readonly float Damping;
            internal readonly bool Ease;

            internal SpringPreset(float response, float damping, bool ease = false)
            {
                Response = response <= 0f ? 0.01f : response;
                Damping = damping;
                Ease = ease;
            }

            internal float Rate => (float)(Math.PI * 2.0) / Response;

            internal void Coefficients(out float k, out float c)
            {
                float w = Rate;
                k = w * w;
                c = 2f * Damping * w;
            }
        }

        // ── 驱动 ────────────────────────────────────────────────

        /// <summary>一个受驱动的对象：一组 Value，加一个"算完之后怎么落地"的回调。</summary>
        internal sealed class Driver
        {
            internal readonly List<Value> Values = new List<Value>();
            internal Action OnStep;
            internal Action OnSettled;
            internal bool IsDisposed;
        }

        private static readonly List<Driver> Drivers = new List<Driver>();
        private static bool _hooked;
        private static long _lastTicks;

        /// <summary>
        /// 注册一个驱动器。<paramref name="values"/> 里任一值在动就会持续出帧，
        /// 全部停稳后自动停表并调一次 finished。
        /// </summary>
        internal static Driver Drive(IEnumerable<Value> values, Action onStep, Action onSettled = null)
        {
            var d = new Driver { OnStep = onStep, OnSettled = onSettled };
            if (values != null) d.Values.AddRange(values);
            Drivers.Add(d);
            EnsureHooked();
            return d;
        }

        /// <summary>注销一个驱动器（部件被销毁时调用，避免引用泄漏）。</summary>
        internal static void Release(Driver driver)
        {
            if (driver == null) return;
            driver.IsDisposed = true;
            driver.OnStep = null;
            driver.OnSettled = null;
            driver.Values.Clear();
            Drivers.Remove(driver);
        }

        /// <summary>把某个 Transform 的 X/Y 直接绑到弹簧上，最常用的一种落地方式。</summary>
        internal static void Animate(
            TranslateTransform transform,
            float targetX,
            float targetY,
            SpringPreset? presetX = null,
            SpringPreset? presetY = null,
            Action finished = null)
        {
            if (transform == null) return;

            if (AnimationsHelper.AnimationsDisabled)
            {
                transform.X = targetX;
                transform.Y = targetY;
                finished?.Invoke();
                return;
            }

            var (vx, vy) = GetOrCreateTransformPair(transform);

            vx.SetPreset(presetX ?? Default);
            vy.SetPreset(presetY ?? Default);
            vx.Set(targetX);
            vy.Set(targetY);

            Drive(new[] { vx, vy },
                () =>
                {
                    transform.X = vx.Current;
                    transform.Y = vy.Current;
                },
                finished);
        }

        /// <summary>停掉某个 Transform 上的弹簧并落位。</summary>
        internal static void Stop(TranslateTransform transform)
        {
            if (transform == null) return;
            if (_transformPairs.TryGetValue(transform, out var pair))
            {
                pair.x.Jump(pair.x.Target);
                pair.y.Jump(pair.y.Target);
                transform.X = pair.x.Current;
                transform.Y = pair.y.Current;
            }
        }

        private static readonly Dictionary<TranslateTransform, (Value x, Value y)> _transformPairs =
            new Dictionary<TranslateTransform, (Value x, Value y)>();

        private static (Value x, Value y) GetOrCreateTransformPair(TranslateTransform t)
        {
            if (_transformPairs.TryGetValue(t, out var pair)) return pair;
            // 从当前实际位移起步，不重置速度已经是 0，第一帧不会跳
            pair = (new Value((float)t.X), new Value((float)t.Y));
            _transformPairs[t] = pair;
            return pair;
        }

        private static void EnsureHooked()
        {
            if (_hooked) return;
            _hooked = true;
            _lastTicks = 0;
            CompositionTarget.Rendering += OnRendering;
        }

        private static void OnRendering(object sender, EventArgs e)
        {
            long now = DateTime.UtcNow.Ticks;
            if (_lastTicks == 0) { _lastTicks = now; return; }

            double elapsed = (now - _lastTicks) / (double)TimeSpan.TicksPerSecond;
            _lastTicks = now;
            if (elapsed <= 0) return;
            if (elapsed > MaxFrameSeconds) elapsed = MaxFrameSeconds;

            float dt = (float)elapsed;
            bool anyMoving = false;

            for (int i = Drivers.Count - 1; i >= 0; i--)
            {
                var d = Drivers[i];
                if (d.IsDisposed) { Drivers.RemoveAt(i); continue; }

                bool moving = false;
                for (int j = 0; j < d.Values.Count; j++)
                {
                    if (d.Values[j].Step(dt)) moving = true;
                }

                try { d.OnStep?.Invoke(); } catch { }

                if (!moving)
                {
                    var cb = d.OnSettled;
                    d.OnSettled = null;
                    try { cb?.Invoke(); } catch { }
                    Drivers.RemoveAt(i);
                }
                else
                {
                    anyMoving = true;
                }
            }

            if (!anyMoving)
            {
                CompositionTarget.Rendering -= OnRendering;
                _hooked = false;
                _lastTicks = 0;
                _transformPairs.Clear();
            }
        }
    }
}
