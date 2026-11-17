using Ink_Canvas.WorkflowAutomation.Abstractions;
using Ink_Canvas.WorkflowAutomation.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Ink_Canvas.WorkflowAutomation.Services
{
    /// <summary>
    /// 自动化注册中心，管理所有已注册的触发器、行动和规则。
    /// 对齐 ClassIsland：行动和规则分别存储在 IActionService.Actions 和 IRulesetService.Rules 中，
    /// 触发器存储在此处并通过 DI 容器解析实例。
    /// </summary>
    public static class AutomationRegistry
    {
        /// <summary>
        /// 已注册的行动字典（兼容 UI 引用）
        /// </summary>
        public static Dictionary<string, ActionRegistryInfo> RegisteredActions => IActionService.Actions;

        /// <summary>
        /// 已注册的规则字典（兼容 UI 引用）
        /// </summary>
        public static Dictionary<string, RuleRegistryInfo> RegisteredRules => IRulesetService.Rules;

        /// <summary>
        /// 已注册的触发器列表
        /// </summary>
        public static List<TriggerInfo> RegisteredTriggers { get; } = new();

        /// <summary>
        /// 注册触发器（仅元数据，实例通过 DI 容器解析）
        /// </summary>
        public static void RegisterTrigger(TriggerInfo info)
        {
            if (RegisteredTriggers.Any(x => x.Id == info.Id))
            {
                throw new InvalidOperationException($"已注册ID为 {info.Id} 的触发器。");
            }
            RegisteredTriggers.Add(info);
        }

        // LTSC（2026-11-17）：这里原来还有 RegisterAction / RegisterRule 两个方法，
        // 注释写着「已迁移到 IActionService.Actions，保留此方法作为兼容入口」。
        // 但全项目零调用，也没有第三方插件在用（插件系统在本仓外，没有对这两个方法的约定）。
        // 「兼容入口」是自我安慰的说法——没人用的兼容入口就是死代码，删了。
        /// <summary>
        /// 通过 DI 容器解析触发器实例。
        /// 对齐 ClassIsland 的 GetKeyedService&lt;TriggerBase&gt;(id)。
        /// </summary>
        public static TriggerBase ResolveTrigger(IServiceProvider serviceProvider, string id)
        {
            var info = RegisteredTriggers.FirstOrDefault(x => x.Id == id);
            if (info?.TriggerType == null) return null;

            try
            {
                return (TriggerBase)serviceProvider.GetService(info.TriggerType);
            }
            catch
            {
                return null;
            }
        }
    }
}
