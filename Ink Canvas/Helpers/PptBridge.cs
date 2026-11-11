using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;
using System.Threading;
using Timer = System.Timers.Timer;
using System.Timers;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// PowerPoint 联动的唯一实现。取代 PPTManager(2434) + ROTPPTManager(3070) + ComPPTLinkManager(112)。
    ///
    /// <para><b>设计依据（全部为本机实测，不是推测，2026-11-10）：</b></para>
    /// <list type="bullet">
    /// <item>ROT 里 PowerPoint 注册的是<b>演示文稿完整路径</b>（形如 E:\x\deck.pptx），不是
    /// "PowerPoint.Application"。按 ProgID 去 ROT 里找永远找不到。</item>
    /// <item>Win32 窗口枚举<b>不受 UIPI 限制</b>：提权进程能拿到普通权限 PPT 的放映窗口
    /// （实测 hwnd/class=screenClass/pid/rect 全部可读）。COM 则被完全阻断。</item>
    /// <item>放映窗口里<b>没有任何暴露页码的 Win32 文本</b>：标题是
    /// "PowerPoint 幻灯片放映  - 文件名"，7 个子窗口 title 全空。页码只在 COM 里。</item>
    /// </list>
    ///
    /// <para><b>因此本类做三件事，并按可用性逐级降级：</b></para>
    /// <list type="number">
    /// <item>COM：取 Application 用 ROT(按.pptx匹配) → GetActiveObject 两级；拿到就全功能。</item>
    /// <item>Win32：拿不到 COM 时，用窗口枚举定位放映窗口，仍然能"贴上去"（知道在放映、
    /// 窗口在哪、多大），只是没有页码、不能翻页。</item>
    /// <item>两者都失败：报告未连接，不做任何事。</item>
    /// </list>
    ///
    /// <para><b>明确的能力边界：</b>跨权限（CC 提权 + PPT 普通权限）时，
    /// COM 被 UIPI 阻断，本类只能做到第 2 级——粘贴可用、翻页不可用。
    /// 要跨权限拿到页码需要独立进程走 IPC，不在本类范围内。</para>
    /// </summary>
    internal sealed class PptBridge : IPPTLinkManager
    {
        const int MK_E_UNAVAILABLE = unchecked((int)0x800401E3);
        const int CO_E_SERVER_EXEC_FAILURE = unchecked((int)0x80080005);
        const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);

        /// <summary>轮询间隔。事件模式可用时仍然保留轮询作为兜底：COM 事件在异常退出时会静默丢失。</summary>
        static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
        /// <summary>连接检查间隔（进程都不在时不必每次都枚举 ROT）。</summary>
        static readonly TimeSpan ConnectCheckInterval = TimeSpan.FromMilliseconds(2000);

        // ---------- 事件 ----------
        public event Action<object> SlideShowBegin;
        public event Action<object> SlideShowNextSlide;
        public event Action<object> SlideShowEnd;
        public event Action<object> PresentationOpen;
        public event Action<object> PresentationClose;
        public event Action<bool> PPTConnectionChanged;
        public event Action<bool> SlideShowStateChanged;

        // ---------- 属性 ----------
        public bool IsConnected => _comApplication != null;
        public bool IsInSlideShow => _inSlideShow;
        public bool IsSupportWPS { get; set; }
        public bool SkipAnimationsWhenNavigating { get; set; }
        public int SlidesCount { get; private set; }

        /// <summary>COM Application。跨权限时恒为 null——调用方必须判空。</summary>
        public object PPTApplication => _comApplication;

        /// <summary>
        /// 当前放映窗口的屏幕矩形。跨权限时这个仍然有效（来自 Win32），
        /// 这是白板"贴上去"所需要的唯一信息。无效时为 Rectangle.Empty。
        /// </summary>
        public System.Drawing.Rectangle SlideShowBounds { get; private set; } = System.Drawing.Rectangle.Empty;

        /// <summary>上一次连接失败的原因，用于把"为什么连不上"写进日志而不是留个空。</summary>
        public string LastFailureReason { get; private set; }

        // ---------- 状态 ----------
        object _comApplication;
        object _comSlideShowWindow;
        int _currentSlide = -1;
        bool _inSlideShow;
        bool _pendingInShow;
        int _pendingInShowTicks;

        /// <summary>
        /// 状态候选值需要连续稳定这么多轮才被采纳。500ms/轮 × 3 = 1.5s 最大延迟。
        /// 用来吃掉 PPT 退出放映时"窗口已隐藏但进程还在"造成的瞬时抖动。
        /// </summary>
        const int StableTicksRequired = 3;
        volatile bool _running;
        volatile bool _pollBusy;
        /// <summary>LTSC UI-1：轮询进行中时登记「等它收尾再释放 COM」，由 Poll 的 finally 落实。</summary>
        volatile bool _releasePending;
        readonly object _gate = new object();
        /// <summary>保护 _timer 字段的创建与销毁。和 _gate 分开，避免等待轮询时持锁太久。</summary>
        readonly object _timerGate = new object();
        Timer _timer;
        DateTime _lastConnectCheck = DateTime.MinValue;
        DateTime _lastProcessCheck = DateTime.MinValue;
        bool _pptProcessAlive;
        bool _disposed;
        bool _comEventsHooked;



        // ---------- 进程检测 ----------
        static readonly string[] PptProcessNames = { "powerpnt", "wpp", "wps", "wpsoffice" };

        #region 启动 / 停止

        /// <summary>
        /// 详细诊断日志。只有用户在设置里打开「PPT 联动详细日志」时才写入。
        /// 默认关闭：课堂上正常使用不产生额外 IO，出问题时再打开取证。
        /// </summary>
        static void VerboseLog(string message)
        {
            try
            {
                var adv = MainWindow.Settings?.Advanced;
                if (adv == null || !adv.IsLogEnabled || !adv.IsVerbosePptLogEnabled) return;
                LogHelper.WriteLogToFile($"[PPT详细] {message}", LogHelper.LogType.Trace);
            }
            catch { /* 诊断日志绝不能影响主流程 */ }
        }

        public void StartMonitoring()
        {
            // 定时器的创建/销毁必须在同一把锁里完成。
            // 上一版把 new Timer 放在锁外，两个线程同时 Start 会互踩：
            // A 刚 new 出 _timer，B 的 Stop 就把它 Dispose 了，A 再 Start 就抛
            // ObjectDisposedException。
            Timer timer;
            lock (_timerGate)
            {
                if (_disposed) return;
                if (_running) return;

                try { _timer?.Stop(); _timer?.Dispose(); } catch { }

                timer = new Timer(PollInterval.TotalMilliseconds) { AutoReset = true };
                timer.Elapsed += (s, e) => Poll();
                _timer = timer;
                _running = true;
            }

            try { timer.Start(); }
            catch (ObjectDisposedException)
            {
                // 极端时序下仍可能被 Stop 抢先，属于合法的"已被停止"，不是错误
                return;
            }

            LogHelper.WriteLogToFile("PptBridge 监控已启动", LogHelper.LogType.Trace);
        }

        public void StopMonitoring(bool isShutdown = false)
        {
            // LTSC P-2：原来这里是 if (!_running) return;——只要「监控没在跑」就直接返回，
            // 后面的计时器停止、COM 释放、状态归零统统跳过。
            // 而调用方有 Disconnect 之后再 Stop、异常路径里 Stop、Dispose 里 Stop 三种情况，
            // 那时 _running 已经是 false，于是 COM 引用和 _comSlideShowWindow 全都留在原地：
            // PPT 侧引用计数只涨不落，退出 PowerPoint 要等 GC 终结器兜底。
            // 改成：只剪掉「重复停表」这一半，清理那一半永远执行。
            lock (_gate)
            {
                _running = false;
            }

            lock (_timerGate)
            {
                try { _timer?.Stop(); _timer?.Dispose(); } catch { }
                _timer = null;
            }

            // 关键：轮询线程可能已经进入 RefreshSlideShowState 并正握着 COM 引用，
            // 此刻若直接 ReleaseComObject，那一侧会拿到已释放的 RCW → 访问冲突。
            // 轮询函数有 try/finally 保证 _pollBusy 一定复位，所以这里等它是安全的。
            // 上限 3 秒：单轮轮询正常在毫秒级，等不到说明 COM 调用卡死，
            // 那也不能无限期阻塞 UI 线程——宁可带着一次异常退出也不冻结界面。
            // LTSC UI-1：这里原来是 while (_pollBusy && ... < 3000) Thread.Sleep(10);
            // 一个最长 3 秒的忙等，而调用方里就有 UI 线程（InitializePPTManagers 走 Loaded、
            // DisposePPTManagers 走设置页、BtnCheckPPT_Click 走按钮点击）。
            // 点一下「检查 PPT」最坏卡 3 秒，课上一按就僵住。
            //
            // 改成：不在这里等，改为「拿到锁的人负责收尾」。
            // 具体是让 ReleaseCom 先置一个 pending 标志，轮询循环退出时自己释放；
            // 如果轮询此刻没在跑（_pollBusy == false），则当场释放。
            // 两条路都不阻塞调用线程。
            ReleaseComAfterPollDrains();
            // 直接改字段而不走 SetInSlideShow：状态要归零（否则 Dispose 后
            // IsInSlideShow 还停在 true，外部会以为仍在放映），但不能发事件——
            // 调用方此时可能正在 Dispose UI，收到 false 反而会去碰已释放的控件。
            _inSlideShow = false;
            _pendingInShow = false;
            _pendingInShowTicks = 0;
            _currentSlide = -1;
            SlideShowBounds = System.Drawing.Rectangle.Empty;

            if (!isShutdown)
            {
                LogHelper.WriteLogToFile("PptBridge 监控已停止", LogHelper.LogType.Trace);
            }
        }

        /// <summary>
        /// LTSC UI-1：把「等轮询收尾再释放 COM」从调用线程上摘下来。
        ///
        /// 原实现是 while (_pollBusy && ... &lt; 3000) Thread.Sleep(10);，
        /// 调用方里有 UI 线程（Loaded / 设置页 / 「检查 PPT」按钮），
        /// 最坏一次点按卡 3 秒。
        ///
        /// 现在：轮询没在跑就当场释放；在跑就置一个待释放标志，
        /// 由轮询函数自己的 finally 在退出时调用 <see cref="DrainPendingRelease"/>。
        /// 调用方立即返回，永远不阻塞。
        /// </summary>
        private void ReleaseComAfterPollDrains()
        {
            if (!_pollBusy)
            {
                // 没有并发的轮询，可以安全地立刻动 COM
                try
                {
                    UnhookComEvents();
                    ReleaseCom();
                    _inSlideShow = false;
                    _pendingInShow = false;
                    _pendingInShowTicks = 0;
                    _currentSlide = -1;
                    SlideShowBounds = System.Drawing.Rectangle.Empty;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"PptBridge 释放 COM 异常: {ex.Message}", LogHelper.LogType.Warning);
                }
                return;
            }

            // 轮询正在跑：标记由它收尾，本线程直接返回
            _releasePending = true;
            LogHelper.WriteLogToFile("PptBridge 轮询仍在进行，已登记延迟释放", LogHelper.LogType.Trace);
        }

        /// <summary>
        /// LTSC UI-1：由轮询循环在退出时调用，落实 <see cref="ReleaseComAfterPollDrains"/> 登记的释放。
        /// 带锁，防止和 ReloadConnection 等路径并发。
        /// </summary>
        private void DrainPendingRelease()
        {
            if (!_releasePending) return;
            _releasePending = false;

            lock (_gate)
            {
                try
                {
                    UnhookComEvents();
                    ReleaseCom();
                    _inSlideShow = false;
                    _pendingInShow = false;
                    _pendingInShowTicks = 0;
                    _currentSlide = -1;
                    SlideShowBounds = System.Drawing.Rectangle.Empty;
                    LogHelper.WriteLogToFile("PptBridge 轮询已收尾，延迟释放完成", LogHelper.LogType.Trace);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"PptBridge 延迟释放异常: {ex.Message}", LogHelper.LogType.Warning);
                }
            }
        }

        public void ReloadConnection()
        {
            // 和 StopMonitoring 同样的理由：先让正在跑的轮询收尾，再动 COM。
            // LTSC UI-1：同样去掉 3 秒忙等，改成交给轮询线程在退出时释放。
            ReleaseComAfterPollDrains();
            _lastConnectCheck = DateTime.MinValue;
            // 刻意不发 SlideShowStateChanged=false。
            // 上一版在这里复位，下一轮轮询又把状态推回 true，导致
            // MW_PPT 收到 false→true 抖动，工具栏闪一下。
            // 重载期间窗口没变，对外状态本来就不该变。
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            StopMonitoring(true);
        }

        #endregion

        #region 轮询主循环

        void Poll()
        {
            if (_disposed || !_running || _pollBusy) return;
            // 二次确认：StopMonitoring 可能在上一行之后、这里之前把 _running 置false，
            // 那这一轮就不该再碰任何 COM 对象。
            if (!_running) return;
            _pollBusy = true;
            try
            {
                var now = DateTime.UtcNow;

                // 已连接时也要确认 PPT 还活着。
                // 不查的话，PPT 被强杀/崩溃后 _comApplication 仍指向一个死 COM 对象，
                // 下面「未连接才重连」的分支永远不会跑，TryConnect 里那句
                // IsPptProcessAlive 也就永远执行不到——ICC 会一直停在 PPT 模式的黑屏里。
                // IsPptProcessAlive 自带 ConnectCheckInterval 缓存，不会每 500ms 枚举进程。
                if (_comApplication != null && !IsPptProcessAlive())
                {
                    Disconnect("PowerPoint 进程已退出");
                }

                // 连接：只在需要时检查
                if (_comApplication == null &&
                    (now - _lastConnectCheck) >= ConnectCheckInterval)
                {
                    _lastConnectCheck = now;
                    TryConnect();
                }

                // 放映状态：每次轮询都更新，因为它决定白板要不要贴上去
                RefreshSlideShowState();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PptBridge 轮询异常: {ex.Message}", LogHelper.LogType.Warning);
            }
            finally
            {
                // 先清 busy 再落实延迟释放：DrainPendingRelease 里会动 COM，
                // 如果反过来，它执行期间 _pollBusy 仍是 true，别的线程就可能
                // 又登记一次释放，或者误以为还有轮询在跑。
                _pollBusy = false;

                // LTSC UI-1：StopMonitoring / ReloadConnection 在轮询进行中调用时，
                // 只是登记了 _releasePending 就返回了，真正的 COM 释放由这里完成。
                try
                {
                    DrainPendingRelease();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"PptBridge 收尾释放异常: {ex.Message}", LogHelper.LogType.Warning);
                }
            }
        }

        #endregion

        #region 连接

        void TryConnect()
        {
            // 进程都不在就别碰 COM：枚举 ROT 是 20+ 次跨进程调用
            if (!IsPptProcessAlive())
            {
                LastFailureReason = "PowerPoint/WPS 进程未运行";
                if (_comApplication != null) Disconnect("PowerPoint 进程已退出");
                return;
            }

            object app = TryAcquireFromRot();
            if (app == null) app = TryAcquireFromActiveObject();

            if (app == null)
            {
                // 拿不到 COM 不代表不能用：Win32 仍然能看到放映窗口。
                // 失败原因由 TryAcquireFromActiveObject 记录，这里不能覆盖成空。
                if (_comApplication != null) Disconnect("COM 连接丢失");
                if (string.IsNullOrEmpty(LastFailureReason))
                    LastFailureReason = "ROT 与 GetActiveObject 均未取到 PowerPoint";
                return;
            }

            lock (_gate)
            {
                ReleaseCom();
                _comApplication = app;
                LastFailureReason = null;
                HookComEvents(app);
                RefreshSlidesCount();
            }

            LogHelper.WriteLogToFile("PptBridge 已连接 PowerPoint", LogHelper.LogType.Trace);
            VerboseLog($"连接成功: 放映中={IsInSlideShow} 当前页={_currentSlide}");
            try { PPTConnectionChanged?.Invoke(true); } catch { }
        }

        void Disconnect(string reason)
        {
            lock (_gate)
            {
                UnhookComEvents();
                ReleaseCom();
            }
            // 只在没有更具体原因时才用这个泛化的（原因通常由上层先设好）
            if (string.IsNullOrEmpty(LastFailureReason)) LastFailureReason = reason;
            LogHelper.WriteLogToFile($"PptBridge 断开连接：{reason}", LogHelper.LogType.Trace);
            VerboseLog($"断开原因={reason} 断开前: 放映中={IsInSlideShow} 当前页={_currentSlide} 上次失败={LastFailureReason}");
            try { PPTConnectionChanged?.Invoke(false); } catch { }
        }

        bool IsPptProcessAlive()
        {
            var now = DateTime.UtcNow;
            if ((now - _lastProcessCheck) < ConnectCheckInterval) return _pptProcessAlive;
            _lastProcessCheck = now;
            _pptProcessAlive = false;

            foreach (var name in PptProcessNames)
            {
                try
                {
                    var list = Process.GetProcessesByName(name);
                    if (list != null && list.Length > 0)
                    {
                        foreach (var p in list) { try { p.Dispose(); } catch { } }
                        _pptProcessAlive = true;
                        break;
                    }
                }
                catch { }
            }
            return _pptProcessAlive;
        }

        /// <summary>
        /// ROT 取 Application。按演示文稿扩展名匹配——实测 ROT 里注册的是完整文件路径，
        /// 不是 "PowerPoint.Application"。
        /// </summary>
        object TryAcquireFromRot()
        {
            IRunningObjectTable rot = null;
            IBindCtx bindCtx = null;
            IEnumMoniker enumMoniker = null;

            try
            {
                if (GetRunningObjectTable(0, out rot) != 0 || rot == null) return null;
                if (CreateBindCtx(0, out bindCtx) != 0 || bindCtx == null) return null;

                rot.EnumRunning(out enumMoniker);
                if (enumMoniker == null) return null;
                enumMoniker.Reset();

                var monikers = new IMoniker[1];
                IntPtr fetched = IntPtr.Zero;

                while (enumMoniker.Next(1, monikers, fetched) == 0)
                {
                    var moniker = monikers[0];
                    if (moniker == null) continue;

                    // LTSC L-3b：原来这几条路径都在漏引用。
                    //   ① moniker 本身是 ROT 给的 COM 对象，循环里从不 Release；
                    //   ② rot.GetObject 拿到的 comObject，在「成功取到 Application」那条路上
                    //      直接 return，SafeRelease 只写在 app==null 的兜底里——永远走不到；
                    //   ③ TryGetApplicationFrom 若返回的是 comObject 本身，又不能再释放一次。
                    // 每次 500ms 轮询重连都漏一组，PowerPoint 侧引用计数只涨不落，
                    // 表现就是「关掉演示文稿后 PowerPoint 退不出去」。
                    // 这里改成：每个分支都保证 moniker / comObject 恰好释放一次。
                    try
                    {
                        string displayName = null;
                        try { moniker.GetDisplayName(bindCtx, null, out displayName); }
                        catch { }

                        if (!LooksLikePresentation(displayName)) continue;

                        object comObject = null;
                        try { rot.GetObject(moniker, out comObject); }
                        catch { continue; }
                        if (comObject == null) continue;

                        object app = TryGetApplicationFrom(comObject);
                        if (app != null && !ReferenceEquals(app, comObject))
                        {
                            // app 是 comObject 上另取的一层；comObject 这份要还掉
                            SafeRelease(comObject);
                            return app;
                        }
                        if (app != null && ReferenceEquals(app, comObject))
                        {
                            // app 就是 comObject 本身，所有权转交给调用方，不能释放
                            return app;
                        }
                        SafeRelease(comObject);
                    }
                    finally
                    {
                        SafeRelease(moniker);
                    }
                }
                return null;
            }
            catch
            {
                return null;
            }
            finally
            {
                SafeRelease(enumMoniker);
                SafeRelease(bindCtx);
                SafeRelease(rot);
            }
        }

        /// <summary>从 Presentation 或 Application 上取 Application（late-bound，兼容 WPS 类型不匹配）。</summary>
        static object TryGetApplicationFrom(object comObject)
        {
            try
            {
                dynamic d = comObject;
                try { object app = d.Application; if (app != null) return app; } catch { }
                try { var pres = d.Presentations; if (pres != null) return comObject; } catch { }
                return null;
            }
            catch { return null; }
        }

        object TryAcquireFromActiveObject()
        {
            try
            {
                int hr = CLSIDFromProgID("PowerPoint.Application", out Guid clsid);
                if (hr != 0) return null;

                hr = GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
                if (hr != 0 || obj == null) return null;

                dynamic probe = obj;
                var _ = probe.Name;
                return obj;
            }
            catch (COMException ex)
            {
                LastFailureReason = ExplainHResult(ex.HResult);
                return null;
            }
            catch { return null; }
        }

        static string ExplainHResult(int hr)
        {
            switch (hr)
            {
                case MK_E_UNAVAILABLE:
                    return "GetActiveObject 返回 MK_E_UNAVAILABLE——" +
                           "本程序与 PowerPoint 的权限级别可能不同（一方以管理员运行），" +
                           "Windows 的 UIPI 会阻断跨完整性级别的 COM。";
                case CO_E_SERVER_EXEC_FAILURE:
                    return "PowerPoint COM 服务器无法启动（CO_E_SERVER_EXEC_FAILURE）——" +
                           "通常是 Office 首次运行向导未完成或安装损坏。";
                case RPC_E_DISCONNECTED:
                    return "PowerPoint COM 对象已断开连接。";
                default:
                    return "COM 调用失败，HRESULT=0x" + hr.ToString("X8");
            }
        }

        #endregion

        #region 放映状态：COM 优先，Win32 兜底

        void RefreshSlideShowState()
        {
            // 一级：COM。能拿到页码时用 COM，最准。
            bool comInShow = false;
            int comSlide = -1;
            if (_comApplication != null)
            {
                try
                {
                    // LTSC L-8：这段每 500ms 跑一次。dynamic 每取一次成员都会造一个新 RCW
                    // （SlideShowWindows / windows[1] / View 各一个），原来一个都不释放。
                    // 一节课 5400 轮 × 3 个 RCW，PowerPoint 侧引用计数只涨不落。
                    // 逐个接住，用完立刻还。
                    object app = _comApplication;
                    object windows = null;
                    object window = null;
                    object view = null;
                    try
                    {
                        windows = app.GetType().InvokeMember("SlideShowWindows",
                            System.Reflection.BindingFlags.GetProperty, null, app, null);
                        if (windows != null)
                        {
                            int count = Convert.ToInt32(windows.GetType().InvokeMember("Count",
                                System.Reflection.BindingFlags.GetProperty, null, windows, null));
                            if (count > 0)
                            {
                                window = windows.GetType().InvokeMember("Item",
                                    System.Reflection.BindingFlags.GetProperty, null, windows, new object[] { 1 });
                                if (window != null)
                                {
                                    try
                                    {
                                        view = window.GetType().InvokeMember("View",
                                            System.Reflection.BindingFlags.GetProperty, null, window, null);
                                        if (view != null)
                                        {
                                            object pos = view.GetType().InvokeMember("CurrentShowPosition",
                                                System.Reflection.BindingFlags.GetProperty, null, view, null);
                                            comSlide = Convert.ToInt32(pos);
                                        }
                                        comInShow = true;
                                    }
                                    catch
                                    {
                                        // View 拿不到但放映窗口存在，仍算在放映
                                        comInShow = true;
                                    }
                                }
                            }
                        }
                    }
                    finally
                    {
                        SafeRelease(view);
                        SafeRelease(window);
                        SafeRelease(windows);
                    }
                }
                catch (COMException ex)
                {
                    if (ex.HResult == RPC_E_DISCONNECTED) Disconnect("COM 对象已断开");
                }
                catch { }
            }

            // 二级：Win32。COM 不可用时靠窗口类名判断，并对齐窗口矩形。
            //
            // 这里刻意每轮都枚举，不做"已知在放映就跳过"的节流。
            // 试过那样省：跳过枚举时只能拿上一次的结果当输入，等于让状态机自己
            // 证明自己——一旦进入放映就永远检测不到退出。省下的那点枚举开销
            // 换一个永远退不出去的故障，不划算。
            System.Drawing.Rectangle bounds = FindSlideShowWindowBounds(out bool win32InShow);

            var newBounds = (comInShow || (!IsConnected && win32InShow)) ? bounds : System.Drawing.Rectangle.Empty;
            // 只在真的变了才写，避免下游绑定时每 500ms 收到一次无意义的通知
            if (newBounds != SlideShowBounds) SlideShowBounds = newBounds;

            bool nowRawInShow = comInShow || win32InShow;

            // 防抖动：PPT 退出放映时会先隐藏窗口再销毁，中间有一两帧窗口
            // 检测不到，直接照状态变化走会报一次假的"放映结束"。
            // 要求候选状态连续稳定 StableTicksRequired 次才认定，代价是最多
            // 500ms × 3 = 1.5s 的延迟——对人眼不可感知，却能吃掉抖动。
            bool stable = (nowRawInShow == _pendingInShow);
            if (stable)
            {
                if (_pendingInShowTicks < StableTicksRequired) _pendingInShowTicks++;
            }
            else
            {
                _pendingInShow = nowRawInShow;
                _pendingInShowTicks = 1;
            }

            bool nowInShow = _inSlideShow;
            if (_pendingInShowTicks >= StableTicksRequired)
                nowInShow = _pendingInShow;

            if (nowInShow && !_inSlideShow)
            {
                SetInSlideShow(true);
                // 这条是轮询兜底路径：COM 事件没到时由状态机补上。
                VerboseLog($"轮询检测到放映开始(COM事件未触发) comSlide={comSlide}");
                try { SlideShowBegin?.Invoke(_comSlideShowWindow); } catch { }
            }
            else if (!nowInShow && _inSlideShow)
            {
                SetInSlideShow(false);
                _currentSlide = -1;
                VerboseLog($"轮询检测到放映结束 comSlide={comSlide}");
                try { SlideShowEnd?.Invoke(null); } catch { }
            }

            if (nowInShow && comSlide > 0 && comSlide != _currentSlide)
            {
                _currentSlide = comSlide;
                try { SlideShowNextSlide?.Invoke(_comSlideShowWindow); } catch { }
            }
        }

        void SetInSlideShow(bool value)
        {
            if (_inSlideShow == value) return;
            _inSlideShow = value;
            try { SlideShowStateChanged?.Invoke(value); } catch { }
        }

        void RefreshSlidesCount()
        {
            int newCount = 0;
            object pres = null;
            try
            {
                if (_comApplication != null)
                {
                    dynamic app = _comApplication;
                    pres = app.ActivePresentation;
                    if (pres != null) newCount = (int)((dynamic)pres).Slides.Count;
                }
            }
            catch { newCount = 0; }

            int oldCount = SlidesCount;
            SlidesCount = newCount;

            // PresentationOpen / PresentationClose 的判据：有没有活动演示文稿。
            // 用 SlidesCount 的 0 ↔ 非0 变化来识别，而不是依赖 COM 事件——
            // 事件在异常退出时会静默丢失，这两个事件本来也没有调用方依赖精确时机。
            if (oldCount == 0 && newCount > 0)
            {
                _hasPresentation = true;
                // 订阅者会在 async 里继续用这个 Presentation（例如
                // Dispatcher.InvokeAsync 之后才调 InitializePresentation）。
                // 本方法尾部会 SafeRelease(pres)，若直接把同一个 RCW 交出去，
                // 异步继续跑时它已被分离，访问 get_Slides() / get_FullName()
                // 会抛 InvalidComObjectException（实测就是这么炸的）。
                //
                // 因此给每个订阅者单独 AddRef 一份引用；订阅者不再需要时
                // 自行 Marshal.ReleaseComObject。谁持有谁负责，不靠时序赌。
                RaisePresentationEvent(PresentationOpen, pres);
            }
            else if (oldCount > 0 && newCount == 0)
            {
                _hasPresentation = false;
                try { PresentationClose?.Invoke(null); } catch { }
            }

            SafeRelease(pres);
        }

        bool _hasPresentation;

        #endregion


        #region Win32 放映窗口

        [StructLayout(LayoutKind.Sequential)]
        struct RECT { public int Left, Top, Right, Bottom; }

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")] static extern bool EnumWindows(EnumWindowsProc cb, IntPtr p);
        [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetClassNameW(IntPtr h, StringBuilder s, int n);
        [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out RECT r);

        /// <summary>PowerPoint 放映窗口类名。实测为 "screenClass"。</summary>
        const string SlideShowWindowClass = "screenClass";

        /// <summary>
        /// 用 Win32 查找放映窗口。不依赖 COM，因此跨权限仍然有效——
        /// 这是本类在提权场景下唯一还能提供的能力：知道"在放映"以及窗口在哪。
        /// </summary>
        System.Drawing.Rectangle FindSlideShowWindowBounds(out bool found)
        {
            found = false;
            var result = System.Drawing.Rectangle.Empty;
            IntPtr best = IntPtr.Zero;
            int bestArea = 0;

            try
            {
                EnumWindows((h, p) =>
                {
                    try
                    {
                        if (!IsWindowVisible(h)) return true;

                        var cls = new StringBuilder(128);
                        if (GetClassNameW(h, cls, 256) <= 0) return true;
                        if (!string.Equals(cls.ToString(), SlideShowWindowClass, StringComparison.OrdinalIgnoreCase))
                            return true;

                        if (!GetWindowRect(h, out RECT rc)) return true;
                        int w = rc.Right - rc.Left;
                        int hgt = rc.Bottom - rc.Top;
                        if (w <= 0 || hgt <= 0) return true;

                        // 多个候选时取面积最大的那个（多显示器 / 预览窗口同时存在时更可靠）
                        int area = w * hgt;
                        if (area > bestArea)
                        {
                            bestArea = area;
                            best = h;
                            result = new System.Drawing.Rectangle(rc.Left, rc.Top, w, hgt);
                        }
                    }
                    catch { }
                    return true;
                }, IntPtr.Zero);
            }
            catch { }

            found = best != IntPtr.Zero;
            return result;
        }

        #endregion

        #region 导航 / 查询

        public bool TryNavigateToSlide(int slideNumber)
        {
            if (!TryGetView(out dynamic view)) return false;
            try
            {
                // SkipAnimationsWhenNavigating：PPT 的 GotoSlide 第二参数 1 = msoFalse（不播动画）
                view.GotoSlide(slideNumber, SkipAnimationsWhenNavigating ? 1 : 0);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PptBridge 跳转到第 {slideNumber} 页失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        public bool TryNavigateNext()
        {
            if (!TryGetView(out dynamic view)) return false;
            try { view.Next(); return true; }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PptBridge 下一页失败: {ex.Message}", LogHelper.LogType.Warning);
                VerboseLog($"下一页异常: {ex}");
                return false;
            }
        }

        public bool TryNavigatePrevious()
        {
            if (!TryGetView(out dynamic view)) return false;
            try { view.Previous(); return true; }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PptBridge 上一页失败: {ex.Message}", LogHelper.LogType.Warning);
                VerboseLog($"上一页异常: {ex}");
                return false;
            }
        }

        public int GetCurrentSlideNumber()
        {
            if (_currentSlide > 0) return _currentSlide;
            if (!TryGetView(out dynamic view)) return 0;
            try { return (int)view.CurrentShowPosition; }
            catch { return 0; }
        }

        public string GetPresentationName()
        {
            if (_comApplication == null) return null;
            try
            {
                dynamic app = _comApplication;
                dynamic pres = app.ActivePresentation;
                return pres == null ? null : (string)pres.Name;
            }
            catch { return null; }
        }

        public object GetCurrentActivePresentation()
        {
            if (_comApplication == null) return null;
            try
            {
                dynamic app = _comApplication;
                return app.ActivePresentation;
            }
            catch { return null; }
        }

        public bool TryStartSlideShow()
        {
            if (!TryGetSlideShowWindows(out dynamic windows)) return false;
            try
            {
                dynamic app = _comApplication;
                dynamic pres = app.ActivePresentation;
                if (pres == null) return false;
                pres.SlideShowSettings.Run();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PptBridge 启动放映失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        public bool TryEndSlideShow()
        {
            if (!TryGetSlideShowWindows(out dynamic windows)) return false;
            try
            {
                int count = (int)windows.Count;
                if (count == 0) return false;
                dynamic window = windows[1];
                dynamic view = window.View;
                view.Exit();
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PptBridge 结束放映失败: {ex.Message}", LogHelper.LogType.Warning);
                return false;
            }
        }

        public bool TryShowSlideNavigation()
        {
            // 右键菜单 / 导航面板：.NET 下没有可靠的等价操作。
            // 保留方法以满足接口，行为是"翻到下一页"——与旧实现保持一致的语义。
            return TryNavigateNext();
        }

        public List<PPTSlideThumbnail> ExportSlideThumbnails(int width, int height, IProgress<double> progress = null)
        {
            var list = new List<PPTSlideThumbnail>();
            if (_comApplication == null) return list;

            object pres = null;
            try
            {
                dynamic app = _comApplication;
                pres = app.ActivePresentation;
                if (pres == null) return list;

                dynamic dPres = pres;
                int total = (int)dPres.Slides.Count;
                string tempDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "icc-ppt-thumbs-" + Guid.NewGuid().ToString("N"));
                System.IO.Directory.CreateDirectory(tempDir);

                try
                {
                    for (int i = 1; i <= total; i++)
                    {
                        try
                        {
                            dynamic slide = dPres.Slides[i];
                            string path = System.IO.Path.Combine(tempDir, "s" + i + ".png");
                            slide.Export(path, "PNG", width, height);
                            if (System.IO.File.Exists(path))
                            {
                                list.Add(new PPTSlideThumbnail { SlideNumber = i, PngBytes = System.IO.File.ReadAllBytes(path) });
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"PptBridge 导出第 {i} 页缩略图失败: {ex.Message}", LogHelper.LogType.Warning);
                        }
                        progress?.Report((double)i / total);
                    }
                }
                finally
                {
                    try { System.IO.Directory.Delete(tempDir, true); } catch { }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PptBridge 导出缩略图失败: {ex.Message}", LogHelper.LogType.Warning);
            }
            finally
            {
                SafeRelease(pres);
            }

            return list;
        }

        bool TryGetSlideShowWindows(out dynamic windows)
        {
            windows = null;
            if (_comApplication == null) return false;
            try
            {
                dynamic app = _comApplication;
                windows = app.SlideShowWindows;
                return (int)windows.Count > 0;
            }
            catch { return false; }
        }

        bool TryGetView(out dynamic view)
        {
            view = null;
            if (!TryGetSlideShowWindows(out dynamic windows)) return false;
            try
            {
                dynamic window = windows[1];
                view = window.View;
                return view != null;
            }
            catch { return false; }
        }

        #endregion

        #region COM 事件

        void HookComEvents(object app)
        {
            try
            {
                dynamic d = app;
                d.SlideShowBegin += new Action<object>(OnComSlideShowBegin);
                d.SlideShowNextSlide += new Action<object>(OnComNextSlide);
                d.SlideShowEnd += new Action<object>(OnComSlideShowEnd);
                d.PresentationOpen += new Action<object>(OnComPresentationOpen);
                d.PresentationClose += new Action<object>(OnComPresentationClose);
                _comEventsHooked = true;
                LogHelper.WriteLogToFile("PptBridge COM 事件已注册", LogHelper.LogType.Trace);
            }
            catch (Exception ex)
            {
                // 事件挂不上不影响轮询兜底，这是刻意设计的双模
                _comEventsHooked = false;
                LogHelper.WriteLogToFile($"PptBridge COM 事件注册失败，改用轮询: {ex.Message}", LogHelper.LogType.Trace);
            }
        }

        void UnhookComEvents()
        {
            if (!_comEventsHooked || _comApplication == null) return;
            try
            {
                dynamic d = _comApplication;
                d.SlideShowBegin -= new Action<object>(OnComSlideShowBegin);
                d.SlideShowNextSlide -= new Action<object>(OnComNextSlide);
                d.SlideShowEnd -= new Action<object>(OnComSlideShowEnd);
                d.PresentationOpen -= new Action<object>(OnComPresentationOpen);
                d.PresentationClose -= new Action<object>(OnComPresentationClose);
            }
            catch { }
            _comEventsHooked = false;
        }

        void OnComSlideShowBegin(object window)
        {
            _comSlideShowWindow = window;
            _currentSlide = -1;
            VerboseLog("COM 事件: 放映开始");
        }

        void OnComNextSlide(object window)
        {
            _comSlideShowWindow = window;
            RefreshSlidesCount();
            VerboseLog($"COM 事件: 翻页 -> 当前页={_currentSlide}");
        }

        void OnComSlideShowEnd(object pres)
        {
            SafeRelease(_comSlideShowWindow);
            _comSlideShowWindow = null;
            _currentSlide = -1;
            VerboseLog("COM 事件: 放映结束");
        }

        void OnComPresentationOpen(object pres)
        {
            RefreshSlidesCount();
            if (!_hasPresentation)
            {
                _hasPresentation = true;
                // 同 RefreshSlidesCount：订阅者可能在异步里继续用这个对象，
                // 而 COM 的 Invoke 回调返回后引用随时可能被释放，必须自己持有。
                RaisePresentationEvent(PresentationOpen, pres);
            }
        }

        void OnComPresentationClose(object pres)
        {
            RefreshSlidesCount();
            if (_hasPresentation)
            {
                _hasPresentation = false;
                try { PresentationClose?.Invoke(pres); } catch { }
            }
        }

        #endregion

        #region COM 释放

        void ReleaseCom()
        {
            SafeRelease(_comSlideShowWindow);
            _comSlideShowWindow = null;
            SafeRelease(_comApplication);
            _comApplication = null;
        }

        static bool LooksLikePresentation(string displayName)
        {
            if (string.IsNullOrEmpty(displayName)) return false;
            // 实测 ROT 里注册的是完整文件路径，按扩展名匹配是唯一可靠的判据
            string[] exts = { ".pptx", ".ppt", ".ppsx", ".pps", ".pptm", ".ppsm", ".potx", ".pot", ".potm" };
            foreach (var ext in exts)
            {
                if (displayName.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }

        /// <summary>
        /// 触发 Presentation 事件，并给每个订阅者各自一份独立的 COM 引用。
        /// 订阅者常见写法是 Dispatcher.InvokeAsync 之后才真正使用这个对象——
        /// 那时本方法早已返回、调用方也已释放了自己那份引用，若共用同一个 RCW，
        /// 订阅者拿到的是已分离的代理，访问任何成员都会抛
        /// InvalidComObjectException。谁持有谁负责，不靠时序赌。
        /// </summary>
        static void RaisePresentationEvent(Action<object> handlers, object pres)
        {
            if (handlers == null) return;
            foreach (Action<object> h in handlers.GetInvocationList())
            {
                object owned = pres;
                if (pres != null && Marshal.IsComObject(pres))
                {
                    // 给订阅者一个独立的 RCW（持有自己的一份引用），它用完自己释放。
                    // 不能直接传 pres：调用方随后就会 SafeRelease，订阅者若在异步里
                    // 继续用，访问任何成员都会抛 InvalidComObjectException。
                    IntPtr pUnk = IntPtr.Zero;
                    try
                    {
                        pUnk = Marshal.GetIUnknownForObject(pres);   // 引用 +1
                        owned = Marshal.GetObjectForIUnknown(pUnk);  // 新 RCW，引用 +1
                    }
                    catch { owned = pres; }
                    finally
                    {
                        if (pUnk != IntPtr.Zero) Marshal.Release(pUnk);  // 放掉探针那次
                    }
                }
                try { h(owned); }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"Presentation 事件处理异常: {ex.Message}", LogHelper.LogType.Warning);
                }
                finally
                {
                    // LTSC L-3：649642f3 给每个订阅者造了独立 RCW，解决它们拿到已释放对象的问题，
                    // 但没有收尾——每订阅一次 PPT 侧引用净 +1，开几次演示文稿 PowerPoint 就退不出去。
                    // 谁持有谁负责：这里就是那个「持有者」，用完必须还。
                    // 只在确实是新建的 RCW 时释放（owned != pres 说明上面 get/GetObject 成功过）。
                    if (!ReferenceEquals(owned, pres)) SafeRelease(owned);
                }
            }
        }

        static void SafeRelease(object comObject)
        {
            if (comObject == null) return;
            try
            {
                if (Marshal.IsComObject(comObject)) Marshal.ReleaseComObject(comObject);
            }
            catch { }
        }

        #endregion

        #region P/Invoke

        [DllImport("ole32.dll")] static extern int GetRunningObjectTable(int reserved, out IRunningObjectTable pprot);
        [DllImport("ole32.dll")] static extern int CreateBindCtx(int reserved, out IBindCtx ppbc);
        [DllImport("ole32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
        static extern int CLSIDFromProgID(string lpszProgID, out Guid lpclsid);
        [DllImport("oleaut32.dll", PreserveSig = true)]
        static extern int GetActiveObject(ref Guid rclsid, IntPtr pvReserved,
            [MarshalAs(UnmanagedType.IUnknown)] out object ppunk);

        #endregion
    }
}

