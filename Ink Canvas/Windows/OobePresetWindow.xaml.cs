using Ink_Canvas.Helpers;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using Screen = System.Windows.Forms.Screen;

namespace Ink_Canvas.Windows
{
    public partial class OobePresetWindow : Window
    {
        public enum PresetKind { None, Standard, Teacher, Lite }

        public PresetKind SelectedPreset { get; private set; } = PresetKind.None;

        public OobePresetWindow()
        {
            InitializeComponent();
            WindowBackdropHelper.Apply(this);
        }

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

        #endregion

        private void OobePresetWindow_OnLoaded(object sender, RoutedEventArgs e)
        {
            SetMaxSizeAndCenter();
            RegisterDpiChangedListener();
        }

        private void OobePresetWindow_OnClosing(object sender, CancelEventArgs e)
        {
            UnregisterDpiChangedListener();
        }

        private void SelectPreset(PresetKind kind)
        {
            SelectedPreset = kind;

            // 选中态画在卡片外面那层 Border 上（CardXxxFrame），
            // 不是 SettingsCard 自己 —— SettingsCard 是 ButtonBase 派生，
            // 它对 BorderBrush 的处理取决于模板，不保证可见。
            var defaultBrush = (Brush)FindResource("SystemControlForegroundBaseLowBrush");
            CardStandardFrame.BorderBrush = Brushes.Transparent;
            CardTeacherFrame.BorderBrush = Brushes.Transparent;
            CardLiteFrame.BorderBrush = Brushes.Transparent;
            IconStandard.Opacity = 0;
            IconTeacher.Opacity = 0;
            IconLite.Opacity = 0;

            var accentBrush = (Brush)FindResource("SystemControlForegroundAccentBrush");
            switch (kind)
            {
                case PresetKind.Standard:
                    CardStandardFrame.BorderBrush = accentBrush;
                    IconStandard.Opacity = 1;
                    break;
                case PresetKind.Teacher:
                    CardTeacherFrame.BorderBrush = accentBrush;
                    IconTeacher.Opacity = 1;
                    break;
                case PresetKind.Lite:
                    CardLiteFrame.BorderBrush = accentBrush;
                    IconLite.Opacity = 1;
                    break;
            }

            _ = defaultBrush; // 保留变量引用，避免编译器警告

            BtnApply.IsEnabled = kind != PresetKind.None;
        }

        // 2026-11-17：界面从手搭 Border 换成 iNKORE SettingsCard，
        // 卡片点击走 SettingsCard.Click（RoutedEventHandler），
        // 不再是原来的 MouseLeftButtonDown（MouseButtonEventArgs）。
        private void CardStandard_Click(object sender, RoutedEventArgs e) => SelectPreset(PresetKind.Standard);

        private void CardTeacher_Click(object sender, RoutedEventArgs e) => SelectPreset(PresetKind.Teacher);
        private void CardLite_Click(object sender, RoutedEventArgs e) => SelectPreset(PresetKind.Lite);

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedPreset == PresetKind.None) return;
            DialogResult = true;
            Close();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        // ── 预设定义 ────────────────────────────────────────────────────────

        /// <summary>
        /// 课堂标准配置：适合大多数教学场景，启用 PPT 联动、自动保存、手势等。
        /// </summary>
        public static void ApplyStandard(Settings settings)
        {
            if (settings == null) return;

            // 启动与隐私
            settings.Startup.IsFoldAtStartup = true;
            settings.Startup.CrashAction = 2; // 弹窗重启

            // 画板与墨迹
            settings.Canvas.IsShowCursor = false;
            settings.Canvas.DisablePressure = false;
            settings.Canvas.HideStrokeWhenSelecting = false;
            settings.Canvas.EnablePalmEraser = false;

            // 墨迹纠正
            settings.InkToShape.IsInkToShapeEnabled = true;

            // 手势
            settings.Gesture.IsEnableTwoFingerZoom = false;
            settings.Gesture.IsEnableTwoFingerTranslate = true;

            // 个性化
            settings.Appearance.Theme = 2;
            settings.Appearance.WindowBackdrop = "Acrylic";
            settings.Appearance.EnableTrayIcon = true;
            settings.Appearance.IsShowQuickPanel = true;
            settings.Appearance.EnableHotkeysInMouseMode = false;

            // PPT 联动
            settings.PowerPointSettings.PowerPointSupport = true;
            settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint = true;
            // LTSC D-02: Agent 架构已移除，预设改用 COM。
            settings.PowerPointSettings.PPTLinkMode = PPTLinkMode.Com;
            settings.PowerPointSettings.ShowPPTButton = true;
            settings.PowerPointSettings.PPTButtonsDisplayOption = 2222;
            settings.PowerPointSettings.EnablePPTButtonPageClickable = true;
            settings.PowerPointSettings.EnablePPTButtonLongPressPageTurn = true;
            settings.PowerPointSettings.ShowPPTSidebarByDefault = false;
            settings.PowerPointSettings.ShowPPTModePrompt = false;
            settings.PowerPointSettings.IsShowCanvasAtNewSlideShow = false;
            settings.PowerPointSettings.IsNoClearStrokeOnSelectWhenInPowerPoint = true;
            settings.PowerPointSettings.IsShowStrokeOnSelectInPowerPoint = false;
            settings.PowerPointSettings.IsNotifyPreviousPage = true;
            settings.PowerPointSettings.IsNotifyHiddenPage = false;
            settings.PowerPointSettings.IsNotifyAutoPlayPresentation = true;
            settings.PowerPointSettings.EnableWppProcessKill = true;
            settings.PowerPointSettings.EnablePowerPointEnhancement = false;
            settings.PowerPointSettings.SkipAnimationsWhenGoNext = false;

            // 自动化
            settings.Automation.IsAutoFoldInPPTSlideShow = false;
            settings.Automation.IsEnableAutoSaveStrokes = true;
            settings.Automation.IsAutoSaveScreenshotAtClear = false;
            settings.Automation.IsSaveScreenshotsInDateFolders = false;
            if (settings.Automation.FloatingWindowInterceptor != null)
                settings.Automation.FloatingWindowInterceptor.IsEnabled = false;

            // 随机点名
            settings.RandSettings.ShowRandomAndSingleDraw = true;

            // 高级
            settings.Advanced.IsLogEnabled = true;
        }

        /// <summary>
        /// 教师推荐配置：直接套用内置的那份真实备课机配置。
        ///
        /// 实现方式是「读整份 JSON 再覆盖」，不是逐字段手写。理由：
        /// 那份 JSON 有 400 多个字段，手写不可能写全，而且以后 Settings.cs
        /// 加字段时手写版会悄悄落后 —— 表现就是「教师推荐」跟真实配置对不上，
        /// 这种偏差没有任何报错会提醒你。读文件则天然跟着走。
        ///
        /// 做法约束：
        /// - 不能直接替换引用。调用方持有的是 readonly 的 _settings，
        ///   所以反序列化出一份新的，再逐段赋回它。
        /// - 赋回时必须判空：老版本 JSON 可能缺段。
        /// - 装完要剥掉「属于某台机器」的几项（保存路径、引导标记），
        ///   否则用户一进来就是别人的路径、或者跳过引导。
        /// </summary>
        public static void ApplyTeacher(Settings settings)
        {
            if (settings == null) return;

            Settings preset = LoadEmbeddedSettings("DefaultConfigs.Settings.json");
            if (preset == null)
            {
                // 资源丢了不该让 OOBE 卡住，也不能静默变成空配置 ——
                // 留日志，然后用「课堂标准」兜底，至少是个能用的状态。
                LogHelper.WriteLogToFile(
                    "教师推荐配置读取失败（嵌入资源缺失），回退到课堂标准配置",
                    LogHelper.LogType.Error);
                ApplyStandard(settings);
                return;
            }

            // 逐段赋回。缺段判空，老 JSON 不会把已有值打掉。
            if (preset.Advanced != null) settings.Advanced = preset.Advanced;
            if (preset.Appearance != null) settings.Appearance = preset.Appearance;
            if (preset.Automation != null) settings.Automation = preset.Automation;
            if (preset.PowerPointSettings != null) settings.PowerPointSettings = preset.PowerPointSettings;
            if (preset.Canvas != null) settings.Canvas = preset.Canvas;
            if (preset.Gesture != null) settings.Gesture = preset.Gesture;
            if (preset.InkToShape != null) settings.InkToShape = preset.InkToShape;
            if (preset.Startup != null) settings.Startup = preset.Startup;
            if (preset.RandSettings != null) settings.RandSettings = preset.RandSettings;
            if (preset.ModeSettings != null) settings.ModeSettings = preset.ModeSettings;
            if (preset.Camera != null) settings.Camera = preset.Camera;
            if (preset.Dlass != null) settings.Dlass = preset.Dlass;
            if (preset.Security != null) settings.Security = preset.Security;
            if (preset.Notification != null) settings.Notification = preset.Notification;
            if (preset.Toolbar != null) settings.Toolbar = preset.Toolbar;
            if (preset.MiniWhiteboard != null) settings.MiniWhiteboard = preset.MiniWhiteboard;
            if (preset.ToolbarConfigName != null) settings.ToolbarConfigName = preset.ToolbarConfigName;
            if (preset.BoardToolbarConfigName != null) settings.BoardToolbarConfigName = preset.BoardToolbarConfigName;
            if (preset.Automation?.FloatingWindowInterceptor != null)
                settings.Automation.FloatingWindowInterceptor = preset.Automation.FloatingWindowInterceptor;

            StripMachineSpecificValues(settings);
        }

        /// <summary>
        /// 剥掉不该跟配置走的东西。
        ///
        /// 这一条是整个方案的关键：能整份套用，前提是知道哪几项「属于机器不属于配置」。
        /// 现在只有两项，以后如果配置文件里再混进机器相关的东西，也在这里加。
        /// </summary>
        private static void StripMachineSpecificValues(Settings settings)
        {
            // 1) 保存路径。素材里是某台机器上的 C:\Users\...，必须清空，
            //    让运行时按 exe 目录重算（LoadSettings 里有这段修正逻辑）。
            if (settings.Automation != null)
            {
                settings.Automation.AutoSavedStrokesLocation = string.Empty;
            }

            // 2) 引导完成标记。这是「这台机器走过引导了」，不是配置项。
            //    不清的话用户套用预设后不会再看到引导。
            if (settings.Startup != null)
            {
                settings.Startup.HasShownOobe = false;
            }

            // 3) 密码。预设里不该带任何密码哈希。
            if (settings.Security != null)
            {
                settings.Security.PasswordSalt = string.Empty;
                settings.Security.PasswordHash = string.Empty;
                settings.Security.PasswordEnabled = false;
            }
        }

        /// <summary>
        /// 从嵌入资源读一份 Settings。失败返回 null，由调用方决定怎么兜底。
        /// </summary>
        private static Settings LoadEmbeddedSettings(string resourceName)
        {
            try
            {
                var asm = System.Reflection.Assembly.GetExecutingAssembly();
                using (var stream = asm.GetManifestResourceStream(resourceName))
                {
                    if (stream == null)
                    {
                        LogHelper.WriteLogToFile(
                            $"嵌入资源不存在: {resourceName}",
                            LogHelper.LogType.Error);
                        return null;
                    }

                    using (var reader = new System.IO.StreamReader(stream, System.Text.Encoding.UTF8))
                    {
                        string json = reader.ReadToEnd();
                        return Newtonsoft.Json.JsonConvert.DeserializeObject<Settings>(json);
                    }
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile(
                    $"读取嵌入配置 {resourceName} 失败: {ex.Message}",
                    LogHelper.LogType.Error);
                return null;
            }
        }


        /// <summary>
        /// 简洁轻量配置：最小化后台行为，适合简单批注场景。
        /// </summary>
        public static void ApplyLite(Settings settings)
        {
            if (settings == null) return;

            // 启动与隐私
            settings.Startup.IsFoldAtStartup = true;
            settings.Startup.CrashAction = 0;

            // 画板与墨迹
            settings.Canvas.IsShowCursor = false;
            settings.Canvas.DisablePressure = false;
            settings.Canvas.HideStrokeWhenSelecting = true;
            settings.Canvas.EnablePalmEraser = false;

            // 墨迹纠正
            settings.InkToShape.IsInkToShapeEnabled = false;

            // 手势
            settings.Gesture.IsEnableTwoFingerZoom = false;
            settings.Gesture.IsEnableTwoFingerTranslate = false;

            // 个性化
            settings.Appearance.Theme = 2; // 跟随系统
            settings.Appearance.WindowBackdrop = "None";
            settings.Appearance.EnableTrayIcon = true;
            settings.Appearance.IsShowQuickPanel = false;
            settings.Appearance.EnableHotkeysInMouseMode = false;

            // PPT 联动
            settings.PowerPointSettings.PowerPointSupport = true;
            settings.PowerPointSettings.IsAutoSaveStrokesInPowerPoint = true;

            // 自动化
            settings.Automation.IsAutoFoldInPPTSlideShow = false;
            settings.Automation.IsEnableAutoSaveStrokes = true;
            settings.Automation.IsAutoSaveScreenshotAtClear = true;
            settings.Automation.IsSaveScreenshotsInDateFolders = false;
            if (settings.Automation.FloatingWindowInterceptor != null)
                settings.Automation.FloatingWindowInterceptor.IsEnabled = false;

            // 随机点名
            settings.RandSettings.ShowRandomAndSingleDraw = true;

            // 高级
            settings.Advanced.IsLogEnabled = true;
        }
    }
}
