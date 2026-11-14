using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 浮动工具栏启动入场动画。
    ///
    /// 需求：主图标先出现 → 工具项随后出现且**间距比正常值大** → 连贯收回到正常间距。
    /// 不允许「一步一步停顿式」播放，所以整段用同一个弹簧驱动器推进，
    /// 不用 Task.Delay 串行等待。
    ///
    /// 实现要点：
    ///   - 正常状态下工具项之间没有 Margin（间距为 0），靠各自的宽度挨着。
    ///     入场时给每个工具项临时加水平/垂直 Margin，再从大收到 0。
    ///   - 主图标用 Bouncy（弹出的感觉），间距用 Smooth（不过冲，避免卡片晃动）。
    ///   - 全部走 SpringAnimator，窗口最小化或动画被禁用时会自动降级为瞬间到位。
    /// </summary>
    internal static class FloatingBarIntroAnimation
    {
        /// <summary>
        /// 从屏幕下方滑入的起始偏移（像素）。正值 = 从下面升上来。
        /// </summary>
        private const double SlideInOffset = 90.0;

        /// <summary>
        /// 正常状态下工具项之间的间距。
        /// 来自 ToolbarRegistry.Populate：非第一个 segment 一律 Margin = (3,0,0,0) 或 (0,3,0,0)。
        /// **不是 0**，恢复时必须还原成这个值，否则工具栏会比平时更紧。
        /// </summary>
        private const double NormalSpacing = 3.0;

        /// <summary>入场时把间距拉到多大再收回。必须明显大于 NormalSpacing 才看得出「拉开」。</summary>
        private const double StartSpacing = 14.0;

        /// <summary>工具项依次出现的间隔（毫秒）。只影响透明度起始时间，不阻塞主流程。</summary>
        private const double ItemStaggerMs = 70.0;

        private static readonly Dictionary<object, double> _itemDelays = new Dictionary<object, double>();

        /// <summary>
        /// 播放一次入场动画。
        /// </summary>
        /// <param name="head">主图标（BorderFloatingBarMoveControls）</param>
        /// <param name="root">工具项所在的 StackPanel（StackPanelFloatingBarRoot）</param>
        /// <param name="horizontal">工具栏是横排还是竖排</param>
        public static void Play(FrameworkElement head, Panel root, bool horizontal, FrameworkElement slideTarget = null)
        {
            if (AnimationsHelper.AnimationsDisabled)
            {
                // 动画被全局关闭时不播，直接给最终态——这是有意设计，
                // 不是失败路径，所以记 Info 不记 Warning。
                LogHelper.WriteLogToFile("[入场动画] 全局动效已关闭，直接显示最终态", LogHelper.LogType.Info);
                RestoreNormal(head, root, horizontal);
                return;
            }

            try
            {
                // ── 1. 主图标：从 0.6 弹到 1，带过冲 ──
                if (head != null)
                {
                    head.Opacity = 0;
                    head.RenderTransformOrigin = new Point(0.5, 0.5);
                    if (!(head.RenderTransform is ScaleTransform))
                        head.RenderTransform = new ScaleTransform(0.6, 0.6);

                    // 透明度用 Smooth（0.39s）而不是 Interactive（0.14s），
                    // 慢下来才有"浮现"的感觉，不是"啪一下出现"。
                    SpringAnimator.AnimateScalar(head, 1.0, v => head.Opacity = v,
                        new SpringAnimator.SpringPreset(0.6f, 0.9f));

                    var scale = head.RenderTransform as ScaleTransform;
                    if (scale != null)
                    {
                        SpringAnimator.SyncScalar(scale, 0.6);
                        // Bouncy 有回弹，但配上更长的 response 才丝滑；
                        // 用自定义预设：response 0.55s、damping 0.62（有一点过冲，但不弹跳）。
                        SpringAnimator.AnimateScalar(scale, 1.0, v =>
                        {
                            scale.ScaleX = v;
                            scale.ScaleY = v;
                        }, new SpringAnimator.SpringPreset(0.55f, 0.62f));
                    }
                }

                // ── 1.5 整条浮动栏从屏幕下方滑入 ──
                // 用 TranslateTransform 做 Y 偏移，不动 Margin：
                //   - Margin 是定位锚点，改了会让后续拖动/吸附算错基准
                //   - RenderTransform 是做视觉位移的标准位置，落地归零即可
                // 曲线速度靠弹簧的 damping < 1（先快后缓，到位前轻微减速），
                // 不是线性匀速。
                if (slideTarget != null)
                {
                    if (!(slideTarget.RenderTransform is TranslateTransform slideTt))
                    {
                        slideTt = new TranslateTransform(0, SlideInOffset);
                        slideTarget.RenderTransform = slideTt;
                    }
                    else
                    {
                        slideTt.Y = SlideInOffset;
                    }

                    // 起始透明度 0，和滑入同步淡入
                    slideTarget.Opacity = 0;
                    SpringAnimator.AnimateScalar(slideTarget, 1.0, v => slideTarget.Opacity = v,
                        new SpringAnimator.SpringPreset(0.85f, 0.95f));

                    var slideSw = System.Diagnostics.Stopwatch.StartNew();
                    int slideFrames = 0;

                    // 不用 Animate(transform,...)，改用自己建 Value + Drive：
                    // Animate 内部用的是共享的 transform pair，无法在 onStep 里采样调试，
                    // 而且实测它的收敛比预期快得多（392ms vs 模拟 1683ms），需要看清中间过程。
                    var slideVal = new SpringAnimator.Value((float)SlideInOffset);
                    // response 0.85s / damping 0.85：
                    // 实测 1.1s 跑出 2082ms，偏拖；0.85 约 1.4s，仍然是"慢"的范畴。
                    // damping 0.85 保留一点末端缓停，但把过冲压到肉眼看不见。
                    slideVal.SetPreset(new SpringAnimator.SpringPreset(0.85f, 0.85f));
                    slideVal.Set(0f);

                    LogHelper.WriteLogToFile(
                        $"[入场动画] 滑入开始：Y={SlideInOffset}px -> 0，response 0.85s / damping 0.85",
                        LogHelper.LogType.Event);

                    SpringAnimator.Drive(new[] { slideVal }, () =>
                    {
                        slideFrames++;
                        if (slideTarget.RenderTransform is TranslateTransform tt)
                            tt.Y = slideVal.Current;
                    }, () =>
                    {
                        try
                        {
                            if (slideTarget.RenderTransform is TranslateTransform t)
                            {
                                t.X = 0;
                                t.Y = 0;
                            }
                            slideTarget.Opacity = 1;
                            LogHelper.WriteLogToFile(
                                $"[入场动画] 滑入结束，用时 {slideSw.ElapsedMilliseconds}ms，{slideFrames} 帧",
                                LogHelper.LogType.Event);
                        }
                        catch { }
                    });

                    // 透明度单独驱动
                    SpringAnimator.AnimateScalar(slideTarget, 1.0, v => slideTarget.Opacity = v,
                        new SpringAnimator.SpringPreset(0.85f, 0.95f));
                }

                // ── 2. 工具项：逐个淡入 + 间距从大收到 0 ──
                if (root == null) return;

                var items = new List<FrameworkElement>();
                var debugNames = new List<string>();
                foreach (var child in root.Children)
                {
                    var t = child?.GetType().Name ?? "null";
                    var n = (child as FrameworkElement)?.Name ?? "";
                    debugNames.Add($"{t}#{n}");
                    if (child is FrameworkElement fe && fe != head)
                        items.Add(fe);
                }
                LogHelper.WriteLogToFile($"[入场动画] root.Children 共 {root.Children.Count} 个: {string.Join(", ", debugNames)}", LogHelper.LogType.Info);

                if (items.Count == 0)
                {
                    LogHelper.WriteLogToFile("[入场动画] 工具项为空，跳过", LogHelper.LogType.Warning);
                    return;
                }

                LogHelper.WriteLogToFile(
                    $"[入场动画] 开始：主图标 + {items.Count} 个工具项，起始间距 {StartSpacing}px，{(horizontal ? "横排" : "竖排")}",
                    LogHelper.LogType.Event);

                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    item.Opacity = 0;

                    // 初始间距：按顺序越靠后加得越多，视觉上像「被拉开」
                    double startMargin = StartSpacing;

                    // 先设成初始大间距（不设就会从 0 弹起，看不到「更大」的状态）
                    if (horizontal)
                        item.Margin = new Thickness(startMargin, 0, 0, 0);
                    else
                        item.Margin = new Thickness(0, startMargin, 0, 0);

                    // 透明度：延迟出现
                    double delay = i * ItemStaggerMs;
                    _itemDelays[item] = delay;
                }

                // 用一个驱动器统一推进所有工具项的间距与透明度，保证「连贯」
                var drivers = new List<SpringAnimator.Value>();
                var spacingValues = new Dictionary<FrameworkElement, SpringAnimator.Value>();

                foreach (var item in items)
                {
                    var v = new SpringAnimator.Value((float)StartSpacing);
                    // 间距收拢和滑入用接近的节奏，视觉上同步结束，不会"先到位的那半截还在动"。
                    v.SetPreset(new SpringAnimator.SpringPreset(0.7f, 0.9f));
                    v.Set((float)NormalSpacing);
                    spacingValues[item] = v;
                    drivers.Add(v);
                }

                int frameNo = 0;
                var spacingSw = System.Diagnostics.Stopwatch.StartNew();
                SpringAnimator.Drive(drivers, () =>
                {
                    frameNo++;

                    foreach (var kv in spacingValues)
                    {
                        double m = kv.Value.Current;
                        if (m < NormalSpacing) m = NormalSpacing;
                        var item = kv.Key;
                        if (horizontal)
                            item.Margin = new Thickness(m, item.Margin.Top, item.Margin.Right, item.Margin.Bottom);
                        else
                            item.Margin = new Thickness(item.Margin.Left, m, item.Margin.Right, item.Margin.Bottom);
                    }
                }, () =>
                {
                    // 收尾：把 Margin 和透明度恢复到正常状态，避免残留
                    // 先把间距强制归位到精确值，再由 RestoreNormal 收尾。
                    // 弹簧积分的终值可能停在 2.97 这种接近值上，
                    // 直接留着会让工具栏比平时紧一点点——虽然肉眼几乎看不出，
                    // 但下一次 RebuildToolbar 时会以这个值为基准，误差会累积。
                    foreach (var kv in spacingValues)
                    {
                        if (horizontal)
                            kv.Key.Margin = new Thickness(NormalSpacing, kv.Key.Margin.Top, kv.Key.Margin.Right, kv.Key.Margin.Bottom);
                        else
                            kv.Key.Margin = new Thickness(kv.Key.Margin.Left, NormalSpacing, kv.Key.Margin.Right, kv.Key.Margin.Bottom);
                    }

                    RestoreNormal(head, root, horizontal);
                    _itemDelays.Clear();
                    LogHelper.WriteLogToFile(
                        $"[入场动画] 间距收尾，共 {frameNo} 帧，用时 {spacingSw.ElapsedMilliseconds}ms",
                        LogHelper.LogType.Info);
                });

                // 透明度单独用标量弹簧，位置由上面的驱动器负责
                for (int i = 0; i < items.Count; i++)
                {
                    var item = items[i];
                    SpringAnimator.AnimateScalar(item, 1.0, v => item.Opacity = v, SpringAnimator.Interactive);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"浮动工具栏入场动画失败: {ex.Message}", LogHelper.LogType.Warning);
                RestoreNormal(head, root, horizontal);
            }
        }

        /// <summary>
        /// 把主图标和工具项恢复成正常运行时的样子。
        /// 动画被禁用、播放失败、或播放结束时都会走这里。
        /// </summary>
        private static void RestoreNormal(FrameworkElement head, Panel root, bool horizontal)
        {
            try
            {
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

                    // 主图标（拖动块）恢复时不能一律置 0：
                    // MW_Toolbar 在 Left 位置给它 3,0,0,0，在 Top 位置给它 0,3,0,0，
                    // 那是为了让图标和工具项之间留出视觉间隙。
                    // 这里按原样还原，避免动画结束后工具栏整体错位。
                    if (ReferenceEquals(fe, head))
                        fe.Margin = horizontal ? new Thickness(3, 0, 0, 0) : new Thickness(0, 3, 0, 0);
                    else
                        fe.Margin = horizontal
                            ? new Thickness(NormalSpacing, 0, 0, 0)
                            : new Thickness(0, NormalSpacing, 0, 0);
                }
            }
            catch { }
        }
    }
}
