using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Ink_Canvas.Helpers;

namespace Ink_Canvas
{
    /// <summary>
    /// 翻页笔适配的界面侧接线。
    ///
    /// 拆成独立部分类，不改 MainWindow.xaml.cs ——
    /// 那个文件 3300 多行，每次往里插东西都容易把括号搞乱。
    ///
    /// 核心逻辑在 Helpers/PresenterKeyAdapter.cs（钩子、拦截判定、会话级提示去重），
    /// 这里只负责两件事：
    ///   1. 把「功能标识」映射到现有方法
    ///   2. 弹误触提示
    /// </summary>
    public partial class MainWindow
    {

        private bool _isInPPTPresentationMode;

        /// <summary>
        /// IsInPPTPresentationMode 的 setter 落点。
        ///
        /// 这里是翻页笔适配的**唯一同步点**：
        /// 进入放映 = 挂键盘钩子；退出放映 = 立刻卸掉。
        ///
        /// 为什么收口在属性 setter 而不是各个赋值点：
        /// 赋值点有三处（PPTUIManager 两个分支 + 设置页预览），
        /// 漏掉任何一处都会让钩子留在不该留的时候 ——
        /// 那会让老师的整个系统键盘被 ICC 拦着，是最严重的后果。
        /// </summary>
        private void SetPowerPointMode(bool value)
        {
            if (_isInPPTPresentationMode == value) return;
            _isInPPTPresentationMode = value;

            try
            {
                PresenterKeyAdapter.SyncWithPowerPointMode(value);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"翻页笔适配同步失败: {ex.Message}", LogHelper.LogType.Error);
            }

            try
            {
                // 退出放映热键只在放映期间注册，退出即归还系统。
                // 裸 ESC 的 RegisterHotKey 是系统级独占（实测：注册后前台窗口只收到
                // KeyUp、收不到 KeyDown），谁挂着谁就把整个系统的 ESC 吃掉。
                // 详见 ApplyExitHotkeyRegistration 的注释。
                GlobalHotkeyManagerInstance?.ApplyExitHotkeyRegistration(value);
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"退出放映热键状态同步失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }
        /// <summary>
        /// 接上翻页笔适配的两个回调。由 Window_Loaded 调用一次。
        /// </summary>
        private void InitializePresenterKeyAdapter()
        {
            PresenterKeyAdapter.FeatureRequested = OnPresenterFeatureRequested;
            PresenterKeyAdapter.WarnRequested = ShowPresenterKeyWarning;

            // 从 Settings 恢复开关与按键配对。
            // 必须在这里做：Window_Loaded 时 LoadSettings 已经跑完，
            // 再往后设置页可能已经打开过，那时读到的是出厂值就晚了。
            PresenterKeyAdapter.LoadFromSettings();
        }

        /// <summary>
        /// 翻页笔按住映射键时执行对应功能。
        ///
        /// 回调已经在 UI 线程上（Adapter 内部排回 Dispatcher），可以直接调界面方法。
        /// </summary>
        private void OnPresenterFeatureRequested(string feature)
        {
            try
            {
                switch (feature)
                {
                    case "annotate":
                        PenIcon_Click(null, null);
                        break;

                    case "clear":
                        SymbolIconDelete_MouseUp(null, null);
                        break;

                    case "blackout":
                        ImageBlackboard_MouseUp(null, null);
                        break;

                    case "exit":
                        ExitPPTSlideShow_MouseUp(null, null);
                        break;

                    case "slideshow":
                        TogglePPTSlideShow();
                        break;

                    default:
                        LogHelper.WriteLogToFile($"翻页笔：未知功能 {feature}", LogHelper.LogType.Warning);
                        break;
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"翻页笔功能执行失败（{feature}）: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>开始/退出 PowerPoint 放映。翻页笔的 Shift+F5 映射到这里。</summary>
        private void TogglePPTSlideShow()
        {
            try
            {
                if (IsInPPTPresentationMode)
                {
                    ExitPPTSlideShow_MouseUp(null, null);
                }
                else
                {
                    BtnPPTSlideShow_Click(null, null);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"切换放映状态失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }

        /// <summary>
        /// 翻页笔误触提示。
        ///
        /// 只在「未开启适配」且按下的是会直接影响上课的键时弹一次（ta 裁决 Q4-6）：
        /// B 黑屏 / ESC 退出放映 / Shift+F5 开播 —— 这三个误触代价大，当场就能毁一节课。
        /// Ctrl+P 和 E 不弹：误触可以当场撤回，不值得打断。
        ///
        /// 「本次会话不再弹」由 PresenterKeyAdapter 的 _warnedFeatures 保证（ta 裁决 Q4-4）：
        /// 关掉软件再打开会重新提示一次。
        /// </summary>
        private async void ShowPresenterKeyWarning(PresenterKeyAdapter.Binding binding)
        {
            try
            {
                var keyText = binding.Modifiers == ModifierKeys.None
                    ? binding.Key.ToString()
                    : $"{binding.Modifiers}+{binding.Key}";

                // 用 iNKORE ContentDialog，和项目其余对话框保持同一套视觉（Q4-5）。
                // 原来临时用的 MessageBox 样式不统一，而且在触摸屏上按钮偏小。
                var content = new System.Windows.Controls.StackPanel();

                content.Children.Add(new TextBlock
                {
                    Text = $"检测到你按下了 {keyText}（{binding.DisplayName}）。",
                    TextWrapping = TextWrapping.Wrap
                });

                content.Children.Add(new TextBlock
                {
                    Text = "这个键会直接影响放映状态，可能是误触翻页笔的特殊键，或者按压到了翻页键。",
                    TextWrapping = TextWrapping.Wrap,
                    Opacity = 0.85
                });

                content.Children.Add(new TextBlock
                {
                    Text = "「继续拦截」= 这个键保持不响应；「启用适配」= 让它按正常功能工作（之后可以在设置里改）。",
                    TextWrapping = TextWrapping.Wrap,
                    FontSize = 12
                    ,
                    Opacity = 0.7
                });

                var dialog = new iNKORE.UI.WPF.Modern.Controls.ContentDialog
                {
                    Title = "翻页笔按键提示",
                    Content = content,
                    PrimaryButtonText = "继续拦截",
                    SecondaryButtonText = "启用适配",
                    DefaultButton = iNKORE.UI.WPF.Modern.Controls.ContentDialogButton.Primary,
                    Owner = this
                };

                var result = await dialog.ShowAsync();

                if (result == iNKORE.UI.WPF.Modern.Controls.ContentDialogResult.Primary)
                {
                    PresenterKeyAdapter.KeepBlocking();
                }
                else
                {
                    PresenterKeyAdapter.EnableAdapter();
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"翻页笔提示显示失败: {ex.Message}", LogHelper.LogType.Error);
            }
        }
    }
}