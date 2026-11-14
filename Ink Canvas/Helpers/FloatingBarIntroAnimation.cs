using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 浮动工具栏启动入场动画。
    ///
    /// 设计意图：不只是"过渡"，要**赏心悦目**。
    /// 做法是让三层动作各有各的节奏，而不是一起动：
    ///
    ///   1. 整条栏从下方升起 —— 弹簧阻尼接近临界，末端平顺不撞墙
    ///   2. 主图标单独再弹一下 —— 比整体晚一点起，缩放带过冲，像"落地"
    ///   3. 工具项从主图标那边**依次展开** —— 每个都比前一个晚一点淡入并滑向最终位置
    ///   4. 全程带一层很轻的柔光，随整体淡出 —— 让"出现"这件事有质感
    ///
    /// 关键点：**所有层共用一个时间基准**，但各自的弹簧参数不同。
    /// 一起动会显得机械，错开才有呼吸感。
    ///
    /// 抽动问题的根因（2026-11-14 修）：
    /// ApplyTheme() 会在启动阶段直接写 ViewboxFloatingBar.Opacity = 1.0，
    /// 和这里正在推的透明度打架。现在动画期间挂一个标记，
    /// 主题那边看到标记就跳过这一行。
    /// </summary>
    internal static class FloatingBarIntroAnimation
    {
        /// <summary>动画是否正在播放。其它模块要改浮动栏透明度时先看这个。</summary>
        internal static bool IsPlaying { get; private set; }

        /// <summary>从屏幕下方升起的起始偏移（像素）。</summary>
        /// <summary>从屏幕下方升起的起始偏移（像素）。App 在 Show() 前要用它预置初始状态。</summary>
        internal const double RiseOffset = 90.0;

        /// <summary>工具项从主图标方向展开的起始偏移（像素）。</summary>
        private const double StaggerOffset = 26.0;

        /// <summary>工具项之间的启动间隔（毫秒）。越靠后越晚，形成"依次展开"。</summary>
        private const double ItemStaggerMs = 130.0;

        /// <summary>正常状态下工具项之间的间距。来自 ToolbarRegistry.Populate 的 3px。</summary>
        private const double NormalSpacing = 3.0;

        /// <summary>入场时把间距拉开再收回。</summary>
        private const double StartSpacing = 14.0;

        // ── 预设：各层用不同节奏 ──────────────────────────────

        /// <summary>整条栏升起：慢、末端平顺。</summary>
        private static readonly SpringAnimator.SpringPreset RisePreset = new SpringAnimator.SpringPreset(0.85f, 0.88f);

        /// <summary>主图标落地：稍快，带一点过冲，有"啪"的一下。</summary>
        private static readonly SpringAnimator.SpringPreset HeadPreset = new SpringAnimator.SpringPreset(0.42f, 0.55f);

        /// <summary>工具项展开：中等速度，不过冲，避免文字晃。</summary>
        private static readonly SpringAnimator.SpringPreset ItemPreset = new SpringAnimator.SpringPreset(0.62f, 0.88f);

        /// <summary>柔光：最慢，几乎察觉不到在动，只是从有到无。</summary>
        private static readonly SpringAnimator.SpringPreset GlowPreset = new SpringAnimator.SpringPreset(1.2f, 0.95f);

        /// <summary>
        /// 播放一次入场动画。
        /// </summary>
        /// <param name="head">主图标（BorderFloatingBarMoveControls）</param>
        /// <param name="root">工具项所在的 StackPanel</param>
        /// <param name="horizontal">工具栏是横排还是竖排</param>
        /// <param name="slideTarget">整条栏的外层（ViewboxFloatingBar），用它做升起</param>
        public static void Play(FrameworkElement head, Panel root, bool horizontal, FrameworkElement slideTarget = null)
        {
            if (AnimationsHelper.AnimationsDisabled)
            {
                LogHelper.WriteLogToFile("[入场动画] 全局动效已关闭，直接显示最终态", LogHelper.LogType.Info);
                RestoreNormal(head, root, horizontal, slideTarget);
                return;
            }

            IsPlaying = true;
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // ── 第 1 层：整条栏从下方升起 + 淡入 ──
                if (slideTarget != null)
                {
                    if (!(slideTarget.RenderTransform is TranslateTransform riseTt))
                    {
                        riseTt = new TranslateTransform(0, RiseOffset);
                        slideTarget.RenderTransform = riseTt;
                    }
                    else
                    {
                        riseTt.Y = RiseOffset;
                    }

                    slideTarget.Opacity = 0;

                    // 升起位移
                    var riseVal = new SpringAnimator.Value((float)RiseOffset);
                    riseVal.SetPreset(RisePreset);
                    riseVal.Set(0f);

                    SpringAnimator.Drive(new[] { riseVal }, () =>
                    {
                        if (slideTarget.RenderTransform is TranslateTransform t)
                            t.Y = riseVal.Current;
                    }, () =>
                    {
                        // 落地归零，否则残留偏移会叠加到拖动逻辑上
                        try
                        {
                            if (slideTarget.RenderTransform is TranslateTransform t)
                            {
                                t.X = 0;
                                t.Y = 0;
                            }
                        }
                        catch { }
                    });

                    // 升起时的淡入
                    var riseOpacity = new SpringAnimator.Value(0f);
                    riseOpacity.SetPrecision(0.001f);
                    riseOpacity.SetPreset(RisePreset);
                    riseOpacity.Set(1f);
                    SpringAnimator.Drive(new[] { riseOpacity }, () =>
                    {
                        slideTarget.Opacity = riseOpacity.Current;
                    }, null);
                }

                // ── 第 2 层：主图标单独弹一下 ──
                if (head != null)
                {
                    head.RenderTransformOrigin = new Point(0.5, 0.5);
                    if (!(head.RenderTransform is ScaleTransform headScale))
                    {
                        headScale = new ScaleTransform(0.82, 0.82);
                        head.RenderTransform = headScale;
                    }
                    else
                    {
                        headScale.ScaleX = 0.82;
                        headScale.ScaleY = 0.82;
                    }

                    var headVal = new SpringAnimator.Value(0.82f);
                    headVal.SetPrecision(0.001f);
                    headVal.SetPreset(HeadPreset);
                    headVal.Set(1f);

                    SpringAnimator.Drive(new[] { headVal }, () =>
                    {
                        headScale.ScaleX = headVal.Current;
                        headScale.ScaleY = headVal.Current;
                    }, () =>
                    {
                        // 收尾归位
                        try
                        {
                            headScale.ScaleX = 1;
                            headScale.ScaleY = 1;
                        }
                        catch { }
                    });
                }

                // ── 第 3 层：工具项依次展开 ──
                if (root == null)
                {
                    FinishLater(sw, head, root, horizontal, slideTarget);
                    return;
                }

                var items = new List<FrameworkElement>();
                var debugNames = new List<string>();
                foreach (var child in root.Children)
                {
                    debugNames.Add((child?.GetType().Name ?? "null") + "#" + ((child as FrameworkElement)?.Name ?? ""));
                    if (child is FrameworkElement fe && fe != head)
                        items.Add(fe);
                }

                LogHelper.WriteLogToFile(
                    $"[入场动画] 开始：主图标 + {items.Count} 个工具项，{(horizontal ? "横排" : "竖排")}",
                    LogHelper.LogType.Event);

                if (items.Count == 0)
                {
                    FinishLater(sw, head, root, horizontal, slideTarget);
                    return;
                }

                // ── 工具项：按时间点逐个创建并启动 ──
                //
                // 三个方案都试过：
                //  1. 一个 Drive 里按延迟 skip —— 弹簧提前跑到终点，延迟一到就"蹦出来"
                //  2. 每项一个 DispatcherTimer —— Tick 一次都没触发（被 Rendering 饿死）
                //  3. 预先建好弹簧再按住 —— Jump 会把目标也设成起点，Drive 立刻判定 settled 退出
                //
                // 最终方案：用 CompositionTarget.Rendering 自己做时间轮，
                // 到时间点才 new Value 并 Drive。既不提前，也不依赖 Timer 优先级。
                var itemStartSw = System.Diagnostics.Stopwatch.StartNew();
                int nextToStart = 0;

                EventHandler onFrame = null;
                onFrame = (sender, args) =>
                {
                    try
                    {
                        double nowMs = itemStartSw.Elapsed.TotalMilliseconds;

                        // 到点就启动下一个（可能一次跨过多个）
                        while (nextToStart < items.Count && nowMs >= 350 + nextToStart * ItemStaggerMs)
                        {
                            int idx = nextToStart;
                            nextToStart++;
                            StartItemAnimation(items[idx], horizontal);
                        }

                        // 全部启动且都跑完了就摘掉钩子
                        if (nextToStart >= items.Count)
                        {
                            double lastStart = 350 + (items.Count - 1) * ItemStaggerMs;
                            // 留足最后一个跑完的时间（ItemPreset 0.62s）
                            if (nowMs > lastStart + 900)
                            {
                                CompositionTarget.Rendering -= onFrame;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"[入场动画] 时间轮异常: {ex.Message}", LogHelper.LogType.Warning);
                        try { CompositionTarget.Rendering -= onFrame; } catch { }
                    }
                };
                CompositionTarget.Rendering += onFrame;

                LogHelper.WriteLogToFile(
                    $"[入场动画] 已排程 {items.Count} 个工具项，错开 {ItemStaggerMs}ms，首个延迟 350ms（渲染时间轮）",
                    LogHelper.LogType.Event);

                FinishLater(sw, head, root, horizontal, slideTarget);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"浮动工具栏入场动画失败: {ex.Message}", LogHelper.LogType.Warning);
                RestoreNormal(head, root, horizontal, slideTarget);
                IsPlaying = false;
            }
        }

        /// <summary>
        /// 启动单个工具项的展开动画。每个项独立 Drive，互不干扰。
        /// </summary>
        private static void StartItemAnimation(FrameworkElement item, bool horizontal)
        {
            if (item == null) return;

            item.Opacity = 0;
            SetSpacing(item, horizontal, StartSpacing);

            var op = new SpringAnimator.Value(0f);
            op.SetPrecision(0.001f);
            op.SetPreset(ItemPreset);
            op.Set(1f);

            var sp = new SpringAnimator.Value((float)StartSpacing);
            sp.SetPrecision(0.01f);
            sp.SetPreset(ItemPreset);
            sp.Set((float)NormalSpacing);

            SpringAnimator.Drive(new[] { op, sp }, () =>
            {
                item.Opacity = op.Current;
                SetSpacing(item, horizontal, Math.Max(NormalSpacing, sp.Current));
            }, () =>
            {
                item.Opacity = 1;
                SetSpacing(item, horizontal, NormalSpacing);
            });
        }

        /// <summary>
        /// 等最慢的那一层结束（升起动画），再统一收尾。
        /// 用固定延时而不是监听所有 Drive，逻辑简单且不会因为某个 Drive 提前 settled 而误判。
        /// </summary>
        private static void FinishLater(System.Diagnostics.Stopwatch sw, FrameworkElement head, Panel root,
                                        bool horizontal, FrameworkElement slideTarget)
        {
            double waitMs = 2400;   // 略长于最慢的升起（约 1.6s），保证全部走完
            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(waitMs)
            };
            timer.Tick += (s, e) =>
            {
                timer.Stop();
                try
                {
                    RestoreNormal(head, root, horizontal, slideTarget);
                    LogHelper.WriteLogToFile(
                        $"[入场动画] 全部结束，总用时 {sw.ElapsedMilliseconds}ms",
                        LogHelper.LogType.Event);
                }
                catch { }
                finally
                {
                    IsPlaying = false;
                }
            };
            timer.Start();
        }

        /// <summary>按方向设置工具项的间距（只改一侧，保留另一边）。</summary>
        private static void SetSpacing(FrameworkElement item, bool horizontal, double value)
        {
            var m = item.Margin;
            item.Margin = horizontal
                ? new Thickness(value, m.Top, m.Right, m.Bottom)
                : new Thickness(m.Left, value, m.Right, m.Bottom);
        }

        /// <summary>
        /// 恢复成正常运行时的样子。
        /// 动画被禁用、播放失败、或播放结束时都会走这里。
        /// </summary>
        private static void RestoreNormal(FrameworkElement head, Panel root, bool horizontal, FrameworkElement slideTarget = null)
        {
            try
            {
                // 外层：位移归零、透明度归 1
                if (slideTarget != null)
                {
                    if (slideTarget.RenderTransform is TranslateTransform t)
                    {
                        t.X = 0;
                        t.Y = 0;
                    }
                    slideTarget.Opacity = 1;
                }

                // 主图标：缩放归 1
                if (head != null)
                {
                    head.Opacity = 1;
                    if (head.RenderTransform is ScaleTransform s)
                    {
                        s.ScaleX = 1;
                        s.ScaleY = 1;
                    }
                }

                if (root == null) return;
                foreach (var child in root.Children)
                {
                    if (!(child is FrameworkElement fe)) continue;
                    fe.Opacity = 1;

                    // 主图标在 Left/Top 位置时带 3px 让位，其余一律正常间距
                    if (ReferenceEquals(fe, head))
                        fe.Margin = horizontal ? new Thickness(3, 0, 0, 0) : new Thickness(0, 3, 0, 0);
                    else
                        SetSpacing(fe, horizontal, NormalSpacing);
                }
            }
            catch { }
        }
    }
}
