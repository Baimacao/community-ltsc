using Ink_Canvas.WorkflowAutomation.Abstractions;
using Ink_Canvas.WorkflowAutomation.Models;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using ActionModel = Ink_Canvas.WorkflowAutomation.Models.Action;

namespace Ink_Canvas.WorkflowAutomation.Services
{
    /// <summary>
    /// 行动服务，负责执行和恢复行动。
    /// 对齐 ClassIsland 的 ActionService，实现 IActionService 接口。
    /// </summary>
    public class ActionService : IActionService
    {
        /// <summary>
        /// 触发行动组
        /// </summary>
        public void Invoke(ActionSet actionSet)
        {
            if (!actionSet.IsEnabled)
            {
                Helpers.LogHelper.WriteLogToFile(
                    $"[自动化] 触发被跳过：行动组未启用（{actionSet.Name}）",
                    Helpers.LogHelper.LogType.Warning);
                return;
            }

            // 先清除所有行动的错误状态
            foreach (var action in actionSet.Actions)
                action.Exception = null;

            Helpers.LogHelper.WriteLogToFile(
                $"[自动化] 触发 {actionSet.Name}，动作数 {actionSet.Actions.Count}",
                Helpers.LogHelper.LogType.Event);

            // IsOn 记录的是「这组动作被执行过」，和「要不要自动恢复」是两件事。
            //
            // 原实现只在 IsRevertEnabled 为真时才置 IsOn，导致关闭自动恢复后：
            //   · 手动点「恢复」永远无效（Revert 第一行 if (!IsOn) return）
            //   · 而且界面不会提示任何原因，点了就是没反应
            // 老师关掉自动恢复只是不想让它自己弹回去，不是不想手动恢复。
            actionSet.IsOn = true;

            // 对齐 ClassIsland：异步执行行动，避免阻塞 UI 线程
            Task.Run(() =>
            {
                foreach (var action in actionSet.Actions)
                {
                    InvokeAction(action);
                }
            });
        }

        /// <summary>
        /// 恢复行动组
        /// </summary>
        public void Revert(ActionSet actionSet)
        {
            if (!actionSet.IsOn)
            {
                Helpers.LogHelper.WriteLogToFile(
                    $"[自动化] 恢复被跳过：IsOn=false（{actionSet.Name}）",
                    Helpers.LogHelper.LogType.Warning);
                return;
            }

            // 先清除所有行动的错误状态
            foreach (var action in actionSet.Actions)
                action.Exception = null;

            Helpers.LogHelper.WriteLogToFile(
                $"[自动化] 恢复 {actionSet.Name}，动作数 {actionSet.Actions.Count}",
                Helpers.LogHelper.LogType.Event);

            actionSet.IsOn = false;

            // 对齐 ClassIsland：异步执行恢复，避免阻塞 UI 线程
            Task.Run(() =>
            {
                foreach (var action in actionSet.Actions)
                {
                    RevertAction(action);
                }
            });
        }

        /// <summary>
        /// 注册行动处理程序。
        /// 对齐 ClassIsland 的 RegisterActionHandler。
        /// </summary>
        public void RegisterActionHandler(string id, ActionRegistryInfo.HandleDelegate handler)
        {
            if (!IActionService.Actions.TryGetValue(id, out var actionRegistryInfo))
                throw new KeyNotFoundException($"找不到行动 {id}。");

            actionRegistryInfo.Handle += handler;
        }

        /// <summary>
        /// 注册行动恢复处理程序。
        /// 对齐 ClassIsland 的 RegisterRevertHandler。
        /// </summary>
        public void RegisterRevertHandler(string id, ActionRegistryInfo.HandleDelegate handler)
        {
            if (!IActionService.Actions.TryGetValue(id, out var actionRegistryInfo))
                throw new KeyNotFoundException($"找不到行动 {id}。");

            actionRegistryInfo.RevertHandle += handler;
        }

        /// <summary>
        /// 执行单个行动
        /// </summary>
        private void InvokeAction(ActionModel action)
        {
            if (!IActionService.Actions.TryGetValue(action.Id, out var info)) return;

            // 对齐 ClassIsland：反序列化 settings
            object settings = null;
            var settingsType = info.SettingsType;
            if (settingsType != null)
            {
                settings = action.Settings ?? Activator.CreateInstance(settingsType);
                if (settings is JToken jToken)
                {
                    try
                    {
                        settings = jToken.ToObject(settingsType);
                    }
                    catch
                    {
                        settings = Activator.CreateInstance(settingsType);
                    }
                }
            }

            action.IsWorking = true;
            action.Exception = null;
            try
            {
                info.Handle?.Invoke(settings, action.Id);
            }
            catch (Exception ex)
            {
                action.Exception = ex;
                // LTSC（2026-11-17）：原来只把异常塞进 action.Exception，而全项目
                // 没有任何 XAML 绑定这个属性——等于自动化行动失败永远不可见。
                // 用户配了工作流，点了没反应，也不知道为什么。现在至少落日志。
                Helpers.LogHelper.WriteLogToFile(
                    $"自动化行动执行失败 [{action.Id}]：{ex}", Helpers.LogHelper.LogType.Error);
            }
            finally
            {
                action.IsWorking = false;
            }
        }

        /// <summary>
        /// 恢复单个行动
        /// </summary>
        private void RevertAction(ActionModel action)
        {
            if (action.Id == string.Empty) return;
            if (!IActionService.Actions.TryGetValue(action.Id, out var info)) return;
            if (info.RevertHandle == null) return;

            // 对齐 ClassIsland：反序列化 settings
            object settings = null;
            var settingsType = info.SettingsType;
            if (settingsType != null)
            {
                settings = action.Settings ?? Activator.CreateInstance(settingsType);
                if (settings is JToken jToken)
                {
                    try
                    {
                        settings = jToken.ToObject(settingsType);
                    }
                    catch
                    {
                        settings = Activator.CreateInstance(settingsType);
                    }
                }
            }

            action.IsWorking = true;
            action.Exception = null;
            try
            {
                info.RevertHandle.Invoke(settings, action.Id);
            }
            catch (Exception ex)
            {
                action.Exception = ex;
                Helpers.LogHelper.WriteLogToFile(
                    $"自动化行动恢复失败 [{action.Id}]：{ex}", Helpers.LogHelper.LogType.Error);
            }
            finally
            {
                action.IsWorking = false;
            }
        }

        /// <summary>
        /// 行动是否有内建的恢复
        /// </summary>
        public bool ExistRevertHandler(ActionModel action)
        {
            if (!IActionService.Actions.TryGetValue(action.Id, out var info)) return false;
            return info.RevertHandle != null;
        }
    }
}
