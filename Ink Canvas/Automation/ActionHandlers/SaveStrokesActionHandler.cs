using Ink_Canvas.WorkflowAutomation.Abstractions;
using Ink_Canvas.WorkflowAutomation.ActionSettings;
using System.Windows;

namespace Ink_Canvas.WorkflowAutomation.ActionHandlers
{
    public class SaveStrokesActionHandler
    {
        public SaveStrokesActionHandler(IActionService actionService)
        {
            actionService.RegisterActionHandler("inkcanvas.savestrokes", (settings, guid) =>
            {
                // 把用户在 UI 上填的两个参数读出来。
                // LTSC（2026-11-17）：这两个字段之前无人读取——UI 生成了输入框，
                // 用户填了却毫无效果，也不报错。现在真正传下去。
                var s = settings as SaveStrokesActionSettings;
                string customPath = s?.SavePath;
                bool saveAsXml = s?.SaveAsXml ?? false;

                Application.Current.Dispatcher.Invoke(() =>
                {
                    var mw = Application.Current.MainWindow as MainWindow;
                    if (mw == null)
                    {
                        // LTSC（2026-11-17）：原来这里是静默 return。
                        // 同一个文件里画布为空的分支反而记日志，不一致。
                        Helpers.LogHelper.WriteLogToFile(
                            "自动化：保存笔画被触发，但主窗口不可用，已跳过",
                            Helpers.LogHelper.LogType.Warning);
                        return;
                    }

                    // LTSC（2026-11-17）：这条行动原先的处理体是空的（只有一句 TODO），
                    // 用户在工作流里配了「保存笔画」，触发后什么都不发生也没有提示——静默失败。
                    // 现在真正接上保存逻辑，并支持自定义路径 / XML 格式。
                    bool ok = (string.IsNullOrWhiteSpace(customPath) && !saveAsXml)
                        ? mw.InvokeSaveForAutomation()
                        : mw.InvokeSaveForAutomation(customPath, saveAsXml);

                    if (!ok)
                    {
                        Helpers.LogHelper.WriteLogToFile(
                            "自动化：保存笔画被触发，但画布为空或保存失败，已跳过",
                            Helpers.LogHelper.LogType.Warning);
                    }
                });
            });
        }
    }
}
