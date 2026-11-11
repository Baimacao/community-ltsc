# 社区 bug × 当前任务 合并表

> 目的：避免 P5 重复修已经做过的东西。
> 来源：`https://forum.smart-teach.cn/d/468-icc-ce-bugfan-kui-zhuan-lan`
> 建立时间：2026-11-11 15:10

---

## 一、重叠关系总览

| # | 社区原话 | 归属阶段 | 状态 | 与当前工作的关系 |
|---|---|---|---|---|
| C1 | 三指及以上触控时锁定缩放只能平移 | P2 | **同源，需一起验** | 和双指共用 `MW_TouchEvents.cs` 手势分支 |
| C2 | 退出 PPT 放映后自动隐藏 | P1 | **待诊断** | 原判「同双指」是错的；真嫌疑是全屏分支 `MW_Timer.cs:1297` |
| C3 | 空 PPT 放映不触发联动 | P1 | **部分覆盖** | 今天刚加的 `SlidesCount` / `Count==0` 正好是这条路径 |
| C4 | PPT 退出直接卡死 | P1 | **同源** | 退出路径与 `RefreshSlideShowState` 状态机同一处 |
| C5 | 点击按钮无限启动 ICC | P2 | 同源（崩溃链） | 崩溃自动重启，和 `App.xaml.cs` 崩溃处理相关 |
| C6 | 任务管理器退出仍重启 | P2 | 同源（崩溃链） | 同 C5，崩溃判定 |
| C7 | 右键重启按钮无限重启 | P2 | 同源（崩溃链） | 同 C5 |
| C8 | 透明度滑块无法拖动 | 独立 | 未动 | `SliderTouchHelper.cs`，与主触摸链不同文件 |
| C9 | 每次更新重置设置 | P3 | **同源** | 设置持久化，与设置树精简同一块 |
| C10 | 全屏无法触控或点击 | 独立 | 未动 | 悬浮窗拦截器（`WS_EX_NOACTIVATE` 那条） |

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
