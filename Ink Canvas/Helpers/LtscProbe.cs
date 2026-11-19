using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Media;
using Ink_Canvas.Windows.SettingsViews.Helpers;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// LTSC 临时探针 —— 验证完毕后整块删除（搜 "LTSC 临时探针" 定位入口）。
    /// 设置窗是 WPF 自绘，外部 UI Automation 读不到控件树，
    /// 所以「页面能否真实构造」只能从进程内部回答。
    /// </summary>
    internal static class LtscProbe
    {
/// <summary>
        /// 暴力测试：反复构造并销毁全部设置页。
        /// 模拟老师「手抖连点设置图标、在页面间乱切」的场景——
        /// 每次构造都会新建一整套控件树、注册事件、绑数据。
        /// 若某页面在构造期持有静态引用或未解绑事件，反复 N 次后内存/句柄会明显上涨。
        /// </summary>
        /// <summary>
        /// 设置页压力测试。
        /// </summary>
        /// <param name="rounds">跑多少轮</param>
        /// <param name="layoutMode">
        /// 布局调用到哪一步。排查泄漏时用来二分定位是哪个调用引入的：
        ///   0 = 完全不调（对照组）
        ///   1 = 只 Measure
        ///   2 = Measure + Arrange
        ///   3 = Measure + Arrange + UpdateLayout（默认，最接近真实使用）
        /// </param>
        public static void StressSettingsPages(int rounds, int layoutMode = 3, bool drainIdle = false)
        {
            // drainIdle：每步之后把 Dispatcher 队列抽到 SystemIdle，而不是只到 Background。
            //
            // 为什么需要这个开关：ItemsRepeater 的 ViewportManagerDownLevel 在构造和
            // 布局时用 DispatcherPriority.ContextIdle 投 Action，那个 Action 捕获了
            // owner 的强引用。YieldUi 的恢复回调挂在 Background，ContextIdle 比
            // Background 低，所以只抽到 Background 时这些 Action 永远轮不到执行，
            // 对象被队列钉住——足以伪造出「每一页都泄漏」。
            // 这个开关打开后能区分「真泄漏」和「队列残留」。
            Log("=== 暴力测试开始 rounds=" + rounds + " layout=" + layoutMode
                + " 抽干=" + (drainIdle ? "SystemIdle" : "Background") + " ===");

            var asm = typeof(LtscProbe).Assembly;
            var winType = asm.GetType("Ink_Canvas.Windows.SettingsViews.SettingsWindow");
            if (winType == null) { Log("找不到 SettingsWindow"); return; }
            var field = winType.GetField("_pageTypes", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) { Log("找不到 _pageTypes"); return; }

            object win = null;
            Dictionary<string, Type> pageTypes = null;
            try
            {
                win = Activator.CreateInstance(winType);
                pageTypes = field.GetValue(win) as Dictionary<string, Type>;
            }
            catch (Exception ex) { Log("创建 SettingsWindow 失败: " + Trim(ex.Message)); return; }
            if (pageTypes == null) { Log("_pageTypes 为空"); return; }

            var gc0 = GC.GetTotalMemory(true);
            int handles0 = GetHandles();
            Log("基线: 内存=" + (gc0 / 1048576) + "MB 句柄=" + handles0);

            int errors = 0;
            var errs = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            // 只记内存数字说明不了「是库的账」还是「我们的页面没被回收」。
            // 每页留一个弱引用，跑完看还活着几个——活着的才是继续查的入口。
            // 不记这个，「内存涨了」只能靠猜，而猜出来的结论不可用。
            var weakRefs = new List<(string Tag, WeakReference Ref)>();

            // 每轮结算后的真实驻留量。判「是不是泄漏」看这条曲线的形状，
            // 不看最终那一次强制回收的差值（那个数会被回收抹平，见下面 GC 判据处）。
            var roundSnapshots = new List<long>();

            for (int r = 0; r < rounds; r++)
            {
                foreach (var kv in pageTypes)
                {
                    try
                    {
                        var page = Activator.CreateInstance(kv.Value);

                        var fe = page as FrameworkElement;
                        if (fe == null) continue;
                        if (layoutMode >= 1) fe.Measure(new Size(1000, 800));
                        if (layoutMode >= 2) fe.Arrange(new Rect(0, 0, 1000, 800));
                        if (layoutMode >= 3) fe.UpdateLayout();
                        // 每轮都记，最后按轮报存活数：
                        // 只有末轮活着 = 在等 GC，不是泄漏；
                        // 前几轮还活着 = 真被攥住了，按轮递减说明有东西线性累积。
                        weakRefs.Add(("r" + (r + 1) + "." + kv.Key, new WeakReference(page)));
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        var inner = ex is TargetInvocationException ? ((TargetInvocationException)ex).InnerException ?? ex : ex;
                        if (errs.Count < 10) errs.Add("r" + r + " " + kv.Key + " | " + inner.GetType().Name + " | " + Trim(inner.Message));
                    }
                    // 每构造一页就让出一次 UI 线程。
                    //
                    // 不让出的后果实测过：整轮是同步死循环，心跳 DispatcherTimer（Background 优先级）
                    // 一次都跑不到，看门狗看到 sinceHeartbeat > 20 秒就判「主线程卡死」强制重启，
                    // 进程在第 3 轮动手之前就没了——探针永远跑不完设定的轮数，也永远拿不到总结数据。
                    // 上一版就栽在这里（日志里是「主线程卡死 29.4 秒，强制重启」）。
                    if (drainIdle) DrainToIdle(); else YieldBackgroundOnly();
                }
                // 每轮都记，才能看出增长是线性的还是趋平的。
                // 只看每 3 轮的数据分辨不出「真泄漏」和「填缓存」。
                {
                    // GC 前后都抽一次：之前是执行掉已入队的操作（解开强引用），
                    // 之后是清掉执行过程中新投递的。
                    if (drainIdle) { DrainToIdle(); DrainToIdle(); }
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    if (drainIdle) DrainToIdle();
                    GC.Collect();
                    roundSnapshots.Add(GC.GetTotalMemory(true));
                    Log("  第 " + (r + 1) + " 轮: 内存=" + (GC.GetTotalMemory(true) / 1048576)
                        + "MB 句柄=" + GetHandles()
                        + " 已耗时=" + sw.ElapsedMilliseconds + "ms");
                }
            }

            sw.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var gc1 = GC.GetTotalMemory(true);
            int handles1 = GetHandles();

            Log("完成: " + rounds + " 轮 × " + pageTypes.Count + " 页 = " + (rounds * pageTypes.Count) + " 次构造");
            Log("耗时: " + sw.ElapsedMilliseconds + "ms  平均 " + (sw.ElapsedMilliseconds / Math.Max(1, rounds * pageTypes.Count)) + "ms/次");
            Log("内存: " + (gc0 / 1048576) + "MB → " + (gc1 / 1048576) + "MB  (Δ" + ((gc1 - gc0) / 1048576) + "MB)");
            Log("句柄: " + handles0 + " → " + handles1 + "  (Δ" + (handles1 - handles0) + ")");
            Log("异常: " + errors);
            foreach (var e in errs) Log("  [错] " + e);

            // ── 反向查：存活页面自己攥着什么「不该有的东西」 ──────────
            //
            // 正向找持有者走不通（gcroot 在这个环境返回 0，静态扫描也没有），
            // 那反过来看：这些页面身上挂着什么「它不该有」的引用？
            //
            // 一个只被 Measure 过的页面，理论上除了自己的控件树什么都不该连。
            // 如果它连着 MainWindow / App / SettingsManager 这类长命对象，
            // 那它自己也就跟着长命了。
            try
            {
                var sample = weakRefs.Where(x => x.Ref.IsAlive)
                                     .Select(x => x.Ref.Target)
                                     .FirstOrDefault(o => o != null);
                if (sample != null)
                {
                    var offenders = new List<string>();
                    var t = sample.GetType();
                    var flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

                    // 往下走三层字段，找长命对象的引用
                    void Walk(object obj, string path, int depth)
                    {
                        if (obj == null || depth > 3 || offenders.Count >= 15) return;
                        if (path.Length > 160) return;

                        var ot = obj.GetType();
                        if (ot.IsPrimitive || obj is string || ot.IsEnum) return;

                        // 命中长命对象就记下来
                        var n = ot.FullName ?? "";
                        if (n.Contains("MainWindow") || n == "Ink_Canvas.App" ||
                            n.Contains("SettingsManager") || n.Contains("InkFadeManager") ||
                            n.Contains("SpringAnimator"))
                        {
                            offenders.Add(path + " → " + n);
                            return;
                        }

                        FieldInfo[] fs;
                        try { fs = ot.GetFields(flags); } catch { return; }
                        foreach (var f in fs)
                        {
                            var ft = f.FieldType;
                            if (ft.IsPrimitive || ft == typeof(string) || ft.IsEnum) continue;
                            if (ft.IsValueType) continue;   // 结构体不追，避免爆炸
                            object fv;
                            try { fv = f.GetValue(obj); } catch { continue; }
                            if (fv == null) continue;
                            Walk(fv, path + "." + f.Name, depth + 1);
                        }
                    }

                    Walk(sample, t.Name, 0);
                    if (offenders.Count == 0)
                        Log("  [反向查] 存活页面三层内没连到长命对象");
                    else
                    {
                        Log("  [反向查] 存活页面连到了这些长命对象：");
                        foreach (var s in offenders.Take(10)) Log("    " + s);
                    }
                }
            }
            catch (Exception ex) { Log("  [反向查] 失败: " + Trim(ex.Message)); }

            // ── 查全局资源：Application.Resources 里有没有被写进东西 ──
            //
            // Measure 会触发模板应用，应用模板要 FindResource，
            // 也就是走 Application.Current.Resources 这条全局链。
            // 如果每构造一个页面就往全局字典写一条（例如样式 Setter 的缓存），
            // 那些条目会永久钉住页面里的元素。
            try
            {
                var app = Application.Current;
                int ressCount = app?.Resources?.Count ?? -1;
                Log("  [全局资源] Application.Resources.Count=" + ressCount);

                // 数一下全局字典里所有 ResourceDictionary 的总条目数
                int totalEntries = 0;
                var stack = new Stack<ResourceDictionary>();
                if (app?.Resources != null) stack.Push(app.Resources);
                var seen = new HashSet<ResourceDictionary>();
                while (stack.Count > 0 && seen.Count < 2000)
                {
                    var rd = stack.Pop();
                    if (rd == null || !seen.Add(rd)) continue;
                    try { totalEntries += rd.Count; } catch { }
                    try { foreach (var m in rd.MergedDictionaries) stack.Push(m); } catch { }
                }
                Log("  [全局资源] 合并字典总数=" + seen.Count + " 条目合计=" + totalEntries);
            }
            catch (Exception ex) { Log("  [全局资源] 失败: " + Trim(ex.Message)); }

            // ── 决定性判据：强制全量 GC 后，对象真的少了吗 ──────────
            //
            // 前面测的「内存」是 GC.GetTotalMemory(true)，它只报托管堆大小。
            // 但真正要看的是：这些对象**能不能被回收**。
            //
            // 做法：连做 3 次 GC.Collect + WaitForPendingFinalizers，
            // 中间让出 UI 线程（Finalizer 可能要在 Dispatcher 上跑），
            // 然后量一次。如果内存几乎没变，就是真泄漏。
            // 原来的这里报「强制回收前/后、回收 X MB」，但那个数字永远是 0，说明不了任何事：
            // after 用的是 GC.GetTotalMemory(true)，它自己就会先做一次 full GC；
            // before 又是在每轮末尾已经回收过的数字。两边量的是同一个低谷，差值为 0 是必然的。
            // 2026-11-19 改成量真正有意义的东西：每轮结算后的低谷斜率。
            try
            {
                for (int i = 0; i < 3; i++)
                {
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    YieldUi();
                }
                long settled = GC.GetTotalMemory(true);
                Log("  [GC 判据] 彻底回收后驻留=" + (settled / 1048576) + "MB"
                    + " 基线=" + (gc0 / 1048576) + "MB"
                    + " Δ=" + ((settled - gc0) / 1048576) + "MB"
                    + " 每轮斜率=" + (((settled - gc0) / 1048576.0) / Math.Max(1, rounds)).ToString("F2") + "MB/轮"
                    + " 代计数=" + GC.CollectionCount(0) + "/" + GC.CollectionCount(1) + "/" + GC.CollectionCount(2));

                // 判据：
                //
                // 只看「彻底回收后的斜率」会漏报——实测过：Background 那组每轮内存
                // 20→56→88→121→153→185 明显线性涨，但彻底回收后只有 25MB、斜率 1.09MB/轮，
                // 参数看着完全正常。因为回收把队列残留全清掉了，量不到。
                //
                // 所以真正该看的是「每轮结算那一刻的数值曲线」：
                // 它在回收之后立刻读，是那一轮实际留下的驻留量。真泄漏是线性累积，
                // 预热/缓存只在前几轮涨然后走平。两个都要报，判断留给读者，
                // 但把曲线形状直接判出来，别只丢一个斜率让人自己猜。
                if (roundSnapshots.Count >= 2)
                {
                    long first = roundSnapshots[0];
                    long last = roundSnapshots[roundSnapshots.Count - 1];
                    double perRound = (last - first) / 1048576.0 / (roundSnapshots.Count - 1);

                    // 线性检验：后半段斜率 / 前半段斜率。线性累积时两段差不多；
                    // 预热型是前陡后平，比值会明显小于 1。
                    bool linear = false;
                    if (roundSnapshots.Count >= 4)
                    {
                        int mid = roundSnapshots.Count / 2;
                        double slopeA = (roundSnapshots[mid] - roundSnapshots[0]) / 1048576.0 / Math.Max(1, mid);
                        double slopeB = (last - roundSnapshots[mid]) / 1048576.0
                                        / Math.Max(1, roundSnapshots.Count - 1 - mid);
                        linear = slopeA > 0.5 && slopeB > slopeA * 0.6;
                        Log("  [判据] 前半斜率=" + slopeA.ToString("F2") + "MB/轮 后半斜率="
                            + slopeB.ToString("F2") + "MB/轮 比值="
                            + (slopeA > 0 ? (slopeB / slopeA).ToString("F2") : "n/a"));
                    }

                    Log("  [判据] 逐轮驻留曲线 " + string.Join("→", roundSnapshots.Select(x => (x / 1048576) + "MB"))
                        + "  平均=" + perRound.ToString("F2") + "MB/轮");

                    if (linear && perRound > 3.0)
                        Log("  [判据] 线性累积且幅度大 —— 符合泄漏特征");
                    else
                        Log("  [判据] 不构成泄漏特征（预热/缓存走平）");
                }
            }
            catch (Exception ex) { Log("  [GC 判据] 失败: " + Trim(ex.Message)); }

            // ── 精确诊断：谁还在引用这些页面 ───────────────────────
            //
            // gcroot 在 .NET 11 的转储上返回 0 roots，但对象确实活着（内存线性涨）。
            // 那条路不通，改成进程内自查：只扫静态字段，不扫属性。
            //
            // 为什么只扫静态字段：静态字段是 GC 根里唯一可能被业务代码直接写坏的，
            // 实例字段要从根走过来，扫它等于重新实现一遍 gcroot。
            // 不扫属性是因为 getter 可能有副作用、也可能极慢（上次就卡死了）。
            DoStaticRootScan(weakRefs);

            // 全量 GC 之后，每轮构造的页面还活着几个。
            // 按轮分开看很关键：只有末轮活着 = 在等 GC，不是泄漏；
            // 前面几轮也活着 = 真被攥住了，且每轮都同一批说明是固定持有者，
            // 不是「订阅越挂越多」那种线性累积。
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                var alive = weakRefs.Where(x => x.Ref.IsAlive).Select(x => x.Tag).ToList();
                var byRound = alive
                    .GroupBy(t => t.Substring(0, t.IndexOf('.')))
                    .OrderBy(g => g.Key, StringComparer.Ordinal)
                    .Select(g => g.Key + "=" + g.Count());
                Log("页面存活: " + alive.Count + "/" + weakRefs.Count
                    + "  按轮: " + string.Join(" ", byRound));
                if (alive.Count > 0) Log("  存活明细: " + string.Join(", ", alive));
            }

            var w = win as Window;
            if (w != null) { try { w.Close(); } catch { } }
            Log("=== 暴力测试结束 ===");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetProcessHandleCount(IntPtr hProcess, out int count);

        /// <summary>
        /// 让出一次 UI 线程：刷心跳 + 把 Dispatcher 队列抽到 SystemIdle。
        ///
        /// 和 PumpWait 的区别：PumpWait 要等满 ms 毫秒，会拖长整个测试；
        /// 这里的目的是让等待中的回调有机会跑一轮，不是等待。
        ///
        /// 为什么必须抽到 SystemIdle 而不只是 Background（2026-11-19 查实）：
        /// 恢复回调挂在 SystemIdle 上，Background 及其以上的操作都会被处理掉，
        /// 所以 Background 回调跑完之后我们才恢复执行。如果恢复回调留在 Background，
        /// 比 Background 低的 ContextIdle / ApplicationIdle / SystemIdle 操作就永远轮不到。
        /// ItemsRepeater 的 ViewportManagerDownLevel.RegisterCacheBuildWork() 正是用
        /// ContextIdle 投递、且那个 Action 捕获了 owner 的强引用。只抽到 Background 时
        /// 对象被队列钉住，任何「造对象→GC→数存活」都会得到「全都活着」的假泄漏。
        /// </summary>
        private static void YieldUi()
        {
            DrainToIdle();
        }

        /// <summary>
        /// 只把队列推进到 Background —— 故意保留的错误抽法。
        ///
        /// 存在的唯一理由：给「抽干程度会不会伪造泄漏」这个对照实验留一条反证路径。
        /// 除了对比实验，任何地方都不该用它：ContextIdle 及以下的操作跑不到，
        /// 捕获了对象的队列项会把对象一直钉住，数出来的存活数是假的。
        /// </summary>
        private static void YieldBackgroundOnly()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.Background,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        private static void PumpWait(int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                var frame = new System.Windows.Threading.DispatcherFrame();
                System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                    System.Windows.Threading.DispatcherPriority.Background,
                    new Action(() => frame.Continue = false));
                System.Windows.Threading.Dispatcher.PushFrame(frame);
                System.Threading.Thread.Sleep(20);
            }
        }

        /// <summary>
        /// 进程内自查：哪些静态字段还在引用这些页面（或页面里的控件）。
        ///
        /// 为什么需要它：dotnet-dump 的 gcroot 在这个 .NET 11 的转储上返回 0 roots，
        /// 和「内存线性增长」这个事实直接矛盾——工具不可信，只能自己查。
        ///
        /// 为什么只扫静态字段不扫属性：属性 getter 可能有副作用或极慢，
        /// 上一次全量扫描就卡死被看门狗杀了。静态字段只读 getvalue，安全。
        /// </summary>
        /// <param name="weakRefs">压力测试留下的弱引用，Tag 里带轮次</param>
        private static void DoStaticRootScan(List<(string Tag, WeakReference Ref)> weakRefs)
        {
            try
            {
                var alive = weakRefs.Where(x => x.Ref.IsAlive)
                                    .Select(x => (x.Tag, Obj: x.Ref.Target))
                                    .Where(x => x.Obj != null)
                                    .ToList();
                if (alive.Count == 0) { Log("  [静态扫描] 没有存活页面，跳过"); return; }

                // 只看我们自己这几个程序集：WPF 内部的静态字段不可能是「业务代码写坏的」，
                // 而且量太大，扫了也读不完。
                var targets = new List<object>(alive.Select(x => x.Obj));
                var hits = new List<string>();

                foreach (var asm in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var name = asm.GetName().Name ?? "";
                    if (!name.StartsWith("ICCCELite") && !name.StartsWith("InkCanvas")) continue;

                    Type[] types;
                    try { types = asm.GetTypes(); } catch { continue; }

                    foreach (var t in types)
                    {
                        FieldInfo[] fields;
                        try { fields = t.GetFields(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic); }
                        catch { continue; }

                        foreach (var f in fields)
                        {
                            object v;
                            try { v = f.GetValue(null); } catch { continue; }
                            if (v == null) continue;

                            // 直接是页面本身
                            for (int i = 0; i < targets.Count; i++)
                            {
                                if (ReferenceEquals(v, targets[i]))
                                {
                                    hits.Add(t.FullName + "." + f.Name + " → 整个页面对象");
                                    break;
                                }
                            }

                            // 是集合，里面装着页面
                            if (v is System.Collections.IEnumerable en && !(v is string))
                            {
                                int n = 0;
                                try
                                {
                                    foreach (var item in en)
                                    {
                                        for (int i = 0; i < targets.Count; i++)
                                        {
                                            if (ReferenceEquals(item, targets[i]))
                                            {
                                                hits.Add(t.FullName + "." + f.Name + "[" + n + "] → 整个页面对象");
                                                break;
                                            }
                                        }
                                        if (++n > 20000) break;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }

                if (hits.Count == 0)
                {
                    Log("  [静态扫描] 没有静态字段直接引用存活页面 —— 持有者在实例字段或 WPF 内部");
                }
                else
                {
                    Log("  [静态扫描] 找到 " + hits.Count + " 条静态持有者：");
                    foreach (var h in hits.Take(20)) Log("    " + h);
                }
            }
            catch (Exception ex)
            {
                Log("  [静态扫描] 失败: " + ex.GetType().Name + " | " + Trim(ex.Message));
            }
        }
        /// <summary>
        /// 最小复现：单独造一个 SettingsExpander，Measure 一下，看它能不能被回收。
        ///
        /// 为什么要做到这一步：前面的对照实验只证明了「Measure 触发泄漏」，
        /// 但没说清是 SettingsExpander 这个控件本身，还是我们页面的写法。
        /// 一个最小复现能把这个分清楚——
        ///   若最小复现也漏 → 控件/库的问题，改用法或绕开
        ///   若最小复现不漏 → 是我们页面里的某个绑定或写法
        /// </summary>
        public static void ProbeSettingsExpander()
        {
            Log("=== 最小复现：SettingsExpander ===");
            try
            {
                var asm = typeof(LtscProbe).Assembly;
                var expType = asm.GetType("iNKORE.UI.WPF.Modern.Controls.SettingsExpander")
                              ?? Type.GetType("iNKORE.UI.WPF.Modern.Controls.SettingsExpander, iNKORE.UI.WPF.Modern.Controls");
                if (expType == null) { Log("  [FAIL] 找不到 SettingsExpander 类型"); return; }
                Log("  类型=" + expType.FullName);

                // 先做对照组：同样造 20 个普通 Border + Measure，
                // 如果普通控件也被「泄漏」，那说明是我的测量方法有问题，
                // 而不是 SettingsExpander 有问题。这一步不能省。
                {
                    var ctrlRefs = new List<WeakReference>();
                    for (int k = 0; k < 20; k++)
                    {
                        object o = new Border();
                        var bfe = (FrameworkElement)o;
                        bfe.Measure(new Size(1000, 800));
                        bfe.Arrange(new Rect(0, 0, 1000, 800));
                        bfe.UpdateLayout();
                        ctrlRefs.Add(new WeakReference(o));
                        o = null;
                        YieldUi();
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        YieldUi();
                    }
                    int cAlive = ctrlRefs.Count(r => r.IsAlive);
                    Log("  [对照] 普通 Border 造了 20 个，GC 后存活 " + cAlive + " 个");
                    // 判据不能是「必须为 0」：循环里最后一个对象可能还被某个
                    // 局部/寄存器残留引用着，回收得晚一拍是正常的。
                    // 真正要区分的是「个别残留」和「几乎全存活」。
                    if (cAlive > 5)
                        Log("  [警告] 普通 Border 存活 " + cAlive + "/20 —— 测量方法有问题，下面的结论不可信");
                }

                // 再对照三个：SettingsCard（Expander 的近亲）、ToggleSwitch、SimpleStackPanel。
                // 目的是缩小范围——如果只有 SettingsExpander 漏、它爹不漏，
                // 那就是 Expander 特有的东西（展开/收起逻辑、Items 集合）；
                // 如果爹也漏，那就是 SettingsCard 这一族共有的。
                foreach (var probeName in new[]
                {
                    "iNKORE.UI.WPF.Modern.Controls.SettingsCard",
                    "iNKORE.UI.WPF.Modern.Controls.ToggleSwitch",
                    "iNKORE.UI.WPF.Controls.SimpleStackPanel",
                })
                {
                    Type pt = null;
                    try { pt = typeof(LtscProbe).Assembly.GetType(probeName) ?? Type.GetType(probeName); }
                    catch { }
                    if (pt == null)
                    {
                        // 类型不在本程序集里，用已加载程序集找
                        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try { pt = a.GetType(probeName); } catch { }
                            if (pt != null) break;
                        }
                    }
                    if (pt == null) { Log("  [对照] 找不到 " + probeName); continue; }

                    var pr = new List<WeakReference>();
                    for (int k = 0; k < 20; k++)
                    {
                        object o = null;
                        try
                        {
                            o = Activator.CreateInstance(pt);
                            var pfe = o as FrameworkElement;
                            if (pfe != null)
                            {
                                pfe.Measure(new Size(1000, 800));
                                pfe.Arrange(new Rect(0, 0, 1000, 800));
                                pfe.UpdateLayout();
                            }
                        }
                        catch { }
                        pr.Add(new WeakReference(o));
                        o = null;
                        YieldUi();
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        YieldUi();
                    }
                    Log("  [对照] " + pt.Name + " 造 20 个，GC 后存活 " + pr.Count(r => r.IsAlive) + " 个");
                }

                // ── 查 SettingsExpander 的静态成员 ────────────────
                // 只有它漏、父类不漏，那多半是它自己挂了个静态的东西。
                // 这里把它的静态字段和属性列出来，直接看。
                try
                {
                    const BindingFlags S = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
                    var fs = expType.GetFields(S);
                    Log("  静态字段 = " + fs.Length + " 个");
                    foreach (var f in fs)
                    {
                        object v = null;
                        try { v = f.GetValue(null); } catch { }
                        var ft = f.FieldType.Name;
                        bool suspicious = ft.Contains("List") || ft.Contains("Dictionary") ||
                                          ft.Contains("Collection") || ft.Contains("ConditionalWeakTable");
                        Log("    " + (suspicious ? "[可疑] " : "") + ft + " " + f.Name +
                            (v == null ? " = null" : " = <非空>"));
                    }

                    var ps = expType.GetProperties(S);
                    Log("  静态属性 = " + ps.Length + " 个");
                    foreach (var p in ps)
                    {
                        var pt = p.PropertyType.Name;
                        if (pt.Contains("List") || pt.Contains("Dictionary") || pt.Contains("Collection"))
                            Log("    [可疑] " + pt + " " + p.Name);
                    }
                }
                catch (Exception ex) { Log("  查静态成员失败: " + Trim(ex.Message)); }

                var refs = new List<WeakReference>();

                // 结论的强度取决于样本量：20 个不够就加到 100，
                // 看存活率是不是还是 100%。如果只是「回收得晚」，
                // 加到 100 个时应该有一部分会死。
                const int N = 100;
                for (int round = 0; round < N; round++)
                {
                    object el = null;
                    try
                    {
                        el = Activator.CreateInstance(expType);
                        var fe = el as FrameworkElement;
                        if (fe != null)
                        {
                            fe.Measure(new Size(1000, 800));
                            fe.Arrange(new Rect(0, 0, 1000, 800));
                            fe.UpdateLayout();
                        }
                    }
                    catch (Exception ex)
                    {
                        if (round == 0) Log("  构造失败: " + Trim(ex.Message));
                    }
                    refs.Add(new WeakReference(el));
                    el = null;
                    YieldUi();
                }

                for (int i = 0; i < 3; i++)
                {
                    GC.Collect(2, GCCollectionMode.Forced, true, true);
                    GC.WaitForPendingFinalizers();
                    YieldUi();
                }

                // ── 检查实例是否挂到了全局事件 / Dispatcher 上 ──────
                // 静态字段是干净的（13 个全是 DependencyProperty），
                // 那就查实例侧：它有没有订阅什么全局东西。
                try
                {
                    object sample = null;
                    for (int i = 0; i < refs.Count; i++)
                    {
                        if (refs[i].IsAlive) { sample = refs[i].Target; break; }
                    }
                    if (sample != null)
                    {
                        // 事件订阅在 .NET 里表现为「调用方 + 订阅者」的强引用。
                        // 如果控件订阅了静态事件，静态事件的委托表就持有控件。
                        // 这里列出它身上所有事件字段的值，看有没有非空
                        const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
                        int evtCount = 0, nonNull = 0;
                        for (var t = sample.GetType(); t != null && t != typeof(object); t = t.BaseType)
                        {
                            FieldInfo[] fs2;
                            try { fs2 = t.GetFields(F); } catch { continue; }
                            foreach (var f in fs2)
                            {
                                if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) continue;
                                evtCount++;
                                object dv = null;
                                try { dv = f.GetValue(sample); } catch { }
                                if (dv != null)
                                {
                                    nonNull++;
                                    var dl = (Delegate)dv;
                                    Log("    [事件非空] " + t.Name + "." + f.Name +
                                        " 订阅者=" + dl.GetInvocationList().Length);
                                }
                            }
                        }
                        Log("  实例事件字段=" + evtCount + " 其中非空=" + nonNull);

                        // 看它的父级链：一个从没挂进树的元素，Parent/VisualParent 应该都是 null
                        var fe2 = sample as FrameworkElement;
                        if (fe2 != null)
                        {
                            var vp = System.Windows.Media.VisualTreeHelper.GetParent(fe2);
                            Log("  VisualParent=" + (vp == null ? "null" : vp.GetType().Name) +
                                " logicalParent=" + (fe2.Parent == null ? "null" : fe2.Parent.GetType().Name));
                        }
                    }
                }
                catch (Exception ex) { Log("  查实例失败: " + Trim(ex.Message)); }
                // ── 试：不 Measure 只构造，看还漏不漏 ──────────────
                // 如果只构造不 Measure 就不漏，那问题在模板应用那一步；
                // 如果只构造就漏，那问题在构造函数里。
                try
                {
                    var noMeasureRefs = new List<WeakReference>();
                    for (int k = 0; k < 30; k++)
                    {
                        object o = null;
                        try { o = Activator.CreateInstance(expType); } catch { }
                        noMeasureRefs.Add(new WeakReference(o));
                        o = null;
                        YieldUi();
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        YieldUi();
                    }
                    Log("  [二分] 只构造不 Measure：30 个存活 " + noMeasureRefs.Count(r => r.IsAlive) + " 个");
                }
                catch (Exception ex) { Log("  [二分] 失败: " + Trim(ex.Message)); }

                // ── 试：只 Measure 不 Arrange ──────────────────────
                try
                {
                    var mOnly = new List<WeakReference>();
                    for (int k = 0; k < 30; k++)
                    {
                        object o = null;
                        try
                        {
                            o = Activator.CreateInstance(expType);
                            var f2 = o as FrameworkElement;
                            f2?.Measure(new Size(1000, 800));
                        }
                        catch { }
                        mOnly.Add(new WeakReference(o));
                        o = null;
                        YieldUi();
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        YieldUi();
                    }
                    Log("  [二分] 只 Measure：30 个存活 " + mOnly.Count(r => r.IsAlive) + " 个");
                }
                catch (Exception ex) { Log("  [二分] 失败: " + Trim(ex.Message)); }
                // ── ApplyTemplate 单独测：是不是模板应用那一步 ──────
                try
                {
                    var atRefs = new List<WeakReference>();
                    for (int k = 0; k < 30; k++)
                    {
                        object o = null;
                        try
                        {
                            o = Activator.CreateInstance(expType);
                            // 关键：只应用模板，不 Measure
                            (o as Control)?.ApplyTemplate();
                        }
                        catch { }
                        atRefs.Add(new WeakReference(o));
                        o = null;
                        YieldUi();
                    }
                    for (int i = 0; i < 3; i++)
                    {
                        GC.Collect(2, GCCollectionMode.Forced, true, true);
                        GC.WaitForPendingFinalizers();
                        YieldUi();
                    }
                    Log("  [二分] 只 ApplyTemplate：30 个存活 " + atRefs.Count(r => r.IsAlive) + " 个");
                }
                catch (Exception ex) { Log("  [二分] ApplyTemplate 失败: " + Trim(ex.Message)); }

                // ── 看看模板里到底有什么 ───────────────────────────
                try
                {
                    object probeObj = Activator.CreateInstance(expType);
                    var ctl = probeObj as Control;
                    if (ctl != null)
                    {
                        ctl.ApplyTemplate();
                        if (ctl.Template != null)
                        {
                            // 数模板里的可视树节点类型
                            var counts = new Dictionary<string, int>();
                            void CountVis(DependencyObject d, int depth)
                            {
                                if (d == null || depth > 12) return;
                                var n = d.GetType().Name;
                                counts[n] = (counts.TryGetValue(n, out var c) ? c : 0) + 1;
                                int cnt = 0;
                                try { cnt = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); } catch { }
                                for (int i = 0; i < cnt && i < 200; i++)
                                {
                                    DependencyObject ch = null;
                                    try { ch = System.Windows.Media.VisualTreeHelper.GetChild(d, i); } catch { }
                                    CountVis(ch, depth + 1);
                                }
                            }
                            CountVis(ctl, 0);
                            var top = counts.OrderByDescending(kv => kv.Value).Take(15);
                            Log("  [模板构成] 节点总数=" + counts.Values.Sum() + " 类型数=" + counts.Count);
                            foreach (var kv in top) Log("    " + kv.Key + " × " + kv.Value);
                        }
                    }
                }
                catch (Exception ex) { Log("  [模板构成] 失败: " + Trim(ex.Message)); }
                // ── 用 Template.VisualTree 直接读模板定义 ────────────
                try
                {
                    object probeObj = Activator.CreateInstance(expType);
                    var ctl2 = probeObj as Control;
                    if (ctl2?.Template != null)
                    {
                        // FrameworkElementFactory 没有公开 Children，
                        // 换成「把实例的模板内容真展开，再数可视树」——
                        // 这个更准，因为模板在实际应用时如何展开才是问题所在。
                        var counts2 = new Dictionary<string, int>();
                        int total2 = 0;
                        void CountDeep(DependencyObject d, int depth)
                        {
                            if (d == null || depth > 25 || total2 > 5000) return;
                            total2++;
                            var nm = d.GetType().Name;
                            counts2[nm] = (counts2.TryGetValue(nm, out var c4) ? c4 : 0) + 1;
                            int cc = 0;
                            try { cc = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); } catch { }
                            if (cc == 0)
                            {
                                // 有些容器在 LogicalTree 里
                                try
                                {
                                    foreach (var lc in System.Windows.LogicalTreeHelper.GetChildren(d))
                                        if (lc is DependencyObject lcd) CountDeep(lcd, depth + 1);
                                }
                                catch { }
                                return;
                            }
                            for (int i = 0; i < cc && i < 200; i++)
                            {
                                DependencyObject ch2 = null;
                                try { ch2 = System.Windows.Media.VisualTreeHelper.GetChild(d, i); } catch { }
                                CountDeep(ch2, depth + 1);
                            }
                        }
                        ctl2.Measure(new Size(1000, 800));
                        CountDeep(ctl2, 0);
                        Log("  [模板展开] 元素总数=" + total2 + " 类型数=" + counts2.Count);
                        foreach (var kv in counts2.OrderByDescending(k => k.Value).Take(18))
                            Log("    " + kv.Key + " × " + kv.Value);
                        foreach (var kv in counts2)
                        {
                            if (kv.Key.Contains("Repeater") || kv.Key.Contains("Items"))
                                Log("    [关注] " + kv.Key + " × " + kv.Value);
                        }
                    }
                }
                catch (Exception ex) { Log("  [模板定义] 失败: " + Trim(ex.Message)); }
                // ── 决定性验证：单独测 ItemsRepeater 会不会漏 ────────
                // 如果它单独就漏，那根因就是它，和 SettingsExpander 无关。
                try
                {
                    var repType = Type.GetType("iNKORE.UI.WPF.Modern.Controls.ItemsRepeater, iNKORE.UI.WPF.Modern.Controls");
                    if (repType == null)
                    {
                        foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            try { repType = a.GetType("iNKORE.UI.WPF.Modern.Controls.ItemsRepeater"); } catch { }
                            if (repType != null) break;
                        }
                    }
                    if (repType == null) Log("  [验证] 找不到 ItemsRepeater");
                    else
                    {
                        var rr = new List<WeakReference>();
                        for (int k = 0; k < 30; k++)
                        {
                            object o = null;
                            try
                            {
                                o = Activator.CreateInstance(repType);
                                var f5 = o as FrameworkElement;
                                f5?.Measure(new Size(1000, 800));
                            }
                            catch { }
                            rr.Add(new WeakReference(o));
                            o = null;
                            YieldUi();
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            GC.Collect(2, GCCollectionMode.Forced, true, true);
                            GC.WaitForPendingFinalizers();
                            YieldUi();
                        }
                        int rAlive = rr.Count(r => r.IsAlive);
                        Log("  [验证] ItemsRepeater 单独测：30 个存活 " + rAlive + " 个");
                        if (rAlive >= 25)
                            Log("  [结论] ItemsRepeater 自己就漏 —— 根因锁定在它身上");
                        else
                            Log("  [结论] ItemsRepeater 单独不漏 —— 是它和 Expander 的组合问题");
                    }
                }
                catch (Exception ex) { Log("  [验证] 失败: " + Trim(ex.Message)); }
                // ── 试：把 ItemsRepeater 从模板里摘掉能不能救 ─────────
                // SettingsExpander 的 ItemsRepeater 是用来显示展开后的子项的。
                // 试着在 Measure 之前把它的 Items 设成 null / 空集合，
                // 看 ItemsRepeater 还会不会创建。
                try
                {
                    var itemsProp = expType.GetProperty("Items");
                    Log("  [尝试] Items 属性=" + (itemsProp == null ? "找不到" : itemsProp.PropertyType.Name));
                    if (itemsProp != null)
                    {
                        var r2 = new List<WeakReference>();
                        int setOk = 0, setFail = 0;
                        for (int k = 0; k < 30; k++)
                        {
                            object o = null;
                            try
                            {
                                o = Activator.CreateInstance(expType);
                                // 显式给一个空集合。
                                //
                                // 注意：Items 的属性类型是 IList（接口），
                                // Activator.CreateInstance(接口) 会抛异常。
                                // 上一版就是在这里被 catch 吞掉，对象根本没设 Items 也没 Measure，
                                // 于是「0/30 存活」是个假象——它不是修复生效，是压根没跑到那一步。
                                // 现在显式用 List<object>，并统计成功次数。
                                if (itemsProp.CanWrite)
                                {
                                    try
                                    {
                                        itemsProp.SetValue(o, new System.Collections.Generic.List<object>());
                                        setOk++;
                                    }
                                    catch { setFail++; }
                                }
                                var f6 = o as FrameworkElement;
                                f6?.Measure(new Size(1000, 800));
                            }
                            catch { }
                            r2.Add(new WeakReference(o));
                            o = null;
                            YieldUi();
                        }
                        Log("  [尝试] 设 Items 成功=" + setOk + " 失败=" + setFail);
                        for (int i = 0; i < 3; i++)
                        {
                            GC.Collect(2, GCCollectionMode.Forced, true, true);
                            GC.WaitForPendingFinalizers();
                            YieldUi();
                        }
                        Log("  [尝试] 显式给空 Items 后：30 个存活 " + r2.Count(r => r.IsAlive) + " 个");
                    }
                }
                catch (Exception ex) { Log("  [尝试] 失败: " + Trim(ex.Message)); }
                // ── 分清：是「Items 为 null」漏，还是「Items 有内容」漏 ──
                // 上一测证明了给空集合就不漏。但我们的页面里 Items 是有子项的。
                // 必须测有子项的情况，才能知道修复方案能不能用。
                try
                {
                    var itemsProp2 = expType.GetProperty("Items");
                    if (itemsProp2 != null && itemsProp2.CanWrite)
                    {
                        // 情况 A：给一个装了 3 个元素的集合
                        var withItems = new List<WeakReference>();
                        for (int k = 0; k < 30; k++)
                        {
                            object o = null;
                            try
                            {
                                o = Activator.CreateInstance(expType);
                                var coll = Activator.CreateInstance(itemsProp2.PropertyType);
                                var addM = itemsProp2.PropertyType.GetMethod("Add");
                                if (addM != null)
                                {
                                    for (int j = 0; j < 3; j++)
                                    {
                                        try { addM.Invoke(coll, new object[] { new Border() }); } catch { }
                                    }
                                }
                                itemsProp2.SetValue(o, coll);
                                (o as FrameworkElement)?.Measure(new Size(1000, 800));
                            }
                            catch { }
                            withItems.Add(new WeakReference(o));
                            o = null;
                            YieldUi();
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            GC.Collect(2, GCCollectionMode.Forced, true, true);
                            GC.WaitForPendingFinalizers();
                            YieldUi();
                        }
                        Log("  [分清] Items 有 3 个子项：30 个存活 " + withItems.Count(r => r.IsAlive) + " 个");
                    }
                }
                catch (Exception ex) { Log("  [分清] 失败: " + Trim(ex.Message)); }
                // ── 核对：页面上真实的 SettingsExpander，Items 到底是 null 还是空集合 ──
                try
                {
                    var asm2 = typeof(LtscProbe).Assembly;
                    var winT = asm2.GetType("Ink_Canvas.Windows.SettingsViews.SettingsWindow");
                    var pf = winT?.GetField("_pageTypes", BindingFlags.NonPublic | BindingFlags.Instance);
                    var sw2 = Activator.CreateInstance(winT);
                    var pts = pf?.GetValue(sw2) as Dictionary<string, Type>;
                    if (pts != null && pts.TryGetValue("SidebarPage", out var sidebarType))
                    {
                        var pageObj = Activator.CreateInstance(sidebarType);
                        var stack2 = new Stack<DependencyObject>();
                        stack2.Push(pageObj as DependencyObject);
                        var seen2 = new HashSet<DependencyObject>();
                        int found = 0;
                        while (stack2.Count > 0 && seen2.Count < 5000)
                        {
                            var d = stack2.Pop();
                            if (d == null || !seen2.Add(d)) continue;
                            if (d.GetType().FullName == "iNKORE.UI.WPF.Modern.Controls.SettingsExpander")
                            {
                                found++;
                                var ip = d.GetType().GetProperty("Items");
                                var iv = ip?.GetValue(d);
                                Log("    Expander#" + found + " Items=" + (iv == null ? "null" : iv.GetType().Name));
                            }
                            try { foreach (var c in System.Windows.LogicalTreeHelper.GetChildren(d)) if (c is DependencyObject cd) stack2.Push(cd); } catch { }
                            try { int n2 = System.Windows.Media.VisualTreeHelper.GetChildrenCount(d); for (int i = 0; i < n2; i++) { DependencyObject c = null; try { c = System.Windows.Media.VisualTreeHelper.GetChild(d, i); } catch { } if (c != null) stack2.Push(c); } } catch { }
                        }
                        Log("  [核对] SidebarPage 找到 " + found + " 个 Expander");
                    }
                }
                catch (Exception ex) { Log("  [核对] 失败: " + Trim(ex.Message)); }
                int alive = refs.Count(r => r.IsAlive);
                Log("  造了 " + refs.Count + " 个，GC 后存活 " + alive + " 个（" +
                    (refs.Count == 0 ? 0 : alive * 100 / refs.Count) + "%）");
                if (alive == 0)
                    Log("  [结论] 最小复现不漏 —— 泄漏在我们页面的写法里");
                else
                    Log("  [结论] 最小复现就漏 —— 是 SettingsExpander/库本身的问题");
            }
            catch (Exception ex) { Log("  [FAIL] " + Trim(ex.Message)); }
        }
        private static int GetHandles()
        {
            try { int c; GetProcessHandleCount(GetCurrentProcess(), out c); return c; }
            catch { return -1; }
        }

// ===== LTSC 临时探针 =====
        /// <summary>
        /// 冒烟自测：在进程内直接调用被测代码，用真实对象验证修复是否生效。
        /// 不依赖鼠标、不依赖窗口层级——那些今晚已经证明不可靠。
        /// </summary>

        /// <summary>
        /// 暴力测试：模拟老师上课时的乱操作。
        ///
        /// 覆盖快速连点工具、快速折叠展开、以及两者混合。
        /// 每轮都记内存和句柄，看增长是线性还是趋平——只看最后一轮分不清
        /// 「真泄漏」和「填缓存」。
        /// </summary>
        public static void StressToolbar(int rounds)
        {
            Log("=== 工具栏暴力测试开始 rounds=" + rounds + " ===");

            var mw = Application.Current?.MainWindow as MainWindow;
            if (mw == null) { Log("主窗口为空，中止"); return; }

            var mwType = typeof(MainWindow);
            const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;
            var cursorClick = mwType.GetMethod("CursorIcon_Click", F);
            var penClick = mwType.GetMethod("PenIcon_Click", F);
            var eraserClick = mwType.GetMethod("EraserIcon_Click", F);
            var selectClick = mwType.GetMethod("SelectIcon_MouseUp", F);

            var tools = new List<(string, System.Reflection.MethodInfo)>();
            foreach (var t in new[] { ("cursor", cursorClick), ("pen", penClick),
                                      ("eraser", eraserClick), ("select", selectClick) })
            {
                if (t.Item2 == null) Log("  !! 找不到方法: " + t.Item1);
                else tools.Add(t);
            }
            Log("  可用工具数: " + tools.Count);
            if (tools.Count == 0) { Log("一个都找不到，中止"); return; }

            var foldMethod = mwType.GetMethod("FoldFloatingBar", F);
            var unfoldMethod = mwType.GetMethod("UnFoldFloatingBar", F);

            void Run(Action a, string tag, ref int errs, List<string> msgs)
            {
                try { a(); }
                catch (Exception ex)
                {
                    errs++;
                    var inner = ex is TargetInvocationException tie ? (tie.InnerException ?? ex) : ex;
                    if (msgs.Count < 12) msgs.Add(tag + " | " + inner.GetType().Name + " | " + Trim(inner.Message));
                }
            }

            var gc0 = GC.GetTotalMemory(true);
            int h0 = GetHandles();
            Log("  基线: 内存=" + (gc0 / 1048576) + "MB 句柄=" + h0);

            int errors = 0;
            var errs = new List<string>();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var rnd = new Random(20261117);

            for (int r = 0; r < rounds; r++)
            {
                // 阶段 1：纯连点，每轮 40 次随机工具切换
                for (int i = 0; i < 40; i++)
                {
                    var (name, mi) = tools[rnd.Next(tools.Count)];
                    Run(() => mi.Invoke(mw, mi.GetParameters().Length == 0 ? null : new object[] { null, null }),
                        "点" + name, ref errors, errs);
                }

                // 阶段 2：折叠展开来回 3 次
                for (int i = 0; i < 3; i++)
                {
                    if (foldMethod != null)
                        Run(() => foldMethod.Invoke(mw, foldMethod.GetParameters().Length == 0
                            ? null : new object[] { new object(), true }), "折叠", ref errors, errs);
                    if (unfoldMethod != null)
                        Run(() => unfoldMethod.Invoke(mw, unfoldMethod.GetParameters().Length == 0
                            ? null : new object[] { null }), "展开", ref errors, errs);
                }

                // 阶段 3：混合——切工具和折叠交替，模拟手抖乱点
                for (int i = 0; i < 10; i++)
                {
                    var (name, mi) = tools[rnd.Next(tools.Count)];
                    Run(() => mi.Invoke(mw, mi.GetParameters().Length == 0 ? null : new object[] { null, null }),
                        "混点" + name, ref errors, errs);
                    if (foldMethod != null && i % 3 == 0)
                        Run(() => foldMethod.Invoke(mw, foldMethod.GetParameters().Length == 0
                            ? null : new object[] { new object(), true }), "混折叠", ref errors, errs);
                    YieldUi();
                }

                // 折叠/展开内部会 await 动画在下一帧完成的序列，不让线程的话
                // 每一轮的折叠都只把状态机推进一步就没下文，测出来的「无异常」是空的。
                YieldUi();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                Log("  第 " + (r + 1) + " 轮: 内存=" + (GC.GetTotalMemory(true) / 1048576)
                    + "MB 句柄=" + GetHandles()
                    + " 已耗时=" + sw.ElapsedMilliseconds + "ms"
                    + " 异常=" + errors);
            }

            sw.Stop();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            var gc1 = GC.GetTotalMemory(true);
            int h1 = GetHandles();

            Log("=== 暴力测试结束 ===");
            Log("  总异常次数: " + errors);
            foreach (var m in errs) Log("    异常: " + m);
            Log("  内存: " + (gc0 / 1048576) + "MB -> " + (gc1 / 1048576) + "MB (+" + ((gc1 - gc0) / 1048576) + "MB)");
            Log("  句柄: " + h0 + " -> " + h1 + " (+" + (h1 - h0) + ")");
            Log("  总耗时: " + sw.ElapsedMilliseconds + "ms");

            // 判据：40+ 次/轮 × N 轮的乱点下，异常应该是 0
            if (errors == 0) Log("  [PASS] 暴力操作下无异常");
            else Log("  [FAIL] 出现 " + errors + " 次异常");
        }

        public static void SmokeTest()
        {
            Log("=== 冒烟自测开始 ===");
            int pass = 0, fail = 0;
            void R(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; Log("  [PASS] " + name + (detail == "" ? "" : " | " + detail)); }
                else { fail++; Log("  [FAIL] " + name + (detail == "" ? "" : " | " + detail)); }
            }

            // --- 0. 激光笔渐隐：淡出时长必须来自用户设置，不能来自书写时长 ---
            //
            // 2026-11-19 反馈：「激光笔的渐隐动画不完整」。
            // 根因：AddFadingStroke 里当 strokeDurationMs > 0 时，
            // 拿「这一笔写了多久」当淡出动画的长度。激光笔写一笔 100~300ms，
            // 淡出就变成 20~300ms —— 快到看不见，用户感知是「突然消失」。
            //
            // 这条断言的形式：直接调用真实的 AddFadingStroke，传入一个极短的
            // strokeDurationMs，然后读它记下来的动画时长。
            // 如果实现又退回「用书写时长」，这里会读到 20ms 而不是配置值，必须红。
            try
            {
                var fadeType = typeof(Ink_Canvas.Helpers.InkFadeManager);
                var mwFade = Application.Current?.MainWindow as MainWindow;
                // _inkFadeManager 是私有字段，没有公开访问器，走反射取。
                var mgr = mwFade == null
                    ? null
                    : typeof(MainWindow)
                        .GetField("_inkFadeManager", BindingFlags.NonPublic | BindingFlags.Instance)
                        ?.GetValue(mwFade) as Ink_Canvas.Helpers.InkFadeManager;

                if (mgr == null)
                {
                    R("FADE-DUR-1 渐隐管理器可用", false, "拿不到 InkFadeManager 实例");
                }
                else
                {
                    // 配置一个明确的动画时长，看它是否被尊重。
                    int configured = 2000;
                    mgr.AnimationDuration = configured;
                    mgr.FadeSpeedMultiplier = 1.0;

                    // AddFadingStroke 第一条就是 if (!IsEnabled) return;
                    // 探针跑的时候用户多半没开渐隐，不打开就什么都测不到。
                    // 记住原值，测完必须还原 —— 这是改用户运行状态，不能留污染。
                    bool wasEnabled = mgr.IsEnabled;
                    mgr.IsEnabled = true;

                    // 造一笔真实的 Stroke
                    var pts = new StylusPointCollection();
                    pts.Add(new StylusPoint(100, 100));
                    pts.Add(new StylusPoint(200, 200));
                    var testStroke = new Stroke(pts);

                    // 关键：传一个极短的「书写时长」，模拟激光笔快速一划。
                    // 修复前的实现会把这个值当淡出时长。
                    mgr.AddFadingStroke(testStroke, new Point(100, 100), new Point(200, 200), 150);

                    // 读回它实际登记下来的动画时长
                    var durField = fadeType.GetField("_strokeAnimationDurations",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    int actual = -1;
                    if (durField?.GetValue(mgr) is System.Collections.IDictionary dict)
                    {
                        foreach (System.Collections.DictionaryEntry de in dict)
                        {
                            if (ReferenceEquals(de.Key, testStroke)) { actual = (int)de.Value; break; }
                        }
                    }

                    // 反向对照：如果实现仍用 strokeDurationMs（150），actual 会是 20 左右。
                    // 正确实现应该接近 configured（2000，可能被 FadeSpeedMultiplier 微调）。
                    R("FADE-DUR-1 淡出时长来自配置而不是书写时长",
                        actual >= configured / 2,
                        "配置=" + configured + "ms 实际登记=" + actual + "ms"
                        + (actual < configured / 2 ? "  !! 又用书写时长当淡出时长了" : ""));

                    // 清掉这一笔，别污染现场
                    try { mgr.RemoveStroke(testStroke); } catch { }
                    // 恢复用户原来的渐隐开关状态
                    mgr.IsEnabled = wasEnabled;
                }
            }
            catch (Exception ex) { R("FADE-DUR", false, Trim(ex.Message)); }

            // --- 1. InkFadeManager：CreateStrokeVisual 失败时不登记字典（L-10）---
            try
            {
                var fadeType = typeof(Ink_Canvas.Helpers.InkFadeManager);
                var f1 = fadeType.GetField("_strokeStartPoints", BindingFlags.NonPublic | BindingFlags.Instance);
                var f2 = fadeType.GetField("_strokeEndPoints", BindingFlags.NonPublic | BindingFlags.Instance);
                var f3 = fadeType.GetField("_strokeAnimationDurations", BindingFlags.NonPublic | BindingFlags.Instance);
                R("L-10 三个登记字典存在", f1 != null && f2 != null && f3 != null);
            }
            catch (Exception ex) { R("L-10", false, Trim(ex.Message)); }

            // --- 2. PPTQuickPanel：容量上限常量与淘汰方法（L-1）---
            try
            {
                var qpType = typeof(Ink_Canvas.Windows.PPTQuickPanel);
                var maxField = qpType.GetField("MaxPptImagesForLtsc", BindingFlags.NonPublic | BindingFlags.Static);
                int max = maxField != null ? (int)maxField.GetValue(null) : -1;
                R("L-1 MaxPptImagesForLtsc == 8", max == 8, "实际=" + max);
                var trim = qpType.GetMethod("TrimPptImages", BindingFlags.NonPublic | BindingFlags.Instance);
                var clear = qpType.GetMethod("ClearAllPptImages", BindingFlags.NonPublic | BindingFlags.Instance);
                R("L-1 TrimPptImages / ClearAllPptImages 存在", trim != null && clear != null);
            }
            catch (Exception ex) { R("L-1", false, Trim(ex.Message)); }

            // --- 3. WindowOverviewModel：_windowCache 是否有锁保护（A-3）---
            try
            {
                var t2 = typeof(Ink_Canvas.Helpers.WindowOverviewModel);
                R("A-3 WindowOverviewModel 可加载", t2 != null);
                var cacheField = t2.GetField("_windowCache", BindingFlags.NonPublic | BindingFlags.Instance);
                var lockField = t2.GetField("_lockObject", BindingFlags.NonPublic | BindingFlags.Instance);
                R("A-3 _windowCache / _lockObject 字段都在", cacheField != null && lockField != null);
            }
            catch (Exception ex) { R("A-3", false, Trim(ex.Message)); }

            // --- 4. PptBridge：安全释放方法（L-3 / L-8）---
            try
            {
                var pb = typeof(Ink_Canvas.Helpers.PptBridge);
                var sr = pb.GetMethod("SafeRelease", BindingFlags.NonPublic | BindingFlags.Static);
                R("L-3/L-8 SafeRelease 存在", sr != null);
                // SafeRelease(null) 不应抛
                var threw = false;
                try { sr?.Invoke(null, new object[] { null }); } catch { threw = true; }
                R("L-3 SafeRelease(null) 不抛异常", !threw);
            }
            catch (Exception ex) { R("L-3/L-8", false, Trim(ex.Message)); }

            // --- 5. 资源释放的对照组（C-5）---
            // 原来测的是 Process.GetCurrentProcess() 的句柄，但那是伪句柄，
            // 实测 using 组和不释放组增长都是 0 —— 测不出差别，这条测试等于没测。
            // 换成真实的文件句柄：它能反映「有没有真的开内核对象」。
            // 判据仍是「A 明显好于 B」，A 单独为 0 说明不了问题。
            try
            {
                const int N = 300;
                string probeFile = Path.Combine(Path.GetTempPath(), "ltsc_handle_probe.tmp");
                if (!File.Exists(probeFile)) File.WriteAllText(probeFile, "");

                // A：用 using 正确释放
                int a0 = GetHandles();
                for (int i = 0; i < N; i++)
                {
                    using (var fs = new FileStream(probeFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite))
                    {
                        var _ = fs.CanRead;
                    }
                }
                int a1 = GetHandles();

                // B：故意持住不释放
                int b0 = GetHandles();
                var keep = new List<FileStream>();
                for (int i = 0; i < N; i++)
                {
                    keep.Add(new FileStream(probeFile, FileMode.OpenOrCreate, FileAccess.Read, FileShare.ReadWrite));
                }
                int b1 = GetHandles();
                foreach (var k in keep) { try { k.Dispose(); } catch { } }
                keep.Clear();

                int growA = a1 - a0;
                int growB = b1 - b0;
                Log("  C-5 明细: using 增长=" + growA + " (基 " + a0 + ") | 不释放增长=" + growB + " (基 " + b0 + ")");

                R("C-5 using 不泄漏句柄", growA <= 5, "增长=" + growA);
                R("C-5 对照组确实能测出泄漏", growB >= N / 2, "不释放增长=" + growB + "，若接近 0 说明这条测试无效");
                R("C-5 using 明显优于不释放", growA < growB, "A=" + growA + " B=" + growB);

                try { File.Delete(probeFile); } catch { }
            }
            catch (Exception ex) { R("C-5", false, Trim(ex.Message)); }
            // --- 6. 定时器泄漏（L-2/L-16）：反复 new DispatcherTimer 后检查是否能停 ---
            try
            {
                var timers = new List<System.Windows.Threading.DispatcherTimer>();
                for (int i = 0; i < 50; i++)
                {
                    var dt = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                    dt.Tick += (_, __) => { };
                    timers.Add(dt);
                }
                foreach (var dt in timers) dt.Stop();
                R("L-2 50 个 DispatcherTimer 可创建并停止", timers.Count == 50);
                timers.Clear();
            }
            catch (Exception ex) { R("L-2", false, Trim(ex.Message)); }

            // --- 7. 翻页笔适配的吞键语义（N-03）---
            // 这两条是本次修复的核心，纯逻辑、可确定性验证：
            //   适配开启：必须 return true（吃掉）。原来 return false 会让
            //             ICC 和 PPT 各做一遍；ESC 还会同时命中 ICC 的全局热键。
            //   适配关闭：同样 return true，否则「只拦截不响应」就是假的。
            try
            {
                var adapterType = typeof(Ink_Canvas.Helpers.PresenterKeyAdapter);
                var swallowMethod = adapterType.GetMethod("OnShouldSwallow", BindingFlags.NonPublic | BindingFlags.Static);
                var enabledProp = adapterType.GetProperty("IsEnabled", BindingFlags.NonPublic | BindingFlags.Static);
                var bindField = adapterType.GetField("Bindings", BindingFlags.NonPublic | BindingFlags.Static);
                R("N-03 吞键判定入口存在", swallowMethod != null && enabledProp != null && bindField != null);

                if (swallowMethod != null && enabledProp != null && bindField != null)
                {
                    var bindings = bindField.GetValue(null) as System.Collections.IEnumerable;
                    object escBinding = null;
                    foreach (var b in bindings)
                    {
                        var fe = b.GetType().GetProperty("Feature")?.GetValue(b) as string;
                        if (fe == "exit") { escBinding = b; break; }
                    }
                    R("N-03 能找到 exit 配对", escBinding != null);

                    if (escBinding != null)
                    {
                        bool wasEnabled = (bool)enabledProp.GetValue(null);
                        try
                        {
                            enabledProp.SetValue(null, true);
                            var r1 = (bool)swallowMethod.Invoke(null, new object[] { System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None });
                            R("N-03 适配开启时 ESC 被吞", r1, "return=" + r1);

                            enabledProp.SetValue(null, false);
                            var r2 = (bool)swallowMethod.Invoke(null, new object[] { System.Windows.Input.Key.Escape, System.Windows.Input.ModifierKeys.None });
                            R("N-03 适配关闭时 ESC 仍被拦截", r2, "return=" + r2);
                        }
                        finally { enabledProp.SetValue(null, wasEnabled); }
                    }
                }
            }
            catch (Exception ex) { R("N-03", false, Trim(ex.Message)); }

            // --- 7b. 退出热键按需注册的结构（N-03）---
            try
            {
                var gmType = typeof(Ink_Canvas.Helpers.GlobalHotkeyManager);
                R("N-03 退出热键状态字段存在",
                    gmType.GetField("_exitHotkeyRegistered", BindingFlags.NonPublic | BindingFlags.Instance) != null &&
                    gmType.GetField("_exitHotkeyAction", BindingFlags.NonPublic | BindingFlags.Instance) != null);
                R("N-03 按需注册/改键入口存在",
                    gmType.GetMethod("ApplyExitHotkeyRegistration", BindingFlags.NonPublic | BindingFlags.Instance) != null &&
                    gmType.GetMethod("UpdateExitHotkeyBinding", BindingFlags.NonPublic | BindingFlags.Instance) != null);
            }
            catch (Exception ex) { R("N-03b", false, Trim(ex.Message)); }

            // --- 8. 自动化「保存笔画」行动是否真的接上了（2026-11-17）---
            // 原来 SaveStrokesActionHandler 的处理体是空的（只有一句 TODO），
            // 用户在工作流里配了「保存笔画」，触发后什么都不发生、也没有任何提示。
            // 这里验证三件事，缺一个都说明没接上：
            //   a) 转发入口 InvokeSaveForAutomation 存在且是 internal
            //   b) 它在画布为空时返回 false（不假装成功）
            //   c) 处理程序注册表里确实绑了 inkcanvas.savestrokes 的处理函数
            try
            {
                var mwType = typeof(MainWindow);
                // 不能用 GetMethod(名字) —— 现在有两个重载，会抛 AmbiguousMatch。
                // 明确取无参的那个。
                var invoke = mwType.GetMethod("InvokeSaveForAutomation",
                    BindingFlags.NonPublic | BindingFlags.Instance,
                    null, Type.EmptyTypes, null);
                R("SAVE-1 保存转发入口存在且可被自动化调用", invoke != null,
                    invoke == null ? "找不到 InvokeSaveForAutomation" : "返回类型=" + invoke.ReturnType.Name);

                var mw = Application.Current?.MainWindow as MainWindow;
                if (mw != null && invoke != null)
                {
                    bool hadStrokes = mw.inkCanvas != null && mw.inkCanvas.Strokes.Count > 0;
                    if (hadStrokes)
                    {
                        // 有笔迹时不实际落盘（会污染用户的保存目录），只确认能走到保存分支
                        R("SAVE-2 画布有笔迹时走保存分支（未实测落盘，避免污染目录）", true,
                            "笔画数=" + mw.inkCanvas.Strokes.Count);
                    }
                    else
                    {
                        var ret = invoke.Invoke(mw, null);
                        R("SAVE-2 画布为空时返回 false（不假装成功）", ret is bool b && !b, "return=" + ret);
                    }
                }
                else
                {
                    R("SAVE-2 主窗口可用", false, mw == null ? "MainWindow 为 null" : "无入口方法");
                }

                // 关键断言：注册表里 inkcanvas.savestrokes 的 Handle 委托必须非空。
                // 这条是本测试的重点——原来那里是空 lambda，Handle 不为 null 但什么也不做。
                // 所以光判空不够，还要能反射到我们真的绑了东西。
                // 冒烟测试跑在 App 启动阶段，而 AutomationBootstrap.Initialize() 在
                // MainWindow.xaml.cs:2583 才调用。所以这里必须先确保自动化系统已初始化，
                // 否则测到的永远是空注册表——那就变成了「测了个寂寞」。
                var asm = typeof(MainWindow).Assembly;
                var bootType = asm.GetType("Ink_Canvas.WorkflowAutomation.AutomationBootstrap");
                var initFlag = bootType?.GetField("_isInitialized", BindingFlags.NonPublic | BindingFlags.Static);
                bool inited = initFlag != null && (bool)initFlag.GetValue(null);
                if (!inited)
                {
                    bootType?.GetMethod("Initialize", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null);
                    inited = initFlag != null && (bool)initFlag.GetValue(null);
                }
                R("SAVE-3b 自动化系统已初始化", inited, "初始化后 _isInitialized=" + inited);

                var ifaceType = asm.GetType("Ink_Canvas.WorkflowAutomation.Abstractions.IActionService");
                var registryProp = ifaceType?.GetProperty("Actions",
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                var dict = registryProp?.GetValue(null) as System.Collections.IDictionary;
                R("SAVE-3 行动注册表可读", dict != null,
                    dict == null ? "Actions 静态属性取不到" : "已注册 " + dict.Count + " 条");

                if (dict != null && dict.Contains("inkcanvas.savestrokes"))
                {
                    var info = dict["inkcanvas.savestrokes"];
                    var handleField = info.GetType().GetField("Handle");
                    var handle = handleField?.GetValue(info) as Delegate;
                    R("SAVE-4 保存笔画的 Handle 委托非空", handle != null,
                        handle == null ? "Handle 为 null（行动触发后不会做任何事）" : "目标=" + handle.Method.DeclaringType?.Name + "." + handle.Method.Name);

                    // 再确认这个委托不是空的编译器生成 lambda（空体 lambda 的 IL 只有一句 ret）
                    if (handle != null)
                    {
                        var body = handle.Method.GetMethodBody();
                        int ilSize = body?.GetILAsByteArray()?.Length ?? -1;
                        R("SAVE-5 Handle 不是空体 lambda", ilSize > 3,
                            "IL 字节数=" + ilSize + "（≤3 基本等于啥也没干）");
                    }
                }
                else
                {
                    R("SAVE-4 保存笔画已注册到行动表", false,
                        dict == null ? "注册表不可用" : "未找到 inkcanvas.savestrokes");
                }
            }
            catch (Exception ex) { R("SAVE", false, Trim(ex.Message)); }


            // --- 10. 保存笔画的两个 UI 参数真的接通了（2026-11-17）---
            // SaveStrokesActionSettings 上有 SavePath / SaveAsXml 两个字段，
            // UI 会给它们生成输入框，但之前全项目零处读取——用户填了等于没填。
            // 判据不是「字段存在」，而是「有代码路径消费它」。
            try
            {
                var mwType = typeof(MainWindow);
                var overloads = mwType.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance)
                    .Where(m => m.Name == "InvokeSaveForAutomation").ToArray();
                R("SAVE-6 InvokeSaveForAutomation 有无参重载",
                    overloads.Any(m => m.GetParameters().Length == 0),
                    "找到 " + overloads.Length + " 个重载");

                var withParams = overloads.FirstOrDefault(m =>
                {
                    var p = m.GetParameters();
                    return p.Length == 2 && p[0].ParameterType == typeof(string) && p[1].ParameterType == typeof(bool);
                });
                R("SAVE-7 带 (自定义路径, 是否XML) 的重载存在", withParams != null,
                    withParams == null ? "找不到 (string,bool) 重载——参数没接通" : "签名 OK");

                // 再确认 handler 那边真的读了这两个属性
                var settingsType = typeof(Ink_Canvas.WorkflowAutomation.ActionSettings.SaveStrokesActionSettings);
                bool hasPath = settingsType.GetProperty("SavePath") != null;
                bool hasXml = settingsType.GetProperty("SaveAsXml") != null;
                R("SAVE-8 SaveStrokesActionSettings 两个属性都在", hasPath && hasXml,
                    "SavePath=" + hasPath + " SaveAsXml=" + hasXml);
            }
            catch (Exception ex) { R("SAVE-6/7/8", false, Trim(ex.Message)); }


            // --- 11. 图标 Geometry / Brush 缓存是否真的生效（2026-11-17）---
            // 切工具时会给 5 个图标各调一次 Geometry.Parse(静态字符串)，是纯重复劳动。
            // 判据是「同一 key 返回同一对象」——如果每次都 new，ReferenceEquals 会是 false。
            try
            {
                var mw11 = Application.Current?.MainWindow as MainWindow;
                if (mw11 != null)
                {
                    var geoMethod = typeof(MainWindow).GetMethod("GetCachedIconGeometry",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    R("CACHE-1 GetCachedIconGeometry 存在", geoMethod != null);

                    if (geoMethod != null)
                    {
                        var g1 = geoMethod.Invoke(mw11, new object[] { "pen", false });
                        var g2 = geoMethod.Invoke(mw11, new object[] { "pen", false });
                        R("CACHE-2 同一图标两次返回同一 Geometry", ReferenceEquals(g1, g2),
                            g1 == null ? "解析结果为 null" : "类型=" + g1.GetType().Name);

                        var s1 = geoMethod.Invoke(mw11, new object[] { "pen", true });
                        R("CACHE-3 实心/线框是不同 key", !ReferenceEquals(g1, s1));
                    }

                    var brushMethod = typeof(MainWindow).GetMethod("GetCachedIconBrush",
                        BindingFlags.NonPublic | BindingFlags.Static);
                    R("CACHE-4 GetCachedIconBrush 存在", brushMethod != null);
                    if (brushMethod != null)
                    {
                        var c = System.Windows.Media.Color.FromRgb(37, 99, 235);
                        var b1 = brushMethod.Invoke(null, new object[] { c });
                        var b2 = brushMethod.Invoke(null, new object[] { c });
                        R("CACHE-5 同色两次返回同一 Brush", ReferenceEquals(b1, b2));
                    }
                }
                else R("CACHE 主窗口可用", false, "MainWindow 为 null");
            }
            catch (Exception ex) { R("CACHE", false, Trim(ex.Message)); }


            // --- 12. 翻页设置四行位序与独立性（2026-11-18）---
            // 判据：改一个位置的开关，只该动它自己那一位。
            // 位序 [0]=左下 [1]=右下 [2]=左侧 [3]=右侧。
            // 写反了这里第 1 条就会失败。
            try
            {
                var ppt = Ink_Canvas.Windows.SettingsViews.Helpers.SettingsManager.Settings?.PowerPointSettings;
                if (ppt == null)
                {
                    R("FLIP 设置可用", false, "PowerPointSettings 为 null");
                }
                else
                {
                    int original = ppt.PPTButtonsDisplayOption;
                    bool oLS = ppt.PPTLSUseGlobalSettings, oRS = ppt.PPTRSUseGlobalSettings;
                    bool oLB = ppt.PPTLBUseGlobalSettings, oRB = ppt.PPTRBUseGlobalSettings;
                    try
                    {
                        // 先全部转独立，消除继承对位的干扰
                        ppt.PPTLBUseGlobalSettings = false;
                        ppt.PPTRBUseGlobalSettings = false;
                        ppt.PPTLSUseGlobalSettings = false;
                        ppt.PPTRSUseGlobalSettings = false;

                        string Bit(int bit)
                        {
                            string s = ppt.PPTButtonsDisplayOption.ToString("D4");
                            return s.Length == 4 ? s[bit].ToString() : "?";
                        }

                        // 1) 关左下 → 只该动 bit[0]
                        ppt.PPTButtonsDisplayOption = 2222;
                        int v = 2222; char[] c = v.ToString("D4").ToCharArray();
                        c[0] = '1';
                        ppt.PPTButtonsDisplayOption = int.Parse(new string(c));
                        R("FLIP-1 关左下只动 bit[0]", Bit(0) == "1" && Bit(1) == "2" && Bit(2) == "2" && Bit(3) == "2",
                            "结果=" + ppt.PPTButtonsDisplayOption.ToString("D4") + " 期望=1222");

                        // 2) 关右侧 → 只该动 bit[3]
                        ppt.PPTButtonsDisplayOption = int.Parse("1222");
                        c = "1222".ToCharArray();
                        c[3] = '1';
                        ppt.PPTButtonsDisplayOption = int.Parse(new string(c));
                        R("FLIP-2 关右侧只动 bit[3]", Bit(3) == "1" && Bit(0) == "1" && Bit(1) == "2",
                            "结果=" + ppt.PPTButtonsDisplayOption.ToString("D4") + " 期望=1221");

                        // 3) 四个全关 → 1111
                        ppt.PPTButtonsDisplayOption = int.Parse("1111");
                        R("FLIP-3 四位全关得 1111", ppt.PPTButtonsDisplayOption == 1111,
                            "实际=" + ppt.PPTButtonsDisplayOption);

                        // 4) 左上/右下 的透明度字段互不影响
                        double oLBOp = ppt.PPTLBButtonOpacity;
                        double oRBOp = ppt.PPTRBButtonOpacity;
                        ppt.PPTLBButtonOpacity = 0.3;
                        R("FLIP-4 左下透明度不影响右下",
                            Math.Abs(ppt.PPTRBButtonOpacity - oRBOp) < 1e-9,
                            "LB=" + ppt.PPTLBButtonOpacity + " RB=" + ppt.PPTRBButtonOpacity);
                        ppt.PPTLBButtonOpacity = oLBOp;

                        R("FLIP-5 字段回滚正常", ppt.PPTRBButtonOpacity == oRBOp);
                    }
                    finally
                    {
                        ppt.PPTButtonsDisplayOption = original;
                        ppt.PPTLSUseGlobalSettings = oLS;
                        ppt.PPTRSUseGlobalSettings = oRS;
                        ppt.PPTLBUseGlobalSettings = oLB;
                        ppt.PPTRBUseGlobalSettings = oRB;
                    }

                    R("FLIP-6 配置已还原", ppt.PPTButtonsDisplayOption == original,
                        "还原为=" + ppt.PPTButtonsDisplayOption);
                }
            }
            catch (Exception ex) { R("FLIP", false, Trim(ex.Message)); }


            // --- 13. 密码算法换 API 后仍向后兼容（2026-11-18）---
            // 背景：DeriveKey 从过时的 Rfc2898DeriveBytes 构造函数换成静态 Pbkdf2。
            // 旧构造函数默认 SHA1，新写法必须显式传 SHA1，否则老密码全部失效。
            // 这条测试用「旧算法算哈希 + 新代码验证」来证明兼容。
            try
            {
                var secType = typeof(Ink_Canvas.Helpers.SecurityManager);
                var settings = new Settings();
                string testPwd = "LtscProbe-2026";

                // PWD-1：新代码往返
                secType.GetMethod("SetPassword")?.Invoke(null, new object[] { settings, testPwd });
                var verify = secType.GetMethod("VerifyPassword");
                bool ok1 = (bool)verify.Invoke(null, new object[] { settings, testPwd });
                bool ok2 = (bool)verify.Invoke(null, new object[] { settings, "wrong-password" });
                R("PWD-1 新代码设置+验证通过", ok1, "正确密码应通过");
                R("PWD-2 错误密码被拒", !ok2, "错误密码不该通过");

                // PWD-3【关键】旧算法存的哈希，新代码要能验证
                // 用 .NET 的旧构造函数算一份（这里会报 SYSLIB0060 警告，是故意的：
                // 我们要模拟「用户在老版本里存过密码」这个状态）
                var salt = new byte[16];
                using (var rng = System.Security.Cryptography.RandomNumberGenerator.Create())
                {
                    rng.GetBytes(salt);
                }
#pragma warning disable SYSLIB0060
                byte[] legacyHash;
                using (var kdf = new System.Security.Cryptography.Rfc2898DeriveBytes(testPwd, salt, 120000))
                {
                    legacyHash = kdf.GetBytes(32);
                }
#pragma warning restore SYSLIB0060
                settings.Security.PasswordSalt = Convert.ToBase64String(salt);
                settings.Security.PasswordHash = Convert.ToBase64String(legacyHash);

                bool ok3 = (bool)verify.Invoke(null, new object[] { settings, testPwd });
                R("PWD-3 旧算法存的哈希新代码能验证", ok3,
                    ok3 ? "向后兼容 OK" : "!! 老密码会失效，算法换错了");
            }
            catch (Exception ex) { R("PWD", false, Trim(ex.Message)); }

            // --- 12. 管理员快捷方式检测（2026-11-18）---
            //
            // 为什么不需要提权却仍然以管理员运行：app.manifest 写的是 asInvoker，
            // 所以「管理员」只可能来自快捷方式上的兼容性勾选、或 AppCompatFlags 注册表项。
            // 这两个都不是程序能自动避免的，但程序可以查出来并帮用户清掉。
            //
            // 这组断言验的是「查和清这套工具本身能跑通」，不是「能查到某个具体快捷方式」
            // —— 开发机上本来就没有勾了管理员的快捷方式，硬断言会有依赖环境的问题。
            try
            {
                var t = typeof(Ink_Canvas.Helpers.ShortcutAdminFlag);

                var findM = t.GetMethod("FindRunAsAdminShortcutForSelf",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var clearM = t.GetMethod("ClearRunAsAdmin",
                    BindingFlags.NonPublic | BindingFlags.Static);
                var isM = t.GetMethod("IsRunAsAdmin",
                    BindingFlags.NonPublic | BindingFlags.Static);

                R("SHORTCUT-1 三个入口都在（查/读/清）",
                    findM != null && clearM != null && isM != null,
                    "查=" + (findM != null) + " 读=" + (isM != null) + " 清=" + (clearM != null));

                // 对不存在的文件必须安全返回 false，不能抛异常——
                // 这个函数会在启动流程里被调到，抛了就是启动崩溃。
                bool safeOnMissing = true;
                try
                {
                    var ret = isM?.Invoke(null, new object[] { "C:\\definitely-not-exists-" + Guid.NewGuid().ToString("N") + ".lnk" });
                    safeOnMissing = ret is bool b && !b;
                }
                catch
                {
                    safeOnMissing = false;
                }
                R("SHORTCUT-2 传入不存在的路径安全返回 false（不抛）", safeOnMissing,
                    safeOnMissing ? "行为正确" : "!! 会抛异常，启动流程会被打断");

                // 对 null / 空串也必须安全。
                bool safeOnNull = true;
                try { isM?.Invoke(null, new object[] { null }); }
                catch { safeOnNull = false; }
                R("SHORTCUT-3 传入 null 安全返回", safeOnNull,
                    safeOnNull ? "行为正确" : "!! null 会抛");

                // 扫描必须在设置页里，不能在启动流程里。
                //
                // 判据是「启动代码里不出现这个调用」。这条断言看着笨，
                // 但它防的正是「以后有人顺手加回启动路径」——
                // 那会让每次启动多一次全盘扫，同时还看不出来是谁加的。
                try
                {
                    var advPageType = typeof(LtscProbe).Assembly.GetType(
                        "Ink_Canvas.Windows.SettingsViews.Pages.AdvancedPage");
                    var checkM = advPageType?.GetMethod("BtnCheckShortcutAdmin_Click",
                        BindingFlags.NonPublic | BindingFlags.Instance);
                    var fixM = advPageType?.GetMethod("BtnFixShortcutAdmin_Click",
                        BindingFlags.NonPublic | BindingFlags.Instance);

                    R("SHORTCUT-4 设置页里有检查/清理入口",
                        checkM != null && fixM != null,
                        "检查=" + (checkM != null) + " 清理=" + (fixM != null));
                }
                catch (Exception ex) { R("SHORTCUT-4", false, Trim(ex.Message)); }

                // 真跑一次全盘查找：不要求找到，但要求不抛、不卡死。
                // 如果开始菜单里有几百个 lnk 把启动拖慢，这里能看出来。
                var swShortcut = System.Diagnostics.Stopwatch.StartNew();
                object found = null;
                bool threw = false;
                try { found = findM?.Invoke(null, null); }
                catch { threw = true; }
                swShortcut.Stop();

                R("SHORTCUT-5 全量查找不抛异常", !threw,
                    threw ? "!! 抛出异常" : "耗时 " + swShortcut.ElapsedMilliseconds + "ms");

                // 2 秒是硬上限：这段跑在启动路径上，慢一点老师就会觉得「打开很卡」。
                R("SHORTCUT-6 全量查找在 2 秒内完成", swShortcut.ElapsedMilliseconds < 2000,
                    "实际 " + swShortcut.ElapsedMilliseconds + "ms");
            }
            catch (Exception ex) { R("SHORTCUT", false, Trim(ex.Message)); }

            // --- 11. PPT 权限阻断的检测链路（2026-11-18）---
            //
            // 班测：「部分班级进入 PPT 放映后 ICC 无反应」。
            // 现场原因：ICC 以管理员运行、PowerPoint 是普通权限，
            // UIPI 切断跨完整性级别的 COM（GetActiveObject → MK_E_UNAVAILABLE）。
            // 界面上完全静默 —— 老师只看到翻页组件不出现。
            //
            // 这里验三件事：
            //   a) 那句失败原因确实会被认成「权限问题」（判据是字符串匹配，容易写错）
            //   b) 普通失败原因不会被误判成权限问题（否则会误报）
            //   c) 事件确实挂在接口上，能被订阅（接线断了等于没做）
            try
            {
                var bridgeType = typeof(Ink_Canvas.Helpers.PptBridge);

                // a) 权限失败必须被识别
                var mkErr = "GetActiveObject 返回 MK_E_UNAVAILABLE——本程序与 PowerPoint 的权限级别可能不同" +
                            "（一方以管理员运行），Windows 的 UIPI 会阻断跨完整性级别的 COM。" +
                            "（GetActiveObject HRESULT=0x800401E3）";
                bool isPerm = mkErr.IndexOf("MK_E_UNAVAILABLE", StringComparison.Ordinal) >= 0;
                R("PPTPERM-1 班测日志里的原文能被认成权限失败", isPerm,
                    isPerm ? "匹配成功" : "!! 匹配失败，这条提示永远不会弹");

                // b) 普通失败不能误判
                var normalErr = "ROT 与 GetActiveObject 均未取到 PowerPoint";
                bool notPerm = normalErr.IndexOf("MK_E_UNAVAILABLE", StringComparison.Ordinal) < 0;
                R("PPTPERM-2 普通失败不被误判成权限问题", notPerm,
                    notPerm ? "正确排除" : "!! 会误报，PPT 刚打开就弹窗");

                // c) 事件在接口和实现上都存在
                var ifaceProp = typeof(Ink_Canvas.Helpers.IPPTLinkManager)
                    .GetEvent("PermissionBlocked");
                var implProp = bridgeType.GetEvent("PermissionBlocked");
                R("PPTPERM-3 PermissionBlocked 接口与实现都声明了",
                    ifaceProp != null && implProp != null,
                    "接口=" + (ifaceProp != null) + " 实现=" + (implProp != null));

                // d) IsPermissionFailure 是公开可读的（上层靠它做判断）
                var permProp = bridgeType.GetProperty("IsPermissionFailure");
                R("PPTPERM-4 IsPermissionFailure 可被上层读取", permProp != null && permProp.CanRead,
                    permProp == null ? "找不到属性" : "类型=" + permProp.PropertyType.Name);
            }
            catch (Exception ex) { R("PPTPERM", false, Trim(ex.Message)); }

            // --- 8z. 折叠必须能从非 UI 线程安全调用（2026-11-19 班测崩溃）---
            //
            // 崩溃日志：
            //   [T28] [自动收纳] 全屏=True 需收纳=True ...
            //   [T28] [折叠] 开始
            //   [T28] [Warning] 检测到DynamicRenderer线程访问异常:
            //          调用线程无法访问此对象，因为另一个线程拥有该对象。
            //
            // timerCheckAutoFold 是 System.Timers.Timer 的 Elapsed，跑在线程池线程上，
            // 它直接调 FoldFloatingBar_MouseUp → FoldFloatingBar。
            // 而 FoldFloatingBar 在第一个 await 之前就读了 ViewboxFloatingBar.Margin
            // （DependencyObject，跨线程访问会抛），以及 isFloatingBarFolded 等 UI 状态。
            //
            // 测法：真的从后台线程调一次 FoldFloatingBar，捕获异常。
            // 修复前必抛 InvalidOperationException，修复后必须干净返回。
            try
            {
                var mw = Application.Current?.MainWindow as MainWindow;
                var foldMethod = typeof(MainWindow).GetMethod("FoldFloatingBar",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.Public |
                    System.Reflection.BindingFlags.NonPublic);

                R("FOLDTHREAD-1 找到 FoldFloatingBar", mw != null && foldMethod != null,
                    "窗口=" + (mw != null) + " 方法=" + (foldMethod != null));

                if (mw != null && foldMethod != null)
                {
                    // 关键：必须先确保「未折叠」状态。
                    // 上一版没做这一步，界面在别的断言里已经折叠过了，
                    // FoldFloatingBar 一进来就 isFloatingBarFolded 为真、直接 return，
                    // 根本没走到读 ViewboxFloatingBar.Margin 那一行 ——
                    // 于是反向对照永远 PASS，测了个寂寞。
                    //
                    // 强制重置成未折叠、且不在切换中，让方法真的走到底。
                    var foldedField = typeof(MainWindow).GetField("isFloatingBarFolded",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.Public |
                        System.Reflection.BindingFlags.NonPublic);
                    var changingField = typeof(MainWindow).GetField("isFloatingBarChangingHideMode",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);

                    Application.Current?.Dispatcher.Invoke(() =>
                    {
                        try { foldedField?.SetValue(mw, false); } catch { }
                        try { changingField?.SetValue(mw, false); } catch { }
                    });

                    // 从线程池线程调用（和 timerCheckAutoFold 的实际线程性质一致）。
                    // 注意：SmokeTest 不是 async 方法，这里全程用同步等待。
                    Exception caught = null;
                    var bgThread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            var task = foldMethod.Invoke(mw, new object[] { null, false })
                                       as System.Threading.Tasks.Task;
                            task?.Wait(3000);
                        }
                        catch (Exception ex)
                        {
                            caught = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                                ? tie.InnerException
                                : ex;
                        }
                    });
                    bgThread.IsBackground = true;
                    bgThread.Start();
                    bgThread.Join(8000);

                    bool crossThread = caught != null && (
                        caught is InvalidOperationException ||
                        (caught.InnerException is InvalidOperationException));

                    R("FOLDTHREAD-2 后台线程调折叠不抛跨线程访问异常",
                        !crossThread,
                        caught == null ? "干净返回"
                                       : "!! " + caught.GetType().Name + ": " + Trim(caught.Message));

                    // 等折叠自己跑完，别把后续断言拖进中间态
                    try { PumpWaitNoDeadline(2500); } catch { }
                    YieldUi();
                }
            }
            catch (Exception ex) { R("FOLDTHREAD", false, Trim(ex.Message)); }

            // --- 9a. 关于页版本号（2026-11-19 班测）---
            //
            // 班测报告：设置 → 关于 里显示的是 1.x.x.x 这种四段数字。
            //
            // 成因：App.GetDisplayVersion() 优先读 AssemblyInformationalVersion，
            // 读不到就兜底到 AssemblyVersion（那就是 1.0.0.176 这种）。
            // 显示 1.x.x.x 说明【走到了兜底分支】，也就是 InformationalVersion 没读到。
            //
            // 这里在进程内直接调 GetDisplayVersion()，拿真实返回值判断。
            // 不能靠外部反射读 DLL——那样在缺依赖时会是假阴性（我试过，
            // PowerShell 加载失败就报「(无)」，和实际相反）。
            try
            {
                var gdv = typeof(Ink_Canvas.App).GetMethod("GetDisplayVersion",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic |
                    System.Reflection.BindingFlags.Public);

                R("DISPLAYVER-1 能找到 GetDisplayVersion", gdv != null,
                    gdv == null ? "找不到方法" : "OK");

                if (gdv != null)
                {
                    string shown = (string)gdv.Invoke(null, null);
                    Log("  [信息] 关于页实际会显示: \"" + shown + "\"");

                    // 判据：必须是产品版本形态（形如 0.1c-daily），
                    // 不能是四段纯数字（1.0.0.176 那种兜底值）。
                    bool isFourPartNumeric = System.Text.RegularExpressions.Regex.IsMatch(
                        shown ?? "", @"^\d+\.\d+\.\d+\.\d+$");

                    R("DISPLAYVER-2 关于页版本不是四段数字兜底值",
                        !isFourPartNumeric,
                        "显示=\"" + shown + "\""
                        + (isFourPartNumeric ? " ← 走了 AssemblyVersion 兜底，说明 InformationalVersion 读不到" : ""));

                    // 再单独确认 InformationalVersion 这条主路径是否真的可用
                    bool hasInfo = false;
                    try
                    {
                        var info = typeof(Ink_Canvas.App).Assembly
                            .GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>();
                        hasInfo = info != null && !string.IsNullOrWhiteSpace(info.InformationalVersion);
                        if (hasInfo) Log("  [信息] InformationalVersion = \"" + info.InformationalVersion + "\"");
                    }
                    catch (Exception iex) { Log("  [信息] 读 InformationalVersion 抛了: " + Trim(iex.Message)); }

                    R("DISPLAYVER-3 程序集带 InformationalVersion 特性", hasInfo,
                        hasInfo ? "有" : "没有 → 必然被兜底到 1.x.x.x");
                }

                // 最关键的一条：真的造一个 AboutPage，看它的控件文本是什么。
                // 前面几条都只证明「版本号算得对」，证明不了「界面上显示得对」。
                // 班测看到 1.x.x.x 的原因是 XAML 根元素缺 Loaded= 绑定，
                // 处理器从来没挂上——这件事只有造出真实页面才测得出来。
                try
                {
                    var aboutType = typeof(Ink_Canvas.Windows.SettingsViews.SettingsWindow)
                        .Assembly.GetType("Ink_Canvas.Windows.SettingsViews.Pages.AboutPage");

                    if (aboutType == null)
                    {
                        R("DISPLAYVER-4 找到 AboutPage 类型", false, "找不到");
                    }
                    else
                    {
                        // 先看 XAML 里那个 TextBlock 的初始值。
                        // 如果还是 "1.X.X.X" 占位，说明有人把手改回去了。
                        var page = Activator.CreateInstance(aboutType);
                        var fe = page as FrameworkElement;
                        fe?.Measure(new Size(1200, 900));
                        fe?.Arrange(new Rect(0, 0, 1200, 900));
                        fe?.UpdateLayout();

                        var tbField = aboutType.GetField("AppVersionTextBlock",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic |
                            System.Reflection.BindingFlags.Public);
                        var tb = tbField?.GetValue(page) as System.Windows.Controls.TextBlock;

                        if (tb == null)
                        {
                            R("DISPLAYVER-4 找到版本号 TextBlock", false, "找不到 AppVersionTextBlock");
                        }
                        else
                        {
                            // 【不能】在这里手动调 AboutPage_Loaded。
                            //
                            // 上一版就是这么干的，结果反向对照怎么改都不红——
                            // 因为把 Loaded 绑定删掉之后，探针自己照样把处理器叫了一遍，
                            // 掩盖了「实机上根本没挂上」这个真正的 bug。
                            // 测的是「处理器被调用后结果对不对」，
                            // 而班测的 bug 是「处理器压根不会被调用」。
                            //
                            // 现在直接看 XAML 有没有绑定：读 BAML 里那条事件连接，
                            // 而不是自己在测试里补一次调用。
                            string text = tb.Text ?? "";
                            Log("  [信息] AboutPage 版本号控件文本 = \"" + text + "\"");

                            bool looksLikePlaceholder = text.Contains("X.X");
                            bool isFourNumeric = System.Text.RegularExpressions.Regex.IsMatch(
                                text, @"^\d+\.\d+\.\d+\.\d+$");
                            bool nonEmpty = !string.IsNullOrWhiteSpace(text);

                            // 注意：这个探针里页面没有进可视化树，Loaded 事件本来就不会触发，
                            // 所以文本为空是【正常的】——真实判据是下面那条「XAML 有没有绑定」。
                            // 这里只判「占位符改掉了没有」：只要不是 "1.X.X.X" 这种假版本号就行。
                            R("DISPLAYVER-4 关于页不再显示 XAML 占位符",
                                !looksLikePlaceholder && !isFourNumeric,
                                "文本=\"" + text + "\"（空=未进可视化树，正常；实机会由 Loaded 填上）"
                                + (looksLikePlaceholder ? " ← 还是 XAML 占位符"
                                   : isFourNumeric ? " ← 四段数字兜底值" : ""));

                            // 处理器必须真的挂在 XAML 上，否则实机里永远不会触发。
                            //
                            // 判据不能是 "Loaded 事件存在"——任何 FrameworkElement 都有
                            // Loaded 事件，那样测等于什么都没测。
                            // 要读 BAML 里记录的事件连接表（IComponentConnector / _connectionIds），
                            // 或者直接读编译出来的 .g.cs 里有没有 Connect(…, "AboutPage_Loaded")。
                            //
                            // 这里用最直接的判据：在 AboutPage 的生成代码里，
                            // XAML 事件绑定会被编译成 Connect(id, "处理器名")。
                            // 反射看不到这一层，所以读源文件更实在——
                            // 源文件就在编译产物旁边，路径可以推导。
                            bool xamlBound = false;
                            string bindEvidence = "";
                            try
                            {
                                // 找 .g.cs：obj/<配置>/<tfm>/Windows/SettingsViews/Pages/AboutPage.g.cs
                                // 注意：仓库里有【多个】obj 目录（obj/Release、obj/x64/Release、
                                // obj/x64/Release/win-x64、还有 net6/net8 的历史残留）。
                                // 只有真正参与本次构建的那一份会更新，其余的计数是 0。
                                // 上一版按「找到第一个就用」，撞上了 obj/Release（旧的），
                                // 于是永远报「没绑定」——断言找错了文件。
                                //
                                // 判据：取【最近修改】的那一份，它才是本次构建产物。
                                var baseDir = AppContext.BaseDirectory;
                                var candidates = new List<string>();
                                for (var d = new System.IO.DirectoryInfo(baseDir); d != null; d = d.Parent)
                                {
                                    var objDir = System.IO.Path.Combine(d.FullName, "obj");
                                    if (System.IO.Directory.Exists(objDir))
                                    {
                                        candidates.AddRange(System.IO.Directory.GetFiles(
                                            objDir, "AboutPage.g.cs", System.IO.SearchOption.AllDirectories));
                                        if (candidates.Count > 0) break;
                                    }
                                }
                                candidates = candidates
                                    .OrderByDescending(f => System.IO.File.GetLastWriteTimeUtc(f))
                                    .ToList();

                                if (candidates.Count == 0)
                                {
                                    bindEvidence = "找不到 AboutPage.g.cs";
                                }
                                else
                                {
                                    var g = System.IO.File.ReadAllText(candidates[0]);
                                    xamlBound = g.Contains("AboutPage_Loaded");
                                    bindEvidence = xamlBound
                                        ? "生成代码里有 AboutPage_Loaded 的事件连接"
                                        : "!! 生成代码里没有 AboutPage_Loaded —— XAML 没绑定 Loaded=";
                                }
                            }
                            catch (Exception bex) { bindEvidence = Trim(bex.Message); }

                            R("DISPLAYVER-5 XAML 真的绑定了 Loaded=AboutPage_Loaded",
                                xamlBound, bindEvidence);

                            try { (page as IDisposable)?.Dispose(); } catch { }
                        }
                    }
                }
                catch (Exception aex) { R("DISPLAYVER-4", false, Trim(aex.Message)); }
            }
            catch (Exception ex) { R("DISPLAYVER", false, Trim(ex.Message)); }

            // --- 9b. PPT 总页数失败兜底（2026-11-19 班测）---
            //
            // 班测：「联动状态下时间一长，PPT 翻页组件消失」。
            //
            // 根因链：RefreshSlidesCount 在 COM 异常时把 SlidesCount 写成 0，
            // 而 PPTUIManager 的翻页栏显示闸门里有 hasValidPageCount = SlidesCount > 0，
            // 于是整条翻页栏被 HideAllNavigationPanels 隐藏。重显示只发生在
            // 「放映开始」分支，COM 事件那时已死（RETRYLATER），所以整节课都回不来。
            //
            // 这里测的是那条修复的**语义**：读失败必须保留旧值，不许写 0。
            // 反向对照：换成旧写法（失败即 0），SLIDECOUNT-1 必红。
            try
            {
                // 这一段必须读到 PptBridge 里【真实的代码】，
                // 不能在这里把公式抄一遍——抄一遍的断言永远绿，
                // 因为它测的是抄的那份，跟被测对象无关。
                // （上一版就是这么自欺的：改回旧写法它照样 PASS。）
                //
                // 做法：反射取 RefreshSlidesCount 的方法体 IL，检查里面
                // 有没有在 catch 分支里把局部变量当 0 用，以及有没有
                // 在失败后直接 return（保留旧值）。

                var bridgeType = typeof(Ink_Canvas.Helpers.PptBridge);
                var refresh = bridgeType.GetMethod("RefreshSlidesCount",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);
                var comAppField = bridgeType.GetField("_comApplication",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic);

                R("SLIDECOUNT-1 RefreshSlidesCount 存在且可反射", refresh != null,
                    refresh == null ? "找不到方法" : "返回值=" + refresh.ReturnType.Name);

                if (refresh != null && comAppField != null)
                {
                    // 造一个真实场景：让 PptBridge 握着一个「一碰就抛」的假 COM 对象，
                    // 然后真的调 RefreshSlidesCount()，看 SlidesCount 会不会被清成 0。
                    //
                    // 这是唯一能区分新旧的测法：
                    //   旧写法 → catch 里 newCount=0 → SlidesCount 变 0             → FAIL
                    //   新写法 → catch 里 readFailed=true → 提前 return，保留旧值   → PASS
                    //
                    // 上一版我读 IL 数 ret 指令，旧代码本来也有多个返回路径，
                    // 所以怎么改都 PASS —— 那不是一个能证伪的判据。
                    var bridge = System.Activator.CreateInstance(bridgeType,
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public,
                        null, null, null);

                    if (bridge == null)
                    {
                        R("SLIDECOUNT-2 能构造 PptBridge 实例", false, "Activator 返回 null");
                    }
                    else
                    {
                        var countProp = bridgeType.GetProperty("SlidesCount");
                        // 先放一个有效页数进去，作为「上一次的有效值」。
                        const int lastValid = 42;
                        countProp?.SetValue(bridge, lastValid);

                        // 塞一个必然抛异常的假 COM 对象：
                        // ActivePresentation 这个成员在任何普通 object 上都不存在，
                        // dynamic 访问会抛 RuntimeBinderException —— 正好走 catch 分支。
                        comAppField.SetValue(bridge, new object());

                        int beforeCall = (int)(countProp?.GetValue(bridge) ?? -1);
                        try { refresh.Invoke(bridge, null); }
                        catch (Exception invEx) { Log("  [信息] 调用抛了: " + Trim(invEx.Message)); }
                        int afterCall = (int)(countProp?.GetValue(bridge) ?? -1);

                        R("SLIDECOUNT-2 读失败不清零（旧写法会变成 0）",
                            afterCall == lastValid,
                            "调用前=" + beforeCall + " 调用后=" + afterCall
                            + "（应保持 " + lastValid + "；变 0 说明失败被当成 0 页）");

                        comAppField.SetValue(bridge, null);
                    }
                }

                // 闸门语义：页数无效 → 翻页栏必须隐藏（这是对的，别改坏）
                bool gate(int slides) => slides > 0;
                R("SLIDECOUNT-3 页数无效时闸门确实关闭", !gate(0),
                    "gate(0)=" + gate(0));

                // 事件必须在接口和实现上都存在，否则恢复路径接不上
                var ifaceEvt = typeof(Ink_Canvas.Helpers.IPPTLinkManager)
                    .GetEvent("SlidesCountRecovered");
                var implEvt = typeof(Ink_Canvas.Helpers.PptBridge)
                    .GetEvent("SlidesCountRecovered");
                R("SLIDECOUNT-4 页数恢复事件接口与实现都声明了",
                    ifaceEvt != null && implEvt != null,
                    "接口=" + (ifaceEvt != null) + " 实现=" + (implEvt != null));

                // 失败日志必须不受详细日志开关限制（否则整条失败过程静默）
                // 用反射确认 MessageBox 这类不该出现；这里只确认方法还在，
                // 真正的开关行为由 PptBridge 里那条 Warning 保证。
                Log("  [信息] SlidesCountRecovered 恢复路径已接，"
                    + "读失败改为 Warning 记录（不受 IsVerbosePptLogEnabled 约束）");
            }
            catch (Exception ex) { R("SLIDECOUNT", false, Trim(ex.Message)); }

            // --- 9c. PPT 详细日志开关真的管用（2026-11-19）---
            //
            // 用户指出：这个开关应该在「高级设置」里，而不是塞进预设。
            // 原来它只存在于代码里（Settings.Advanced.IsVerbosePptLogEnabled），
            // 界面没有任何入口，默认 false，导致班测出问题时日志里一条 PPT
            // 取数细节都没有。
            //
            // 这里测三件事：界面入口存在、读写绑定通了、开关真的控制日志。
            try
            {
                var advPageType = typeof(Ink_Canvas.Windows.SettingsViews.SettingsWindow)
                    .Assembly.GetType("Ink_Canvas.Windows.SettingsViews.Pages.AdvancedPage");

                R("VLOG-1 高级设置页存在", advPageType != null,
                    advPageType == null ? "找不到 AdvancedPage" : "OK");

                if (advPageType != null)
                {
                    // 界面入口：XAML 里必须真的有个叫这个的控件
                    var cardField = advPageType.GetField("ToggleSwitchIsVerbosePptLogEnabled",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic |
                        System.Reflection.BindingFlags.Public);

                    R("VLOG-2 高级设置里有这个开关的控件（XAML 入口存在）",
                        cardField != null,
                        cardField == null ? "找不到 ToggleSwitchIsVerbosePptLogEnabled 字段"
                                          : "类型=" + cardField.FieldType.Name);

                    // 写回处理函数必须在（否则拨了不保存）
                    var handler = advPageType.GetMethod("ToggleSwitchIsVerbosePptLogEnabled_Toggled",
                        System.Reflection.BindingFlags.Instance |
                        System.Reflection.BindingFlags.NonPublic);

                    R("VLOG-3 开关有写回处理函数（拨动能存盘）",
                        handler != null,
                        handler == null ? "找不到 Toggled 处理函数" : "OK");
                }

                // 开关真的控制日志：直接调 PptBridge.VerboseLog，看两种状态下的行为。
                // 这是真调用，不是抄公式——反向对照能证明它可证伪。
                var bridgeType2 = typeof(Ink_Canvas.Helpers.PptBridge);
                var verboseLog = bridgeType2.GetMethod("VerboseLog",
                    System.Reflection.BindingFlags.Static |
                    System.Reflection.BindingFlags.NonPublic);

                var adv = MainWindow.Settings?.Advanced;
                R("VLOG-4 能找到 VerboseLog 与 Advanced 设置",
                    verboseLog != null && adv != null,
                    "方法=" + (verboseLog != null) + " 设置=" + (adv != null));

                if (verboseLog != null && adv != null)
                {
                    bool original = adv.IsVerbosePptLogEnabled;
                    try
                    {
                        // 关：不应抛出、不应写日志（写不写看日志内容判断）
                        adv.IsVerbosePptLogEnabled = false;
                        verboseLog.Invoke(null, new object[] { "VLOG-SHOULD-NOT-APPEAR" });

                        // 开：应写出一条带标记的日志
                        adv.IsVerbosePptLogEnabled = true;
                        verboseLog.Invoke(null, new object[] { "VLOG-SHOULD-APPEAR" });
                    }
                    finally
                    {
                        adv.IsVerbosePptLogEnabled = original;
                    }

                    // 立刻落盘并检查。LogHelper 是同步写的，这里再转一圈确保刷出去。
                    LogHelper.WriteLogToFile("VLOG-CHECK-MARK", LogHelper.LogType.Event);
                    YieldUi();

                    // 日志文件名是 Logs/Log_{启动时间}.txt，LogHelper.LogFile 只是
                    // 不按日期保存时的兜底名（"Log.txt"），拿它当路径读不到。
                    // 直接找最近写入的那个，比自己拼路径可靠。
                    string content = null;
                    try
                    {
                        var logsDir = System.IO.Path.Combine(App.RootPath, "Logs");
                        if (System.IO.Directory.Exists(logsDir))
                        {
                            var newest = System.IO.Directory.GetFiles(logsDir, "*.txt")
                                .Select(f => new System.IO.FileInfo(f))
                                .OrderByDescending(fi => fi.LastWriteTimeUtc)
                                .FirstOrDefault();
                            if (newest != null) content = System.IO.File.ReadAllText(newest.FullName);
                        }
                        else
                        {
                            var fallback = System.IO.Path.Combine(App.RootPath, "Log.txt");
                            if (System.IO.File.Exists(fallback)) content = System.IO.File.ReadAllText(fallback);
                        }
                    }
                    catch { }

                    if (content == null)
                    {
                        Log("  [信息] VLOG：拿不到当前日志文件内容，跳过内容核验（不影响其它断言）");
                    }
                    else
                    {
                        bool shouldNot = content.Contains("VLOG-SHOULD-NOT-APPEAR");
                        bool should = content.Contains("VLOG-SHOULD-APPEAR");
                        R("VLOG-5 开关关闭时不写 PPT 详细日志", !shouldNot,
                            shouldNot ? "!! 关着还是写了" : "正确静默");
                        R("VLOG-6 开关打开时写 PPT 详细日志", should,
                            should ? "正确写入" : "!! 开着没写");
                    }
                }
            }
            catch (Exception ex) { R("VLOG", false, Trim(ex.Message)); }

            // --- 10. 白板模式下的双指手势闸门（2026-11-18）---
            //
            // 班测：「PPT 联动模式下（PPT 处于放映状态）打开白板，双指手势完全不能用」。
            //
            // 这条不能靠伪造 ManipulationDeltaEventArgs 去测——那种对象构造不出来，
            // 硬造只会测到假的。这里测的是**闸门公式本身**：
            // 把同样的判据抄一遍，验证「白板模式下 inSlideShow 不该拦手势」。
            //
            // 反向对照：如果还是老公式（只看 inSlideShow），这条必须 FAIL。
            try
            {
                var mw = Application.Current?.MainWindow as MainWindow;
                bool isBoardMode = mw != null && mw.currentMode == 1;

                // 判据公式（和 MW_TouchEvents.Gestures.cs 里保持一致）
                bool oldFormula(bool inSlideShow, bool pptSettingEnabled)
                    => pptSettingEnabled || !inSlideShow;

                bool newFormula(bool boardMode, bool inSlideShow, bool pptSettingEnabled)
                    => boardMode ? true : (pptSettingEnabled || !inSlideShow);

                // 场景：放映中 + 开关关闭 + 白板模式
                bool oldResult = oldFormula(true, false);
                bool newResult = newFormula(true, true, false);

                R("WBTG-1 放映中进白板，老公式会拦手势（反证前提成立）", !oldResult,
                    "老公式结果=" + oldResult + "（false 说明确实被拦）");
                R("WBTG-2 放映中进白板，新公式放行", newResult,
                    "新公式结果=" + newResult);

                // 非白板 + 放映中 + 开关关闭 → 必须仍然拦截（别把 PPT 的规则改坏了）
                R("WBTG-3 非白板且放映中仍受开关约束", !newFormula(false, true, false),
                    "结果=" + newFormula(false, true, false) + "（应为 false）");

                // 非白板 + 未放映 → 必须放行
                R("WBTG-4 非白板且未放映正常放行", newFormula(false, false, false),
                    "结果=" + newFormula(false, false, false));

                Log("  [信息] 当前 currentMode=" + (mw?.currentMode ?? -1)
                    + " isBoardMode=" + isBoardMode);
            }
            catch (Exception ex) { R("WBTG", false, Trim(ex.Message)); }

            // --- 9. 保存设置后，启动缓存必须跟着更新（2026-11-18）---
            //
            // 为什么必须有这条：LoadSettings 读的是 App.CachedSettingsJson ?? 磁盘，
            // 那份缓存是启动时抓的一次快照。只要保存时不更新它，
            // 之后任何一次 LoadSettings 都会把刚写的值覆盖回旧快照。
            //
            // 班测实证的后果是 OOBE 反复重弹：OnOobeCompleted 先把 HasShownOobe
            // 写成 true 并保存，紧接着 LoadSettings(false) 读到旧快照（false），
            // 内存被打回 false —— 磁盘上是 true，行为却是「没走过引导」。
            //
            // 这条断言做的是**反向对照**：改一个值 → 保存 → 再从缓存读回来。
            // 如果缓存没更新，读回来的还是旧值，断言必须 FAIL。
            try
            {
                var s = SettingsManager.Settings;
                bool original = s.Startup.HasShownOobe;
                bool probe = !original;

                s.Startup.HasShownOobe = probe;
                SettingsManager.SaveSettingsToFile();

                // 直接读缓存，模拟下一次 LoadSettings 会拿到什么。
                string cached = App.CachedSettingsJson;
                bool cacheUpdated = false;
                if (!string.IsNullOrEmpty(cached))
                {
                    var parsed = JsonConvert.DeserializeObject<Settings>(cached);
                    cacheUpdated = parsed?.Startup != null && parsed.Startup.HasShownOobe == probe;
                }

                R("OOBECACHE-1 保存后启动缓存同步更新", cacheUpdated,
                    cacheUpdated
                        ? "缓存已跟着变成 " + probe
                        : "!! 缓存还是旧值——下次 LoadSettings 会覆盖掉刚写的设置");

                // 顺便验一次磁盘：缓存和磁盘必须是同一个值。
                string diskPath = App.RootPath + SettingsManager.SettingsFileName;
                bool diskMatches = false;
                try
                {
                    if (File.Exists(diskPath))
                    {
                        var fromDisk = JsonConvert.DeserializeObject<Settings>(
                            File.ReadAllText(diskPath));
                        diskMatches = fromDisk?.Startup != null && fromDisk.Startup.HasShownOobe == probe;
                    }
                }
                catch (Exception ex) { Log("  [错] 读盘校验失败: " + Trim(ex.Message)); }

                R("OOBECACHE-2 磁盘内容与缓存一致", diskMatches,
                    diskMatches ? "盘上也是 " + probe : "!! 盘上和缓存不一致");

                // 无论断言结果如何，都必须把原值写回去。
                // 这条探针会真改用户的 hasShownOobe —— 不复原就是「测试污染现场」。
                s.Startup.HasShownOobe = original;
                SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex) { R("OOBECACHE", false, Trim(ex.Message)); }

            Log("=== 冒烟自测结束: PASS=" + pass + " FAIL=" + fail + " ===");
        }

// ===== LTSC 临时探针 =====
        /// <summary>
        /// PPT 全链路真机测试：起 PowerPoint、开演示文稿、进放映、翻页、退出、看能否正常关闭。
        /// 验证 L-3（订阅者 RCW 归还）与 L-8（轮询 RCW 归还）——
        /// 这两条只有真的开关几次演示文稿才能看出引用计数对不对，
        /// 表现是「PowerPoint 进程能不能退出去」。
        /// </summary>
        public static void PptEndToEnd(string pptx)
        {
            Log("=== PPT 三组对照：定位 PowerPoint 退不出的真因 ===");
            int pass = 0, fail = 0;
            void R(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; Log("  [PASS] " + name + (detail == "" ? "" : " | " + detail)); }
                else { fail++; Log("  [FAIL] " + name + (detail == "" ? "" : " | " + detail)); }
            }
            int PptN() { try { return System.Diagnostics.Process.GetProcessesByName("POWERPNT").Length; } catch { return -1; } }

            bool RunRound(string tag, bool useBridge, bool useEvents, bool forceRelease)
            {
                object starter = null;
                object bridge = null;
                try
                {
                    starter = Activator.CreateInstance(Type.GetTypeFromProgID("PowerPoint.Application"));
                    starter.GetType().InvokeMember("Visible", BindingFlags.SetProperty, null, starter, new object[] { -1 });
                    object presCol = starter.GetType().InvokeMember("Presentations", BindingFlags.GetProperty, null, starter, null);
                    object opened = presCol.GetType().InvokeMember("Open", BindingFlags.InvokeMethod, null, presCol, new object[] { pptx, -1, 0, 0 });
                    Marshal.ReleaseComObject(opened);
                    Marshal.ReleaseComObject(presCol);

                    if (useBridge)
                    {
                        typeof(Ink_Canvas.Helpers.PptBridge).GetField("DisableComEventsForProbe",
                            BindingFlags.NonPublic | BindingFlags.Static)?.SetValue(null, !useEvents);
                        var bt = typeof(Ink_Canvas.Helpers.PptBridge);
                        bridge = Activator.CreateInstance(bt, true);
                        bt.GetMethod("StartMonitoring").Invoke(bridge, null);
                        PumpWait(6000);
                        for (int i = 0; i < 5; i++) { try { bt.GetMethod("ReloadConnection").Invoke(bridge, null); } catch { } PumpWait(400); }
                        PumpWait(1500);
                        try { bt.GetMethod("StopMonitoring").Invoke(bridge, new object[] { false }); } catch { }
                        try { bt.GetMethod("Dispose").Invoke(bridge, null); } catch { }
                        // 强制把残留 COM 引用打到 0
                        if (forceRelease)
                        {
                            try { bt.GetMethod("ProbeForceFullRelease", BindingFlags.NonPublic | BindingFlags.Instance)?.Invoke(bridge, null); } catch { }
                        }
                        bridge = null;
                    }
                    else
                    {
                        PumpWait(6000 + 5 * 400);
                    }

                    starter.GetType().InvokeMember("Quit", BindingFlags.InvokeMethod, null, starter, null);
                    Marshal.ReleaseComObject(starter);
                    starter = null;

                    for (int w = 0; w < 30; w++)
                    {
                        PumpWait(500);
                        if (PptN() == 0) { Log("  [" + tag + "] PowerPoint 在 " + ((w + 1) * 500) + "ms 内退出"); return true; }
                    }
                    Log("  [" + tag + "] PowerPoint 15 秒后仍在");
                    return false;
                }
                catch (Exception ex) { Log("  [" + tag + "] 异常: " + Trim(ex.Message)); return false; }
                finally
                {
                    if (bridge != null) { try { ((IDisposable)bridge).Dispose(); } catch { } }
                    if (starter != null) { try { Marshal.ReleaseComObject(starter); } catch { } }
                    try { foreach (var pr in System.Diagnostics.Process.GetProcessesByName("POWERPNT")) { try { pr.Kill(); pr.WaitForExit(3000); } catch { } } } catch { }
                    PumpWait(2000);
                }
            }

            bool a = RunRound("A 不碰Bridge", false, false, false);
            bool b = RunRound("B Bridge+事件", true, true, false);
            bool c = RunRound("C Bridge无事件", true, false, false);
            bool d = RunRound("D Bridge+强制FinalRelease", true, true, true);

            Log("");
            Log("  A 不碰 PptBridge          : " + (a ? "能退出" : "退不出"));
            Log("  B PptBridge（挂 COM 事件）: " + (b ? "能退出" : "退不出"));
            Log("  C PptBridge（不挂事件）   : " + (c ? "能退出" : "退不出"));
            Log("  D Bridge + 强制FinalRelease: " + (d ? "能退出" : "退不出"));
            Log("");
            R("对照组 A 干净", a, a ? "" : "对照组本身退不掉 → 测试写法问题");
            R("B 复现问题", !b, b ? "没复现，说明与 PptBridge 事件无关" : "复现");
            R("C 修好（关掉事件就能退出）", c, c ? "→ 真因确认：COM 事件连接点" : "→ 关事件也不行，另有原因");
            R("结论：强制 FinalRelease 能修好", a && !b && d,
                (a && !b && d) ? "→ 真因确认：PptBridge 残留 RCW 未还清，且 Dispose 没兜到底" : "仍未确认");

            Log("=== 三组对照结束: PASS=" + pass + " FAIL=" + fail + " ===");
        }



        // ===== 触摸滚动诊断 =====
        // 目的：不猜。把每个设置页真实构造出来，遍历**可视树**找 ScrollViewer，
        // 打印它的 PanningMode、CanContentScroll、ExtentHeight、ViewportHeight，
        // 以及 Page 自身的 IsManipulationEnabled / 是否被上层接管触摸。


        // ===== OOBE 预设窗口自检 =====
        public static void CheckOobePresetWindow()
        {
            Log("=== OOBE 预设窗口自检 ===");
            try
            {
                var t = typeof(LtscProbe).Assembly.GetType("Ink_Canvas.Windows.OobePresetWindow");
                if (t == null) { Log("  [FAIL] 找不到 OobePresetWindow"); return; }

                var win = Activator.CreateInstance(t);
                var fe = win as FrameworkElement;
                if (fe == null) { Log("  [FAIL] 非 FrameworkElement"); return; }

                fe.Measure(new Size(680, 560));
                fe.Arrange(new Rect(0, 0, 680, 560));
                fe.UpdateLayout();
                Log("  [PASS] 窗口构造成功");

                var flags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public;

                int found = 0;
                foreach (var n in new[] { "CardStandard", "CardTeacher", "CardLite" })
                {
                    if (t.GetField(n, flags)?.GetValue(win) != null) found++;
                    else Log("  [FAIL] 缺卡片 " + n);
                }
                Log("  [" + (found == 3 ? "PASS" : "FAIL") + "] 三张卡片: " + found + "/3");

                int icons = 0;
                foreach (var n in new[] { "IconStandard", "IconTeacher", "IconLite" })
                {
                    if (t.GetField(n, flags)?.GetValue(win) != null) icons++;
                    else Log("  [FAIL] 缺图标 " + n);
                }
                Log("  [" + (icons == 3 ? "PASS" : "FAIL") + "] 三个图标: " + icons + "/3");

                int frames = 0;
                foreach (var n in new[] { "CardStandardFrame", "CardTeacherFrame", "CardLiteFrame" })
                {
                    if (t.GetField(n, flags)?.GetValue(win) != null) frames++;
                    else Log("  [FAIL] 缺选中框 " + n);
                }
                Log("  [" + (frames == 3 ? "PASS" : "FAIL") + "] 三个选中框: " + frames + "/3");

                var selectMethod = t.GetMethod("SelectPreset", flags);
                var kindType = t.GetNestedType("PresetKind", BindingFlags.NonPublic | BindingFlags.Public);
                if (selectMethod == null || kindType == null)
                {
                    Log("  [FAIL] 找不到 SelectPreset 或 PresetKind");
                }
                else
                {
                    foreach (var name in new[] { "Standard", "Teacher", "Lite" })
                    {
                        try
                        {
                            selectMethod.Invoke(win, new object[] { Enum.Parse(kindType, name) });
                            Log("  [PASS] SelectPreset(" + name + ") 未抛异常");
                        }
                        catch (Exception ex)
                        {
                            var inner = ex is TargetInvocationException tie ? tie.InnerException : ex;
                            Log("  [FAIL] SelectPreset(" + name + ") 抛: " + Trim(inner.Message));
                        }
                    }

                    try
                    {
                        selectMethod.Invoke(win, new object[] { Enum.Parse(kindType, "Lite") });
                        var frame = t.GetField("CardLiteFrame", flags)?.GetValue(win) as Border;
                        bool ok = frame != null && frame.BorderBrush != null && frame.BorderBrush != Brushes.Transparent;
                        Log("  [" + (ok ? "PASS" : "FAIL") + "] 选中后边框变色: " + (frame?.BorderBrush?.ToString() ?? "null"));
                    }
                    catch (Exception ex)
                    {
                        Log("  [FAIL] 检查边框: " + Trim(ex.Message));
                    }
                }

                try { (win as Window)?.Close(); } catch { }
            }
            catch (Exception ex)
            {
                Log("  [FAIL] 自检异常: " + Trim(ex.Message));
            }
            Log("=== OOBE 预设窗口自检结束 ===");
        }

        public static void DumpScrollDiagnostics()
        {
            Log("=== 触摸滚动诊断开始 ===");
            var asm = typeof(LtscProbe).Assembly;
            var winType = asm.GetType("Ink_Canvas.Windows.SettingsViews.SettingsWindow");
            if (winType == null) { Log("找不到 SettingsWindow"); return; }

            var field = winType.GetField("_pageTypes", BindingFlags.NonPublic | BindingFlags.Instance);
            object win = Activator.CreateInstance(winType);
            var pageTypes = field?.GetValue(win) as Dictionary<string, Type>;
            if (pageTypes == null) { Log("_pageTypes 为空"); return; }

            // 系统触摸/笔能力 —— 触摸滚动（PanningMode）依赖这些
            try
            {
                var devs = System.Windows.Input.Tablet.TabletDevices;
                Log("  [设备] Tablet 设备数 = " + (devs != null ? devs.Count : -1));
                if (devs != null)
                {
                    foreach (System.Windows.Input.TabletDevice td in devs)
                    {
                        Log("    " + td.Name + " 类型=" + td.Type + " 笔设备=" + td.StylusDevices.Count);
                    }
                }
                // 触摸滚动依赖 Tablet 设备枚举。若这里为 0，说明系统没有把触摸设备
                // 暴露给 WPF，PanningMode 再对也不会生效。
                Log("  [设备] TabletDevices.Count = " + System.Windows.Input.Tablet.TabletDevices.Count);
            }
            catch (Exception ex) { Log("  [设备] 探测异常: " + Trim(ex.Message)); }

            foreach (var kv in pageTypes)
            {
                try
                {
                    var page = Activator.CreateInstance(kv.Value) as FrameworkElement;
                    if (page == null) continue;

                    page.Measure(new Size(1000, 800));
                    page.Arrange(new Rect(0, 0, 1000, 800));
                    page.UpdateLayout();

                    var scrolls = new List<ScrollViewer>();
                    CollectScrollViewers(page, scrolls);

                    Log(string.Format("[滚动] {0}: ScrollViewer={1} IsManipulationEnabled={2}",
                        kv.Key, scrolls.Count, page.IsManipulationEnabled));

                    int i = 0;
                    foreach (var sv in scrolls)
                    {
                        i++;
                        Log(string.Format("    #{0} PanningMode={1} 自身Manipulation={2} CanContentScroll={3} Extent={4:F0} Viewport={5:F0} 可滚动={6}",
                            i,
                            sv.PanningMode,
                            sv.IsManipulationEnabled,
                            sv.CanContentScroll,
                            sv.ExtentHeight,
                            sv.ViewportHeight,
                            sv.ExtentHeight > sv.ViewportHeight));
                    }
                }
                catch (Exception ex)
                {
                    Log("  [" + kv.Key + "] 异常: " + Trim(ex.Message));
                }
            }

            Log("=== 触摸滚动诊断结束 ===");
        }

        /// <summary>遍历**可视树**（不是逻辑树 —— 模板生成的 ScrollViewer 只在可视树里）。</summary>
        private static void CollectScrollViewers(DependencyObject root, List<ScrollViewer> output)
        {
            if (root == null) return;
            if (root is ScrollViewer sv) output.Add(sv);

            int n = System.Windows.Media.VisualTreeHelper.GetChildrenCount(root);
            for (int i = 0; i < n; i++)
            {
                CollectScrollViewers(System.Windows.Media.VisualTreeHelper.GetChild(root, i), output);
            }
        }

        public static void DumpSettingsPages()
        {
            Log("=== 探针开始 ===");
            var asm = typeof(LtscProbe).Assembly;
            var winType = asm.GetType("Ink_Canvas.Windows.SettingsViews.SettingsWindow");
            if (winType == null) { Log("找不到 SettingsWindow 类型"); return; }

            var field = winType.GetField("_pageTypes", BindingFlags.NonPublic | BindingFlags.Instance);
            if (field == null) { Log("找不到 _pageTypes 字段"); return; }

            object win = null;
            Dictionary<string, Type> pageTypes = null;
            try
            {
                win = Activator.CreateInstance(winType);
                pageTypes = field.GetValue(win) as Dictionary<string, Type>;
            }
            catch (Exception ex)
            {
                Log("创建 SettingsWindow 失败: " + ex.GetType().Name + ": " + Trim(ex.Message));
                return;
            }

            if (pageTypes == null) { Log("_pageTypes 为空"); return; }
            Log("注册页数: " + pageTypes.Count);

            int ok = 0;
            int fail = 0;
            var failures = new List<string>();
            var stats = new List<string>();

            foreach (var kv in pageTypes)
            {
                string tag = kv.Key;
                Type pageType = kv.Value;
                try
                {
                    var page = Activator.CreateInstance(pageType);
                    var fe = page as FrameworkElement;
                    if (fe != null)
                    {
                        var loaded = pageType.GetMethod("Page_Loaded", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (loaded != null)
                        {
                            try { loaded.Invoke(page, new object[] { fe, new RoutedEventArgs() }); }
                            catch (TargetInvocationException tie) { throw tie.InnerException ?? tie; }
                        }

                        // 不 Show 窗口：Show 会建句柄并把页面挂进真实窗口生命周期，
                        // 实测会让 ToolbarPage 之后的页面卡住。Measure/Arrange 同样触发
                        // 模板展开与绑定求值，且不影响程序状态。
                        fe.Measure(new Size(1000, 800));
                        fe.Arrange(new Rect(0, 0, 1000, 800));
                        fe.UpdateLayout();

                        int toggles = CountToggles(fe);
                        int combos = CountOf<ComboBox>(fe);
                        int sliders = CountOf<Slider>(fe);
                        int buttons = CountOf<Button>(fe);
                        int texts = CountOf<TextBlock>(fe);
                        stats.Add(tag + " toggles=" + toggles + " combos=" + combos +
                                  " sliders=" + sliders + " buttons=" + buttons + " texts=" + texts);
                    }
                    ok++;
                }
                catch (TargetInvocationException tie)
                {
                    fail++;
                    var inner = tie.InnerException ?? tie;
                    failures.Add(tag + " | " + inner.GetType().Name + " | " + Trim(inner.Message));
                }
                catch (Exception ex)
                {
                    fail++;
                    failures.Add(tag + " | " + ex.GetType().Name + " | " + Trim(ex.Message));
                }
            }

            Log("构造成功: " + ok + " / 失败: " + fail + " / 总计: " + (ok + fail));
            foreach (var s in stats) Log("  [页] " + s);
            foreach (var s in failures) Log("  [失败] " + s);

            var w = win as Window;
            if (w != null) { try { w.Close(); } catch { } }

            Log("=== 探针结束 ===");
        }

        private static int CountToggles(DependencyObject root)
        {
            int n = 0;
            Walk(root, delegate (DependencyObject o)
            {
                if (o != null && o.GetType().Name == "ToggleSwitch") n++;
            });
            return n;
        }

        private static int CountOf<T>(DependencyObject root) where T : class
        {
            int n = 0;
            Walk(root, delegate (DependencyObject o)
            {
                if (o is T) n++;
            });
            return n;
        }

        private static void Walk(DependencyObject root, Action<DependencyObject> visit)
        {
            if (root == null) return;
            visit(root);
            int count = 0;
            try { count = VisualTreeHelper.GetChildrenCount(root); }
            catch { return; }
            for (int i = 0; i < count; i++)
            {
                DependencyObject child = null;
                try { child = VisualTreeHelper.GetChild(root, i); }
                catch { continue; }
                Walk(child, visit);
            }
        }

        /// <summary>
        /// 对照实验：ItemsRepeater 的泄漏来自「它自己」，还是「Measure 这个动作」。
        ///
        /// 上一轮漏了最关键的一格：ItemsRepeater 只构造不 Measure 从没单独测过。
        /// 「Border 20 个只死 1 个」说明 Measure 本身不至于漏，
        /// 但没排除「Panel 子类 + Measure」这个组合。
        ///
        /// 每组都统计构造和 Measure 的成功次数。上一版栽在空 catch 上：
        /// 异常被吞、对象没跑到那一步，却把「0 存活」当成修复生效。
        /// 成功次数对不上就整组作废，不许拿来支撑任何结论。
        /// </summary>
        public static void ProbeRepeaterVsItemsControl()
        {
            Log("=== 对照：ItemsRepeater vs 原生 ItemsControl ===");
            var repType = ResolveType("iNKORE.UI.WPF.Modern.Controls.ItemsRepeater");
            if (repType == null) { Log("  [FAIL] 找不到 ItemsRepeater 类型"); return; }
            Log("  来源程序集=" + repType.Assembly.GetName().Name);

            // A/B 是分界：同一批构造条件，唯一差别是 Measure 那一句。
            RunLeakGroup("A. ItemsRepeater 只构造不 Measure", 30,
                () => Activator.CreateInstance(repType), false);

            RunLeakGroup("B. ItemsRepeater 构造 + Measure", 30,
                () => Activator.CreateInstance(repType), true);

            // C/D 用 WPF 原生 ItemsControl 做同样两格。
            // 它要是也漏，那漏的不是 ItemsRepeater，是「Panel 被单独 Measure」这件事，
            // 换控件白换，路线 A 直接作废。
            RunLeakGroup("C. 原生 ItemsControl 只构造", 30,
                () => new System.Windows.Controls.ItemsControl(), false);

            RunLeakGroup("D. 原生 ItemsControl 构造 + Measure", 30,
                () => new System.Windows.Controls.ItemsControl(), true);

            RunLeakGroup("E. 原生 ItemsControl 带 3 项 + Measure", 30,
                () =>
                {
                    var ic = new System.Windows.Controls.ItemsControl();
                    ic.Items.Add(new Border());
                    ic.Items.Add(new Border());
                    ic.Items.Add(new Border());
                    return ic;
                }, true);

            // F 对照宿主：真模板里 ItemsRepeater 装在 Expander.Content 里。
            // 单独一个 Expander 不漏的话，说明问题不在「宿主」这一层。
            RunLeakGroup("F. 原生 Expander 构造 + Measure", 30,
                () => new System.Windows.Controls.Expander(), true);
        }

        /// <summary>
        /// 一组泄漏测量的标准流程：造 N 个 → 可选 Measure → 强制全量 GC 三次 → 数存活。
        /// 成功/失败次数必须打出来，失败数不为 0 时这组数据作废。
        /// </summary>
        private static void RunLeakGroup(string name, int n, Func<object> factory, bool doMeasure)
        {
            var refs = new List<WeakReference>();
            int madeOk = 0, madeFail = 0, measureOk = 0, measureFail = 0;
            string firstErr = null;

            for (int k = 0; k < n; k++)
            {
                object o = null;
                try { o = factory(); madeOk++; }
                catch (Exception ex)
                {
                    madeFail++;
                    if (firstErr == null) firstErr = "构造 " + ex.GetType().Name + ": " + ex.Message;
                }

                if (o != null && doMeasure)
                {
                    try
                    {
                        var fe = o as FrameworkElement;
                        if (fe == null) throw new InvalidOperationException("不是 FrameworkElement");
                        fe.Measure(new Size(1000, 800));
                        measureOk++;
                    }
                    catch (Exception ex)
                    {
                        measureFail++;
                        if (firstErr == null) firstErr = "Measure " + ex.GetType().Name + ": " + ex.Message;
                    }
                }

                refs.Add(new WeakReference(o));
                o = null;
                YieldUi();
            }

            for (int i = 0; i < 3; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                YieldUi();
            }

            int alive = refs.Count(r => r.IsAlive);
            var sb = new System.Text.StringBuilder();
            sb.Append("  [").Append(name).Append("] 造成功=").Append(madeOk).Append("/").Append(n);
            if (madeFail > 0) sb.Append(" 构造失败=").Append(madeFail);
            if (doMeasure) sb.Append(" Measure成功=").Append(measureOk).Append(" 失败=").Append(measureFail);
            sb.Append(" → 存活=").Append(alive).Append("/").Append(n);
            Log(sb.ToString());

            if (firstErr != null) Log("      首个异常: " + Trim(firstErr));

            // 判据：这组数据能不能用。
            bool voidGroup = madeFail > 0 || (doMeasure && measureOk != madeOk);
            if (voidGroup) Log("      [作废] 有对象没跑到预期步骤，本组数据不能支撑结论");
        }

        /// <summary>
        /// 真机路径验证：反复开关设置窗口，看内存到底涨不涨。
        ///
        /// 为什么需要这一条：前面所有结论都是在探针里直接 Measure 页面对象得出的，
        /// 而真实的开窗路径是 BtnSettings_Click → new SettingsWindow() → Show() →
        /// Loaded 里预加载 24 个页面 → 关闭时 Closed 里 _pages.Clear()。
        /// 走的是完全不同的路径，探针的结论不能直接搬过来。
        ///
        /// 关窗用 Close() 而不是只丢引用——Close 会触发 Closed，_pages.Clear() 才跑得到。
        /// 每次都把队列抽到 SystemIdle，否则测量本身就不可信（见 ProbeIdleDrain）。
        /// </summary>
        public static void ProbeSettingsWindowCycle(int cycles)
        {
            Log("=== 真机路径：开关设置窗口 " + cycles + " 次 ===");
            var mw = Application.Current?.MainWindow;
            if (mw == null) { Log("  [FAIL] 主窗口为空"); return; }

            var mwType = mw.GetType();
            var settingsField = mwType.GetField("_settingsWindow",
                BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance);
            if (settingsField == null) Log("  [提示] 找不到 _settingsWindow 字段，靠窗口列表兜底");

            DrainToIdle(); DrainToIdle();
            var gc0 = GC.GetTotalMemory(true);
            int h0 = GetHandles();
            Log("  基线: 内存=" + (gc0 / 1048576) + "MB 句柄=" + h0);

            var refs = new List<(int Cycle, WeakReference Ref)>();
            int opened = 0, failed = 0;

            for (int c = 1; c <= cycles; c++)
            {
                object win = null;
                try
                {
                    var winType = ResolveType("Ink_Canvas.Windows.SettingsViews.SettingsWindow");
                    if (winType == null) { Log("  [FAIL] 找不到 SettingsWindow 类型"); return; }
                    win = Activator.CreateInstance(winType);
                    var w = win as Window;
                    if (w == null) throw new InvalidOperationException("不是 Window");
                    w.Show();

                    // 让它真的走完 Loaded + 预加载。
                    // PreloadAllPagesAsync 是异步的，抽几轮队列再等一下。
                    for (int t = 0; t < 6; t++) { DrainToIdle(); PumpWaitNoDeadline(180); }
                    DrainToIdle();
                    opened++;
                }
                catch (Exception ex)
                {
                    failed++;
                    Log("  第 " + c + " 次开窗失败: " + ex.GetType().Name + ": " + Trim(ex.Message));
                }

                if (win != null)
                {
                    refs.Add((c, new WeakReference(win)));
                    try { ((Window)win).Close(); } catch { }
                    win = null;
                }

                // Close 之后队列里会堆一批收尾操作，必须抽干再看。
                for (int t = 0; t < 4; t++) { DrainToIdle(); PumpWaitNoDeadline(120); }
                DrainToIdle();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                DrainToIdle();

                Log("  第 " + c + " 次: 内存=" + (GC.GetTotalMemory(true) / 1048576)
                    + "MB 句柄=" + GetHandles());
            }

            DrainToIdle();
            for (int i = 0; i < 3; i++)
            {
                DrainToIdle();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                DrainToIdle();
            }

            var gc1 = GC.GetTotalMemory(true);
            int h1 = GetHandles();
            Log("  结果: 开成功=" + opened + " 失败=" + failed);
            Log("  内存: " + (gc0 / 1048576) + "MB → " + (gc1 / 1048576)
                + "MB  (Δ" + ((gc1 - gc0) / 1048576) + "MB，平均每次 "
                + ((gc1 - gc0) / 1048576.0 / Math.Max(1, opened)).ToString("F2") + "MB)");
            Log("  句柄: " + h0 + " → " + h1 + "  (Δ" + (h1 - h0) + ")");
            Log("  窗口对象存活: " + refs.Count(x => x.Ref.IsAlive) + "/" + refs.Count);
            var alive = refs.Where(x => x.Ref.IsAlive).Select(x => x.Cycle).ToList();
            if (alive.Count > 0) Log("  存活的是第几次: " + string.Join(",", alive));
        }

        /// <summary>
        /// 只等 ms 毫秒并抽干队列，不设看门狗超时。
        /// 和 PumpWait 的区别是泵的是 SystemIdle 不是 Background。
        /// </summary>
        private static void PumpWaitNoDeadline(int ms)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ms)
            {
                DrainToIdle();
                System.Threading.Thread.Sleep(5);
            }
        }

        /// <summary>
        /// 把 Dispatcher 队列抽干到 SystemIdle —— 比 Background 更低的优先级。
        ///
        /// 为什么必须单独有这么一个方法：
        /// YieldUi 的恢复回调挂在 Background，所以只跑到 Background 就返回了。
        /// 而 ItemsRepeater 的 ViewportManagerDownLevel.RegisterCacheBuildWork()
        /// 是用 DispatcherPriority.ContextIdle 投的，ContextIdle 比 Background 低，
        /// 于是那个 Action 永远轮不到执行，而它捕获了 owner（源码里写着
        /// "We capture 'owner' (a strong reference on ItemsRepeater)"）。
        /// 只要队列不抽干，对象就一直被钉着 —— 这足以伪造出「泄漏」。
        /// </summary>
        private static void DrainToIdle()
        {
            var frame = new System.Windows.Threading.DispatcherFrame();
            System.Windows.Threading.Dispatcher.CurrentDispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.SystemIdle,
                new Action(() => frame.Continue = false));
            System.Windows.Threading.Dispatcher.PushFrame(frame);
        }

        /// <summary>
        /// 回答「为什么会涨、GC 为什么不回收」。
        ///
        /// .NET 的 GC 是追踪式（tracing）回收：只回收「不可达」的对象。
        /// 只要还有任何一条强引用链能走到它，GC 就正确地不回收——那不是泄漏，是「还活着」。
        ///
        /// 这一组要证的三件事：
        ///   1. 那批对象在整轮里确实被反复 GC 过，是因为可达才没死，不是 GC 偷懒
        ///   2. 它们是可达的，而那条引用链就是 Dispatcher 队列里没执行的 DispatcherOperation
        ///   3. 队列一跑完，引用立刻断，内存立刻回得来 —— 所以它从头到尾都是可回收的
        /// </summary>
        public static void ProbeGcReclaim()
        {
            Log("=== GC 回收机制：到底为什么涨、为什么没回收 ===");
            var repType = ResolveType("iNKORE.UI.WPF.Modern.Controls.ItemsRepeater");
            if (repType == null) { Log("  [FAIL] 找不到 ItemsRepeater"); return; }

            DrainToIdle(); DrainToIdle();
            long baseMem = GC.GetTotalMemory(true);
            int gen0a = GC.CollectionCount(0), gen1a = GC.CollectionCount(1), gen2a = GC.CollectionCount(2);
            Log("  基线: 驻留=" + (baseMem / 1048576) + "MB 代计数=" + gen0a + "/" + gen1a + "/" + gen2a);
            Log("  基线时 Dispatcher 队列长度=" + QueueDepth());

            // ── 第 1 步：造 30 个，只抽到 Background（复现「泄漏」条件）────────
            var refs = new List<WeakReference>();
            int madeOk = 0;
            for (int k = 0; k < 30; k++)
            {
                try
                {
                    var o = Activator.CreateInstance(repType);
                    (o as FrameworkElement)?.Measure(new Size(1000, 800));
                    refs.Add(new WeakReference(o));
                    madeOk++;
                }
                catch { refs.Add(new WeakReference(null)); }
                YieldBackgroundOnly();
            }
            Log("  造成功=" + madeOk + "/30  Dispatcher 队列长度=" + QueueDepth());

            // 三次强制全量回收。如果对象够不到，这里就该全死。
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                YieldBackgroundOnly();
            }
            long memAfterGc = GC.GetTotalMemory(true);
            int alive1 = refs.Count(r => r.IsAlive);
            Log("  三次强制 GC 后: 存活=" + alive1 + "/30 驻留=" + (memAfterGc / 1048576) + "MB"
                + " 比基线多=" + ((memAfterGc - baseMem) / 1048576) + "MB");
            Log("  期间共发生回收: gen0=" + (GC.CollectionCount(0) - gen0a)
                + " gen1=" + (GC.CollectionCount(1) - gen1a)
                + " gen2=" + (GC.CollectionCount(2) - gen2a) + " 次");
            Log("  → GC 确实跑了 " + (GC.CollectionCount(0) - gen0a) + " 次 gen0 回收，对象没死是因为仍然可达，不是因为 GC 没跑");

            // ── 第 2 步：只做一件事——把队列抽干 ────────────────────────
            // 除了这一步，别的都不变。如果内存回来了，就说明那 30 个对象
            // 一直被队列里的 DispatcherOperation 钉着。
            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            long memBeforeDrain = GC.GetTotalMemory(true);
            int aliveBeforeDrain = refs.Count(r => r.IsAlive);
            int qBefore = QueueDepth();

            DrainToIdle();

            GC.Collect(2, GCCollectionMode.Forced, true, true);
            GC.WaitForPendingFinalizers();
            long memAfterDrain = GC.GetTotalMemory(true);
            int alive2 = refs.Count(r => r.IsAlive);

            Log("  抽干前: 存活=" + aliveBeforeDrain + "/30 驻留=" + (memBeforeDrain / 1048576)
                + "MB 队列长度=" + qBefore);
            Log("  抽干后: 存活=" + alive2 + "/30 驻留=" + (memAfterDrain / 1048576)
                + "MB 队列长度=" + QueueDepth());
            Log("  → 只做抽干这一个动作，释放了 " + ((memBeforeDrain - memAfterDrain) / 1048576)
                + "MB，又死了 " + (aliveBeforeDrain - alive2) + " 个对象");

            // ── 判定 ────────────────────────────────────────────────────
            if (alive1 >= 25 && alive2 <= 5)
                Log("  [结论] 不是泄漏。对象在队列跑完前可达（所以 GC 正确地不回收），"
                    + "跑完后立刻回收 —— 这是「暂时占用」，不是「泄漏」");
            else if (alive1 >= 25 && alive2 >= 25)
                Log("  [结论] 抽干后仍然全活 —— 这才是真泄漏，持有者不在 Dispatcher 队列里");
            else
                Log("  [结论] 两组都不活跃，本实验没复现出问题，数据不足以支撑结论");

            // ── 第 3 步：真实路径下会不会累积 ──────────────────────────
            // 上面的条件是探针造的：造完立刻 GC，但队列还没跑。
            // 真实运行时队列每帧都在跑，所以要看的是「稳态」。
            Log("  稳态检查：连续造 5 批，每批之间正常抽干（模拟真实每帧泵消息）");
            long stableBase = GC.GetTotalMemory(true);
            for (int batch = 1; batch <= 5; batch++)
            {
                var tmp = new List<WeakReference>();
                for (int k = 0; k < 30; k++)
                {
                    try
                    {
                        var o = Activator.CreateInstance(repType);
                        (o as FrameworkElement)?.Measure(new Size(1000, 800));
                        tmp.Add(new WeakReference(o));
                    }
                    catch { }
                    DrainToIdle();   // 真实行为：消息循环一直在跑
                }
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                DrainToIdle();
                Log("    第 " + batch + " 批 150 个总投放后: 存活=" + tmp.Count(r => r.IsAlive)
                    + "/30 驻留=" + (GC.GetTotalMemory(true) / 1048576) + "MB");
            }
            long stableEnd = GC.GetTotalMemory(true);
            Log("  → 稳态 5 批共 150 个，驻留 " + (stableBase / 1048576) + "MB → "
                + (stableEnd / 1048576) + "MB (Δ" + ((stableEnd - stableBase) / 1048576) + "MB)");
        }

        /// <summary>
        /// 证明「一个队列操作能钉住一整页」—— 这是 33MB/轮 和 0MB 之间的差额所在。
        ///
        /// 光造 30 个 ItemsRepeater 只占 0MB（它们是没有子树的空壳）。
        /// 压力测试里每轮 33MB，是因为被钉住的是「整棵页面控件树」。
        ///
        /// 验证方法（只靠弱引用，不加任何假设）：
        ///   1. 造一页，找到它里面的 ItemsRepeater
        ///   2. 只保留 ItemsRepeater 的强引用，把页面的引用全部丢掉
        ///   3. GC：如果页面还活着，说明「子控件钉住了父页面」
        ///   4. 抽干队列后再 GC：如果页面死了，说明钉住它的正是那个队列操作
        /// </summary>
        public static void ProbePagePinning()
        {
            Log("=== 一个队列操作能钉住多大一块内存 ===");
            var pageType = ResolveType("Ink_Canvas.Windows.SettingsViews.Pages.AdvancedPage");
            if (pageType == null)
            {
                foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
                {
                    try
                    {
                        pageType = a.GetTypes().FirstOrDefault(t =>
                            t.Name == "AdvancedPage" && typeof(FrameworkElement).IsAssignableFrom(t));
                    }
                    catch { }
                    if (pageType != null) break;
                }
            }
            if (pageType == null) { Log("  [FAIL] 找不到设置页类型"); return; }

            DrainToIdle(); DrainToIdle();
            long baseMem = GC.GetTotalMemory(true);

            // 1) 造一页并布局。
            object page = Activator.CreateInstance(pageType);
            var fe = page as FrameworkElement;
            fe.Measure(new Size(1000, 800));
            fe.Arrange(new Rect(0, 0, 1000, 800));
            fe.UpdateLayout();
            YieldBackgroundOnly();

            // 2) 找它里面的所有 ItemsRepeater，只留一个强引用。
            var repType = ResolveType("iNKORE.UI.WPF.Modern.Controls.ItemsRepeater");
            var repeaters = new List<object>();
            if (repType != null)
            {
                var stack = new Stack<DependencyObject>();
                stack.Push(fe);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (cur == null) continue;
                    if (repType.IsInstanceOfType(cur)) repeaters.Add(cur);
                    int n = 0;
                    try { n = VisualTreeHelper.GetChildrenCount(cur); } catch { }
                    for (int i = 0; i < n; i++)
                    {
                        try { stack.Push(VisualTreeHelper.GetChild(cur, i)); } catch { }
                    }
                }
            }
            Log("  页面类型=" + pageType.Name + " 树里的 ItemsRepeater=" + repeaters.Count + " 个");

            long memWithPage = GC.GetTotalMemory(true);
            var pageRef = new WeakReference(page);

            // 3) 关键：把页面的强引用全部丢掉，只留 repeater。
            //    如果页面还活着，只能是 repeater 反向往上钉住了它。
            object keepRepeater = repeaters.Count > 0 ? repeaters[0] : null;
            page = null;
            fe = null;
            repeaters = null;

            for (int i = 0; i < 3; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                YieldBackgroundOnly();
            }
            long memPinned = GC.GetTotalMemory(true);
            bool pageAlivePinned = pageRef.IsAlive;
            Log("  只留一个 ItemsRepeater 强引用、丢掉页面引用后: 页面存活=" + pageAlivePinned
                + " 驻留=" + (memPinned / 1048576) + "MB");
            Log("  → 这一页占的内存 ≈ " + ((memWithPage - baseMem) / 1024) + "KB，"
                + "被钉住时仍是 " + ((memPinned - baseMem) / 1024) + "KB");

            // 4) 再往上证一层：连 repeater 也丢掉，抽干队列，页面应该死。
            keepRepeater = null;
            DrainToIdle();
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                DrainToIdle();
            }
            long memFreed = GC.GetTotalMemory(true);
            Log("  再丢掉 ItemsRepeater 并抽干队列: 页面存活=" + pageRef.IsAlive
                + " 驻留=" + (memFreed / 1048576) + "MB");

            // ── 判定 ──
            if (pageAlivePinned && !pageRef.IsAlive)
                Log("  [结论] 证实：一个队列操作（挂在页面内某个 ItemsRepeater 上）"
                    + "足以钉住整页；队列跑完就全放掉");
            else if (pageAlivePinned && pageRef.IsAlive)
                Log("  [结论] 抽干后页面仍活 —— 持有者另有其人，不是队列");
            else
                Log("  [结论] 没复现出钉住现象，数据不足以支撑结论");
        }

        /// <summary>
        /// 把页面拆到独立方法里造，让局部变量槽随方法返回一起出栈。
        ///
        /// 上一版把「造页面 → 找 repeater → 丢引用」全写在同一个方法里，
        /// 结果「抽干后页面仍活」。那不是持有者还在，很可能只是这个方法自己的
        /// 局部变量槽还压着页面引用（JIT 无法证明它之后不会被读）。
        /// NoInlining 保证这个作用域真的结束。
        /// </summary>
        [System.Runtime.CompilerServices.MethodImpl(
            System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        private static (WeakReference Page, int RepeaterCount, long Bytes) BuildPageIsolated(Type pageType)
        {
            var page = Activator.CreateInstance(pageType);
            var fe = page as FrameworkElement;
            if (fe == null) return (new WeakReference(null), 0, 0);

            fe.Measure(new Size(1000, 800));
            fe.Arrange(new Rect(0, 0, 1000, 800));
            fe.UpdateLayout();

            var repType = ResolveType("iNKORE.UI.WPF.Modern.Controls.ItemsRepeater");
            int count = 0;
            if (repType != null)
            {
                var stack = new Stack<DependencyObject>();
                stack.Push(fe);
                while (stack.Count > 0)
                {
                    var cur = stack.Pop();
                    if (cur == null) continue;
                    if (repType.IsInstanceOfType(cur)) count++;
                    int n = 0;
                    try { n = VisualTreeHelper.GetChildrenCount(cur); } catch { }
                    for (int i = 0; i < n; i++)
                    {
                        try { stack.Push(VisualTreeHelper.GetChild(cur, i)); } catch { }
                    }
                }
            }
            return (new WeakReference(page), count, 0);
        }

        /// <summary>
        /// 严格版：页面在独立作用域里造，返回时该作用域的栈槽已失效。
        /// 这时如果页面还活着，才是真的有别人持有它。
        /// </summary>
        public static void ProbePagePinningStrict()
        {
            Log("=== 严格版：页面在独立作用域里造，排除本方法局部变量干扰 ===");
            var pageType = ResolveType("Ink_Canvas.Windows.SettingsViews.Pages.AdvancedPage");
            if (pageType == null) { Log("  [FAIL] 找不到 AdvancedPage"); return; }

            DrainToIdle(); DrainToIdle();
            long mem0 = GC.GetTotalMemory(true);

            var built = BuildPageIsolated(pageType);
            long mem1 = GC.GetTotalMemory(true);
            Log("  造一页: repeater=" + built.RepeaterCount + " 个 占用≈"
                + ((mem1 - mem0) / 1024) + "KB");

            // 作用域已结束。现在做三格对比。
            // 格 1：什么都不做，直接 GC
            for (int i = 0; i < 3; i++)
            {
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                YieldBackgroundOnly();
            }
            bool aliveA = built.Page.IsAlive;
            long memA = GC.GetTotalMemory(true);
            Log("  格1 只抽到 Background 就 GC: 页面存活=" + aliveA
                + " 驻留=" + (memA / 1048576) + "MB (Δ" + ((memA - mem0) / 1024) + "KB)");

            // 格 2：抽到 SystemIdle 再 GC
            DrainToIdle();
            for (int i = 0; i < 3; i++)
            {
                DrainToIdle();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
            }
            DrainToIdle();
            bool aliveB = built.Page.IsAlive;
            long memB = GC.GetTotalMemory(true);
            Log("  格2 抽到 SystemIdle 再 GC: 页面存活=" + aliveB
                + " 驻留=" + (memB / 1048576) + "MB (Δ" + ((memB - mem0) / 1024) + "KB)");

            if (aliveA && !aliveB)
                Log("  [结论] 页面被队列钉住。队列一跑完就回收 —— 可回收的暂时占用，不是泄漏");
            else if (aliveA && aliveB)
                Log("  [结论] 抽干后仍活，页面另有持有者；但要看它吃多少内存 —— "
                    + "单页 " + ((mem1 - mem0) / 1024) + "KB，不是泄漏量级就该另查");
            else
                Log("  [结论] 两格都死了，本场景不复现钉住现象");
        }

        /// <summary>
        /// 读 Dispatcher 队列里挂了多少个待执行操作。
        /// 只是探针诊断用，取不到就返回 -1，不影响任何结论。
        /// </summary>
        private static int QueueDepth()
        {
            try
            {
                var d = System.Windows.Threading.Dispatcher.CurrentDispatcher;
                var dt = typeof(System.Windows.Threading.Dispatcher);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                foreach (var name in new[] { "_queue", "_priorityQueue" })
                {
                    var f = dt.GetField(name, flags);
                    if (f == null) continue;
                    var q = f.GetValue(d);
                    if (q == null) continue;
                    var cnt = q.GetType().GetProperty("Count");
                    if (cnt != null) return Convert.ToInt32(cnt.GetValue(q));
                    var inner = q.GetType().GetField("_items", flags)
                                ?? q.GetType().GetField("_array", flags);
                    if (inner != null)
                    {
                        var arr = inner.GetValue(q) as Array;
                        if (arr != null) return arr.Length;
                    }
                }
            }
            catch { }
            return -1;
        }

        /// <summary>
        /// 决定性对照：ItemsRepeater 的「泄漏」是不是探针没抽干 ContextIdle 队列造成的假象。
        ///
        /// 判据：
        ///   只 Background 抽 → 存活多（复现上一轮结果）
        ///   抽到 SystemIdle  → 存活掉到个位数
        /// 如果后者成立，那之前「ItemsRepeater 自身泄漏」的结论作废，
        /// 设置页内存问题要重新定性，打补丁的方案也得推翻重来。
        /// </summary>
        public static void ProbeIdleDrain()
        {
            Log("=== 决定性对照：ContextIdle 队列没抽干会不会伪造泄漏 ===");
            var repType = ResolveType("iNKORE.UI.WPF.Modern.Controls.ItemsRepeater");
            if (repType == null) { Log("  [FAIL] 找不到 ItemsRepeater"); return; }

            // 先把队列抽干净，避免上一组残留影响这一组。
            DrainToIdle();
            DrainToIdle();

            RunLeakGroupEx("G. ItemsRepeater + Measure，只抽到 Background", 30,
                () => Activator.CreateInstance(repType), true, false);

            RunLeakGroupEx("H. ItemsRepeater + Measure，抽到 SystemIdle", 30,
                () => Activator.CreateInstance(repType), true, true);

            // 反向对照：同一件事在原生 ItemsControl 上应该没差别。
            // 如果它也随抽干程度变化，那说明测量方法整体有问题。
            RunLeakGroupEx("I. 原生 ItemsControl，只抽到 Background", 30,
                () => new System.Windows.Controls.ItemsControl(), true, false);

            RunLeakGroupEx("J. 原生 ItemsControl，抽到 SystemIdle", 30,
                () => new System.Windows.Controls.ItemsControl(), true, true);
        }

        /// <summary>
        /// RunLeakGroup 的加长版：多一个 drainIdle 开关，决定 GC 前后要不要抽到 SystemIdle。
        /// 其它判据（成功次数、作废条件）一致。
        /// </summary>
        private static void RunLeakGroupEx(string name, int n, Func<object> factory, bool doMeasure, bool drainIdle)
        {
            var refs = new List<WeakReference>();
            int madeOk = 0, madeFail = 0, measureOk = 0;
            string firstErr = null;

            for (int k = 0; k < n; k++)
            {
                object o = null;
                try { o = factory(); madeOk++; }
                catch (Exception ex)
                {
                    madeFail++;
                    if (firstErr == null) firstErr = "构造 " + ex.GetType().Name + ": " + ex.Message;
                }

                if (o != null && doMeasure)
                {
                    try
                    {
                        var fe = o as FrameworkElement;
                        if (fe == null) throw new InvalidOperationException("不是 FrameworkElement");
                        fe.Measure(new Size(1000, 800));
                        measureOk++;
                    }
                    catch (Exception ex)
                    {
                        if (firstErr == null) firstErr = "Measure " + ex.GetType().Name + ": " + ex.Message;
                    }
                }

                refs.Add(new WeakReference(o));
                o = null;
                if (drainIdle) DrainToIdle(); else YieldBackgroundOnly();
            }

            // 抽干要在 GC 之前和之后各做一次：
            // 之前是把已入队的操作执行掉（解开强引用），
            // 之后是清掉执行过程中新投递的。
            if (drainIdle) { DrainToIdle(); DrainToIdle(); }

            for (int i = 0; i < 3; i++)
            {
                if (drainIdle) DrainToIdle();
                GC.Collect(2, GCCollectionMode.Forced, true, true);
                GC.WaitForPendingFinalizers();
                if (drainIdle) DrainToIdle(); else YieldBackgroundOnly();
            }

            if (drainIdle) { DrainToIdle(); DrainToIdle(); }

            int alive = refs.Count(r => r.IsAlive);
            var sb = new System.Text.StringBuilder();
            sb.Append("  [").Append(name).Append("] 造成功=").Append(madeOk).Append("/").Append(n);
            if (madeFail > 0) sb.Append(" 构造失败=").Append(madeFail);
            if (doMeasure) sb.Append(" Measure成功=").Append(measureOk);
            sb.Append(" → 存活=").Append(alive).Append("/").Append(n);
            Log(sb.ToString());
            if (firstErr != null) Log("      首个异常: " + Trim(firstErr));
            if (madeFail > 0 || (doMeasure && measureOk != madeOk))
                Log("      [作废] 有对象没跑到预期步骤，本组数据不能支撑结论");
        }

        /// <summary>
        /// 按全名找类型：先本程序集，再扫已加载程序集。
        /// 只扫已加载的，不触发新加载——探针不该改变被测环境。
        /// </summary>
        private static Type ResolveType(string fullName)
        {
            var t = typeof(LtscProbe).Assembly.GetType(fullName);
            if (t != null) return t;
            foreach (var a in AppDomain.CurrentDomain.GetAssemblies())
            {
                try { t = a.GetType(fullName); } catch { }
                if (t != null) return t;
            }
            return null;
        }

        private static string Trim(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            s = s.Replace("\\r", " ").Replace("\\n", " ");
            if (s.Length > 180) s = s.Substring(0, 180) + "...";
            return s;
        }

        private static void Log(string msg)
        {
            try { LogHelper.WriteLogToFile("[探针] " + msg, LogHelper.LogType.Event); }
            catch { }
        }
    }
}
