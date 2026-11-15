using Ink_Canvas.WorkflowAutomation.Abstractions;

namespace Ink_Canvas.WorkflowAutomation.Triggers
{
    /// <summary>
    /// 进程检测触发器的设置
    /// </summary>
    public class ProcessDetectedSettings
    {
        /// <summary>
        /// 要检测的进程名称（不含 .exe，不含路径）。
        /// 这是真正拿去比对进程列表的值。
        /// </summary>
        public string ProcessName { get; set; } = "";

        /// <summary>
        /// 给人看的显示名（例如 "Photoshop" / "PowerPoint"）。
        /// 只用于界面展示，不参与匹配。手动输入时留空即可。
        /// </summary>
        public string DisplayName { get; set; } = "";
    }

    /// <summary>
    /// 当指定进程启动时触发的触发器。
    /// 通过 SystemEventMonitor 的进程监控驱动，无需独立轮询。
    /// </summary>
    // 挂上自定义设置界面：从运行中的程序里挑，而不是手打进程名。
    [TriggerInfo("inkcanvas.processdetected", "进程检测", "ApplicationOutline",
        SettingsControlType = typeof(Ink_Canvas.WorkflowAutomation.SettingsControls.ProcessDetectedSettingsControl))]
    public class ProcessDetectedTrigger : TriggerBase<ProcessDetectedSettings>
    {
        private bool _wasRunning = false;

        public override void Loaded()
        {
            var monitor = AutomationBootstrap.Monitor;
            if (monitor == null) return;

            // 注册进程监控
            if (!string.IsNullOrEmpty(Settings.ProcessName))
            {
                monitor.RegisterProcess(Settings.ProcessName);
            }

            _wasRunning = monitor.IsProcessRunning(Settings.ProcessName);
            monitor.ProcessChanged += OnProcessChanged;
        }

        public override void UnLoaded()
        {
            var monitor = AutomationBootstrap.Monitor;
            if (monitor == null) return;

            monitor.ProcessChanged -= OnProcessChanged;

            // 取消注册进程监控
            if (!string.IsNullOrEmpty(Settings.ProcessName))
            {
                monitor.UnregisterProcess(Settings.ProcessName);
            }
        }

        private void OnProcessChanged(object sender, System.EventArgs e)
        {
            var monitor = AutomationBootstrap.Monitor;
            if (monitor == null) return;

            var isRunning = monitor.IsProcessRunning(Settings.ProcessName);

            if (isRunning && !_wasRunning)
            {
                // 进程刚启动
                Trigger();
            }
            else if (!isRunning && _wasRunning)
            {
                // 进程刚退出
                TriggerRevert();
            }

            _wasRunning = isRunning;
        }
    }
}
