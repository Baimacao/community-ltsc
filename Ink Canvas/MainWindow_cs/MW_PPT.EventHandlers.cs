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
        /// PowerPoint 在放映、但 ICC 因权限级别连不上时，给用户一条看得见的提示。
        ///
        /// 为什么必须提示：这个故障在界面上完全静默 —— 老师点了放映，
        /// 屏幕上是 PPT，ICC 却什么都不出现（翻页组件不出、页码不动、批注层不贴）。
        /// 2026-11-18 班测反馈「部分班级进入 PPT 放映后 ICC 无反应」，
        /// 现场就是这么来的：ICC 以管理员启动，PowerPoint 是普通权限。
        ///
        /// 提示里必须给出「怎么办」，否则用户知道出事了也没辙。
        /// </summary>
        private void OnPPTPermissionBlocked(string reason)
        {
            try
            {
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    try
                    {
                        MessageBox.Show(
                            "检测到 PowerPoint 正在放映，但本程序连不上它。\n\n" +
                            "原因是两者的权限级别不同：Windows 会阻断\n" +
                            "「管理员程序的 ICC」和「普通权限的 PowerPoint」之间的通信。\n\n" +
                            "解决办法（任选其一，做完重启 ICC）：\n" +
                            "  · 用普通权限重新打开 ICC（不要右键「以管理员身份运行」）\n" +
                            "  · 或者用管理员权限打开 PowerPoint\n\n" +
                            "（设置 → 启动 里可以设为「检测到管理员时自动降权重启」，\n" +
                            "  这样以后就不用每次手动处理了）",
                            "PPT 联动被系统权限阻断",
                            MessageBoxButton.OK,
                            MessageBoxImage.Warning);
                    }
                    catch (Exception ex)
                    {
                        LogHelper.WriteLogToFile($"显示 PPT 权限提示失败: {ex.Message}", LogHelper.LogType.Warning);
                    }
                });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"处理 PPT 权限阻塞事件失败: {ex}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 处理「总页数从无效恢复到有效」事件：重新评估翻页栏可见性。
        ///
        /// 背景（2026-11-19 班测）：翻页栏的显示闸门里有 hasValidPageCount = SlidesCount &gt; 0。
        /// 一次 COM 超时曾把 SlidesCount 写成 0，翻页栏被 HideAllNavigationPanels() 隐藏；
        /// 而重新显示只发生在「放映开始」分支里，COM 事件那时已经死了，
        /// 于是整节课翻页栏都回不来。
        ///
        /// 现在 RefreshSlidesCount 读失败不再写 0，但「从 0 恢复」这条路径仍然可能存在
        /// （例如换了个演示文稿），所以必须有人重新叫一次可见性评估。
        /// </summary>
        private void OnPPTSlidesCountRecovered(int slideCount)
        {
            try
            {
                LogHelper.WriteLogToFile(
                    $"PPT 总页数恢复有效（{slideCount} 页），重新评估翻页栏可见性",
                    LogHelper.LogType.Event);
                _pptUIManager?.UpdateNavigationPanelsVisibility();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"恢复翻页栏可见性失败: {ex}", LogHelper.LogType.Error);
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

        /// <summary>
        /// 拼出笔迹文件夹路径 —— 走 <see cref="PptInkPresentationId"/>，和读侧同一套。
        ///
        /// LTSC（2026-11-20）：这里原来自己算一份，格式和读侧不一样：
        ///   拿到 FullName 时 {名字}_{页数}_{哈希}（三段）
        ///   拿不到时退化成 {名字}_{页数}（两段）
        /// 而读侧永远三段。一旦走了退化分支，存进去的笔迹就读不回来。
        ///
        /// 现在统一成 {名字}_{路径哈希}，页数不进文件夹名（页数会变），
        /// 改由文件夹里的 SlideCount 文件记录，用于检测「页数变了没有」。
        /// </summary>
        /// <summary>
        /// 页数变了就问老师要不要沿用上次的笔迹。
        ///
        /// LTSC（2026-11-20）：只在两个条件同时成立时问：
        ///   1. 文件夹里记下的页数和当前页数不一样
        ///   2. 那个文件夹里真的有笔迹（没笔迹就没什么可丢的）
        /// 问了以后：
        ///   沿用 → 把记录的页数更新成当前值，下次不再问
        ///   重新开始 → 删掉那个文件夹里的笔迹文件
        ///   先不改 → 什么都不动，下次还会问
        /// </summary>
        private async Task PromptForSlideCountChangeIfNeeded(string strokePath, string presentationName, int currentSlideCount)
        {
            try
            {
                if (string.IsNullOrEmpty(strokePath) || currentSlideCount <= 0) return;

                int savedCount = PptInkPresentationId.ReadSlideCount(strokePath);
                if (savedCount <= 0) return;               // 没记录过，第一次见，不用问
                if (savedCount == currentSlideCount) return; // 页数没变，不用问

                // 没笔迹就没什么可丢的，直接更新记录
                if (!PptInkPresentationId.HasAnyStrokes(strokePath, ".icstk"))
                {
                    PptInkPresentationId.WriteSlideCount(strokePath, currentSlideCount);
                    return;
                }

                bool keepOld = await PptInkSlideCountPrompt.AskAsync(
                    this, presentationName, savedCount, currentSlideCount);

                if (keepOld)
                {
                    // 沿用：把记录更新成当前页数，下次打开不再问同一个问题
                    PptInkPresentationId.WriteSlideCount(strokePath, currentSlideCount);
                }
                else
                {
                    // 重新开始：删掉旧笔迹，再记下新页数
                    try
                    {
                        foreach (string file in Directory.GetFiles(strokePath, "*.icstk"))
                        {
                            try { File.Delete(file); } catch { }
                        }
                        PptInkPresentationId.WriteSlideCount(strokePath, currentSlideCount);
                        LogHelper.WriteLogToFile("[PPT笔迹] 已按用户选择清空上次笔迹", LogHelper.LogType.Event);
                    }
                    catch (Exception dex)
                    {
                        LogHelper.WriteLogToFile("[PPT笔迹] 清空旧笔迹失败: " + dex.Message, LogHelper.LogType.Warning);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile("[PPT笔迹] 页数变化处理失败: " + ex.Message, LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 进放映后补跳上次播放页 —— 带轮询重试。
        ///
        /// LTSC（2026-11-20）：为什么需要重试：
        ///   上游原版用 YesOrNoNotificationWindow(...).ShowDialog()，是模态阻塞窗口，
        ///   用户点按钮时 PPT 的打开-放映状态早已稳定，TryNavigateToSlide 一次就成。
        ///   现在换成内联非阻塞弹窗，点「是」的那一刻放映可能还没真正开始，
        ///   TryNavigateToSlide 内部的 TryGetView() 拿不到 SlideShowWindows 就失败。
        ///
        /// 所以这里每 250ms 试一次，最多 10 次（2.5 秒）。成功就停。
        /// </summary>
        private void TryJumpToPageWithRetry(int targetPage, int maxAttempts, int intervalMs)
        {
            if (targetPage <= 0 || maxAttempts <= 0) return;

            int attempt = 0;
            Action tryOnce = null;
            tryOnce = () =>
            {
                attempt++;
                try
                {
                    bool ok = _pptManager?.TryNavigateToSlide(targetPage) == true;
                    if (ok)
                    {
                        LogHelper.WriteLogToFile(
                            $"[PPT跳转] 进放映后补跳第 {targetPage} 页：成功（第 {attempt} 次尝试）",
                            LogHelper.LogType.Event);
                        return;
                    }
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"[PPT跳转] 补跳第 {attempt} 次异常: {ex.Message}",
                        LogHelper.LogType.Warning);
                }

                if (attempt < maxAttempts)
                {
                    System.Threading.Tasks.Task.Delay(intervalMs).ContinueWith(_ =>
                    {
                        Application.Current?.Dispatcher.BeginInvoke(tryOnce);
                    });
                }
                else
                {
                    LogHelper.WriteLogToFile(
                        $"[PPT跳转] 进放映后补跳第 {targetPage} 页：{maxAttempts} 次都没成功" +
                        $"（放映中={_pptManager?.IsInSlideShow} 已连接={_pptManager?.IsConnected}）",
                        LogHelper.LogType.Warning);
                }
            };

            Application.Current?.Dispatcher.BeginInvoke(tryOnce);
        }

        private string GetPresentationStrokeFolderPath(Presentation presentation, string presentationName, int totalSlides, string presentationFullName = null)
        {
            string fullName = presentationFullName;
            string name = presentationName;

            if (presentation != null)
            {
                // LTSC（2026-11-20）：漏了 pres 这一层兜底，导致**写盘和读取用了不同的文件夹**。
                //
                // 班测现象：退出放映时画了笔，日志显示「写盘 → test1.pptx_C236620F」，
                // 但再进放映读的是「test1.pptx_39041F03」，永远读不到。
                //
                // 根因：presentationFullName 参数来自 agentState?.PresentationFullName，
                // 而 agentState 恒为 null（D-02 之后 Agent 架构已移除），
                // 所以这个参数**永远是空**。空路径时 PptInkPresentationId
                // 退化成「用名字算哈希」，得到一个和路径哈希不同的值。
                //
                // 而 presentation 对象本身是好的，它的 FullName 一直都在。
                // 所以这里要在参数为空时补取。
                try
                {
                    if (string.IsNullOrEmpty(fullName)) fullName = presentation.FullName;
                    if (string.IsNullOrEmpty(name)) name = presentation.Name;
                }
                catch (Exception ex)
                {
                    LogHelper.WriteLogToFile($"读取演示文稿标识失败: {ex.Message}", LogHelper.LogType.Warning);
                }
            }

            string id = PptInkPresentationId.Create(name, fullName);

            // LTSC（2026-11-20）：这条日志是为了查「写盘和读取用了不同文件夹」。
            // 班测现象：退出放映时写进 test1.pptx_C236620F，
            // 再进放映却读 test1.pptx_39041F03 —— 哈希不一样，永远读不到。
            // 差别的来源就是这里的 fullName 有没有值：有值是路径哈希，没值是名字哈希。
            LogHelper.WriteLogToFile(
                $"[PPT笔迹] 拼路径：name={name ?? "(空)"} " +
                $"fullName={(string.IsNullOrEmpty(fullName) ? "(空→用名字算哈希)" : fullName)} id={id}",
                LogHelper.LogType.Event);

            return PptInkPresentationId.GetFolderPath(Settings.Automation.AutoSavedStrokesLocation, id);
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

                // LTSC（2026-11-23 班测）：进放映时必须把页码推给翻页栏。
                // 原来这里只设了 _previousSlideID，**从来没调 UpdateCurrentSlideNumber**，
                // 所以进放映停在哪一页，翻页栏就显示「?」——直到用户翻下一页才更新。
                // 这次打开 PPT 停在第 82 页，翻页栏一直是「?」直到翻到 83 才正常。
                LogHelper.WriteLogToFile(
                    $"[PPT页码] 进放映：currentSlide={currentSlide} totalSlides={totalSlides}" +
                    $"（推给翻页栏）",
                    LogHelper.LogType.Event);

                // 页码显示自检：万一还是显示「?」，5 秒后告诉老师怎么办。
                // 班测出现过「重启后才好」的案例，没有复现方法，先做兜底。
                _pageBrokenHintShown = false;
                _pageBrokenZeroTicks = 0;
                SchedulePageBrokenSelfCheck();
                if (currentSlide > 0 && totalSlides > 0)
                {
                    _pptUIManager?.UpdateCurrentSlideNumber(currentSlide, totalSlides);
                }

                // LTSC（2026-11-20）：补跳「上次播放页」。
                //
                // 提示框是在「打开演示文稿」时弹的，那时还没进放映，
                // TryNavigateToSlide 内部的 TryGetView() 拿不到 SlideShowWindows，
                // 必然失败 —— 班测表现就是「点了是但不跳」。
                //
                // 这里在真正进放映之后补一次。用 BeginInvoke 排到当前事件之后，
                // 让放映窗口先完成初始化。
                if (_pendingJumpToPage > 0)
                {
                    int targetPage = _pendingJumpToPage;
                    _pendingJumpToPage = 0;
                    Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                    {
                        // 轮询重试：放映窗口不是一瞬间就绪的。
                        // 上游用的是 ShowDialog()（模态阻塞），用户点按钮时状态早已稳定；
                        // 现在改成内联非阻塞弹窗后，点「是」的那一刻放映可能还没真正开始，
                        // 所以这里多试几次，成功就停。
                        TryJumpToPageWithRetry(targetPage, 10, 250);
                    }));
                }

                lock (_memoryStreams)
                {
                    foreach (var stream in _memoryStreams.Values)
                        stream?.Dispose();
                    _memoryStreams.Clear();
                }

                if (Settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint && !string.IsNullOrEmpty(presentationName))
                {
                    string strokePath = GetPresentationStrokeFolderPath(activePresentation, presentationName, totalSlides, presentationFullName);

                    // LTSC（2026-11-20）：页数变了就问一句再决定要不要沿用。
                    //
                    // 为什么要问：文件夹名只按「名字 + 路径哈希」认，页数已经不进名字了，
                    // 所以改过页数的 PPT 也能找到上次的笔迹。但页数一变，
                    // 第 8 页的墨迹可能已经不是原来那页了 ——
                    // 自动沿用会让老师上错页的墨迹，自动丢弃会让他白画一遍。
                    // 这个判断只有他知道。
                    //
                    // 只在「记下的页数」和「当前页数」不一致时问，
                    // 而且文件夹里得真的有笔迹，否则没什么可问的。
                    await PromptForSlideCountChangeIfNeeded(strokePath, presentationName, totalSlides);

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

                        // 诊断：进放映时到底停在第几页、内存里有哪些页的笔迹。
                        // 加这条是因为上一版「进放映时已恢复第 N 页」一条日志都没有，
                        // 分不清是「当前页没笔迹」还是「代码没跑到」。
                        try
                        {
                            var diagKeys = new List<int>();
                            lock (_memoryStreams)
                            {
                                foreach (var kv in _memoryStreams)
                                {
                                    if (kv.Value != null) diagKeys.Add(kv.Key);
                                }
                            }
                            diagKeys.Sort();
                            LogHelper.WriteLogToFile(
                                $"[PPT笔迹] 进放映：当前页={currentSlide} 总页数={totalSlides} " +
                                $"内存中有笔迹的页=[{(diagKeys.Count == 0 ? "无" : string.Join(",", diagKeys))}]",
                                LogHelper.LogType.Event);
                        }
                        catch { }

                        // LTSC（2026-11-20）：读完必须把**当前这一页**画上去。
                        //
                        // 原来只读进 _memoryStreams 就结束了，真正往画布上添加笔迹的
                        // 代码在 OnPPTSlideShowNextSlide 里（翻页时触发）。
                        // 所以：打开 PPT 直接进放映、不翻页的话，
                        // 笔迹一直躺在内存里，画布上空空如也 ——
                        // 用户看到的就是「识别到 PPT 了，但上次的笔迹没出来」。
                        Application.Current?.Dispatcher.InvokeAsync(() =>
                        {
                            try
                            {
                                byte[] firstPageBytes = null;
                                lock (_memoryStreams)
                                {
                                    if (currentSlide > 0
                                        && _memoryStreams.TryGetValue(currentSlide, out var firstStream)
                                        && firstStream != null)
                                    {
                                        firstPageBytes = firstStream.ToArray();
                                    }
                                }

                                if (firstPageBytes != null && firstPageBytes.Length > 8)
                                {
                                    var strokes = new StrokeCollection(new MemoryStream(firstPageBytes));
                                    if (strokes.Count > 0)
                                    {
                                        inkCanvas.Strokes.Add(strokes);
                                        LogHelper.WriteLogToFile(
                                            $"[PPT笔迹] 进放映时已恢复第 {currentSlide} 页的 {strokes.Count} 条笔迹",
                                            LogHelper.LogType.Event);
                                    }
                                }
                            }
                            catch (Exception drawEx)
                            {
                                LogHelper.WriteLogToFile("[PPT笔迹] 恢复当前页笔迹失败: " + drawEx.Message,
                                    LogHelper.LogType.Warning);
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

                // LTSC（2026-11-23 班测）：翻页组件显示「?」—— 说明 CurrentSlide 或 TotalSlides 有一个是 0。
                // 这里把真实值打出来，不用再猜是哪一边。
                LogHelper.WriteLogToFile(
                    $"[PPT页码] 切页处理：currentSlide={currentSlide} totalSlides={totalSlides} " +
                    $"previousSlideID={_previousSlideID} " +
                    $"桥页数={_pptManager?.SlidesCount} 桥当前页={_pptManager?.GetCurrentSlideNumber()}",
                    LogHelper.LogType.Event);

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
                // LTSC（2026-11-19 班测）：把「这次切页有没有带恢复标记」也记下来。
                // 用户报「笔状态通过预览快翻翻页后进入写不了字的批注模式」，
                // 关键就在这条链上：如果标记没置上，TryRestoreAnnotationAfterPptNavigation
                // 第一句就 return，界面停在一个半切状态，而且以前完全没有痕迹。
                // LTSC（2026-11-20 第十七轮）：这里原来会自动调
                // TryRestoreAnnotationAfterPptNavigation 把模式切回批注。
                // 按用户新定的规则，翻页后**保持鼠标穿透**，不自动回批注。
                //
                // 那条自动恢复链是前面十六轮所有麻烦的来源：
                // 它要在切页后 500ms 异步把窗口激活/命中/选中框/EditingMode
                // 一起调对，任何一个没调对就出现「显示批注但实际穿透」的半切状态。
                // 现在直接不做了 —— 老师要写字就自己点批注，走那条成熟路径。
                //
                // 笔迹显示还需要恢复：点页码时藏了笔迹（为了看清缩略图），
                // 翻完页应该让它重新显示出来，否则老师以为墨迹丢了。
                LogHelper.WriteLogToFile(
                    $"[PPT导航] 检测到切页 currentSlide={currentSlide} "
                    + $"当前批注={IsAnnotating} 工具模式={_currentToolMode}"
                    + "（保持穿透，不自动回批注）",
                    LogHelper.LogType.Event);

                // 恢复笔迹显示（保持穿透，不切模式）。
                // 用 BeginInvoke 排到墨迹切页之后再执行，避免和上面的保存/恢复打架。
                Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
                {
                    try
                    {
                        if (inkCanvas != null && inkCanvas.Visibility != Visibility.Visible)
                        {
                            inkCanvas.Visibility = Visibility.Visible;
                            LogHelper.WriteLogToFile(
                                "[PPT导航] 翻页后恢复笔迹显示（仍保持鼠标穿透）",
                                LogHelper.LogType.Event);
                        }
                    }
                    catch (Exception bex)
                    {
                        LogHelper.WriteLogToFile("[PPT导航] 恢复笔迹显示失败: " + bex.Message,
                            LogHelper.LogType.Warning);
                    }
                }), System.Windows.Threading.DispatcherPriority.Background);


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
            // LTSC（2026-11-20）：payload 现在可能是两种东西：
            //   · string —— 演示文稿的完整路径（PptBridge 轮询路径传的，现在走这条）
            //   · Presentation —— 兼容老调用方
            // 原来写死成 Presentation，而 PptBridge 一直传 null，
            // 于是 pres 永远 null、FullName 永远空，保存时用名字算哈希，
            // 文件夹名和读侧对不上，笔迹存进去就再也读不出来。
            var pres = payload as Presentation;
            string payloadFullName = payload as string;
            var agentState = (PPTState)null; // LTSC D-02: Agent 架构已移除
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
                        catch (Exception ex) { Ink_Canvas.Helpers.LogHelper.WriteLogToFile("未处理的异常：" + ex.Message, Ink_Canvas.Helpers.LogHelper.LogType.Warning); }
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
                // 优先用 payload 传进来的路径（PptBridge 从活的 COM 对象取到的），
                // 拿不到再从 pres 对象取。两个都空就会退化成名字哈希 —— 那正是之前的 bug。
                string presentationFullNameForSave = payloadFullName;
                if (string.IsNullOrEmpty(presentationFullNameForSave) && pres != null)
                {
                    try { presentationFullNameForSave = pres.FullName; } catch { }
                }
                if (string.IsNullOrEmpty(presentationFullNameForSave))
                {
                    LogHelper.WriteLogToFile(
                        "[PPT笔迹] 警告：退出放映时拿不到演示文稿路径，将退化成名字哈希，" +
                        "可能和读取时用的文件夹不一致",
                        LogHelper.LogType.Warning);
                }
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

                    // LTSC（2026-11-20）：我上一版在这里又加了一段「收下当前页笔迹」，
                    // 是重复劳动 —— 上面第 943 行的 Dispatcher.InvokeAsync 已经做过了。
                    // 而且那段跑在非 UI 线程，读 inkCanvas.Strokes 会抛
                    // 「调用线程无法访问此对象」，日志里能看到，纯噪声。已删。
                    LogHelper.WriteLogToFile(
                        $"[PPT笔迹] 准备写盘：当前页={currentPage} 总页数={totalSlidesForSave} " +
                        $"文件夹={folderPathForSave} 内存页数={_memoryStreams.Count}",
                        LogHelper.LogType.Event);

                    await Task.Run(() =>
                    {
                        try
                        {
                            if (!Directory.Exists(folderPathForSave))
                                Directory.CreateDirectory(folderPathForSave);

                            int diagWritten = 0, diagSkipped = 0;
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
                                            {
                                                File.WriteAllBytes(filePath, allBytes);
                                                diagWritten++;
                                            }
                                            else if (File.Exists(filePath))
                                            {
                                                File.Delete(filePath);
                                                diagSkipped++;
                                            }
                                            else
                                            {
                                                diagSkipped++;
                                            }
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
