# WinUI3 迁移可行性核查（2026-11-17）

给未来接手这个任务的自己看。**先读这份，再动手。**

---

# 一、查证结论

## 系统兼容性：✅ 可行

官方 [Windows App SDK 支持页](https://learn.microsoft.com/en-us/windows/apps/windows-app-sdk/support) 原文：

> Although the Windows App SDK is currently **backward compatible to Windows 10 version 1809**,
> the Windows App SDK is supported by Microsoft only on Windows releases still in support.

1809 = build 17763。我们的目标框架是 `net11.0-windows10.0.19041.0`（Win10 2004），
**在兼容范围内。**

### 但有一个坑要说清楚

[issue #6254](https://github.com/microsoft/WindowsAppSDK/issues/6254)（2026-02-26 创建，**至今 open**）：

> `1.8.260209005` does not run on Windows 10 17763, and there is no error message.
> The same issue occurs on 19041 as well.
> `1.7.260208005` is normal.

**Windows App SDK 1.8 在 Win10 17763 / 19041 上静默启动失败。**

但这不是终点 —— 当前 SDK 已经到 **2.6.x**（2026-11-03 发布 v2.6.5）。
2.6.5 的 changelog 里还在修 Windows 10 的渲染/合成问题：

> Fixed an `E_NOINTERFACE` crash when enabling the System Composition Engine
> on Windows builds that lack the required interfaces.
> Fixed an issue where window backgrounds and GDI controls could become unexpectedly
> transparent on Windows 10, including apps hosting XAML islands.

**说明 2.x 仍支持 Win10。**

→ 落点：**用 2.6.x，不要用 1.8**。选定版本后必须在真机（教室白板）上跑一次空 WinUI3 项目，
确认能起来，再开始动工。

## 笔迹控件：✅ 有，但架构完全不同

WinUI3 **有 `InkCanvas`**（`Microsoft.UI.Xaml.Controls.InkCanvas`），
但我一开始以为它会像 WPF 那样「一站式」，查完发现不是。

## 完整 Inking 栈（`Microsoft.UI.Xaml.Controls` 下 532 个类里）

```
inkcanvas                     ← 控件本体
inkpresenter                  ← 真正干活的渲染层（InkCanvas 只有这一个核心属性）
inkstrokecontainer            ← 笔迹存储
inksynchronizer               ← 增量同步
inkinputprocessingmode
inkinputrightdragaction
inkhighcontrastadjustment
inktoolbar                    ← 现成的工具栏
inktoolbarpenbutton / eraserbutton / highlighterbutton
inktoolbarstencilbutton / rulerbutton / custompenbutton
inktoolbarinitialcontrols / toolbutton / togglebutton / flyoutitem
```

**这是好消息**：不用自己写笔迹引擎，微软提供了一整套。
**也是坏消息**：API 形状和 WPF 完全不同。

## WPF vs WinUI3 的笔迹模型差异

| | WPF | WinUI3 |
|---|---|---|
| 控件 | `InkCanvas` 自带一切 | `InkCanvas` 只是壳，核心属性只有 `InkPresenter` |
| 存储 | `StrokeCollection` | `InkStrokeContainer` |
| 编辑模式 | `InkCanvasEditingMode` 枚举 | `InkInputProcessingMode` + `InkPresenter` 配置 |
| 橡皮擦 | 内置 | 要配 `InkPresenter` 的 eraser 行为 |
| 工具切换 | 换 `EditingMode` | 改 `InkPresenter.InputDeviceTypes` / 处理模式 |
| 形状识别 | `IAWinFX` COM + `InkAnalyzer` | `InkRecognizerContainer`（UWP 那套，是**手写识别**不是**形状识别**） |

**最后一行是最大风险**：WPF 那边用 `IAWinFX` 的 `InkAnalyzer` 做「墨迹转形状」，
WinUI3 栈里的 `InkRecognizerContainer` 面向文字识别，不是几何形状。

→ **形状识别这一块需要单独找方案**，可能要继续保留 `IAWinFX` 的 IPC 子进程
（我们现在已经是这个架构：`IpcIACoreClient` + `InkCanvas.IACoreHelper.exe`），
**换成 WinUI3 不影响这个 IPC 隔离设计**，只要进程间通信还在。

## 项目侧的依赖面

```
System.Windows.Controls.InkCanvas / InkCanvasEditingMode / StrokeCollection   516 处
涉及文件                                                                   63 个
```

数量看着吓人，但都是**同一个 API 族**，集中在绘制层，不是散落在业务逻辑里。
真正要重写的是这一层封装。

---

# 二、我的判断（写下来提醒自己）

**换 WinUI3 值得做，但顺序不能反。**

## 错误顺序（会做死）

```
先换库 → 在还看不懂的结构上，同时换运行时 + 换 UI 框架 + 换笔迹引擎
        → 三个变量一起动，出问题查不出是谁的问题
```

## 正确顺序

**第 0 步：验证地基（1 天）**
- 用 Windows App SDK **2.6.x** 建一个空的 WinUI3 项目
- 在教室白板上跑起来，确认不是 issue #6254 那个静默失败
- 放一个 `InkCanvas` + `InkPresenter`，用触控笔写几个字，确认笔迹跟手
- 这一条不通，后面全是空谈

**第 1 步：拆结构（不动库，1-2 周）**
- `MW_FloatingBarIcons.cs` 6281 行拆成：光标 / 穿透 / 动画 / 菜单 / 图标
- 触摸逻辑三套并行（`SliderTouchHelper` / `TouchAwareDragDropHelper` / `MainWindowSettingsHelper`）收口成一个
- 窗口置顶三处分散统一到 `WindowTopmostManager`
- `MainWindow` 两万多行抽服务

判据：改一个功能时，能说出「只需读哪几个文件」。

**第 2 步：抽绘制层接口（1 周）**
把「笔迹怎么画、怎么擦、怎么识别形状、怎么序列化」抽成接口，WPF 先实现一份。
目标：业务逻辑不直接碰 `StrokeCollection`。

**第 3 步：换实现**
新建 WinUI3 项目，实现第 2 步的接口。业务逻辑几乎不动。

---

# 三、必须先查清楚的几件事（动手前）

1. **2.6.x 在 Win10 19041 上能不能起来**
   建空项目实测。issue #6254 说的是 1.8，但 2.x 没人保证。
2. **`InkCanvas` 的触摸跟手延迟**
   教室白板上，笔迹延迟超过 ~30ms 老师就会觉得「不顺」。要用真机测。
3. **形状识别怎么办**
   `IAWinFX` 能不能继续用？它是 COM 组件，和 UI 框架无关，
   理论上 WinUI3 里也能通过 IPC 调用。但 `InkAnalyzer` 需要 `Ink` 对象，
   而 WinUI3 的是 `InkStrokeContainer` —— 两者对象模型不同，需要转换层。
4. **PPT 联动**
   现在用 `Microsoft.Office.Interop.PowerPoint`（COM），这和 UI 框架无关，能继续用。
5. **打包方式**
   WinUI3 推荐 MSIX 打包。但我们要的是「解压即用」的绿色包。
   需要确认 unpackaged 部署（`WindowsPackageType=None`）可行。

---

# 四、对照数据（已测，别重复测）

## iNKORE 内存泄漏

上游 [issue #438](https://github.com/iNKORE-NET/UI.WPF.Modern/issues/438)：

> When using `WindowHelper.UseModernWindowStyle="True"` on a Window,
> **the window is kept alive forever on memory even after it is closed.**

我们 8 个窗口全开了这个属性。修复在 v0.11.0（2026-10-28）。

## 升级前后实测（压力测试：反复构造 24 个设置页）

| 项 | 0.10.2.1 | 0.11.0 |
|---|---|---|
| 3 轮后内存 | 20→149MB（+129） | 20→110MB（+90） |
| 每轮均摊 | +43MB | **+30MB** |
| 6 轮后 | 未测 | 196MB |
| 句柄 | +20 | +21 |
| 3 轮耗时 | 15212ms | 15696ms |

**30MB/轮仍在，线性增长，不是缓存。** 这条没解决。

## 项目规模

```
C#   111,364 行 / 343 文件
XAML  14,651 行 /  68 文件
```

最大的几个：

| 文件 | 行数 |
|---|---|
| `MainWindow_cs/MW_FloatingBarIcons.cs` | 6281 |
| `MainWindow.xaml.cs` | 3312 |
| `MainWindow_cs/MW_PPT.cs` | 3203 |
| `MainWindow_cs/MW_SimulatePressure&InkToShape.cs` | 3121 |
| `MainWindow_cs/MW_ElementsControls.cs` | 3094 |

---

# 五、我这一阶段最容易犯的错（写给自己）

## 错误模式一：拿最像的东西当判据

- #48：把「页面有问题 + 要求触摸可用」读成「页面滚不动」，查了一上午
- #49：用 `Page.Title` 比页面标题，得出 7 处误判差点全删

**共同点：证据和结论之间没有因果链，只有「看起来像」。**

判据：动手前能说出「这句话由哪一行代码渲染 / 这个功能由哪个方法驱动」。说不出来就是还没查清。

## 错误模式二：拿新发现否定已观察事实

用户说「之前就能滚」，我查到 `_isPointerStackEnabled = False` 就判定「所以不能滚」，
还改了上游反复权衡过的开关。

**已观察事实 > 我的推断。** 两者冲突时先怀疑自己。

## 错误模式三：批量改文件不看 git diff

Python `split('\n')` 把 CRLF 写成 LF，产生 3299/3388 行假 diff。

**判据：`git diff --numstat` 的增删行数和实际改动量对不上，就是整文件被重写了。**
改已有文件按字节操作，从 `git show HEAD:` 取原文。

---

# 六、动手前的检查清单

- [ ] Windows App SDK 2.6.x 空项目在教室白板上能起来
- [ ] `InkCanvas` 笔迹跟手延迟可接受
- [ ] 形状识别方案定了（继续 IAWinFX IPC / 换方案）
- [ ] unpackaged 部署能做出「解压即用」的包
- [ ] 第 1 步的拆分做完，能说清「改 X 只需读哪几个文件」
- [ ] 第 2 步的绘制层接口抽完，WPF 实现通过全部回归测试

**以上四条没打勾就不要开始第 3 步。**

---

# 七、当前仓库状态

```
分支      ltsc
HEAD      c3f00ec8
发布      sorrow-0.1a（GitHub Release 已发）
tag       本地 3 个（baseline-2026-11-11 / ltsc-1.7.19.7 / sorrow-0.1a）
          远端 1 个（sorrow-0.1a）
上游 tag  已清理（74 个全删）
```

相关文档：

| 文档 | 内容 |
|---|---|
| `docs/SORROW-AND-NEXT.md` | sorrow 定版记录 + 四阶段计划 |
| `docs/WINUI3-FEASIBILITY.md` | 本文件 |
| `docs/BUILD.md` | 构建、打包、验证流程 |
| `docs/LTSC-CHANGES.md` | 相对上游的全部改动 |
| `docs/FORK-BRANCHES-ARCHIVE.md` | 已删分支的 SHA |
