using Ink_Canvas.WorkflowAutomation.Abstractions;
using Ink_Canvas.WorkflowAutomation.Actions;
using System;
using System.Diagnostics;

namespace Ink_Canvas.WorkflowAutomation.ActionHandlers
{
    public class KillProcessActionHandler
    {
        public KillProcessActionHandler(IActionService actionService)
        {
            actionService.RegisterActionHandler("inkcanvas.killprocess", (settings, guid) =>
            {
                var s = settings as KillProcessActionSettings;
                if (s == null || string.IsNullOrEmpty(s.ProcessName)) return;

                // LTSC（2026-11-17）：原来是两层空 catch。
                // 自动化配了「结束进程」，杀不掉也静默——工作流继续往下跑，
                // 用户以为进程已经结束了。现在每个失败都留痕。
                try
                {
                    var targets = Process.GetProcessesByName(s.ProcessName);
                    if (targets.Length == 0)
                    {
                        Helpers.LogHelper.WriteLogToFile(
                            $"自动化：结束进程时找不到进程 {s.ProcessName}，已跳过",
                            Helpers.LogHelper.LogType.Warning);
                        return;
                    }

                    foreach (var process in targets)
                    {
                        try
                        {
                            process.Kill();
                        }
                        catch (Exception killEx)
                        {
                            Helpers.LogHelper.WriteLogToFile(
                                $"自动化：结束进程 {s.ProcessName}(PID {process.Id}) 失败：{killEx.Message}",
                                Helpers.LogHelper.LogType.Error);
                        }
                        finally
                        {
                            process.Dispose();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Helpers.LogHelper.WriteLogToFile(
                        $"自动化：枚举进程 {s.ProcessName} 失败：{ex.Message}",
                        Helpers.LogHelper.LogType.Error);
                }
            });
        }
    }
}
