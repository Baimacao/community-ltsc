using Ink_Canvas.WorkflowAutomation.Models;

namespace Ink_Canvas.WorkflowAutomation.ActionSettings
{
    /// <summary>
    /// 保存笔画行动的设置
    /// </summary>
    public class SaveStrokesActionSettings
    {
        /// <summary>
        /// 保存路径（空则使用默认路径）
        /// </summary>
        public string SavePath { get; set; } = "";

        /// <summary>
        /// 是否保存为XML格式
        /// </summary>
        public bool SaveAsXml { get; set; } = false;
    }
}
