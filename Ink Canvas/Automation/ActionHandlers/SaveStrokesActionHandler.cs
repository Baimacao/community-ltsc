using Ink_Canvas.WorkflowAutomation.Abstractions;
using System.Windows;

namespace Ink_Canvas.WorkflowAutomation.ActionHandlers
{
    public class SaveStrokesActionHandler
    {
        public SaveStrokesActionHandler(IActionService actionService)
        {
            actionService.RegisterActionHandler("inkcanvas.savestrokes", (settings, guid) =>
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    var mw = Application.Current.MainWindow as MainWindow;
                    if (mw == null) return;

                    // LTSC（2026-11-17）：这条行动原先的处理体是空的（只有一句 TODO），
                    // 用户在工作流里配了「保存笔画」，触发后什么都不发生也没有提示——静默失败。
                    // 现在真正接上保存逻辑；画布为空时明确记一条警告，不假装成功。
                    if (!mw.InvokeSaveForAutomation())
                    {
                        Helpers.LogHelper.WriteLogToFile(
                            "自动化：保存笔画被触发，但画布为空，已跳过", Helpers.LogHelper.LogType.Warning);
                    }
                });
            });
        }
    }
}
