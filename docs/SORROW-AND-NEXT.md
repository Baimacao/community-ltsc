# 0.1a sorrow 定版与后续重构计划

写于 2026-11-17 15:15 · 提交 `1ffbbd8e`

---

# 一、sorrow 定版

`0.1a` 到此为止，打为 **sorrow** 版本，之后开始大改。

产物：`dist/ICC-CE-Lite-sorrow-0.1a.7z`（19 MB，10 个程序文件 + 说明）

## 定版前的状态

| 项 | 值 |
|---|---|
| 提交 | `1ffbbd8e` |
| 版本号 | `0.1a+1ffbbd8` |
| 编译 | 0 错误 |
| 四语 | 1311 键一致 |
| 冒烟自测 | PASS=17 FAIL=0 |
| 设置页构造 | 24/24 |
| 干净目录启动 | 单实例、0 Error、0 崩溃 |

## 定版前最后两批修复

| 提交 | 内容 |
|---|---|
| `918b3650` | 删 6 处「分类标题 == 紧邻卡片标题」的重复 |
| `1ffbbd8e` | 删 3 处「页面顶部标题 == 内容首个分类」的重复 |

判断依据：导航项 `Content` 的文案 vs 页面首个 `SettingsSectionHeaderTextBlockStyle` 的文案。
**不能用 `Page.Title`** —— 那是 WPF 元数据属性，不渲染到界面。

保留的 4 处（首页 3 处 + 白板外观的「信仰の源」）是「分组名 + 快捷跳转卡」，
卡片带 `QuickNavCard_Click`，不是重复。

## 已知未解决（带进下一阶段）

1. **自动化不会自动触发**。手动触发走 Action 链正常，按条件自动触发不工作。
   用户建议：重写为 ClassIsland 的逻辑，然后自己造一个进程做触发测试。
2. **翻页笔实物按键未经真机验收**。代码层验过（吞键语义、按需注册热键、配对落盘），
   五个键（B/ESC/Shift+F5/Ctrl+P/E）的实际效果需要老师真机按一遍。

---

# 二、为什么大改：阻力诊断

用户问「阻力是代码石山还是 UI 库不行」，查证结论：

## 代码石山（根本原因）

```
C#   111,364 行 / 343 个文件
XAML  14,651 行 /  68 个文件
```

| 文件 | 行数 |
|---|---|
| `MainWindow_cs/MW_FloatingBarIcons.cs` | 6281 |
| `MainWindow.xaml.cs` | 3312 |
| `MainWindow_cs/MW_PPT.cs` | 3203 |
| `MainWindow_cs/MW_SimulatePressure&InkToShape.cs` | 3121 |
| `MainWindow_cs/MW_ElementsControls.cs` | 3094 |
| `MainWindow_cs/MW_ShapeDrawing.cs` | 3009 |
| `MainWindow_cs/MW_TouchEvents.cs` | 2409 |
| `App.xaml.cs` | 2131 |

具体代价：

- 改 PPT 页码逻辑要先翻完 3203 行找入口，链路横跨 3 个文件
- `IsInPPTPresentationMode` 在 6 个文件里有 20+ 个读取点
- 窗口置顶逻辑分散在 `MainWindow.xaml`、`SettingsWindow`、`WindowTopmostManager` 三处
- 触摸相关三套并行：`SliderTouchHelper`、`TouchAwareDragDropHelper`、`MainWindowSettingsHelper`

## iNKORE（放大器，不是根源）

```
iNKORE.UI.WPF.Modern  0.10.2.1   编译于 2025-11-18
iNKORE.UI.WPF         1.2.8      编译于 2024-10-24
提供的目标框架：net452 / net6.0-windows7.0 / net6.0-windows10.0.18362
我们在用：      net11.0-windows10.0.19041.0
```

**它在 net11 上跑的是 net6 兼容目标。**

具体坑：

| 现象 | 归属 |
|---|---|
| `SettingsCard` 设 `BorderBrush` 不生效 | iNKORE（模板不暴露） |
| `NavigationView` 上设 `ScrollViewer.PanningMode` 语义不明 | iNKORE（附加属性能否传入取决于模板） |
| 触摸拖拽要用 `TouchAwareDragDropHelper` 手写 | iNKORE + gong-dragdrop 都不管 |
| 预设窗口 60 个原生控件 | 我们的（当初没统一） |

上游自己也在跟这些问题搏斗：`EnablePointerSupport` 一周内删→加回→再注释三次。

## 判断

**先动结构，再换库。** 换库是几周工作量，换完代码石山还在；
但如果只动结构不换库，iNKORE 的坑还能靠封装挡掉。

---

# 三、下一阶段计划

## 阶段 0：验证前提（半天）

不写代码，先确认几件事，避免按错假设开工：

1. **iNKORE 的坑里，哪些是我们的用法问题**
   查它的开源仓库和文档。重点三个：
   - `SettingsCard` 的选中态正确的做法是什么
   - `NavigationView` 的触摸滚动应该配在哪
   - `NavigationViewItem` 文字裁切是宽度不够还是模板问题

2. **换库候选的可行性**
   评估方向（待定，需要用户拍板）：
   - 继续 iNKORE，升到最新版并自己做封装层
   - 换 WPF UI（lepo.co 那套 Fluent）
   - 换 Avalonia（跨平台，但 WPF 特性要重写，风险最大）
   - 保留原生 WPF 控件 + 自己写样式

3. **自动化为什么不自触发**
   定位断点：Trigger 侧的评估时机。手动能走通说明 Action 链没问题。

## 阶段 1：拆代码石山（最大块）

不动 UI 库，先把结构理出来。

| 目标 | 做法 |
|---|---|
| `MW_FloatingBarIcons.cs` 6281 行 | 按职责拆：光标 / 穿透 / 动画 / 菜单 / 图标 |
| 触摸逻辑三套并行 | 收口到一个 `TouchInputService` |
| 窗口置顶三处分散 | 统一到 `WindowTopmostManager` |
| `MainWindow` 两万多行 | 抽服务：PPT、墨迹、工具栏、自动化各成独立服务 |

判据：改一个功能时，能说出「只需读哪几个文件」。

## 阶段 2：UI 库决策与封装

阶段 0 的结论出来后定。

无论换不换，都要做的一件事：**给用到的每个 iNKORE 控件写一个薄封装**，
把踩过的坑固化进去（选中态怎么设、PanningMode 怎么配、文字裁切怎么防）。

## 阶段 3：现代化

- 异步化：`async void` 全部换掉（现在有 6 处）
- 空 catch 清理（现在有若干处静默吞异常）
- 硬编码值挪进设置
- 单一数据源：消除重复的状态判断

## 阶段 4：回归与发布

沿用现有的验收标准：

- 编译 0 错误
- 四语 1311 键一致
- 冒烟自测 FAIL=0
- 24 个设置页构造成功
- 干净目录启动 0 Error
- 暴力测试（连点、乱切、快速开关）
- 触摸全流程可操作

---

# 四、这一阶段吃过的亏（带进下一阶段）

| # | 教训 |
|---|---|
| #48 | **用户报「某功能有问题」时，先确认这个功能到底有没有坏，再动手查。** 我把「页面有问题」+「要求触摸可用」读成「页面滚不动」，查了一上午，还改错了 `EnablePointerSupport` 开关。 |
| #49 | **查 UI 文案前先确认这串字显示在哪行代码里。** 用 `Page.Title` 比页面标题得出 7 处误判，实际标题来自 `NavigationViewControl.Header = selectedItem.Content`。 |
| #36 | **改已有文件按字节操作。** Python `split('\n')` 把 CRLF 文件写成 LF，产生 3299/3388 行假 diff。用 `git show HEAD:` 取原始字节，`bytes.split(b'\r\n')`。 |
| — | **机制验证 ≠ 产品验证。** 证明某个 API 有某种效果后，还要问产品在什么条件下才调用它。 |
| — | **有反向对照才算验证。** `0 条 ManipulationStarting` 被我当成「不能滚」的证据，但从没验证过正常滚动时它该有什么。 |

---

# 五、当前仓库状态

```
分支    ltsc
HEAD    1ffbbd8e
远端    https://github.com/Baimacao/community-ltsc
默认分支 ltsc（已从 net10 改过来）
分支数  1（上游 10 条已删，SHA 归档在 docs/FORK-BRANCHES-ARCHIVE.md）
```

文档索引：

| 文档 | 内容 |
|---|---|
| `docs/BUILD.md` | 构建、打包、验证流程 |
| `docs/LTSC-CHANGES.md` | 相对上游的全部改动 |
| `docs/MASTER-PLAN.md` | 历史计划与执行记录 |
| `docs/FORK-BRANCHES-ARCHIVE.md` | 已删分支的 SHA 与恢复方法 |
| `docs/SORROW-AND-NEXT.md` | 本文件 |
