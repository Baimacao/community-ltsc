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
        /// <summary>LTSC P1b：上一轮一级读到的页码，只在变化时记日志，避免每 500ms 刷屏。</summary>
        int _lastComSlideLogged = -1;

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

        /// <summary>
        /// 检测到「PPT 已经在放映，但 ICC 因为权限级别连不上」时触发一次。
        ///
        /// 为什么要单独开一个事件：这个故障对用户是完全静默的 ——
        /// 老师点了放映，屏幕上是 PPT，ICC 却什么都不显示（翻页组件不出、
        /// 页码不动、批注层不贴）。日志里只有一行 Warning，用户看不到。
        ///
        /// 和普通连接失败的区别：普通失败会自己好，权限失败不会。
        /// 所以这条只在「确实在放映 + 确实连不上 + 原因就是权限」时发，
        /// 而且同一个失败状态只发一次，不刷屏。
        /// </summary>
        public event Action<string> PermissionBlocked;

        /// <summary>
        /// 总页数从「无效（≤0）」恢复到「有效（&gt;0）」时触发，参数是新页数。
        ///
        /// 为什么需要这个事件：翻页栏的显示闸门里有一条 hasValidPageCount，
        /// 一旦总页数掉到 0，整条翻页栏会被 HideAllNavigationPanels() 隐藏，
        /// 而隐藏是单向的——重新显示只发生在「放映开始」分支里。
        /// 所以页数恢复有效之后，必须有人重新评估一次可见性，
        /// 否则翻页栏要等到下一次进入放映才会回来。
        /// </summary>
        public event Action<int> SlidesCountRecovered;

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

        /// <summary>
        /// 最近一次连接失败是不是「权限级别不同」造成的（GetActiveObject → MK_E_UNAVAILABLE）。
        ///
        /// 单独暴露这一个判断，是因为它和普通失败的处理方式完全不同：
        ///   · 普通失败（PPT 没开、还没加载完）→ 等一会儿就自己好了，不该打扰用户
        ///   · 权限失败 → 等到天荒地老也不会自己好，必须告诉用户怎么办
        ///
        /// 2026-11-18 班测：「部分班级报告进入 PPT 放映后 ICC 无反应」。
        /// 现场就是这样：ICC 以管理员启动，PowerPoint 是普通权限，
        /// UIPI 直接切断跨完整性级别的 COM —— 界面上什么提示都没有，
        /// 老师只能看到翻页组件不出现。
        /// </summary>
        public bool IsPermissionFailure =>
            !string.IsNullOrEmpty(LastFailureReason) &&
            LastFailureReason.IndexOf("MK_E_UNAVAILABLE", StringComparison.Ordinal) >= 0;

        /// <summary>上次已写进日志的连接失败原因，用于限流，避免每 2 秒刷一遍。</summary>
        string _lastLoggedFailureReason;
        /// <summary>上次写连接失败日志的时间（UTC）。</summary>
        DateTime _lastFailureLogAt = DateTime.MinValue;

        // ---------- 状态 ----------
        object _comApplication;

        /// <summary>
        /// 当前演示文稿的完整路径 —— 打开时缓存，之后不再问 COM。
        ///
        /// LTSC（2026-11-20）：原来保存笔迹时要在「退出放映」那一刻去问 COM 要路径，
        /// 而那时 PPT 正在拆放映窗口，这一问会拖住 COM 引用，PowerPoint 退不掉。
        /// 改成打开时记下来，保存时直接用字符串 —— 没有 COM 参与。
        /// </summary>
        string _cachedPresentationFullName;

        /// <summary>
        /// 释放 COM 之后的冷却期。在这之前轮询不重连。
        ///
        /// LTSC（2026-11-20）：用户关演示文稿时我们会解绑钩子并释放引用，
        /// 否则 PowerPoint 会认为还有外部程序连着它，关窗口后进程退不掉。
        /// 但释放完立刻重连，等于白解绑 —— 实测 0.3 秒就挂回去了，
        /// PPT 只能等 21 秒超时才退出。
        /// </summary>
        DateTime _comReleaseCooldownUntil = DateTime.MinValue;

        /// <summary>释放 COM 后进入多久的冷却期。</summary>
        static readonly TimeSpan ComReleaseCooldown = TimeSpan.FromSeconds(8);
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
                //
                // LTSC（2026-11-20，用户报「PowerPoint 被劫持」）：
                // 加了一层冷却。原因：用户关掉演示文稿时我们在 OnComPresentationClose
                // 里解绑了 COM 钩子（不解绑 PPT 就退不掉），但解绑后 _comApplication
                // 变成 null，这一轮轮询立刻又重连、又挂上钩子 —— 0.3 秒就挂回去了，
                // 日志实测：16:16:21 解绑 → 16:16:22 又注册事件。
                //
                // 结果 PPT 要等 21 秒超时才退（用户已经以为「没释放」）。
                //
                // 冷却期内不重连，让 PPT 能真正退出。
                if (_comApplication == null &&
                    (now - _lastConnectCheck) >= ConnectCheckInterval &&
                    (now - _comReleaseCooldownUntil) >= TimeSpan.Zero)
                {
                    _lastConnectCheck = now;
                    TryConnect();
                }

                // 放映状态：每次轮询都更新，因为它决定白板要不要贴上去
                RefreshSlideShowState();

                // 「在放映但连不上、而且原因是权限」—— 这是唯一一种用户必须被惊动的失败。
                //
                // 判据三条同时成立：
                //   1. 没有 COM 连接（IsConnected == false）
                //   2. Win32 那边确实看到了放映窗口（IsInSlideShow == true）
                //      —— 只看 1 会误报：PPT 刚打开还没放映时也连不上，那种等一下就好
                //   3. 失败原因就是权限级别不同
                CheckPermissionBlocked();
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

        // ── 权限阻塞通知 ───────────────────────────────────────────
        /// <summary>上一次已经上报过的失败原因。相同原因不重复上报。</summary>
        private string _lastReportedPermissionBlock;

        /// <summary>
        /// 在放映中、但连不上、且原因是权限级别时，通知上层一次。
        ///
        /// 三条同时成立才报，缺一条都会误报：
        ///   · 没连上   —— 连上了就没有问题
        ///   · 在放映   —— 只是开着 PPT 没放映时连不上很常见，等一下就好
        ///   · 是权限   —— 别的失败原因有别的处理方式
        /// </summary>
        void CheckPermissionBlocked()
        {
            // 连接恢复就清掉「已上报」标记，下次再断还能再报。
            if (IsConnected || !IsInSlideShow)
            {
                _lastReportedPermissionBlock = null;
                return;
            }

            if (!IsPermissionFailure) return;
            if (LastFailureReason == _lastReportedPermissionBlock) return;

            _lastReportedPermissionBlock = LastFailureReason;

            LogHelper.WriteLogToFile(
                "PPT 在放映但因权限级别连不上，已提示用户：" + LastFailureReason,
                LogHelper.LogType.Warning);

            try
            {
                PermissionBlocked?.Invoke(LastFailureReason);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(
                    "PPT 事件 PermissionBlocked 的订阅方抛异常：" + ex,
                    LogHelper.LogType.Error);
            }
        }

        #region 连接

        /// <summary>
        /// 这个 PowerPoint 实例当前有没有打开的演示文稿。
        ///
        /// LTSC（2026-11-20）：用来决定「该不该连」。
        /// 用户关掉最后一个演示文稿后，PPT 进程往往还活着（在等外部连接），
        /// 这时候连上去挂 COM 钩子，会让它永远退不掉。
        ///
        /// 宁可多花一次 COM 调用，也不要留一个退不掉的 PowerPoint 进程。
        /// </summary>
        static bool HasOpenPresentation(object app)
        {
            if (app == null) return false;
            try
            {
                object presentations = app.GetType().InvokeMember("Presentations",
                    System.Reflection.BindingFlags.GetProperty, null, app, null);
                if (presentations == null) return false;
                try
                {
                    object countObj = presentations.GetType().InvokeMember("Count",
                        System.Reflection.BindingFlags.GetProperty, null, presentations, null);
                    int count = countObj == null ? 0 : Convert.ToInt32(countObj);
                    // 诊断：把真实数量记下来。
                    // 用户报「PPT 退不掉」时，这行能直接看出判断依据对不对。
                    LogHelper.WriteLogToFile($"[PPT] 检测到 Presentations.Count={count}",
                        LogHelper.LogType.Trace);
                    return count > 0;
                }
                finally { SafeRelease(presentations); }
            }
            catch
            {
                // 读不到就当没有 —— 不连最坏是不联动，连上最坏是 PPT 退不掉。
                return false;
            }
        }

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

            // LTSC（2026-11-20，用户报「PowerPoint 被劫持了，pptcom 都没被释放」）：
            //
            // 关键判断：**有打开的演示文稿才连**。
            //
            // 为什么：ICC 连上 PowerPoint.Application 会挂 5 个 COM 事件钩子，
            // 只要有钩子在，PPT 就认为「还有外部程序连接我」，用户关窗口后进程退不掉。
            //
            // 而原来的判断只有「进程活着」（IsPptProcessAlive）。
            // 用户关掉演示文稿后进程是活的（等外部连接），于是：
            //   活着 → 重连 → 挂钩子 → 退不掉 → 一直活着 → 又重连 —— 死循环。
            // 日志实测：16:21:39 解绑释放 → 16:21:47 又注册，8 秒冷却一到就挂回去。
            //
            // 改成看「有没有 Presentation」。关掉演示文稿后没有 Presentation，
            // 就不连、不挂钩子，PPT 才能真正退出。
            if (app != null && !HasOpenPresentation(app))
            {
                SafeRelease(app);
                app = null;
                LastFailureReason = "PowerPoint 运行中但没有打开的演示文稿";
                if (_comApplication != null) Disconnect("没有打开的演示文稿");
                return;
            }

            if (app == null)
            {
                // 拿不到 COM 不代表不能用：Win32 仍然能看到放映窗口。
                // 失败原因由 TryAcquireFromActiveObject 记录，这里不能覆盖成空。
                if (_comApplication != null) Disconnect("COM 连接丢失");
                if (string.IsNullOrEmpty(LastFailureReason))
                    LastFailureReason = "ROT 与 GetActiveObject 均未取到 PowerPoint";

                // LTSC P2（2026-11-12）：原来这里直接 return —— 失败原因只存进字段，
                // 从不写日志。而 Disconnect 只在「已连接又断开」时才走，
                // 于是「从头到尾没连上」这种情况日志里一条线索都没有。
                // 2026-11-12 实测：一整场 4911 行日志里「已连接 PowerPoint」一次都没出现，
                // 也一条失败原因都没有，完全查不出为什么。
                // 这里补写日志，但要限流——轮询每 2 秒一轮，不能刷屏。
                if (LastFailureReason != _lastLoggedFailureReason ||
                    (DateTime.UtcNow - _lastFailureLogAt).TotalMinutes >= 1)
                {
                    _lastLoggedFailureReason = LastFailureReason;
                    _lastFailureLogAt = DateTime.UtcNow;
                    LogHelper.WriteLogToFile(
                        $"PPT 连接失败：{LastFailureReason}", LogHelper.LogType.Warning);
                }
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
            try
            {
                PPTConnectionChanged?.Invoke(true);
            }
            catch (Exception evtEx)
            {
                // LTSC（2026-11-17）：原来这里是 catch { }。
                // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                // 但日志里什么都没有。事件派发失败必须可见。
                LogHelper.WriteLogToFile(
                    "PPT 事件 PPTConnectionChanged 的订阅方抛异常：" + evtEx,
                    LogHelper.LogType.Error);
            }
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
            try
            {
                PPTConnectionChanged?.Invoke(false);
            }
            catch (Exception evtEx)
            {
                // LTSC（2026-11-17）：原来这里是 catch { }。
                // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                // 但日志里什么都没有。事件派发失败必须可见。
                LogHelper.WriteLogToFile(
                    "PPT 事件 PPTConnectionChanged 的订阅方抛异常：" + evtEx,
                    LogHelper.LogType.Error);
            }
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

            object bestApp = null;
            int bestPriority = 0;

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
                        if (app == null)
                        {
                            SafeRelease(comObject);
                            continue;
                        }
                        // app 与 comObject 是同一个对象时所有权转移，不能再释放
                        if (!ReferenceEquals(app, comObject)) SafeRelease(comObject);

                        int priority = ScorePptCandidate(app);
                        VerboseLog($"[PPT详细] ROT候选 {displayName} 优先级={priority}");

                        if (priority > bestPriority)
                        {
                            // 换更好的：先把旧的还掉，避免引用泄漏
                            if (bestApp != null) SafeRelease(bestApp);
                            bestPriority = priority;
                            bestApp = app;
                        }
                        else
                        {
                            SafeRelease(app);
                        }
                    }
                    finally
                    {
                        SafeRelease(moniker);
                    }
                }

                if (bestApp != null)
                    VerboseLog($"[PPT详细] 选中 ROT 实例 优先级={bestPriority}");
                return bestApp;
            }
            catch
            {
                if (bestApp != null) { SafeRelease(bestApp); bestApp = null; }
                return null;
            }
            finally
            {
                SafeRelease(enumMoniker);
                SafeRelease(bindCtx);
                SafeRelease(rot);
            }
        }

        /// <summary>
        /// 给一个 PowerPoint Application 候选打分。0 分表示这个实例没打开任何演示文稿。
        /// 学 Inkeys3 PptCOM.GetAnyActivePowerPoint 的评分口径。
        /// </summary>
        int ScorePptCandidate(object app)
        {
            object pres = null;
            object window = null;
            try
            {
                try
                {
                    pres = app.GetType().InvokeMember("ActivePresentation",
                        System.Reflection.BindingFlags.GetProperty, null, app, null);
                }
                catch { return 0; }

                if (pres == null) return 0;

                // 有 ActivePresentation：1 分
                int score = 1;

                try
                {
                    window = pres.GetType().InvokeMember("SlideShowWindow",
                        System.Reflection.BindingFlags.GetProperty, null, pres, null);
                }
                catch { window = null; }

                if (window == null) return score;

                // 放映窗口存在：2 分
                score = 2;

                // 窗口 Active：3 分。MS PPT 返回 int(-1)，WPS 返回 bool(true)
                try
                {
                    object val = window.GetType().InvokeMember("Active",
                        System.Reflection.BindingFlags.GetProperty, null, window, null);
                    if (val is int iv && iv == -1) score = 3;
                    else if (val is bool bv && bv) score = 3;
                }
                catch { }

                return score;
            }
            finally
            {
                SafeRelease(window);
                SafeRelease(pres);
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
                if (hr != 0)
                {
                    // LTSC P2（2026-11-12）：原来这里直接 return null，原因一个字不记。
                    LastFailureReason = "CLSIDFromProgID(PowerPoint.Application) 失败，HRESULT=0x" + hr.ToString("X8");
                    return null;
                }

                // 关键：GetActiveObject 失败时【不抛异常】，它把错误放在返回值 hr 里。
                // 原来这里 if (hr != 0) return null; —— 原因直接丢掉。
                // 于是 LastFailureReason 一直是 null，TryConnect 兜底成空字符串，
                // 日志里只剩「连不上」三个字，查不出为什么。
                // 2026-11-12 实测：一整场 4911 行日志里「已连接 PowerPoint」一次都没出现，
                // 却一条失败原因都没有——就是这条路径吞掉的。
                hr = GetActiveObject(ref clsid, IntPtr.Zero, out object obj);
                if (hr != 0 || obj == null)
                {
                    LastFailureReason = ExplainHResult(hr) +
                        $"（GetActiveObject HRESULT=0x{hr:X8}）";
                    return null;
                }

                dynamic probe = obj;
                var _ = probe.Name;
                return obj;
            }
            catch (COMException ex)
            {
                LastFailureReason = ExplainHResult(ex.HResult);
                return null;
            }
            catch (Exception ex)
            {
                // LTSC P2：原来这里是 catch { return null; }，任何托管异常都被吞。
                LastFailureReason = $"{ex.GetType().Name}: {ex.Message}";
                return null;
            }
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
                        if (windows == null)
                        {
                            VerboseLog("[PPT详细] SlideShowWindows 取到 null");
                        }
                        else
                        {
                            int count = Convert.ToInt32(windows.GetType().InvokeMember("Count",
                                System.Reflection.BindingFlags.GetProperty, null, windows, null));
                            // LTSC P2：这是整条链路上唯一没有诊断的出口。
                            // Count==0 意味着「这个 Application 上没有放映窗口」——
                            // 可能是没在放映，也可能是连错了 PowerPoint 实例（多开的情况）。
                            // 之前这里静默跳过，日志里只看得到 comSlide=-1，查不出是哪一种。
                            if (count == 0)
                            {
                                VerboseLog("[PPT详细] SlideShowWindows.Count=0（当前实例无放映窗口）");
                            }
                            if (count > 0)
                            {
                                // LTSC P1b（2026-11-11 实测）：SlideShowWindows 是 COM 集合，
                                // Index 是带参数的索引器，不是普通属性。
                                // 用 BindingFlags.GetProperty + args 调它，在 MS PowerPoint 16.0 上
                                // 稳定抛 0x80020003 DISP_E_MEMBERNOTFOUND（Count 能拿到、Item 拿不到）。
                                // 正确做法是先按 InvokeMethod 试索引器，不行再退回 GetProperty。
                                try
                                {
                                    window = windows.GetType().InvokeMember("Item",
                                        System.Reflection.BindingFlags.InvokeMethod, null, windows, new object[] { 1 });
                                }
                                catch (Exception exItem)
                                {
                                    VerboseLog($"[PPT详细] SlideShowWindows.Item(InvokeMethod) 失败: {exItem.GetType().Name}: {exItem.Message}");
                                    window = null;
                                }
                                if (window != null)
                                {
                                    try
                                    {
                                        view = window.GetType().InvokeMember("View",
                                            System.Reflection.BindingFlags.GetProperty, null, window, null);
                                        comInShow = true;

                                        // LTSC P1-6（2026-11-12）：轮询路径原来从不刷 SlidesCount。
                                        //
                                        // RefreshSlidesCount() 只有 4 个调用点，全在 COM 事件回调里
                                        // （连接成功 / 翻页 / 打开演示文稿 / 关闭演示文稿）。
                                        // 而 COM 事件在放映刚启动时经常不触发——那时靠的就是这条轮询。
                                        //
                                        // 后果：SlidesCount 一直是 0，而
                                        // PPTUIManager.UpdateNavigationPanelsVisibility 里
                                        // 有 hasValidPageCount = SlidesCount > 0 这个闸门，
                                        // 于是整条翻页栏被 HideAllNavigationPanels() 隐藏。
                                        // 2026-11-12 早上「翻页栏不出现」就是这么来的：
                                        // 轮询能算出 comSlide=1，却没人去刷总页数。
                                        //
                                        // 这里在确认进入放映后补刷一次。RefreshSlidesCount 自带
                                        // oldCount/newCount 变化判断，重复调用不会重复触发事件。
                                        if (SlidesCount <= 0) RefreshSlidesCount();
                                        if (view != null)
                                        {
                                            // 一级：View.Slide.SlideIndex（Office 标准路径）
                                            comSlide = ReadShowPosition(view);
                                            if (comSlide > 0 && comSlide != _lastComSlideLogged)
                                            {
                                                _lastComSlideLogged = comSlide;
                                                VerboseLog($"[PPT详细] 一级拿到页码: {comSlide}");
                                            }
                                        }
                                    }
                                    catch (Exception exView)
                                    {
                                        // 放映窗口在，但 View 取不到。仍然算在放映。
                                        comInShow = true;
                                        // LTSC P1：这里原来什么都不记。日志里 comSlide 一路 -1，
                                        // 却查不出是哪一级断的——所以补上三级诊断。
                                        VerboseLog($"[PPT详细] View 取不到: {exView.GetType().Name}: {exView.Message}");
                                    }

                                    // 二级兜底：View 拿不到页码时，退到 Presentation.SlideShowWindow.View
                                    if (comSlide <= 0)
                                    {
                                        comSlide = TryGetSlideIndexFallback();
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
                    else VerboseLog($"[PPT详细] 一级取页码 COMException: 0x{ex.HResult:X8} {ex.Message}");
                }
                catch (Exception ex)
                {
                    // LTSC P2：原来这里是 catch { }，什么都不知道就过去了。
                    // 管理员权限下跨完整性访问 COM 抛的异常如果属于
                    // AccessViolation / InvalidCast 一类，会一路走到这里。
                    //
                    // LTSC P1b：InvokeMember 抛的是 TargetInvocationException，
                    // 真正的 COM 错误包在 InnerException 里。只打外层等于什么都没说——
                    // 实机测试时日志被 "TargetInvocationException" 刷满，
                    // 却查不出到底是哪个属性、什么 HRESULT。
                    var inner = ex is System.Reflection.TargetInvocationException tie && tie.InnerException != null
                        ? tie.InnerException
                        : null;
                    if (inner != null)
                    {
                        string hr = inner is COMException cie ? $"0x{cie.HResult:X8} " : "";
                        VerboseLog($"[PPT详细] 一级取页码异常: {hr}{inner.GetType().Name}: {inner.Message}");
                    }
                    else
                    {
                        VerboseLog($"[PPT详细] 一级取页码异常: {ex.GetType().FullName}: {ex.Message}");
                    }
                }
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

            // LTSC（2026-11-20）：在这里缓存演示文稿路径。
            //
            // 为什么不在放映结束时缓存：那一刻 PPT 正在拆放映窗口，
            // 去问 COM 会拖住引用，PowerPoint 就退不掉（用户报「被劫持」）。
            //
            // 为什么不在 OnComPresentationOpen 里缓存：那条 COM 事件不一定会触发
            // （注册时遇到过 RPC_E_SERVERCALL_RETRYLATER，实际走的是轮询兜底），
            // 班测日志里「拼路径：fullName=(空→用名字算哈希)」就是证据。
            //
            // 轮询里最合适的时机是「还没进放映」——那时 PPT 空闲，
            // 取一次路径很安全，而且此后整个放映过程都有缓存可用。
            if (!nowInShow && string.IsNullOrEmpty(_cachedPresentationFullName) && IsConnected)
            {
                try
                {
                    object cachedPres = GetCurrentActivePresentation();
                    if (cachedPres != null)
                    {
                        try
                        {
                            string fn = (string)cachedPres.GetType().InvokeMember("FullName",
                                System.Reflection.BindingFlags.GetProperty, null, cachedPres, null);
                            if (!string.IsNullOrEmpty(fn))
                            {
                                _cachedPresentationFullName = fn;
                                LogHelper.WriteLogToFile($"[PPT笔迹] 已缓存演示文稿路径：{fn}",
                                    LogHelper.LogType.Event);
                            }
                        }
                        finally { SafeRelease(cachedPres); }
                    }
                }
                catch (Exception cacheEx)
                {
                    VerboseLog("缓存演示文稿路径失败: " + cacheEx.Message);
                }
            }

            if (nowInShow && !_inSlideShow)
            {
                SetInSlideShow(true);
                // LTSC P1：轮询兜底路径原来直接把 _comSlideShowWindow 传下去，
                // 而走这条路时 COM 事件从没触发过，这个字段就是 null。
                // 订阅方 MW_PPT.OnPPTSlideShowBegin 拿 payload as SlideShowWindow 得到 null，
                // 进不了主分支，只能走兜底——而兜底里 GetCurrentSlideNumber() 又常常是 -1。
                // 结果：放映明明开始了，页码是 0，翻页组件不出现。
                // 这里在触发事件前先把窗口补上。
                if (_comSlideShowWindow == null)
                {
                    _comSlideShowWindow = TryFetchSlideShowWindow();
                    if (_comSlideShowWindow != null)
                        VerboseLog("[PPT详细] 轮询路径已补取放映窗口");
                }
                VerboseLog($"轮询检测到放映开始(COM事件未触发) comSlide={comSlide} 窗口={(_comSlideShowWindow != null ? "有" : "无")}");
                try
                {
                    SlideShowBegin?.Invoke(_comSlideShowWindow);
                }
                catch (Exception evtEx)
                {
                    // LTSC（2026-11-17）：原来这里是 catch { }。
                    // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                    // 但日志里什么都没有。事件派发失败必须可见。
                    LogHelper.WriteLogToFile(
                        "PPT 事件 SlideShowBegin 的订阅方抛异常：" + evtEx,
                        LogHelper.LogType.Error);
                }
            }
            else if (!nowInShow && _inSlideShow)
            {
                SetInSlideShow(false);
                _currentSlide = -1;
                VerboseLog($"轮询检测到放映结束 comSlide={comSlide}");
                try
                {
                            // LTSC（2026-11-20）：原来这里传 null，导致订阅方（退出放映的保存逻辑）
                    // 拿不到 presentation，也就拿不到 FullName。
                    //
                    // 后果很隐蔽：GetPresentationStrokeFolderPath 在 FullName 为空时
                    // 退化成「用演示文稿名算哈希」，得到一个和路径哈希不同的文件夹名 ——
                    // 写盘写进 A 文件夹，读的时候按路径哈希去 B 文件夹找，永远读不到。
                    // 班测表现就是「退出放映后新笔迹没保存」。
                    //
                    // 这里把还活着的 presentation 传出去。拿不到才退回 null。
                    // 传当前演示文稿的完整路径字符串（不是 COM 对象）。
                    //
                    // 不传对象的原因：COM 对象在事件回调返回后随时可能被释放，
                    // 订阅方（保存逻辑）要等到后面 async 才用，那时可能已经失效。
                    // 而保存逻辑真正需要的只是「算哈希用的那个路径」，
                    // 所以传字符串最稳 —— 它是托管对象，不受 COM 生命周期影响。
                    // 用打开时缓存下来的路径，**不要**在这时候问 COM。
                    //
                    // LTSC（2026-11-20，用户反馈「PowerPoint 被劫持了，pptcom 都没被释放」）：
                    // 我上一版在这里调 GetPresentationFullNameSafe()，
                    // 走的是 app.ActivePresentation —— 而此刻 PPT 正在拆放映窗口。
                    // 这一问会给 ActivePresentation 造一个新的 RCW 并拖住它，
                    // 后果是关掉 PPT 后进程退不掉。
                    //
                    // 路径在打开演示文稿时就该记下来（见 OnComPresentationOpen），
                    // 它是纯字符串，不受 COM 生命周期影响。
                    string endFullName = _cachedPresentationFullName;

                    VerboseLog($"放映结束，演示文稿路径={(string.IsNullOrEmpty(endFullName) ? "(空，用缓存也没有)" : endFullName)}");
                    SlideShowEnd?.Invoke(endFullName);
                }
                catch (Exception evtEx)
                {
                    // LTSC（2026-11-17）：原来这里是 catch { }。
                    // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                    // 但日志里什么都没有。事件派发失败必须可见。
                    LogHelper.WriteLogToFile(
                        "PPT 事件 SlideShowEnd 的订阅方抛异常：" + evtEx,
                        LogHelper.LogType.Error);
                }
            }

            if (nowInShow && comSlide > 0 && comSlide != _currentSlide)
            {
                _currentSlide = comSlide;
                try
                {
                    SlideShowNextSlide?.Invoke(_comSlideShowWindow);
                }
                catch (Exception evtEx)
                {
                    // LTSC（2026-11-17）：原来这里是 catch { }。
                    // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                    // 但日志里什么都没有。事件派发失败必须可见。
                    LogHelper.WriteLogToFile(
                        "PPT 事件 SlideShowNextSlide 的订阅方抛异常：" + evtEx,
                        LogHelper.LogType.Error);
                }
            }
        }

        void SetInSlideShow(bool value)
        {
            if (_inSlideShow == value) return;
            _inSlideShow = value;
            try
            {
                SlideShowStateChanged?.Invoke(value);
            }
            catch (Exception evtEx)
            {
                // LTSC（2026-11-17）：原来这里是 catch { }。
                // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                // 但日志里什么都没有。事件派发失败必须可见。
                LogHelper.WriteLogToFile(
                    "PPT 事件 SlideShowStateChanged 的订阅方抛异常：" + evtEx,
                    LogHelper.LogType.Error);
            }
        }

        void RefreshSlidesCount()
        {
            int newCount = 0;
            object pres = null;
            bool readFailed = false;
            try
            {
                if (_comApplication != null)
                {
                    dynamic app = _comApplication;
                    pres = app.ActivePresentation;
                    if (pres != null) newCount = (int)((dynamic)pres).Slides.Count;
                }
            }
            catch (Exception ex)
            {
                // LTSC（2026-11-19，班测报告「联动久了翻页组件消失」）：
                //
                // 这里原来写的是 newCount = 0，然后无条件 SlidesCount = newCount。
                // 等于把「读不到」当成「这份 PPT 有 0 页」。
                //
                // 后果链：
                //   一次 COM 超时 → SlidesCount=0 → PPTUIManager 里
                //   hasValidPageCount=false → HideAllNavigationPanels()
                //   → 翻页栏消失。而重显示要等一次新的 SlideShowBegin，
                //   COM 事件又已经死了（见 HookComEvents 的 RETRYLATER），
                //   于是整节课再也回不来。
                //
                // 读不到不是 0 页。保留上一次的有效值，什么都不写。
                // 而且必须用 Warning 记——这条以前只有 VerboseLog，
                // 详细日志开关默认关，整条失败过程一个字都不留。
                readFailed = true;
                LogHelper.WriteLogToFile(
                    $"PptBridge 读取总页数失败，保留上次值 {SlidesCount}：" +
                    $"{ex.GetType().Name}: {ex.Message}",
                    LogHelper.LogType.Warning);
            }

            if (readFailed)
            {
                SafeRelease(pres);
                return;
            }

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
                try
                {
                    PresentationClose?.Invoke(null);
                }
                catch (Exception evtEx)
                {
                    // LTSC（2026-11-17）：原来这里是 catch { }。
                    // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                    // 但日志里什么都没有。事件派发失败必须可见。
                    LogHelper.WriteLogToFile(
                        "PPT 事件 PresentationClose 的订阅方抛异常：" + evtEx,
                        LogHelper.LogType.Error);
                }
            }
            else if (oldCount <= 0 && newCount > 0)
            {
                // LTSC（2026-11-19，班测报告「联动久了翻页组件消失」）：
                //
                // 页数从「无效」恢复到「有效」，但翻页栏这时可能还是隐藏的。
                //
                // 为什么必须在这里叫一声：HideAllNavigationPanels() 是单向的，
                // PPTUIManager.UpdateNavigationPanelsVisibility() 只在
                // 「放映开始」分支里被重新调用。而放映开始只会触发一次
                // （状态从 false→true），此后 SlidesCount 就算恢复成有效值，
                // 也没有任何人去重新评估可见性 —— 翻页栏要等下一节课才回来。
                //
                // 这条以前不存在，是因为旧代码根本不会从 0 恢复：
                // 读失败写 0 之后只会一直失败下去。修掉那个之后，
                // 恢复路径必须补上，否则只是把「永远不会恢复」换了个位置。
                try
                {
                    SlidesCountRecovered?.Invoke(newCount);
                }
                catch (Exception evtEx)
                {
                    LogHelper.WriteLogToFile(
                        "PPT 事件 SlidesCountRecovered 的订阅方抛异常：" + evtEx,
                        LogHelper.LogType.Error);
                }
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

        /// <summary>按类名 screenClass 找当前可见的放映窗口句柄。</summary>
        IntPtr FindSlideShowWindowHandle()
        {
            IntPtr found = IntPtr.Zero;
            try
            {
                EnumWindows((h, l) =>
                {
                    if (!IsWindowVisible(h)) return true;
                    var sb = new StringBuilder(128);
                    GetClassNameW(h, sb, sb.Capacity);
                    if (sb.ToString() == SlideShowWindowClass)
                    {
                        found = h;
                        return false;
                    }
                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"查找放映窗口失败: {ex.Message}", LogHelper.LogType.Warning);
            }
            return found;
        }

        [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
        [DllImport("user32.dll")] static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);
        /// <summary>虚拟键码：G。</summary>
        const byte VkG = 0x47;
        /// <summary>keybd_event 的 KEYEVENTF_KEYUP。</summary>
        const uint KeyUp = 0x0002;

        /// <summary>
        /// 给当前前台窗口发一次 G。
        /// 放映态下 G = 全套幻灯片缩略图网格（2026-11-12 实测确认）。
        /// 别再改成 ESC（退出放映）或 Ctrl+S（下拉对话框）。
        /// </summary>
        static void SendKeyG()
        {
            keybd_event(VkG, 0, 0, UIntPtr.Zero);
            System.Threading.Thread.Sleep(40);
            keybd_event(VkG, 0, KeyUp, UIntPtr.Zero);
        }

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
            // LTSC P2：原来失败时返回 0，但页码从 1 开始——
            // 调用方拿到 0 分不清「第 0 页」和「不知道」。
            // 而且 _currentSlide 初值是 -1，一旦被重置就再也不刷新：
            // OnComSlideShowBegin 只把 _currentSlide 设成 -1，从不填真实页码。
            //
            // 改成：失败统一返回 -1（含义明确是"未知"），
            // 并且每次都尝试实时读一次，不再无条件相信可能过期的缓存。
            int live = ReadLiveSlideNumber();
            if (live > 0)
            {
                if (live != _currentSlide)
                {
                    _currentSlide = live;
                    VerboseLog($"[PPT详细] 页码实时读取={live}");
                }
                return live;
            }

            // 实时读不到就退回缓存，但缓存也得是真值
            if (_currentSlide > 0) return _currentSlide;

            VerboseLog($"[PPT详细] 页码未知（实时读取失败 live={live} 缓存={_currentSlide}）");
            return -1;
        }

        /// <summary>
        /// 实时读当前页码，不走缓存。读不到返回 -1。
        /// 一级 CurrentShowPosition，二级 View.Slide.SlideIndex。
        /// </summary>
        int ReadLiveSlideNumber()
        {
            if (TryGetView(out dynamic view))
            {
                try
                {
                    int pos = (int)view.CurrentShowPosition;
                    if (pos > 0) return pos;
                }
                catch { }
            }
            return TryGetSlideIndexFallback();
        }

        /// <summary>
        /// 取当前演示文稿名。取不到返回 null。
        /// </summary>
        /// <remarks>
        /// LTSC P1：原实现用 <c>dynamic pres = app.ActivePresentation</c>，
        /// 每调一次造一个 RCW 且从不归还——和 L-8 修的是同一类泄漏。
        /// 改成走 GetCurrentActivePresentation()（调用方持有）+ 显式 SafeRelease。
        /// </remarks>
        /// <summary>
        /// 取当前演示文稿的完整路径。失败返回 null。
        ///
        /// LTSC（2026-11-20）：抽出来给「放映结束」事件用。
        /// 保存笔迹要用这个路径算文件夹哈希 —— 拿不到就只能用名字算，
        /// 算出来的文件夹名和读侧对不上，笔迹就存进了一个没人读的地方。
        /// </summary>
        public string GetPresentationFullNameSafe()
        {
            object pres = GetCurrentActivePresentation();
            if (pres == null) return null;
            try
            {
                return (string)pres.GetType().InvokeMember("FullName",
                    System.Reflection.BindingFlags.GetProperty, null, pres, null);
            }
            catch { return null; }
            finally { SafeRelease(pres); }
        }

        public string GetPresentationName()
        {
            object pres = GetCurrentActivePresentation();
            if (pres == null) return null;
            try
            {
                return (string)pres.GetType().InvokeMember("Name",
                    System.Reflection.BindingFlags.GetProperty, null, pres, null);
            }
            catch { return null; }
            finally { SafeRelease(pres); }
        }

        /// <summary>
        /// 取当前演示文稿的完整文件路径。取不到返回 null。
        /// </summary>
        /// <remarks>
        /// LTSC P1：旧代码三处用 <c>pptApplication.SlideShowWindows[1].Presentation.FullName</c>
        /// 取路径，那是「放映窗口的演示文稿」；而 GetPresentationName() 用的是
        /// ActivePresentation。放映中有隐藏页、或同时开了多个演示文稿时，两者会不一致
        /// ——同一次保存里名字和路径来自不同文档，恢复时按路径比对就会认错文件。
        /// 这里统一走 ActivePresentation，和 GetPresentationName() 同源。
        /// </remarks>
        public string GetPresentationPath()
        {
            object pres = GetCurrentActivePresentation();
            if (pres == null) return null;
            try
            {
                return (string)pres.GetType().InvokeMember("FullName",
                    System.Reflection.BindingFlags.GetProperty, null, pres, null);
            }
            catch { return null; }
            finally { SafeRelease(pres); }
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
                // LTSC P1：dynamic 每取一次成员造一个 RCW。这里 pres 用完必须还，
                // 否则每次「开始放映」都留一份引用，PowerPoint 就一直退不掉。
                object pres = GetCurrentActivePresentation();
                if (pres == null) return false;
                try
                {
                    object settings = pres.GetType().InvokeMember("SlideShowSettings",
                        System.Reflection.BindingFlags.GetProperty, null, pres, null);
                    if (settings == null) return false;
                    try
                    {
                        settings.GetType().InvokeMember("Run",
                            System.Reflection.BindingFlags.InvokeMethod, null, settings, null);
                        return true;
                    }
                    finally { SafeRelease(settings); }
                }
                finally { SafeRelease(pres); }
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

        /// <summary>
        /// 调出 PowerPoint 原版的幻灯片导航面板（放映时点页码要的效果）。
        ///
        /// LTSC P1c：原来这个方法被实现成 TryNavigateNext()，
        /// 于是"点页码"变成了"翻到下一页"，面板从来没出现过。
        ///
        /// PPT 放映态下对应的操作是 SlideShowView.GotoNamedShow 之外的
        /// "显示幻灯片导航"：等价于人手动按右键菜单里的"查看所有幻灯片"。
        /// COM 里没有直接 API，可靠做法是给放映窗口发键盘/鼠标消息，
        /// 让 PowerPoint 自己弹它自己的导航。
        /// </summary>
        public bool TryShowSlideNavigation()
        {
            try
            {
                IntPtr hwnd = FindSlideShowWindowHandle();

                if (hwnd == IntPtr.Zero)
                {
                    LogHelper.WriteLogToFile("调出PPT导航失败：找不到放映窗口", LogHelper.LogType.Warning);
                    return false;
                }

                // PowerPoint 放映态下"显示幻灯片导航"没有公开 COM 入口，
                // 只能用它的键盘快捷键。
                //
                // 放映态下按 G = 全套幻灯片缩略图网格（就是"查看所有幻灯片"那种预览）。
                // 2026-11-12 实测确认：按 G 后整屏铺开 53 页缩略图、带页码、左上角有退出按钮。
                //
                // 历史错误（都别再犯）：
                //   ESC   —— 放映态是"退出放映"，会把放映退掉
                //   Ctrl+S —— 弹的是"所有幻灯片"下拉对话框，不是缩略图网格
                SetForegroundWindow(hwnd);
                System.Threading.Thread.Sleep(80);
                SendKeyG();
                System.Threading.Thread.Sleep(150);
                LogHelper.WriteLogToFile("已请求 PowerPoint 幻灯片缩略图网格（G）", LogHelper.LogType.Event);
                return true;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"调出PPT导航异常: {ex.Message}", LogHelper.LogType.Error);
                return false;
            }
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

        // LTSC（2026-11-20，用户报「PowerPoint 被劫持了，pptcom 都没被释放」）：
        //
        // 这五个委托必须**存成字段**，不能每次 new。
        //
        // 原来的写法：
        //     d.PresentationOpen += new Action<object>(OnComPresentationOpen);
        //     d.PresentationOpen -= new Action<object>(OnComPresentationOpen);
        //
        // 对 dynamic COM 事件来说，-= 会走 IDispatch 的 remove_XXX，
        // 传进去的是**另一个新建的委托实例**，COM 那边认不出来，
        // 于是根本解不掉。
        //
        // 后果：每次重连都在原有钩子上再叠一层，钩子越积越多。
        // PowerPoint 认为一直有外部程序连着，关窗口后进程退不掉，
        // 也就打不开新的 PPT（用户看到的「被劫持」）。
        //
        // 用固定实例加减，才是同一个引用。
        Action<object> _onSlideShowBeginAction;
        Action<object> _onNextSlideAction;
        Action<object> _onSlideShowEndAction;
        Action<object> _onPresentationOpenAction;
        Action<object> _onPresentationCloseAction;

        /// <summary>
        /// 初始化那五个事件委托。只做一次，之后加/减都用同一个实例。
        /// </summary>
        void EnsureEventActions()
        {
            if (_onSlideShowBeginAction != null) return;
            _onSlideShowBeginAction = new Action<object>(OnComSlideShowBegin);
            _onNextSlideAction = new Action<object>(OnComNextSlide);
            _onSlideShowEndAction = new Action<object>(OnComSlideShowEnd);
            _onPresentationOpenAction = new Action<object>(OnComPresentationOpen);
            _onPresentationCloseAction = new Action<object>(OnComPresentationClose);
        }

        void HookComEvents(object app)
        {
            try
            {
                EnsureEventActions();
                dynamic d = app;
                d.SlideShowBegin += _onSlideShowBeginAction;
                d.SlideShowNextSlide += _onNextSlideAction;
                d.SlideShowEnd += _onSlideShowEndAction;
                d.PresentationOpen += _onPresentationOpenAction;
                d.PresentationClose += _onPresentationCloseAction;
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
                d.SlideShowBegin -= _onSlideShowBeginAction;
                d.SlideShowNextSlide -= _onNextSlideAction;
                d.SlideShowEnd -= _onSlideShowEndAction;
                d.PresentationOpen -= _onPresentationOpenAction;
                d.PresentationClose -= _onPresentationCloseAction;
            }
            catch (Exception ex)
            {
                VerboseLog($"[PPT详细] 解绑 COM 事件失败: {ex.GetType().Name}: {ex.Message}");
            }
            _comEventsHooked = false;
        }

        void OnComSlideShowBegin(object window)
        {
            _comSlideShowWindow = window;
            // LTSC P2：原来这里只置 -1 就完事，等于把页码清空后不再填。
            // 而 GetCurrentSlideNumber 又优先信缓存，于是整场放映页码都是空的。
            // 现在立刻读一次真实页码。
            _currentSlide = -1;
            try
            {
                int live = ReadLiveSlideNumber();
                if (live > 0) _currentSlide = live;
                VerboseLog($"COM 事件: 放映开始 页码={_currentSlide}");
            }
            catch (Exception ex)
            {
                VerboseLog($"COM 事件: 放映开始取页码失败 {ex.GetType().Name}: {ex.Message}");
            }
        }

        void OnComNextSlide(object window)
        {
            _comSlideShowWindow = window;
            // 翻页事件是页码最可靠的来源，优先读它。
            // COM 事件触发时窗口一定有效，比 500ms 轮询准得多。
            int prev = _currentSlide;
            try
            {
                int live = ReadLiveSlideNumber();
                if (live > 0) _currentSlide = live;
            }
            catch { }
            RefreshSlidesCount();
            VerboseLog($"COM 事件: 翻页 -> 当前页={_currentSlide}");

            // LTSC P1b（2026-11-11 实测）：这里原来只更新 _currentSlide 就结束了，
            // 从不把 SlideShowNextSlide 抛给订阅者。
            // 而轮询路径 RefreshSlideShowState(:819) 的触发条件是
            //   comSlide != _currentSlide
            // COM 翻页时 _currentSlide 已经被这里改成新页码，轮询再读到同一页就不成立，
            // 于是 SlideShowNextSlide 一次都发不出去。
            // 下游 MW_PPT.OnPPTSlideShowNextSlide 因此从不执行，页码、墨迹切页、导航栏
            // 全都停在第一页——实机现象就是「翻页器始终显示 1」。
            //
            // 修法：COM 翻页确认页码真的变了，就在这里自己把事件抛出去，
            // 不依赖轮询那条路（它只在 COM 事件缺失时才该兜底）。
            if (_currentSlide > 0 && _currentSlide != prev)
            {
                try
                {
                    SlideShowNextSlide?.Invoke(_comSlideShowWindow);
                }
                catch (Exception evtEx)
                {
                    // LTSC（2026-11-17）：原来这里是 catch { }。
                    // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                    // 但日志里什么都没有。事件派发失败必须可见。
                    LogHelper.WriteLogToFile(
                        "PPT 事件 SlideShowNextSlide 的订阅方抛异常：" + evtEx,
                        LogHelper.LogType.Error);
                }
            }
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

            // LTSC（2026-11-20）：打开时就把路径记下来，以后一律用缓存。
            //
            // 为什么：保存笔迹要用路径算文件夹哈希，而保存发生在「退出放映」时。
            // 那时候 PPT 正在拆放映窗口，再去问 COM 要 ActivePresentation
            // 是不安全的 —— 会拖住一个 COM 引用，PowerPoint 就退不掉（被劫持）。
            //
            // 打开演示文稿时 PPT 是空闲的，这一刻取路径最稳。
            try
            {
                if (pres != null)
                {
                    string fn = (string)pres.GetType().InvokeMember("FullName",
                        System.Reflection.BindingFlags.GetProperty, null, pres, null);
                    if (!string.IsNullOrEmpty(fn))
                    {
                        _cachedPresentationFullName = fn;
                        VerboseLog($"记住演示文稿路径：{fn}");
                    }
                }
            }
            catch (Exception cacheEx)
            {
                VerboseLog("记路径失败: " + cacheEx.Message);
            }

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
            // LTSC（2026-11-20，用户报「PowerPoint 被劫持了，pptcom 都没被释放」）：
            //
            // 这里**先解绑钩子再干别的**。原因：
            //   我们在 PowerPoint.Application 上挂了 5 个 COM 事件，
            //   只要还有一个事件订阅在，PPT 就认为「还有外部程序连着它」，
            //   用户关窗口时会退不掉，进程留在后台（就是用户看到的「被劫持」）。
            //
            //   正常情况下 UnhookComEvents 只在 Disconnect 里调，
            //   而 Disconnect 的条件是「PPT 进程已经死了」—— 死循环：
            //   PPT 活着 → 不解绑 → PPT 退不掉 → 进程一直活着 → 永远不解绑。
            //
            //   演示文稿关闭是用户要退出 PPT 的明确信号，在这里解绑正好。
            try
            {
                UnhookComEvents();
                ReleaseCom();
                _inSlideShow = false;
                _pendingInShow = false;
                _pendingInShowTicks = 0;
                _currentSlide = -1;
                SlideShowBounds = System.Drawing.Rectangle.Empty;
                LogHelper.WriteLogToFile("[PPT] 演示文稿已关闭，已解绑 COM 钩子并释放引用（否则 PowerPoint 退不掉）",
                    LogHelper.LogType.Event);
            }
            catch (Exception releaseEx)
            {
                LogHelper.WriteLogToFile("[PPT] 演示文稿关闭时释放 COM 失败: " + releaseEx.Message,
                    LogHelper.LogType.Warning);
            }

            // 演示文稿关闭了，缓存的路径就没意义了。不清的话，
            // 下次打开另一份 PPT 却还没触发 PresentationOpen 时，
            // 会拿旧路径去算哈希，又写错文件夹。
            _cachedPresentationFullName = null;

            // RefreshSlidesCount 会访问 COM，而上面已经释放了，
            // 所以这里不能再调 —— 挪到解绑之前也不合适（那时 ppt 正在关）。
            // 页数由下一次连接时的 RefreshSlidesCount 负责。

            if (_hasPresentation)
            {
                _hasPresentation = false;
                try
                {
                    PresentationClose?.Invoke(pres);
                }
                catch (Exception evtEx)
                {
                    // LTSC（2026-11-17）：原来这里是 catch { }。
                    // 订阅方抛异常被完全吞掉——表现是翻页组件不出现、页码不动，
                    // 但日志里什么都没有。事件派发失败必须可见。
                    LogHelper.WriteLogToFile(
                        "PPT 事件 PresentationClose 的订阅方抛异常：" + evtEx,
                        LogHelper.LogType.Error);
                }
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

            // 释放后进冷却期，轮询不要在下一个 tick 就把钩子重新挂上。
            // 不设这个的话，用户关演示文稿后我们解绑了，0.3 秒后又连上，
            // PowerPoint 仍然退不掉（实测要等 21 秒超时）。
            _comReleaseCooldownUntil = DateTime.UtcNow + ComReleaseCooldown;
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

        /// <summary>
        /// 主动取一次放映窗口对象（SlideShowWindow），成功返回并保有引用，失败返回 null。
        /// </summary>
        /// <remarks>
        /// LTSC P1：轮询兜底路径没有 COM 事件送来的 window，必须自己取一次，
        /// 否则订阅方拿到的 payload 是 null，页码永远是 0。
        /// </remarks>
        object TryFetchSlideShowWindow()
        {
            if (_comApplication == null) return null;
            object windows = null;
            try
            {
                windows = _comApplication.GetType().InvokeMember("SlideShowWindows",
                    System.Reflection.BindingFlags.GetProperty, null, _comApplication, null);
                if (windows == null) return null;
                int count = Convert.ToInt32(windows.GetType().InvokeMember("Count",
                    System.Reflection.BindingFlags.GetProperty, null, windows, null));
                if (count <= 0) return null;
                return windows.GetType().InvokeMember("Item",
                    System.Reflection.BindingFlags.GetProperty, null, windows, new object[] { 1 });
            }
            catch (Exception ex)
            {
                VerboseLog($"[PPT详细] 补取放映窗口失败: {ex.GetType().Name}: {ex.Message}");
                return null;
            }
            finally
            {
                SafeRelease(windows);
            }
        }

        /// <summary>
        /// 从 View 对象读当前放映页码。失败返回 -1。
        /// </summary>
        /// <remarks>
        /// LTSC P1b（2026-11-11 实机实测）：原来这里只读 <c>View.CurrentShowPosition</c>，
        /// 在 MS PowerPoint 16.0 上稳定抛 <c>0x80020003 DISP_E_MEMBERNOTFOUND</c>——
        /// 该成员在 Slide 的 View 对象上不存在，每 500ms 刷一条日志，一直取不到页码。
        /// 同期日志显示 <c>View.Slide.SlideIndex</c> 这条路是通的（它原本只当二级兜底用）。
        ///
        /// 参考 Inkeys3 的 PptCOM.GetCurrentSlideIndex：它读的也是 <c>view.Slide.SlideIndex</c>。
        ///
        /// 现在的顺序：先 Slide.SlideIndex（Office 标准路径），失败再试 CurrentShowPosition
        /// （WPS / 老版本可能只有后者）。两条都不通才返回 -1，交给上层兜底。
        /// </remarks>
        int ReadShowPosition(object view)
        {
            if (view == null) return -1;

            // 一级：view.Slide.SlideIndex —— Office 标准路径，实测可用
            object slide = null;
            try
            {
                slide = view.GetType().InvokeMember("Slide",
                    System.Reflection.BindingFlags.GetProperty, null, view, null);
                if (slide != null)
                {
                    object idx = slide.GetType().InvokeMember("SlideIndex",
                        System.Reflection.BindingFlags.GetProperty, null, slide, null);
                    int v = Convert.ToInt32(idx);
                    if (v > 0) return v;
                }
            }
            catch (Exception ex)
            {
                VerboseLog($"[PPT详细] View.Slide.SlideIndex 读取失败: {ex.GetType().Name}: {ex.Message}");
            }
            finally
            {
                SafeRelease(slide);
            }

            // 二级：view.CurrentShowPosition —— WPS / 老版本可能只有这个
            try
            {
                object pos = view.GetType().InvokeMember("CurrentShowPosition",
                    System.Reflection.BindingFlags.GetProperty, null, view, null);
                int v2 = Convert.ToInt32(pos);
                if (v2 > 0) return v2;
            }
            catch (Exception ex)
            {
                VerboseLog($"[PPT详细] View.CurrentShowPosition 读取失败: {ex.GetType().Name}: {ex.Message}");
            }

            return -1;
        }

        /// <summary>
        /// 二级兜底：用 ActivePresentation.SlideShowWindow.View.Slide.SlideIndex 取页码。
        /// </summary>
        /// <remarks>
        /// LTSC P1：历史日志里 comSlide 全程 -1，翻页组件因此从不出现。
        /// 一级路径走 SlideShowWindows[1].View.CurrentShowPosition，
        /// 某些情况下（放映窗口刚建立、WPS、跨权限）这一级取不到；
        /// 退到 ActivePresentation 这条路往往还能拿到。
        /// 三级（Presentation.Slides 里定位当前页）留作最后手段。
        /// </remarks>
        int TryGetSlideIndexFallback()
        {
            object pres = GetCurrentActivePresentation();
            if (pres != null)
            {
                object window = null;
                object view = null;
                object slide = null;
                try
                {
                    window = pres.GetType().InvokeMember("SlideShowWindow",
                        System.Reflection.BindingFlags.GetProperty, null, pres, null);
                    if (window != null)
                    {
                        view = window.GetType().InvokeMember("View",
                            System.Reflection.BindingFlags.GetProperty, null, window, null);
                        if (view != null)
                        {
                            slide = view.GetType().InvokeMember("Slide",
                                System.Reflection.BindingFlags.GetProperty, null, view, null);
                            if (slide != null)
                            {
                                object idx = slide.GetType().InvokeMember("SlideIndex",
                                    System.Reflection.BindingFlags.GetProperty, null, slide, null);
                                int v = Convert.ToInt32(idx);
                                if (v > 0)
                                {
                                    VerboseLog($"[PPT详细] 二级兜底拿到页码: {v}");
                                    return v;
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    VerboseLog($"[PPT详细] 二级兜底失败: {ex.GetType().Name}: {ex.Message}");
                }
                finally
                {
                    SafeRelease(slide);
                    SafeRelease(view);
                    SafeRelease(window);
                    SafeRelease(pres);
                }
            }
            return -1;
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

