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
        // LTSC（2026-11-17）：从 MW_FloatingBarIcons.cs 按 #region 拆出。
        // 只搬代码，不改逻辑。using 区整块复制（原文有 7 条别名）。
        #region New PPT Event Handlers
        /// <summary>
        /// 处理 PowerPoint 连接状态的变更：更新界面连接/放映状态，并在断开时启动一个短延迟以安全退出 PPT 模式。
        /// </summary>
        /// <param name="isConnected">指示当前是否已与 PowerPoint 建立连接；`true` 表示已连接，`false` 表示已断开。</param>
        private void OnPPTConnectionChanged(bool isConnected)
        {
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    _pptUIManager?.UpdateConnectionStatus(isConnected);

                    if (isConnected)
                    {
                        _exitPPTModeAfterDisconnectTimer?.Stop();
                        _exitPPTModeAfterDisconnectTimer = null;
                        SchedulePPTEnhancedPreviewPreload();
                        LogHelper.WriteLogToFile("PPT连接已建立", LogHelper.LogType.Event);
                    }
                    else
                    {
                        LogHelper.WriteLogToFile("PPT连接已断开", LogHelper.LogType.Event);
                        _singlePPTInkManager?.ClearAllStrokes();
                        CollapseAllPPTNavBarPreviews();
                        ResetPPTEnhancedPreviewCache();
                        _exitPPTModeAfterDisconnectTimer?.Stop();
                        _exitPPTModeAfterDisconnectTimer = null;
                        _pptUIManager?.UpdateSlideShowStatus(false);
                        _pptUIManager?.UpdateSidebarExitButtons(false);

                        // 隐藏浮动栏退出PPT按钮
                        HideFloatingBarExitPPTBtn();
                        ResetPPTStateVariables();
                        _ = HandleManualSlideShowEnd();

                        CheckMainWindowVisibility();
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理PPT连接状态变化失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理 PowerPoint 演示文稿打开事件：清理画布墨迹、初始化墨迹管理器、处理导航逻辑、检查隐藏幻灯片和自动播放设置，并更新连接状态。
        /// </summary>
        /// <param name="pres">已打开的 PowerPoint 演示文稿（Presentation）实例。</param>
        /// <remarks>
        /// 操作包括：清理画布墨迹和备份历史记录，初始化墨迹管理器，处理跳转到首页或上次播放页的逻辑，检查隐藏幻灯片和自动播放设置，更新UI连接状态，并记录事件日志。
        /// 所有操作在UI线程异步执行，异常会被捕获并记录为错误日志。
        /// </remarks>
        private void OnPPTPresentationOpen(object payload)
        {
            var pres = payload as Presentation;
            var agentState = (PPTState)null; // LTSC D-02: payload 恒为 Presentation
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 在初始化墨迹管理器之前，先清理画布上的所有墨迹
                    ResetPPTEnhancedPreviewCache();

                    ClearStrokes(true);

                    // 清理备份历史记录，防止旧演示文稿的墨迹影响新演示文稿
                    if (TimeMachineHistories != null && TimeMachineHistories.Length > 0)
                    {
                        TimeMachineHistories[0] = null;
                    }

                    if (pres != null)
                    {
                        _singlePPTInkManager?.InitializePresentation(pres);
                    }

                    // 处理跳转到首页或上次播放页的逻辑
                    HandlePresentationOpenNavigation(pres, agentState);

                    // 检查隐藏幻灯片
                    if (Settings.PowerPointSettings.IsNotifyHiddenPage)
                    {
                        CheckAndNotifyHiddenSlides(pres, agentState);
                    }

                    // 检查自动播放设置
                    if (Settings.PowerPointSettings.IsNotifyAutoPlayPresentation)
                    {
                        CheckAndNotifyAutoPlaySettings(pres, agentState);
                    }

                    _pptUIManager?.UpdateConnectionStatus(true);

                    SchedulePPTEnhancedPreviewPreload();

                    LogHelper.WriteLogToFile($"已打开新演示文稿: {pres?.Name ?? agentState?.PresentationName ?? _pptManager?.GetPresentationName()}，墨迹状态已清理", LogHelper.LogType.Event);
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理演示文稿打开事件失败: {ex}", LogHelper.LogType.Error);
            }
        }

        private string GetPresentationStrokeFolderPath(Presentation presentation, string presentationName, int totalSlides, string presentationFullName = null)
        {
            string basePath = Path.Combine(Settings.Automation.AutoSavedStrokesLocation, "Auto Saved - Presentations");
            string fullName = presentationFullName;
            string name = presentationName;
            int slidesTotal = totalSlides;

            if (presentation != null)
            {
                try
                {
                    if (string.IsNullOrEmpty(fullName)) fullName = presentation.FullName;
                    if (string.IsNullOrEmpty(name)) name = presentation.Name;
                    if (slidesTotal <= 0) slidesTotal = presentation.Slides.Count;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"读取演示文稿标识失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }

            if (!string.IsNullOrEmpty(fullName))
            {
                string hash = HashHelper.GetFileHash(fullName);
                return Path.Combine(basePath, $"{name ?? ""}_{slidesTotal}_{hash}");
            }

            return Path.Combine(basePath, $"{name ?? ""}_{slidesTotal}");
        }

        private void OnPPTPresentationClose(object payload)
        {
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    CollapseAllPPTNavBarPreviews();
                    ResetPPTEnhancedPreviewCache();

                    lock (_memoryStreams)
                    {
                        foreach (var stream in _memoryStreams.Values)
                            stream?.Dispose();
                        _memoryStreams.Clear();
                    }

                    _pptUIManager?.UpdateConnectionStatus(false);
                });
            }
            catch (COMException comEx)
            {
                // COM对象已失效，这是正常情况，完全静默处理
                var hr = (uint)comEx.HResult;
                if (hr == 0x8001010E || hr == 0x80004005 || hr == 0x800706BA || hr == 0x800706BE || hr == 0x80048010)
                {
                }
            }
            catch (Exception)
            {
            }
        }

        /// <summary>
        /// 处理 PowerPoint 幻灯片放映状态变化事件：更新UI管理器的放映状态并检查主窗口可见性。
        /// </summary>
        /// <param name="isInSlideShow">指示当前是否处于幻灯片放映状态；`true` 表示正在放映，`false` 表示已退出放映。</param>
        /// <remarks>
        /// 操作包括：在UI线程异步通知UI管理器放映状态变化，检查并更新主窗口的可见性（用于仅PPT模式）。
        /// 异常会被捕获并记录为错误日志，确保方法执行不会中断。
        /// </remarks>
        private void OnPPTSlideShowStateChanged(bool isInSlideShow)
        {
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    // 通知UI管理器放映状态变化
                    _pptUIManager?.OnSlideShowStateChanged(isInSlideShow);
                    NotifyPluginSlideShowStateChanged(isInSlideShow);

                    if (!isInSlideShow)
                    {
                        CollapseAllPPTNavBarPreviews();
                    }

                    // 检查主窗口可见性（用于仅PPT模式）
                    CheckMainWindowVisibility();
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理PPT放映状态变化失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理 PowerPoint 幻灯片放映开始事件：根据设置折叠或展开浮动栏，初始化放映状态，更新UI，加载当前页墨迹，并设置相关参数。
        /// </summary>
        /// <param name="wn">PowerPoint 幻灯片放映窗口（SlideShowWindow）实例，包含当前放映状态和视图信息。</param>
        /// <remarks>
        /// 操作包括：
        /// 1. 根据设置自动折叠或展开浮动栏
        /// 2. 停止墨迹重放
        /// 3. 获取当前活动演示文稿、当前幻灯片和总幻灯片数
        /// 4. 初始化墨迹管理器
        /// 5. 处理跳转到首页或上次播放位置的逻辑
        /// 6. 更新UI状态，包括放映状态、当前幻灯片编号
        /// 7. 设置浮动栏透明度和边距
        /// 8. 显示侧边栏退出按钮
        /// 9. 处理画板显示
        /// 10. 关闭白板模式（如果当前在白板模式）
        /// 11. 显示浮动栏主控件
        /// 12. 根据设置隐藏或显示手势面板和按钮
        /// 13. 如果设置了在新放映时显示画布，则进入批注模式并显示调色盘
        /// 14. 重置幻灯片放映结束事件标志
        /// 15. 加载当前页墨迹
        /// 16. 调整浮动栏边距动画
        /// 
        /// 所有UI操作在UI线程异步执行，异常会被捕获并记录为错误日志。
        /// </remarks>
        private async void OnPPTSlideShowBegin(object payload)
        {
            var wn = payload as SlideShowWindow;
            var agentState = (PPTState)null; // LTSC D-02: payload 恒为 Presentation
            try
            {
                if (Settings.Automation.IsAutoFoldInPPTSlideShow)
                {
                    if (!isFloatingBarFolded)
                        FoldFloatingBar_MouseUp(new object(), null);
                }
                else
                {
                    if (isFloatingBarFolded)
                    {
                        await UnFoldFloatingBar(new object());
                    }
                }

                isStopInkReplay = true;

                int currentSlide = 0;
                int totalSlides = 0;
                string presentationName = null;
                string presentationFullName = null;
                Presentation activePresentation = null;

                if (wn != null)
                {
                    try
                    {
                        if (wn.View != null && wn.Presentation != null)
                        {
                            activePresentation = wn.Presentation;
                            currentSlide = wn.View.CurrentShowPosition;
                            totalSlides = activePresentation.Slides.Count;
                            presentationName = activePresentation.Name;
                            presentationFullName = activePresentation.FullName;
                        }
                    }
                    catch (COMException comEx)
                    {
                        var hr = (uint)comEx.HResult;
                        activePresentation = null;
                        currentSlide = 0;
                        totalSlides = 0;
                        presentationName = null;
                    }
                    catch (Exception)
                    {
                        activePresentation = null;
                        currentSlide = 0;
                        totalSlides = 0;
                        presentationName = null;
                    }
                }

                if (activePresentation == null)
                {
                    // LTSC D-02: Agent 架构已移除，agentState 不再有来源，直接走 COM/ROT 取值。
                    if (agentState != null)
                    {
                        currentSlide = agentState.SlideIndex;
                        totalSlides = agentState.TotalSlides;
                        presentationName = agentState.PresentationName;
                        presentationFullName = agentState.PresentationFullName;
                    }
                    else
                    {
                        activePresentation = _pptManager?.GetCurrentActivePresentation() as Presentation;
                        currentSlide = Math.Max(0, _pptManager?.GetCurrentSlideNumber() ?? 0);
                        totalSlides = _pptManager?.SlidesCount ?? 0;
                        presentationName = _pptManager?.GetPresentationName() ?? activePresentation?.Name;
                    }
                }

                _currentSlideShowPosition = currentSlide;
                _previousSlideID = currentSlide;
                ApplyPPTNavBarAppearanceFromSettings();
                NotifyPluginSlideShowStateChanged(true);

                lock (_memoryStreams)
                {
                    foreach (var stream in _memoryStreams.Values)
                        stream?.Dispose();
                    _memoryStreams.Clear();
                }

                if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint && !string.IsNullOrEmpty(presentationName))
                {
                    string strokePath = GetPresentationStrokeFolderPath(activePresentation, presentationName, totalSlides, presentationFullName);
                    if (Directory.Exists(strokePath))
                    {
                        await Task.Run(() =>
                        {
                            try
                            {
                                var files = new DirectoryInfo(strokePath).GetFiles("*.icstk");
                                foreach (var file in files)
                                {
                                    int pageNum = 0;
                                    try
                                    {
                                        string name = Path.GetFileNameWithoutExtension(file.Name);
                                        if (int.TryParse(name, out pageNum) && pageNum > 0)
                                        {
                                            byte[] bytes = File.ReadAllBytes(file.FullName);
                                            if (bytes.Length > 8)
                                            {
                                                lock (_memoryStreams)
                                                {
                                                    _memoryStreams[pageNum] = new MemoryStream(bytes);
                                                    _memoryStreams[pageNum].Position = 0;
                                                }
                                            }
                                        }
                                    }
                                    catch (Exception ex)
                                    {
                                        LogHelper.WriteLogToFile($"加载第 {pageNum} 页墨迹文件失败: {ex}", LogHelper.LogType.Warning);
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                LogHelper.WriteLogToFile($"加载PPT墨迹文件失败: {ex}", LogHelper.LogType.Error);
                            }
                        });
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(async () =>
                {
                    if (activePresentation != null && _singlePPTInkManager != null)
                    {
                        try
                        {
                            _singlePPTInkManager.InitializePresentation(activePresentation);
                        }
                        catch (Exception)
                        {
                        }
                    }

                    // 处理跳转到首页或上次播放位置
                    if (Settings.PowerPointSettings.IsAlwaysGoToFirstPageOnReenter)
                    {
                        _pptManager?.TryNavigateToSlide(1);
                        if (Settings.PowerPointSettings.SkipAnimationsWhenGoNext) ExceptionHandler.TryExecute(() => this.Activate(), "激活主窗口失败（PPT 重入首页时）");
                    }
                    else if (_shouldNavigateToLastPage && _lastPlaybackPage > 0)
                    {
                        _pptManager?.TryNavigateToSlide(_lastPlaybackPage);
                        _shouldNavigateToLastPage = false;
                        if (Settings.PowerPointSettings.SkipAnimationsWhenGoNext) ExceptionHandler.TryExecute(() => this.Activate(), "激活主窗口失败（PPT 重入末页时）");
                    }

                    // 更新UI状态
                    _pptUIManager?.UpdateSlideShowStatus(true, currentSlide, totalSlides);

                    // 设置浮动栏透明度和边距
                    _pptUIManager?.SetFloatingBarOpacity(Settings.Appearance.ViewboxFloatingBarOpacityInPPTValue);
                    ApplyFloatingBarMenuOpacity();
                    _pptUIManager?.SetMainPanelMargin(new Thickness(10, 10, 10, 10));

                    // 显示侧边栏退出按钮
                    _pptUIManager?.UpdateSidebarExitButtons(true);

                    // 显示浮动栏退出PPT按钮
                    ShowFloatingBarExitPPTBtn();

                    // 处理画板显示
                    if (Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow &&
                        !Settings.Automation.IsAutoFoldInPPTSlideShow &&
                        GridTransparencyFakeBackground.Background == Brushes.Transparent && !isFloatingBarFolded)
                    {
                        BtnHideInkCanvas_Click(null, null);
                    }

                    if (currentMode != 0)
                    {
                        ImageBlackboard_MouseUp(null, null);
                        BtnHideInkCanvas_Click(null, null);
                    }

                    SetFloatingBarContentVisibility(true);

                    // 在PPT模式下根据设置决定是否隐藏手势面板和手势按钮
                    AnimationsHelper.HideWithSlideAndFade(TwoFingerGestureBorder);
                    AnimationsHelper.HideWithSlideAndFade(BoardTwoFingerGestureBorder);

                    // 根据设置决定是否在PPT放映模式下显示手势按钮
                    UpdateToolbarComponentVisibility();

                    if (Settings.PowerPointSettings.IsShowCanvasAtNewSlideShow &&
                        !Settings.Automation.IsAutoFoldInPPTSlideShow)
                    {
                        await Task.Delay(300);
                        // 先进入批注模式，这会显示调色盘
                        PenIcon_Click(null, null);
                        // 然后设置颜色
                        BtnColorRed_Click(null, null);
                        try
                        {
                            if (inkCanvas.EditingMode == InkCanvasEditingMode.Ink)
                            {
                                UpdateCurrentToolMode("pen");
                                SetFloatingBarHighlightPosition("pen");
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"PPT进入批注模式后同步浮动栏高光状态失败: {ex.Message}", LogHelper.LogType.Error);
                        }
                    }

                    isEnteredSlideShowEndEvent = false;

                    // 加载当前页墨迹
                    LoadCurrentSlideInk(currentSlide);

                    // 仅PPT模式：放映开始立即同步主窗口可见性（勿仅依赖 SlideShowStateChanged 定时器）
                    CheckMainWindowVisibility();

                });

                if (!isFloatingBarFolded)
                {
                    new Thread(() =>
                    {
                        Thread.Sleep(100);
                        Application.Current.Dispatcher.Invoke(() =>
                        {
                            ViewboxFloatingBarMarginAnimation(60);
                        });
                    }).Start();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理幻灯片放映开始事件失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理幻灯片放映中的切换：在幻灯片变更时保存当前页墨迹、加载目标页墨迹并更新界面状态。
        /// </summary>
        /// <param name="wn">当前的幻灯片放映窗口；若为 null 或其 View/Presentation 无效则方法不执行。</param>
        /// <remarks>
        /// - 如果收到与当前记录相同的页码或已有切换正在处理，则忽略该事件。 
        /// - 在切换过程中会保存前一页的墨迹（如存在）、清空画布与历史、加载新页的墨迹、锁定新页墨迹并刷新当前页显示序号，同时更新内部的当前播放位置状态。
        /// </remarks>
        private void OnPPTSlideShowNextSlide(object payload)
        {
            var wn = payload as SlideShowWindow;
            var agentState = (PPTState)null; // LTSC D-02: payload 恒为 Presentation
            try
            {
                int currentSlide = 0;
                int totalSlides = 0;

                if (wn != null)
                {
                    try
                    {
                        if (wn.View != null)
                        {
                            currentSlide = wn.View.CurrentShowPosition;
                        }
                    }
                    catch (COMException comEx)
                    {
                        var hr = (uint)comEx.HResult;
                        LogHelper.WriteLogToFile(
                            $"通过 SlideShowWindow.View 获取当前页失败: {comEx.Message} (HR: 0x{hr:X8})，将回退到 PPT 管理器获取",
                            LogHelper.LogType.Warning);
                        currentSlide = 0;
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile(
                            $"通过 SlideShowWindow.View 获取当前页时发生异常，将回退到 PPT 管理器获取: {ex}",
                            LogHelper.LogType.Warning);
                        currentSlide = 0;
                    }
                }

                if (currentSlide <= 0)
                {
                    currentSlide = agentState?.SlideIndex ?? Math.Max(0, _pptManager?.GetCurrentSlideNumber() ?? 0);
                }

                totalSlides = agentState?.TotalSlides ?? _pptManager?.SlidesCount ?? 0;

                if (currentSlide == _previousSlideID) return;

                int prev = _previousSlideID;

                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    MemoryStream ms;
                    if (inkCanvas.Strokes.Count > 0)
                    {
                        ms = new MemoryStream();
                        inkCanvas.Strokes.Save(ms);
                        ms.Position = 0;
                    }
                    else
                    {
                        ms = new MemoryStream();
                    }

                    lock (_memoryStreams)
                    {
                        if (_memoryStreams.ContainsKey(prev))
                            _memoryStreams[prev]?.Dispose();
                        _memoryStreams[prev] = ms;
                    }

                    ClearStrokes(true);
                    timeMachine.ClearStrokeHistory();

                    _currentSlideShowPosition = currentSlide;
                    _singlePPTInkManager?.LockInkForSlide(currentSlide);
                    _pptUIManager?.UpdateCurrentSlideNumber(currentSlide, totalSlides);

                    byte[] bytesToLoad = null;
                    lock (_memoryStreams)
                    {
                        if (_memoryStreams.ContainsKey(currentSlide) && _memoryStreams[currentSlide] != null)
                            bytesToLoad = _memoryStreams[currentSlide].ToArray();
                    }
                    if (bytesToLoad != null)
                    {
                        if (bytesToLoad.Length > 8)
                        {
                            int loadingPage = currentSlide;
                            Task.Run(() =>
                            {
                                try
                                {
                                    return new StrokeCollection(new MemoryStream(bytesToLoad));
                                }
                                catch (Exception ex)
                                {
                                    LogHelper.WriteLogToFile($"从内存流加载第 {loadingPage} 页墨迹失败: {ex}", LogHelper.LogType.Warning);
                                    return null;
                                }
                            }).ContinueWith(t =>
                            {
                                if (t.IsFaulted || t.Result == null) return;
                                Application.Current.Dispatcher.InvokeAsync(() =>
                                {
                                    if (_currentSlideShowPosition != loadingPage) return;
                                    inkCanvas.Strokes.Add(t.Result);
                                });
                            });
                        }
                    }
                });
                _previousSlideID = currentSlide;
                NotifyPluginSlideChanged(currentSlide);

                // LTSC D-07：这次切页若是「点页码进 PPT 原生网格选页」引发的，
                // 自动回到批注态。挂在这里是因为 PowerPoint 的网格选页只发 COM 翻页事件，
                // 不经过 ICC 自己的 PPTNavBar（SlideSelected 不会触发）。
                if (_pptNavPendingRestoreAnnotation)
                {
                    int targetPage = currentSlide;
                    Application.Current?.Dispatcher.InvokeAsync(async () =>
                    {
                        try
                        {
                            // 等墨迹切到新页（上面那段 Dispatcher 里的保存/恢复）跑完。
                            await Task.Delay(500);
                            TryRestoreAnnotationAfterPptNavigation($"切到第 {targetPage} 页");
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"[PPT导航] 延迟恢复异常: {ex.Message}", LogHelper.LogType.Warning);
                        }
                    });
                }

                // 转发PPT翻页事件到小白板（如果已打开且启用了联动）
                _miniWhiteboardWindow?.OnPPTSlideChangedExternal(currentSlide - 1);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理幻灯片切换事件失败: {ex}", LogHelper.LogType.Error);
            }
        }

        // LTSC D-06: 「智慧模式」（视频控件穿透区域）整块移除，见 REMOVED.md。

        /// <summary>
        /// 处理 PowerPoint 幻灯片放映结束时的清理与界面恢复，包括保存当前幻灯片墨迹、重置墨迹管理器状态、恢复主题与工具栏显示，并根据配置折叠或展示浮动工具栏等 UI 调整。
        /// </summary>
        /// <param name="pres">触发结束事件的 PowerPoint 演示文稿（Presentation）实例，用于保存墨迹并尝试读取放映时的当前页码。</param>
        private async void OnPPTSlideShowEnd(object payload)
        {
            var pres = payload as Presentation;
            var agentState = (PPTState)null; // LTSC D-02: payload 恒为 Presentation
            try
            {
                // ── LTSC P5 / C4（2026-11-11）────────────────────────────────
                // 社区原话：「ppt 退出直接卡死（严重）**关闭长按翻页修复**」——
                // 用户自己已经试出来规避方法，说明卡死和长按定时器直接相关。
                //
                // LTSC P1c：长按连续翻页已整体移除（实测从未生效）。
                //       BtnPPTSlidesDown_Click → Task.Run 冲 COM。
                //       而本函数（整个 247 行）原来**一次都没停过这个定时器**。
                // 于是按住翻页按钮时退出放映，定时器还在跑，50ms 一次去碰
                // 正在销毁的 COM 对象 —— PPT 卡死。
                //
                // 修法：退出放映第一件事就停掉长按检测，并清掉「这次按下已被消费」
                // 标记，避免下一次进入放映时手还没松就误判。
                StopLongPressDetection();
                MarkAllNavBarsLongPressConsumed();
                await Application.Current.Dispatcher.InvokeAsync(() => CollapseAllPPTNavBarPreviews());

                if (Settings.Automation.IsAutoFoldAfterPPTSlideShow && !isFloatingBarFolded)
                {
                    FoldFloatingBar_MouseUp(new object(), null);
                }

                if (isEnteredSlideShowEndEvent) return;
                isEnteredSlideShowEndEvent = true;
                NotifyPluginSlideShowStateChanged(false);

                // 获取当前播放页码，优先使用跟踪的页码，否则尝试从PPT管理器获取
                int currentPage = _currentSlideShowPosition;
                if (currentPage <= 0)
                {
                    try
                    {
                        currentPage = Math.Max(0, _pptManager?.GetCurrentSlideNumber() ?? 0);
                    }
                    catch
                    {
                        // 如果无法获取，尝试从演示文稿的SlideShowWindow获取
                        try
                        {
                            if (pres != null && pres.SlideShowWindow != null && pres.SlideShowWindow.View != null)
                            {
                                currentPage = pres.SlideShowWindow.View.CurrentShowPosition;
                            }
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (currentPage > 0 && inkCanvas?.Strokes != null)
                    {
                        MemoryStream ms;
                        if (inkCanvas.Strokes.Count > 0)
                        {
                            ms = new MemoryStream();
                            inkCanvas.Strokes.Save(ms);
                            ms.Position = 0;
                        }
                        else
                        {
                            ms = new MemoryStream();
                        }

                        lock (_memoryStreams)
                        {
                            if (_memoryStreams.ContainsKey(currentPage))
                                _memoryStreams[currentPage]?.Dispose();
                            _memoryStreams[currentPage] = ms;
                        }
                    }
                });

                string presentationNameForSave = agentState?.PresentationName ?? _pptManager?.GetPresentationName() ?? (pres != null ? pres.Name : null);
                string presentationFullNameForSave = agentState?.PresentationFullName;
                int totalSlidesForSave = agentState?.TotalSlides ?? _pptManager?.SlidesCount ?? 0;
                if (totalSlidesForSave <= 0 && pres != null)
                {
                    try
                    {
                        totalSlidesForSave = pres.Slides.Count;
                    }
                    catch (COMException comEx)
                    {
                        var hr = (uint)comEx.HResult;
                        if (hr != 0x80048240 && hr != 0x80010108 && hr != 0x800706BA && hr != 0x800706BE)
                            LogHelper.WriteLogToFile($"读取放映结束总页数失败: {comEx.Message}", LogHelper.LogType.Warning);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"读取放映结束总页数失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                }

                if (currentPage > 0 && Settings.PowerPointSettings.IsNotifyPreviousPage && !string.IsNullOrEmpty(presentationNameForSave) && totalSlidesForSave > 0)
                {
                    try
                    {
                        string folderPathForPosition = GetPresentationStrokeFolderPath(pres, presentationNameForSave, totalSlidesForSave, presentationFullNameForSave);
                        if (!Directory.Exists(folderPathForPosition))
                            Directory.CreateDirectory(folderPathForPosition);
                        File.WriteAllText(Path.Combine(folderPathForPosition, "Position"), currentPage.ToString());
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"保存上次播放位置失败: {ex}", LogHelper.LogType.Warning);
                    }
                }

                if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint && !string.IsNullOrEmpty(presentationNameForSave) && totalSlidesForSave > 0)
                {
                    string folderPathForSave = GetPresentationStrokeFolderPath(pres, presentationNameForSave, totalSlidesForSave, presentationFullNameForSave);
                    await Task.Run(() =>
                    {
                        try
                        {
                            if (!Directory.Exists(folderPathForSave))
                                Directory.CreateDirectory(folderPathForSave);

                            lock (_memoryStreams)
                            {
                                for (int i = 1; i <= totalSlidesForSave; i++)
                                {
                                    if (_memoryStreams.TryGetValue(i, out MemoryStream value) && value != null)
                                    {
                                        try
                                        {
                                            byte[] allBytes = value.ToArray();
                                            string filePath = Path.Combine(folderPathForSave, i.ToString("0000") + ".icstk");
                                            if (allBytes.Length > 8)
                                                File.WriteAllBytes(filePath, allBytes);
                                            else if (File.Exists(filePath))
                                                File.Delete(filePath);
                                        }
                                        catch (Exception ex)
                                        {
                                            LogHelper.WriteLogToFile($"为第 {i} 页保存墨迹文件失败: {ex}", LogHelper.LogType.Warning);
                                        }
                                    }
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            LogHelper.WriteLogToFile($"保存PPT墨迹文件失败: {ex}", LogHelper.LogType.Error);
                        }
                        finally
                        {
                            lock (_memoryStreams)
                            {
                                foreach (var stream in _memoryStreams.Values)
                                    stream?.Dispose();
                                _memoryStreams.Clear();
                            }
                        }
                    });
                }
                else
                {
                    lock (_memoryStreams)
                    {
                        foreach (var stream in _memoryStreams.Values)
                            stream?.Dispose();
                        _memoryStreams.Clear();
                    }
                }

                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        isPresentationHaveBlackSpace = false;

                        // 恢复主题
                        if (ThemeManager.Current.ApplicationTheme == ApplicationTheme.Light)
                        {
                            { /* Old UI removed */ }
                            ThemeManager.Current.ApplicationTheme = ApplicationTheme.Light;
                        }

                        // 更新UI状态
                        _pptUIManager?.UpdateSlideShowStatus(false);
                        _pptUIManager?.UpdateSidebarExitButtons(false);

                        // 隐藏浮动栏退出PPT按钮
                        HideFloatingBarExitPPTBtn();

                        _pptUIManager?.SetMainPanelMargin(new Thickness(10, 10, 10, 55));
                        _pptUIManager?.SetFloatingBarOpacity(Settings.Appearance.ViewboxFloatingBarOpacityValue);
                        ApplyFloatingBarMenuOpacity();

                        if (currentMode != 0)
                        {
                            CloseWhiteboardImmediately();
                            currentMode = 0;
                            AutomationBootstrap.Monitor?.NotifyInternalStateChanged();
                        }

                        SyncPdfPageSidebarWithCanvas();

                        ClearStrokes(true);
                        // 清空备份历史记录，防止退出白板时恢复已结束PPT的墨迹
                        // 注意：这里只清空索引0的备份，不影响白板页面的墨迹（索引1及以上）
                        TimeMachineHistories[0] = null;

                        // 重置墨迹管理器的锁定状态，防止下次放映时墨迹显示错误
                        ResetInkManagerLockState();

                        // 退出PPT模式时恢复手势面板和手势按钮的显示状态
                        UpdateToolbarComponentVisibility();

                        // 注意：快捷调色盘的可见性现在完全由工具栏规则集管理
                        // 不需要手动设置，UpdateToolbarComponentVisibility 会处理好

                        // LTSC（2026-11-13）：原来靠 BtnHideInkCanvas_Click 顺带清理，
                        // 但那条路径只在特定状态下生效。这里改成无条件清理，
                        // 避免透明遮罩残留盖住底部浮动栏。
                        ResetPptTransparencyOverlay("退出PPT模式");
                        SetCurrentToolMode(InkCanvasEditingMode.None);

                        UpdateCurrentToolMode("cursor");
                        SetFloatingBarHighlightPosition("cursor");

                        CheckMainWindowVisibility();
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"处理幻灯片放映结束UI更新失败: {ex}", LogHelper.LogType.Error);
                    }
                });

                await Task.Delay(100);
                await Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (!isFloatingBarFolded)
                    {
                        PureViewboxFloatingBarMarginAnimationInDesktopMode();
                        if (Settings.Automation.IsAutoEnterAnnotationModeWhenExitFoldMode)
                        {
                            Task.Delay(350).ContinueWith(_ =>
                            {
                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    if (!isFloatingBarFolded)
                                    {
                                        ViewboxFloatingBarMarginAnimation(-60);
                                    }
                                });
                            });
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理幻灯片放映结束事件失败: {ex}", LogHelper.LogType.Error);
            }
        }
        #endregion
    }
}
