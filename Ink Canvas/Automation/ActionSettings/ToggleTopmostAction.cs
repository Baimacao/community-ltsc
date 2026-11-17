using Ink_Canvas.Helpers;
using Ink_Canvas.WorkflowAutomation.Models;
using System.Windows;

namespace Ink_Canvas.WorkflowAutomation.ActionSettings
{
    /// <summary>
    /// 切换窗口置顶行动的设置
    /// </summary>
    public class ToggleTopmostActionSettings
    {
        /// <summary>
        /// true = 置顶，false = 取消置顶
        /// </summary>
        public bool Topmost { get; set; } = true;
    }
}
