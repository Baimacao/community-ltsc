using Ink_Canvas.Helpers;
using Ink_Canvas.Properties;
using Ink_Canvas.Windows.SettingsViews.Pages;
using iNKORE.UI.WPF.Modern.Controls;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Navigation;
using MessageBox = iNKORE.UI.WPF.Modern.Controls.MessageBox;
using Screen = System.Windows.Forms.Screen;

namespace Ink_Canvas.Windows.SettingsViews
{
    public partial class SettingsWindow : Window
    {
        private readonly Dictionary<string, Type> _pageTypes = new Dictionary<string, Type>
        {
            { "HomePage", typeof(HomePage) },
            { "StartupPage", typeof(StartupPage) },
            { "ClockPage", typeof(ClockPage) },
            { "SecurityPage", typeof(SecurityPage) },
            { "WindowPage", typeof(WindowPage) },
            { "SidebarPage", typeof(SidebarPage) },
            { "HotkeyPage", typeof(HotkeyPage) },
            { "ToolbarPage", typeof(ToolbarPage) },
            { "ToolbarAppearancePage", typeof(ToolbarAppearancePage) },
            { "ToolbarMenuPage", typeof(ToolbarMenuPage) },
            { "BoardToolbarPage", typeof(BoardToolbarPage) },
            { "BoardAppearancePage", typeof(BoardAppearancePage) },
            { "BoardMenuPage", typeof(BoardMenuPage) },
            { "WhiteboardTipsPage", typeof(WhiteboardTipsPage) },
            { "AdvancedPage", typeof(AdvancedPage) },
            { "StoragePage", typeof(StoragePage) },
            { "AutomationWorkflowPage", typeof(AutomationWorkflowPage) },
            { "PowerPointPage", typeof(PowerPointPage) },
            { "TimerPage", typeof(TimerPage) },
            { "CanvasPage", typeof(CanvasPage) },
            { "FriendlyLinksPage", typeof(FriendlyLinksPage) },
            { "AboutPage", typeof(AboutPage) },
            { "Settings", typeof(SettingsPage) },
            { "PPTPageFlipPreviewPage", typeof(PPTPageFlipPreviewPage) },
            { "PresenterKeyPage", typeof(PresenterKeyPage) },
        };
        private readonly Dictionary<string, object> _pages = new Dictionary<string, object>();

        // 保存窗口原始位置和大小
        private double _originalLeft;
        private double _originalTop;
        private double _originalWidth;
        private double _originalHeight;

        // 标记窗口是否曾经最大化过
        private bool _wasMaximized = false;

        private bool _isNavigating = false;

        public SettingsWindow()
        {
            InitializeComponent();

            ApplyCurrentTheme();
            global::Ink_Canvas.Helpers.WindowBackdropHelper.Apply(this, Helpers.SettingsManager.Settings);

            // 内置页面映射见字段初始化器 _pageTypes（第 19 行起）。
            // 这里原本重复了一份完全相同的 36 条字典，两份各自演化过，
            // 是 C-03 误判「页面不可达」的直接原因：改一处、另一处不变，
            // 审计时搜到哪份就信哪份。已删除，只保留字段初始化器作为唯一来源。

            // 默认选中首页
            if (NavigationViewControl.MenuItems.Count > 0)
            {
                NavigateToPage("HomePage");
                NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[0];
                NavigationViewControl.Header = NavStrings.Nav_Home;
            }

            UpdateAppTitleBarMargin();

            this.Loaded += (sender, e) =>
            {
                SetMaxSizeAndCenter();
                RegisterDpiChangedListener();

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    NavigateToPage("HomePage");
                    NavigationViewControl.SelectedItem = NavigationViewControl.MenuItems[0];
                    NavigationViewControl.Header = NavStrings.Nav_Home;

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        UpdateUpdateBadgeVisibility();
                        // 绑定设置窗口中的 ToggleSwitch 本地化文本
                        Ink_Canvas.Helpers.LocalizationHelper.BindToggleSwitchesInWindow(this);
                    }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                }), System.Windows.Threading.DispatcherPriority.Normal);

                _ = PreloadAllPagesAsync();
            };

            this.Closed += (sender, e) =>
            {
                UnregisterDpiChangedListener();
                _pages.Clear();
                _pageTypes.Clear();
            };

            this.TouchUp += (s, e) => ShowCursor(true);
            this.MouseEnter += (s, e) => ShowCursor(true);
            this.Activated += (s, e) => ShowCursor(true);

            this.StateChanged += (sender, e) =>
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    _originalLeft = this.Left;
                    _originalTop = this.Top;
                    _originalWidth = this.Width;
                    _originalHeight = this.Height;
                    _wasMaximized = true;
                    this.MaxWidth = double.PositiveInfinity;
                    this.MaxHeight = double.PositiveInfinity;
                }
                else if (this.WindowState == WindowState.Normal && _wasMaximized)
                {
                    this.Left = _originalLeft;
                    this.Top = _originalTop;
                    this.Width = _originalWidth;
                    this.Height = _originalHeight;
                    _wasMaximized = false;
                    SetMaxSizeOnly();
                }
                else if (this.WindowState == WindowState.Normal)
                {
                    SetMaxSizeOnly();
                }
                UpdateAppTitleBarMargin();
            };

            this.SizeChanged += (sender, e) =>
            {
                if (NavigationViewControl.DisplayMode == NavigationViewDisplayMode.Minimal)
                {
                    UpdateAppTitleBarMargin();
                }
            };
        }

        public void RefreshTheme()
        {
            ApplyCurrentTheme();
            global::Ink_Canvas.Helpers.WindowBackdropHelper.Apply(this, Helpers.SettingsManager.Settings);
        }



        public void ApplyWindowBackdrop(string backdropName)
        {
            global::Ink_Canvas.Helpers.WindowBackdropHelper.Apply(this, backdropName);
        }

        private void ApplyCurrentTheme()
        {
            try
            {
                int themeIndex = Helpers.SettingsManager.Settings.Appearance.Theme;
                var elementTheme = themeIndex switch
                {
                    0 => iNKORE.UI.WPF.Modern.ElementTheme.Light,
                    1 => iNKORE.UI.WPF.Modern.ElementTheme.Dark,
                    _ => IsSystemThemeLight() ? iNKORE.UI.WPF.Modern.ElementTheme.Light : iNKORE.UI.WPF.Modern.ElementTheme.Dark,
                };
                iNKORE.UI.WPF.Modern.ThemeManager.SetRequestedTheme(this, elementTheme);
            }
            catch { }
        }

        private static bool IsSystemThemeLight()
        {
            try
            {
                using (var themeKey = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    if (themeKey?.GetValue("AppsUseLightTheme") is int v) return v == 1;
                }
            }
            catch { }
            return false;
        }

        #region 修复触摸屏鼠标指针消失问题

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern int ShowCursor(bool bShow);
        #endregion

        #region 高DPI/多屏自适应窗口控制

        /// <summary>
        /// 获取当前窗口所在屏幕的工作区尺寸（DIP单位）
        /// </summary>
        private void GetWorkAreaSize(out double workAreaWidthDip, out double workAreaHeightDip, out double screenLeftDip, out double screenTopDip)
        {
            // 1. 获取窗口当前所在屏幕
            var windowHandle = new WindowInteropHelper(this).Handle;
            var currentScreen = Screen.FromHandle(windowHandle);
            var workingArea = currentScreen.WorkingArea;
            var screenBounds = currentScreen.Bounds;

            // 2. 获取当前窗口的DPI缩放因子
            var source = PresentationSource.FromVisual(this);
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            // 3. 物理像素 → WPF设备无关像素(DIP)转换
            workAreaWidthDip = workingArea.Width / dpiScaleX;
            workAreaHeightDip = workingArea.Height / dpiScaleY;
            screenLeftDip = screenBounds.Left / dpiScaleX;
            screenTopDip = screenBounds.Top / dpiScaleY;
        }

        private void SetMaxSizeAndCenter()
        {
            if (!this.IsLoaded) return;

            GetWorkAreaSize(out double workAreaWidthDip, out double workAreaHeightDip, out double screenLeftDip, out double screenTopDip);

            // 设置窗口最大尺寸
            this.MaxWidth = workAreaWidthDip;
            this.MaxHeight = workAreaHeightDip;

            // 窗口在当前屏幕居中（解决副屏居中跑偏问题）
            this.Left = screenLeftDip + (workAreaWidthDip - this.ActualWidth) / 2;
            this.Top = screenTopDip + (workAreaHeightDip - this.ActualHeight) / 2;
        }

        private void SetMaxSizeOnly()
        {
            if (!this.IsLoaded) return;

            GetWorkAreaSize(out double workAreaWidthDip, out double workAreaHeightDip, out _, out _);

            // 只设置窗口最大尺寸，不改变窗口位置
            this.MaxWidth = workAreaWidthDip;
            this.MaxHeight = workAreaHeightDip;
        }

        #region DPI/系统缩放变化监听
        private HwndSource _hwndSource;
        private void RegisterDpiChangedListener()
        {
            _hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            _hwndSource?.AddHook(DpiChangedWndProc);
        }

        private void UnregisterDpiChangedListener()
        {
            _hwndSource?.RemoveHook(DpiChangedWndProc);
            _hwndSource = null;
        }

        private IntPtr DpiChangedWndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            const int WM_DPICHANGED = 0x02E0;
            // 系统DPI/缩放变化时自动重新计算窗口参数
            if (msg == WM_DPICHANGED)
            {
                SetMaxSizeAndCenter();
                handled = true;
            }
            return IntPtr.Zero;
        }
        #endregion
        #endregion

        #region 导航逻辑优化（含页面缓存）
        private void OnNavigationViewSelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_isNavigating)
            {
                return;
            }

            if (args.IsSettingsSelected)
            {
                NavigateToPage("Settings");
                NavigationViewControl.Header = NavStrings.Settings_Title;
                return;
            }

            // 处理普通导航项
            if (args.SelectedItem is NavigationViewItem selectedItem)
            {
                string tag = selectedItem.Tag as string;
                if (!string.IsNullOrEmpty(tag) && _pageTypes.ContainsKey(tag))
                {
                    object cachedPage = null;
                    _pages.TryGetValue(tag, out cachedPage);

                    if (cachedPage == null || rootFrame.Content != cachedPage)
                    {
                        NavigateToPage(tag);
                    }
                    NavigationViewControl.Header = selectedItem.Content;

                }
            }
        }

        public void NavigateToPage(string pageTag)
        {
            if (!_pageTypes.TryGetValue(pageTag, out Type pageType))
            {
                // 找不到页面键时不能静默吞掉：用户点了一张卡什么都没发生，
                // 排查时只能翻日志。这里保留日志（含已注册的全部键，便于对照），
                // 同时把错误显示出来，让"点了没反应"变成"点了报错"。
                LogHelper.WriteLogToFile($"SettingsWindow: NavigateToPage 找不到页面类型 [{pageTag}]，已注册: [{string.Join(", ", _pageTypes.Keys)}]", LogHelper.LogType.Error);
                MessageBox.Show(string.Format(NavStrings.Nav_NavigateError, $"未注册的页面键: {pageTag}"), NavStrings.Nav_Error, MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                _isNavigating = true;

                if (!_pages.TryGetValue(pageTag, out var cachedPage))
                {
                    cachedPage = Activator.CreateInstance(pageType);
                    _pages.Add(pageTag, cachedPage);
                }


                rootFrame.NavigationUIVisibility = NavigationUIVisibility.Hidden;
                rootFrame.RemoveBackEntry();
                rootFrame.Navigate(cachedPage);
                rootFrame.RemoveBackEntry();
            }
            catch (Exception ex)
            {
                var detail = ex.ToString();
                if (ex.InnerException != null)
                {
                    detail += "\nInnerException:\n" + ex.InnerException;
                }

                Ink_Canvas.Helpers.LogHelper.WriteLogToFile($"SettingsWindow: 导航到 {pageTag} 异常: {detail}", Ink_Canvas.Helpers.LogHelper.LogType.Error);
                MessageBox.Show(string.Format(NavStrings.Nav_NavigateError, ex.InnerException?.Message ?? ex.Message), NavStrings.Nav_Error, MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                _isNavigating = false;
            }
        }


        private void OnNavigationViewBackRequested(NavigationView sender, NavigationViewBackRequestedEventArgs args)
        {
            if (rootFrame.CanGoBack) rootFrame.GoBack();
        }
        private void OnRootFrameNavigated(object sender, NavigationEventArgs e)
        {
            Type currentPageType = rootFrame.SourcePageType;
            if (currentPageType == typeof(PPTPageFlipPreviewPage))
            {
                NavigationViewControl.PaneDisplayMode = iNKORE.UI.WPF.Modern.Controls.NavigationViewPaneDisplayMode.LeftMinimal;
            }
            else
            {
                NavigationViewControl.PaneDisplayMode = iNKORE.UI.WPF.Modern.Controls.NavigationViewPaneDisplayMode.Auto;
            }

            if (_isNavigating)
            {
                return;
            }

            // 处理设置项的选中状态
            if (currentPageType == typeof(SettingsPage))
            {
                NavigationViewControl.SelectedItem = NavigationViewControl.SettingsItem;
                NavigationViewControl.Header = NavStrings.Settings_Title;
                return;
            }

            // 同步其他页面的选中状态
            foreach (var kvp in _pageTypes)
            {
                if (kvp.Value == currentPageType)
                {
                    var targetItem = FindNavigationViewItemByTag(kvp.Key);
                    if (targetItem != null && NavigationViewControl.SelectedItem != targetItem)
                    {
                        NavigationViewControl.SelectedItem = targetItem;
                        NavigationViewControl.Header = targetItem.Content;
                    }
                    break;
                }
            }

            ApplySmoothScrollingToPage(e.Content as FrameworkElement);
        }

        private void ApplySmoothScrollingToPage(FrameworkElement root)
        {
            if (root == null) return;

            var queue = new Queue<DependencyObject>();
            if (root is DependencyObject rootDep) queue.Enqueue(rootDep);

            while (queue.Count > 0)
            {
                var current = queue.Dequeue();

                if (current is ScrollViewer sv)
                {
                    sv.PanningMode = PanningMode.VerticalOnly;
                    sv.PanningDeceleration = 0.001;
                    sv.PanningRatio = 1;
                    sv.ManipulationBoundaryFeedback += (s, e) => e.Handled = true;
                }

                var children = LogicalTreeHelper.GetChildren(current);
                foreach (var child in children)
                {
                    if (child is DependencyObject childDep)
                        queue.Enqueue(childDep);
                }
            }
        }

        private void NavigationViewControl_DisplayModeChanged(NavigationView sender, NavigationViewDisplayModeChangedEventArgs args)
        {
            UpdateAppTitleBarMargin(sender);
        }

        private void UpdateAppTitleBarMargin()
        {
            UpdateAppTitleBarMargin(NavigationViewControl);
        }

        private void UpdateAppTitleBarMargin(NavigationView sender)
        {
            Thickness currMargin = AppTitleBar.Margin;
            if (sender.DisplayMode == NavigationViewDisplayMode.Minimal)
            {
                AppTitleBar.Margin = new Thickness((sender.CompactPaneLength * 2), currMargin.Top, currMargin.Right, currMargin.Bottom);

                // 当窗口宽度非常小时，隐藏图标和应用设置文字
                if (this.ActualWidth < 400)
                {
                    AppTitle.Visibility = Visibility.Collapsed;
                }
                else
                {
                    AppTitle.Visibility = Visibility.Visible;
                }
            }
            else
            {
                AppTitleBar.Margin = new Thickness(sender.CompactPaneLength, currMargin.Top, currMargin.Right, currMargin.Bottom);
                AppTitle.Visibility = Visibility.Visible;
            }
            AppTitleBar.Visibility = sender.PaneDisplayMode == NavigationViewPaneDisplayMode.Top ? Visibility.Collapsed : Visibility.Visible;
        }

        private NavigationViewItem FindNavigationViewItemByTag(string tag)
        {
            // 遍历主菜单
            foreach (var item in NavigationViewControl.MenuItems)
            {
                if (item is NavigationViewItem navItem)
                {
                    if (navItem.Tag as string == tag)
                        return navItem;

                    // 遍历子菜单，自动展开父项
                    foreach (var childItem in navItem.MenuItems)
                    {
                        if (childItem is NavigationViewItem childNavItem && childNavItem.Tag as string == tag)
                        {
                            navItem.IsExpanded = true;
                            return childNavItem;
                        }
                    }
                }
            }

            // 遍历底部菜单
            foreach (var item in NavigationViewControl.FooterMenuItems)
            {
                if (item is NavigationViewItem navItem && navItem.Tag as string == tag)
                {
                    return navItem;
                }
            }

            return null;
        }
        #endregion

        #region 搜索框逻辑优化

        private sealed class SearchEntry
        {
            public string Text;
            public string PageTag;
            public WeakReference<FrameworkElement> Target;
        }

        private List<SearchEntry> _searchIndex;
        private bool _indexBuilt;

        private void EnsureSearchIndexBuilt()
        {
            if (_indexBuilt && _searchIndex != null) return;
            _searchIndex = new List<SearchEntry>(256);

            foreach (var item in GetAllNavigationItems())
            {
                var text = item.Content?.ToString();
                var tag = item.Tag as string;
                if (!string.IsNullOrWhiteSpace(text) && !string.IsNullOrEmpty(tag))
                {
                    _searchIndex.Add(new SearchEntry { Text = text.Trim(), PageTag = tag });
                }
            }

            foreach (var kv in _pageTypes.ToList())
            {
                var tag = kv.Key;
                if (tag == "Settings") continue;

                try
                {
                    if (!_pages.TryGetValue(tag, out var page))
                    {
                        page = Activator.CreateInstance(kv.Value);
                        _pages[tag] = page;
                    }
                    if (page is FrameworkElement feRoot)
                    {
                        if (!feRoot.IsLoaded)
                        {
                            try { feRoot.ApplyTemplate(); } catch { }
                        }
                        CollectEntriesFromPage(feRoot, tag);
                    }
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(string.Format(NavStrings.Nav_IndexBuildFailed, tag, ex.Message));
                }
            }

            _indexBuilt = true;
        }

        private void CollectEntriesFromPage(DependencyObject root, string pageTag)
        {
            foreach (var node in EnumerateLogicalDescendants(root))
            {
                string header = null;
                FrameworkElement target = node as FrameworkElement;

                if (node is Ink_Canvas.Controls.LabeledSettingsCard lsc)
                {
                    header = lsc.Header;
                }
                else if (node is iNKORE.UI.WPF.Modern.Controls.SettingsCard sc)
                {
                    header = sc.Header?.ToString();
                }
                else if (node is iNKORE.UI.WPF.Modern.Controls.SettingsExpander se)
                {
                    header = se.Header?.ToString();
                }

                if (!string.IsNullOrWhiteSpace(header) && target != null)
                {
                    _searchIndex.Add(new SearchEntry
                    {
                        Text = header.Trim(),
                        PageTag = pageTag,
                        Target = new WeakReference<FrameworkElement>(target)
                    });
                }
            }
        }

        private static IEnumerable<DependencyObject> EnumerateLogicalDescendants(DependencyObject root)
        {
            if (root == null) yield break;
            var stack = new Stack<DependencyObject>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                var node = stack.Pop();
                yield return node;
                foreach (var child in LogicalTreeHelper.GetChildren(node))
                {
                    if (child is DependencyObject d) stack.Push(d);
                }
            }
        }

        private void NavigateToSearchEntry(SearchEntry entry)
        {
            if (entry == null) return;

            NavigateToPage(entry.PageTag);
            var navItem = FindNavigationViewItemByTag(entry.PageTag);
            if (navItem != null && NavigationViewControl.SelectedItem != navItem)
            {
                NavigationViewControl.SelectedItem = navItem;
                NavigationViewControl.Header = navItem.Content;
            }

            if (entry.Target != null && entry.Target.TryGetTarget(out var fe))
            {
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    try { fe.BringIntoView(); } catch { }
                }), System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void OnControlsSearchBoxQuerySubmitted(AutoSuggestBox sender, AutoSuggestBoxQuerySubmittedEventArgs args)
        {
            EnsureSearchIndexBuilt();

            string raw = (args.ChosenSuggestion as string) ?? args.QueryText;
            if (string.IsNullOrWhiteSpace(raw)) return;

            string query = raw.Trim();

            var entry = _searchIndex.FirstOrDefault(e => e.Text.Equals(query, StringComparison.OrdinalIgnoreCase))
                        ?? _searchIndex.FirstOrDefault(e => e.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0);

            NavigateToSearchEntry(entry);
        }

        private void OnControlsSearchBoxTextChanged(AutoSuggestBox sender, AutoSuggestBoxTextChangedEventArgs args)
        {
            if (args.Reason != AutoSuggestionBoxTextChangeReason.UserInput) return;

            EnsureSearchIndexBuilt();

            string query = sender.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(query))
            {
                sender.ItemsSource = null;
                return;
            }

            var suggestions = _searchIndex
                .Where(e => e.Text.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(e => e.Text)
                .Distinct()
                .Take(50)
                .ToList();

            sender.ItemsSource = suggestions;
        }

        // 统一获取所有导航项（主菜单+子菜单+底部菜单）
        private List<NavigationViewItem> GetAllNavigationItems()
        {
            var items = new List<NavigationViewItem>();

            // 主菜单+子菜单
            foreach (var item in NavigationViewControl.MenuItems)
            {
                if (item is NavigationViewItem navItem)
                {
                    items.Add(navItem);
                    foreach (var child in navItem.MenuItems)
                    {
                        if (child is NavigationViewItem childNavItem)
                            items.Add(childNavItem);
                    }
                }
            }

            // 底部菜单
            foreach (var item in NavigationViewControl.FooterMenuItems)
            {
                if (item is NavigationViewItem navItem)
                    items.Add(navItem);
            }

            return items;
        }

        #endregion

        public NavigationView GetNavigationView()
        {
            return NavigationViewControl;
        }

        private async System.Threading.Tasks.Task PreloadAllPagesAsync()
        {
            await System.Threading.Tasks.Task.Delay(1000);

            try
            {
                var tags = _pageTypes.Keys.ToList();
                int count = 0;
                foreach (var tag in tags)
                {
                    if (_pages.ContainsKey(tag))
                        continue;
                    if (!_pageTypes.TryGetValue(tag, out var type))
                        continue;

                    try
                    {
                        if (!_pages.ContainsKey(tag))
                        {
                            var page = Activator.CreateInstance(type);
                            _pages[tag] = page;
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(string.Format(NavStrings.Nav_PreloadPageFailed, tag, ex.Message));
                    }

                    // 每加载一页后让出 UI 线程，防止阻塞心跳定时器
                    if (++count % 3 == 0)
                    {
                        await System.Threading.Tasks.Task.Delay(50);
                    }
                    else
                    {
                        await System.Windows.Threading.Dispatcher.Yield(System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(string.Format(NavStrings.Nav_PreloadPagesFailed, ex.Message));
            }
        }

        /// <summary>
        /// LTSC D-01: 自动更新已移除，更新角标没有存在的意义。保留空方法是因为
        /// 调用点散布在窗口初始化与导航里，删调用比留空实现风险大。
        /// </summary>
        public void UpdateUpdateBadgeVisibility()
        {
        }

    }
}