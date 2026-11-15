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

        /// <summary>适配是否已开启（映射正常工作的状态）。</summary>
        internal static bool IsEnabled { get; private set; }

        /// <summary>当前是否处于「接管」状态（= 钩子挂着的状态）。</summary>
        internal static bool IsHooked => _hook != null && _hook.IsHooked;

        /// <summary>
        /// 根据当前是否在 PPT 联动模式，挂上或卸下钩子。
        /// 由主窗口在 PPT 状态变化时调用。
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

            if (IsEnabled)
            {
                // 已开启适配：按映射正常工作 —— 不拦截，让系统/PPT 收到
                RunFeatureAsync(binding.Feature);
                return false;
            }

            // 未开启：拦截，不响应
            if (binding.ShouldWarn && _warnedFeatures.Add(binding.Feature))
            {
                // 本次会话第一次按到这个键 —— 排回 UI 线程弹提示
                RequestWarn(binding);
            }

            return true;
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
        }

        /// <summary>关闭适配（回到「只拦截、不响应」）。</summary>
        internal static void DisableAdapter()
        {
            IsEnabled = false;
            LogHelper.WriteLogToFile("翻页笔适配已关闭", LogHelper.LogType.Event);
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