# PPT 联动失效诊断（2026-11-19 班测报告）

**报告**：联动状态下时间一长，PPT 翻页组件消失，PPT 联动模式异常
**配置**：教师预设
**日志**：`Log_2026-11-19-11-18-27.txt`（11:18:27 – 12:11:54，共 53 分钟，225 行）

---

## 一、结论

翻页组件消失不是组件本身的问题，是 **`SlidesCount` 被一次 COM 失败永久写成 0**。

翻页栏的显示闸门在 `PPTUIManager.UpdateNavigationPanelsVisibility()`：

```csharp
bool shouldShowButtons = ShowPPTButton &&
                      _mainWindow.IsInPPTPresentationMode &&
                      isInSlideShow &&
                      hasValidPageCount &&        // ← SlidesCount > 0
                      !Settings.Automation.IsAutoFoldInPPTSlideShow;

if (!shouldShowButtons)
{
    HideAllNavigationPanels();   // ← 整个翻页栏消失
    return;
}
```

四个条件里，**只有 `hasValidPageCount` 这一条会被一次失败永久破坏**，其余三条都是跟随真实状态。

---

## 二、失败是怎么发生的

### 2.1 起点：COM 事件注册失败，全程靠轮询

日志第 69 行：

```
11:19:21 [PptBridge.HookComEvents] PptBridge COM 事件注册失败，改用轮询:
         消息筛选器显示应用程序正在使用中。(0x8001010A (RPC_E_SERVERCALL_RETRYLATER))
```

这是决定性的前提。`RPC_E_SERVERCALL_RETRYLATER` = Office 的 COM 服务端当时忙，
拒绝事件订阅。**这个状态一旦发生，本次运行内所有 COM 事件都不会触发。**

### 2.2 `RefreshSlidesCount` 把失败当成 0

`PptBridge.cs:1043`：

```csharp
void RefreshSlidesCount()
{
    int newCount = 0;
    try
    {
        if (_comApplication != null)
        {
            dynamic app = _comApplication;
            pres = app.ActivePresentation;
            if (pres != null) newCount = (int)((dynamic)pres).Slides.Count;
        }
    }
    catch (Exception ex)
    {
        newCount = 0;                                    // ← 拿不到就当成 0
        VerboseLog($"[PPT详细] SlidesCount 取失败: ...");  // ← 详细日志关着时不写
    }

    int oldCount = SlidesCount;
    SlidesCount = newCount;                              // ← 无条件覆盖
    ...
}
```

**两个问题叠在一起：**

1. **异常兜底写 0**——「读不到」和「真的是 0 页」被当成同一件事。
   实际页数没变，但 `SlidesCount` 变成 0，`hasValidPageCount` 立刻为 false。

2. **唯一的失败记录被开关挡住**——`VerboseLog` 的前置条件是：

   ```csharp
   if (adv == null || !adv.IsLogEnabled || !adv.IsVerbosePptLogEnabled) return;
   ```

   而 `IsVerbosePptLogEnabled` 默认 `false`（`Settings.cs:1049`）。
   **教师预设里没有这一项，所以整条失败过程一行日志都不留。**

### 2.3 修复入口只在 `<= 0` 时触发，形成自我失败循环

`PptBridge.cs:855`（轮询路径）：

```csharp
if (SlidesCount <= 0) RefreshSlidesCount();
```

一旦被写成 0，下一轮会再试；但 Office 的 COM 已经处于 `RETRYLATER` 状态，
再试还是失败，还是写 0。**越失败越重试，越重试越失败。**

另一条路——COM 事件回调里的 `RefreshSlidesCount()`（`OnComNextSlide` /
`OnComPresentationOpen` / `OnComPresentationClose`）——**在 2.1 的前提下一次都不会执行**。

### 2.4 为什么是「时间一长」而不是「一开就坏」

三个条件随着时间的推进会先后成熟：

| 时间点 | 会发生什么 |
|---|---|
| 刚连上 | `HookComEvents` 可能成功，也可能失败（日志里第一次就失败了） |
| 连续用 10+ 分钟 | Office 的 COM 服务开始周期性忙，`InvokeMember` 抛 `RPC_E_SERVERCALL_RETRYLATER` |
| 某一次失败 | `SlidesCount` → 0 → `hasValidPageCount` = false → 翻页栏隐藏 |
| 之后每一轮 | `if (SlidesCount <= 0)` 再试，仍失败 → 保持 0 → **永不恢复** |

**关键点：翻页栏隐藏之后，没有任何代码会把它重新显示出来。**

`UpdateNavigationPanelsVisibility()` 只在两处被调用：
- `UpdateSlideShowStatus(true, ...)` 里 → 需要一次新的「放映开始」事件
- `MoveWindow` 之后的 `ApplicationIdle` 回调里 → 也在放映开始分支内

而 COM 事件已死，轮询侧的 `SlideShowBegin` 只在**状态从 false 翻到 true** 时触发一次
（`else if` 分支，`PptBridge.cs:955`）。状态一直是 true 就不会再触发。
**所以它一旦消失，就只能退出放映再重新进入才能恢复。**

---

## 三、日志里的直接证据

### 3.1 两个状态位不同步

```
[自动收纳] 全屏=False 需收纳=False 已收=False 放映中=True 放映模式=False 用户展开=True
```

- `放映中` = `PPTManager.IsInSlideShow`（PptBridge 的 `_inSlideShow`）
- `放映模式` = `IsInPPTPresentationMode`（PPTUIManager 置的独立位）

这两个位由**两条不同的代码路径**更新，会各自漂移。
表里第 23 行就是这个中间态——翻页栏此时已经被隐藏了。

### 3.2 41 分钟里没有任何 PPT 事件

```
11:29:12  COM 事件已注册 / 已连接 PowerPoint / PPT连接已建立
11:38:09  键盘钩子已挂载
12:10:17  键盘钩子已卸载
```

从 11:29:12 到 12:10:17，**`SlideShowEnd` 一次没有，`ResetPPTStateVariables` 一次没有**。
对照 11:22:52 那次正常退出，日志是齐全的：

```
11:22:52  PptBridge 断开连接：PowerPoint 进程已退出
11:22:52  PPT连接已断开
11:22:52  PPT状态变量已重置
```

### 3.3 每一次放映都走同一个「进入→退出」循环

```
帧1  放映中=False 放映模式=True   ← PPTUIManager 先置位，PptBridge 还没跟上
帧2  放映中=False 放映模式=False
帧3  放映中=True  放映模式=True   ← 正常放映
...
帧N  放映中=True  放映模式=False  ← 翻页栏已隐藏，但桥还认为在放映
帧N+1 放映中=False 放映模式=False
```

这个循环在 53 分钟里重复了至少 6 次。**每一次都是一次翻页栏消失。**

---

## 四、要修的四件事

按优先级，前两条是这次班级报告的直接原因。

### 修 1：`RefreshSlidesCount` 不许把失败当 0

**现状问题**：`catch { newCount = 0; }` 把「读不到」和「0 页」混为一谈。
**判据**：读失败时应该**保留上一次的有效值**，不是清零。

```csharp
catch (Exception ex)
{
    // 读不到 ≠ 0 页。保留旧值，并强制记日志（不受详细日志开关限制）。
    LogHelper.WriteLogToFile(
        $"PptBridge 读取总页数失败，保留上次值 {SlidesCount}: {ex.GetType().Name}: {ex.Message}",
        LogHelper.LogType.Warning);
    SafeRelease(pres);
    return;                       // 直接返回，不写 SlidesCount
}
```

### 修 2：状态被破坏的日志不许静默，详细开关也要有界面入口

**现状问题（两个，分开说）：**

一、\`IsVerbosePptLogEnabled\` 默认 false，**而且界面里根本没有开关能改它**。
所以 \`SlidesCount 取失败\`、\`View 取不到\`、\`SlideShowWindows.Count=0\`
这些关键诊断全部静默，出问题时谁也开不了。

二、即使打开了详细日志，把「状态被破坏」和「每 500ms 的追踪」混在同一档，
也是不对的——前者必须始终可见。

**判据**：区分两类日志——

- 「每 500ms 的详细追踪」→ 归详细开关管（该关就关，避免日志膨胀）
- 「状态被破坏 / 转换失败 / 判据不成立」→ **必须记**，用 \`Warning\`，
  不受详细开关约束

这是「不许写静默错误」的直接要求：错误发生时得有人能知道。

**已修**：\`SlidesCount\` 读失败改成 \`LogHelper.LogType.Warning\`（已经不受开关管），
另外在高级设置里补上了详细日志的界面开关（见 5.2）。

### 修 3：隐藏翻页栏之后要有恢复路径

**现状问题**：`HideAllNavigationPanels()` 是单向的。条件一旦不满足就隐藏，
而重新显示依赖一次新的事件，事件已死就不会再来。

**判据**：`SlidesCount` 恢复成 > 0 之后，必须重新评估一次可见性。

在 `RefreshSlidesCount` 的赋值之后补：

```csharp
bool wasInvalid = oldCount <= 0;
...
SlidesCount = newCount;
if (wasInvalid && newCount > 0)
{
    // 页数从「无效」恢复到「有效」，翻页栏之前被 HideAllNavigationPanels 隐藏过，
    // 必须重新评估，否则要等下一次放映开始才回来。
    _mainWindow?.PPTUIManager?.UpdateNavigationPanelsVisibility();
}
```

（`PptBridge` 不持有 MainWindow 引用，需要用现有的事件或回调接一下，
具体接法在实现时定，不要新加静态依赖。）

### 修 4：两个状态位要收敛到一个来源

**现状问题**：`IsInSlideShow`（PptBridge）和 `IsInPPTPresentationMode`（MainWindow）
各自维护，会漂移成 `放映中=True 放映模式=False` 这种不可能状态。

**判据**：翻页栏的显示条件里，两个位**同时**参与判断——
一个说在放映、一个说不在，是自相矛盾，应该以「两者都成立」为准，
并且**当两者不一致时记一条 Warning**，让人看得见。

---

## 五、验证方法（修完必须做）

### 5.1 能不能稳定复现

现在无法在开发机上完整复现（需要真实 PowerPoint 放映 + 长时间）。
**但可以造出确定的失败条件**：

在探针里模拟 `RefreshSlidesCount` 读到 `ActivePresentation == null` 或抛异常，
看 `SlidesCount` 是否被写成 0、翻页栏是否隐藏。
**这是修 1 的反向对照：修复前必红，修复后必绿。**

### 5.2 详细日志的入口在「高级设置」，不在预设

**上一版这里写错了**，写的是「给教师预设加 \`IsVerbosePptLogEnabled = true\`」。
ta 直接指出：这个开关应该在高级设置里，而不是预设。

错在哪：

- 教师预设（\`Resources/DefaultConfigs/Settings.json\`）里本来就有
  \`"isVerbosePptLogEnabled": false\`——**它一直是关的，而且是故意的**。
  这是排查用的临时开关，会让日志量暴增，不该作为教师配置的一部分。
- 真正的问题是**它根本没有界面入口**。代码里读它（\`PptBridge.VerboseLog\`）、
  预设有它的值，但设置页面从头到尾没有一个控件能改它。
  结果是：出问题时用户没有任何办法打开它，日志里永远是空的。

**已修**：高级设置 → 日志 分组里加了「PPT 联动详细日志」开关，
位置在「记录日志」「日志以日期保存」下面。

\`\`\`
AdvancedPage.xaml        新增 LabeledSettingsCard x:Name="ToggleSwitchIsVerbosePptLogEnabled"
AdvancedPage.xaml.cs     加载时读 settings.Advanced.IsVerbosePptLogEnabled
                         Toggled 时写回 + SaveSettingsToFile
AdvancedStrings.*.resx   四语新增 VerbosePptLog / VerbosePptLogDesc
AdvancedStrings.Designer.cs  对应属性
\`\`\`

**验证**（\`--smoke-test\` 里的 VLOG-1..6）：

\`\`\`
VLOG-1 高级设置页存在                            PASS
VLOG-2 XAML 里有这个控件                         PASS  类型=LabeledSettingsCard
VLOG-3 有写回处理函数（拨动能存盘）              PASS
VLOG-4 能找到 VerboseLog 与 Advanced 设置        PASS
VLOG-5 开关关闭时不写 PPT 详细日志               PASS  正确静默
VLOG-6 开关打开时写 PPT 详细日志                 PASS  正确写入
\`\`\`

VLOG-5/6 是真调用：反射取 \`PptBridge.VerboseLog\`，分别关/开开关各调一次，
再读日志文件内容看那两条标记有没有出现。

**反向对照**：把 \`VerboseLog\` 里的开关判断去掉（等于开关失效、永远写），
VLOG-5 立刻变红（\`!! 关着还是写了\`），PASS=66 FAIL=1。**这条断言能证伪。**

### 5.3 真机验收

这条只有真机能验：连续放映 20 分钟以上，看翻页栏是否还在。
开发机上跑不了完整 PowerPoint 放映流程。

---

## 六、我读日志时差点犯的错（记下来）

第一遍 grep 的时候我看到 `退出放映热键注册失败（Escape）` 出现了 12 次，
差点当成问题——那是**已知的设计**（裸 ESC 的 RegisterHotKey 是系统级独占，
只在放映期间注册，退出即归还；这里报失败是因为放映期间被占用）。
它和翻页栏消失**没有因果关系**，只是同一时段一起出现。
**同期出现不等于因果。**
