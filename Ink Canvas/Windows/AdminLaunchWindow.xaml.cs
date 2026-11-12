using Ink_Canvas.Helpers;
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
        }

        private void BtnIgnore_Click(object sender, RoutedEventArgs e)
        {
            LogHelper.WriteLogToFile("用户选择忽略管理员权限提醒，继续使用", LogHelper.LogType.Event);
            Close();
        }

        private void BtnRestart_Click(object sender, RoutedEventArgs e)
        {
            LogHelper.WriteLogToFile("用户选择以普通权限重启", LogHelper.LogType.Event);
            Close();
            AppRestartHelper.RestartApp(false);
        }
    }
}
