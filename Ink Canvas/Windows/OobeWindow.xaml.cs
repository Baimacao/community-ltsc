using Ink_Canvas.Helpers;
using System.Windows.Media.Imaging;
using iNKORE.UI.WPF.Modern;
using System.Windows.Threading;
using System.Diagnostics;
using Ink_Canvas.Properties;
using iNKORE.UI.WPF.Modern.Common.IconKeys;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using FontIcon = iNKORE.UI.WPF.Modern.Controls.FontIcon;
using NavigationView = iNKORE.UI.WPF.Modern.Controls.NavigationView;
using NavigationViewItem = iNKORE.UI.WPF.Modern.Controls.NavigationViewItem;
using NavigationViewSelectionChangedEventArgs = iNKORE.UI.WPF.Modern.Controls.NavigationViewSelectionChangedEventArgs;
using Screen = System.Windows.Forms.Screen;

namespace Ink_Canvas.Windows
{
    /// <summary>
    /// 首次启动体验(OOBE)窗口。使用 iNKORE.UI.WPF.Modern 的 NavigationView 作为左侧导航,
    /// 引导用户依次完成欢迎页、8 个配置步骤与完成摘要页。
    /// </summary>
    public partial class OobeWindow : Window
    {
        private readonly Settings _settings;

        // 视图状态: -1 = 欢迎; 0..7 = 步骤; 8 = 完成
        private const int WelcomeIndex = -1;
        private const int FinishIndex = 4;
        private const int StepCount = 4;
        private const int MaxStepIndex = StepCount - 1;

        private int _currentStep = WelcomeIndex;
        private bool _suppressNavSelection;
        private bool _suppressLanguageChange;

        private FrameworkElement[] _stepPanels;
        private NavigationViewItem[] _navItems;

        public OobeWindow(Settings settings)
        {
            if (settings == null) throw new ArgumentNullException(nameof(settings));

            _settings = settings;
            InitializeComponent();
            WindowBackdropHelper.Apply(this, _settings);

            Opacity = 0;

            _stepPanels = new FrameworkElement[]
            {
                StepCanvasPanel,
                StepGesturesPanel,
                StepAppearancePanel,
                StepPresenterPanel,
            };

            _navItems = new[]
            {
                NavItemCanvas,
                NavItemGestures,
                NavItemAppearance,
                NavItemPresenter,
            };

            InitializeFromSettings();
            UpdateView(animateDirection: 0, instant: true);
            SyncNavSelection();
        }

        #region Settings IO

        private void InitializeFromSettings()
        {
            try
            {
                if (_settings.Startup != null)
                {
                    int crashAction = _settings.Startup.CrashAction;
                    if (crashAction < 0 || crashAction > 2) crashAction = 0;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Canvas != null)
                {
                    CardShowCursor.IsOn = _settings.Canvas.IsShowCursor;
                    CardDisablePressure.IsOn = _settings.Canvas.DisablePressure;
                    CardHideStrokeWhenSelecting.IsOn = _settings.Canvas.HideStrokeWhenSelecting;
                    CardEnablePalmEraser.IsOn = _settings.Canvas.EnablePalmEraser;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Gesture != null)
                {
                    CardTwoFingerZoom.IsOn = _settings.Gesture.IsEnableTwoFingerZoom;
                    CardTwoFingerTranslate.IsOn = _settings.Gesture.IsEnableTwoFingerTranslate;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.InkToShape != null)
                {
                    CardInkToShapeEnabled.IsOn = _settings.InkToShape.IsInkToShapeEnabled;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Appearance != null)
                {
                    int themeIndex = _settings.Appearance.Theme;
                    if (themeIndex < 0 || themeIndex > 2) themeIndex = 2;
                    ComboBoxTheme.SelectedIndex = themeIndex;
                    SelectComboBoxItemByTag(ComboBoxWindowBackdrop, _settings.Appearance.WindowBackdrop);
                    CardEnableTrayIcon.IsOn = _settings.Appearance.EnableTrayIcon;
                    CardShowQuickPanel.IsOn = _settings.Appearance.IsShowQuickPanel;
                    CardEnableHotkeysInMouseMode.IsOn = _settings.Appearance.EnableHotkeysInMouseMode;

                    // 语言：回填当前设置。初次启动时是空字符串 = 跟随系统。
                    _suppressLanguageChange = true;
                    SelectComboBoxItemByTag(ComboBoxOobeLanguage, _settings.Appearance.Language ?? "");
                    _suppressLanguageChange = false;

                    // 翻页笔适配：回填当前开关状态。
                    ToggleSwitchPresenterAdapter.IsOn = PresenterKeyAdapter.IsEnabled;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.PowerPointSettings != null)
                {
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Automation != null)
                {
                    if (_settings.Automation.FloatingWindowInterceptor != null)
                    {
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.RandSettings != null)
                {
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Advanced != null)
                {
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        private void ApplySelection()
        {
            try
            {
                // LTSC：OOBE 不再涉及遥测/崩溃处理，这些留在「设置 → 启动」里改。
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Canvas != null)
                {
                    _settings.Canvas.IsShowCursor = CardShowCursor.IsOn;
                    _settings.Canvas.DisablePressure = CardDisablePressure.IsOn;
                    _settings.Canvas.HideStrokeWhenSelecting = CardHideStrokeWhenSelecting.IsOn;
                    _settings.Canvas.EnablePalmEraser = CardEnablePalmEraser.IsOn;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Gesture != null)
                {
                    _settings.Gesture.IsEnableTwoFingerZoom = CardTwoFingerZoom.IsOn;
                    _settings.Gesture.IsEnableTwoFingerTranslate = CardTwoFingerTranslate.IsOn;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.InkToShape != null)
                {
                    _settings.InkToShape.IsInkToShapeEnabled = CardInkToShapeEnabled.IsOn;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Appearance != null)
                {
                    int themeIndex = ComboBoxTheme.SelectedIndex;
                    if (themeIndex < 0) themeIndex = 2;
                    _settings.Appearance.Theme = themeIndex;
                    _settings.Appearance.WindowBackdrop = GetSelectedComboBoxTag(ComboBoxWindowBackdrop, "None");
                    _settings.Appearance.EnableTrayIcon = CardEnableTrayIcon.IsOn;
                    _settings.Appearance.IsShowQuickPanel = CardShowQuickPanel.IsOn;

                    // 语言：空字符串 = 跟随系统
                    var langTag = GetSelectedComboBoxTag(ComboBoxOobeLanguage, "");
                    _settings.Appearance.Language = langTag;

                    // 翻页笔适配：开关状态写入运行时适配器。
                    // 不进 Settings —— 它是会话级状态（ta 裁决 Q4-4：关掉重开会重新提示），
                    // 但 OOBE 里老师明确选了「开」，那就该在本次会话立刻生效。
                    if (ToggleSwitchPresenterAdapter.IsOn)
                        PresenterKeyAdapter.EnableAdapter();
                    _settings.Appearance.EnableHotkeysInMouseMode = CardEnableHotkeysInMouseMode.IsOn;
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.PowerPointSettings != null)
                {
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Automation != null)
                {
                    if (_settings.Automation.FloatingWindowInterceptor != null)
                    {
                    }
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.RandSettings != null)
                {
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }

            try
            {
                if (_settings.Advanced != null)
                {
                }
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex); }
        }

        private static void SelectComboBoxItemByTag(ComboBox comboBox, string tag)
        {
            if (comboBox == null) return;

            var selectedItem = comboBox.Items
                .OfType<ComboBoxItem>()
                .FirstOrDefault(item => string.Equals(item.Tag?.ToString(), tag, StringComparison.OrdinalIgnoreCase))
                ?? comboBox.Items.OfType<ComboBoxItem>().FirstOrDefault();

            comboBox.SelectedItem = selectedItem;
        }

        private static string GetSelectedComboBoxTag(ComboBox comboBox, string fallback)
        {
            return (comboBox?.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? fallback;
        }

        private void ComboBoxWindowBackdrop_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ComboBoxWindowBackdrop == null) return;
            WindowBackdropHelper.Apply(this, GetSelectedComboBoxTag(ComboBoxWindowBackdrop, "None"));
        }

        #endregion

        #region Navigation

        private void BtnStartWelcome_Click(object sender, RoutedEventArgs e)
        {
            NavigateTo(0, direction: 1);
        }

        private void BtnUsePreset_Click(object sender, RoutedEventArgs e)
        {
            var presetWindow = new OobePresetWindow { Owner = this };
            bool? result = presetWindow.ShowDialog();
            if (result != true) return;

            switch (presetWindow.SelectedPreset)
            {
                case OobePresetWindow.PresetKind.Standard:
                    OobePresetWindow.ApplyStandard(_settings);
                    break;
                case OobePresetWindow.PresetKind.Teacher:
                    OobePresetWindow.ApplyTeacher(_settings);
                    break;
                case OobePresetWindow.PresetKind.Lite:
                    OobePresetWindow.ApplyLite(_settings);
                    break;
                default:
                    return;
            }

            DialogResult = true;
            Close();
        }

        /// <summary>
        /// 欢迎页的语言切换。
        ///
        /// 行为：立即改运行时的 UI 语言并记住选择，但**不重启文本**。
        /// 已经在屏幕上的控件不会立刻变成新语言（WPF 的静态资源是编译期绑定的），
        /// 所以这里只保存设置，让下次启动生效 —— 和设置页的口径一致。
        ///
        /// 老师的实际体验：第一屏选中文/English，之后每一步的标题会按新语言显示，
        /// 这是因为在切步骤时会重新读一次文案；已经在屏幕上的那部分要重启才变。
        /// </summary>
        private void ComboBoxOobeLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_suppressLanguageChange) return;

            try
            {
                var tag = GetSelectedComboBoxTag(ComboBoxOobeLanguage, "");
                _settings.Appearance.Language = tag;

                // 立即应用：后续创建的控件会用新语言
                Ink_Canvas.Windows.SettingsViews.Helpers.SettingsActionHub.OnLanguageChanged(tag);

                // 重新读一遍当前步骤的标题/副标题，让这一步立刻能看到变化
                ApplyStepMeta(_currentStep);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"OOBE 切换语言失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void BtnConfirm_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep == FinishIndex)
            {
                ApplySelection();
                DialogResult = true;
                Close();
                return;
            }

            NavigateTo(_currentStep + 1, direction: 1);
        }

        private void BtnPreviousStep_Click(object sender, RoutedEventArgs e)
        {
            if (_currentStep <= WelcomeIndex) return;
            NavigateTo(_currentStep - 1, direction: -1);
        }

        /// <summary>
        /// 跳过引导：直接使用当前设置开始使用。
        ///
        /// 为什么可以安全地调 ApplySelection：
        /// InitializeFromSettings() 在构造函数里就调过，
        /// 每个控件已经被填成「当前设置」的值。
        /// 所以 ApplySelection() 写回去的是「用户改过的那些 + 其余原值」，
        /// 不会把设置清成默认值。
        ///
        /// 但有一个例外必须处理：隐私协议（CheckBoxPrivacyAccepted）。
        /// 那是法律层面的同意，用户没勾就是没同意，跳过不能替他勾。
        /// 所以跳过时不动它。
        /// </summary>
        /// <summary>
        /// 反馈入口：智教联盟。
        ///
        /// 网址还没拿到，先留一个明确的占位分支 —— 点下去会记日志、不跳转。
        /// 拿到 ta 给的地址后，只要把 ZhijiaoUrl 换掉即可，不用再动别处。
        /// 不填假地址：编一个链接比暂时不能点更糟。
        /// </summary>
        private const string ZhijiaoUrl = "https://forum.smart-teach.cn";
        private const string GitHubUrl = "https://github.com/Baimacao/community-ltsc";

        // 用 Click 而不是鼠标事件：老师用触摸屏，PreviewMouseLeftButtonUp 只认鼠标。
        // 曾经怀疑过「开局自己打开 GitHub」，查清了是调试脚本用鼠标点在了按钮上，
        // 不是程序问题，所以这里不加任何拦阻。
        private void LinkZhijiao_Click(object sender, RoutedEventArgs e)
            => OpenExternal(ZhijiaoUrl, "智教联盟");

        private void LinkGitHub_Click(object sender, RoutedEventArgs e)
            => OpenExternal(GitHubUrl, "GitHub");

        private static void OpenExternal(string url, string name)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    LogHelper.WriteLogToFile($"{name} 链接尚未配置", LogHelper.LogType.Warning);
                    return;
                }

                // 记一笔：这个函数只应在用户主动点击时被调用。
                // 如果日志里在没有点击的情况下出现这条，说明链接被误触发了，
                // 拿这份记录去比对触发时机，比凭现象猜快得多。
                LogHelper.WriteLogToFile($"打开外部链接：{name} -> {url}", LogHelper.LogType.Event);
                Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"打开{name}链接失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void BtnSkipOobe_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 跳过 = 用当前设置直接开始，不应用任何默认值。
                ApplySelection();
                LogHelper.WriteLogToFile("用户跳过 OOBE 引导", LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"跳过 OOBE 时出错: {ex.Message}", LogHelper.LogType.Warning);
            }

            DialogResult = true;
            Close();
        }

        private void NavView_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
        {
            if (_suppressNavSelection) return;

            int target = ResolveTargetFromNavItem(args.SelectedItem as NavigationViewItem);
            if (target == _currentStep) return;

            int direction = target > _currentStep ? 1 : -1;
            NavigateTo(target, direction);
        }

        private int ResolveTargetFromNavItem(NavigationViewItem item)
        {
            if (item == null) return _currentStep;
            if (item == NavItemWelcome) return WelcomeIndex;
            if (item == NavItemFinish) return FinishIndex;
            for (int i = 0; i < _navItems.Length; i++)
            {
                if (_navItems[i] == item) return i;
            }
            return _currentStep;
        }

        private void NavigateTo(int target, int direction)
        {
            if (target < WelcomeIndex) target = WelcomeIndex;
            if (target > FinishIndex) target = FinishIndex;
            _currentStep = target;
            UpdateView(direction);
            SyncNavSelection();
        }

        private void SyncNavSelection()
        {
            _suppressNavSelection = true;
            try
            {
                if (_currentStep == WelcomeIndex)
                    NavView.SelectedItem = NavItemWelcome;
                else if (_currentStep == FinishIndex)
                    NavView.SelectedItem = NavItemFinish;
                else
                    NavView.SelectedItem = _navItems[_currentStep];
            }
            finally
            {
                _suppressNavSelection = false;
            }
        }

        private void CheckBoxPrivacyAccepted_Changed(object sender, RoutedEventArgs e)
        {
            UpdateConfirmEnabled();
        }

        private bool _privacyDialogShown;

        private void HyperlinkPrivacy_Click(object sender, RoutedEventArgs e)
        {
            e.Handled = true;
            if (_privacyDialogShown) return;
            _privacyDialogShown = true;
            try
            {
                var dialog = new PrivacyAgreementWindow { Owner = this };
                bool? result = dialog.ShowDialog();
                if (result == true && dialog.UserAccepted)
                {
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
            finally
            {
                Dispatcher.BeginInvoke(new Action(() => _privacyDialogShown = false),
                    System.Windows.Threading.DispatcherPriority.Background);
            }
        }

        private void UpdateConfirmEnabled()
        {
            if (BtnConfirm == null) return;
            BtnConfirm.IsEnabled = true;
        }

        #endregion

        #region 高DPI/多屏自适应窗口控制

        private HwndSource _hwndSource;

        private void GetWorkAreaSize(out double workAreaWidthDip, out double workAreaHeightDip, out double screenLeftDip, out double screenTopDip)
        {
            var windowHandle = new WindowInteropHelper(this).Handle;
            var currentScreen = Screen.FromHandle(windowHandle);
            var workingArea = currentScreen.WorkingArea;
            var screenBounds = currentScreen.Bounds;

            var source = PresentationSource.FromVisual(this);
            double dpiScaleX = 1.0;
            double dpiScaleY = 1.0;

            if (source?.CompositionTarget != null)
            {
                dpiScaleX = source.CompositionTarget.TransformToDevice.M11;
                dpiScaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            workAreaWidthDip = workingArea.Width / dpiScaleX;
            workAreaHeightDip = workingArea.Height / dpiScaleY;
            screenLeftDip = screenBounds.Left / dpiScaleX;
            screenTopDip = screenBounds.Top / dpiScaleY;
        }

        private void SetMaxSizeAndCenter()
        {
            if (!this.IsLoaded) return;

            GetWorkAreaSize(out double workAreaWidthDip, out double workAreaHeightDip, out double screenLeftDip, out double screenTopDip);

            this.MaxWidth = workAreaWidthDip;
            this.MaxHeight = workAreaHeightDip;

            this.Left = screenLeftDip + (workAreaWidthDip - this.ActualWidth) / 2;
            this.Top = screenTopDip + (workAreaHeightDip - this.ActualHeight) / 2;
        }

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
            if (msg == WM_DPICHANGED)
            {
                SetMaxSizeAndCenter();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // ────────────────────────────────────────────────────────────────
        // 首屏入场：错开时间的组合动画
        //
        // 单一淡入看不出层级，这里按阅读顺序给三拍：
        //
        //   拍1  logo  0.6 → 1.0 弹性放大（Bouncy，damping 0.45 轻微过冲）
        //   拍2  标题  从下方 24px 弹起（Bouncy）
        //   拍3  卡片  三张从右侧 28px 依次滑入，间隔 70ms
        //
        // 全程约 700ms。过冲只给 logo 和标题——它们是无正文的大色块，过一点自然；
        // 卡片里有说明文字，过冲会晃眼，所以用 Smooth 不过冲。
        // ────────────────────────────────────────────────────────────────
        private void PlayWelcomeIntro()
        {
            try
            {
                LogHelper.WriteLogToFile("OOBE 入场动画开始（logo→标题→3 张卡片）", LogHelper.LogType.Event);
                var bouncy = SpringAnimator.Bouncy;
                var smooth = SpringAnimator.Smooth;

                // 拍1：logo 弹出
                WelcomeLogo.Opacity = 0;
                SpringAnimator.SyncScalar(WelcomeLogoScale, 0.6);
                SpringAnimator.AnimateScalar(WelcomeLogoScale, 1.0,
                    v => { WelcomeLogoScale.ScaleX = v; WelcomeLogoScale.ScaleY = v; },
                    bouncy, null);
                SpringAnimator.AnimateScalar(WelcomeLogo, 1.0,
                    v => WelcomeLogo.Opacity = v, smooth, null);

                // 拍2：标题从下方弹起
                WelcomeTitle.Opacity = 0;
                if (WelcomeTitle.RenderTransform is TranslateTransform titleT)
                {
                    titleT.Y = 24;
                    SpringAnimator.SyncToTransform(titleT);
                    SpringAnimator.Animate(titleT, 0f, 0f, bouncy, null);
                }
                SpringAnimator.AnimateScalar(WelcomeTitle, 1.0,
                    v => WelcomeTitle.Opacity = v, smooth, null);

                // 拍3：卡片依次滑入
                var cards = new[] { WelcomeCard1, WelcomeCard2, WelcomeCard3 };
                var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(220) };
                timer.Tick += (_, _) =>
                {
                    timer.Stop();
                    for (int i = 0; i < cards.Length; i++)
                    {
                        var card = cards[i];
                        if (card?.RenderTransform is not TranslateTransform t) continue;

                        card.Opacity = 0;
                        t.X = 28;
                        SpringAnimator.SyncToTransform(t);

                        var cardTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(i * 70) };
                        cardTimer.Tick += (_, _) =>
                        {
                            cardTimer.Stop();
                            SpringAnimator.Animate(t, 0f, 0f, smooth, null);
                            SpringAnimator.AnimateScalar(card, 1.0,
                                v => card.Opacity = v, smooth, null);
                        };
                        cardTimer.Start();
                    }
                };
                timer.Start();
            }
            catch (Exception ex)
            {
                // 动画失败不能让首屏黑着：全部复位到可见。
                LogHelper.WriteLogToFile($"OOBE 入场动画出错: {ex.Message}", LogHelper.LogType.Warning);
                try
                {
                    WelcomeLogo.Opacity = 1;
                    WelcomeLogoScale.ScaleX = WelcomeLogoScale.ScaleY = 1;
                    WelcomeTitle.Opacity = 1;
                    if (WelcomeTitle.RenderTransform is TranslateTransform tt) tt.Y = 0;
                    foreach (var c in new[] { WelcomeCard1, WelcomeCard2, WelcomeCard3 })
                    {
                        if (c == null) continue;
                        c.Opacity = 1;
                        if (c.RenderTransform is TranslateTransform ct) ct.X = 0;
                    }
                }
                catch { }
            }
        }

        #endregion

        private void OobeWindow_OnClosing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            UnregisterDpiChangedListener();
        }

        private void OobeWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            // 记下窗口真正显示的时刻，链接的时间闸门要用。
            try
            {
                // 窗口淡入也走弹簧：和翻步、进度条同一套手感。
                // Smooth 比 Snappy 慢一点、尾巴更软，适合"整扇窗出现"这种大动作。
                Opacity = 0;
                SpringAnimator.SyncScalar(this, 0);
                SpringAnimator.AnimateScalar(this, 1.0, v => Opacity = v,
                    SpringAnimator.Smooth, null);
            }
            catch
            {
                Opacity = 1;
            }

            SetMaxSizeAndCenter();
            UpdateThemedIcons();
            PlayWelcomeIntro();
            RegisterDpiChangedListener();
        }


        // ────────────────────────────────────────────────────────────────
        // 按当前主题切换 LTSC 图标（深浅两套）
        //
        // 为什么不用 XAML 绑定：图标是 png 资源，深浅是两张不同的图，
        // ThemeManager 的字典替换对 Image.Source 不生效，得手动换。
        //
        // 窗口 Icon（任务栏 / Alt-Tab）保持浅色不切 —— 那是系统级图标，
        // 跟着主题变会让任务栏里同一程序忽深忽浅，反而怪。
        // ────────────────────────────────────────────────────────────────
        private void UpdateThemedIcons()
        {
            try
            {
                bool isLight;
                try
                {
                    var theme = ThemeHelper.GetEffectiveTheme(_settings ?? new Settings());
                    isLight = theme != ElementTheme.Dark;
                }
                catch
                {
                    // 拿不到主题就按浅色处理：浅色图标在深色底上也还能看，
                    // 反过来（深色图标放白底）几乎看不见。
                    isLight = true;
                }

                var name = isLight ? "light" : "dark";
                if (TitleBarIcon != null)
                {
                    TitleBarIcon.Source = new BitmapImage(
                        new Uri($"pack://application:,,,/Resources/Icons-png/iccltsc-{name}-32.png"));
                }
                if (WelcomeLogoImage != null)
                {
                    WelcomeLogoImage.Source = new BitmapImage(
                        new Uri($"pack://application:,,,/Resources/Icons-png/iccltsc-{name}-64.png"));
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"OOBE 图标切换失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }

        private void UpdateView(int animateDirection, bool instant = false)
        {
            try
            {
                bool isWelcome = _currentStep == WelcomeIndex;
                bool isFinish = _currentStep == FinishIndex;
                bool isStep = !isWelcome && !isFinish;

                WelcomePanel.Visibility = isWelcome ? Visibility.Visible : Visibility.Collapsed;
                StepScrollViewer.Visibility = isStep ? Visibility.Visible : Visibility.Collapsed;
                FinishPanel.Visibility = isFinish ? Visibility.Visible : Visibility.Collapsed;

                if (isStep)
                {
                    for (int i = 0; i < _stepPanels.Length; i++)
                    {
                        _stepPanels[i].Visibility = i == _currentStep ? Visibility.Visible : Visibility.Collapsed;
                    }

                    StepIndicatorText.Text = string.Format(Properties.OobeStrings.Oobe_StepFormat, _currentStep + 1, StepCount);
                    ApplyStepMeta(_currentStep);

                    if (StepScrollViewer != null) StepScrollViewer.ScrollToTop();
                }

                if (isFinish)
                {
                    BuildFinishSummary();
                }

                // 底部进度: 欢迎=0, 各步骤按比例, 完成=100
                double progress;
                if (isWelcome) progress = 0;
                else if (isFinish) progress = 100;
                else progress = (_currentStep + 1) / (double)(StepCount + 1) * 100.0;

                AnimateProgress(progress, instant);

                // Footer 步骤计数
                if (isWelcome) FooterStepText.Text = string.Empty;
                else if (isFinish) FooterStepText.Text = string.Format(Properties.OobeStrings.Oobe_StepFinishFormat, StepCount, StepCount);
                else FooterStepText.Text = $"{_currentStep + 1} / {StepCount}";

                // 上一步按钮: 欢迎页隐藏
                BtnPreviousStep.Visibility = isWelcome ? Visibility.Collapsed : Visibility.Visible;

                // 主按钮: 欢迎页隐藏(由欢迎页自身的"开始"按钮负责)
                BtnConfirm.Visibility = isWelcome ? Visibility.Collapsed : Visibility.Visible;
                if (isFinish)
                {
                    BtnConfirmText.Text = Properties.OobeStrings.Oobe_SaveAndStart;
                    BtnConfirmIcon.Icon = SegoeFluentIcons.Accept;
                }
                else
                {
                    BtnConfirmText.Text = Properties.OobeStrings.Oobe_Next;
                    BtnConfirmIcon.Icon = SegoeFluentIcons.ChevronRight;
                }

                UpdateConfirmEnabled();

                if (!instant && animateDirection != 0)
                {
                    AnimateContentSlide(animateDirection);
                }
                else
                {
                    StepHostTransform.X = 0;
                    StepHost.Opacity = 1;
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void ApplyStepMeta(int step)
        {
            string title; string subtitle;
            switch (step)
            {
                case 0:
                    title = Properties.OobeStrings.Oobe_Step1Title;
                    subtitle = Properties.OobeStrings.Oobe_Step1Subtitle;
                    break;
                case 1:
                    title = Properties.OobeStrings.Oobe_Step2Title;
                    subtitle = Properties.OobeStrings.Oobe_Step2Subtitle;
                    break;
                case 2:
                    // 第 3 步是「外观」，标题沿用主题页的分组名
                    title = ThemeStrings.Theme_GroupTitle;
                    subtitle = Properties.OobeStrings.Oobe_Step3Subtitle;
                    break;
                case 3:
                    // 第 4 步是「翻页笔与语言」
                    title = Properties.OobeStrings.Oobe_Step4Title;
                    subtitle = Properties.OobeStrings.Oobe_Step4Subtitle;
                    break;
                default:
                    title = string.Empty; subtitle = string.Empty;
                    break;
            }

            StepTitleText.Text = title;
            StepSubtitleText.Text = subtitle;
        }

        private void BuildFinishSummary()
        {
            FinishSummaryHost.Children.Clear();

            string themeText;
            string backdropText = GetSelectedComboBoxContent(ComboBoxWindowBackdrop, ThemeStrings.Theme_WindowBackdrop_None);
            switch (ComboBoxTheme.SelectedIndex)
            {
                case 0: themeText = Properties.OobeStrings.Oobe_ThemeLight; break;
                case 1: themeText = FloatingBarStrings.Theme_Dark; break;
                default: themeText = ThemeStrings.Theme_System; break;
            }

            AddSummaryRow(SegoeFluentIcons.Personalize, Properties.OobeStrings.Oobe_SummaryAppTheme, themeText);
            AddSummaryRow(SegoeFluentIcons.FullScreen, ThemeStrings.Theme_WindowBackdrop, backdropText);
            AddSummaryRow(SegoeFluentIcons.TouchPointer, Properties.OobeStrings.Oobe_SummaryTwoFingerZoom,
                $"{BoolText(CardTwoFingerZoom.IsOn)} / {BoolText(CardTwoFingerTranslate.IsOn)}");
            AddSummaryRow(SegoeFluentIcons.Pin, Properties.OobeStrings.Oobe_SummaryTrayIcon, BoolText(CardEnableTrayIcon.IsOn));
        }

        private static string BoolText(bool value) => value ? Properties.OobeStrings.Oobe_BoolEnabled : Properties.OobeStrings.Oobe_BoolDisabled;

        private static string GetSelectedComboBoxContent(ComboBox comboBox, string fallback)
        {
            return (comboBox?.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? fallback;
        }

        private void AddSummaryRow(FontIconData icon, string label, string value)
        {
            var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(28) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(180) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var fontIcon = new FontIcon { Icon = icon, FontSize = 16, Opacity = 0.85 };
            Grid.SetColumn(fontIcon, 0);
            grid.Children.Add(fontIcon);

            var labelBlock = new TextBlock
            {
                Text = label,
                Opacity = 0.85,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(labelBlock, 1);
            grid.Children.Add(labelBlock);

            var valueBlock = new TextBlock
            {
                Text = value,
                FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(valueBlock, 2);
            grid.Children.Add(valueBlock);

            FinishSummaryHost.Children.Add(grid);
        }

        private void AnimateProgress(double targetPercent, bool instant)
        {
            try
            {
                if (StepProgressBar == null) return;

                if (instant)
                {
                    StepProgressBar.BeginAnimation(System.Windows.Controls.Primitives.RangeBase.ValueProperty, null);
                    StepProgressBar.Value = targetPercent;
                    return;
                }

                // 进度条也走弹簧：值变了就弹过去，不再用固定 320ms 曲线。
                SpringAnimator.AnimateScalar(StepProgressBar, targetPercent,
                    v => StepProgressBar.Value = v, SpringAnimator.Smooth, null);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(ex);
            }
        }

        private void AnimateContentSlide(int direction)
        {
            // direction: 1 = 前进 (新内容从右滑入), -1 = 后退 (从左滑入)
            // 用弹簧引擎，和浮动工具栏同一套物理模型：没有时长、没有曲线，
            // 只有 response（未阻尼周期）和 damping（临界阻尼比例）。
            double from = direction > 0 ? 36 : -36;

            // 位移从 from 弹回 0。Interactive 是"跟手、几乎不过冲"的那档，
            // 翻步要的是快速到位，不是弹一下。
            // 先把弹簧内部值对齐到当前位置，否则它记着上一轮的旧值，会从那儿往过冲。
            StepHostTransform.X = from;
            SpringAnimator.SyncToTransform(StepHostTransform);
            SpringAnimator.Animate(StepHostTransform, 0f, (float)StepHostTransform.Y,
                SpringAnimator.Interactive, null);

            // 透明度单独走弹簧，起始值显式压到 0，避免上一次动画残留。
            StepHost.Opacity = 0;
            SpringAnimator.AnimateScalar(StepHost, 1.0, v => StepHost.Opacity = v,
                SpringAnimator.Snappy, null);
        }
    }
}
