using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

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
        public static void StressSettingsPages(int rounds)
        {
            Log("=== 暴力测试开始 rounds=" + rounds + " ===");

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

            for (int r = 0; r < rounds; r++)
            {
                foreach (var kv in pageTypes)
                {
                    try
                    {
                        var page = Activator.CreateInstance(kv.Value);
                        var fe = page as FrameworkElement;
                        if (fe == null) continue;
                        fe.Measure(new Size(1000, 800));
                        fe.Arrange(new Rect(0, 0, 1000, 800));
                        fe.UpdateLayout();
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        var inner = ex is TargetInvocationException ? ((TargetInvocationException)ex).InnerException ?? ex : ex;
                        if (errs.Count < 10) errs.Add("r" + r + " " + kv.Key + " | " + inner.GetType().Name + " | " + Trim(inner.Message));
                    }
                }
                if ((r + 1) % 3 == 0)
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
                    Log("  第 " + (r + 1) + " 轮: 内存=" + (GC.GetTotalMemory(true) / 1048576) + "MB 句柄=" + GetHandles());
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

            var w = win as Window;
            if (w != null) { try { w.Close(); } catch { } }
            Log("=== 暴力测试结束 ===");
        }

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        [System.Runtime.InteropServices.DllImport("kernel32.dll", SetLastError = true)]
        private static extern int GetProcessHandleCount(IntPtr hProcess, out int count);

        /// <summary>
        /// 泵消息式等待：把控制权交回 Dispatcher，让界面保持响应。
        /// 直接用 Thread.Sleep 堵 UI 线程会触发看门狗「主线程无响应 20 秒强制重启」，
        /// 长测试会断在半路。
        /// </summary>
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
        public static void SmokeTest()
        {
            Log("=== 冒烟自测开始 ===");
            int pass = 0, fail = 0;
            void R(string name, bool ok, string detail = "")
            {
                if (ok) { pass++; Log("  [PASS] " + name + (detail == "" ? "" : " | " + detail)); }
                else { fail++; Log("  [FAIL] " + name + (detail == "" ? "" : " | " + detail)); }
            }

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

            // --- 5. 心跳 Process 是否已释放（C-5）---
            // 带对照组：A 用 using（修复后的写法），B 故意不释放（修复前的写法）。
            // 只看 A 是不是 0 说明不了问题——CLR 的 RCW 终结器本来就会兜底。
            // 真正要证明的是「A 明显好于 B」，以及 A 本身不随次数线性上涨。
            try
            {
                const int N = 300;

                int a0 = GetHandles();
                for (int i = 0; i < N; i++)
                {
                    using (var p = System.Diagnostics.Process.GetCurrentProcess())
                    {
                        var _ = p.TotalProcessorTime;
                    }
                }
                int a1 = GetHandles();   // 不强制 GC：看「当场」是否释放

                int b0 = GetHandles();
                var keep = new List<System.Diagnostics.Process>();
                for (int i = 0; i < N; i++)
                {
                    var p = System.Diagnostics.Process.GetCurrentProcess();
                    var _ = p.TotalProcessorTime;
                    keep.Add(p);         // 故意持有，不让终结器回收
                }
                int b1 = GetHandles();
                keep.Clear();

                GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
                int a2 = GetHandles();

                int growA = a1 - a0;
                int growB = b1 - b0;
                Log("  C-5 明细: using 增长=" + growA + " (基 " + a0 + ") | 不释放增长=" + growB + " (基 " + b0 + ") | GC 后=" + a2);

                R("C-5 using 写法增长 <= 5", growA <= 5, "增长=" + growA);
                R("C-5 using 明显优于不释放", growB < 0 || growA < growB, "A=" + growA + " B=" + growB);
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
