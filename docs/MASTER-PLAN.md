# ICC-CE LTSC 1.7.19.7 完整重构规划

| 项 | 值 |
|---|---|
| 文档版本 | v2.1（2026-11-17 三路合并，见下「合并记录」） |
| 写于 | 2026-11-11 09:45，合并于 2026-11-16 17:00，2026-11-17 合并两份分叉副本 |
| 分支 | `ltsc`（原 `ltsc/1.7.19.7`） |
| 当前 HEAD | `7ea7cd4f`（2026-11-17 合并时；正文中提到的 `4d4f6e35` / `5c0dba42` 是各节写就时的 HEAD） |
| 文档状态 | **主体完成**（N-08 回归与交付未开始） |
| 编写原则 | 每条结论必须能追到 `文件:行` 或日志行号；未验证的标「未验证」 |

> **合并记录（2026-11-17，v2.0 → v2.1）**
>
> 合并前存在两份 `MASTER-PLAN.md`，内容**双向分叉**——不是简单的「旧的 / 新的」：
>
> | 副本 | 行数 | 独有内容 |
> |---|---|---|
> | `src/docs/`（进 git） | 1083 | 版本头更新到 `4d4f6e35`、N 系列标「剩余 1 项」 |
> | `docs/`（本地） | 1116 | **N-03 冲突矩阵五条逐项处置**、裸 ESC 三组反向对照实测、`--smoke-test` PASS=15 FAIL=1 |
>
> 关键事实：`grep -c "5b494b37"` → src **0** / 本地 **3**。
> `src/docs/` 那份是在 `1d652f80` 的底座上改的，**把 N-03 的成果整段丢了**，
> 只留下「冲突矩阵未验收」的旧说法。若按「src 新、本地旧」单方向覆盖，会丢内容。
>
> 合并方式：以 `1d652f80` 为共同祖先做三路合并（`git merge-file`），
> 两侧改动区域不重叠（src 只动头部 1 个 hunk，本地只动 N-03 3 个 hunk），
> 合并结果 0 冲突、1116 行，两侧独有内容全部保留。

> **v2.0 合并说明**
> 原 v1.0 只覆盖上游重构（P0–P6）。2026-11-15 19:25 用户追加了九条增量需求，
> 当时另写了 `docs/PLAN.md`（N-01～N-09）。两份文档各自维护会打架，
> 现将 PLAN.md 并入本文档，**P 系列原样保留**（那是上游重构路径记录），
> N 系列作为**本分支实际执行的主线**附在 P6 之后。
>
> 阅读顺序：要看「现在还剩什么」直接跳 §14；要看「当初为什么这么拆」读 §1–§13。

---

## 0. 这份文档解决什么

用户（ta）提出三条需求 + 六条提醒建议。本文件把它们拆成可执行、可验收、可回退的步骤。

### 0.1 需求原文（一字不改）

1. **设置冗余**：「设置有很多冗余项，建议完整列出设置树并且精简去除不必要的或是一定带来副作用的功能和一定获得正收益的功能（使它默认开启）、按逻辑重新分类排列、分页设置项，去除不需要的设置页（如性能页），部分已经修改的功能文案不适用就更改」

2. **PPT 联动失效**：「ppt联动功能整体失效，无论是否使用ppt联动增强，iccce虽能检测到ppt处于放映状态，但退出功能无效，翻页组件未出现」

3. **笔迹卡顿**：「刚启动时笔迹渲染较稳定，但稍微久一点或者笔画多一点、使用多指书写就会出现可感知的卡顿感且此时会出现白板的双指移动与缩放会失效，建议深查清楚」

### 0.2 提醒与建议原文

| # | 内容 | 我的执行位置 |
|---|---|---|
| R1 | 「电脑操作插件已经给你换成更好用的版本了」 | 阶段 0.3 |
| R2 | 「ICC CE 本来就作为一个多人接手，n 次破坏性改动的石山，对里面任何可疑代码都要深查，任何冗余都要删除，任何技术债都要修，必要时一定要重构，不能有侥幸心理」 | 贯穿全文档 |
| R3 | 「笔迹性能优化可以学习 inkeys3」 | 阶段 2.1 |
| R4 | 「ppt联动可以学习 inkeys3」 | 阶段 1.1 |
| R5 | 「这个项目引用的库要好好查，如果不知名或臃肿直接换」 | 阶段 4 |
| R6 | 「浮动栏动效一定要学习 inkeys3」 | 阶段 2.6 |
| R7 | 「还有社区反应过的bug最好都复查一遍」（附论坛链接） | 阶段 5 |

### 0.3 执行准则（用户明示）

> **先规划 — 假设所有情况 — 修改 — 验证 — 交付**
> **一步一步来，每次交付都要告诉我现在的进度**

---

## 1. 证据基线

以下全部为实测，非推测。

### 1.1 PPT 问题的日志证据

日志文件：`E:\Users\B1807\Documents\Log_2026-11-11-08-35-12.txt`（252 行）

**现象 A：27 次创建失败**

```
08:37:07.140 [Error] 创建PowerPoint应用程序实例失败:
                     COMException (0x80048240): Application (unknown member) : Invalid request.
                     at Microsoft.Office.Interop.PowerPoint.ApplicationClass.set_Visible(MsoTriState Visible)
                     at Ink_Canvas.MainWindow.CreatePowerPointApplication()
```

**现象 B：每一轮的完整事件序列**

```
Error   创建PowerPoint应用程序实例失败          ← 失败
Event   PowerPoint应用程序守护已启动            ← StartPowerPointProcessMonitoring
Event   PowerPoint应用程序已关闭                ← ClosePowerPointApplication（Quit 了用户的 PPT）
Event   PowerPoint应用程序守护已停止            ← StopPowerPointProcessMonitoring
（下一轮）
```

**现象 C：间隔无规律**

| 统计项 | 值 |
|---|---|
| 次数 | 27（首条无间隔） |
| 最小间隔 | 36 ms |
| 中位间隔 | 329 ms |
| 最大间隔 | 3088 ms |
| < 100 ms | 7 次 |
| < 300 ms | 10 次 |

**现象 D：PptBridge 在同一时刻连接成功**

```
:88  08:37:07.585 [T7] PptBridge COM 事件已注册
:89  08:37:07.593 [T7] PptBridge 已连接 PowerPoint
:91  08:37:07.597 [T1] PPT连接已建立
```

**现象 E：comSlide 全程 -1**

```
:229  08:37:39.781 [PPT详细] 轮询检测到放映开始(COM事件未触发) comSlide=-1
:230  08:37:59.279 [PPT详细] 轮询检测到放映结束 comSlide=-1
:233  08:38:17.780 [PPT详细] 轮询检测到放映开始(COM事件未触发) comSlide=-1
:234  08:38:31.781 [PPT详细] 轮询检测到放映结束 comSlide=-1
```

**现象 F：0x80048240 的真实语义**

`Invalid request. Hiding the application window is not allowed.`
—— 当 PowerPoint 已经存在可见窗口时，禁止把 Application 设为不可见。

### 1.2 PPT 问题的代码定位

| 位置 | 代码 | 问题 |
|---|---|---|
| `MW_PPT.cs:288` | `// PPTLinkMode 枚举保留但不再分支：老配置反序列化得到任何值都走同一条路。` | D-02 收口只做了一半：统一了 manager，没删旧 COM 路径 |
| `MW_PPT.cs:290` | `_pptManager = new PptBridge();` | 新路已生效 |
| `MW_PPT.cs:371` | `StartPowerPointProcessMonitoring()` | **旧路残留**，`:376` 判 `PPTLinkMode != Com` 就 return |
| `MW_PPT.cs:379` | `CreatePowerPointApplication();` | **旧路入口** |
| `MW_PPT.cs:427` | `CreatePowerPointApplication()` | `:431` 同一判据；`:439` `new Application()`；**`:442` `Visible = msoFalse` 抛异常** |
| `MW_PPT.cs:471` | `catch (Exception ex) { LogHelper... }` | **只记日志，没把 `pptApplication` 置空** |
| `MW_PPT.cs:485` | `SetPPTManagerApplication(app)` | `:494` 反射找 `ConnectToPPT` |
| `MW_PPT.cs:597` | `ClosePowerPointApplication()` | `:628` `pptApplication.Quit()` —— **会关掉用户的 PPT** |
| `MW_PPT.cs:702` | `OnPowerPointApplicationMonitorTick` | 1 秒守护定时器，`:714` 判失效 → `:717` 重建 |
| `MW_PPT.cs:2353` | `StartPowerPointProcessMonitoring()` | 设置页开关切换时调用 |

**关键矛盾**：`PptBridge` 类**没有** `ConnectToPPT` 方法（已 grep 确认），所以 `SetPPTManagerApplication` 必然失败；但 `PptBridge` 有只读属性 `PPTApplication`。

**跨文件依赖**：`pptApplication` 静态字段在 `MW_Save&OpenStrokes.cs` 有 **11 处**引用（`:702 :704 :705 :706 :812 :814 :815 :816 :1091 :1174 :1183`），全部是 `pptApplication.SlideShowWindows[1].Presentation.*` 形式。

**设置页残留**：`PowerPointPage.xaml` 里 `ComboBoxPPTArchitecture` 仍在；`PowerPointPage.xaml.cs:51 :88 :90 :125-144` 仍在使用 `PPTLinkMode`。

### 1.3 笔迹与触摸的规模

| 指标 | 数值 |
|---|---|
| 项目 .cs 总数 | 406 |
| 笔迹/触摸相关 .cs | 79 |
| 触摸/触控处理点（grep） | 431 处 |

**关键嫌疑文件**（尚未验证因果，阶段 2 需实测）：

| 文件 | 为什么可疑 |
|---|---|
| `MainWindow.xaml.cs:2224` `inkCanvas_PreviewStylusDown` | **Preview 阶段**拦截，会在 InkCanvas 拿到事件之前抢走 |
| `MainWindow.xaml.cs:2233` `inkCanvas_StylusDown` | 同一事件链的第二道 |
| `Helpers\MultiTouchInput.cs` | 多点输入处理 |
| `MainWindow_cs\MW_TouchEvents.cs` | 触摸事件总入口 |
| `Helpers\AdvancedBezierSmoothing.cs` | 每笔都跑贝塞尔细化 |
| `Helpers\InkSmoothingManager.cs` | 平滑调度器（H-1 改过） |
| `Helpers\SliderTouchHelper.cs` | 滑块触摸（社区反馈过滑块拖不动） |

### 1.4 依赖清单（27 个，逐个核过）

**代码零引用（7 个，删）**：

| 包 | 证据 |
|---|---|
| MdXaml 1.27.0 | 只在 `packages.lock.json` 出现，`.cs/.xaml` 零命中 |
| Microsoft.International.Converters.PinYinConverter 1.0.0 | 同上 |
| Microsoft.Toolkit.Uwp.Notifications 7.1.3 | 同上（N-01 已移除通知） |
| System.ComponentModel.Composition 10.0.5 | 同上 |
| System.Composition.AttributedModel 10.0.5 | 同上 |
| System.Data.DataSetExtensions 4.5.0 | 同上 |
| System.Private.Uri 4.3.2 | 同上 |

**保留（有正当理由）**：

| 包 | 理由 |
|---|---|
| Costura.Fody 6.0.0 | `FodyWeavers.xml` 里配着 `<Costura ExcludeAssemblies="IACore\|IALoader\|IAWinFX" />`，是打包器 |
| Microsoft.NETFramework.ReferenceAssemblies 1.0.3 | `PrivateAssets="All"`，编译期用 |
| AForge.* 4 个 | 摄像头功能在用 |
| 其余 16 个 | 均有 `.cs/.xaml` 命中 |

### 1.5 inkeys3 是什么（已查清）

| 项 | 内容 |
|---|---|
| 全名 | 智绘教 Inkeys |
| 作者 | Alan-CRL（AlanCRL 工作室） |
| 仓库 | https://github.com/Alan-CRL/Inkeys |
| 官网 | https://www.inkeys.top/ |
| 核心技术 | **Draw3 绘制引擎** + **UI3 界面框架** |
| UI3 特征 | 高性能 UI、**流畅且可打断的非线性回弹动画**、光影与动态光影、无级缩放、底栏吸附/居中 |
| 许可情况 | LTS 正式版**尚未发布**，最新为 Inkeys2 `20260214a`；Canary 版 `20261014a` 已带 Draw3 |
| 我的用法 | **只读它的公开代码与设计思路，不引入依赖、不复制代码** |

> ⚠️ 待办：阶段 1.1 前需克隆/阅读 Inkeys 仓库，确认 Draw3 与 UI3 的具体实现方式。**未验证它们是否开源到可参考的程度**。

### 1.6 社区反馈里命中本次症状的条目

来源：`https://forum.smart-teach.cn/d/468-icc-ce-bugfan-kui-zhuan-lan`

| # | 社区原话 | 与本次症状的关系 |
|---|---|---|
| C1 | 「白板书写时三指及以上触控时锁定缩放只能平移」 | **直接命中**需求 3 的「双指移动与缩放失效」 |
| C2 | 「退出ppt放映后会自动隐藏（似乎与设置没有关系）」 | 命中需求 2 |
| C3 | 「空ppt放映不会触发联动」 | 命中需求 2 |
| C4 | 「ppt退出直接卡死（严重）关闭长按翻页修复」 | 命中需求 2 |
| C5 | 「点击这个按钮会无限启动 icc-ce / 应该是崩溃自动重启的问题」 | 与 PPT 守护 loop 同形，需复查 |
| C6 | 「现在 icc 无法识别是手动退出还是自身崩溃，如果使用任务管理器强制退出仍会重启」 | 崩溃判定，需复查 |
| C7 | 「右键任务栏图标，点击重启按钮后会无限重启」 | 已声称修复，需复查 |
| C8 | 「设置界面的透明度滑块无法正常拖动，只能通过屏幕键盘来移动」 | 命中 `SliderTouchHelper.cs` |
| C9 | 「每次更新可能会重置设置」 | 需复查设置持久化 |
| C10 | 「icc 运行时有小概率出现整个屏幕除了 icc 程序之外其他地方无法触控或点击」 | 悬浮窗拦截器，需复查 |

---

## 2. 阶段总览

| 阶段 | 名称 | 交付物 | 预计步骤 |
|---|---|---|---|
| **P0** | 前置准备与基线冻结 | 基线包、验证工具、inkeys3 调研 | 5 |
| **P1** | PPT 联动彻底重建 | 可运行的 PPT 联动 | 12 |
| **P2** | 笔迹性能与触摸系统 | 流畅书写 + 双指正常 | 14 |
| **P3** | 设置树精简与重排 | 新设置树 + 迁移期兼容 | 11 |
| **P4** | 依赖清理与技术债 | 干净的 csproj | 6 |
| **P5** | 社区 bug 逐条复查 | 复查报告 | 10 |
| **P6** | 总验证与 LTSC 交付 | 发布包 + 清单 | 8 |

**原则**：每个阶段结束都产出**可运行的包**，交给用户实测。测试不通过不进入下一阶段。

---

## 3. 阶段 P0：前置准备与基线冻结

### P0-1　冻结基线

**目标**：有一个「改动前」的可对照版本，任何一步出问题都能退回。

- [ ] `git tag baseline-2026-11-11` 打在 `654f807c`
- [ ] 完整编译一次，产物复制到 `dist\_baseline\`
- [ ] 记录基线指标：启动耗时、稳态句柄/GDI/USER/线程/WS/PM（静置 90 秒）

**验收**：`dist\_baseline\` 能双击运行。

### P0-2　建立自动化验证脚本

**目标**：不再靠肉眼判断「改没改」，每次都跑同一套。

- [ ] 写 `tools\verify.ps1`，内容：
  - 剥注释后按方法体做括号配对提取（吸取上次正则跨方法边界的教训）
  - 对每个决策 ID 断言「代码符合预期」
  - 输出 PASS/FAIL 表
- [ ] 写 `tools\smoke.ps1`：启动 exe，等 15 秒，抓日志，检查 Error 数量

**验收**：脚本能正确报出当前 `654f807c` 的真实状态。

### P0-3　验证新版电脑操作插件

**目标**：R1 说插件换了新版，我要先确认它能不能用，再决定测试怎么做。

- [ ] 检查当前会话可用的 `computer_*` 类工具
- [ ] 实测：截图能否拿到 ICC 窗口真实像素
- [ ] 实测：能否把 ICC 窗口提到前台
- [ ] 记录结论到本文档 §8 工具篇

**验收**：明确写出「能用 / 不能用 / 部分能用」，不含糊。

### P0-4　调研 inkeys3

**目标**：R3/R4/R6 都要求学它，我得先知道学什么。

- [ ] 读 Inkeys 仓库的 README、目录结构、关键类
- [ ] 重点看三块：
  - 绘制引擎（Draw3）：笔迹是怎么渲染的、有没有用 D3D/D2D、平滑在哪一步
  - PPT 联动：它怎么拿页码、怎么处理模态框
  - UI3 动画：可打断动画怎么实现
- [ ] 输出 `docs/INKEYS3-NOTES.md`，**只记录可借鉴的设计，不复制代码**
- [ ] 对照 ICC 现状，列出「值得抄的思路」和「抄不了的（架构差异）」

**验收**：notes 文档里每条都有出处（文件:行 或 文档链接）。

### P0-5　确认 P1 的测试方法

**目标**：PPT 测试要能真的看到 53 页，不是「打开了个空白」。

- [ ] 确认 `Presentations.Open` 第 4 参数必须是 `-1`（WithWindow），不是 `0`
- [ ] 准备测试用 PPT：`E:\DSH\tmp\ppt-test\test1.pptx`（53 页，12.5 MB）
- [ ] 确认验收标准：**ICC 显示总页数 53、当前页跟随、ICC 工具栏翻页能驱动 PPT**

**验收**：测试步骤写死在本文档 §4.9。

---

## 4. 阶段 P1：PPT 联动彻底重建

### 4.0 本阶段的核心判断

**不做补丁，做拆除。**

理由：现状是两套机制并行且互相打架——

- **新路**（PptBridge + ROT）：日志证明它连上了（`:88-91`）
- **旧路**（COM 模式）：每次开开关都失败，还会 `Quit` 用户的 PPT
- 翻页组件依赖旧路的 `pptApplication` 字段

只要旧路还在，任何补丁都只是让它失败得更安静。**必须拆干净。**

### 4.1 步骤清单

#### P1-1　读全所有受影响代码（不写代码，只读）

- [ ] `MW_PPT.cs` 全文（约 2400 行）
- [ ] `PptBridge.cs` 全文（约 1200 行）
- [ ] `MW_Save&OpenStrokes.cs` 中 11 处 `pptApplication` 引用及其上下文
- [ ] `PowerPointPage.xaml` + `.xaml.cs` 全文
- [ ] `Settings.cs` 里 `PowerPointSettings` 全部字段
- [ ] 找出所有 `OnPPTSlideShowBegin` / `OnPPTSlideShowEnd` / `OnPPTPresentationOpen` / `OnPPTSlideShowNextSlide` 的实现

**验收**：能画出「谁调用谁」的完整调用图。

#### P1-2　确定字段迁移方案

**决策点**：`pptApplication`（静态 COM 字段）往哪去？

三个候选：

| 方案 | 做法 | 风险 |
|---|---|---|
| **A** | 全部删掉，11 处改用 `_pptManager.GetCurrentActivePresentation()` | 改动大，但最干净 |
| **B** | 保留字段，但只在 PptBridge 连上时从它的 `PPTApplication` 属性同步过来 | 改动小，但留着两套状态 |
| **C** | 保留字段，但改为「PptBridge 连不上时的 Win32 兜底」 | 最保守 |

**我倾向 A**。理由：R2 说「任何冗余都要删除，必要时一定要重构，不能有侥幸心理」。B/C 都是留后门。

- [ ] 列 11 处引用的每一处，写清「它真正需要什么数据」
- [ ] 逐处给出替代写法
- [ ] 提交给用户确认 A/B/C

**验收**：用户拍板选哪个。

#### P1-3　拆除旧 COM 路径

- [ ] 删 `CreatePowerPointApplication()`（`MW_PPT.cs:427-475`）
- [ ] 删 `StartPowerPointProcessMonitoring()`（`:371-396`）
- [ ] 删 `StopPowerPointProcessMonitoring()`（`:401-417`）
- [ ] 删 `OnPowerPointApplicationMonitorTick()`（`:702-724`）
- [ ] 删 `ClosePowerPointApplication()`（`:597-661`）—— **这一条最关键，它现在是会 Quit 用户 PPT 的凶手**
- [ ] 删 `_powerPointProcessMonitorTimer` 字段
- [ ] 删 `ProcessMonitorInterval` 常量（`:158`）
- [ ] 删 `IsPowerPointApplicationValid()`（`:554`）—— 上一轮刚改过，现在连同调用者一起删
- [ ] 删 `IsPowerPointProcessAlive()`（上一轮新增的，若无其他调用者一并删）
- [ ] 删 `SetPPTManagerApplication()`（`:485-523`）—— 它反射找 `ConnectToPPT`，PptBridge 没这方法
- [ ] 删 `ClearStaticInteropState()`（`:669-694`）—— 若方案 A

**验收**：`grep -c "pptApplication\|CreatePowerPointApplication\|ClosePowerPointApplication"` 在 `MW_PPT.cs` 里为 0 或仅剩注释。

#### P1-4　清理 `PPTLinkMode` 死判断

- [ ] 删 `:376` `if (Settings.PowerPointSettings.PPTLinkMode != PPTLinkMode.Com) return;`
- [ ] 删 `:431` 同上
- [ ] 删 `:490` 同上
- [ ] 删 `:711` 同上
- [ ] **保留** `enum PPTLinkMode`（老配置文件反序列化需要）
- [ ] 在枚举上写注释说明「已无分支，仅兼容旧配置」

**验收**：`grep PPTLinkMode` 只剩枚举定义和反序列化相关。

#### P1-5　修翻页组件的页码来源（对应现象 E）

- [ ] 定位 `RefreshSlideShowState()` 里 `comSlide` 的计算（`PptBridge.cs:568-632`）
- [ ] 现状：`SlideShowWindows.Count > 0` → `Item(1)` → `View` → `CurrentShowPosition`
- [ ] 失败时被 `catch` 吞掉（`:611-615`），只置 `comInShow = true`，`comSlide` 保持 -1
- [ ] **加二级兜底**：`View` 失败时尝试 `window.View.Slide.SlideIndex`
- [ ] **加三级兜底**：再失败时用 `Application.ActivePresentation.SlideShowWindow.View.Slide.SlideIndex`
- [ ] **加日志**：三级都失败时，把三级的异常各记一条（现在全被吞了，查不出为什么）

**验收**：真机测试时日志能显示具体页码，不是 -1。

#### P1-6　修翻页组件不出现

- [ ] 找出翻页组件的显示条件（很可能依赖 `pptApplication != null`）
- [ ] 改为依赖 `_pptManager.IsConnected && _pptManager.IsInSlideShow`
- [ ] 确认 `OnPPTSlideShowBegin` 被正确触发

**验收**：进放映后翻页组件出现。

#### P1-7　修退出放映无效

- [ ] 查 `OnPPTSlideShowEnd` 是否被调用（日志里 `:230` 有「轮询检测到放映结束」，说明检测到了）
- [ ] 查检测到之后做了什么 —— 是否因为 `pptApplication` 为 null 而提前 return
- [ ] 修状态复位链路

**验收**：退出放映后 ICC 回到白板，工具栏恢复。

#### P1-8　重写开关逻辑

**新设计**（开关只做两件事）：

```
开：_pptManager.StartMonitoring();
关：_pptManager.StopMonitoring(isShutdown: false);
```

- [ ] `ToggleSwitchPowerPointEnhancement_Toggled` 改为只调上面两行
- [ ] **绝不**创建 Application
- [ ] **绝不**调用 `Quit()`
- [ ] 加防抖：连续切换时只处理最后一次（用户手动快速开关 27 次的情况）

**验收**：连续开/关 20 次，日志里 0 条 Error，PPT 不被关闭。

#### P1-9　清理设置页

- [ ] 删 `PowerPointPage.xaml` 里的 `ComboBoxPPTArchitecture`
- [ ] 删 `PowerPointPage.xaml.cs:51` 的 `ComboBoxPPTArchitecture.SelectedIndex = ...`
- [ ] 删 `:88-95` `UpdatePPTArchitectureDependentCards()`
- [ ] 删 `:125-147` `ComboBoxPPTArchitecture_SelectionChanged`
- [ ] 检查 `:133-135` 那段「非 Com 就关增强」的逻辑一并删除

**验收**：设置页 PPT 部分没有「架构」选项。

#### P1-10　处理配置迁移

- [ ] 老配置文件里 `PPTLinkMode` 可能是 1(Rot) 或 2(Agent)
- [ ] 读配置时忽略该值，但**不要报错**
- [ ] 写一条迁移日志：「PPTLinkMode 已废弃，统一走 PptBridge」

**验收**：用老配置文件启动，不崩、不报错。

#### P1-11　编译 + 代码级验证

- [ ] 编译 0 错误
- [ ] 跑 `tools\verify.ps1`
- [ ] 人工复核每条删除的调用者都没漏

**验收**：verify 全 PASS。

#### P1-12　真机验证（交给用户）

见 §4.9。

### 4.2 假设的所有情况（P1）

| # | 假设 | 应对 |
|---|---|---|
| H1 | 用户 PPT 已打开且可见 | 新的开关逻辑永不碰 `Visible`，因此不触发 0x80048240 |
| H2 | 用户 PPT 未打开 | PptBridge 走 ROT 找不到 → `IsConnected=false`，翻页组件不显示，不报错 |
| H3 | PPT 有模态框（保存提示等） | 轮询的 COM 调用会阻塞，但轮询在后台线程（T5/T7/T17/T26），不冻 UI |
| H4 | PPT 崩溃 | `IsPptProcessAlive()` 检测到，`Disconnect`，等下轮重连 |
| H5 | 用户快速连点开关 20 次 | 防抖只处理最后一次 |
| H6 | 跨权限（ICC 提权 + PPT 普通） | ROT 拿不到 → 降级 Win32，只能贴窗口不能翻页（**这是已知限制，要在 UI 上说清**） |
| H7 | WPS 而非 Office | `IsSupportWPS` 分支保持原样 |
| H8 | 老配置文件 PPTLinkMode=2 | 忽略该值，不报错 |
| H9 | 删掉 `pptApplication` 后发现某处还在用 | 编译期就会报错，不会漏 |
| H10 | `ConnectToPPT` 反射失败 | 整段删除，不再尝试 |

### 4.3 回退方案（P1）

- 每个子步骤一个 commit，出错 `git revert` 单个 commit
- 阶段级回退：`git reset --hard baseline-2026-11-11`

---

## 5. 阶段 P2：笔迹性能与触摸系统

### 5.0 本阶段的前提

**必须先实测，不能靠猜。** 用户描述的组合是：
> 刚启动稳 → 用久了卡 → 笔画多更卡 → 多指书写更卡 → **此时双指缩放失效**

这个组合有两种可能，必须分清：

| 可能 | 特征 | 验证方法 |
|---|---|---|
| **甲：性能退化导致触摸事件被丢弃** | 卡顿时 CPU 高、GC 频繁；触摸事件队列积压 | 加性能计数器，看卡顿时是不是 GC/CPU 尖峰 |
| **乙：触摸事件被写字逻辑抢占** | 双指失效与卡顿同时出现；单指写字正常时双指也失效 | 关掉平滑、只做单指写，看双指是否恢复 |

**两种情况修法完全不同，不能混。**

### 5.1 步骤清单

#### P2-1　加性能观测（不改行为）

- [ ] 在笔迹路径加计数器：
  - 每笔从 `StylusDown` 到 `StrokeCollected` 的耗时
  - 每秒 `Stroke` 对象创建数
  - `Dispatcher` 队列长度
  - GC 各代次数与耗时
- [ ] 加「当前触摸点数」的实时记录
- [ ] 输出到日志（限流，每 5 秒一条）

**验收**：能拿到卡顿时刻的真实数据。

#### P2-2　复现卡顿（用户操作，我给步骤）

- [ ] 给用户一份精确的复现脚本：
  1. 启动 ICC，进白板
  2. 连续写 50 笔（每笔约 3 秒）
  3. 记录第 1 笔和第 50 笔的耗时
  4. 双指缩放，记录是否有效
  5. 再写 50 笔，重复
  6. 三指触摸，记录缩放是否失效
- [ ] 用户跑完把日志给我

**验收**：拿到「第 N 笔开始卡」的临界点。

#### P2-3　判定甲/乙

- [ ] 分析 P2-2 的数据
- [ ] 若 GC 频繁 → 甲
- [ ] 若 GC 正常但触摸丢失 → 乙

**验收**：明确写下判定和依据。

#### P2-4　读 inkeys3 的绘制引擎

- [ ] 按 §P0-4 的笔记，重点看：
  - 它用什么画（D3D? D2D? WPF? 自绘？）
  - 平滑在哪一步做
  - 多指时怎么处理
- [ ] 对比 ICC 的 `InkCanvas` + `AdvancedBezierSmoothing` 路线

**验收**：写出「ICC 与 Inkeys 的差异清单」。

#### P2-5　修触摸事件抢占（若判定为乙）

- [ ] 检查 `inkCanvas_PreviewStylusDown`（`MainWindow.xaml.cs:2224`）
  - 它在 Preview 阶段，会先于 InkCanvas 拿到事件
  - 看它有没有 `e.Handled = true`
- [ ] 检查 `MW_TouchEvents.cs` 的触摸路由
- [ ] 检查 `MultiTouchInput.cs`
- [ ] **目标**：多指时写字与手势能正确分流（2 指 = 手势，1 笔 = 写字）

**验收**：写字时双指缩放正常。

#### P2-6　修笔迹渲染路径（若判定为甲）

- [ ] 按 inkeys3 的思路重构平滑
- [ ] 当前：`InkSmoothingManager` → `HardwareAcceleratedInkProcessor.SmoothStrokeSync`
- [ ] 看能否改成「增量平滑」（每来一个点就平滑一次，而不是整笔重算）
- [ ] 或改成「分帧平滑」（把长笔迹拆到多帧）

**验收**：第 50 笔的耗时接近第 1 笔。

#### P2-7　修滑块拖不动（社区 C8）

- [ ] `SliderTouchHelper.cs:46-51` 同时挂 `TouchDown/TouchMove/StylusDown/StylusMove`
- [ ] 现状可能是触摸与鼠标事件冲突
- [ ] 修法：只保留一套，或用 `PreviewTouch` 统一处理

**验收**：手指能拖滑块。

#### P2-8　修三指锁定缩放（社区 C1）

- [ ] 找到现有实现
- [ ] 用户原话：「三指及以上触控时锁定缩放只能平移」
- [ ] 这个行为**可能是有意的**（防误触），需要跟用户确认保留还是改

**验收**：确认行为符合预期。

#### P2-9　浮动栏动效（R6）

- [ ] 读 inkeys3 的 UI3 动效实现
- [ ] 关键点：「流畅且可打断的非线性回弹动画」
- [ ] 现状：ICC 已有全局动效开关（D-05）
- [ ] 目标：浮动栏显隐动画改到合成层，可打断

**验收**：浮动栏动画不掉帧。

#### P2-10　内存与句柄稳态

- [ ] 写 500 笔，监测句柄/内存
- [ ] 目标：**无向上趋势**

**验收**：500 笔后指标回到基线附近。

#### P2-11　暴力测试

- [ ] 按约定#8：
  - 快速连写不停
  - 写字同时连按工具栏
  - 写字同时双指缩放
  - 反复进出白板 20 次
  - 写 200 笔后立刻切 PPT 模式再切回

**验收**：不崩、不卡死、不泄漏。

#### P2-12～14　分三批交付

把 P2 拆成三个可测的包：
- P2-12：性能观测 + 判定报告（用户看数据）
- P2-13：触摸修复包（用户测双指）
- P2-14：性能修复包（用户测长时书写）

---

## 6. 阶段 P3：设置树精简与重排

### 6.0 现状

**37 个设置页文件**（`Windows\SettingsViews\Pages\*.xaml`）：

```
AboutPage              AdvancedPage           AppearancePage
AutomationWorkflowPage BackupPage             BoardAppearancePage
BoardMenuPage          BoardToolbarPage       CanvasPage
ClockPage              CloudStoragePage       DebugPage
ExperimentalPage       FriendlyLinksPage      HomePage
HotkeyPage             InkRecognitionPage     NotificationPage
PerformancePage        PluginMarketplacePage  PluginPage
PluginSettingsPage     PowerPointPage         PPTPageFlipPreviewPage
PrivacyPage            RandomDrawPage         SecurityPage
SettingsPage           SidebarPage            StartupPage
StoragePage            ToolbarAppearancePage  ToolbarMenuPage
ToolbarPage            WhiteboardTipsPage     WindowPage
（+ PluginMarketSourcesWindow 是子窗口）
```

### 6.1 步骤清单

#### P3-1　导出完整设置树（只读，不改）

- [ ] 对每个 Page：
  - 读 XAML，提取所有控件
  - 读 `.xaml.cs` 的 `LoadSettings()`，找到它绑定的 `Settings` 字段
  - 读 `Settings.cs`，找该字段的默认值
- [ ] 输出表格：

```
| 页 | 控件 | 绑定字段 | 默认值 | 读它的代码位置 | 判定 |
```

- [ ] 判定四档：
  - **保留**：真的有用
  - **默认开启**：一定正收益（用户要求）
  - **删**：一定副作用 / 死配置 / 重复
  - **需决策**：要用户拍板

**验收**：完整的 CSV/Markdown 表格。

#### P3-2　识别「一定带来副作用」的

已知候选：

| 项 | 为什么有副作用 |
|---|---|
| 进程保护 | 已在 D-04 移除 |
| 硬件加速（M-2） | 名不副实，实为 CPU 运算，误导用户 |
| PPT 架构选择 | 已无意义（P1 会删） |
| 自动更新 | 已移除（D-01） |
| 「检查更新」入口 | 同上 |

#### P3-3　识别「一定正收益」→ 默认开启

待识别。候选：

| 项 | 为什么正收益 |
|---|---|
| 笔画平滑 | 用户明确反馈卡顿，平滑是必要的 |
| 撤消/重做 | 基础功能 |
| 自动保存墨迹 | 防止丢数据 |

**此项必须用户拍板。** 我不替他决定默认值。

#### P3-4　删除整个设置页

用户点名：**性能页**。

我的建议清单（**待用户确认**）：

| 页 | 建议 | 理由 |
|---|---|---|
| PerformancePage | **删** | 用户点名 |
| DebugPage | **删** | 普通老师不该看到 |
| ExperimentalPage | **删** | 实验功能不该出现在稳定版 |
| CloudStoragePage | 需决策 | 若无人用则删 |
| FriendlyLinksPage | 需决策 | 与本机使用无关 |
| PluginMarketplacePage | 需决策 | 依赖网络 |
| WhiteboardTipsPage | 需决策 | 若只是说明文本可并入别处 |
| PrivacyPage | 需决策 | 合规需要？ |
| SecurityPage | 需决策 | |
| AboutPage | **保留** | 版本号需要 |

#### P3-5　按逻辑重新分类

当前分类（`SettingsWindow.xaml` 顺序）看不出逻辑。建议新分组（**待用户确认**）：

```
一、常用
   启动与外观 / 画布 / 白板
二、工具栏
   浮动栏 / 白板工具栏 / 侧边栏 / 快捷键
三、教学功能
   PPT 联动 / 点名 / 随机抽选 / 时钟 / 自动化
四、数据
   存储 / 备份 / 墨迹识别
五、系统
   窗口 / 通知 / 高级
六、关于
```

#### P3-6　分页设置项

用户说「分页设置项」——把过长的一页拆开。

待识别：哪几页控件过多（需要 P3-1 的数据）。

#### P3-7　修文案

- [ ] 「硬件加速」→ 改为「笔迹增强（CPU 运算）」
- [ ] 所有因 D-01/D-04/P1 改动而失效的文案
- [ ] 「关闭」按钮文案（C-01 的 AnimationOff 教训：名字像≠用途同）

#### P3-8～11　执行 + 验证

- [ ] 删页面（连同 `_pageTypes` 注册）
- [ ] 删控件
- [ ] 改默认值
- [ ] 重排顺序
- [ ] 编译 + 逐页点开验证

---

## 7. 阶段 P4：依赖清理

### P4-1　删 7 个零引用包

逐个删，每删一个编译一次：

- [ ] MdXaml
- [ ] Microsoft.International.Converters.PinYinConverter
- [ ] Microsoft.Toolkit.Uwp.Notifications
- [ ] System.ComponentModel.Composition
- [ ] System.Composition.AttributedModel
- [ ] System.Data.DataSetExtensions
- [ ] System.Private.Uri

**验收**：`packages.lock.json` 重新生成后这 7 个消失。

### P4-2　评估其余 20 个

对每个写一行：「它做什么 / 是否臃肿 / 有无替代」

重点看：
- `AForge.*`（4 个，2013 年的库，已停止维护）—— 摄像头功能是否可换 `OpenCvSharp` 或 `MediaCapture`
- `iNKORE.UI.WPF` + `iNKORE.UI.WPF.Modern`（两个 UI 库）
- `MdXaml` 删后是否影响帮助文档渲染

### P4-3　检查传递依赖

- [ ] 看 `packages.lock.json` 的完整闭包
- [ ] 找有没有被间接引入的重包

---

## 8. 阶段 P5：社区 bug 逐条复查

按 §1.6 的 C1~C10，每条：
- [ ] 在代码里找对应实现
- [ ] 判断：已修 / 未修 / 无法复现 / 设计如此
- [ ] 写进 `docs/COMMUNITY-BUGS.md`

---

## 9. 阶段 P6：总验证与交付

- [ ] 全量编译 0 错误
- [ ] verify.ps1 全 PASS
- [ ] 启动测试（无 .NET 8 环境时弹提示）
- [ ] 稳态测试（90 秒，指标无上升趋势）
- [ ] 暴力测试（约定#8）
- [ ] 打包 zip
- [ ] 写 `RELEASE-CHECKLIST.md`

---

## 10. 风险登记

| # | 风险 | 影响 | 应对 |
|---|---|---|---|
| R1 | 删 `pptApplication` 影响面比预期大 | 编译大量报错 | P1-1 先读全再动手 |
| R2 | inkeys3 不可参考 | P2 无法对标 | P0-4 先确认；不可参考就走自己的路 |
| R3 | 用户 PPT 版本差异 | 新逻辑在某些版本失效 | 加详细日志，让用户测时能定位 |
| R4 | 设置树精简删掉用户需要的东西 | 用户不满 | P3-1 表格先给用户看，他拍板再删 |
| R5 | 笔迹重构引入新 bug | 更卡 | 每步编译 + 可回退 |
| R6 | 删依赖导致某功能静默失效 | 难查 | 每删一个跑一次冒烟 |

---

## 11. 工具篇（P0-3 填写）

| 工具 | 能力 | 状态 |
|---|---|---|
| `computer_screenshot` | 截 ICC 窗口真实像素 | **待验证** |
| `computer_drag` | 合成鼠标拖拽 | **待验证**（上次点在了浏览器上） |
| PowerShell 兜底 | EnumWindows + PrintWindow | **已知对 WPF 无效** |

> ⚠️ 上次教训：ICC 默认悬浮窗 + 鼠标穿透，必须先点「批注」或「白板」才能画。

---

## 12. 进度追踪

| 阶段 | 状态 | 开始 | 完成 | 备注 |
|---|---|---|---|---|
| P0 | 未开始 | | | |
| P1 | 未开始 | | | |
| P2 | 未开始 | | | |
| P3 | 未开始 | | | |
| P4 | 未开始 | | | |
| P5 | 未开始 | | | |
| P6 | 未开始 | | | |

### 12.1 P1 逐项实测状态（2026-11-12 08:20 按代码与提交核对）

核对方法：grep 代码 + 查提交记录 + 日志实测。不是凭印象。

> 本节来自 `src/docs/MASTER-PLAN.md`（仓库内版本），合并前外层 `docs/MASTER-PLAN.md`
> 是 11-11 的旧版，缺这一节。补回以免丢失 P1 的实际执行记录。

| 项 | 状态 | 证据 |
|---|---|---|
| P1-1 读全代码 | 完成 | 各文件已通读，调用图见 §1.2 |
| P1-2 pptApplication 迁移方案 | 完成（选 A 全删） | 全仓仅剩 2 处注释，无实际引用 |
| P1-3 拆旧 COM 路径 | **完成，但与计划描述不同** | `CreatePowerPointApplication` / `OnPowerPointApplicationMonitorTick` / `_powerPointProcessMonitorTimer` / `ProcessMonitorInterval` / `IsPowerPointApplicationValid` / `SetPPTManagerApplication` / `ClearStaticInteropState` 全部为 0 引用。`ClosePowerPointApplication` 仅剩 1 处注释。**`Start/StopPowerPointProcessMonitoring` 保留了，但已换实现**：现在只调 `_pptManager.StartMonitoring()/StopMonitoring()`，不碰 PowerPoint 进程。原计划写「删掉这两个方法」，实际做法是保留方法名、换掉实现——因为它们是 `SettingsActionHub` 和开关逻辑的稳定入口，删了要改 11 处调用且没有收益。 |
| P1-4 清理 PPTLinkMode 死判断 | 完成 | 全仓 11 处均为：枚举定义、序列化字段、迁移日志、注释。**无任何分支依赖它** |
| P1-5 页码三级兜底 | 完成 | 一级 View.Slide、二级 TryGetSlideIndexFallback、三级 Win32；三级失败各自记日志（本次又补了连接失败日志） |
| P1-6 翻页栏不出现 | **已修，未验证** | `f81bbdc7`：轮询路径补刷 SlidesCount。见下方问题 A |
| P1-7 退出放映无效 | 完成 | `OnPPTSlideShowEnd` 存在且被订阅，日志实测有「轮询检测到放映结束」+「PPT状态变量已重置」 |
| P1-8 重写开关逻辑 | 完成 | 只调 Start/Stop ProcessMonitoring；防抖 400ms 已实现；不再创建 Application、不调 Quit |
| P1-9 清理设置页 | 完成 | `ComboBoxPPTArchitecture` 全仓 0 引用 |
| P1-10 配置迁移 | 完成 | 老 PPTLinkMode 值会写迁移日志，不报错 |
| P1-11 编译验证 | 完成 | 0 error |
| P1-12 真机验证 | **部分** | 翻页栏出现 ✅、点页码弹缩略图网格 ✅（2026-11-12 用户确认）；退出放映 ✅；**字体/外观类未逐项验** |

#### 问题 A（P1-6 实际暴露的缺陷）

`Helpers/PPTUIManager.cs:302-310`：

```
bool hasValidPageCount = slidesCount > 0;
bool shouldShowButtons = ShowPPTButton && ... && hasValidPageCount && ...;
if (!shouldShowButtons) { HideAllNavigationPanels(); return; }
```

`slidesCount` 来自 `PPTManager.SlidesCount`，而它只在 COM 事件回调里刷新。
**只要总页数读不到，翻页栏就整个不显示**——这是 2026-11-12 早上「翻页栏不出现」的直接原因。

要不要放宽这个条件是设计取舍（没有总页数时显示 1/0 更怪），留待定夺，先记录。

**2026-11-12 已修**（`f81bbdc7`）：根因比预想的更具体。

`RefreshSlidesCount()` 一共 4 个调用点，**全都在 COM 事件回调里**
（连接成功 / 翻页 / 打开演示文稿 / 关闭演示文稿）。
`RefreshSlideShowState()` 这条 500ms 轮询里没有任何一处刷新 `SlidesCount`。

于是 COM 事件不触发时（放映刚启动经常如此），
轮询能算出 `comSlide=1`，却没人去刷总页数，
`SlidesCount` 一直是 0 → `hasValidPageCount=false` → 整条翻页栏被隐藏。

修法：轮询确认拿到放映窗口后，`if (SlidesCount <= 0) RefreshSlidesCount();`

**验证状态：未验证。** 真机跑过一次翻页栏正常，但日志显示 COM 事件先到，
那次 `SlidesCount` 是事件路径刷的，不是新加的这行。
目标场景（COM 事件不触发）没能复现。
下次再出现「翻页栏不出现」，看日志有没有
「轮询检测到放映开始(COM事件未触发)」且没有「COM 事件: 放映开始」——
有的话就是这条路径生效了。

> **P 系列状态说明**：P1–P6 是 2026-11-11 的上游重构路径。实际执行中，
> 用户于 11-14 与 11-15 追加了增量需求，本分支优先做了那些（见 §14）。
> P 系列中已被 N 系列覆盖的部分，在 §14 末尾「P 系列的去留」里逐条说明，
> **不再单独排期**，避免同一件事记两遍。

---

## 13. 我需要用户拍板的事

| # | 事项 | 我的建议 | 何时需要 |
|---|---|---|---|
| Q1 | P1-2：`pptApplication` 用方案 A/B/C | **A（全删）** | P1 开始前 |
| Q2 | P3-4：删哪些设置页 | 见 §6.1 P3-4 表 | P3 开始前 |
| Q3 | P3-5：新的分组方案 | 见 §6.1 P3-5 | P3 开始前 |
| Q4 | P3-3：哪些改为默认开启 | 待 P3-1 数据 | P3 开始后 |
| Q5 | P2-8：三指锁缩放保留还是改 | 需确认设计意图 | P2 中 |
| Q6 | P4-2：AForge 要不要换 | 待评估 | P4 |

---

---

> ## ⚠ 阅读提醒：§0–§13 未经复核
>
> **§0–§13（第一部分的 717 行）写于 2026-11-11 09:45**，记录的是上游重构路径 P0–P6 的规划。
>
> 此后到 2026-11-17 合并时，仓库累计新增 **182 个提交**（`git rev-list --count --since="2026-11-11 09:45" HEAD`）。
> 这 182 个提交**没有回头核对过 §0–§13 里的任何一条结论**——其中的行号引用、代码位置、
> 「未做 / 待做」判断都可能已经失效。
>
> §16 记录了逐阶段的现状对照，但那是 N 系列收尾时的粗粒度复盘，不等于逐条复核。
>
> **结论**：§0–§13 当**历史决策记录**读，不要当**当前代码事实**引用。
> 要引用其中任何一条，先回代码重新验证。第二部分（§14 起）才是当前执行状态。

---

# 第二部分：增量执行计划（原 docs/PLAN.md，2026-11-15 19:50）

> **本轮性质**：重做，不是修补。用户原话：「本次范围内不可遗留代码、能重做全部重做」。
> **边界**：设置与多语言可重做；不做无关重构；不破坏已完成功能。

## 14. 总览与状态

| 项 | 内容 | 状态 |
|---|---|---|
| N-01 | 设置页整体重做（含自动化重点重做） | ✅ **完成**（8 个提交） |
| N-02 | 多语言重做（3 语 → 4 语） | ✅ **完成** |
| N-03 | 翻页笔适配与快捷键冲突治理 | ✅ **代码完成**（`5b494b37` 收口五条冲突），缺实物按键验收 |
| N-04 | 默认配置文件落地 | ✅ **完成**（2 个提交） |
| N-05 | PPT 翻页组件深浅同步修复 | ✅ **完成** |
| N-06 | 系统深浅模式 logo 适配 | ✅ **完成** |
| N-07 | 废弃物清理 | ✅ **基本完成**（`f37f8d8d` 清 7 个空壳 resx；零引用属性剩 3 个，见 N-07 节） |
| N-08 | 回归与交付 | ❌ 未开始 |
| N-09 | 计时功能重做 | ✅ **完成** |

**剩余 1 项**：N-08 回归与交付（`git log --grep="N-08"` 零命中）。
其余八项全部完成；N-03 仅缺翻页笔实物按键验收。

> **本表原写作「N-01 / N-04 / N-07 未开始」，2026-11-17 合并时按 git 记录更正。**
> 原写法与同一节的「剩余 1 项」结论、以及汇总行「N-01 / N-04 / N-07 已完成」自相矛盾，
> 属于表格行没跟上执行。核对命令：
> `git log --oneline --grep="N-01" | wc -l` → 8，`N-04` → 2，`N-07` → 1。

---

## N-01 设置页整体重做（含自动化重点重做）

| 项 | 内容 |
|---|---|
| 状态 | ✅ **已完成**（8 个提交，`a26ce5f6` … `b0d9ae67`） |
| 影响模块 | `Windows/SettingsViews/**`、`Automation/**`、`Windows/SettingsViews/SettingsWindow.xaml(.cs)` |
| 规模 | 26 个页面、约 900 个可见控件、`AutomationWorkflowPage` 867 行 XAML + 1327 行 code-behind |
| 验收标准 | ①每个可见设置项都能追到一个真实读取点；②无悬停依赖；③最小点击区域 44×44 逻辑像素；④深浅色两套截图无对比度低于 4.5:1 的正文；⑤自动化可用「选程序 / 选窗口 / 填类名」配出「目标出现→隐藏，目标关闭→恢复」全流程 |

**必须解决的存量问题**

1. `AutomationWorkflowPage` 同时承载两套自动化：旧硬编码（AutoFold 52 / AutoKill 16 / FloatingInterceptor 26）与新工作流引擎。两套语义重叠、互不知道对方状态。
2. 旧硬编码的消费点在 `MainWindow_cs/MW_Timer.cs` 的 500ms 统一计时器里轮询前台窗口 + 进程名 + 文件版本（`MW_Timer.cs:841-1000`）。新引擎已有 `SystemEventMonitor`（WinEvent 钩子 + 1s 进程计时器），能力覆盖前者。
3. 翻页笔设置的按键配对**不落盘** —— `PresenterKeyAdapter.Bindings` 是 `static readonly` 内存列表，改键后重启即丢。

---

## N-02 多语言重做（3 语 → 4 语）✅ 完成

| 项 | 内容 |
|---|---|
| 状态 | **已完成**（提交 `9452a797` → `6756fcbf`） |
| 影响模块 | `Properties/*.resx`（37 类）、`Helpers/LocalizationHelper.cs`、`Windows/OobeWindow.*`、`SettingsWindow.*` |
| 验收结果 | 四套 key 完全一致，共 **1316 条**；无缺失、无孤儿；简繁同形字误报已排除；切换语言走重启提示 |

**结构性变更（已完成）**：原 `zh-ME` 一码两用（既当繁体又当梗体）。已拆成
`zh-CN` 简体 / `zh-TW` 繁体（新建，全量）/ `zh-ME` 梗体 / `en-US` 英文。

**完成明细**

- 繁中全量生成：台湾用语表 74 条 + Windows `LCMapString` 字形转换
- 梗体重写 1034 条，保留 278 条（按钮名 / 技术词 / 产品名，按风格约定不动）
- 丢弃死文案 686 条（1993 → 1307），另补回 9 条（5 条关于页开发者角色 + 4 条换语言重启提示），现为 1316
- 语言切换改为「保存 + 提示重启」，修复 OOBE 与设置窗口被连带关闭
- 详细进度见 `docs/i18n-PROGRESS.md`

---

## N-03 翻页笔适配与快捷键冲突治理

| 项 | 内容 |
|---|---|
| 状态 | **代码完成**（`64513fdb` / `8ede7990` / **`5b494b37`**），缺翻页笔实物验收 |
| 影响模块 | `Helpers/PresenterKeyAdapter.cs`、`Helpers/LowLevelKeyboardHook.cs`、`Helpers/GlobalHotkeyManager.cs`、`MainWindow_cs/MW_PresenterKey.cs`、`Windows/SettingsViews/Pages/PresenterKeyPage.*`、`Resources/Settings.cs` |
| 验收标准 | ①冲突矩阵每一项都有明确处置；②提示框正文 ≥14px、按钮 ≥44 高、对比度 ≥4.5:1、深浅色各验一遍；③钩子在任何退出路径上都被卸载（含崩溃、注销、关机） |

### 冲突矩阵（`5b494b37` 收口）

| # | 冲突 | 处置 |
|---|---|---|
| 1 | 适配开启时 `return false` 放行 —— ICC 与 PPT 各响应一次 | **已修**：改 `return true`，吃掉并执行映射 |
| 2 | `Exit` 全局热键 = 裸 Escape，与适配 `exit` 映射同键 | **已修**：退出热键改为**仅放映期间注册**，退出即注销 |
| 3 | 裸 Escape 的 `RegisterHotKey` 是系统级独占 | **已修**：见上，非放映状态不再挂系统热键 |
| 4 | 按键配对 `Bindings` 是内存列表，改键后重启即丢 | **已修**：落盘到 `Settings.presenterKey` |
| 5 | `CleanupObsoleteSettings` 把 `bindings` 子键当过期属性删掉 | **已修**：默认值为空对象时视为开放字典，跳过子键比对 |

### 关于裸 Escape 的实测结论（推翻了本节旧说法）

旧文写「适配未开启时 PPT 模式下按 Escape 退不出放映」，**范围判断错了**。
实测（三组反向对照，焦点在前台窗口、B 键作对照均到达）：

```
无进程注册裸 ESC → 前台窗口 escDown=1 escUp=1
有进程注册裸 ESC → 前台窗口 escDown=0 escUp=1（KeyDown 被系统吃掉）
该进程退出后     → 恢复 escDown=1
```

即谁挂着裸 ESC，**全系统所有程序的 ESC 都收不到 KeyDown**，与 ICC 处于什么模式无关。

但生效条件要说准（查日志确认）：ICC 启动时是鼠标模式，
`InitializeGlobalHotkeyManager` 会立刻 `UpdateHotkeyStateForToolMode(true)` 把热键全注销，
**启动时并不占用 ESC**；只有切进批注/白板模式（非鼠标模式且 `EnableHotkeysInMouseMode=false`）
才会整批注册，那时才独占。

### 验证

`InkCanvasForClass.exe --smoke-test`，PASS=15 FAIL=1（唯一 FAIL 为既有的 C-5 对照组设计问题）。
N-03 六项全过，其中两条带反向对照：

```
return true  → [PASS] N-03 适配开启时 ESC 被吞 | return=True
return false → [FAIL] N-03 适配开启时 ESC 被吞 | return=False
```

### 剩余

翻页笔**实物按键**验收（需要真设备按 B / ESC / Shift+F5 / Ctrl+P / E）。

---

## N-04 默认配置文件落地

| 项 | 内容 |
|---|---|
| 状态 | ✅ **已完成**（`cca6c906` 出厂默认配置落地 / `34e1ab2b` OOBE 教师推荐预设） |
| 输入 | `E:\Program Files\Netease\GameViewer\Download\Configs.zip`（8 个文件，已解压至 `E:\DSH\tmp\cfgzip`） |
| 影响模块 | `Windows/SettingsViews/Helpers/SettingsManager.cs`、`Resources/Settings.cs`、首次运行路径 |
| 验收标准 | ①出厂默认与「把用户配置原样装进去」两种语义分开实现；②迁移不覆盖用户已有值；③用户机器绝对路径与 `hasShownOobe` 不得进入出厂默认 |

---

## N-05 PPT 翻页组件深浅同步修复 ✅ 完成

已提交 `f399f9b8`。根因：`PPTUIManager.IsDarkThemeForNavBar()` 读的是系统主题而非应用主题。
已改为读主窗口即时主题，并在 `SetTheme()` 里主动刷新翻页条样式。
`PPTGlobalBlackBackground` 与 4 个 `PPTxxBlackBackground` 及对应设置项全删。

---

## N-06 系统深浅模式 logo 适配 ✅ 完成

已提交 `496dd40a`。新增 `AppLogo16/20/24/32/64/Ico` 六档 BitmapImage 资源键，
深浅两套各指向对应图标；界面全部改用 `{DynamicResource AppLogoNN}`；
新增 `Resources/icc-dark.ico`（7 个尺寸，手工组装）。
`grep -rn "icc.ico"` 只剩 csproj 的 `ApplicationIcon`。

---

## N-07 废弃物清理

| 项 | 内容 |
|---|---|
| 状态 | **基本完成**（空壳 resx 已清；零引用属性清了但还剩 3 个） |
| 影响模块 | `Resources/Settings.cs`（319 个 JsonProperty 属性）、`Properties/*.resx`、`App.xaml.cs` |
| 验收标准 | ①删除后 `dotnet build` 无警告新增；②首启仍只生成 6 个正常目录；③旧版 `Settings.json` 能加载且未知键忽略而非崩 |

**已完成**：

1. 清掉 686 条死文案（见 N-02）。
2. **7 个空壳 resx 类已删**（`f37f8d8d`）：`CloudStorageStrings` / `PluginStrings` / `FeedbackStrings` /
   `PerformanceStrings` / `AnnouncementStrings` / `PopupStrings` / `UpdateCenterPanelStrings`，
   连 `.resx` × 4 语言 + `.Designer.cs` 一起清。核实：`ls Properties/<name>.resx` 七个全部「No such file」。

**未完成**（2026-11-17 实测重新核过，原数字要更正）：

原写「351 个属性中的零引用项未清（上次审计说有 29 个）」。**两个数都不对**：

| 原说法 | 实测 | 怎么测的 |
|---|---|---|
| 351 个属性 | **319 个** `[JsonProperty]` 属性 | 正则抽 `[JsonProperty("x")]` + `public T X { get; set; }`，去重 |
| 29 个零引用 | **3 个** | 319 个属性名逐个对 341 个 `.cs`（排除 `Settings.cs` 自身、`obj/`、`bin/`）做 `\bName\b` 全文匹配 |

剩下的 3 个零引用属性：

```
SplashScreenStyle           (Settings.cs:377)
CustomSplashImagePath
CustomSplashTextPosition
```

三个都只在 `Settings.cs` 里声明，**`.cs` / `.xaml` / `.resx` 全无第二处引用**，
也没有对应 UI。属于启动闪屏的遗留配置，不影响运行，但按验收标准①「每个可见设置项都能追到一个真实读取点」
它们连设置项都不算——是彻底的孤儿字段。**未删，等后续一并处理。**

> **方法说明**：这里的「零引用」是**属性名在 `.cs` 源码里零命中**，不含 XAML 绑定与
> 反射/序列化访问。所以它是「几乎没有引用」的下界证据，不是「确定可删」的证明。
> 要真删必须先确认没有 XAML 绑定（本轮已查：这三个不在任何 `.xaml` 里）。

---

## N-08 回归与交付

| 项 | 内容 |
|---|---|
| 状态 | 未开始（依赖 N-01 / N-03 / N-04 / N-07） |
| 验收标准 | ①暴力测试：连点 20 次不崩、快速切页不卡、触摸全流程可操作；②触摸屏验证（无 hover 依赖）；③深度性能：500ms 计时器不再做跨进程 UIA 全枚举；④干净包 10 文件；⑤交接文档只写 bug 修复与改进 |

---

## N-09 计时功能重做 ✅ 完成

| 项 | 内容 |
|---|---|
| 状态 | **已完成**（提交 `3941dda3` → `e6f2518b`） |
| 需求原文 | 「计时功能重做 要求：美观、易用、动画流畅、在进入倒计时状态后，5秒后折叠调时间的组件」 |
| 追加需求 | 「其他除了时间显示的控件都可以收纳（就直接整个窗口都是时间显示）」+「收纳状态也要可以拖动」 |

**已完成**

- 单窗口双形态：调时态 ↔ 运行态，弹簧动画切换
- 进入倒计时 5 秒后，除时间显示外全部折叠；点击恢复
- 折叠态整窗可拖动，点击不误触拖动（`FoldClickThreshold=6`，触摸 16）
- 小窗模式：窗口尺寸减半、无标题栏、整窗可拖、靠屏幕上边缘
- 删掉旧版 `CountdownTimerWindow`（860 行）与无人引用的 `NewStyleMinimizedTimerWindow`
- `UseLegacyTimerUI` / `UseNewStyleUI` 两个开关一并删除
- 数字 Path 图形保留（Q16 选的方案），54×54 无裁剪、无描边

---

## 15. 待确认问题（合并后统一编号）

原有 Q1–Q6 见 §13。N 系列的问题与当前状态：

| # | 问题 | 状态 |
|---|---|---|
| Q13 | `CountdownTimerWindow` 删除还是保留为第二套皮肤？ | ✅ 已裁决：**删除**，已执行 |
| Q14 | 折叠后怎么展开？ | ✅ 已裁决：**点数字区**，已执行 |
| Q15 | 独立迷你窗保留还是删掉？ | ✅ 已裁决：**删掉**，已执行 |
| Q16 | 数字用 Path 还是 TextBlock？ | ✅ 保留 Path，未重做 `DigitResources.xaml` |
| Q17 | 折叠动画预设？ | ✅ 已用弹簧预设实现 |

**当前真正待裁决的只有 §13 的 Q1–Q6，且其中多数已被本分支的执行覆盖**：

| # | 原问题 | 现状 |
|---|---|---|
| Q1 | `pptApplication` 用方案 A/B/C | ✅ 已按 A（全删）执行，`pptApplication.Quit()` 已不存在 |
| Q2 | 删哪些设置页 | ⏳ 并入 N-01 一起定 |
| Q3 | 新的分组方案 | ⏳ 并入 N-01 一起定 |
| Q4 | 哪些改为默认开启 | ⏳ 并入 N-01 一起定 |
| Q5 | 三指锁缩放保留还是改 | ⏳ 待裁决 |
| Q6 | AForge 要不要换 | ⏳ 依赖数已从 27 降到 19，AForge 仍在，待评估 |

---

## 16. P 系列的去留

P1–P6 是上游重构路径，本分支没走那条路。逐条说明为什么：

| 阶段 | 原计划 | 现状 |
|---|---|---|
| P0 | 基线冻结、验证脚本、inkeys3 调研 | 部分做了（`tools/verify.js`），inkeys3 未调研 |
| P1 | PPT 联动彻底重建 | **未按 P1 走**，但 N-05 修掉了翻页条深浅问题，PPT 主链路另有 R 系列修复 |
| P2 | 笔迹性能与触摸系统 | 未做 |
| P3 | 设置树精简与重排 | **被 N-01 取代**，不再单独排期 |
| P4 | 依赖清理 | 部分做了（27 → 19），剩余并入 N-07 |
| P5 | 社区 bug 逐条复查 | 未做 |
| P6 | 总验证与交付 | **被 N-08 取代** |

**结论**：P1 / P2 / P5 三项是真实未做的工作，其余已被 N 系列覆盖。
是否还要按 P 系列继续推进，等 N 系列收尾后再定。

---

*本文档随执行更新。任何与代码不符之处，以代码为准，并回头修正本文档。*
