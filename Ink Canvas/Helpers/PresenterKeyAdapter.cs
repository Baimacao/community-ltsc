using System;
using System.Collections.Generic;
using System.Windows.Input;

namespace Ink_Canvas.Helpers
{
    /// <summary>
    /// 翻页笔适配。
    ///
    /// 背景：部分翻页笔的特殊键会触发快捷键（B 黑屏、ESC 退出放映、Shift+F5 开播），
    /// 老师并不知道自己按到了什么，课堂上就直接黑屏/退出了。
    ///
    /// 两种状态：
    ///   未开启适配 —— 只**拦截**这些键、不响应。按 B / ESC / Shift+F5 时弹一次提示，
    ///                  问「继续拦截」还是「我需要这个功能」。选后者则自动开启适配。
    ///   已开启适配 —— 按映射正常工作。
    ///
    /// 生效范围（ta 裁决 Q4-1/2/3/9）：**只在 PPT 联动模式下**接管。
    /// 其他模式（批注/白板）下这些键走原逻辑，一个都不碰。
    ///
    /// 「后续同一个键不再弹窗」是**本次会话**级，不写配置（ta 裁决 Q4-4）。
    /// </summary>
    internal static class PresenterKeyAdapter
    {
        /// <summary>按键与功能的配对。</summary>
        /// <summary>
        /// 一个按键与功能的配对。
        ///
        /// 这是**可编辑的配置项**：老师可以在设置页里改键（Q4-7）。
        /// `DefaultKey` / `DefaultModifiers` 记住出厂值，供「恢复默认」用。
        /// </summary>
        internal sealed class Binding
        {
            public Key Key { get; set; }
            public ModifierKeys Modifiers { get; set; }
            public Key DefaultKey { get; set; }
            public ModifierKeys DefaultModifiers { get; set; }
            public string Feature { get; set; } = "";
            public string DisplayName { get; set; } = "";
            public string Hint { get; set; } = "";
            public bool ShouldWarn { get; set; }

            /// <summary>给人看的按键文本，例如「Ctrl+P」「B」。</summary>
            public string KeyText
            {
                get
                {
                    if (Key == Key.None) return "（未设置）";
                    return Modifiers == ModifierKeys.None
                        ? Key.ToString()
                        : $"{Modifiers}+{Key}";
                }
            }

            /// <summary>恢复出厂配对。</summary>
            public void ResetToDefault()
            {
                Key = DefaultKey;
                Modifiers = DefaultModifiers;
            }
        }

        /// <summary>
        /// 当前的按键配对。设置页会直接改这个列表里的项。
        ///
        /// 默认值来自需求文档 R-04；老师可以在设置页里改键（Q4-7）。
        /// </summary>
        internal static readonly List<Binding> Bindings = new List<Binding>
        {
            new Binding
            {
                Key = Key.P, Modifiers = ModifierKeys.Control,
                DefaultKey = Key.P, DefaultModifiers = ModifierKeys.Control,
                Feature = "annotate", DisplayName = "打开批注",
                Hint = "进入批注模式，可以直接在白板上写", ShouldWarn = false
            },
            new Binding
            {
                Key = Key.E, Modifiers = ModifierKeys.None,
                DefaultKey = Key.E, DefaultModifiers = ModifierKeys.None,
                Feature = "clear", DisplayName = "清空笔迹",
                Hint = "清掉当前这一页的批注", ShouldWarn = false
            },
            new Binding
            {
                Key = Key.B, Modifiers = ModifierKeys.None,
                DefaultKey = Key.B, DefaultModifiers = ModifierKeys.None,
                Feature = "blackout", DisplayName = "黑屏",
                Hint = "把屏幕变黑，用来让学生注意力回到黑板", ShouldWarn = true
            },
            new Binding
            {
                Key = Key.F5, Modifiers = ModifierKeys.Shift,
                DefaultKey = Key.F5, DefaultModifiers = ModifierKeys.Shift,
                Feature = "slideshow", DisplayName = "开始/退出放映",
                Hint = "开始 PowerPoint 放映；放映中按会退出", ShouldWarn = true
            },
            new Binding
            {
                Key = Key.Escape, Modifiers = ModifierKeys.None,
                DefaultKey = Key.Escape, DefaultModifiers = ModifierKeys.None,
                Feature = "exit", DisplayName = "退出放映",
                Hint = "立刻结束 PowerPoint 放映", ShouldWarn = true
            },
        };

        private static LowLevelKeyboardHook _hook;

        /// <summary>本次会话里已经提示过的功能（ta 裁决 Q4-4：会话级，不写配置）。</summary>
        private static readonly HashSet<string> _warnedFeatures = new HashSet<string>();

        /// <summary>
        /// 适配是否已开启（映射正常工作的状态）。
        ///
        /// 这个值**落盘**（Settings.PresenterKey.Enabled）：老师改过就不该重启又变回去。
        /// 只有「本次会话不再弹提示」是会话级的（ta 裁决 Q4-4），两者是不同的东西。
        /// </summary>
        internal static bool IsEnabled { get; private set; }

        /// <summary>
        /// 从 Settings 恢复状态。程序启动时调一次。
        ///
        /// 出厂默认 = 关闭：老师第一次遇到这些键时，宁可只拦截 + 弹提示，
        /// 也不能让翻页笔一按就把课上状态改掉。
        /// </summary>
        internal static void LoadFromSettings()
        {
            try
            {
                var settings = MainWindow.Settings?.PresenterKey;
                if (settings == null) return;

                IsEnabled = settings.Enabled;

                // 配对：先用出厂值铺底，再用配置里存过的覆盖。
                // 只覆盖存在且合法的项 —— 老配置缺条目、或存了非法枚举值时，
                // 那一项保持出厂值，不会把按键变成「未设置」。
                if (settings.Bindings != null)
                {
                    foreach (var b in Bindings)
                    {
                        if (settings.Bindings.TryGetValue(b.Feature, out var saved) && saved != null)
                        {
                            var key = (Key)saved.Key;
                            var mods = (ModifierKeys)saved.Modifiers;
                            if (key != Key.None && IsValidModifiers(mods))
                            {
                                b.Key = key;
                                b.Modifiers = mods;
                            }
                        }
                    }
                }

                LogHelper.WriteLogToFile(
                    $"翻页笔适配状态已载入：{(IsEnabled ? "开启" : "关闭")}",
                    LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"载入翻页笔适配状态失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 把当前配对与开关写回 Settings 并落盘。
        ///
        /// 调用点：设置页改键/改开关、OOBE 选完、提示框里选「启用适配」。
        /// 只写用户实际改过的条目不现实（无法判断哪条算改过），
        /// 全量写也不亏 —— 5 条记录而已。
        /// </summary>
        internal static void PersistToSettings()
        {
            try
            {
                var root = MainWindow.Settings;
                if (root == null) return;

                if (root.PresenterKey == null)
                    root.PresenterKey = new PresenterKeySettings();

                root.PresenterKey.Enabled = IsEnabled;

                var map = new Dictionary<string, PresenterKeyBinding>();
                foreach (var b in Bindings)
                {
                    map[b.Feature] = new PresenterKeyBinding
                    {
                        Key = (int)b.Key,
                        Modifiers = (int)b.Modifiers
                    };
                }
                root.PresenterKey.Bindings = map;

                Ink_Canvas.Windows.SettingsViews.Helpers.SettingsManager.SaveSettingsToFile();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"保存翻页笔适配状态失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>只认 WPF 定义过的修饰键组合，避免配置文件被改坏后塞进奇怪的值。</summary>
        private static bool IsValidModifiers(ModifierKeys mods)
        {
            const ModifierKeys all = ModifierKeys.Alt | ModifierKeys.Control
                                   | ModifierKeys.Shift | ModifierKeys.Windows;
            return (mods & ~all) == ModifierKeys.None;
        }

        /// <summary>当前是否处于「接管」状态（= 钩子挂着的状态）。</summary>
        internal static bool IsHooked => _hook != null && _hook.IsHooked;

        /// <summary>
        /// 进入 PPT 联动时挂上拦截钩子，退出时卸下。由主窗口在 PPT 状态变化时调用。
        ///
        /// 拦截的本意是保护：翻页笔的特殊键会触发 B 黑屏 / ESC 退放映 /
        /// Shift+F5 重播，老师不知道自己按到了什么，课堂上直接黑屏或退出放映。
        /// 所以联动期间接管这些键是对的。
        ///
        /// 范围只限联动 —— 不在放映时这些键走原逻辑，一个都不碰。
        /// （班测反馈的「一直弹提示」就是范围跑出了联动，见 ShouldWarn 的调用处。）
        /// </summary>
        internal static void SyncWithPowerPointMode(bool isInPresentation)
        {
            if (isInPresentation && !IsHooked)
            {
                InstallHook();
            }
            else if (!isInPresentation && IsHooked)
            {
                UninstallHook();
            }
        }

        private static void InstallHook()
        {
            try
            {
                _hook = new LowLevelKeyboardHook
                {
                    ShouldSwallow = OnShouldSwallow
                };

                if (!_hook.Install())
                {
                    _hook.Dispose();
                    _hook = null;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"翻页笔适配挂载失败: {ex.Message}", LogHelper.LogType.Error);
                _hook = null;
            }
        }

        private static void UninstallHook()
        {
            try
            {
                _hook?.Dispose();
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"翻页笔适配卸载失败: {ex.Message}", LogHelper.LogType.Error);
            }
            finally
            {
                _hook = null;
            }
        }

        /// <summary>
        /// 钩子的判断逻辑。返回 true = 吃掉这个键。
        /// 运行在钩子线程上，**不能直接动 UI**。
        /// </summary>
        private static bool OnShouldSwallow(Key key, ModifierKeys modifiers)
        {
            var binding = FindBinding(key, modifiers);
            if (binding == null) return false;

            // LTSC（2026-11-19 班测）：拦截和弹窗都只能在 PPT 联动期间发生。
            //
            // 钩子由 SyncWithPowerPointMode 按联动状态挂/卸，正常情况下不该跑到这里。
            // 但班测反馈「提示在不在联动时也弹」—— 说明存在钩子没及时卸下的窗口：
            //   · SetPowerPointMode 第一句是 if (当前值 == 新值) return;
            //     状态位一旦不同步，这个方法就永远不再同步钩子
            //   · 折叠/展开、窗口重建等路径也可能让钩子状态和放映状态脱节
            //
            // 所以判据下沉到钩子回调本身：只要不在联动，一律放行，不吃键也不弹窗。
            // 这样即使钩子还挂着，行为也和无钩子一致，不会打扰老师。
            if (!IsInPptPresentationMode())
            {
                return false;
            }

            if (IsEnabled)
            {
                // 已开启适配：按映射正常工作 —— **吃掉这个键，同时执行映射**。
                //
                // 这里原来是 return false（放行给系统/PPT），是错的，实测能证明：
                //   · 这 5 个键在放映状态下 PPT 自己也认（ESC 退放映、B 黑屏、
                //     E 擦除、Ctrl+P 画笔、Shift+F5 从头播放）。放行 = ICC 和 PPT
                //     各做一遍，同一件事做两次。
                //   · ESC 更糟：它同时还是 ICC 的全局热键（GlobalHotkeyManager
                //     "Exit" = Escape）。放行时是 ICC 退一次 + PPT 退一次 +
                //     全局热键再退一次。
                //
                // 探针实测（E:\DSH\tmp\hookprobe，WH_KEYBOARD_LL + RegisterHotKey）：
                //   钩子不吞 → hookSwallowed=0 hotkeyFired=1
                //   钩子吞   → hookSwallowed=1 hotkeyFired=0
                // 也就是说：只要吃掉，系统就收不到 WM_HOTKEY，重复触发不可能发生。
                //
                // 代价是「吃掉后 PPT 也收不到」，但这是适配的本意 ——
                // 老师开适配就是为了让这些键只由 ICC 管，不要顺带把 PPT 也动了。
                RunFeatureAsync(binding.Feature);
                return true;
            }

            // 未开启：拦截，不响应
            if (binding.ShouldWarn && _warnedFeatures.Add(binding.Feature))
            {
                // 本次会话第一次按到这个键 —— 排回 UI 线程弹提示
                RequestWarn(binding);
            }

            return true;
        }

        /// <summary>
        /// 当前是否处于 PPT 联动（放映）状态。
        ///
        /// 钩子回调跑在钩子线程上，只能读这个布尔值，不能碰 UI。
        /// 拿不到主窗口时按「不在联动」处理 —— 宁可放行，也不要误拦。
        /// </summary>
        private static bool IsInPptPresentationMode()
        {
            try
            {
                var mw = System.Windows.Application.Current?.MainWindow as Ink_Canvas.MainWindow;
                return mw != null && mw.IsInPPTPresentationMode;
            }
            catch { return false; }
        }

        private static Binding FindBinding(Key key, ModifierKeys modifiers)
        {
            foreach (var b in Bindings)
            {
                if (b.Key == key && b.Modifiers == modifiers) return b;
            }
            return null;
        }

        /// <summary>需要在 UI 线程上执行的「警告 + 询问」回调，由主窗口设置。</summary>
        internal static Action<Binding> WarnRequested { get; set; }

        /// <summary>需要在 UI 线程上执行的「功能动作」，由主窗口设置。</summary>
        internal static Action<string> FeatureRequested { get; set; }

        private static void RequestWarn(Binding binding)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { WarnRequested?.Invoke(binding); }
                catch (Exception ex) { LogHelper.WriteLogToFile($"翻页笔提示失败: {ex.Message}", LogHelper.LogType.Error); }
            }));
        }

        private static void RunFeatureAsync(string feature)
        {
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null || dispatcher.HasShutdownStarted) return;
            dispatcher.BeginInvoke(new Action(() =>
            {
                try { FeatureRequested?.Invoke(feature); }
                catch (Exception ex) { LogHelper.WriteLogToFile($"翻页笔功能执行失败: {ex.Message}", LogHelper.LogType.Error); }
            }));
        }

        /// <summary>用户在提示框里选了「我需要这个功能」——开启适配。</summary>
        internal static void EnableAdapter()
        {
            IsEnabled = true;
            LogHelper.WriteLogToFile("翻页笔适配已开启", LogHelper.LogType.Event);
            // 开关变了要立刻同步钩子状态，否则要等下一次 PPT 状态变化才生效。
            SyncHookToCurrentPptState();
        }

        /// <summary>关闭适配（钩子会被卸掉，按键原样交给系统）。</summary>
        internal static void DisableAdapter()
        {
            IsEnabled = false;
            LogHelper.WriteLogToFile("翻页笔适配已关闭", LogHelper.LogType.Event);
            SyncHookToCurrentPptState();
        }

        /// <summary>
        /// 按当前 PPT 放映状态和适配开关，重新决定钩子该不该挂。
        /// 设置页里拨开关、或提示框里选了某个选项之后都要叫一次。
        /// </summary>
        internal static void SyncHookToCurrentPptState()
        {
            try
            {
                var mw = System.Windows.Application.Current?.MainWindow as Ink_Canvas.MainWindow;
                SyncWithPowerPointMode(mw != null && mw.IsInPPTPresentationMode);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"同步翻页笔钩子状态失败: {ex.Message}", LogHelper.LogType.Warning);
            }
        }
        /// <summary>用户在提示框里选了「继续拦截」——保持关闭，但不影响已经提示过的事实。</summary>
        internal static void KeepBlocking()
        {
            LogHelper.WriteLogToFile("翻页笔适配：用户选择继续拦截", LogHelper.LogType.Event);
        }

        /// <summary>程序退出时务必调用，保证钩子一定被卸掉。</summary>
        internal static void Shutdown()
        {
            UninstallHook();
            _warnedFeatures.Clear();
        }
    }
}