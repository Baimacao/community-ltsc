using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 浮动工具栏「折叠 / 展开」动画。
    ///
    /// 注意区分两件事（之前搞混过）：
    ///   - **折叠**：点主图标，收起工具项那部分，只留主图标
    ///   - **隐藏**：点「隐藏」键，整个浮动栏收到屏幕两侧
    ///   这个类只管**折叠**，隐藏走 MW_AutoFold 那套。
    ///
    /// 折叠的动画难点：工具项是用 Visibility.Collapsed 瞬时隐藏的，
    /// 直接切会「啪」一下消失。这里在切换前后插入一层过渡：
    ///   折叠：先淡出 + 收缩到 0 宽，动画结束再置 Collapsed
    ///   展开：先置 Visible 并从 0 宽展开 + 淡入
    ///
    /// 主图标的位置跟随由调用方（PlaceFloatingBarAfterHeadToggle）负责，
    /// 这里只改内容区的视觉状态。
    /// </summary>
    internal static class FloatingBarFoldAnimation
    {
        /// <summary>正在播放折叠/展开动画。其它模块要改这些值时先看这个。</summary>
        internal static bool IsPlaying { get; private set; }

        /// <summary>
        /// 折叠前的内容区尺寸缓存。
        ///
        /// 为什么需要：展开时控件刚从 Collapsed 切回来，ActualWidth 是 0，
        /// 而上一轮动画可能残留 Width 约束，UpdateLayout() 之后测出来的
        /// 是那个约束值（实测得到 8），不是真实内容宽度。
        /// 所以折叠时把宽度记下来，展开时直接用这个值当终值。
        /// </summary>
        private static readonly Dictionary<FrameworkElement, double> _savedSize =
            new Dictionary<FrameworkElement, double>();

        // 折叠时内容区收缩：用弹簧，和入场动画同一套手感
        private static readonly SpringAnimator.SpringPreset WidthPreset = new SpringAnimator.SpringPreset(0.38f, 0.92f);
        private static readonly SpringAnimator.SpringPreset OpacityPreset = new SpringAnimator.SpringPreset(0.30f, 0.95f);

        /// <summary>
        /// 播放折叠动画，结束后调用 onCompleted（通常是真正的 Visibility 切换 + 重排）。
        /// </summary>
        /// <param name="root">工具项容器（StackPanelFloatingBarRoot）</param>
        /// <param name="head">主图标，折叠时保持在原位不动</param>
        /// <param name="horizontal">横排还是竖排</param>
        /// <param name="onCompleted">动画结束后执行真正的隐藏切换</param>
        public static void PlayCollapse(Panel root, FrameworkElement head, bool horizontal, Action onCompleted)
        {
            Run(root, head, horizontal, collapsing: true, onCompleted);
        }

        /// <summary>
        /// 播放展开动画。调用前内容应该已经 Visible。
        /// </summary>
        public static void PlayExpand(Panel root, FrameworkElement head, bool horizontal, Action onCompleted = null)
        {
            Run(root, head, horizontal, collapsing: false, onCompleted);
        }

        private static void Run(Panel root, FrameworkElement head, bool horizontal, bool collapsing, Action onCompleted)
        {
            if (root == null)
            {
                onCompleted?.Invoke();
                return;
            }

            if (AnimationsHelper.AnimationsDisabled)
            {
                // 动效关闭：直接切，不做过渡
                RestoreAll(root, head, horizontal);
                onCompleted?.Invoke();
                return;
            }

            IsPlaying = true;

            try
            {
                // 只对「真正有内容、当前可见」的块做动画。
                // root.Children 里通常有 4 个：主图标 + 1 个有内容的 Border + 2 个本来就
                // Collapsed 的空 Border。对空块做动画是白费功夫，还会把时长算偏。
                var targets = new List<FrameworkElement>();
                foreach (var child in root.Children)
                {
                    if (!(child is FrameworkElement fe)) continue;
                    if (fe == head) continue;
                    // 折叠方向：只处理当前可见的
                    // 展开方向：调用方已经把 Visibility 切回来了，所以这里只按宽度过滤
                    if (fe.Visibility != Visibility.Visible) continue;
                    double size = horizontal ? fe.ActualWidth : fe.ActualHeight;
                    if (size <= 0.01 && collapsing) continue;   // 折叠时没内容的块不用管
                    targets.Add(fe);
                }

                if (targets.Count == 0)
                {
                    RestoreAll(root, head, horizontal);
                    IsPlaying = false;
                    onCompleted?.Invoke();
                    return;
                }

                var sw = System.Diagnostics.Stopwatch.StartNew();

                // 每个内容块：宽度/高度 + 透明度 两组弹簧
                var widths = new List<SpringAnimator.Value>();
                var opacities = new List<SpringAnimator.Value>();
                var startSizes = new List<double>();

                foreach (var t in targets)
                {
                    double currentSize = horizontal ? t.ActualWidth : t.ActualHeight;

                    if (collapsing)
                    {
                        // 折叠方向：现在的尺寸就是真实尺寸，记下来给展开用
                        if (currentSize > 1.0) _savedSize[t] = currentSize;
                    }
                    else
                    {
                        // 展开方向：ActualWidth 不可信（见 _savedSize 的说明），
                        // 优先用折叠时缓存的值。
                        if (_savedSize.TryGetValue(t, out var cached) && cached > 1.0)
                            currentSize = cached;
                        else if (currentSize <= 1.0)
                        {
                            // 没有缓存（比如启动后第一次展开），退回按主图标估算
                            var headW = head != null ? head.ActualWidth : 58;
                            currentSize = Math.Max(60, headW * 5.5);
                        }
                    }
                    startSizes.Add(currentSize);

                    var w = new SpringAnimator.Value(collapsing ? (float)currentSize : 0f);
                    w.SetPrecision(0.05f);
                    w.SetPreset(WidthPreset);
                    w.Set(collapsing ? 0f : (float)currentSize);
                    widths.Add(w);

                    var o = new SpringAnimator.Value(collapsing ? 1f : 0f);
                    o.SetPrecision(0.001f);
                    o.SetPreset(OpacityPreset);
                    o.Set(collapsing ? 0f : 1f);
                    opacities.Add(o);

                    if (!collapsing)
                        t.Opacity = 0;
                }

                var all = new List<SpringAnimator.Value>();
                all.AddRange(widths);
                all.AddRange(opacities);

                var vals = new List<SpringAnimator.Value>();
                vals.AddRange(all);

                SpringAnimator.Drive(vals, () =>
                {
                    for (int i = 0; i < targets.Count; i++)
                    {
                        var t = targets[i];
                        t.Opacity = opacities[i].Current;

                        double size = widths[i].Current;
                        if (size < 0.01) size = 0.01;

                        // 必须用 Width/Height 而不是 MaxWidth/MaxHeight：
                        // MaxWidth 只是上限，内容原本就窄的时候它压不小，
                        // 内容宽的时候它又撑不开——展开方向完全失效（实测卡在 8）。
                        // 显式 Width 才能双向控制。配合 ClipToBounds 裁掉溢出部分，
                        // 视觉上是"内容被逐步推开"而不是被压扁。
                        if (horizontal)
                        {
                            t.Width = size;
                            t.ClipToBounds = true;
                        }
                        else
                        {
                            t.Height = size;
                            t.ClipToBounds = true;
                        }
                    }
                }, () =>
                {
                    // 动画结束：清掉临时约束，恢复自然布局
                    foreach (var t in targets)
                    {
                        t.ClearValue(FrameworkElement.WidthProperty);
                        t.ClearValue(FrameworkElement.HeightProperty);
                        t.ClearValue(FrameworkElement.MaxWidthProperty);
                        t.ClearValue(FrameworkElement.MaxHeightProperty);
                        t.ClipToBounds = false;
                        t.Opacity = 1;
                    }

                    // 顺序很重要：**先把 IsPlaying 复位，再执行回调**。
                    //
                    // 原来是反的（回调在前、复位在后），结果展开动画结束时，
                    // 回调里调 SetFloatingBarHighlightPosition，而那个方法开头有
                    //     if (FloatingBarFoldAnimation.IsPlaying || ...) return;
                    // 守卫看到 IsPlaying 还是 true，直接把高亮拦掉了 ——
                    // 表现出来就是「折叠再展开后，选中高亮不见了」。
                    IsPlaying = false;

                    // 折叠时动画做完才真正隐藏——这一步由调用方负责（它知道该走哪套 Visibility 规则）
                    onCompleted?.Invoke();

                    LogHelper.WriteLogToFile(
                        $"[折叠动画] {(collapsing ? "折叠" : "展开")}完成，{targets.Count} 个内容块，用时 {sw.ElapsedMilliseconds}ms",
                        LogHelper.LogType.Info);

                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[折叠动画] 失败: {ex.Message}", LogHelper.LogType.Warning);
                RestoreAll(root, head, horizontal);
                IsPlaying = false;
                onCompleted?.Invoke();
            }
        }

        /// <summary>把动画期间加的临时约束清干净。</summary>
        private static void RestoreAll(Panel root, FrameworkElement head, bool horizontal)
        {
            try
            {
                if (root == null) return;
                foreach (var child in root.Children)
                {
                    if (!(child is FrameworkElement fe)) continue;
                    fe.ClearValue(FrameworkElement.WidthProperty);
                    fe.ClearValue(FrameworkElement.HeightProperty);
                    fe.ClearValue(FrameworkElement.MaxWidthProperty);
                    fe.ClearValue(FrameworkElement.MaxHeightProperty);
                    fe.ClipToBounds = false;
                    fe.Opacity = 1;
                }
            }
            catch { }
        }
    }
}
