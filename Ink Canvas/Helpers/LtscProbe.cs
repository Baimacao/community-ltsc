using System;
using System.Collections.Generic;
using System.Reflection;
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

        private static int GetHandles()
        {
            try { int c; GetProcessHandleCount(GetCurrentProcess(), out c); return c; }
            catch { return -1; }
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
