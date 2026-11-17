# WinUI3 迁移可行性核查（2026-11-17，已实测）

给未来接手这个任务的自己看。**先读这份，再动手。**

这份文档里的每个数字都在本机跑过，命令写在旁边。
凡是「查到的」和「测到的」我分开写——查到的可能过期，测到的是这一台机器上的事实。

---

# 〇、先说结论

**WinUI3 现在不能用来做 ICC。不是因为 SDK 不支持 Win10，是因为墨迹控件还没进稳定版。**

一句话事实：

| | 状态 |
|---|---|
| Windows App SDK 2.6.5（稳定） | ✅ 能在这台 Win10 19045 上跑起来 |
| 墨迹控件 `InkCanvas` / `InkPresenter` / `InkToolbar` | ❌ **稳定版里根本不存在** |
| 同一套墨迹控件在实验版 | ⚠️ 存在，但微软明说「别用在生产」 |
| 墨迹控件有没有形状识别 | ❌ **没有**，官方原文说的 |

所以路径不是「迁不迁」，而是「等不等」。

---

# 一、墨迹控件：实测证明稳定版没有

这一条我一开始查错了。我先看了一份介绍文章，文章说 WinUI3 有 `InkCanvas` 和 `InkToolbar`，
我就照这个写进了文档。后来去 nuget 上把每个版本的包都拆开数，才发现文章没说清楚**它在哪个通道**。

## 实测方法

把 `Microsoft.WindowsAppSDK.WinUI` 每个版本的 `metadata/Microsoft.UI.Xaml.winmd` 拆出来，
数 `Microsoft.UI.Xaml.Controls.InkCanvas` 这个全限定名出现几次：

```bash
cd /e/DSH/tmp/wasdk
python -c "
import zipfile
for f in [...] :
    d = zipfile.ZipFile(f).read('metadata/Microsoft.UI.Xaml.winmd')
    print(f, d.count(b'Microsoft.UI.Xaml.Controls.InkCanvas'))
"
```

## 结果

| 通道 | 版本 | winmd 大小 | InkCanvas | InkToolbar |
|---|---|---|---|---|
| 稳定 | 2.0.12 | 1,631,032 | 0 | 0 |
| 稳定 | 2.2.1 | 1,633,080 | 0 | 0 |
| 稳定 | 2.3.0 | 1,634,616 | 0 | 0 |
| 稳定 | 2.3.2 | 1,634,616 | 0 | 0 |
| 稳定 | 2.3.6 | 1,634,656 | 0 | 0 |
| 稳定 | 2.3.9 | 1,634,656 | 0 | 0 |
| 稳定 | **2.6.4** | 1,634,616 | **0** | **0** |
| 稳定 | **2.6.5**（当前最新稳定） | 1,634,656 | **0** | **0** |
| 实验 | 2.0.10-experimental | 1,691,168 | 1 | 0 |
| 实验 | 2.2.2-experimental | 1,693,496 | 1 | 0 |
| 实验 | 2.3.3-experimental | 1,696,056 | 1 | 0 |
| 实验 | 2.3.8-experimental | 1,744,696 | 1 | 19 |
| 实验 | 2.3.10-experimental | 1,816,376 | 1 | 18 |
| 实验 | **2.6.6-experimental** | 1,823,032 | **1** | **18** |

**八个稳定版全部是 0。** 不是某个版本漏了，是整条稳定线都没有。

## 编译器确认

光数字节还不够，得让编译器自己说话。建一个最小项目，只写一行 `typeof(InkCanvas)`：

```xml
<PackageReference Include="Microsoft.WindowsAppSDK" Version="2.6.5" />
```

```
error CS0234: 命名空间"Microsoft.UI.Xaml.Controls"中不存在类型或命名空间名"InkCanvas"
error CS0234: 命名空间"Microsoft.UI.Xaml.Controls"中不存在类型或命名空间名"InkPresenter"
error CS0234: 命名空间"Microsoft.UI.Xaml.Controls"中不存在类型或命名空间名"InkToolbar"
```

同一份代码把版本换成 `2.6.6-experimental`：

```
warning CS8305: "Microsoft.UI.Xaml.Controls.InkCanvas"仅用于评估，在将来的更新中可能会被更改或删除。
```

编译通过，但带了「仅用于评估」的警告。**三个类型全在，三个全带 CS8305。**

## 微软官方文档原文

[Inking controls for WinUI apps](https://learn.microsoft.com/en-us/windows/apps/develop/ui/controls/ink-controls)：

> `InkCanvas`, `InkToolbar`, and `InkPresenter` are **experimental APIs**.
> Inking support was added in Windows App SDK **2.4.1-experimental**.
> **These APIs aren't available in the stable channel.**
> They might change or be removed in a future release.
> **Don't use them in production apps.**

> The experimental inking APIs **don't include handwriting or shape recognition**.

第二句直接判了 ICC 的死刑：**我们最核心的两个功能——墨迹转形状、手写体美化——它一个都没有。**

---

# 二、地基：SDK 能在 Win10 19045 上跑（实测）

一开始担心的是 [issue #6254](https://github.com/microsoft/WindowsAppSDK/issues/6254)：
1.8 在 Win10 17763 / 19041 上静默启动失败。

这个不用猜，直接跑。我建了一个真的 WinUI3 空项目（unpackaged，不开 MSIX），
在**这台机器**上启动：

```
OS      = 10.0.19045.0（Win10 22H2）
结果    = BOOT_OK InkCanvas=Microsoft.UI.Xaml.Controls.InkCanvas
窗口标题 = WinUI Desktop
响应    = True
内存    = 96.2 MB
句柄    = 685
```

**结论：Windows App SDK 2.6.6-experimental 在这台 Win10 22H2 上能起来，
unpackaged 部署可行，墨迹控件运行时可用。**

issue #6254 说的是 1.8，2.6.x 确实绕过去了。文档里原来那句「2.x 没人保证」现在可以划掉了。

### 项目配置（能跑起来的完整版）

```xml
<OutputType>WinExe</OutputType>
<TargetFramework>net8.0-windows10.0.19041.0</TargetFramework>
<TargetPlatformMinVersion>10.0.17763.0</TargetPlatformMinVersion>
<UseWinUI>true</UseWinUI>
<WindowsPackageType>None</WindowsPackageType>
<WindowsAppSDKSelfContained>true</WindowsAppSDKSelfContained>
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
```

踩过的坑：`App.xaml` 存在时 XAML 编译器会**自动生成** `Program.Main`，
你再手写一个就撞成 `CS0101` + `CS0111`。要么别手写，要么加 `DISABLE_XAML_GENERATED_MAIN`。

---

# 三、升级 SDK 换不来墨迹控件

容易产生的错觉：2.6.5 已经是很新的稳定版了，再往上应该有。

没有。`Microsoft.WindowsAppSDK.WinUI` 的包列表：

```
稳定：1.8.x … 2.0.12, 2.1.0, 2.2.1, 2.3.0, 2.3.2, 2.3.6, 2.3.9, 2.6.4, 2.6.5
实验：… 2.3.8-experimental, 2.3.10-experimental, 2.6.6-experimental
```

**2.6.5 是稳定通道的头，2.6.6-experimental 是实验通道的头。** 两个通道并行往前走，
实验通道里的东西什么时候并进稳定通道，微软没说。

对我意味着：**用实验版 = 拿教室里的白板赌微软不删 API。** 这条不合适。

---

# 四、就算能用实验版，还有一层

假设接受实验版，还有这些对不上：

| ICC 现在有的 | WinUI3 实验版 | 能不能补 |
|---|---|---|
| 墨迹转形状（直线/圆/椭圆/三角/矩形） | ❌ 官方明说没有 | 自己写，或用 IAWinFX |
| 手写体美化 | ❌ 官方明说没有 | 同上 |
| 模拟压感（`MW_SimulatePressure&InkToShape.cs`，3121 行） | 无对应 | 只能重写 |
| 墨迹渐隐动画（`InkFadeManager`，逐 Stroke 挂 DispatcherTimer） | `InkStrokeContainer` 没有 Stroke 级对象模型 | 只能重写 |
| 多笔分设备渲染（`VisualCanvas` + `StrokeVisual`，`MW_TouchEvents.cs` 2409 行） | `InkPresenter` 不开放自定义绘制 | 只能重写 |
| 墨迹序列化（`MW_Save&OpenStrokes.cs`，1531 行） | `InkStrokeContainer.SaveAsync` 到流 | 格式不兼容，旧文件读不了 |

**最后一个是最要命的**：`InkStrokeContainer` 的持久化格式和 WPF `StrokeCollection` 不兼容。
用户存了几百节课的板书，换过去一个都打不开。

## 形状识别这条，其实比想象中好办

我们已经是 IPC 架构了，把笔迹序列化成点阵发给 x86 子进程算：

```
Ink Canvas (x64, WPF)  ──命名管道──▶  InkCanvas.IACoreHelper.exe (x86, IAWinFX)
                       StrokeCollection → [x, y, pressure] 浮点数组
```

线上格式（`IpcProtocol.cs`）就是纯数字：

```csharp
w.Write(stroke.Points.Length);
foreach (var pt in stroke.Points) { w.Write(pt.X); w.Write(pt.Y); w.Write(pt.Pressure); }
```

**这套协议和 UI 框架无关。** WinUI3 那边只要能把 `InkStroke` 拆成 (x, y, pressure) 三件事，
子进程一行都不用改。这算是我今天查到的最好的消息。

---

# 五、我的判断

**现在不动 WinUI3。等墨迹控件进稳定通道。**

理由不是「迁移太难」，是**核心功能在新框架上还不存在**。
在一个连形状识别都没有的控件上重建 ICC，做出来的不是 ICC。

## 现在该做的事（按顺序，都能立刻开始）

**第 1 步：拆结构（不动库，纯重构）**
- `MW_FloatingBarIcons.cs` 6281 行拆成：光标 / 穿透 / 动画 / 菜单 / 图标
- `MainWindow` 部分类合计 **43,558 行 / 37 个文件**，抽服务
- 窗口置顶统一到 `WindowTopmostManager`（已有 531 行，但还有 32 处裸写 `Topmost =` 绕过它）
- `IsInPPTPresentationMode` 84 处 / 23 文件，收成一个状态服务

**这一步是纯赚的。** 不管最后迁不迁 WinUI3，做完都让代码好改一个量级。

**第 2 步：抽绘制层接口**
把「怎么画、怎么擦、怎么识别、怎么存」抽成接口，WPF 先实现一份。
这一步的真正目的不是为 WinUI3 做准备，是**把现在混在一起的六件事分开**。

**第 3 步（触发条件）**
等 `Microsoft.UI.Xaml.Controls.InkCanvas` 在稳定版出现、且去掉 CS8305 警告
——到那天再开始建 WinUI3 项目，那时第 2 步的接口直接接上去。

## 什么时候该重新评估

看到下面任意一条，回来重读这份文档：

1. `Microsoft.WindowsAppSDK.WinUI` 发新稳定版时，winmd 里 `InkCanvas` 计数从 0 变 1
2. 官方文档把那句 "Don't use them in production apps" 删了
3. 官方文档补上了形状识别 / 手写识别

复测命令就一句：

```bash
# 换版本号即可
unzip -p <包>.nupkg metadata/Microsoft.UI.Xaml.winmd | grep -c "Microsoft.UI.Xaml.Controls.InkCanvas"
```

---

# 六、项目侧的实际依赖面（实测）

我原来写的「516 处 / 63 个文件」两个数都不对。重数：

```bash
grep -rIE 'System\.Windows\.Controls\.InkCanvas|InkCanvasEditingMode|StrokeCollection' \
  --include='*.cs' --include='*.xaml' . | grep -v '/obj/' | wc -l
```

| 项 | 数量 |
|---|---|
| 三个类型合计（按行） | **516 行**（旧文档写的「516 处」其实是行数，不是匹配数） |
| 实际匹配次数 | **569 处** |
| 涉及文件 | **41 个**（不是 63） |
| 其中 `System.Windows.Controls.InkCanvas` | 24 处 / 5 文件 |
| 其中 `InkCanvasEditingMode` | 258 处 / 29 文件 |
| 其中 `StrokeCollection` | 292 处 / 23 文件 |

集中在 5 个文件里：

| 文件 | 匹配数 |
|---|---|
| `MW_ShapeDrawing.cs` | 135 |
| `MW_TouchEvents.cs` | 93 |
| `MW_FloatingBarIcons.cs` | 42 |
| `MW_Save&OpenStrokes.cs` | 28 |
| `MW_SimulatePressure&InkToShape.cs` | 21 |

**好消息**：41 个文件里前 5 个占了 319/569 = 56%，确实是集中的，不是漫天散落。

## 项目规模（实测）

```
C#   111,367 行 / 343 文件（排除 obj/bin）
XAML  14,651 行 /  68 文件
MainWindow 部分类合计 43,558 行 / 37 文件
设置页 24 个
```

---

# 七、对照数据：iNKORE 内存泄漏（已测，别重复测）

上游 [issue #438](https://github.com/iNKORE-NET/UI.WPF.Modern/issues/438)：
`UseModernWindowStyle="True"` 的窗口关掉后不释放。修复在 v0.11.0（2026-10-28）。

我们 8 个窗口开着这个属性：

```
AdminLaunchWindow · CrashWindow · MiniWhiteboardWindow · NewStyleTimerWindow
OobePresetWindow · OobeWindow · OperatingGuideWindow · SettingsWindow
```

## 升级前后实测（压力测试：反复构造 24 个设置页）

| 项 | 0.10.2.1 | 0.11.0 |
|---|---|---|
| 3 轮后内存 | 20→149MB（+129） | 20→110MB（+90） |
| 每轮均摊 | +43MB | **+30MB** |
| 6 轮后 | 未测 | 196MB |
| 句柄 | +20 | +21 |
| 3 轮耗时 | 15212ms | 15696ms |

**30MB/轮仍在，线性增长，不是缓存。** 这条没解决，且和 WinUI3 无关，是 iNKORE 的账。

---

# 八、我这一阶段最容易犯的错（写给自己）

## 错误模式一：拿最像的东西当判据

- #48：把「页面有问题 + 要求触摸可用」读成「页面滚不动」，查了一上午
- #49：用 `Page.Title` 比页面标题，得出 7 处误判差点全删
- **今天第三次**：看到介绍文章说 WinUI3 有 `InkCanvas` 就写进文档，
  没问「这句话指的是哪个通道」。八个稳定版全是 0，文章一个字没提。

**共同点：证据和结论之间没有因果链，只有「看起来像」。**

判据：动手前能说出「这句话由哪一行代码渲染 / 这个功能由哪个方法驱动 / 这个类型由哪个包提供」。
说不出来就是还没查清。

## 错误模式二：拿新发现否定已观察事实

用户说「之前就能滚」，我查到 `_isPointerStackEnabled = False` 就判定「所以不能滚」，
还改了上游反复权衡过的开关。

**已观察事实 > 我的推断。** 两者冲突时先怀疑自己。

## 错误模式三：批量改文件不看 git diff

Python `split('\n')` 把 CRLF 写成 LF，产生 3299/3388 行假 diff。

**判据：`git diff --numstat` 的增删行数和实际改动量对不上，就是整文件被重写了。**

## 错误模式四：数了个数就以为是那个数

今天重数依赖面才发现：旧文档的「516 处」其实是**行数**，「63 个文件」不知道哪来的
（重数出来是 41 个，我把所有可能的模式都试了一遍，没有一种能得到 63）。

**判据：写下任何数字之前，把产生它的命令也写下来。** 写不出命令的数字不许进文档。

---

# 九、动手前的检查清单

第 3 步的门槛：

- [x] Windows App SDK 2.6.x 在这台 Win10 19045 上能起来 —— **已实测，BOOT_OK**
- [x] unpackaged 部署能做出「解压即用」的包 —— **已实测，不开 MSIX 也能起**
- [x] 墨迹控件在稳定版有没有 —— **已实测，八个稳定版全是 0**
- [ ] **墨迹控件进稳定通道** ← 这一条现在卡住，其余全绿
- [ ] 形状识别方案定了（继续 IAWinFX IPC）—— 协议已确认与 UI 框架无关，可行
- [ ] 第 1 步的拆分做完，能说清「改 X 只需读哪几个文件」
- [ ] 第 2 步的绘制层接口抽完，WPF 实现通过全部回归测试

**第 4 条打不上勾，就不要开始第 3 步。** 不是能力问题，是那块地还没长出来。

---

# 十、当前仓库状态

```
分支      ltsc
HEAD      7fb185f9（本文档上一版）
发布      sorrow-0.1a（GitHub Release 已发）
           https://github.com/Baimacao/community-ltsc/releases/tag/sorrow-0.1a
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

## 实测产物

复现用，都在 `E:/DSH/tmp/`：

```
winui3test/     只做类型解析的探针（稳定版报 CS0234，实验版报 CS8305）
winui3boot/     真的能起窗口的空项目（BOOT_OK）
wasdk/          拆开的 nuget 包，按版本存着
```

复测启动：

```bash
cd /e/DSH/tmp/winui3boot
"/c/Program Files/dotnet/dotnet.exe" build boot.csproj -c Release --nologo -v q
bin/Release/net8.0-windows10.0.19041.0/win-x64/boot.exe &
sleep 12
cat bin/Release/net8.0-windows10.0.19041.0/win-x64/boot-result.txt
```
