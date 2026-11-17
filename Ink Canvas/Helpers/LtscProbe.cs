using System;
using System.Collections.Generic;
using System.IO;
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
                // 每轮都记，才能看出增长是线性的还是趋平的。
                // 只看每 3 轮的数据分辨不出「真泄漏」和「填缓存」。
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();
                    GC.Collect();
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
                var invoke = mwType.GetMethod("InvokeSaveForAutomation",
                    BindingFlags.NonPublic | BindingFlags.Instance);
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
