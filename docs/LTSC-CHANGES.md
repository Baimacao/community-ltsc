# LTSC 改动清单（1.0a-ltsc）

> 记录本分支相对上游基线的**全部改动**。
> 用途：对外发布时让用户分清「这是上游的问题还是我们改的」；后期反哺上游时作为依据。

| 项 | 值 |
|---|---|
| 上游基线 commit | `083f694e`（CJK_mkp，2026-07-15，上游 version 1.0.0） |
| 当前版本 | 1.0a-ltsc |
| 分叉后提交数 | 133（whale 39 + ICC-LTSC 94） |
| 文件改动 | 343 个文件，+15018 / −43766 |
| 上游仓库 | https://github.com/InkCanvasForClass/community |
| 本分支仓库 | https://github.com/Baimacao/community-ltsc |

---

## 一、砍掉的功能

### 1.1 点名 / 随机抽选

**删了什么**：`RandomDrawPage` 整个页面（文件级删除）

**为什么**：干扰课堂。且原实现里计时器设置混在这个页面，砍掉后计时器得单独成页。

**留下的痕迹**：`Settings` 里还有个孤立的 `ShowRandomAndSingleDraw` 布尔字段，无页面引用。

### 1.2 数据上传 / 使用统计

**删了什么**：OOBE 里的遥测同意步骤、隐私协议页

**为什么**：核实过 `Helpers/DeviceIdentifier.cs` —— **零 `HttpClient`、零 `WebRequest`**，只写本地 `usage_stats.enc`。
既然没有任何上传行为，就不需要让用户同意。

> **未验证**：`usage_stats.enc` 是否纯粹本地留存、有没有其他路径外发，没做全量网络调用审计。

### 1.3 窗口渲染模式切换（WindowChrome）

**删了什么**：设置里的渲染模式切换项

**为什么**：稳定性问题。

### 1.4 其它

- 设置里的控件库 Demo 残留项
- 13 个已废弃的设置字段

---

## 二、修掉的问题

### 2.1 退出后残留后台进程（C3）

**现象**：主程序关了，`InkCanvas.IACoreHelper.exe` 还挂着。

**根因**：子进程没有父进程退出检测。

**改法**：加父进程看门狗，父进程退出即自我结束。

**验证**：任务管理器强杀主进程 → 确认 helper 跟着退。

### 2.2 跨线程访问 DynamicRenderer（C2）

**根因**：原判据依赖异常消息里的英文字符串匹配，换了语言就失效。

**改法**：改成语言无关的判据。

### 2.3 与上游共存时互相干扰（B1）

**现象**：同时装原版和 LTSC，任务栏图标合并、配置互相覆盖。

**改法**：
- `AppUserModelID` → `InkCanvasForClass.LTSC`
- 互斥体名 → `InkCanvasForClass CE LTSC`

**刻意没改**：`AssemblyName`（仍是 `InkCanvasForClass`）、进程名自检谓词。
改了会让「检测原版是否在运行」的逻辑失效。

### 2.4 触摸屏无法自然滚动设置页

**根因**：`SettingsWindow.xaml` 的 `NavigationView` 内部 `ScrollViewer` 先吞了触摸事件。
页面自己虽然有 `PanningMode`，但外层没设。

**改法**：给外层 `NavigationView` 补 `ScrollViewer.PanningMode="VerticalFirst"` + `PanningDeceleration`。

**教训**：查「谁吃掉了输入」要从外往里查，不是从里往外。

### 2.5 PPT 模式退出后残留透明条

**根因**：`GridTransparencyFakeBackground` 的 opacity 和命中测试状态没复位。

**改法**：`ResetPptTransparencyOverlay(reason)`，幂等，挂在两处退出路径上。

---

## 三、体验改动

### 3.1 OOBE 重写（7 步 → 3 步）

| | 上游 | 本分支 |
|---|---|---|
| 步骤数 | 7 + 欢迎页 + 完成页 | 3 步 + 欢迎页 + 完成页 |
| 首屏 | 单栏文字 | 双栏（左标题按钮 / 右亮点卡片） |
| 反馈入口 | 无 | 智教联盟 + GitHub |
| 跳过 | 无 | 有（应用当前值，不重置） |
| 入场动画 | 整体淡入 | logo 弹 → 标题升 → 卡片错落 |

**删掉的步骤**：遥测同意、PPT 设置、自动化设置、点名设置。

**为什么**：这些设置项在「设置」里都能改，OOBE 不该全走一遍。老师第一次打开软件是为了能用，不是为了配置。

### 3.2 弹簧动画引擎

**改了什么**：`AnimationsHelper` 里 18 处动画从 Storyboard 换成交互式弹簧（模型移植自 codmtune）。

**为什么**：Storyboard 的缓动曲线是死的，连续操作时会打断、跳帧。弹簧能打断并平滑接上。

**刻意没换**：`InkFadeManager`。
它的 Storyboard 用时钟驱动，最小化后仍然推进；弹簧走 `CompositionTarget.Rendering`，窗口最小化就不走了。
换掉会导致笔迹淡出不完整、内存泄漏。**这是有意的例外，不是漏改。**

### 3.3 设置页结构重整

- 导航按授课场景重新分类
- 「画板」页拆干净，6 项按性质迁到该去的地方
- 调试项收进折叠组
- 14 条开发文档式的说明改成老师说的人话
- 「画板与笔迹」拆掉单子项分组

### 3.4 中文化（i18n）

设置页硬编码中文 → resx，共 **109 条**，分四批完成。支持中英切换。

### 3.5 品牌区分

- 独立版本序列 `1.0a-ltsc`（绕过 Nerdbank.GitVersioning，显式设 `AssemblyVersion`/`FileVersion`）
- `Title`/`Product` → `InkCanvasForClass LTSC`
- 独立图标（`iccltsc-light` / `iccltsc-dark`），深浅跟随主题
- 关于页加本分支仓库链接 + 「重新引导」入口
- 文案全面区分 ICC LTSC 与上游 ICC-CE

---

## 四、刻意没做的

| 项 | 为什么 |
|---|---|
| **不裁剪（PublishTrimmed）** | WPF 反射绑 XAML，裁剪后是运行时崩，不是编译期报错。老师课堂上才会发现 |
| **不换 InkFadeManager** | 见 3.2，弹簧在最小化时不推进 |
| **不改 AssemblyName** | 会让原版检测失效 |
| **不做安装程序** | 推后，当前先保证程序本体 |

---

## 五、已知未验证项

以下写的是**没有运行证据**的，不当已通过：

- `usage_stats.enc` 是否纯本地（没做全量网络审计）
- 教室真实设备上的长时间稳定性（缺现场日志）
- 极端低配机器（核显 + 4GB）的表现

---

## 六、反哺上游的候选

按「通用性 × 收益」排序，这些改动与 LTSC 定位无关，上游用户也会受益：

| 改动 | 通用性 | 说明 |
|---|---|---|
| 触摸屏滚动修复 | 高 | 一行属性，任何用 NavigationView 的窗口都受益 |
| 跨线程判据语言无关 | 高 | 修的是国际化缺陷 |
| IACoreHelper 父进程看门狗 | 高 | 任何场景都需要 |
| PPT 透明遮罩复位 | 高 | 纯 bug 修复 |
| 弹簧动画引擎 | 中 | 收益明确，但改动面大，需要上游评估 |
| OOBE 重写 | 低 | 与 LTSC「只为课堂」的定位绑定，上游不一定要 |
