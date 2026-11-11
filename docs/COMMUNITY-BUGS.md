# 社区 bug × 当前任务 合并表

> 目的：避免 P5 重复修已经做过的东西。
> 来源：`https://forum.smart-teach.cn/d/468-icc-ce-bugfan-kui-zhuan-lan`
> 建立时间：2026-11-11 15:10

---

## 一、重叠关系总览

| # | 社区原话 | 归属阶段 | 状态 | 与当前工作的关系 |
|---|---|---|---|---|
| C1 | 三指及以上触控时锁定缩放只能平移 | — | **不存在** | 所有者 2026-11-11 确认 |
| C2 | 退出 PPT 放映后自动隐藏 | P1 | **待诊断** | 原判「同双指」是错的；真嫌疑是全屏分支 `MW_Timer.cs:1297` |
| C3 | 空 PPT 放映不触发联动 | — | **不修** | 链路已走完，无法复现「空 ppt」具体情形；见 §七 |
| C4 | PPT 退出直接卡死 | P1 | **已修** `f1b71fe7` | 根因是退出时不停长按定时器；见 §七 |
| C5 | 点击按钮无限启动 ICC | P2 | 同源（崩溃链） | 崩溃自动重启，和 `App.xaml.cs` 崩溃处理相关 |
| C6 | 任务管理器退出仍重启 | P2 | 同源（崩溃链） | 同 C5，崩溃判定 |
| C7 | 右键重启按钮无限重启 | P2 | 同源（崩溃链） | 同 C5 |
| C8 | 透明度滑块无法拖动 | 独立 | 未动 | `SliderTouchHelper.cs`，与主触摸链不同文件 |
| C9 | 每次更新重置设置 | P3 | **同源** | 设置持久化，与设置树精简同一块 |
| C10 | 全屏无法触控或点击 | — | **不修** | 两个原怀疑对象已排除；见 §七 |

---

## 二、四条必须合并处理

### C2 → 前两次判断都错，真相是：这不是 bug

**第一次判断（错）**：看到 `MW_AutoFold.cs:410` 有 `ArePPTControlsVisible`，
就判定 C2 和双指同源。**拿变量名推断，没读机制。**

**第二次判断（也错）**：改口说真嫌疑是 `MW_Timer.cs:1297` 的 `if (hasFullScreen)` 分支。
读完 `HasFullScreenWindowOfAutoFoldApps()` 全文（`MW_Timer.cs:816-941`）后推翻：
这条分支认的进程清单里**没有 PowerPoint**，全是白板一体机软件
（EasiNote / HiteCamera / seewoPincoTeacher / MicrosoftWhiteboard / Amdox / QPoint / WhiteBoard 等）。
PPT 放映根本进不了这条分支。

**第三次判断（错）**：说这两个开关"没有暴露"。
**搜法错了**——我用字段名 `IsAutoFoldAfterPPTSlideShow` 去搜 xaml，
但 xaml 里的控件名是 `CardAutoFoldAfterPPTSlideShow`，字段名在 xaml 里根本不出现。

**实际事实**：两个开关都在「自动化」页，用户能开能关。

| 字段 | 界面文案 | 调用点 |
|---|---|---|
| `IsAutoFoldInPPTSlideShow` | 播放 PPT 时自动收纳 | `MW_PPT.cs:878` |
| `IsAutoFoldAfterPPTSlideShow` | 退出 PPT 放映模式后自动收纳浮动栏 | `MW_PPT.cs:1257` / `:2966` |

界面位置：`AutomationWorkflowPage.xaml:256`（CardAutoFoldInPPTSlideShow）/
`:270`（CardAutoFoldAfterPPTSlideShow）。
默认值：两个都是 `false`（`Settings.cs:942/945`，无初始化器）。

**结论：这不是 ICC 的 bug。**

用户吐槽的「退出 PPT 后自动隐藏」，是他自己开了
「退出 PPT 放映模式后自动收纳浮动栏」。社区原话「似乎与设置没有关系」——
那是**没找到这个开关**：它不在「PPT 联动」页，在「自动化」页。

**唯一可能的真实瑕疵**：这个开关放在自动化页，但用户心理预期它在 PPT 联动页。
这属于**设置项位置不好找**，不是行为错误。要不要挪位置另说。

**状态：关闭（不是 bug）。不再需要实测。**

### C1 → 同一文件的手势子系统

三指及以上锁定缩放只能平移，走的是 `Main_Grid_ManipulationDelta` 里
`enableZoom` / `enableTranslate` / `enableRotate` 那组分支
（`MW_TouchEvents.cs:2195-2222`）。

双指改的是同一个方法的**入口判据**。改入口可能影响三指分支的行为，
所以 C1 必须在 P2 双指稳定之后立刻验，不能留到 P5。

### C3 + C4 → 就在今天改的代码里

- **C3 空 PPT**：`SlideShowWindows.Count == 0` 或 `SlidesCount == 0`。
  今天刚给 `Count==0` 加了诊断日志，给 `SlidesCount` 加了 `Math.Max(0, ...)`。
  **这条已经在改动范围里，只是没按 C3 命名验证过。**
- **C4 退出卡死**：`RefreshSlideShowState` 的 `nowInShow && !_inSlideShow` 分支。
  今天补的三处页码断点全在这个方法里。

### C5 + C6 + C7 → 崩溃判定三条同源

都指向「ICC 分不清手动退出和自身崩溃」。
今天在 `App.xaml.cs` 加了 `DispatcherUnhandledException` 兜底，
但**没有动崩溃后的重启判定逻辑**（`CrashAction` / `IsAppExitByUser`）。
这三条应该合成一个子任务做。

---

## 三、真正的独立项（P5 只剩这些）

| # | 原因 |
|---|---|
| C8 | `SliderTouchHelper.cs`，和主触摸链不同文件，可以单独做 |
| C10 | `WS_EX_NOACTIVATE` 悬浮窗拦截器，独立子系统 |

---

## 四、结论：P5 从 10 条缩到 2 条 + 8 条验证

| 原计划 | 调整后 |
|---|---|
| P5 逐条复查 C1~C10（10 条） | **P5 只剩 C8、C10 两条要修** |
| — | C1/C3/C4 移进 P2 验证清单 |
| — | C2 移进 P1 修复清单 |
| — | C5/C6/C7 合成一个子任务，归 P2 |
| — | C9 移进 P3 |

---

## 五、修正后的任务归属

### P1（PPT 联动）追加
- [ ] **C2**：`ArePPTControlsVisible` 在 `MW_AutoFold.cs:410` 的语义错误
- [ ] **C3**：空 PPT（`SlidesCount==0`）不触发联动 —— 已有诊断，补行为
- [ ] **C4**：退出放映卡死 —— 检查 `RefreshSlideShowState` 退出分支

### P2（笔迹/触摸）追加
- [ ] **C1**：三指及以上锁定缩放只能平移
- [ ] **C5+C6+C7**：崩溃后重启判定（合成一条）

### P3（设置树）追加
- [ ] **C9**：更新后设置被重置

### P5（社区 bug）只剩
- [ ] **C8**：透明度滑块无法拖动
- [ ] **C10**：全屏无法触控

---

## 六、当前状态

以下是**已改但未按 bug 编号验证**的项，需在对应测试里补验：

| 改动 | 提交 | 对应的社区 bug |
|---|---|---|
| 双指判据改 `IsInPPTPresentationMode` | P2e | C1 的前置（双指恢复） |
| ROT 实例优先级评分 | `0e97cb09` | C3（选错实例 → 看不到放映） |
| `SlideShowWindows.Count==0` 诊断 | `0e97cb09` | C3 |
| 页码三断点 + 10 调用点 | `64d151a9` | C4（退出后状态残留） |


---

## 七、2026-11-11 晚补充：C1/C3/C4/C10 的实际查证

以上 §二～§六 写于当天 15:10，是**排期阶段的判断**。
晚上实际动手后，有四条结论需要按实测更正。**以本节为准。**

### C1 → 不存在（所有者裁决）

所有者 2026-11-11 22:03 原话：「c1 不存在」。

§二 当时把它排进 P2「需一起验」，依据是「和双指共用 `MW_TouchEvents.cs`
手势分支」——这只说明**代码同源**，不等于**现象成立**。两件事要分开。

### C3 → 不修（链路已走完，无法复现）

§二 当时写「部分覆盖：今天刚加的 `SlidesCount` / `Count==0` 正好是这条路径」。
实际读完链路后发现这个判断不成立——「进入放映」根本**不看**演示文稿内容：

| 路径 | 位置 | 判据 |
|---|---|---|
| COM 事件 | `PptBridge.cs:1239` 订阅 → `:1274 OnComSlideShowBegin` | COM 抛了 SlideShowBegin |
| 轮询兜底 | `PptBridge.cs:793` | `_pendingInShow` 连续稳定 3 次（500ms×3） |

`OnPPTSlideShowBegin`（`MW_PPT.cs:922`）内部**没有任何内容判定**，
拿到事件就往下走，最多页码取不到时落到 0。

所以空演示文稿（1 页空白）按现有判据**本来就该触发**，页码显示 `1 / 1`。
社区说的「空 ppt」究竟指哪种情形，没有复现手段之前不能改——
改错了会把正常联动一起挡掉。

所有者 2026-11-11 22:38 裁决：不修。

### C4 → 已修（`f1b71fe7`），根因不是 §二 猜的那个

§二 当时猜「`RefreshSlideShowState` 的 `nowInShow && !_inSlideShow` 分支」。
**猜错了。**

真正的线索在社区原话的后半句：「**关闭长按翻页修复**」——这是用户自己
试出来的规避方法，它把范围直接钉死在长按定时器上。

沿着这条线查到的：

```
OnLongPressTimerTick(MW_PPT.cs:535)
  → 每 LongPressInterval(50ms) 调一次
  → BtnPPTSlidesDown_Click → Task.Run 冲 COM
```

而 `OnPPTSlideShowEnd`（`MW_PPT.cs:1300`）用大括号配对切出整个函数体，
共 **247 行**，统计：

| 符号 | 出现次数 |
|---|---|
| `StopLongPressDetection` | **0** |
| `_longPressTimer` | **0** |

**退出放映的整条路径从来没有停过长按定时器。**

于是按住翻页按钮时退出放映 → 定时器还在跑 → 每 50ms 碰一个正在销毁的
COM 对象 → PPT 卡死。这精确解释了「关闭长按翻页就好了」。

修法：退出放映第一件事 `StopLongPressDetection()`，
并 `MarkAllNavBarsLongPressConsumed()` 清标记。

### C10 → 不修（两个原怀疑对象都已排除）

§二 当时归到「`WS_EX_NOACTIVATE` 悬浮窗拦截器」。实际查下来：

1. **悬浮窗拦截器**——排除。
   - `FloatingWindowInterceptorSettings.IsEnabled` 默认 `false`（`Settings.cs:1073`）
   - 它只操作**别人的**窗口（`ShowWindow` / `SetWindowPos`），自己不创建 UI 窗口。
     早先 grep 到 23 处 `new Window` 是误读——实际全是 `new WindowInfo` /
     `new WindowRestoredEventArgs` 这类数据对象。

2. **主窗口 WindowChrome 命中判定**——排除。
   `MW_WindowChromeHitTest.cs:27` 的 `ShouldHandleWindowChromeHitTest` 是
   **白名单式**的：只有点落在 20 个具体控件矩形内才返回 `true`
   （`ContainsPoint` 还过滤 `!IsVisible` / `!IsHitTestVisible` / 尺寸为 0）。
   透明区域不会被当成可点区域。

剩余候选（折叠模式下 `SetTransparentHitThrough` 的状态机、其他 Topmost
子窗口的存活状态）都需要现场抓 dump 才能证。

所有者 2026-11-11 22:38 裁决：不修。

---

## 八、教训：三条「拿二手材料当一手证据」

本文件 §二～§六 里有多处判断是**基于推断而非实测**写下的，
当晚实际动手后有四条被推翻（C1 状态、C3 机理、C4 根因、C10 归属）。

同一天里同类问题还有两次：

1. **C1 排期**——依据是自己写的工作摘要，而摘要不是原始清单
2. **本文件「不存在」的误判**——我在 `icc-lts/docs/` 下查
   `COMMUNITY-BUGS.md`，没找到就断言「根本不存在」，
   实际它在 `src/docs/`（git 仓库根在 `src`，不是 `icc-lts`）
3. **图标名**——grep「仓库里用过 `Clock`」，命中的正是自己刚写进去那行

共性：**验证材料本身没有独立于被验证对象**。

以后写这份文档的规矩：
- 「已修」必须有 commit + 可复算的证据（行号、计数、复现步骤）
- 「不存在 / 不修」必须有所有者裁决或代码证明
- 推断阶段的排期判断，动过手之后要回来改，不能留着当结论


---

## 九、C8 / C9 的结论（2026-11-11 晚）

### C8 滑块拖不动 —— 已改，但**未经真机触屏验证**

社区原话「只能通过屏幕键盘来移动」这个细节给了方向：屏幕键盘发的是鼠标事件。

**查证过程中我撤回了两条自己的推断**：

1. 一开始说「触摸被提升成鼠标事件导致 TouchDown 被吞」——推断，没验证，撤回。
2. 接着说「设置窗口是独立线程，跨线程访问」——查了打开路径
   （`MW_FloatingBarIcons.cs:4362` 直接 `new SettingsWindow()`），**同线程**，撤回。

最后站得住的依据来自仓库里现成的注释。`TouchAwareDragDropHelper.cs`
的 `GripHandle_PreviewStylusDown` 写着：

> 禁用所有父级 ScrollViewer 的 IsManipulationEnabled，防止触屏移动被处理为 panning

而 `SliderTouchHelper.AddTouchSupportToAllSliders` 只遍历树找 Slider，
从来没管过它们外面裹着的 ScrollViewer。

**修法**（`c8634f9d`）：给 Slider 自己开 `IsManipulationEnabled` 的同时，
沿视觉树往上把所有父级 ScrollViewer 的该项关掉（附加属性记原值）。

**验证边界**：Build 0 error。**没有触屏设备，无法验证拖动是否真的变顺。**
这条基于同仓库已验证的同类做法 + 机制推理，不是实测通过。需要真机复验才算闭环。

若复验后仍拖不动，下一个怀疑点是 Slider 模板取不到 `PART_Track`，
导致回退算法里硬编码的 `thumbSize = 20` 与实际滑块尺寸不符 —— 那是独立问题。

### C9 「每次更新可能会重置设置」—— 在 LTSC 上**不成立**

D-01 已经把自动更新整个移除（`AutoUpdateHelper` 现在只剩占位成员，
`HandleUpdateModeStartup` 恒返回 false，所有联网/下载/替换文件的能力都删了）。

**没有自动更新，就不存在「更新导致的设置重置」。**

检查了实际会碰配置的 `CleanupObsoleteSettings`（`MW_SettingsToLoad.cs:742`）：
它的逻辑是拿当前版本的默认配置做 schema，**只删「默认配置里已经不存在的键」，
不删值**。设计是对的。

顺带发现一处不足（不影响 C9 结论，另记）：
`RemoveObsoleteProperties` 处理数组时有个前置条件——

    if (userArray.Count > 0 && defaultArray.Count > 0)
        if (userArray[0].Type == Object && defaultArray[0].Type == Object)

**默认数组为空时整个数组跳过检查**，用户数组里的过期字段永远清不掉。
这是「该清的没清」，不是「用户的值丢了」，和 C9 反方向。暂不改。

用户手动下载新版本覆盖安装导致设置重置，属于部署/打包问题（有没有把
`Configs/` 排除在覆盖范围外），不是这轮代码能解决的，留给 P6 交付时定。
