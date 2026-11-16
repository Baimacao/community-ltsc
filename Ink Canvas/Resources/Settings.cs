using Ink_Canvas.Controls.Toolbar.FloatingToolbar;
using Newtonsoft.Json;
using OSVersionExtension;
using System;
using System.Collections.Generic;
using System.IO;

namespace Ink_Canvas
{
    public class Settings
    {
        [JsonProperty("advanced")]
        public Advanced Advanced { get; set; } = new Advanced();

        [JsonProperty("appearance")]
        public Appearance Appearance { get; set; } = new Appearance();

        [JsonProperty("automation")]
        public Automation Automation { get; set; } = new Automation();

        [JsonProperty("behavior")]
        public PowerPointSettings PowerPointSettings { get; set; } = new PowerPointSettings();

        [JsonProperty("canvas")]
        public Canvas Canvas { get; set; } = new Canvas();

        [JsonProperty("gesture")]
        public Gesture Gesture { get; set; } = new Gesture();

        [JsonProperty("inkToShape")]
        public InkToShape InkToShape { get; set; } = new InkToShape();

        [JsonProperty("startup")]
        public Startup Startup { get; set; } = new Startup();

        [JsonProperty("randSettings")]
        public RandSettings RandSettings { get; set; } = new RandSettings();

        [JsonProperty("modeSettings")]
        public ModeSettings ModeSettings { get; set; } = new ModeSettings();

        [JsonProperty("camera")]
        public CameraSettings Camera { get; set; } = new CameraSettings();

        [JsonProperty("dlass")]
        public DlassSettings Dlass { get; set; } = new DlassSettings();

        [JsonProperty("security")]
        public Security Security { get; set; } = new Security();

        [JsonProperty("notification")]
        public NotificationSettings Notification { get; set; } = new NotificationSettings();

        [JsonProperty("toolbar")]
        public ToolbarLayoutSettings Toolbar { get; set; } = new ToolbarLayoutSettings();

        [JsonProperty("toolbarConfigName")]
        public string ToolbarConfigName { get; set; } = "default";

        [JsonProperty("boardToolbarConfigName")]
        public string BoardToolbarConfigName { get; set; } = "default";

        [JsonProperty("miniWhiteboard")]
        public MiniWhiteboardSettings MiniWhiteboard { get; set; } = new MiniWhiteboardSettings();
    }

    public class NotificationSettings
    {
        [JsonProperty("placement")]
        public string Placement { get; set; } = "TopCenter";
    }

    public class Security
    {
        [JsonProperty("passwordEnabled")]
        public bool PasswordEnabled { get; set; } = false;
        [JsonProperty("passwordSalt")]
        public string PasswordSalt { get; set; } = "";
        [JsonProperty("passwordHash")]
        public string PasswordHash { get; set; } = "";
        [JsonProperty("requirePasswordOnExit")]
        public bool RequirePasswordOnExit { get; set; } = false;
        [JsonProperty("requirePasswordOnEnterSettings")]
        public bool RequirePasswordOnEnterSettings { get; set; } = false;
        [JsonProperty("requirePasswordOnResetConfig")]
        public bool RequirePasswordOnResetConfig { get; set; } = false;
        [JsonProperty("requirePasswordOnModifyOrClearNameList")]
        public bool RequirePasswordOnModifyOrClearNameList { get; set; } = false;
    }

    public class Canvas
    {
        [JsonProperty("inkWidth")]
        public double InkWidth { get; set; } = 2.5;

        [JsonProperty("highlighterWidth")]
        public double HighlighterWidth { get; set; } = 20;

        [JsonProperty("highlighterOverlapEnabled")]
        public bool HighlighterOverlapEnabled { get; set; } = false;

        [JsonProperty("inkAlpha")]
        public double InkAlpha { get; set; } = 255;

        [JsonProperty("highlighterAlpha")]
        public double HighlighterAlpha { get; set; } = 255;

        [JsonProperty("isShowCursor")]
        public bool IsShowCursor { get; set; }
        /// <summary>画笔光标类型：0 系统光标，1 软件内置光标（默认），2 用户自定义光标。</summary>
        [JsonProperty("penCursorType")]
        public int PenCursorType { get; set; } = 1;
        /// <summary>用户自定义光标文件路径（当 PenCursorType == 2 时使用）。</summary>
        [JsonProperty("customPenCursorPath")]
        public string CustomPenCursorPath { get; set; } = "";
        /// <summary>笔锋存储值：0 基于点集，1 基于速率，2 关闭，3 实时笔锋（速度与压感混合）。界面下拉顺序为实时笔锋、点集、速率、关闭。</summary>
        [JsonProperty("inkStyle")]
        public int InkStyle { get; set; }
        [JsonProperty("eraserSize")]
        public int EraserSize { get; set; } = 2;
        [JsonProperty("eraserShapeType")]
        public int EraserShapeType { get; set; } // 0 - 圆形擦  1 - 黑板擦
        [JsonProperty("hideStrokeWhenSelecting")]
        public bool HideStrokeWhenSelecting { get; set; } = true;
        [JsonProperty("fitToCurve")]
        public bool FitToCurve { get; set; } // 默认关闭原来的贝塞尔平滑
        [JsonProperty("useAdvancedBezierSmoothing")]
        public bool UseAdvancedBezierSmoothing { get; set; } = true; // 默认启用高级贝塞尔曲线平滑
        [JsonProperty("mergeInkSmoothingWithUndo")]
        public bool MergeInkSmoothingWithUndo { get; set; } = false;
        [JsonProperty("useAsyncInkSmoothing")]
        public bool UseAsyncInkSmoothing { get; set; } = true; // 默认启用异步墨迹平滑
        [JsonProperty("useHardwareAcceleration")]
        public bool UseHardwareAcceleration { get; set; } = true; // 默认启用硬件加速
        [JsonProperty("inkSmoothingQuality")]
        public int InkSmoothingQuality { get; set; } = 2; // 0-低质量高性能, 1-平衡, 2-高质量低性能，默认为高质量
        [JsonProperty("maxConcurrentSmoothingTasks")]
        public int MaxConcurrentSmoothingTasks { get; set; } // 0表示自动检测CPU核心数
        [JsonProperty("clearCanvasAndClearTimeMachine")]
        public bool ClearCanvasAndClearTimeMachine { get; set; }
        [JsonProperty("enablePressureTouchMode")]
        public bool EnablePressureTouchMode { get; set; } // 是否启用压感触屏模式
        [JsonProperty("disablePressure")]
        public bool DisablePressure { get; set; } // 是否屏蔽压感
        [JsonProperty("autoStraightenLine")]
        public bool AutoStraightenLine { get; set; } = true; // 是否启用直线自动拉直
        [JsonProperty("autoStraightenLineThreshold")]
        public int AutoStraightenLineThreshold { get; set; } = 80; // 直线自动拉直的长度阈值（像素）
        [JsonProperty("highPrecisionLineStraighten")]
        public bool HighPrecisionLineStraighten { get; set; } = true; // 是否启用高精度直线拉直
        [JsonProperty("pauseStraightenLine")]
        public bool PauseStraightenLine { get; set; } = false; // 是否启用停顿拉直（书写中停顿时自动拉直笔画）
        [JsonProperty("pauseStraightenDelay")]
        public int PauseStraightenDelay { get; set; } = 300; // 停顿拉直触发延迟（毫秒）
        [JsonProperty("lineEndpointSnapping")]
        public bool LineEndpointSnapping { get; set; } = true; // 是否启用直线端点吸附
        [JsonProperty("lineEndpointSnappingThreshold")]
        public int LineEndpointSnappingThreshold { get; set; } = 15; // 直线端点吸附的距离阈值（像素）
        [JsonProperty("usingWhiteboard")]
        public bool UsingWhiteboard { get; set; }
        [JsonProperty("customBackgroundColor")]
        public string CustomBackgroundColor { get; set; } = "#162924";
        [JsonProperty("hyperbolaAsymptoteOption")]
        public OptionalOperation HyperbolaAsymptoteOption { get; set; } = OptionalOperation.Ask;
        [JsonProperty("isCompressPicturesUploaded")]
        public bool IsCompressPicturesUploaded { get; set; }
        [JsonProperty("enablePalmEraser")]
        public bool EnablePalmEraser { get; set; } = true;
        [JsonProperty("palmEraserSensitivity")]
        public int PalmEraserSensitivity { get; set; } = 0; // 0-低敏感度, 1-中敏感度, 2-高敏感度
        [JsonProperty("clearCanvasAlsoClearImages")]
        public bool ClearCanvasAlsoClearImages { get; set; } = true;
        [JsonProperty("showCircleCenter")]
        public bool ShowCircleCenter { get; set; }
        [JsonProperty("showCoordinateUnitMarks")]
        public bool ShowCoordinateUnitMarks { get; set; }
        [JsonProperty("enableInkFade")]
        public bool EnableInkFade { get; set; } = false;
        [JsonProperty("inkFadeTime")]
        public int InkFadeTime { get; set; } = 3000;
        [JsonProperty("inkFadeSpeedMultiplier")]
        public double InkFadeSpeedMultiplier { get; set; } = 1.0;
        [JsonProperty("laserPenWidth")]
        public double LaserPenWidth { get; set; } = 5;
        [JsonProperty("laserPenAlpha")]
        public int LaserPenAlpha { get; set; } = 128;
        [JsonProperty("enableBrushAutoRestore")]
        public bool EnableBrushAutoRestore { get; set; } = false;
        [JsonProperty("brushAutoRestoreDelaySeconds")]
        public int BrushAutoRestoreDelaySeconds { get; set; } = 30;
        [JsonProperty("brushAutoRestoreTimes")]
        public string BrushAutoRestoreTimes { get; set; } = "";
        [JsonProperty("brushAutoRestoreColor")]
        public string BrushAutoRestoreColor { get; set; } = "#FFFF0000";
        [JsonProperty("brushAutoRestoreWidth")]
        public double BrushAutoRestoreWidth { get; set; } = 5;
        [JsonProperty("brushAutoRestoreAlpha")]
        public int BrushAutoRestoreAlpha { get; set; } = 255;
        [JsonProperty("enableEraserAutoSwitchBack")]
        public bool EnableEraserAutoSwitchBack { get; set; } = false;
        [JsonProperty("eraserAutoSwitchBackDelaySeconds")]
        public int EraserAutoSwitchBackDelaySeconds { get; set; } = 10; // 默认10秒
        [JsonProperty("velocityBrushTipMix")]
        public double VelocityBrushTipMix { get; set; } = 0.45;
        [JsonProperty("realtimeBrushTipMinDistanceScale")]
        public double RealtimeBrushTipMinDistanceScale { get; set; } = 0.5;
        [JsonProperty("enableVelocityBrushTip")]
        public bool EnableVelocityBrushTip { get; set; }

        /// <summary>为 true 时，白板工具栏「展台」按钮启动希沃视频展台（sweclauncher），否则使用内置展台。</summary>
        [JsonProperty("launchSeewoVideoShowcaseForWhiteboardBooth")]
        public bool LaunchSeewoVideoShowcaseForWhiteboardBooth { get; set; } = false;

    }

    public enum OptionalOperation
    {
        Yes,
        No,
        Ask
    }

    public class Gesture
    {
        [JsonIgnore]
        public bool IsEnableTwoFingerGesture => IsEnableTwoFingerZoom || IsEnableTwoFingerTranslate || IsEnableTwoFingerRotation
            || IsEnableTwoFingerZoomBoard || IsEnableTwoFingerTranslateBoard || IsEnableTwoFingerRotationBoard;
        [JsonIgnore]
        public bool IsEnableTwoFingerGestureTranslateOrRotation => IsEnableTwoFingerTranslate || IsEnableTwoFingerRotation
            || IsEnableTwoFingerTranslateBoard || IsEnableTwoFingerRotationBoard;
        [JsonProperty("isEnableMultiTouchMode")]
        public bool IsEnableMultiTouchMode { get; set; } = false;
        [JsonProperty("isEnableTwoFingerZoom")]
        public bool IsEnableTwoFingerZoom { get; set; } = true;
        [JsonProperty("isEnableTwoFingerTranslate")]
        public bool IsEnableTwoFingerTranslate { get; set; } = true;
        [JsonProperty("isEnableTwoFingerRotation")]
        public bool IsEnableTwoFingerRotation { get; set; }
        [JsonProperty("isEnableTwoFingerRotationOnSelection")]
        public bool IsEnableTwoFingerRotationOnSelection { get; set; }

        [JsonProperty("isEnableMultiTouchModeBoard")]
        public bool IsEnableMultiTouchModeBoard { get; set; } = false;
        [JsonProperty("isEnableTwoFingerZoomBoard")]
        public bool IsEnableTwoFingerZoomBoard { get; set; } = true;
        [JsonProperty("isEnableTwoFingerTranslateBoard")]
        public bool IsEnableTwoFingerTranslateBoard { get; set; } = true;
        [JsonProperty("isEnableTwoFingerRotationBoard")]
        public bool IsEnableTwoFingerRotationBoard { get; set; }
    }

    // 更新通道枚举
    public enum UpdateChannel
    {
        Release,
        Preview,
        Beta
    }

    /// <summary>自动更新要下载的安装包架构。默认跟随当前软件进程架构；64 位包对应发布物 ZIP 文件名在 .zip 前增加 -x64。</summary>
    public enum UpdatePackageArchitecture
    {
        /// <summary>32 位包，例如 InkCanvasForClass.CE.1.7.0.0.zip</summary>
        X86 = 0,
        /// <summary>64 位包，例如 InkCanvasForClass.CE.1.7.0.0-x64.zip</summary>
        X64 = 1
    }

    /// <summary>
    /// 遥测上传等级
    /// </summary>
    public enum TelemetryUploadLevel
    {
        /// <summary>
        /// 不上传任何匿名使用数据
        /// </summary>
        None = 0,
        /// <summary>
        /// 仅上传基础数据
        /// </summary>
        Basic = 1,
        /// <summary>
        /// 上传基础数据 + 可选数据
        /// </summary>
        Extended = 2
    }

    public class Startup
    {
        [JsonProperty("isAutoUpdate")]
        public bool IsAutoUpdate { get; set; } = true;
        [JsonProperty("isAutoUpdateWithSilence")]
        public bool IsAutoUpdateWithSilence { get; set; }
        [JsonProperty("isAutoUpdateWithSilenceStartTime")]
        public string AutoUpdateWithSilenceStartTime { get; set; } = "06:00";
        [JsonProperty("isAutoUpdateWithSilenceEndTime")]
        public string AutoUpdateWithSilenceEndTime { get; set; } = "22:00";
        [JsonProperty("updateChannel")]
        public UpdateChannel UpdateChannel { get; set; } = UpdateChannel.Release;
        [JsonProperty("updatePackageArchitecture")]
        public UpdatePackageArchitecture UpdatePackageArchitecture { get; set; } = Environment.Is64BitProcess ? UpdatePackageArchitecture.X64 : UpdatePackageArchitecture.X86;
        [JsonProperty("isEnableNibMode")]
        public bool IsEnableNibMode { get; set; }
        [JsonProperty("isFoldAtStartup")]
        public bool IsFoldAtStartup { get; set; }
        [JsonProperty("crashAction")]
        public int CrashAction { get; set; } = 2;
        [JsonProperty("telemetryUploadLevel")]
        public TelemetryUploadLevel TelemetryUploadLevel { get; set; } = TelemetryUploadLevel.None;
        [JsonProperty("hasAcceptedTelemetryPrivacy")]
        public bool HasAcceptedTelemetryPrivacy { get; set; } = false;
        [JsonProperty("hasShownOobe")]
        public bool HasShownOobe { get; set; } = false;

    }

    public enum TrayClickAction
    {
        ShowMenu = 0,
        HideShowMainWindow = 1,
        TempShowMainWindow = 2,
        OpenSettings = 3,
        DisableAllHotkeys = 4,
        ForceFullScreen = 5,
        ToggleFoldFloatingBar = 6,
        ResetFloatingBarPosition = 7,
        RestartApp = 8,
        CloseApp = 9,
        NoAction = 10
    }

    public enum ToolbarPosition
    {
        Right = 0,
        Left = 1,
        Top = 2,
        Bottom = 3
    }

    public class Appearance
    {
        [JsonProperty("viewboxFloatingBarScaleTransformValue")]
        public double ViewboxFloatingBarScaleTransformValue { get; set; } = 1.0;
        [JsonProperty("floatingBarImg")]
        public int FloatingBarImg { get; set; }
        [JsonProperty("customFloatingBarImgs")]
        public List<CustomFloatingBarIcon> CustomFloatingBarImgs { get; set; } = new List<CustomFloatingBarIcon>();
        [JsonProperty("viewboxFloatingBarOpacityValue")]
        public double ViewboxFloatingBarOpacityValue { get; set; } = 1.0;
        [JsonProperty("enableTrayIcon")]
        public bool EnableTrayIcon { get; set; } = true;
        [JsonProperty("trayLeftClickAction")]
        public TrayClickAction TrayLeftClickAction { get; set; } = TrayClickAction.ShowMenu;
        [JsonProperty("trayRightClickAction")]
        public TrayClickAction TrayRightClickAction { get; set; } = TrayClickAction.ShowMenu;
        [JsonProperty("viewboxFloatingBarOpacityInPPTValue")]
        public double ViewboxFloatingBarOpacityInPPTValue { get; set; } = 0.5;
        [JsonProperty("floatingBarMenuOpacity")]
        public double FloatingBarMenuOpacity { get; set; } = 1.0;
        [JsonProperty("floatingBarMenuOpacityInPPT")]
        public double FloatingBarMenuOpacityInPPT { get; set; } = 1.0;
        [JsonProperty("boardMenuOpacity")]
        public double BoardMenuOpacity { get; set; } = 1.0;
        [JsonProperty("viewboxBlackBoardScaleTransformValue")]
        public double ViewboxBlackBoardScaleTransformValue { get; set; } = 1;
        [JsonProperty("viewboxBlackBoardLeftScaleTransformValue")]
        public double ViewboxBlackBoardLeftScaleTransformValue { get; set; } = 1;
        [JsonProperty("viewboxBlackBoardRightScaleTransformValue")]
        public double ViewboxBlackBoardRightScaleTransformValue { get; set; } = 1;
        [JsonProperty("boardToolbarLeftOpacity")]
        public double BoardToolbarLeftOpacity { get; set; } = 0.77;
        [JsonProperty("boardToolbarCenterOpacity")]
        public double BoardToolbarCenterOpacity { get; set; } = 0.77;
        [JsonProperty("boardToolbarRightOpacity")]
        public double BoardToolbarRightOpacity { get; set; } = 0.77;
        [JsonProperty("enableTimeDisplayInWhiteboardMode")]
        public bool EnableTimeDisplayInWhiteboardMode { get; set; } = true;
        [JsonProperty("enableChickenSoupInWhiteboardMode")]
        public bool EnableChickenSoupInWhiteboardMode { get; set; } = true;
        [JsonProperty("unFoldButtonImageType")]
        public int UnFoldButtonImageType { get; set; }
        [JsonProperty("enableSplashScreen")]
        /// <summary>
        /// 【已废弃】启动画面（SplashScreen）已整体移除，改为浮动工具栏入场动画。
        ///
        /// 字段保留只是为了旧配置文件能正常反序列化——直接删掉会让老配置文件里
        /// 的这个键无处可去，Newtonsoft 虽然会忽略未知键，但留着更明确。
        /// 代码里不要再读它，读了也不会生效。
        /// </summary>
        public bool EnableSplashScreen { get; set; } = false;
        [JsonProperty("splashScreenStyle")]
        /// <summary>【已废弃】启动画面已移除，保留字段仅为兼容旧配置文件，读取不会生效。</summary>
        public int SplashScreenStyle { get; set; } = 1; // 0-随机, 1-跟随四季, 2-春季, 3-夏季, 4-秋季, 5-冬季, 6-马年限定 
        [JsonProperty("customSplashImagePath")]
        /// <summary>【已废弃】启动画面已移除，保留字段仅为兼容旧配置文件，读取不会生效。</summary>
        public string CustomSplashImagePath { get; set; } = string.Empty;
        [JsonProperty("customSplashTextPosition")]
        /// <summary>【已废弃】启动画面已移除，保留字段仅为兼容旧配置文件，读取不会生效。</summary>
        public int CustomSplashTextPosition { get; set; } = 1; // 0-左下, 1-中下, 2-右下
        [JsonProperty("isShowQuickPanel")]
        public bool IsShowQuickPanel { get; set; } = true;
        [JsonProperty("chickenSoupSource")]
        public int ChickenSoupSource { get; set; } = 1;
        [JsonProperty("chickenSoupPosition")]
        public string ChickenSoupPosition { get; set; } = "TopRight";
        [JsonProperty("enableChickenSoupAutoRotation")]
        public bool EnableChickenSoupAutoRotation { get; set; } = false;
        [JsonProperty("chickenSoupAutoRotationInterval")]
        public int ChickenSoupAutoRotationInterval { get; set; } = 60;
        [JsonProperty("customTipsSchemes", NullValueHandling = NullValueHandling.Ignore)]
        public List<TipsScheme> CustomTipsSchemes { get; set; }
        [JsonProperty("enabledPresetTipsSources", NullValueHandling = NullValueHandling.Ignore)]
        public List<string> EnabledPresetTipsSources { get; set; }
        [JsonProperty("theme")]
        public int Theme { get; set; } = 2;
        [JsonProperty("windowBackdrop")]
        public string WindowBackdrop { get; set; } = "Mica";

        [JsonProperty("compactFloatingBar")]
        public bool CompactFloatingBar { get; set; } = false;

        [JsonProperty("hideFloatingBarBorder")]
        public bool HideFloatingBarBorder { get; set; } = false;


        [JsonProperty("isShowQuickColorPalette")]
        public bool IsShowQuickColorPalette { get; set; }

        [JsonProperty("quickColorPaletteDisplayMode")]
        public int QuickColorPaletteDisplayMode { get; set; } = 1;

        [JsonProperty("enableHotkeysInMouseMode")]
        public bool EnableHotkeysInMouseMode { get; set; } = false;

        [JsonProperty("language")]
        public string Language { get; set; } = "";

        [JsonProperty("use24HourTimeFormat")]
        public bool Use24HourTimeFormat { get; set; } = false;

        [JsonProperty("quickPanelBottomOffset")]
        public double QuickPanelBottomOffset { get; set; } = -150;

        [JsonProperty("showPenColorOnFloatingBarIcon")]
        public bool ShowPenColorOnFloatingBarIcon { get; set; } = false;

        [JsonProperty("allowDragSidePanel")]
        public bool AllowDragSidePanel { get; set; } = true;

        [JsonProperty("quickPanelOpacity")]
        public double QuickPanelOpacity { get; set; } = 1.0;

        [JsonProperty("isAutoCollapseQuickPanel")]
        public bool IsAutoCollapseQuickPanel { get; set; } = false;

        [JsonProperty("autoCollapseQuickPanelDelay")]
        public double AutoCollapseQuickPanelDelay { get; set; } = 3.0;

        [JsonProperty("toolbarPosition")]
        public ToolbarPosition ToolbarPosition { get; set; } = ToolbarPosition.Right;

        [JsonProperty("reverseToolbarContent")]
        public bool ReverseToolbarContent { get; set; } = false;

        [JsonProperty("autoFlipWhenSpaceInsufficient")]
        public bool AutoFlipWhenSpaceInsufficient { get; set; } = true;

        [JsonProperty("flipContentOnAutoFlip")]
        public bool FlipContentOnAutoFlip { get; set; } = false;

        [JsonProperty("disableToolbarAnimation")]
        public bool DisableToolbarAnimation { get; set; } = false;

        /// <summary>
        /// LTSC D-05：全局动效开关。为 true 时全仓所有动画退化为瞬时完成。
        /// 默认 false，保持与旧版一致的视觉表现。
        /// </summary>
        [JsonProperty("disableAllAnimations")]
        public bool DisableAllAnimations { get; set; } = false;
    }

    public enum PPTLinkMode
    {
        Com = 0,
        Rot = 1,
        Agent = 2
    }

    public enum UIAMode
    {
        UserToken = 0,
        ProcessToken = 1
    }

    public class PowerPointSettings
    {
        [JsonProperty("showPPTButton")]
        public bool ShowPPTButton { get; set; } = true;

        // 每一个数位代表一个选项，2就是开启，1就是关闭
        [JsonProperty("pptButtonsDisplayOption")]
        public int PPTButtonsDisplayOption { get; set; } = 2222;

        // 0居中，+就是往上，-就是往下
        [JsonProperty("pptLSButtonPosition")]
        public int PPTLSButtonPosition { get; set; }

        // 0居中，+就是往上，-就是往下
        [JsonProperty("pptRSButtonPosition")]
        public int PPTRSButtonPosition { get; set; }

        // 0居中，+就是往右，-就是往左
        [JsonProperty("pptLBButtonPosition")]
        public int PPTLBButtonPosition { get; set; }

        // 0居中，+就是往右，-就是往左
        [JsonProperty("pptRBButtonPosition")]
        public int PPTRBButtonPosition { get; set; }

        [JsonProperty("pptSButtonsOption")]
        public int PPTSButtonsOption { get; set; } = 221;

        [JsonProperty("pptBButtonsOption")]
        public int PPTBButtonsOption { get; set; } = 121;

        [JsonProperty("enablePPTButtonPageClickable")]
        public bool EnablePPTButtonPageClickable { get; set; } = true;

        [JsonProperty("enablePPTButtonLongPressPageTurn")]
        public bool EnablePPTButtonLongPressPageTurn { get; set; } = true;

        [JsonProperty("pptLSButtonOpacity")]
        public double PPTLSButtonOpacity { get; set; } = 0.5;

        [JsonProperty("pptRSButtonOpacity")]
        public double PPTRSButtonOpacity { get; set; } = 0.5;

        [JsonProperty("pptLBButtonOpacity")]
        public double PPTLBButtonOpacity { get; set; } = 0.5;

        [JsonProperty("pptRBButtonOpacity")]
        public double PPTRBButtonOpacity { get; set; } = 0.5;

        [JsonProperty("pptNavBarScale")]
        public double PPTNavBarScale { get; set; } = 1.0;

        // 全局默认设置（各位置"使用全局设置"开启时引用这些值）
        [JsonProperty("pptGlobalButtonEnabled")]
        public bool PPTGlobalButtonEnabled { get; set; } = true;

        [JsonProperty("pptGlobalShowPageNumber")]
        public bool PPTGlobalShowPageNumber { get; set; } = true;
        [JsonProperty("pptGlobalSideButtonPosition")]
        public int PPTGlobalSideButtonPosition { get; set; } = 0;

        [JsonProperty("pptGlobalBottomButtonPosition")]
        public int PPTGlobalBottomButtonPosition { get; set; } = 0;

        [JsonProperty("pptGlobalButtonOpacity")]
        public double PPTGlobalButtonOpacity { get; set; } = 0.5;

        // 每位置"使用全局设置"开关（默认开启，开启时该位置外观跟随全局）
        [JsonProperty("pptLSUseGlobalSettings")]
        public bool PPTLSUseGlobalSettings { get; set; } = true;

        // ── LTSC P1c 外观增强（2026-11-11）────────────────────────────
        // 三项都来自实际授课场景：
        // · 页码字号：投影/大屏上 17px 的页码坐在后排看不清楚
        // · 自动隐藏：翻页条常驻会挡住板书右下角
        // · 触感反馈：投影模式下点按钮几乎没反馈，老师不确定点没点中

        /// <summary>当前页码字号（像素）。默认 17，即改造前的固定值。</summary>
        [JsonProperty("pptPageNumberFontSize")]
        public double PPTPageNumberFontSize { get; set; } = 17;

        /// <summary>总页数字号（像素）。默认 10，即改造前的固定值。</summary>
        [JsonProperty("pptPageTotalFontSize")]
        public double PPTPageTotalFontSize { get; set; } = 10;

        /// <summary>
        /// 翻页条自动淡出：鼠标/触摸静止一段时间后，翻页条降到极低透明度，不再挡板书。
        /// 默认关闭，保持和改造前一致的行为。
        /// </summary>
        [JsonProperty("pptNavBarAutoHide")]
        /// <summary>翻页组件自动淡出。默认开启——老师上课时不希望它一直挡在 PPT 上。</summary>
        public bool PPTNavBarAutoHide { get; set; } = true;

        /// <summary>自动淡出前的静止时间（秒）。默认 3 秒。</summary>
        [JsonProperty("pptNavBarAutoHideDelaySeconds")]
        public double PPTNavBarAutoHideDelaySeconds { get; set; } = 3.0;

        /// <summary>自动淡出后的透明度。默认 0.08，仍能看见轮廓，不至于完全找不到。</summary>
        [JsonProperty("pptNavBarAutoHideOpacity")]
        public double PPTNavBarAutoHideOpacity { get; set; } = 0.08;

        /// <summary>
        /// LTSC D-13：主悬浮窗（屏幕/PPT 批注模式下的底部工具栏）是否也做空闲减淡。
        ///
        /// 和翻页条的区别：
        ///   - 只在非白板模式 + PPT 联动放映时生效
        ///   - 计时按「整个画布有没有操作」，不是「有没有碰到工具栏」——
        ///     这是触摸屏，老师写字时笔不会碰工具栏，按那个算会一节课都淡着
        ///   - 淡出比例更保守，工具栏要一直看得见轮廓
        /// </summary>
        [JsonProperty("floatingBarIdleFade")]
        public bool FloatingBarIdleFade { get; set; } = true;

        [JsonProperty("floatingBarIdleFadeDelaySeconds")]
        public double FloatingBarIdleFadeDelaySeconds { get; set; } = 4.0;

        [JsonProperty("floatingBarIdleFadeOpacity")]
        public double FloatingBarIdleFadeOpacity { get; set; } = 0.45;

        /// <summary>
        /// 按下翻页按钮时给一点额外视觉反馈强度（除原有半透明高亮外再加一层）。
        /// 默认开启；投影下能明显看出「点到了」。
        /// </summary>
        [JsonProperty("pptButtonPressFeedback")]
        public bool PPTButtonPressFeedback { get; set; } = true;

        /// <summary>按下反馈的不透明度。默认 0.35。</summary>
        [JsonProperty("pptButtonPressFeedbackOpacity")]
        public double PPTButtonPressFeedbackOpacity { get; set; } = 0.35;


        [JsonProperty("pptRSUseGlobalSettings")]
        public bool PPTRSUseGlobalSettings { get; set; } = true;

        [JsonProperty("pptLBUseGlobalSettings")]
        public bool PPTLBUseGlobalSettings { get; set; } = true;

        [JsonProperty("pptRBUseGlobalSettings")]
        public bool PPTRBUseGlobalSettings { get; set; } = true;

        // 每位置独立缩放（"使用全局设置"关闭时可独立设置；开启时跟随 PPTNavBarScale）
        [JsonProperty("pptLSButtonScale")]
        public double PPTLSButtonScale { get; set; } = 1.0;

        [JsonProperty("pptRSButtonScale")]
        public double PPTRSButtonScale { get; set; } = 1.0;

        [JsonProperty("pptLBButtonScale")]
        public double PPTLBButtonScale { get; set; } = 1.0;

        [JsonProperty("pptRBButtonScale")]
        public double PPTRBButtonScale { get; set; } = 1.0;

        private bool? _pptLSShowPageNumber;
        [JsonProperty("pptLSShowPageNumber")]
        public bool PPTLSShowPageNumber
        {
            get
            {
                if (_pptLSShowPageNumber == null)
                {
                    string sOpt = PPTSButtonsOption.ToString();
                    _pptLSShowPageNumber = sOpt.Length > 0 && sOpt[0] == '2';
                }
                return _pptLSShowPageNumber.Value;
            }
            set => _pptLSShowPageNumber = value;
        }

        private bool? _pptRSShowPageNumber;
        [JsonProperty("pptRSShowPageNumber")]
        public bool PPTRSShowPageNumber
        {
            get
            {
                if (_pptRSShowPageNumber == null)
                {
                    string sOpt = PPTSButtonsOption.ToString();
                    _pptRSShowPageNumber = sOpt.Length > 0 && sOpt[0] == '2';
                }
                return _pptRSShowPageNumber.Value;
            }
            set => _pptRSShowPageNumber = value;
        }

        private bool? _pptLBShowPageNumber;
        [JsonProperty("pptLBShowPageNumber")]
        public bool PPTLBShowPageNumber
        {
            get
            {
                if (_pptLBShowPageNumber == null)
                {
                    string bOpt = PPTBButtonsOption.ToString();
                    _pptLBShowPageNumber = bOpt.Length > 0 && bOpt[0] == '2';
                }
                return _pptLBShowPageNumber.Value;
            }
            set => _pptLBShowPageNumber = value;
        }

        private bool? _pptRBShowPageNumber;
        [JsonProperty("pptRBShowPageNumber")]
        public bool PPTRBShowPageNumber
        {
            get
            {
                if (_pptRBShowPageNumber == null)
                {
                    string bOpt = PPTBButtonsOption.ToString();
                    _pptRBShowPageNumber = bOpt.Length > 0 && bOpt[0] == '2';
                }
                return _pptRBShowPageNumber.Value;
            }
            set => _pptRBShowPageNumber = value;
        }

        // -- new --

        [JsonProperty("powerPointSupport")]
        public bool PowerPointSupport { get; set; } = true;
        [JsonProperty("isShowCanvasAtNewSlideShow")]
        public bool IsShowCanvasAtNewSlideShow { get; set; } = false;
        [JsonProperty("isNoClearStrokeOnSelectWhenInPowerPoint")]
        public bool IsNoClearStrokeOnSelectWhenInPowerPoint { get; set; } = true;
        /// <summary>
        /// LTSC D-06：PPT 模式下点鼠标（退出批注）时是否保留笔迹。
        /// 原默认 false——老师写完一转向鼠标，整页笔迹直接消失，实际用起来是缺陷。
        /// 改为 true，并在「PPT 联动」设置页暴露开关。
        /// </summary>
        [JsonProperty("isShowStrokeOnSelectInPowerPoint")]
        public bool IsShowStrokeOnSelectInPowerPoint { get; set; } = true;
        [JsonProperty("isAutoSaveStrokesInPowerPoint")]
        public bool IsAutoSaveStrokesInPowerPoint { get; set; } = true;
        [JsonProperty("isNotifyPreviousPage")]
        public bool IsNotifyPreviousPage { get; set; }
        [JsonProperty("isNotifyHiddenPage")]
        public bool IsNotifyHiddenPage { get; set; } = true;
        [JsonProperty("isNotifyAutoPlayPresentation")]
        public bool IsNotifyAutoPlayPresentation { get; set; } = true;
        [JsonProperty("isEnableTwoFingerGestureInPresentationMode")]
        public bool IsEnableTwoFingerGestureInPresentationMode { get; set; }
        [JsonProperty("isEnableFingerGestureSlideShowControl")]
        public bool IsEnableFingerGestureSlideShowControl { get; set; } = true;
        [JsonProperty("isSupportWPS")]
        public bool IsSupportWPS { get; set; }
        [JsonProperty("enableWppProcessKill")]
        public bool EnableWppProcessKill { get; set; } = true;
        [JsonProperty("isAlwaysGoToFirstPageOnReenter")]
        public bool IsAlwaysGoToFirstPageOnReenter { get; set; }
        [JsonProperty("enablePowerPointEnhancement")]
        public bool EnablePowerPointEnhancement { get; set; } = false;
        [JsonProperty("skipAnimationsWhenGoNext")]
        public bool SkipAnimationsWhenGoNext { get; set; } = false;
        [JsonProperty("pptLinkMode")]
        public PPTLinkMode PPTLinkMode { get; set; } = PPTLinkMode.Com;
        [JsonProperty("showPPTSidebarByDefault")]
        public bool ShowPPTSidebarByDefault { get; set; } = false;
        [JsonProperty("showPPTModePrompt")]
        public bool ShowPPTModePrompt { get; set; } = false;
    }

    public class Automation
    {
        [JsonIgnore]
        public bool IsEnableAutoFold =>
            IsAutoFoldInEasiNote
            || IsAutoFoldInEasiCamera
            || IsAutoFoldInEasiNote3C
            || IsAutoFoldInEasiNote5C
            || IsAutoFoldInSeewoPincoTeacher
            || IsAutoFoldInHiteTouchPro
            || IsAutoFoldInHiteCamera
            || IsAutoFoldInWxBoardMain
            || IsAutoFoldInOldZyBoard
            || IsAutoFoldInPPTSlideShow
            || IsAutoFoldInMSWhiteboard
            || IsAutoFoldInAdmoxWhiteboard
            || IsAutoFoldInAdmoxBooth
            || IsAutoFoldInQPoint
            || IsAutoFoldInYiYunVisualPresenter
            || IsAutoFoldInMaxHubWhiteboard;

        [JsonProperty("isAutoEnterAnnotationModeWhenExitFoldMode")]
        public bool IsAutoEnterAnnotationModeWhenExitFoldMode { get; set; }

        [JsonProperty("isAutoFoldWhenExitWhiteboard")]
        public bool IsAutoFoldWhenExitWhiteboard { get; set; }

        [JsonProperty("isAutoFoldInEasiNote")]
        public bool IsAutoFoldInEasiNote { get; set; }


        [JsonProperty("isAutoFoldInEasiCamera")]
        public bool IsAutoFoldInEasiCamera { get; set; }

        [JsonProperty("isAutoFoldInEasiNote3")]
        public bool IsAutoFoldInEasiNote3 { get; set; }

        [JsonProperty("isAutoFoldInEasiNote3C")]
        public bool IsAutoFoldInEasiNote3C { get; set; }

        [JsonProperty("isAutoFoldInEasiNote5C")]
        public bool IsAutoFoldInEasiNote5C { get; set; }

        [JsonProperty("isAutoFoldInSeewoPincoTeacher")]
        public bool IsAutoFoldInSeewoPincoTeacher { get; set; }

        [JsonProperty("isAutoFoldInHiteTouchPro")]
        public bool IsAutoFoldInHiteTouchPro { get; set; }
        [JsonProperty("isAutoFoldInHiteLightBoard")]
        public bool IsAutoFoldInHiteLightBoard { get; set; }

        [JsonProperty("isAutoFoldInHiteCamera")]
        public bool IsAutoFoldInHiteCamera { get; set; }

        [JsonProperty("isAutoFoldInWxBoardMain")]
        public bool IsAutoFoldInWxBoardMain { get; set; }
        /*
        */
        [JsonProperty("isAutoFoldInOldZyBoard")]
        public bool IsAutoFoldInOldZyBoard { get; set; }

        [JsonProperty("isAutoFoldInMSWhiteboard")]
        public bool IsAutoFoldInMSWhiteboard { get; set; }

        [JsonProperty("isAutoFoldInAdmoxWhiteboard")]
        public bool IsAutoFoldInAdmoxWhiteboard { get; set; }

        [JsonProperty("isAutoFoldInAdmoxBooth")]
        public bool IsAutoFoldInAdmoxBooth { get; set; }

        [JsonProperty("isAutoFoldInQPoint")]
        public bool IsAutoFoldInQPoint { get; set; }

        [JsonProperty("isAutoFoldInYiYunVisualPresenter")]
        public bool IsAutoFoldInYiYunVisualPresenter { get; set; }

        [JsonProperty("isAutoFoldInMaxHubWhiteboard")]
        public bool IsAutoFoldInMaxHubWhiteboard { get; set; }

        [JsonProperty("isAutoFoldInPPTSlideShow")]
        public bool IsAutoFoldInPPTSlideShow { get; set; }

        [JsonProperty("isAutoFoldAfterPPTSlideShow")]
        public bool IsAutoFoldAfterPPTSlideShow { get; set; }

        [JsonProperty("isAutoKillPPTService")]
        public bool IsAutoKillPPTService { get; set; }

        [JsonProperty("isAutoKillEasiNote")]
        public bool IsAutoKillEasiNote { get; set; }

        [JsonProperty("isAutoKillHiteAnnotation")]
        public bool IsAutoKillHiteAnnotation { get; set; }

        [JsonProperty("isAutoKillVComYouJiao")]
        public bool IsAutoKillVComYouJiao { get; set; }

        [JsonProperty("isAutoKillSeewoLauncher2DesktopAnnotation")]
        public bool IsAutoKillSeewoLauncher2DesktopAnnotation { get; set; }

        [JsonProperty("isAutoKillInkCanvas")]
        public bool IsAutoKillInkCanvas { get; set; }

        [JsonProperty("isAutoKillICA")]
        public bool IsAutoKillICA { get; set; }

        [JsonProperty("isAutoKillIDT")]
        public bool IsAutoKillIDT { get; set; }

        [JsonProperty("isSaveScreenshotsInDateFolders")]
        public bool IsSaveScreenshotsInDateFolders { get; set; }

        [JsonProperty("isAutoSaveStrokesAtScreenshot")]
        public bool IsAutoSaveStrokesAtScreenshot { get; set; }

        [JsonProperty("isAutoSaveStrokesAtClear")]
        public bool IsAutoSaveScreenshotAtClear { get; set; }

        [JsonProperty("isEnablePhotoCorrection")]
        public bool IsEnablePhotoCorrection { get; set; } = false;

        [JsonProperty("isAutoClearWhenExitingWritingMode")]
        public bool IsAutoClearWhenExitingWritingMode { get; set; }

        [JsonProperty("minimumAutomationStrokeNumber")]
        public int MinimumAutomationStrokeNumber { get; set; }

        [JsonProperty("autoSavedStrokesLocation")]
        public string AutoSavedStrokesLocation = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Saves");

        [JsonProperty("autoDelSavedFiles")]
        public bool AutoDelSavedFiles;

        [JsonProperty("autoDelSavedFilesDaysThreshold")]
        public int AutoDelSavedFilesDaysThreshold = 15;

        [JsonProperty("keepFoldAfterSoftwareExit")]
        public bool KeepFoldAfterSoftwareExit { get; set; } = false;

        [JsonProperty("isSaveFullPageStrokes")]
        public bool IsSaveFullPageStrokes;

        [JsonProperty("isUseCustomSaveFileName")]
        public bool IsUseCustomSaveFileName { get; set; } = false;

        [JsonProperty("customSaveFileNameTemplate")]
        public string CustomSaveFileNameTemplate { get; set; } = "{datetime}";

        [JsonProperty("isSaveStrokesAsXML")]
        public bool IsSaveStrokesAsXML { get; set; } = false;

        [JsonProperty("isAutoEnterAnnotationAfterKillHite")]
        public bool IsAutoEnterAnnotationAfterKillHite { get; set; }

        [JsonProperty("isEnableAutoSaveStrokes")]
        public bool IsEnableAutoSaveStrokes { get; set; } = true;

        [JsonProperty("autoSaveStrokesIntervalMinutes")]
        public int AutoSaveStrokesIntervalMinutes { get; set; } = 5;

        [JsonProperty("thoroughlyHideWhenFolded")]
        public bool ThoroughlyHideWhenFolded { get; set; } = false;

        [JsonProperty("floatingWindowInterceptor")]
        public FloatingWindowInterceptorSettings FloatingWindowInterceptor { get; set; } = new FloatingWindowInterceptorSettings();
    }

    public class FloatingWindowInterceptorSettings
    {
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = false;

        [JsonProperty("scanIntervalMs")]
        public int ScanIntervalMs { get; set; } = 5000;

        [JsonProperty("autoStart")]
        public bool AutoStart { get; set; } = false;

        [JsonProperty("showNotifications")]
        public bool ShowNotifications { get; set; } = true;

        [JsonProperty("interceptRules")]
        public Dictionary<string, bool> InterceptRules { get; set; } = new Dictionary<string, bool>
        {
            { "SeewoWhiteboard3Floating", false },
            { "SeewoWhiteboard5Floating", false },
            { "SeewoWhiteboard5CFloating", false },
            { "SeewoPincoSideBarFloating", false },
            { "SeewoPincoDrawingFloating", false },
            { "SeewoPincoBoardService", false },
            { "SeewoPPTFloating", false },
            { "AiClassFloating", false },
            { "HiteAnnotationFloating", false },
            { "ChangYanFloating", false },
            { "ChangYanBrushSettings", false },
            { "ChangYanSwipeClear", false },
            { "ChangYanInteraction", false },
            { "ChangYanSubjectApp", false },
            { "ChangYanControl", false },
            { "ChangYanCommonTools", false },
            { "ChangYanSceneToolbar", false },
            { "ChangYanDrawWindow", false },
            { "ChangYanPPTFloating", false },
            { "ChangYanPPTPageControl", false },
            { "ChangYanPPTGoBack", false },
            { "ChangYanPPTPreview", false },
            { "IntelligentClassFloating", false },
            { "IntelligentClassPPTFloating", false },
            { "SeewoDesktopAnnotationFloating", false },
            { "SeewoDesktopSideBarFloating", false }
        };
    }

    public class Advanced
    {
        [JsonProperty("isSpecialScreen")]
        public bool IsSpecialScreen { get; set; }

        /// <summary>
        /// LTSC D-09：以管理员身份启动时的处理方式。
        /// 0 = 弹窗提醒（默认）  1 = 自动以普通权限重启  2 = 不做处理
        ///
        /// 为什么需要：Windows 的 UIPI 会隔断不同完整性级别的进程。
        /// ICC 以管理员运行时，PowerPoint 若是普通权限，COM 连接会被拒绝
        /// （GetActiveObject → MK_E_UNAVAILABLE），表现为 PPT 联动整体失效
        /// （翻页组件消失、页码读取失败、退出无响应）。
        /// </summary>
        [JsonProperty("adminLaunchAction")]
        public int AdminLaunchAction { get; set; } = 0;
        [JsonProperty("isQuadIR")]
        public bool IsQuadIR { get; set; }

        [JsonProperty("touchMultiplier")]
        public double TouchMultiplier { get; set; } = 0.25;

        [JsonProperty("nibModeBoundsWidth")]
        public int NibModeBoundsWidth { get; set; } = 10;

        [JsonProperty("fingerModeBoundsWidth")]
        public int FingerModeBoundsWidth { get; set; } = 30;

        [JsonProperty("nibModeBoundsWidthThresholdValue")]
        public double NibModeBoundsWidthThresholdValue { get; set; } = 2.5;

        [JsonProperty("fingerModeBoundsWidthThresholdValue")]
        public double FingerModeBoundsWidthThresholdValue { get; set; } = 2.5;

        [JsonProperty("nibModeBoundsWidthEraserSize")]
        public double NibModeBoundsWidthEraserSize { get; set; } = 0.8;

        [JsonProperty("fingerModeBoundsWidthEraserSize")]
        public double FingerModeBoundsWidthEraserSize { get; set; } = 0.8;

        [JsonProperty("eraserBindTouchMultiplier")]
        public bool EraserBindTouchMultiplier { get; set; }

        [JsonProperty("isLogEnabled")]
        public bool IsLogEnabled { get; set; } = true;

        [JsonProperty("isSaveLogByDate")]
        public bool IsSaveLogByDate { get; set; } = true;

        /// <summary>
        /// PPT 联动详细诊断日志开关。默认关闭。
        /// 打开后 PptBridge 会记录连接、放映启停、翻页、COM 往返等每一步细节，
        /// 用于课堂上出问题时定位；平时关闭以免日志膨胀、也避免额外的 IO。
        /// </summary>
        [JsonProperty("isVerbosePptLogEnabled")]
        public bool IsVerbosePptLogEnabled { get; set; } = false;

        [JsonProperty("isDebugConsoleEnabled")]
        public bool IsDebugConsoleEnabled { get; set; } = false;

        [JsonProperty("isPPTComDebugProbeEnabled")]
        public bool IsPPTComDebugProbeEnabled { get; set; } = false;

        [JsonProperty("isPPTPageFlipPreviewVisible")]
        public bool IsPPTPageFlipPreviewVisible { get; set; } = false;

        [JsonProperty("isEnableFullScreenHelper")]
        public bool IsEnableFullScreenHelper { get; set; }

        [JsonProperty("isEnableEdgeGestureUtil")]
        public bool IsEnableEdgeGestureUtil { get; set; }


        [JsonProperty("isEnableForceFullScreen")]
        public bool IsEnableForceFullScreen { get; set; }

        [JsonProperty("isEnableResolutionChangeDetection")]
        public bool IsEnableResolutionChangeDetection { get; set; }

        [JsonProperty("isEnableDPIChangeDetection")]
        public bool IsEnableDPIChangeDetection { get; set; }

        [JsonProperty("isSecondConfirmWhenShutdownApp")]
        public bool IsSecondConfirmWhenShutdownApp { get; set; }

        [JsonProperty("isEnableAvoidFullScreenHelper")]
        public bool IsEnableAvoidFullScreenHelper { get; set; } = OSVersion.GetOperatingSystem() >= OSVersionExtension.OperatingSystem.Windows11;

        [JsonProperty("isAutoBackupBeforeUpdate")]
        public bool IsAutoBackupBeforeUpdate { get; set; } = true;

        [JsonProperty("isAutoBackupEnabled")]
        public bool IsAutoBackupEnabled { get; set; } = true;

        [JsonProperty("autoBackupIntervalDays")]
        public int AutoBackupIntervalDays { get; set; } = 7;

        [JsonProperty("lastAutoBackupTime")]
        public DateTime LastAutoBackupTime { get; set; } = DateTime.MinValue;

        [JsonProperty("isNoFocusMode")]
        public bool IsNoFocusMode { get; set; } = true;

        [JsonProperty("isAlwaysOnTop")]
        public bool IsAlwaysOnTop { get; set; } = true;

        [JsonProperty("enableUIAccessTopMost")]
        public bool EnableUIAccessTopMost { get; set; } = false;

        [JsonProperty("uiaMode")]
        public UIAMode UIAMode { get; set; } = UIAMode.UserToken;

        [JsonProperty("isEnableUriScheme")]
        public bool IsEnableUriScheme { get; set; } = false;

        [JsonProperty("windowMode")]
        public bool WindowMode { get; set; } = true;

        [JsonProperty("enableMultiScreenSupport")]
        public bool EnableMultiScreenSupport { get; set; } = true;

        [JsonProperty("followMouseForScreenSelection")]
        public bool FollowMouseForScreenSelection { get; set; } = true;
    }

    public class InkToShape
    {
        [JsonProperty("isInkToShapeEnabled")]
        public bool IsInkToShapeEnabled { get; set; } = true;
        [JsonProperty("isInkToShapeNoFakePressureRectangle")]
        public bool IsInkToShapeNoFakePressureRectangle { get; set; }
        [JsonProperty("isInkToShapeNoFakePressureTriangle")]
        public bool IsInkToShapeNoFakePressureTriangle { get; set; }
        [JsonProperty("isInkToShapeTriangle")]
        public bool IsInkToShapeTriangle { get; set; } = true;
        [JsonProperty("isInkToShapeRectangle")]
        public bool IsInkToShapeRectangle { get; set; } = true;
        [JsonProperty("isInkToShapeRounded")]
        public bool IsInkToShapeRounded { get; set; } = true;
        [JsonProperty("lineStraightenSensitivity")]
        public double LineStraightenSensitivity { get; set; } = 0.20;
        [JsonProperty("lineNormalizationThreshold")]
        public double LineNormalizationThreshold { get; set; } = 0.5;
    }

    public class RandSettings
    {
        [JsonProperty("showRandomAndSingleDraw")]
        public bool ShowRandomAndSingleDraw { get; set; } = true;
        // useLegacyTimerUI / useNewStyleUI 已删除：
        // 旧版计时窗口（CountdownTimerWindow）整个删掉了，只留 NewStyleTimerWindow。
        // 旧配置里残留的这两个键会被 Newtonsoft 忽略，不影响加载。
        [JsonProperty("timerVolume")]
        public double TimerVolume { get; set; } = 1.0;
        [JsonProperty("customTimerSoundPath")]
        public string CustomTimerSoundPath { get; set; } = "";
        [JsonProperty("enableOvertimeCountUp")]
        public bool EnableOvertimeCountUp { get; set; } = false;
        [JsonProperty("enableOvertimeRedText")]
        public bool EnableOvertimeRedText { get; set; } = false;
        [JsonProperty("enableProgressiveReminder")]
        public bool EnableProgressiveReminder { get; set; } = false;
        [JsonProperty("progressiveReminderVolume")]
        public double ProgressiveReminderVolume { get; set; } = 1.0;
        [JsonProperty("progressiveReminderSoundPath")]
        public string ProgressiveReminderSoundPath { get; set; } = "";
    }

    public class CustomPickNameBackground
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("filePath")]
        public string FilePath { get; set; }

        public CustomPickNameBackground(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
        }

        // 用于JSON序列化
        public CustomPickNameBackground() { }
    }

    public class CustomFloatingBarIcon
    {
        [JsonProperty("name")]
        public string Name { get; set; }

        [JsonProperty("filePath")]
        public string FilePath { get; set; }

        public CustomFloatingBarIcon(string name, string filePath)
        {
            Name = name;
            FilePath = filePath;
        }

        // 用于JSON序列化
        public CustomFloatingBarIcon() { }
    }

    public class ModeSettings
    {
        [JsonProperty("isPPTOnlyMode")]
        public bool IsPPTOnlyMode { get; set; } = false; // 是否为仅PPT模式，默认为false（正常模式）
    }

    public class CameraSettings
    {
        [JsonProperty("rotationAngle")]
        public int RotationAngle { get; set; } = 0;

        [JsonProperty("resolutionWidth")]
        public int ResolutionWidth { get; set; } = 1920;

        [JsonProperty("resolutionHeight")]
        public int ResolutionHeight { get; set; } = 1080;
    }

    public class DlassSettings
    {
        [JsonProperty("userToken")]
        public string UserToken { get; set; } = string.Empty;
    }

    public class MiniWhiteboardSettings
    {
        [JsonProperty("isEnabled")]
        public bool IsEnabled { get; set; } = true;

        [JsonProperty("defaultWidth")]
        public double DefaultWidth { get; set; } = 400;

        [JsonProperty("defaultHeight")]
        public double DefaultHeight { get; set; } = 300;

        [JsonProperty("defaultOpacity")]
        public double DefaultOpacity { get; set; } = 0.95;

        [JsonProperty("syncWithPPTPages")]
        public bool SyncWithPPTPages { get; set; } = true;

        [JsonProperty("penWidth")]
        public double PenWidth { get; set; } = 3;

        [JsonProperty("penColor")]
        public string PenColor { get; set; } = "#FFFFFFFF";

        [JsonProperty("currentColorIndex")]
        public int CurrentColorIndex { get; set; } = 0; // 0=White, 1=Black, 2=Red, 3=Orange, 4=Yellow, 5=Green, 6=Blue, 7=Purple
    }
}
