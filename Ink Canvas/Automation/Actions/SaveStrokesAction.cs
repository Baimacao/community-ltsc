using Ink_Canvas.WorkflowAutomation.Models;

namespace Ink_Canvas.WorkflowAutomation.Actions
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

    /// <summary>
    /// 保存笔画的行动 ID 常量。
    ///
    /// LTSC（2026-11-17）：这里原来有个 `Register()` 静态方法，全项目零调用
    /// （真正在跑的是 ActionHandlers/SaveStrokesActionHandler.cs）。已删除。
    /// 现在这个类只剩 ID 常量，供 PresetToWorkflowConverter 等地方引用。
    /// </summary>
    public static class SaveStrokesAction
    {
        public const string ActionId = "inkcanvas.savestrokes";
    }
}
