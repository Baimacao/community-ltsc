using Ink_Canvas.Helpers;
using Ink_Canvas.Windows.SettingsViews.Helpers;
using System;
using System.Windows;

namespace Ink_Canvas.Windows
{
    /// <summary>
    /// LTSC D-09：检测到以管理员权限启动时弹出的提醒窗口。
    ///
    /// 背景：Windows 的 UIPI 会隔断不同完整性级别的进程。ICC 以管理员运行时，
    /// 普通权限的 PowerPoint 无法建立 COM 连接（GetActiveObject → MK_E_UNAVAILABLE），
    /// 表现为 PPT 联动整体失效——翻页组件消失、页码读取失败、退出无响应。
    /// 这是权限问题，不是程序缺陷，所以给用户两条路：忽略，或降权重启。
    /// </summary>
    public partial class AdminLaunchWindow : Window
    {
        /// <summary>找到的那个「勾了以管理员运行」的快捷方式路径；没找到是 null。</summary>
        private string _adminShortcutPath;

        public AdminLaunchWindow()
        {
            InitializeComponent();

            // 启动时就把「是谁把我提权的」查出来。
            //
            // 这一步是本窗口最有价值的地方：老师只知道自己「双击了图标」，
            // 不知道快捷方式上被勾了兼容性选项。与其让他去翻右键属性，
            // 不如直接告诉他文件名，并且给一个按钮当场清掉。
            try
            {
                _adminShortcutPath = ShortcutAdminFlag.FindRunAsAdminShortcutForSelf();
                if (!string.IsNullOrEmpty(_adminShortcutPath))
                {
                    BtnFixShortcut.Visibility = Visibility.Visible;
                    TextBlockShortcutPath.Text = _adminShortcutPath;
                    TextBlockShortcutPath.Visibility = Visibility.Visible;
                    LogHelper.WriteLogToFile(
                        "找到勾了以管理员运行的本程序快捷方式：" + _adminShortcutPath,
                        LogHelper.LogType.Event);
                }
                else
                {
                    // 没找到不等于没有：快捷方式可能在别的位置，或者是别的提权途径
                    // （任务计划、其他程序的调用、兼容性注册表项）。
                    // 不能因为没找到就不提示 —— 把这一句写进日志便于回查。
                    LogHelper.WriteLogToFile(
                        "未找到勾了以管理员运行的本程序快捷方式（可能是别的提权途径）",
                        LogHelper.LogType.Trace);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"查找管理员快捷方式异常：{ex.Message}", LogHelper.LogType.Warning);
            }
        }

        /// <summary>
        /// 清掉快捷方式上的「以管理员身份运行」标记，然后提示重启。
        /// </summary>
        private void BtnFixShortcut_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrEmpty(_adminShortcutPath))
                {
                    MessageBox.Show(this,
                        "没有找到指向本程序、且勾了「以管理员身份运行」的快捷方式。\n\n" +
                        "请手动检查：右键你平时打开本程序用的那个图标 → 属性 → 兼容性 →\n" +
                        "取消勾选「以管理员身份运行」。",
                        "未找到快捷方式", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                bool ok = ShortcutAdminFlag.ClearRunAsAdmin(_adminShortcutPath);

                if (ok)
                {
                    MessageBox.Show(this,
                        "已清掉快捷方式上的「以管理员身份运行」标记：\n" + _adminShortcutPath + "\n\n" +
                        "下次从这个图标启动时就是普通权限了，PPT 联动会恢复正常。\n" +
                        "现在建议以普通权限重启一次。",
                        "修复成功", MessageBoxButton.OK, MessageBoxImage.Information);

                    BtnFixShortcut.Visibility = Visibility.Collapsed;
                    TextBlockShortcutPath.Visibility = Visibility.Collapsed;
                }
                else
                {
                    MessageBox.Show(this,
                        "没能修改这个快捷方式：\n" + _adminShortcutPath + "\n\n" +
                        "可能是文件被占用或没有写权限。请手动处理：\n" +
                        "右键该图标 → 属性 → 兼容性 → 取消勾选「以管理员身份运行」。",
                        "修复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                LogHelper.WriteLogToFile($"修复快捷方式异常：{ex}", LogHelper.LogType.Error);
                MessageBox.Show(this, "修复时出错：" + ex.Message,
                    "修复失败", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        /// <summary>
        /// 把「以后不再询问」写进设置。勾了才写，没勾就保持原样。
        /// </summary>
        /// <param name="action">0=以后都弹窗 1=以后都自动降权 2=以后都不处理</param>
        private void RememberIfChecked(int action)
        {
            if (CheckRemember?.IsChecked != true) return;

            try
            {
                SettingsManager.Settings.Advanced.AdminLaunchAction = action;
                SettingsManager.SaveSettingsToFile();
                LogHelper.WriteLogToFile(
                    $"管理员权限处理方式已记住：AdminLaunchAction={action}（下次启动直接生效）",
                    LogHelper.LogType.Event);
            }
            catch (Exception ex)
            {
                // 记不住不是致命错误，但必须可见——否则用户以为「记住」了，
                // 下次照样弹窗，还以为程序坏了。
                LogHelper.WriteLogToFile(
                    $"记住管理员权限处理方式失败（下次仍会询问）：{ex.Message}",
                    LogHelper.LogType.Error);
            }
        }

        private void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            LogHelper.WriteLogToFile("用户选择忽略管理员权限提醒，继续使用", LogHelper.LogType.Event);
            // 忽略 = 以后都不处理，不再打扰
            RememberIfChecked(2);
            Close();
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            LogHelper.WriteLogToFile("用户选择以普通权限重启", LogHelper.LogType.Event);
            // 降权 = 以后都自动降权，下次启动直接以普通权限起来，连窗口都不用弹
            RememberIfChecked(1);
            Close();
            AppRestartHelper.RestartApp(false);
        }
    }
}
