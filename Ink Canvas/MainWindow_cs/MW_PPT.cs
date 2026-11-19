using Ink_Canvas.Helpers;
using Ink_Canvas.WorkflowAutomation;
using InkCanvasPPTAgent.Contracts;
using iNKORE.UI.WPF.Modern;
using Microsoft.Office.Core;
using Microsoft.Office.Interop.PowerPoint;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Ink;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Application = System.Windows.Application;
using File = System.IO.File;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        #region Win32 API Declarations
        [DllImport("user32.dll")]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hWnd);

        [StructLayout(LayoutKind.Sequential)]
        private struct RECT
        {
            public int Left, Top, Right, Bottom;
        }

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsZoomed(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern IntPtr GetWindow(IntPtr hWnd, uint uCmd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out ForegroundWindowInfo.RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        private const int GWL_STYLE = -16;
        private const int WS_VISIBLE = 0x10000000;
        private const int WS_MINIMIZE = 0x20000000;
        private const uint GW_HWNDNEXT = 2;
        private const uint GW_HWNDPREV = 3;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);
        #endregion

        #region PPT Application Variables
        /// <summary>
        /// 当前活动的PowerPoint演示文稿。
        /// </summary>
        public static Presentation presentation;

        /// <summary>
        /// 当前演示文稿的幻灯片集合。
        /// </summary>
        public static Slides slides;

        /// <summary>
        /// 当前活动的幻灯片。
        /// </summary>
        public static Slide slide;

        /// <summary>
        /// 当前演示文稿的幻灯片总数。
        /// </summary>
        public static int slidescount;
        #endregion

        #region PPT State Management
        /// <summary>
        /// 幻灯片放映结束事件重入保护标志，防止重复处理放映结束事件。
        /// </summary>
        private bool isEnteredSlideShowEndEvent;

        /// <summary>
        /// 演示文稿是否有黑边的指示标志。
        /// </summary>
        private bool isPresentationHaveBlackSpace;

        // 长按翻页相关字段
        /// <summary>
        /// 用于处理长按翻页功能的定时器。
        /// </summary>
        private DispatcherTimer _longPressTimer;

        /// <summary>
        /// 长按翻页方向标志，true表示下一页，false表示上一页。
        /// </summary>

        /// <summary>
        /// 长按延迟时间（毫秒），即用户需要按住按钮多长时间才开始连续翻页。
        /// </summary>
        private const int LongPressDelay = 500; // 长按延迟时间（毫秒）

        /// <summary>
        /// 长按翻页间隔（毫秒），即连续翻页的时间间隔。
        /// </summary>
        private const int LongPressInterval = 50; // 长按翻页间隔（毫秒）

        // PowerPoint应用程序守护相关字段
        // 上次播放位置相关字段
        /// <summary>
        /// 上次播放的幻灯片页码。
        /// </summary>
        private int _lastPlaybackPage = 0;

        /// <summary>
        /// 是否应该导航到上次播放页码的标志。
        /// </summary>
        private bool _shouldNavigateToLastPage = false;

        // 当前播放页码跟踪
        /// <summary>
        /// 当前幻灯片放映的位置（页码）。
        /// </summary>
        private int _currentSlideShowPosition = 0;

        /// <summary>
        /// 当前幻灯片放映位置的公开访问器（0-based）。
        /// 用于小白板等组件获取当前PPT页码。
        /// </summary>
        internal int CurrentPPTSlideIndex => _currentSlideShowPosition > 0 ? _currentSlideShowPosition - 1 : 0;

        private Dictionary<int, MemoryStream> _memoryStreams = new Dictionary<int, MemoryStream>();
        private readonly object _pptEnhancedPreviewCacheLock = new object();
        private List<PPTEnhancedPreviewItem> _pptEnhancedPreviewCache;
        private Task<List<PPTEnhancedPreviewItem>> _pptEnhancedPreviewBuildTask;
        private CancellationTokenSource _pptEnhancedPreviewCacheCts;
        private int _pptEnhancedPreviewCacheGeneration;
        private const int PPTEnhancedPreviewPreloadDelayMs = 100;
        private int _previousSlideID = 0;

        /// <summary>
        /// 用于在PowerPoint连接断开后延迟退出PPT模式的定时器。
        /// </summary>
        private DispatcherTimer _exitPPTModeAfterDisconnectTimer;

        /// <summary>
        /// 断开连接后退出PPT模式的延迟时间（毫秒），即连接断开后多长时间才退出PPT模式。
        /// </summary>
        private const int ExitPPTModeAfterDisconnectDelayMs = 1200;

        /// <summary>
        /// 仅PPT模式下周期性探测放映界面（COM 失效时依赖 Win32），间隔不宜过小以免多余开销。
        /// </summary>
        private DispatcherTimer _pptOnlyVisibilityProbeTimer;

        private const int PPTOnlyVisibilityProbeIntervalMs = 800;

        /// <summary>
        /// PowerPoint 全屏放映顶层窗口类名（与编辑态 PPTFrameClass 区分）。
        /// </summary>
        private const string PowerPointSlideShowWindowClassName = "screenClass";

        #endregion

        #region PPT Managers
        /// <summary>
        /// PPT链接管理器，用于管理与PowerPoint的连接和事件处理。
        /// </summary>
        private IPPTLinkManager _pptManager;

        /// <summary>
        /// PPT墨迹管理器，用于管理PowerPoint幻灯片上的墨迹。
        /// </summary>
        private PPTInkManager _singlePPTInkManager;

        /// <summary>
        /// PPT UI管理器，用于管理与PowerPoint相关的用户界面元素。
        /// </summary>
        private PPTUIManager _pptUIManager;

        /// <summary>
        /// 获取PPT管理器实例
        /// </summary>
        /// <remarks>
        /// 提供对内部PPT链接管理器的公共访问，用于外部代码与PowerPoint进行交互。
        /// </remarks>
        public IPPTLinkManager PPTManager => _pptManager;
        public PPTUIManager PPTUIManager => _pptUIManager;
        #endregion

        #region PPT Manager Initialization
        /// <summary>
        /// 初始化并配置用于 PowerPoint 集成的管理器与相关状态。
        /// </summary>
        /// <remarks>
        /// 清理并释放现有的 PPT 管理器与 COM/Interop 状态，创建并配置新的 PPT 管理器（ROT 或 COM 实现，取决于设置）、单一的 PPT 墨迹管理器及其自动保存行为，以及 PPT UI 管理器与其显示/按钮位置选项。方法内部会订阅必要的 PPT 事件并记录初始化过程中的错误或警告。同时初始化长按页翻页定时器以支持长按翻页功能。
        /// </remarks>
        public void InitializePPTManagers()
        {
            try
            {
                // 初始化长按定时器
                InitializeLongPressTimer();
                WirePPTNavBars();

                // 完全清理旧模式
                try
                {
                    _pptManager?.StopMonitoring();
                    _pptManager?.Dispose();
                    _pptManager = null;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"清理旧 PPT 管理器异常: {ex}", LogHelper.LogType.Warning);
                }

                try
                {
                    StopPowerPointProcessMonitoring();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"清理 PPT 联动状态异常: {ex}", LogHelper.LogType.Warning);
                }

                // LTSC D-02c: PPT 联动统一到 PptBridge 一套实现。
                //
                // 旧的三套（PPTManager 2434 行 / ROTPPTManager 3070 行 / ComPPTLinkManager 112 行）
                // 并行实现同一件事，且全部依赖 COM——在「CC 提权 + PPT 普通权限」时一起失效。
                // PptBridge 用 ROT 按 .pptx 路径匹配取对象、Win32 兜底拿放映窗口，
                // 跨权限时至少还能把白板贴到放映窗口上。
                //
                // PPTLinkMode 枚举保留但不再分支：老配置反序列化得到任何值都走同一条路。
                // 这是刻意的——三套实现已经证明没有一套比另一套更强。
                //
                // LTSC P1-10：老配置文件里 PPTLinkMode 可能是 1(Rot) 或 2(Agent)。
                // 读进来忽略它、不报错，但要留一条日志——否则用户看到设置里
                // 「架构」选项没了，会以为是配置丢了。
                try
                {
                    var legacyMode = Settings.PowerPointSettings.PPTLinkMode;
                    if (legacyMode != PPTLinkMode.Com)
                    {
                        LogHelper.WriteLogToFile(
                            $"老配置里的 PPTLinkMode={legacyMode} 已废弃，统一走 PptBridge(ROT)，不影响使用",
                            LogHelper.LogType.Event);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"检查旧 PPTLinkMode 失败: {ex.Message}", LogHelper.LogType.Warning);
                }

                _pptManager = new PptBridge();

                _pptManager.IsSupportWPS = Settings.PowerPointSettings.IsSupportWPS;
                _pptManager.SkipAnimationsWhenNavigating = Settings.PowerPointSettings.SkipAnimationsWhenGoNext;

                // 注册事件
                _pptManager.PPTConnectionChanged += OnPPTConnectionChanged;
                _pptManager.SlideShowBegin += OnPPTSlideShowBegin;
                _pptManager.SlideShowNextSlide += OnPPTSlideShowNextSlide;
                _pptManager.SlideShowEnd += OnPPTSlideShowEnd;
                _pptManager.PresentationOpen += OnPPTPresentationOpen;
                _pptManager.PresentationClose += OnPPTPresentationClose;
                _pptManager.SlideShowStateChanged += OnPPTSlideShowStateChanged;
                _pptManager.PermissionBlocked += OnPPTPermissionBlocked;
                // LTSC（2026-11-19）：总页数从无效恢复到有效时重新评估翻页栏可见性。
                // 翻页栏的显示闸门里有 hasValidPageCount，一旦页数掉到 0 就被
                // HideAllNavigationPanels 隐藏，而隐藏是单向的——重新显示只发生在
                // 「放映开始」分支。页数恢复后必须有人再叫一次，否则要等下次放映。
                _pptManager.SlidesCountRecovered += OnPPTSlidesCountRecovered;

                _singlePPTInkManager = new PPTInkManager();
                _singlePPTInkManager.IsAutoSaveEnabled = Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint;
                _singlePPTInkManager.AutoSaveLocation = Settings.Automation.AutoSavedStrokesLocation;

                // 初始化UI管理器
                _pptUIManager = new PPTUIManager(this);
                _pptUIManager.ShowPPTButton = Settings.PowerPointSettings.ShowPPTButton;
                _pptUIManager.PPTButtonsDisplayOption = Settings.PowerPointSettings.PPTButtonsDisplayOption;
                _pptUIManager.PPTSButtonsOption = Settings.PowerPointSettings.PPTSButtonsOption;
                _pptUIManager.PPTBButtonsOption = Settings.PowerPointSettings.PPTBButtonsOption;
                _pptUIManager.PPTLSButtonPosition = Settings.PowerPointSettings.PPTLSButtonPosition;
                _pptUIManager.PPTRSButtonPosition = Settings.PowerPointSettings.PPTRSButtonPosition;
                _pptUIManager.PPTLBButtonPosition = Settings.PowerPointSettings.PPTLBButtonPosition;
                _pptUIManager.PPTRBButtonPosition = Settings.PowerPointSettings.PPTRBButtonPosition;
                _pptUIManager.EnablePPTButtonPageClickable = Settings.PowerPointSettings.EnablePPTButtonPageClickable;
                ApplyPPTNavBarAppearanceFromSettings();

                // 设置页改完立刻下发外观，不用等下次进入放映。
                PPTUIManager.AppearanceChanged -= ApplyPPTNavBarAppearanceFromSettings;
                PPTUIManager.AppearanceChanged += ApplyPPTNavBarAppearanceFromSettings;
                _pptUIManager.EnablePPTButtonLongPressPageTurn = Settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn;

                LogHelper.WriteLogToFile("PPT管理器初始化完成", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PPT管理器初始化失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 启动PPT监控：当PowerPoint支持功能启用时，启动PPT管理器的监控功能。
        /// </summary>
        /// <remarks>
        /// 只有当Settings.PowerPointSettings.PowerPointSupport为true时才会启动监控，并记录启动事件日志。
        /// </remarks>
        public void StartPPTMonitoring()
        {
            if (Settings.PowerPointSettings.PowerPointSupport)
            {
                _pptManager?.StartMonitoring();
                LogHelper.WriteLogToFile("PPT监控已启动", LogHelper.LogType.Event);
            }
        }

        /// <summary>
        /// 停止 PowerPoint 相关的监控：停止并清除用于延迟退出 PPT 模式的定时器，并停止 PPT 管理器的监控，同时记录事件日志。
        /// </summary>
        public void StopPPTMonitoring()
        {
            try
            {
                _exitPPTModeAfterDisconnectTimer?.Stop();
                _exitPPTModeAfterDisconnectTimer = null;
                ResetPPTEnhancedPreviewCache();
            }
            catch
            {
            }

            _pptManager?.StopMonitoring();
            LogHelper.WriteLogToFile("PPT监控已停止", LogHelper.LogType.Event);
        }

        #region PowerPoint Application Management
        /// <summary>
        /// 启动 PPT 联动监控。
        /// </summary>
        /// <remarks>
        /// LTSC P1：原实现会 new 一个 PowerPoint.Application、设 Visible=msoFalse、
        /// 再起 1 秒守护定时器。而 Visible=msoFalse 在 PPT 已有可见窗口时必抛
        /// 0x80048240（"Hiding the application window is not allowed"），
        /// catch 里又不置空字段，于是每次开关都失败一次、还顺带 Quit 掉老师的 PPT。
        ///
        /// 现在只剩一件事：让 PptBridge 自己走 ROT 找已经在跑的 PowerPoint。
        /// 不创建、不隐藏、不退出。
        /// </remarks>
        public void StartPowerPointProcessMonitoring()
        {
            try
            {
                _pptManager?.StartMonitoring();
                LogHelper.WriteLogToFile("PPT 联动监控已启动", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"启动 PPT 联动监控失败: {ex}", LogHelper.LogType.Error);
            }
        }
        /// <summary>
        /// 停止 PPT 联动监控。
        /// </summary>
        /// <remarks>
        /// LTSC P1：原实现会调用 ClosePowerPointApplication()，
        /// 那是 pptApplication.Quit() —— 直接退出 PowerPoint 进程，
        /// 老师的演示文稿跟着一起没了。现在只断开 PptBridge 的轮询与 COM 连接，
        /// 不碰 PowerPoint 本身。
        /// </remarks>
        public void StopPowerPointProcessMonitoring(bool isShutdown = false)
        {
            try
            {
                _pptManager?.StopMonitoring(isShutdown: isShutdown);
                LogHelper.WriteLogToFile("PPT 联动监控已停止", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"停止 PPT 联动监控失败: {ex}", LogHelper.LogType.Error);
            }
        }
        #endregion

        /// <summary>
        /// 释放并停止所有与 PowerPoint 集成相关的管理器与资源，恢复和清理应用的 PPT 相关运行状态。
        /// </summary>
        /// <remarks>
        /// 操作包括停止并释放 PPT 管理器、墨迹管理器和长按计时器，停止 PowerPoint 进程监控，关闭 PowerPoint 应用并清除静态 COM/互操作状态；所有异常会被捕获并记录为错误日志。
        /// </remarks>
        private void DisposePPTManagers(bool isShutdown = false)
        {
            try
            {
                if (_pptManager != null)
                {
                    _pptManager.StopMonitoring(isShutdown: isShutdown);
                    _pptManager.Dispose();
                    _pptManager = null;
                }

                _singlePPTInkManager?.Dispose();
                _singlePPTInkManager = null;

                _longPressTimer?.Stop();
                _longPressTimer = null;

                _pptUIManager = null;

                StopPowerPointProcessMonitoring(isShutdown);

                StopPPTOnlyVisibilityProbeTimer();

                LogHelper.WriteLogToFile("PPT管理器已释放", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"释放PPT管理器失败: {ex}", LogHelper.LogType.Error);
            }
        }

        internal void UnloadPPTModuleForShutdown()
        {
            try
            {
                try
                {
                    _longPressTimer?.Stop();
                    StopPPTOnlyVisibilityProbeTimer();
                    LogHelper.WriteLogToFile("关机时已停止所有 PPT 相关定时器", LogHelper.LogType.Event);
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"关机时停止定时器失败: {ex}", LogHelper.LogType.Warning);
                }

                // 再处理需要 dispatcher 或 COM 的清理操作
                if (Dispatcher == null || Dispatcher.CheckAccess())
                {
                    DisposePPTManagers(isShutdown: true);
                    return;
                }

                if (Dispatcher.HasShutdownStarted || Dispatcher.HasShutdownFinished)
                {
                    LogHelper.WriteLogToFile("关机时 Dispatcher 已关闭，跳过需要 UI 线程的清理操作", LogHelper.LogType.Warning);
                    return;
                }

                Dispatcher.Invoke(() => DisposePPTManagers(isShutdown: true), DispatcherPriority.Send);
            }
            catch (TaskCanceledException ex)
            {
                LogHelper.WriteLogToFile($"关机时卸载PPT模块被取消: {ex.Message}", LogHelper.LogType.Warning);
            }
            catch (ObjectDisposedException ex)
            {
                LogHelper.WriteLogToFile($"关机时卸载PPT模块对象已释放: {ex.Message}", LogHelper.LogType.Warning);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"关机时卸载PPT模块失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 初始化长按定时器
        /// </summary>
        private void InitializeLongPressTimer()
        {
            // LTSC A-4：这个方法由 InitializePPTManagers 调用，而 InitializePPTManagers
            // 有 6 个调用点（切换 PPT 模式、重连、设置变更等）。
            // 原实现每次直接 new 一个 DispatcherTimer 覆盖字段，
            // 旧实例既没 Stop 也没解绑 Tick，如果它正处于 Start 态，
            // 就会和新实例一起每 LongPressInterval 触发一次 —— 表现为「长按一下翻两页」。
            // 先停旧的、解绑、再建新的。
            if (_longPressTimer != null)
            {
                try
                {
                    _longPressTimer.Stop();
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"释放旧长按定时器异常: {ex.Message}", LogHelper.LogType.Warning);
                }
                _longPressTimer = null;
            }

            _longPressTimer = new DispatcherTimer();
            _longPressTimer.Interval = TimeSpan.FromMilliseconds(LongPressDelay);
        }

        /// <summary>
        /// 停止长按检测
        /// </summary>
        private void StopLongPressDetection()
        {
            if (_longPressTimer != null && _longPressTimer.IsEnabled)
            _longPressTimer?.Stop();
        }

        /// <summary>
        /// 长按定时器事件处理
        /// </summary>
        /// <summary>
        /// 把外观设置（页码字号 / 自动淡出 / 按下反馈）从 Settings 读进 PPTUIManager 并下发到四条导航栏。
        /// 初始化时调一次，每次进入放映再调一次——后者是为了让设置页改完立刻生效，
        /// 不用重启。
        /// </summary>
        private void ApplyPPTNavBarAppearanceFromSettings()
        {
            if (_pptUIManager == null) return;
            try
            {
                var ppt = Settings.PowerPointSettings;
                _pptUIManager.PageNumberFontSize = ppt.PPTPageNumberFontSize;
                _pptUIManager.PageTotalFontSize = ppt.PPTPageTotalFontSize;
                _pptUIManager.NavBarAutoHide = ppt.PPTNavBarAutoHide;
                _pptUIManager.NavBarAutoHideDelaySeconds = ppt.PPTNavBarAutoHideDelaySeconds;
                _pptUIManager.NavBarAutoHideOpacity = ppt.PPTNavBarAutoHideOpacity;
                _pptUIManager.ButtonPressFeedback = ppt.PPTButtonPressFeedback;
                _pptUIManager.ButtonPressFeedbackOpacity = ppt.PPTButtonPressFeedbackOpacity;
                _pptUIManager.ApplyAppearanceToAllBars();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"应用翻页栏外观设置失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void MarkAllNavBarsLongPressConsumed()
        {
            try
            {
                LeftBottomPanelForPPTNavigation?.MarkLongPressConsumed();
                RightBottomPanelForPPTNavigation?.MarkLongPressConsumed();
                LeftSidePanelForPPTNavigation?.MarkLongPressConsumed();
                RightSidePanelForPPTNavigation?.MarkLongPressConsumed();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"标记长按消费失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }
        #endregion

        #region 仅PPT模式可见性（COM + Win32 兜底）

        /// <summary>
        /// 在启用「仅PPT模式」时启动轻量探测，COM 事件延迟或失效时仍可根据全屏放映窗口显示主窗口。
        /// </summary>
        internal void EnsurePPTOnlyVisibilityProbeTimer()
        {
            try
            {
                if (!Settings.ModeSettings.IsPPTOnlyMode)
                {
                    StopPPTOnlyVisibilityProbeTimer();
                    return;
                }

                if (_pptOnlyVisibilityProbeTimer == null)
                {
                    _pptOnlyVisibilityProbeTimer = new DispatcherTimer
                    {
                        Interval = TimeSpan.FromMilliseconds(PPTOnlyVisibilityProbeIntervalMs)
                    };
                    _pptOnlyVisibilityProbeTimer.Tick += (_, __) => CheckMainWindowVisibility();
                }

                if (!_pptOnlyVisibilityProbeTimer.IsEnabled)
                    _pptOnlyVisibilityProbeTimer.Start();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"仅PPT可见性探测计时器启动失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        internal void StopPPTOnlyVisibilityProbeTimer()
        {
            try
            {
                _pptOnlyVisibilityProbeTimer?.Stop();
            }
            catch
            {
            }
        }

        /// <summary>
        /// 检测是否存在 PowerPoint 全屏放映顶层窗口（类名 screenClass，进程 powerpnt），用于 COM 不可用时的兜底。
        /// </summary>
        internal bool IsPowerPointSlideshowSurfacePresentWin32()
        {
            if (!Settings.ModeSettings.IsPPTOnlyMode)
                return false;

            try
            {
                bool found = false;
                EnumWindows((hWnd, _) =>
                {
                    if (!IsWindow(hWnd) || !IsWindowVisible(hWnd))
                        return true;

                    var cls = new StringBuilder(256);
                    if (GetClassName(hWnd, cls, cls.Capacity) == 0)
                        return true;

                    if (!string.Equals(cls.ToString(), PowerPointSlideShowWindowClassName, StringComparison.OrdinalIgnoreCase))
                        return true;

                    try
                    {
                        GetWindowThreadProcessId(hWnd, out uint pid);
                        using (var proc = Process.GetProcessById((int)pid))
                        {
                            var name = proc.ProcessName;
                            if (string.Equals(name, "POWERPNT", StringComparison.OrdinalIgnoreCase))
                            {
                                found = true;
                                return false;
                            }
                        }
                    }
                    catch
                    {
                    }

                    return true;
                }, IntPtr.Zero);

                return found;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"Win32 检测 PPT 放映窗口失败: {ex.Message}", LogHelper.LogType.Trace);
                return false;
            }
        }

        #endregion

        #region Helper Methods
        /// <summary>
        /// 处理演示文稿打开时的导航逻辑：根据设置决定跳转到首页或显示上次播放页通知。
        /// </summary>
        /// <param name="pres">当前打开的 PowerPoint 演示文稿（Presentation）实例。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 如果设置了总是跳转到首页，则尝试导航到第1页
        /// 2. 否则，如果设置了显示上次播放页通知，则显示上次播放页通知
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private void HandlePresentationOpenNavigation(Presentation pres, PPTState agentState = null)
        {
            try
            {
                if (Settings.PowerPointSettings.IsAlwaysGoToFirstPageOnReenter)
                {
                    _pptManager?.TryNavigateToSlide(1);
                    if (Settings.PowerPointSettings.SkipAnimationsWhenGoNext) ExceptionHandler.TryExecute(() => this.Activate(), "激活主窗口失败（PPT 重入首页时）");
                }
                else if (Settings.PowerPointSettings.IsNotifyPreviousPage)
                {
                    ShowPreviousPageNotification(pres, agentState);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理演示文稿导航失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 显示上次播放页通知：检查演示文稿的上次播放位置并显示跳转提示。
        /// </summary>
        /// <param name="pres">当前打开的 PowerPoint 演示文稿（Presentation）实例。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查演示文稿是否为null
        /// 2. 获取演示文稿路径并计算文件哈希值
        /// 3. 构建保存位置文件夹路径和位置文件路径
        /// 4. 检查位置文件是否存在
        /// 5. 尝试解析位置文件中的页码
        /// 6. 如果解析成功且页码大于0，则保存上次播放页码并显示跳转提示窗口
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private TaskCompletionSource<bool> _inlineDialogTcs;

        private async Task<bool> ShowInlineYesNoDialog(string title, string content)
        {
            _inlineDialogTcs = new TaskCompletionSource<bool>();

            InlineDialogTitle.Content = title;
            InlineDialogContent.Text = content;

            InlineDialogRoot.Opacity = 0;
            InlineDialogRoot.Visibility = Visibility.Visible;
            InlineDialogScaleTransform.ScaleX = 1.05;
            InlineDialogScaleTransform.ScaleY = 1.05;

            var showAnimation = new System.Windows.Media.Animation.Storyboard();

            var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation(0, 1,
                TimeSpan.FromMilliseconds(150));
            opacityAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnimation, InlineDialogRoot);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation,
                new PropertyPath(Grid.OpacityProperty));
            showAnimation.Children.Add(opacityAnimation);

            var scaleXAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.05, 1.0,
                TimeSpan.FromMilliseconds(250));
            scaleXAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            var ease = new System.Windows.Media.Animation.CubicEase { EasingMode = System.Windows.Media.Animation.EasingMode.EaseOut };
            scaleXAnimation.EasingFunction = ease;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleXAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleXAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            showAnimation.Children.Add(scaleXAnimation);

            var scaleYAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.05, 1.0,
                TimeSpan.FromMilliseconds(250));
            scaleYAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            scaleYAnimation.EasingFunction = ease;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleYAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleYAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            showAnimation.Children.Add(scaleYAnimation);

            showAnimation.Begin(this);

            return await _inlineDialogTcs.Task;
        }

        private void HideInlineDialog()
        {
            var hideAnimation = new System.Windows.Media.Animation.Storyboard();

            var opacityAnimation = new System.Windows.Media.Animation.DoubleAnimation(1, 0,
                TimeSpan.FromMilliseconds(150));
            opacityAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(opacityAnimation, InlineDialogRoot);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(opacityAnimation,
                new PropertyPath(Grid.OpacityProperty));
            hideAnimation.Children.Add(opacityAnimation);

            var scaleXAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.05,
                TimeSpan.FromMilliseconds(100));
            scaleXAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleXAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleXAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleX)"));
            hideAnimation.Children.Add(scaleXAnimation);

            var scaleYAnimation = new System.Windows.Media.Animation.DoubleAnimation(1.0, 1.05,
                TimeSpan.FromMilliseconds(100));
            scaleYAnimation.FillBehavior = System.Windows.Media.Animation.FillBehavior.HoldEnd;
            System.Windows.Media.Animation.Storyboard.SetTarget(scaleYAnimation, InlineDialogCard);
            System.Windows.Media.Animation.Storyboard.SetTargetProperty(scaleYAnimation,
                new PropertyPath("(UIElement.RenderTransform).(ScaleTransform.ScaleY)"));
            hideAnimation.Children.Add(scaleYAnimation);

            hideAnimation.Completed += (s, e) =>
            {
                InlineDialogRoot.Visibility = Visibility.Collapsed;
            };

            hideAnimation.Begin(this);
        }

        private void InlineDialogPrimaryButton_Click(object sender, RoutedEventArgs e)
        {
            HideInlineDialog();
            _inlineDialogTcs?.TrySetResult(true);
        }

        private void InlineDialogSecondaryButton_Click(object sender, RoutedEventArgs e)
        {
            HideInlineDialog();
            _inlineDialogTcs?.TrySetResult(false);
        }

        private async void ShowPreviousPageNotification(Presentation pres, PPTState agentState = null)
        {
            try
            {
                var presentationName = agentState?.PresentationName ?? pres?.Name ?? _pptManager?.GetPresentationName();
                var presentationFullName = agentState?.PresentationFullName;
                var totalSlides = agentState?.TotalSlides ?? _pptManager?.SlidesCount ?? 0;
                if (pres != null && totalSlides <= 0)
                    totalSlides = pres.Slides.Count;
                if (string.IsNullOrEmpty(presentationName) || totalSlides <= 0) return;

                var folderPath = GetPresentationStrokeFolderPath(pres, presentationName, totalSlides, presentationFullName);
                var positionFile = Path.Combine(folderPath, "Position");

                if (!File.Exists(positionFile)) return;

                if (int.TryParse(File.ReadAllText(positionFile), out var page) && page > 0)
                {
                    _lastPlaybackPage = page;
                    var result = await ShowInlineYesNoDialog("Ink Canvas For Class CE", string.Format(Properties.PPTStrings.PPT_RememberLastPage_Prompt, page));
                    if (result)
                    {
                        try
                        {
                            if (_pptManager?.TryNavigateToSlide(page) == true)
                            {
                                if (Settings.PowerPointSettings.SkipAnimationsWhenGoNext) ExceptionHandler.TryExecute(() => this.Activate(), "激活主窗口失败（PPT 翻页时）");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"跳转到第{page}页失败: {ex}", LogHelper.LogType.Error);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"显示上次播放页通知失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 检查并通知隐藏幻灯片：扫描演示文稿中的所有幻灯片，检测隐藏幻灯片并显示取消隐藏的提示。
        /// </summary>
        /// <param name="pres">要检查的 PowerPoint 演示文稿（Presentation）实例。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查演示文稿及其幻灯片集合是否为null
        /// 2. 遍历所有幻灯片，检测是否存在隐藏的幻灯片
        /// 3. 如果存在隐藏幻灯片且未显示过恢复隐藏幻灯片窗口，则显示确认窗口
        /// 4. 如果用户确认，则取消所有幻灯片的隐藏状态
        /// 5. 无论用户选择如何，都会重置IsShowingRestoreHiddenSlidesWindow标志
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private async void CheckAndNotifyHiddenSlides(Presentation pres, PPTState agentState = null)
        {
            try
            {
                bool hasHiddenSlides = agentState?.HasHiddenSlides == true;
                if (!hasHiddenSlides && pres?.Slides != null)
                {
                    foreach (Slide slide in pres.Slides)
                    {
                        if (slide.SlideShowTransition.Hidden == MsoTriState.msoTrue)
                        {
                            hasHiddenSlides = true;
                            break;
                        }
                    }
                }

                if (hasHiddenSlides && !IsShowingRestoreHiddenSlidesWindow)
                {
                    IsShowingRestoreHiddenSlidesWindow = true;
                    var result = await ShowInlineYesNoDialog("Ink Canvas For Class CE", Properties.PPTStrings.PPT_HiddenSlides_Detected);
                    if (result)
                    {
                        try
                        {
                            if (pres?.Slides != null)
                            {
                                foreach (Slide slide in pres.Slides)
                                {
                                    if (slide.SlideShowTransition.Hidden == MsoTriState.msoTrue)
                                        slide.SlideShowTransition.Hidden = MsoTriState.msoFalse;
                                }
                            }
                            // LTSC D-02: Agent 架构已移除，隐藏幻灯片走上面的 COM 路径。
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"取消隐藏幻灯片失败: {ex}", LogHelper.LogType.Error);
                        }
                        finally
                        {
                            IsShowingRestoreHiddenSlidesWindow = false;
                        }
                    }
                    else
                    {
                        IsShowingRestoreHiddenSlidesWindow = false;
                    }
                }
            }
            catch (Exception ex)
            {
                IsShowingRestoreHiddenSlidesWindow = false;
                LogHelper.WriteLogToFile($"检查隐藏幻灯片失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 检查并通知自动播放设置：扫描演示文稿中的所有幻灯片，检测自动播放或排练计时设置并显示取消提示。
        /// </summary>
        /// <param name="pres">要检查的 PowerPoint 演示文稿（Presentation）实例。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 检查是否正在显示PPT放映结束按钮，如果是则直接返回
        /// 2. 检查演示文稿及其幻灯片集合是否为null
        /// 3. 遍历所有幻灯片，检测是否存在自动播放或排练计时设置
        /// 4. 如果存在自动播放设置且未显示过自动播放提示窗口，则显示确认窗口
        /// 5. 如果用户确认，则将演示文稿的放映设置设置为手动播放模式
        /// 6. 无论用户选择如何，都会重置IsShowingAutoplaySlidesWindow标志
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private async void CheckAndNotifyAutoPlaySettings(Presentation pres, PPTState agentState = null)
        {
            try
            {
                if (IsInPPTPresentationMode) return;

                bool hasSlideTimings = agentState?.HasAutoPlayTimings == true;
                if (!hasSlideTimings && pres?.Slides != null)
                {
                    foreach (Slide slide in pres.Slides)
                    {
                        if (slide.SlideShowTransition.AdvanceOnTime == MsoTriState.msoTrue &&
                            slide.SlideShowTransition.AdvanceTime > 0)
                        {
                            hasSlideTimings = true;
                            break;
                        }
                    }
                }

                if (hasSlideTimings && !IsShowingAutoplaySlidesWindow)
                {
                    IsShowingAutoplaySlidesWindow = true;
                    var result = await ShowInlineYesNoDialog("Ink Canvas For Class CE", Properties.PPTStrings.PPT_AutoPlay_Detected);
                    if (result)
                    {
                        try
                        {
                            if (pres != null)
                            {
                                pres.SlideShowSettings.AdvanceMode = PpSlideShowAdvanceMode.ppSlideShowManualAdvance;
                            }
                            // LTSC D-02: Agent 架构已移除。
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"设置手动播放模式失败: {ex}", LogHelper.LogType.Error);
                        }
                        finally
                        {
                            IsShowingAutoplaySlidesWindow = false;
                        }
                    }
                    else
                    {
                        IsShowingAutoplaySlidesWindow = false;
                    }
                }
            }
            catch (Exception ex)
            {
                IsShowingAutoplaySlidesWindow = false;
                LogHelper.WriteLogToFile($"检查自动播放设置失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 加载当前幻灯片的墨迹：清空画布和历史记录，然后加载指定幻灯片的墨迹。
        /// </summary>
        /// <param name="slideIndex">要加载墨迹的幻灯片索引。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 清空画布上的所有墨迹
        /// 2. 清空时间机器的墨迹历史记录
        /// 3. 从墨迹管理器加载指定幻灯片的墨迹
        /// 4. 如果加载到墨迹且墨迹集合不为空，则将墨迹添加到画布
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private void LoadCurrentSlideInk(int slideIndex)
        {
            try
            {
                ClearStrokes(true);
                timeMachine.ClearStrokeHistory();

                byte[] bytes = null;
                lock (_memoryStreams)
                {
                    if (_memoryStreams.TryGetValue(slideIndex, out var ms) && ms != null && ms.Length > 0)
                    {
                        ms.Position = 0;
                        bytes = ms.ToArray();
                    }
                }
                if (bytes != null)
                {
                    try
                    {
                        inkCanvas.Strokes.Add(new StrokeCollection(new MemoryStream(bytes)));
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"从内存流加载第 {slideIndex} 页墨迹失败: {ex}", LogHelper.LogType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"加载当前页墨迹失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 重置墨迹管理器的锁定状态，防止墨迹显示错误
        /// </summary>
        private void ResetInkManagerLockState()
        {
            try
            {
                _singlePPTInkManager?.ResetLockState();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重置墨迹管理器锁定状态失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 重置PPT相关的状态变量，当PPT自动收纳设置变更时调用
        /// </summary>
        /// <remarks>
        /// 将与 PowerPoint 播放和状态追踪相关的内部字段重置为初始默认值。
        /// 具体重置的字段包括：
        /// 1. 播放结束重入保护标志（isEnteredSlideShowEndEvent）
        /// 2. 演示文稿黑边指示（isPresentationHaveBlackSpace）
        /// 3. 上次播放页码（_lastPlaybackPage）
        /// 4. 导航标志（_shouldNavigateToLastPage）
        /// 5. 当前放映位置（_currentSlideShowPosition）
        /// 6. 滑动切换处理状态（_isProcessingSlideSwitch）
        /// 
        /// 该方法在执行过程中会：
        /// - 使用线程安全的方式重置滑动切换处理状态
        /// - 成功时记录追踪日志
        /// - 发生异常时记录错误日志并继续执行
        /// </remarks>
        public void ResetPPTStateVariables()
        {
            try
            {
                // 重置PPT放映结束事件标志
                isEnteredSlideShowEndEvent = false;

                // 重置演示文稿黑边状态
                isPresentationHaveBlackSpace = false;

                // 重置上次播放位置相关字段
                _lastPlaybackPage = 0;
                _shouldNavigateToLastPage = false;

                // 重置当前播放页码跟踪
                _currentSlideShowPosition = 0;
                _previousSlideID = 0;
                lock (_memoryStreams)
                {
                    foreach (var stream in _memoryStreams.Values)
                        stream?.Dispose();
                    _memoryStreams.Clear();
                }

                LogHelper.WriteLogToFile("PPT状态变量已重置", LogHelper.LogType.Trace);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"重置PPT状态变量失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        #endregion

        /// <summary>
        /// 发起一次手动的 PowerPoint 连接检查并在短延迟后报告结果。
        /// </summary>
        /// <remarks>
        /// 如果尚未初始化 PPT 管理器则先进行初始化，然后重载连接并启动监控；
        /// 延迟约 800 毫秒后在 UI 线程上检查连接状态：若已连接仅记录事件日志，若未连接则弹出提示并记录警告；
        /// 若过程中抛出异常则记录错误日志、将 UI 连接状态置为断开并提示用户未找到幻灯片。
        /// </remarks>
        private void BtnCheckPPT_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 使用新的PPT管理器进行连接检查
                if (_pptManager == null)
                {
                    InitializePPTManagers();
                }

                _pptManager?.ReloadConnection();
                _pptManager?.StartMonitoring();

                Task.Delay(800).ContinueWith(_ =>
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (_pptManager?.IsConnected == true)
                        {
                            LogHelper.WriteLogToFile("手动PPT连接检查成功", LogHelper.LogType.Event);
                        }
                        else
                        {
                            MessageBox.Show(Properties.PPTStrings.PPT_SlidesNotFound);
                            LogHelper.WriteLogToFile("手动PPT连接检查失败", LogHelper.LogType.Warning);
                        }
                    });
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"手动检查PPT应用程序失败: {ex}", LogHelper.LogType.Error);
                _pptUIManager?.UpdateConnectionStatus(false);
                MessageBox.Show(Properties.PPTStrings.PPT_SlidesNotFound);
            }
        }

        /// <summary>
        /// 处理PowerPoint增强功能开关的切换事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 当PowerPoint增强功能被启用时：
        /// 1. 禁用WPS支持
        /// 2. 更新PPT管理器的WPS支持设置
        /// 3. 启动PowerPoint进程守护
        /// 当PowerPoint增强功能被禁用时：
        /// 1. 停止PowerPoint进程守护
        /// 无论开关状态如何变化，都会保存设置到文件
        /// </remarks>
        // LTSC P1-8：开关防抖。老师会连点（历史上有一次手动快速开关 27 次），
        // 每一下都 Start/Stop 监控会把 PptBridge 的轮询线程反复建销。
        // 用一个短延时把连续变化合并成最后一次，只在停下之后真正执行。
        private DispatcherTimer _pptEnhancementDebounceTimer;

        private void ToggleSwitchPowerPointEnhancement_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            var toggle = sender as iNKORE.UI.WPF.Modern.Controls.ToggleSwitch;
            if (toggle != null)
                Settings.PowerPointSettings.EnablePowerPointEnhancement = toggle.IsOn;

            // LTSC P1-8：不再在这里强改 IsSupportWPS。
            // 那是旧「架构二选一」时代留下的——当时 COM 和 WPS 两条路互斥。
            // 现在统一走 PptBridge(ROT)，两个开关各管各的，改一个不该动另一个。
            SaveSettingsToFile();

            _pptEnhancementDebounceTimer?.Stop();
            _pptEnhancementDebounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(PptEnhancementDebounceMs)
            };
            _pptEnhancementDebounceTimer.Tick += (_, __) =>
            {
                _pptEnhancementDebounceTimer?.Stop();
                _pptEnhancementDebounceTimer = null;

                if (Settings.PowerPointSettings.EnablePowerPointEnhancement)
                    StartPowerPointProcessMonitoring();
                else
                    StopPowerPointProcessMonitoring();
            };
            _pptEnhancementDebounceTimer.Start();
        }

        /// <summary>开关防抖窗口。足够合并连续点击，又不至于让人感觉按下没反应。</summary>
        private const int PptEnhancementDebounceMs = 400;

        /// <summary>
        /// 处理WPS支持开关的切换事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 当WPS支持被启用时：
        /// 1. 如果PowerPoint支持未启用，则启用PowerPoint支持
        /// 2. 启动PPT监控
        /// 3. 如果PowerPoint增强功能已启用，则禁用它并停止PowerPoint进程守护
        /// 无论开关状态如何变化，都会：
        /// 1. 更新PPT管理器的WPS支持设置
        /// 2. 保存设置到文件
        /// </remarks>
        private void ToggleSwitchSupportWPS_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            var toggle = sender as iNKORE.UI.WPF.Modern.Controls.ToggleSwitch;
            if (toggle != null)
                Settings.PowerPointSettings.IsSupportWPS = toggle.IsOn;

            if (Settings.PowerPointSettings.IsSupportWPS)
            {
                if (!Settings.PowerPointSettings.PowerPointSupport)
                {
                    Settings.PowerPointSettings.PowerPointSupport = true;

                    if (_pptManager == null)
                    {
                        InitializePPTManagers();
                    }
                    StartPPTMonitoring();
                }

                if (Settings.PowerPointSettings.EnablePowerPointEnhancement)
                {
                    Settings.PowerPointSettings.EnablePowerPointEnhancement = false;
                    StopPowerPointProcessMonitoring();
                }
            }

            if (_pptManager != null)
            {
                _pptManager.IsSupportWPS = Settings.PowerPointSettings.IsSupportWPS;
                _pptManager.SkipAnimationsWhenNavigating = Settings.PowerPointSettings.SkipAnimationsWhenGoNext;
            }

            SaveSettingsToFile();
        }

        private void ToggleSwitchSkipAnimationsWhenGoNext_Toggled(object sender, RoutedEventArgs e)
        {
            if (!isLoaded) return;

            var toggle = sender as iNKORE.UI.WPF.Modern.Controls.ToggleSwitch;
            if (toggle != null)
                Settings.PowerPointSettings.SkipAnimationsWhenGoNext = toggle.IsOn;

            if (_pptManager != null)
            {
                _pptManager.SkipAnimationsWhenNavigating = Settings.PowerPointSettings.SkipAnimationsWhenGoNext;
            }

            SaveSettingsToFile();
        }

        /// <summary>
        /// 获取当前是否启用了WPS支持
        /// </summary>
        /// <value>如果启用了WPS支持，则为true；否则为false</value>
        private static bool isWPSSupportOn => Settings.PowerPointSettings.IsSupportWPS;

        /// <summary>
        /// 指示是否正在显示恢复隐藏幻灯片的窗口
        /// </summary>
        public static bool IsShowingRestoreHiddenSlidesWindow;

        /// <summary>
        /// 指示是否正在显示自动播放提示窗口
        /// </summary>
        private static bool IsShowingAutoplaySlidesWindow;

        // ── 翻页防抖（LTSC P1c，2026-11-11）──────────────────────────────
        // 老师会快速连点翻页按钮。原来每次点击都直接 Task.Run 冲进 COM，
        // 连点 5 次就是 5 个线程同时调 PowerPoint —— COM 单线程单元扛不住，
        // 表现为跳页、卡顿、甚至「点了没反应」。
        // 这里用一个带超时自释放的 in-flight 标记：上一页还没翻完，新的点击直接丢弃。
        // 刻意不 await、不阻塞 UI —— 丢一次点击远好过卡住整个界面。
        private static readonly object _pptNavLock = new object();
        private static bool _pptNavInFlight;
        private static DateTime _pptNavInFlightSince = DateTime.MinValue;

        /// <summary>
        /// 翻页互斥窗口：上一次翻页还在进行中就直接放弃这次点击。
        /// 超过 <see cref="_pptNavStaleMs"/> 仍未释放的锁视为失效（COM 卡死时不至于永久锁住翻页）。
        /// </summary>

        private static bool TryEnterPPTNavigation()
        {
            lock (_pptNavLock)
            {
                if (_pptNavInFlight)
                {
                    // 锁超时保护：COM 调用偶尔会拖很久，不能因此永久锁死按钮。
                    if ((DateTime.UtcNow - _pptNavInFlightSince).TotalMilliseconds < _pptNavStaleMs)
                        return false;
                }
                _pptNavInFlight = true;
                _pptNavInFlightSince = DateTime.UtcNow;
                return true;
            }
        }

        private static void ExitPPTNavigation()
        {
            lock (_pptNavLock) { _pptNavInFlight = false; }
        }

        /// <summary>锁失效阈值：超过这个时间仍持有锁，下一次点击强行接管。</summary>
        private const int _pptNavStaleMs = 1500;

        /// <summary>
        /// 处理“上一页”按钮的点击操作：在满足自动保存条件时保存当前幻灯片截图并尝试切换到上一张幻灯片；在切换失败或发生异常时记录日志并更新连接状态。
        /// </summary>
        /// <param name="sender">事件的来源对象（通常是触发按钮）。</param>
        /// <param name="e">路由事件参数。</param>
        private void BtnPPTSlidesUp_Click(object sender, RoutedEventArgs e)
        {
            // 防抖：上一次翻页还没结束就丢弃这次点击。
            if (!TryEnterPPTNavigation()) return;

            Task.Run(() =>
            {
                try
                {
                    return _pptManager?.TryNavigatePrevious() ?? false;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"PPT上一页操作异常: {ex}", LogHelper.LogType.Error);
                    return false;
                }
            }).ContinueWith(t =>
            {
                ExitPPTNavigation();
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (t.IsFaulted) { _pptUIManager?.UpdateConnectionStatus(false); return; }
                    if (t.Result)
                    {
                        if (Settings.PowerPointSettings.SkipAnimationsWhenGoNext) ExceptionHandler.TryExecute(() => this.Activate(), "激活主窗口失败（PPT 上一页时）");
                    }
                    else
                    {
                        LogHelper.WriteLogToFile("切换到上一页失败", LogHelper.LogType.Warning);
                        _pptUIManager?.UpdateConnectionStatus(false);
                    }
                }));
            });
        }

        /// <summary>
        /// 处理“下一页”按钮点击：在满足自动保存条件时保存当前幻灯片的截图并尝试切换到下一张幻灯片。
        /// </summary>
        /// <remarks>
        /// 如果切换操作失败或发生异常，会写入日志并将 PPT 连接状态更新为断开。
        /// </remarks>
        private void BtnPPTSlidesDown_Click(object sender, RoutedEventArgs e)
        {
            // 防抖：同上。
            if (!TryEnterPPTNavigation()) return;

            Task.Run(() =>
            {
                try
                {
                    return _pptManager?.TryNavigateNext() ?? false;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"PPT下一页操作异常: {ex}", LogHelper.LogType.Error);
                    return false;
                }
            }).ContinueWith(t =>
            {
                ExitPPTNavigation();
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (t.IsFaulted) { _pptUIManager?.UpdateConnectionStatus(false); return; }
                    if (t.Result)
                    {
                        if (Settings.PowerPointSettings.SkipAnimationsWhenGoNext) ExceptionHandler.TryExecute(() => this.Activate(), "激活主窗口失败（PPT 下一页时）");
                    }
                    else
                    {
                        LogHelper.WriteLogToFile("切换到下一页失败", LogHelper.LogType.Warning);
                        _pptUIManager?.UpdateConnectionStatus(false);
                    }
                }));
            });
        }

        // LTSC P2：删掉 PPTNavigationBtn_MouseDown / MouseLeave / MouseUp 三个空方法。
        // 它们绑定的是旧 XAML 里一个 Visibility="Collapsed" 的死按钮，
        // 该按钮的 ElementName 绑定（PPTNavigationBtn / PPTNavigationTextBlock）
        // 在 XAML 里根本没有 x:Name 定义，绑定永远失败。
        // 现代翻页条已经是 Controls/PPTNavBar，走 PageClick 事件，
        // 对应 OnPPTNavBarPageClickAsync。这三个方法从来没被真正调用过。

        /// <summary>由 PPTNavBar 控件 PageClick 事件触发的页码点击逻辑。</summary>
        #region LTSC D-07：点页码临时穿透 → 切页后自动回批注

        /// <summary>
        /// 这次「点页码进导航」是不是从批注态发起的。
        /// 需求原话：「点击页码键后先临时进入 ppt 鼠标穿透模式并且隐藏笔迹，
        /// 切页成功后自动恢复至 ppt 批注状态（笔迹恢复成当前页的笔迹）；
        /// 如果没有切页，点批注键也要能回到 ppt 批注模式并恢复此页笔迹」。
        /// </summary>
        private bool _pptNavPendingRestoreAnnotation;

        /// <summary>
        /// 下一次「点鼠标进穿透」时跳过一次隐藏笔迹。
        ///
        /// 用户裁决（2026-11-17）：这个豁免是一次性的，只作用于从页码点击
        /// 回来后的那一次。不是改 PPT 穿透模式的通用行为 ——
        /// 后续再点鼠标仍按 HideStrokeWhenSelecting / IsShowStrokeOnSelectInPowerPoint 走。
        /// </summary>
        private bool _pptNavSkipHideStrokeOnce;

        /// <summary>切页完成后自动回批注态（幂等，只生效一次）。</summary>
        internal void TryRestoreAnnotationAfterPptNavigation(string reason)
        {
            if (!_pptNavPendingRestoreAnnotation) return;
            _pptNavPendingRestoreAnnotation = false;

            try
            {
                if (_pptManager?.IsConnected != true || _pptManager?.IsInSlideShow != true) return;

                Application.Current?.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        // 已经自动回到批注态了，那个「下次点鼠标不隐藏笔迹」的豁免用不上；
                        // 留着会让用户之后一次正常点鼠标时莫名不隐藏，行为不一致。
                        _pptNavSkipHideStrokeOnce = false;

                        inkCanvas.Visibility = Visibility.Visible;

                        // LTSC（2026-11-17）：这里原来是直接调 PenIcon_Click(null, null)。
                        //
                        // 但 PenIcon_Click 里第一个判断是：
                        //     if (Pen_Icon.Background == null || !IsAnnotating)
                        //     {
                        //         真正切换：SetCurrentToolMode(Ink) + UpdateCurrentToolMode("pen") + ...
                        //     }
                        //     else { ...弹/收笔面板，不碰 EditingMode... }
                        //
                        // 而 IsAnnotating 是只读属性：
                        //     internal bool IsAnnotating => _currentToolMode != "cursor";
                        // 只要 _currentToolMode 已经不是 "cursor"，这里就吃 else 分支，
                        // **EditingMode 完全没人设**。
                        //
                        // 表现就是用户说的：「自动进了批注模式，但写不了字，
                        // 必须点一下鼠标再点一下批注才能写」——
                        // 因为那两次点击才真的走了一遍 if 分支。
                        //
                        // 修法：恢复批注态这件事不能指望「点一下笔按钮」的副作用，
                        // 这里显式把状态设到位。PenIcon_Click 仍然照调（它还要管
                        // 笔属性、面板、高亮等），但 EditingMode 由下面两句保证。
                        SetCurrentToolMode(InkCanvasEditingMode.Ink);
                        UpdateCurrentToolMode("pen");

                        // 再调一次让 UI（笔高亮、面板状态）跟上。
                        // 此时 IsAnnotating 已经是 true，PenIcon_Click 走 else 分支，
                        // 但那正好是它该做的事——不会再往下改 EditingMode。
                        PenIcon_Click(null, null);

                        // LTSC（2026-11-13）：回到批注态时也要清遮罩，
                        // 否则这块半透明层继续挡着底部浮动栏。
                        ResetPptTransparencyOverlay($"回到批注模式（{reason}）");

                        // 打一行能验证的日志：把当时的 EditingMode 一起记下来，
                        // 以后再有「进了批注却写不了」的反馈，看这一行就知道是不是这个原因。
                        LogHelper.WriteLogToFile(
                            $"[PPT导航] 已自动回到批注模式（{reason}）| EditingMode={inkCanvas.EditingMode} " +
                            $"currentToolMode={GetCurrentSelectedMode()}",
                            LogHelper.LogType.Event);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"[PPT导航] 恢复批注模式失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[PPT导航] 恢复批注模式失败(外层): {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>用户自己点了批注键 —— 作废挂起的自动恢复。</summary>
        internal void CancelPendingPptNavigationRestore()
        {
            _pptNavPendingRestoreAnnotation = false;
        }

        /// <summary>
        /// LTSC（2026-11-13）任务栏大小的透明遮罩残留修复。
        ///
        /// 复现：PPT 放映中点页码 → OnPPTNavBarPageClickAsync 把
        /// GridTransparencyFakeBackground 设成 #01FFFFFF（视觉全透明但仍吃点击）
        /// 并调 SetTransparentNotHitThrough()，把整个窗口变成可拦截状态。
        /// 如果之后没有走到正常的「退出 PPT / 回到批注」路径，
        /// 这块遮罩就留在窗口里，盖住底部一条（和任务栏等大），
        /// 老师看到的就是「浮动栏点不动，那块地方像是被什么挡住了」。
        ///
        /// 这个方法是幂等的：不管当前是什么状态，一律恢复到「全透明 + 点击穿透」。
        /// </summary>
        internal void ResetPptTransparencyOverlay(string reason)
        {
            try
            {
                bool wasDirty = GridTransparencyFakeBackground.Background != Brushes.Transparent
                                || GridTransparencyFakeBackground.Opacity > 0.001;

                GridTransparencyFakeBackground.Opacity = 0;
                GridTransparencyFakeBackground.Background = Brushes.Transparent;
                SetTransparentHitThrough();

                if (wasDirty)
                {
                    LogHelper.WriteLogToFile($"[PPT遮罩] 已清理透明遮罩（{reason}）", LogHelper.LogType.Event);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"[PPT遮罩] 清理失败（{reason}）: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        #endregion

        private async Task OnPPTNavBarPageClickAsync(Controls.PPTNavBar bar)
        {
            if (!Settings.PowerPointSettings.EnablePPTButtonPageClickable)
            {
                // LTSC（2026-11-19 班测）：原来这里是静默 return。
                // 用户点页码没反应时，日志里一个字都没有，只有"没反应"这个现象。
                LogHelper.WriteLogToFile("[PPT导航] 页码点击被忽略：设置里关闭了「页码可点击」",
                    LogHelper.LogType.Warning);
                return;
            }
            if (_pptManager?.IsConnected != true || _pptManager?.IsInSlideShow != true)
            {
                LogHelper.WriteLogToFile("[PPT导航] 页码点击被忽略：PPT未连接或未在放映状态"
                    + $"（IsConnected={_pptManager?.IsConnected} IsInSlideShow={_pptManager?.IsInSlideShow}）",
                    LogHelper.LogType.Warning);
                return;
            }

            try
            {
                // LTSC（2026-11-17 修正）：这里原来有个 bug。
                //
                // 上一版写法是：
                //     if (!IsAnnotating) { 记日志; return; }   // 直接退出整个方法
                //
                // 但需求原话是「**保持**即可，不需要遵循后面的逻辑」——
                // 「保持」指的是**不要改变穿透状态**，不是「什么都别做」。
                //
                // 结果：鼠标状态下点页码，整个方法直接 return，
                // TryShowSlideNavigation() 根本没被调到，页码键看起来完全没反应。
                //
                // 现在改成：状态切换只在批注态时做；导航任何状态都执行。
                bool wasAnnotating = IsAnnotating;

                // LTSC（2026-11-19 班测）：把进入时的完整状态打出来。
                // 用户报的两个症状都指向「批注/穿透状态没按预期切换」，
                // 而这几项以前一条都不记，出事时只能猜。
                LogHelper.WriteLogToFile(
                    "[PPT导航] 点页码：进入时状态 "
                    + $"批注={wasAnnotating} 工具模式={_currentToolMode} "
                    + $"笔迹数={inkCanvas?.Strokes?.Count} "
                    + $"笔迹可见={(inkCanvas?.Visibility)} "
                    + $"待恢复标记={_pptNavPendingRestoreAnnotation} "
                    + $"穿透豁免={_pptNavSkipHideStrokeOnce} "
                    + $"浮动栏折叠={isFloatingBarFolded}",
                    LogHelper.LogType.Event);

                if (wasAnnotating)
                {
                    // 只有从批注态进来才需要走切换流程：
                    // 记下待恢复状态 → 切穿透 → 隐藏笔迹 → 记「下次点鼠标别藏笔迹」。
                    _pptNavPendingRestoreAnnotation = true;

                    GridTransparencyFakeBackground.Opacity = 1;
                    GridTransparencyFakeBackground.Background = new SolidColorBrush(StringToColor("#01FFFFFF"));
                    SetTransparentNotHitThrough();
                    CursorIcon_Click(null, null);

                    // 点页码进导航时必须隐藏笔迹（这是与「点鼠标」不同的行为：
                    // 点鼠标要保留笔迹，点页码要临时藏起来，方便看清缩略图）。
                    inkCanvas.Visibility = Visibility.Collapsed;

                    // 记一笔：用户下次点鼠标退出穿透时，跳过隐藏笔迹，把笔迹还给他。
                    // 只生效一次 —— 见 _pptNavSkipHideStrokeOnce 的注释。
                    _pptNavSkipHideStrokeOnce = true;
                }
                else
                {
                    // 已经是鼠标穿透态：状态保持不动，不重置挂起的恢复标记，
                    // 直接去调导航就行。
                    LogHelper.WriteLogToFile("[PPT导航] 鼠标穿透态下点页码，保持状态并执行导航",
                        LogHelper.LogType.Event);
                }

                // LTSC P1c：增强预览已整体移除（用户裁决）。
                // 点页码 = 调 PowerPoint 原版导航，不再画 ICC 自己的缩略图面板。
                // 这一句必须在两个分支之外——否则鼠标态下点页码什么都不会发生。
                if (!_pptManager.TryShowSlideNavigation())
                {
                    LogHelper.WriteLogToFile("显示PPT幻灯片导航失败", LogHelper.LogType.Warning);
                }

                if (wasAnnotating && !isFloatingBarFolded)
                {
                    await Task.Delay(100);
                    ViewboxFloatingBarMarginAnimation(60);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PPT翻页控件操作失败: {ex}", LogHelper.LogType.Error);
            }
        }

        private void OnPPTNavBarSlideSelected(Controls.PPTNavBar bar, int slideNumber)
        {
            try { _pptManager?.TryNavigateToSlide(slideNumber); }
            catch (Exception ex) { LogHelper.WriteLogToFile($"PPT增强预览跳转异常: {ex}", LogHelper.LogType.Error); }
            finally { if (bar != null) bar.IsPreviewExpanded = false; }

        }

        /// <summary>
        /// 选择承载增强预览的底部条:
        /// - 来自侧边条的点击重定向到同侧底部条;
        /// - 若同侧底部条不可用,退化到任意可用的底部条;
        /// - 来自底部条的点击保持原行为。
        /// </summary>
        private Controls.PPTNavBar ResolvePreviewTargetBar(Controls.PPTNavBar source)
        {
            if (source == null) return null;
            switch (source.Direction)
            {
                case Controls.PPTNavBar.NavDirection.LeftSide:
                    return PickVisibleBar(LeftBottomPanelForPPTNavigation, RightBottomPanelForPPTNavigation) ?? source;
                case Controls.PPTNavBar.NavDirection.RightSide:
                    return PickVisibleBar(RightBottomPanelForPPTNavigation, LeftBottomPanelForPPTNavigation) ?? source;
                default:
                    return source;
            }
        }

        private static Controls.PPTNavBar PickVisibleBar(params Controls.PPTNavBar[] candidates)
        {
            foreach (var c in candidates)
            {
                if (c != null && c.Visibility == Visibility.Visible) return c;
            }
            return null;
        }

        private double _pptLoadingProgress;
        private DispatcherTimer _pptLoadingTimer;

        /// <summary>直接同步回调的 IProgress 实现，不走 SynchronizationContext。</summary>
        private sealed class DirectProgress : IProgress<double>
        {
            private readonly Action<double> _handler;
            public DirectProgress(Action<double> handler) => _handler = handler;
            public void Report(double value) => _handler(value);
        }

        private void UpdatePPTLoadingRing()
        {
            if (PPTLoadingRing == null) return;
            double progress = Math.Max(0, Math.Min(1, _pptLoadingProgress));

            // 像素坐标：Path Stretch="None"，Grid 64×64，StrokeThickness=4
            const double cx = 32, cy = 32, r = 30;
            const double startAngle = -Math.PI / 2; // 从 12 点方向开始

            if (progress <= 0.001)
            {
                PPTLoadingRing.Data = Geometry.Empty;
                return;
            }

            double sweepAngle = progress * 2 * Math.PI;
            double sx = cx + r * Math.Cos(startAngle);
            double sy = cy + r * Math.Sin(startAngle);

            var figure = new PathFigure { StartPoint = new System.Windows.Point(sx, sy) };

            if (sweepAngle >= 2 * Math.PI - 0.01)
            {
                // 满环：两段 180° 弧
                figure.Segments.Add(new ArcSegment(
                    new System.Windows.Point(cx, cy + r),
                    new Size(r, r), 0, false, SweepDirection.Clockwise, true));
                figure.Segments.Add(new ArcSegment(
                    new System.Windows.Point(sx, sy),
                    new Size(r, r), 0, false, SweepDirection.Clockwise, true));
            }
            else
            {
                double endAngle = startAngle + sweepAngle;
                figure.Segments.Add(new ArcSegment(
                    new System.Windows.Point(cx + r * Math.Cos(endAngle), cy + r * Math.Sin(endAngle)),
                    new Size(r, r), 0, sweepAngle > Math.PI, SweepDirection.Clockwise, true));
            }

            var geom = new PathGeometry();
            geom.Figures.Add(figure);
            PPTLoadingRing.Data = geom;
        }

        private sealed class PPTEnhancedPreviewItem : IDisposable
        {
            public int SlideNumber { get; set; }
            public MemoryStream ThumbnailStream { get; set; }
            public BitmapImage Thumbnail { get; set; }

            public void Dispose()
            {
                ThumbnailStream?.Dispose();
                ThumbnailStream = null;
                Thumbnail = null;
            }
        }

        private void CollapseAllPPTNavBarPreviews()
        {
            var bars = new[]
            {
                LeftBottomPanelForPPTNavigation,
                RightBottomPanelForPPTNavigation,
                LeftSidePanelForPPTNavigation,
                RightSidePanelForPPTNavigation,
            };
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                ExceptionHandler.TryExecute(
                    () =>
                    {
                        bar.IsPreviewExpanded = false;
                        bar.PreviewItems = null;
                    },
                    "重置 PPT 导航栏预览状态失败",
                    continueOnError: true);
            }
        }

        /// <summary>在 MainWindow 加载完成后调用,把 4 个 PPTNavBar 的事件接到本类。</summary>
        private bool _pptNavBarsWired;

        private void WirePPTNavBars()
        {
            // InitializePPTManagers 可能被多次调用（切换 COM/ROT、设置变更等）。
            // PPTNavBar 事件若在同一控件上重复订阅，会导致翻页、长按、预览展开等逻辑成倍触发。
            if (_pptNavBarsWired) return;

            var bars = new[]
            {
                LeftBottomPanelForPPTNavigation,
                RightBottomPanelForPPTNavigation,
                LeftSidePanelForPPTNavigation,
                RightSidePanelForPPTNavigation,
            };
            foreach (var bar in bars)
            {
                if (bar == null) continue;
                bar.PreviousClick += (s, e) => BtnPPTSlidesUp_Click(null, null);
                bar.NextClick += (s, e) => BtnPPTSlidesDown_Click(null, null);
                // LTSC P1c：长按连续翻页整条功能移除。
                // 实测从未生效（老师真机 + 我的脚本都没能触发定时器），
                // 而它带来的是「松手多翻一页」这个更难修的副作用。
                // 一个不生效的功能留着只会继续藏 bug。
                bar.PressEnded += (s, e) => StopLongPressDetection();
                var captured = bar;
                bar.PageClick += async (s, e) => await OnPPTNavBarPageClickAsync(captured);
                bar.SlideSelected += (s, slideNumber) => OnPPTNavBarSlideSelected(captured, slideNumber);
                bar.PreviewExpandedChanged += (s, expanded) => OnPPTNavBarPreviewExpandedChanged(captured, expanded);
            }

            _pptNavBarsWired = true;
        }

        private bool _suppressPreviewExpandedSync;

        private void OnPPTNavBarPreviewExpandedChanged(Controls.PPTNavBar bar, bool expanded)
        {
            if (_suppressPreviewExpandedSync) return;
            try
            {
                _suppressPreviewExpandedSync = true;

                if (expanded)
                {
                    // 仅允许同时一侧展开
                    foreach (var other in new[]
                    {
                        LeftBottomPanelForPPTNavigation,
                        RightBottomPanelForPPTNavigation,
                        LeftSidePanelForPPTNavigation,
                        RightSidePanelForPPTNavigation,
                    })
                    {
                        if (other == null || ReferenceEquals(other, bar)) continue;
                        if (other.IsPreviewExpanded) other.IsPreviewExpanded = false;
                    }
                }

                // 底部条展开时,隐藏同侧的中部侧边条避免遮挡;收起后还原可见性
                ApplyBottomBarSideOcclusion();
            }
            finally
            {
                _suppressPreviewExpandedSync = false;
            }
        }

        private void ApplyBottomBarSideOcclusion()
        {
            var leftBottomExpanded = LeftBottomPanelForPPTNavigation != null && LeftBottomPanelForPPTNavigation.IsPreviewExpanded;
            var rightBottomExpanded = RightBottomPanelForPPTNavigation != null && RightBottomPanelForPPTNavigation.IsPreviewExpanded;

            // 同侧的侧边条在底部条展开时隐藏
            if (LeftSidePanelForPPTNavigation != null)
            {
                if (leftBottomExpanded)
                {
                    LeftSidePanelForPPTNavigation.Tag = LeftSidePanelForPPTNavigation.Visibility;
                    LeftSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                }
                else if (LeftSidePanelForPPTNavigation.Tag is Visibility cached)
                {
                    LeftSidePanelForPPTNavigation.Visibility = cached;
                    LeftSidePanelForPPTNavigation.ClearValue(TagProperty);
                }
            }
            if (RightSidePanelForPPTNavigation != null)
            {
                if (rightBottomExpanded)
                {
                    RightSidePanelForPPTNavigation.Tag = RightSidePanelForPPTNavigation.Visibility;
                    RightSidePanelForPPTNavigation.Visibility = Visibility.Collapsed;
                }
                else if (RightSidePanelForPPTNavigation.Tag is Visibility cached)
                {
                    RightSidePanelForPPTNavigation.Visibility = cached;
                    RightSidePanelForPPTNavigation.ClearValue(TagProperty);
                }
            }
        }

        private CancellationToken EnsurePPTEnhancedPreviewCacheToken()
        {
            lock (_pptEnhancedPreviewCacheLock)
            {
                if (_pptEnhancedPreviewCacheCts == null)
                    _pptEnhancedPreviewCacheCts = new CancellationTokenSource();

                return _pptEnhancedPreviewCacheCts.Token;
            }
        }

        private void ResetPPTEnhancedPreviewCache()
        {
            CancellationTokenSource ctsToCancel = null;
            List<PPTEnhancedPreviewItem> cacheToDispose = null;

            lock (_pptEnhancedPreviewCacheLock)
            {
                _pptEnhancedPreviewCacheGeneration++;
                ctsToCancel = _pptEnhancedPreviewCacheCts;
                _pptEnhancedPreviewCacheCts = null;
                _pptEnhancedPreviewBuildTask = null;
                cacheToDispose = _pptEnhancedPreviewCache;
                _pptEnhancedPreviewCache = null;
            }

            try
            {
                ctsToCancel?.Cancel();
            }
            catch
            {
            }

            try
            {
                ctsToCancel?.Dispose();
            }
            catch
            {
            }

            DisposePPTEnhancedPreviewItems(cacheToDispose);
        }

        private void SchedulePPTEnhancedPreviewPreload()
        {
            // LTSC P1c：增强预览已整体移除，不再预热缩略图。
        }

        private async Task PreloadPPTEnhancedPreviewAfterDelayAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(PPTEnhancedPreviewPreloadDelayMs, cancellationToken);
                if (cancellationToken.IsCancellationRequested) return;
                if (_pptManager?.IsConnected != true) return;

                if (true) // LTSC P1c：加载动画开关随增强预览一起移除，此分支保留原行为
                {
                    await Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        _pptLoadingProgress = 0;
                        PPTLoadingRing.Data = Geometry.Empty;
                        PPTLoadingArcRotate.Angle = 0;
                        PPTEnhancedPreviewLoadingOverlay.Visibility = Visibility.Visible;
                        // LTSC L-2：这里是 new 一个 16ms(62.5Hz) 的 DispatcherTimer，
                        // 而 finally 里的 _pptLoadingTimer?.Stop() 停的是「字段当前指向的那个」。
                        // 并发预载时（上一次还没跑完又打开一个演示文稿），第二次进来直接把字段
                        // 覆盖成新实例，第一个实例既没被 Stop 也没被引用，Tick 却继续每 16ms 跑——
                        // 匿名 lambda 无法解绑，永久泄漏一个 62.5Hz 回调，越多越卡。
                        // 先停旧的再建新的。
                        _pptLoadingTimer?.Stop();
                        _pptLoadingTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
                        _pptLoadingTimer.Tick += (_, _) =>
                        {
                            PPTLoadingArcRotate.Angle = (PPTLoadingArcRotate.Angle + 8) % 360;
                            UpdatePPTLoadingRing();
                        };
                        _pptLoadingTimer.Start();
                    });
                }

                // 不用 Progress<double>（会走 SynchronizationContext 合并），直接同步调用
                var dispatcher = Application.Current.Dispatcher;
                IProgress<double> progress = new DirectProgress(v =>
                {
                    _pptLoadingProgress = v;
                    dispatcher.Invoke(() => UpdatePPTLoadingRing());
                });
                var slides = await GetOrBuildPPTEnhancedPreviewItemsAsync(cancellationToken, progress);
                if (!cancellationToken.IsCancellationRequested && slides != null && slides.Count > 0)
                {
                    LogHelper.WriteLogToFile($"PPT enhanced preview preloaded {slides.Count} thumbnails.", LogHelper.LogType.Trace);
                }
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"PPT enhanced preview preload failed: {ex}", LogHelper.LogType.Warning);
            }
            finally
            {
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _pptLoadingTimer?.Stop();
                    _pptLoadingTimer = null;
                    PPTEnhancedPreviewLoadingOverlay.Visibility = Visibility.Collapsed;
                });
            }
        }

        private Task<List<PPTEnhancedPreviewItem>> GetOrBuildPPTEnhancedPreviewItemsAsync(CancellationToken cancellationToken, IProgress<double> progress = null)
        {
            lock (_pptEnhancedPreviewCacheLock)
            {
                if (_pptEnhancedPreviewCache != null && _pptEnhancedPreviewCache.Count > 0)
                    return Task.FromResult(_pptEnhancedPreviewCache);

                if (_pptEnhancedPreviewBuildTask != null && !_pptEnhancedPreviewBuildTask.IsCompleted)
                    return _pptEnhancedPreviewBuildTask;

                int generation = _pptEnhancedPreviewCacheGeneration;
                var task = RunOnStaAsync(() => BuildPPTPreviewItems(cancellationToken, progress), cancellationToken);
                _pptEnhancedPreviewBuildTask = task;

                task.ContinueWith(
                    completedTask => StorePPTEnhancedPreviewBuildResult(completedTask, generation),
                    CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously,
                    TaskScheduler.Default);

                return task;
            }
        }

        private void StorePPTEnhancedPreviewBuildResult(Task<List<PPTEnhancedPreviewItem>> task, int generation)
        {
            List<PPTEnhancedPreviewItem> itemsToDispose = null;

            if (task.IsFaulted)
            {
                LogHelper.WriteLogToFile($"PPT enhanced preview build failed: {task.Exception?.GetBaseException()}", LogHelper.LogType.Warning);
            }

            lock (_pptEnhancedPreviewCacheLock)
            {
                if (ReferenceEquals(_pptEnhancedPreviewBuildTask, task))
                    _pptEnhancedPreviewBuildTask = null;

                if (task.Status == TaskStatus.RanToCompletion)
                {
                    var result = task.Result;
                    if (generation == _pptEnhancedPreviewCacheGeneration && result != null && result.Count > 0)
                    {
                        itemsToDispose = _pptEnhancedPreviewCache;
                        _pptEnhancedPreviewCache = result;
                    }
                    else
                    {
                        itemsToDispose = result;
                    }
                }
            }

            DisposePPTEnhancedPreviewItems(itemsToDispose);
        }

        private static void DisposePPTEnhancedPreviewItems(List<PPTEnhancedPreviewItem> items)
        {
            if (items == null) return;
            foreach (var item in items)
                item?.Dispose();
        }

        private static Task<T> RunOnStaAsync<T>(Func<T> func, CancellationToken cancellationToken = default)
        {
            // Office interop 要求 STA + COM 单元；Task.Run 跑到 MTA 线程池里会触发 RPC_E_WRONG_THREAD
            // 等随机 COM 失败，表现为增强预览空白或崩溃。显式创建 STA worker 在其中执行导出。
            if (cancellationToken.IsCancellationRequested)
                return Task.FromCanceled<T>(cancellationToken);

            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            var thread = new Thread(() =>
            {
                try
                {
                    if (cancellationToken.IsCancellationRequested)
                    {
                        tcs.TrySetCanceled(cancellationToken);
                        return;
                    }

                    tcs.TrySetResult(func());
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    tcs.TrySetCanceled(cancellationToken);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            });
            thread.IsBackground = true;
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            return tcs.Task;
        }

        private List<PPTEnhancedPreviewItem> BuildPPTPreviewItems(CancellationToken cancellationToken, IProgress<double> progress = null)
        {
            var result = new List<PPTEnhancedPreviewItem>();

            try
            {
                // 阶段1：COM导出缩略图 → 进度 0~70%
                var exportProgress = progress != null
                    ? new Progress<double>(p => progress.Report(p * 0.7))
                    : null;
                var thumbnails = _pptManager?.ExportSlideThumbnails(480, 270, exportProgress);
                if (thumbnails == null || thumbnails.Count == 0) return result;

                // 阶段2：转换为 BitmapImage → 进度 70~100%
                int total = thumbnails.Count;
                int converted = 0;
                foreach (var thumbnail in thumbnails)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (thumbnail?.PngBytes == null || thumbnail.PngBytes.Length == 0) { converted++; continue; }

                    var thumbnailStream = new MemoryStream(thumbnail.PngBytes, false);
                    var image = LoadBitmapImage(thumbnailStream);
                    if (image == null)
                    {
                        thumbnailStream.Dispose();
                        converted++;
                        continue;
                    }

                    thumbnailStream.Position = 0;
                    result.Add(new PPTEnhancedPreviewItem
                    {
                        SlideNumber = thumbnail.SlideNumber,
                        ThumbnailStream = thumbnailStream,
                        Thumbnail = image
                    });
                    converted++;
                    progress?.Report(0.7 + 0.3 * converted / total);
                }
            }
            catch (OperationCanceledException)
            {
                DisposePPTEnhancedPreviewItems(result);
                throw;
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"构建PPT增强预览列表失败: {ex}", LogHelper.LogType.Error);
            }

            return result;
        }

        private static BitmapImage LoadBitmapImage(MemoryStream stream)
        {
            try
            {
                if (stream == null || stream.Length == 0) return null;
                lock (stream)
                {
                    stream.Position = 0;
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.StreamSource = stream;
                    bitmap.EndInit();
                    bitmap.Freeze();
                    stream.Position = 0;
                    return bitmap;
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 处理“开始幻灯片放映”按钮的点击事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">路由事件参数</param>
        /// <remarks>
        /// 该方法在用户点击“开始幻灯片放映”按钮时执行以下操作：
        /// 1. 在新线程中尝试启动PPT幻灯片放映
        /// 2. 如果启动失败，记录警告日志
        /// 3. 捕获并记录可能的异常
        /// </remarks>
        private void BtnPPTSlideShow_Click(object sender, RoutedEventArgs e)
        {
            new Thread(() =>
            {
                try
                {
                    if (_pptManager?.TryStartSlideShow() != true)
                    {
                        LogHelper.WriteLogToFile("启动幻灯片放映失败", LogHelper.LogType.Warning);
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"启动幻灯片放映异常: {ex}", LogHelper.LogType.Error);
                }
            }).Start();
        }

        public async Task ExitPPTPresentation()
        {
            try
            {
                var currentSlide = Math.Max(0, _pptManager?.GetCurrentSlideNumber() ?? 0);
                if (currentSlide > 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        if (inkCanvas?.Strokes != null && inkCanvas.Strokes.Count > 0)
                        {
                            var ms = new MemoryStream();
                            inkCanvas.Strokes.Save(ms);
                            ms.Position = 0;
                            // LTSC A-8：这里是 _memoryStreams 唯一一处没加锁的访问
                            // （同文件另外 11 处都在 lock (_memoryStreams) 里）。
                            // 由 PPT 切页路径调用，和后台保存线程可能同时碰。
                            lock (_memoryStreams)
                            {
                                if (_memoryStreams.ContainsKey(currentSlide))
                                    _memoryStreams[currentSlide]?.Dispose();
                                _memoryStreams[currentSlide] = ms;
                            }
                        }
                        timeMachine.ClearStrokeHistory();
                    });
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CursorIcon_Click(null, null);
                });

                await Task.Delay(100);
                await Application.Current.Dispatcher.InvokeAsync(() => { }, DispatcherPriority.Background);

                _ = Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        _pptManager?.TryEndSlideShow();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"结束放映时发生异常: {ex}", LogHelper.LogType.Error);
                    }
                }), DispatcherPriority.Normal);

                await Task.Delay(150);
                if (!isFloatingBarFolded)
                {
                    PureViewboxFloatingBarMarginAnimationInDesktopMode();
                    if (Settings.Automation.IsAutoEnterAnnotationModeWhenExitFoldMode)
                    {
                        await Task.Delay(350);
                        if (!isFloatingBarFolded)
                        {
                            ViewboxFloatingBarMarginAnimation(-60);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"结束PPT放映操作异常: {ex}", LogHelper.LogType.Error);

                // 确保UI状态正确
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _pptUIManager?.UpdateSlideShowStatus(false);
                    _pptUIManager?.UpdateSidebarExitButtons(false);
                    HideFloatingBarExitPPTBtn();
                    CheckMainWindowVisibility();
                });

                // 异常情况下也手动处理自动收纳
                await HandleManualSlideShowEnd();

                await Task.Delay(150);
                if (!isFloatingBarFolded)
                {
                    PureViewboxFloatingBarMarginAnimationInDesktopMode();
                    if (Settings.Automation.IsAutoEnterAnnotationModeWhenExitFoldMode)
                    {
                        await Task.Delay(350);
                        if (!isFloatingBarFolded)
                        {
                            ViewboxFloatingBarMarginAnimation(-60);
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 手动处理PPT放映结束时的自动收纳
        /// </summary>
        private async Task HandleManualSlideShowEnd()
        {
            try
            {
                await Application.Current.Dispatcher.InvokeAsync(() => CollapseAllPPTNavBarPreviews());

                if (Settings.Automation.IsAutoFoldAfterPPTSlideShow && !isFloatingBarFolded)
                {
                    FoldFloatingBar_MouseUp(new object(), null);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"手动处理PPT放映结束自动收纳失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理PPT上一页控制按钮的鼠标按下事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 该方法在用户按下PPT上一页控制按钮时执行以下操作：
        /// 1. 记录按下的按钮对象
        /// 2. 根据按下的按钮设置相应的反馈边框透明度
        /// 3. 如果启用了PPT按钮长按翻页功能，则启动长按检测
        /// </remarks>
        private void GridPPTControlPrevious_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 旧 XAML 入口已废弃，事件由 PPTNavBar 控件转发；保留方法签名以兼容潜在外部引用。
        }
        private void GridPPTControlPrevious_MouseLeave(object sender, MouseEventArgs e)
        {
            StopLongPressDetection();
        }
        private void GridPPTControlPrevious_MouseUp(object sender, MouseButtonEventArgs e)
        {
            StopLongPressDetection();
            BtnPPTSlidesUp_Click(null, null);
        }


        private void GridPPTControlNext_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // 旧 XAML 入口已废弃，事件由 PPTNavBar 控件转发；保留方法签名以兼容潜在外部引用。
        }
        private void GridPPTControlNext_MouseLeave(object sender, MouseEventArgs e)
        {
            StopLongPressDetection();
        }
        private void GridPPTControlNext_MouseUp(object sender, MouseButtonEventArgs e)
        {
            StopLongPressDetection();
            BtnPPTSlidesDown_Click(null, null);
        }

        /// <summary>
        /// 处理PPT结束控制按钮的鼠标释放事件
        /// </summary>
        /// <param name="sender">事件的来源对象</param>
        /// <param name="e">鼠标按钮事件参数</param>
        /// <remarks>
        /// 该方法在用户释放PPT结束控制按钮时调用BtnPPTSlideShowEnd_Click方法，实现结束幻灯片放映的功能
        /// </remarks>
        internal async void ImagePPTControlEnd_MouseUp(object sender, MouseButtonEventArgs e)
        {
            await ExitPPTPresentation();
        }

        private void ShowFloatingBarExitPPTBtn()
        {
            UpdateToolbarComponentVisibility();
        }

        private void HideFloatingBarExitPPTBtn()
        {
            UpdateToolbarComponentVisibility();
        }
    }
}
