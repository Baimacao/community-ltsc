using Ink_Canvas.Controls;
using Ink_Canvas.Controls.Toolbar.FloatingToolbar;
using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.WorkflowAutomation;
using iNKORE.UI.WPF.Modern;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Forms;
using System.Windows.Ink;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using Application = System.Windows.Application;
using Button = System.Windows.Controls.Button;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using OpenFileDialog = Microsoft.Win32.OpenFileDialog;
using Panel = System.Windows.Controls.Panel;
using Point = System.Windows.Point;

namespace Ink_Canvas
{
    public partial class MainWindow : Ink_Canvas.Helpers.PerformanceTransparentWin
    {
        // LTSC（2026-11-17）：从 MW_FloatingBarIcons.cs 按 #region 拆出。
        // 只搬代码，不改逻辑。using 区整块复制（原文有 7 条别名）。
        #region 浮動工具欄的拖動實現

        /// <summary>
        /// 是否正在拖动浮动工具栏
        /// </summary>
        private bool isDragDropInEffect;
        /// <summary>
        /// 当前位置
        /// </summary>
        private Point pos;
        /// <summary>
        /// 按下鼠标时的位置
        /// </summary>
        private Point downPos;
        /// <summary>
        /// 用于记录上次在桌面时的坐标
        /// </summary>
        internal Point pointDesktop = new Point(-1, -1);

        /// <summary>
        /// 浮动栏定位动画的世代号。
        ///
        /// 为什么需要：展开/折叠会连续发起多次定位动画（启动流程一次就发了 6 次），
        /// 每次的弹簧 finished 回调各自落地自己的 targetMargin，**执行顺序不保证**。
        /// 实测：展开动画在 17.287 发起，折叠在 17.432 发起，
        /// 结果折叠先落地（17.591），展开的旧回调晚 1ms 才到（17.592），
        /// 把位置又覆盖回展开态——表现出来就是「折叠完浮窗还留在屏幕上」，
        /// 启动收纳校验于是又折一次，用户看到的是折了两次。
        ///
        /// 现在每次发起动画都 ++，回调里比对；不是最新一代就丢弃，不落地。
        /// </summary>
        private int _floatingBarPosVersion = 0;
        /// <summary>
        /// 用于记录上次在PPT中的坐标
        /// </summary>
        internal Point pointPPT = new Point(-1, -1);
        internal bool _userHasDraggedFloatingBar;
        private DispatcherTimer _floatingBarScreenFollowTimer;
        private string _lastFloatingBarScreenDeviceName;
        private string _lastCanvasScreenDeviceName;
        private bool _isRebuildingCanvasForScreen;

        /// <summary>
        /// Popup 管理器（负责置顶和拖动跟随）
        /// </summary>
        private double _cachedFloatingBarWidth;
        private double _cachedFloatingBarHeadWidth;
        private double _cachedScreenWidth;
        private DateTime _lastFloatingBarSizeCacheTime;

        private void RefreshFloatingBarSizeCache(bool force = false)
        {
            var now = DateTime.Now;
            if (!force && (now - _lastFloatingBarSizeCacheTime).TotalMilliseconds < 100)
                return;

            var scale = GetFloatingBarScaleX();
            _cachedFloatingBarWidth = GetElementWidthForFloatingBar(ViewboxFloatingBar, 200) * scale;
            var dragElement = FindDragHandleInRoot();
            _cachedFloatingBarHeadWidth = GetElementWidthForFloatingBar(dragElement, 50) * scale;
            _cachedScreenWidth = GetFloatingBarScreenWidth(Settings.Advanced.IsEnableAvoidFullScreenHelper);
            _cachedFloatingBarHeight = GetElementHeightForFloatingBar(ViewboxFloatingBar, 58) * scale;
            _cachedFloatingBarHeadHeight = GetElementHeightForFloatingBar(dragElement, 50) * scale;
            _cachedScreenHeight = GetFloatingBarScreenHeight(Settings.Advanced.IsEnableAvoidFullScreenHelper);
            _lastFloatingBarSizeCacheTime = now;
        }

        private PopupManagerHelper _popupManager;

        /// <summary>
        /// 获取 PopupManagerHelper 实例，供插件等外部组件使用
        /// </summary>
        public PopupManagerHelper GetPopupManager() => _popupManager;

        /// <summary>
        /// LTSC L-16：释放 Popup 管理器。窗口关闭时调用。
        /// PopupManagerHelper 订阅了 CompositionTarget.Rendering（每帧），
        /// 不 Dispose 就永不退订。
        /// </summary>
        internal void ReleasePopupManager()
        {
            if (_popupManager == null) return;
            try
            {
                if (_popupManager is IDisposable d) d.Dispose();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"释放 PopupManager 异常: {ex.Message}", LogHelper.LogType.Warning);
            }
            finally
            {
                _popupManager = null;
            }
        }

        /// <summary>
        /// 关闭所有已注册的 Popup 弹窗
        /// </summary>
        public void CloseAllPopups()
        {
            HideSubPanelsImmediately();
        }

        /// <summary>
        /// 浮动工具栏移动事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标事件参数</param>
        private void SymbolIconEmoji_MouseMove(object sender, MouseEventArgs e)
        {
            if (isDragDropInEffect)
            {
                var currentPos = e.GetPosition(null);
                var xPos = currentPos.X - pos.X + ViewboxFloatingBar.Margin.Left;
                var yPos = currentPos.Y - pos.Y + ViewboxFloatingBar.Margin.Top;
                ViewboxFloatingBar.Margin = new Thickness(xPos, yPos, -2000, -200);

                pos = currentPos;

                RefreshFloatingBarSizeCache();

                if (IsVerticalToolbar)
                {
                    if (Settings.Appearance.AutoFlipWhenSpaceInsufficient)
                    {
                        var headTop = ViewboxFloatingBar.Margin.Top + (isFloatingBarHeadOnBottom ? Math.Max(0, _cachedFloatingBarHeight - _cachedFloatingBarHeadHeight) : 0);

                        const double flipHysteresis = 20.0;
                        bool shouldFlip;
                        var vPosition = Settings.Appearance.ToolbarPosition;
                        if (vPosition == ToolbarPosition.Bottom)
                        {
                            if (!isFloatingBarHeadOnBottom && headTop + _cachedFloatingBarHeight > _cachedScreenHeight)
                                shouldFlip = true;
                            else if (isFloatingBarHeadOnBottom && headTop + _cachedFloatingBarHeight <= _cachedScreenHeight - flipHysteresis)
                                shouldFlip = false;
                            else
                                shouldFlip = isFloatingBarHeadOnBottom;
                        }
                        else
                        {
                            var toolsTopWhenUnflipped = headTop - Math.Max(0, _cachedFloatingBarHeight - _cachedFloatingBarHeadHeight);
                            if (isFloatingBarHeadOnBottom && toolsTopWhenUnflipped < 0)
                                shouldFlip = false;
                            else if (!isFloatingBarHeadOnBottom && toolsTopWhenUnflipped >= flipHysteresis)
                                shouldFlip = true;
                            else
                                shouldFlip = isFloatingBarHeadOnBottom;
                        }

                        if (shouldFlip != isFloatingBarHeadOnBottom)
                        {
                            var savedHeadTop = headTop;
                            SetFloatingBarHeadPlacementVertical(shouldFlip);

                            RefreshFloatingBarSizeCache(true);

                            double newTop;
                            if (shouldFlip)
                                newTop = savedHeadTop - Math.Max(0, _cachedFloatingBarHeight - _cachedFloatingBarHeadHeight);
                            else
                                newTop = savedHeadTop;

                            newTop = ClampFloatingBarTop(newTop, _cachedFloatingBarHeight, _cachedScreenHeight);
                            ViewboxFloatingBar.Margin = new Thickness(ViewboxFloatingBar.Margin.Left, newTop, -2000, -200);
                        }
                    }
                }
                else
                {
                    if (Settings.Appearance.AutoFlipWhenSpaceInsufficient)
                    {
                        var headLeft = ViewboxFloatingBar.Margin.Left + (isFloatingBarHeadOnRight ? Math.Max(0, _cachedFloatingBarWidth - _cachedFloatingBarHeadWidth) : 0);

                        const double flipHysteresis = 20.0;
                        bool shouldFlip;
                        var hPosition = Settings.Appearance.ToolbarPosition;
                        if (hPosition == ToolbarPosition.Right)
                        {
                            if (!isFloatingBarHeadOnRight && headLeft + _cachedFloatingBarWidth > _cachedScreenWidth)
                                shouldFlip = true;
                            else if (isFloatingBarHeadOnRight && headLeft + _cachedFloatingBarWidth <= _cachedScreenWidth - flipHysteresis)
                                shouldFlip = false;
                            else
                                shouldFlip = isFloatingBarHeadOnRight;
                        }
                        else
                        {
                            var toolsLeftWhenUnflipped = headLeft - Math.Max(0, _cachedFloatingBarWidth - _cachedFloatingBarHeadWidth);
                            if (isFloatingBarHeadOnRight && toolsLeftWhenUnflipped < 0)
                                shouldFlip = false;
                            else if (!isFloatingBarHeadOnRight && toolsLeftWhenUnflipped >= flipHysteresis)
                                shouldFlip = true;
                            else
                                shouldFlip = isFloatingBarHeadOnRight;
                        }

                        if (shouldFlip != isFloatingBarHeadOnRight)
                        {
                            var savedHeadLeft = headLeft;
                            SetFloatingBarHeadPlacement(shouldFlip);

                            RefreshFloatingBarSizeCache(true);

                            double newLeft;
                            if (shouldFlip)
                                newLeft = savedHeadLeft - Math.Max(0, _cachedFloatingBarWidth - _cachedFloatingBarHeadWidth);
                            else
                                newLeft = savedHeadLeft;

                            newLeft = ClampFloatingBarLeft(newLeft, _cachedFloatingBarWidth, _cachedScreenWidth);
                            ViewboxFloatingBar.Margin = new Thickness(newLeft, ViewboxFloatingBar.Margin.Top, -2000, -200);
                        }
                    }
                }

                var currentMargin = ViewboxFloatingBar.Margin;
                if (IsInPPTPresentationMode)
                    pointPPT = new Point(currentMargin.Left, currentMargin.Top);
                else
                    pointDesktop = new Point(currentMargin.Left, currentMargin.Top);
                _userHasDraggedFloatingBar = true;

                _popupManager?.MarkNeedsUpdate();

                if (BorderTools.IsOpen) _popupManager?.BringToFront(BorderTools);
                if (BoardBorderToolsPopup.IsOpen) _popupManager?.BringToFront(BoardBorderToolsPopup);
                if (BorderDrawShape.IsOpen) _popupManager?.BringToFront(BorderDrawShape);
                if (BoardBorderDrawShape.IsOpen) _popupManager?.BringToFront(BoardBorderDrawShape);
            }
        }

        /// <summary>
        /// 初始化 Popup 管理器（创建实例、注册 Popup、启动跟随系统）
        /// 在 Window_Loaded 中调用一次
        /// </summary>
        internal void InitializePopupManager()
        {
            try
            {
                // LTSC L-16：PopupManagerHelper 在 Initialize 里订阅了
                // CompositionTarget.Rendering（每帧回调），全项目从未 Dispose。
                // 重新初始化时先释放旧的，否则 Rendering 订阅一路累加。
                // 窗口关闭时的释放见 ReleasePopupManager。
                ReleasePopupManager();

                _popupManager = new PopupManagerHelper();

                _popupManager.ShouldBeTopmost = () => Settings.Advanced.IsAlwaysOnTop;

                _popupManager.RegisterPopup(BorderTools);
                _popupManager.RegisterPopup(BoardBorderToolsPopup);
                _popupManager.RegisterPopup(BorderDrawShape);
                _popupManager.RegisterPopup(BoardBorderDrawShape);
                _popupManager.RegisterPopup(PenPalette);
                _popupManager.RegisterPopup(BoardPenPalette);
                _popupManager.RegisterPopup(EraserSizePanel);
                _popupManager.RegisterPopup(BoardEraserSizePanel);
                _popupManager.RegisterPopup(BoardImageOptionsPanel);
                _popupManager.RegisterPopup(TwoFingerGestureBorder);
                _popupManager.RegisterPopup(BoardTwoFingerGestureBorder);
                _popupManager.RegisterPopup(BackgroundPalette);

                _popupManager.Initialize(this);

                System.Diagnostics.Debug.WriteLine("[PopupManager] Initialized successfully");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PopupManager] Initialize error: {ex.Message}");
            }
        }

        private void SymbolIconEmoji_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (isViewboxFloatingBarMarginAnimationRunning)
            {
                ViewboxFloatingBar.BeginAnimation(MarginProperty, null);
                // LTSC D-05：位移动画现在跑在 RenderTransform 上，中断时也要一并归零，
                // 否则残留的 TranslateTransform 会和拖动叠加，出现偏移。
                if (ViewboxFloatingBar.RenderTransform is TranslateTransform dragTranslate)
                {
                    dragTranslate.BeginAnimation(TranslateTransform.XProperty, null);
                    dragTranslate.BeginAnimation(TranslateTransform.YProperty, null);
                    dragTranslate.X = 0;
                    dragTranslate.Y = 0;
                }
                isViewboxFloatingBarMarginAnimationRunning = false;
            }

            isDragDropInEffect = true;
            pos = e.GetPosition(null);
            downPos = e.GetPosition(null);
            GridForFloatingBarDraging.Visibility = Visibility.Visible;
        }

        /// <summary>
        /// 浮动工具栏鼠标释放事件处理
        /// </summary>
        /// <param name="sender">发送者</param>
        /// <param name="e">鼠标按钮事件参数</param>
        internal void SymbolIconEmoji_MouseUp(object sender, MouseButtonEventArgs e)
        {
            isDragDropInEffect = false;

            var isClick = e is null || (Math.Abs(downPos.X - e.GetPosition(null).X) <= 10 &&
                                        Math.Abs(downPos.Y - e.GetPosition(null).Y) <= 10);

            if (isClick)
            {
                var headPos = IsVerticalToolbar ? GetCurrentFloatingBarHeadTop() : GetCurrentFloatingBarHeadLeft();
                var horizontal = !IsVerticalToolbar;
                var root = FloatingBarRootPanel;

                LogHelper.WriteLogToFile(
                    $"[折叠] 主图标点击，当前内容可见={IsFloatingBarContentVisible()}",
                    LogHelper.LogType.Event);

                if (IsFloatingBarContentVisible())
                {
                    // 折叠前先把选中高亮收掉。
                    // 那个蓝框（SelectionBGFloatingBar）是跟着工具项定位的，
                    // 内容一收它就成了悬空的一个框，看着像"折完还留了个选中框"。
                    // HideAllSelectionHighlights 原本只在隐藏/切模式时调，折叠没管过。
                    HideAllSelectionHighlights();

                    // 折叠：先让内容淡出收缩，动画结束再真正切 Visibility。
                    // 直接切 Collapsed 是瞬时的，视觉上"啪"一下就没了。
                    FloatingBarFoldAnimation.PlayCollapse(root, BorderFloatingBarMoveControls, horizontal, () =>
                    {
                        SetFloatingBarContentVisibility(false);
                        UpdateToolbarComponentVisibility();
                        PlaceFloatingBarAfterHeadToggle(headPos, false);
                        UpdateFloatingBarModeBadge(true);
                        _popupManager?.MarkNeedsUpdate();

                        // 折叠完成后强制确认高亮已灭。
                        // 日志里能直接读到状态，不用再靠截图猜。
                        HideAllSelectionHighlights();
                        LogHelper.WriteLogToFile(
                            $"[折叠] 完成，selectionBG={SelectionBGFloatingBar?.Visibility} indicatorBar={IndicatorBarFloatingBar?.Visibility} 内容可见={IsFloatingBarContentVisible()}",
                            LogHelper.LogType.Event);
                    });
                }
                else
                {
                    // 展开：先把 Visibility 切回来（否则动画看不到东西），再展开。
                    SetFloatingBarContentVisibility(true);
                    UpdateToolbarComponentVisibility();
                    FloatingBarFoldAnimation.PlayExpand(root, BorderFloatingBarMoveControls, horizontal, () =>
                    {
                        PlaceFloatingBarAfterHeadToggle(headPos, true);
                        _popupManager?.MarkNeedsUpdate();
                        // 展开完成后把高亮按当前工具模式重新定位回去
                        SetFloatingBarHighlightPosition(_currentToolMode);
                    });
                }
            }
            else
            {
                var headPos = IsVerticalToolbar ? GetCurrentFloatingBarHeadTop() : GetCurrentFloatingBarHeadLeft();
                PlaceFloatingBarAfterHeadToggle(
                    headPos,
                    IsFloatingBarContentVisible());
                _popupManager?.MarkNeedsUpdate();
            }

            // 每次点击或拖动结束后都重新定位高光。
            // 折叠/展开动画期间不画（SetFloatingBarHighlightPosition 内部已经挡了），
            // 展开完成时由动画回调自己补一次。
            SetFloatingBarHighlightPosition(_currentToolMode);

            GridForFloatingBarDraging.Visibility = Visibility.Collapsed;
        }

        #endregion 浮動工具欄的拖動實現
    }
}
