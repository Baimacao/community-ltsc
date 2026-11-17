using Ink_Canvas.WorkflowAutomation.Models;
using System.Windows;
using System.Windows.Controls;

namespace Ink_Canvas.WorkflowAutomation.Actions
{
    /// <summary>
    /// 切换批注模式行动的设置
    /// </summary>
    public class ToggleAnnotationModeActionSettings
    {
        /// <summary>
        /// true = 进入批注模式，false = 退出批注模式
        /// </summary>
        public bool EnterAnnotation { get; set; } = true;
    }

    /// <summary>
    /// 切换批注模式的行动注册。
    /// </summary>
    public static class ToggleAnnotationModeAction
    {
        public const string ActionId = "inkcanvas.toggleannotationmode";

    }
}
