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
        public AdminLaunchWindow()
        {
            InitializeComponent();

            // 这个窗口不在启动时扫快捷方式。
            //
            // 它本身是启动流程里弹的，再往里塞一遍桌面 + 开始菜单全量扫描，
            // 就是让「启动慢」这件事雪上加霜，而且大部分机器上根本扫不出东西
            // （manifest 是 asInvoker，多数人的快捷方式没勾管理员）。
            //
            // 扫描和清理挪到 设置 → 高级 → 「检查快捷方式的管理员标记」，由用户主动点。
            // 这个窗口只负责提醒 + 记住选择。
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
