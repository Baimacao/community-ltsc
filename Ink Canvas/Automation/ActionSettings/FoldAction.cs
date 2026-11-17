using Ink_Canvas.WorkflowAutomation.Models;
using System.Windows;

namespace Ink_Canvas.WorkflowAutomation.ActionSettings
{
    /// <summary>
    /// 折叠/展开浮动栏行动的设置
    /// </summary>
    public class FoldActionSettings
    {
        /// <summary>
        /// true = 折叠，false = 展开
        /// </summary>
        public bool Fold { get; set; } = true;
    }

    /// <summary>
    /// 折叠/展开浮动栏的行动注册。
    /// </summary>
    public static class FoldAction
    {
        public const string ActionId = "inkcanvas.fold";
    }
}
