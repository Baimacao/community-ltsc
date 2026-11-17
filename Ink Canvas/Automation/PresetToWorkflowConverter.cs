using Ink_Canvas.WorkflowAutomation.Enums;
using Ink_Canvas.WorkflowAutomation.Models;
using System;
using System.Collections.Generic;

namespace Ink_Canvas.WorkflowAutomation
{
    /// <summary>
    /// 把「预设自动化」（旧硬编码的那 25 个开关）转成等价的工作流。
    ///
    /// 为什么需要这个：自动化设置页里同时存在两套系统 ——
    /// 左边是 18 个自动收纳 + 8 个自动杀进程的固定开关（旧），
    /// 右边是自由编排的工作流（新）。两套并行跑，互不知道对方状态，
    /// 老师看到两个地方都能「让 ICC 在希沃里让路」，不知道该信哪个。
    ///
    /// 彻底合并不是删掉旧的那套 —— 旧预设的价值是「点一下就生效」，
    /// 老师不需要自己拼规则。这里做的是把旧预设翻译成工作流，
    /// 让新引擎能接管这些场景，旧开关仍可用（它走的是更快的固定路径）。
    ///
    /// 匹配条件严格按旧代码的语义复刻，包含同名不同代软件的区分：
    ///   · 希沃白板 EasiNote 按文件版本号区分 5.x / 3.x，按产品名区分 3C
    ///   · 其余按进程名直接匹配
    /// </summary>
    internal static class PresetToWorkflowConverter
    {
        /// <summary>一个预设项的完整定义：怎么匹配 + 命中后做什么。</summary>
        private sealed class PresetDef
        {
            public string SettingName;      // Settings.Automation 里的属性名
            public string DisplayName;      // 给人看的工作流名
            public string ProcessName;      // 进程名（不含 .exe）
            public string VersionPrefix;    // 可选：版本前缀
            public string ProductContains;  // 可选：产品名包含串
            public bool IsKill;             // false=折叠，true=杀进程
        }

        /// <summary>
        /// 全部预设的定义表。刻意和 MW_Timer.cs 里的旧逻辑一一对应，
        /// 任何一边改了都要同步另一边 —— 这张表就是两套系统之间的契约。
        /// </summary>
        private static readonly List<PresetDef> Defs = new List<PresetDef>
        {
            // ── 自动收纳：希沃系 ─────────────────────────────
            new PresetDef { SettingName="IsAutoFoldInEasiNote",        DisplayName="希沃白板 5",     ProcessName="EasiNote", VersionPrefix="5." },
            new PresetDef { SettingName="IsAutoFoldInEasiNote3",       DisplayName="希沃白板 3",     ProcessName="EasiNote", VersionPrefix="3." },
            new PresetDef { SettingName="IsAutoFoldInEasiNote3C",      DisplayName="希沃白板 3C",    ProcessName="EasiNote", ProductContains="3C" },
            new PresetDef { SettingName="IsAutoFoldInEasiNote5C",      DisplayName="希沃白板 5C",    ProcessName="EasiNote5C" },
            new PresetDef { SettingName="IsAutoFoldInEasiCamera",      DisplayName="希沃展台",       ProcessName="EasiCamera" },
            new PresetDef { SettingName="IsAutoFoldInSeewoPincoTeacher", DisplayName="希沃品课",     ProcessName="seewoPincoTeacher" },

            // ── 自动收纳：鸿合系 ─────────────────────────────
            new PresetDef { SettingName="IsAutoFoldInHiteTouchPro",    DisplayName="鸿合屏幕书写",   ProcessName="HiteTouchPro" },
            new PresetDef { SettingName="IsAutoFoldInHiteLightBoard",  DisplayName="鸿合轻量白板",   ProcessName="HiteLightBoard" },
            new PresetDef { SettingName="IsAutoFoldInHiteCamera",      DisplayName="鸿合展台",       ProcessName="HiteCamera" },

            // ── 自动收纳：其它白板 ───────────────────────────
            new PresetDef { SettingName="IsAutoFoldInWxBoardMain",     DisplayName="文香白板",       ProcessName="WxBoardMain" },
            new PresetDef { SettingName="IsAutoFoldInMSWhiteboard",    DisplayName="微软白板",       ProcessName="MicrosoftWhiteboard" },
            new PresetDef { SettingName="IsAutoFoldInAdmoxWhiteboard", DisplayName="安道白板",       ProcessName="Amdox.WhiteBoard" },
            new PresetDef { SettingName="IsAutoFoldInAdmoxBooth",      DisplayName="安道展台",       ProcessName="Amdox.Booth" },
            new PresetDef { SettingName="IsAutoFoldInQPoint",          DisplayName="QPoint",         ProcessName="QPoint" },
            new PresetDef { SettingName="IsAutoFoldInYiYunVisualPresenter", DisplayName="艺云展台",  ProcessName="YiYunVisualPresenter" },
            new PresetDef { SettingName="IsAutoFoldInMaxHubWhiteboard", DisplayName="MaxHub 白板",   ProcessName="WhiteBoard", VersionPrefix="6.", ProductContains="WhiteBoard" },
            new PresetDef { SettingName="IsAutoFoldInOldZyBoard",      DisplayName="中原旧白板",     ProcessName="ZyBoard" },

            // ── 自动杀进程 ───────────────────────────────────
            new PresetDef { SettingName="IsAutoKillPPTService",        DisplayName="结束 PPT 服务",  ProcessName="PPTService", IsKill=true },
            new PresetDef { SettingName="IsAutoKillEasiNote",          DisplayName="结束希沃白板",   ProcessName="EasiNote", IsKill=true },
            new PresetDef { SettingName="IsAutoKillHiteAnnotation",    DisplayName="结束鸿合批注",   ProcessName="HiteAnnotation", IsKill=true },
            new PresetDef { SettingName="IsAutoKillVComYouJiao",       DisplayName="结束 VCom",      ProcessName="VComYouJiao", IsKill=true },
            new PresetDef { SettingName="IsAutoKillSeewoLauncher2DesktopAnnotation", DisplayName="结束希沃桌面画笔", ProcessName="SeewoLauncher2DesktopAnnotation", IsKill=true },
            new PresetDef { SettingName="IsAutoKillInkCanvas",         DisplayName="结束 InkCanvas", ProcessName="InkCanvas", IsKill=true },
            new PresetDef { SettingName="IsAutoKillICA",               DisplayName="结束 ICA",       ProcessName="InkCanvasForClass", IsKill=true },
            new PresetDef { SettingName="IsAutoKillIDT",               DisplayName="结束 IDT",       ProcessName="IDT", IsKill=true },
        };

        /// <summary>
        /// 用当前预设设置生成工作流。只生成「已启用」的那些 ——
        /// 老师没勾的预设不该凭空多出一条工作流。
        /// </summary>
        public static List<Workflow> Build(Settings settings)
        {
            var result = new List<Workflow>();
            if (settings?.Automation == null) return result;

            var auto = settings.Automation;
            var type = typeof(Automation);

            foreach (var def in Defs)
            {
                // 读旧开关的当前值
                bool enabled;
                try
                {
                    var prop = type.GetProperty(def.SettingName);
                    if (prop == null) continue;
                    enabled = (bool)(prop.GetValue(auto) ?? false);
                }
                catch
                {
                    continue;
                }
                if (!enabled) continue;

                result.Add(CreateWorkflow(def));
            }

            return result;
        }

        private static Workflow CreateWorkflow(PresetDef def)
        {
            var wf = new Workflow
            {
                IsConditionEnabled = true,
            };
            // Workflow 本身没有名字字段，名字挂在 ActionSet 上（页面上显示的就是它）。
            wf.ActionSet.Name = def.DisplayName;

            // 条件：前台窗口进程名（+版本号/产品名）
            var settings = new Rules.ForegroundWindowProcessRuleSettings
            {
                ProcessName = def.ProcessName,
                VersionPrefix = def.VersionPrefix ?? "",
                ProductNameContains = def.ProductContains ?? "",
            };
            var rule = new Rule
            {
                Id = Rules.ForegroundWindowProcessRule.RuleId,
                Settings = settings,
            };
            var group = new RuleGroup { Mode = RulesetLogicalMode.And, IsEnabled = true };
            group.Rules.Add(rule);
            wf.Ruleset.Groups.Add(group);

            // 动作：杀进程 或 折叠
            if (def.IsKill)
            {
                wf.ActionSet.Actions.Add(new Models.Action
                {
                    Id = ActionSettings.KillProcessAction.ActionId,
                    Settings = new ActionSettings.KillProcessActionSettings { ProcessName = def.ProcessName },
                });
            }
            else
            {
                wf.ActionSet.Actions.Add(new Models.Action
                {
                    Id = ActionSettings.FoldAction.ActionId,
                    Settings = new ActionSettings.FoldActionSettings { Fold = true },
                });
            }

            return wf;
        }
    }
}
