# ICC CE Lite 接手提示词（2026-11-18）

给压缩上下文之后的自己，或者下一个接手的人。
**先读这份，再动手。**

---

# 一、现在是什么状态

```
分支      ltsc
HEAD      见 git log -1（写文档时是 042f4836，会往后走，别照抄）
版本      0.1c-daily（日常测试线，不是定版）
上一个定版 sorrow-0.1a
发布      GitHub Release 三个：
          https://github.com/Baimacao/community-ltsc/releases/tag/0.1c-daily-20261119
          https://github.com/Baimacao/community-ltsc/releases/tag/0.1c-daily-20261119
          https://github.com/Baimacao/community-ltsc/releases/tag/0.1b-daily-20261118
          https://github.com/Baimacao/community-ltsc/releases/tag/sorrow-0.1a
```

**代码量**

统计口径：只算 `Ink Canvas/` 项目（不含 InkCanvas.Controls 等 4 个子项目）。
全仓 C# 是 114,188 行 / 386 文件，XAML 15,782 行 / 81 文件。

| | 现在 | 起点（151992af，0.1a 定版） |
|---|---|---|
| C# | 110,740 行 / 352 文件 | 111,364 行 / 343 文件 |
| XAML | 14,623 行 / 65 文件 | 14,651 行 / 68 文件 |

行数只少了 624，但**最大的文件从 6281 行降到 3101 行**。多出来的文件数是拆分产物。

**质量基线**

```
编译错误   0
编译警告   2 条（同一个 WFO2000 被主 csproj 和临时 wpftmp 各报一遍，
           所以 grep 会看到 4 行。全是同一个地方，见第四节）
冒烟       PASS=40 FAIL=0
设置页     24/24 构造成功
工具栏暴力 5 轮 × 60 次操作 = 300 次，异常 0，内存 19MB 不涨，句柄 1068→1105
设置页暴力 3 轮 × 24 页 = 72 次构造，异常 0，内存 19→120MB（见 4.3）
```

---

# 二、构建与验证（照抄即可）

## 构建

```bash
taskkill //F //IM ICCCELite.exe 2>/dev/null | head -1
cd "E:/B1807/Documents/icc-lts/src/Ink Canvas"
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvasForClass.csproj" -c Release -p:Platform=x64 --nologo -v q
```

**必须 taskkill。** 不杀会 MSB3027/MSB3021。

输出目录：`bin/Release/x64/net11.0-windows10.0.19041.0/`

## 测试探针

全部在 `App.xaml.cs` 里接到命令行：

| 参数 | 作用 | 耗时 |
|---|---|---|
| `--smoke-test` | 40 条断言，最常用的验收 | ~30s |
| `--dump-settings-pages` | 构造 24 个设置页，抓 XAML 解析失败 | ~40s |
| `--stress-toolbar N` | 工具栏暴力测试 N 轮，每轮 60 次乱点 | N×1s |
| `--stress-settings N` | 设置页压力测试，看内存增长 | N×10s + 24页定长 |

**探针不会自己退出**，跑完必须 taskkill，否则脚本会一直挂在那里等。
`--dump-settings-pages` 和 `--stress-settings` 都是这样。

**跑法**（注意：别把 sleep 塞进同步 bash 命令，会撞 run_code 的 120 秒上限）：

```bash
cd bin/Release/x64/net11.0-windows10.0.19041.0
rm -f Logs/*.txt
./ICCCELite.exe --smoke-test --skip-mutex-check 2>&1 | head -2
sleep 32
f=$(ls -t Logs/*.txt | head -1)
grep -E "冒烟自测结束|FAIL" "$f"
```

超过 90 秒的任务用 `run_in_background: true`，下一轮用 `job_output({wait:false})` 查，
**不要用 `wait:true`** —— 等待时间也算在 120 秒里。

## 打包

**只打这 10 个文件 + 说明.md**（约定#12）：

```
IACore.dll                    ICCCELite.exe
IALoader.dll                  ICCCELite.runtimeconfig.json
IAWinFX.dll                   InkCanvas.IACoreHelper.exe
ICCCELite.deps.json           InkCanvas.IACoreHelper.exe.config
ICCCELite.dll
ICCCELite.dll.config
```

**绝对不能进包**：`Configs/` `Logs/` `Saves/` `Crashes/` `Backups/` `Automations/`
`Plugins*/` `*.pdb`。判据：**程序第一次启动会自己生成的东西，一律不进包。**

打完必须解压到空目录启动验证一遍。

---

# 三、这一轮做了什么（2026-11-17 ~ 11-18）

## 3.1 代码整理（约 30 个提交）

**拆分大文件**

| 原文件 | 拆前 | 拆后 | 拆成 |
|---|---|---|---|
| MW_FloatingBarIcons.cs | 6281 | 2600 | 6 个 partial |
| MW_PPT.cs | 3203 | 2358 | 2 个 |
| MainWindow.xaml.cs | 3312 | 3101 | 2 个 |
| MW_SimulatePressure&InkToShape.cs（后改名去掉 &） | 3121 | 2148 | 2 个 |
| MW_ElementsControls.cs | 3094 | 1872 | 2 个 |
| MW_ShapeDrawing.cs | 3009 | 1067 | 3 个 |
| MW_TouchEvents.cs | 2409 | 814 | 3 个 |
| App.xaml.cs | 2131 | 1490 | 2 个 |
| PPTQuickPanel.xaml.cs | 2090 | 1621 | 2 个 |
| NewStyleTimerWindow.xaml.cs | 1970 | 1801 | 2 个 |

**所有拆分都验四件事**：括号平衡、方法名集合双向差集为空、编译 0 错误、冒烟通过。
每次的 +增/-删 都能对上（多出来的是 using 头和注释）。

**删的死代码**

（下面的数都是逐个 `git show --numstat` 数出来的，不是估的。
早先我写过「约 2100 行」，那个数是估的，撤回。）

```
11 个死文件 1301 行（PPTROTConnectionHelper 594 行 / Hotkey.cs 96 行 /
CycleProcessBar 219 行 / ExternalCallerLauncher 81 行 / ... 其中 3 个是 .xaml）
Action.Register     259 行（7ea7cd4f，删 9 个 Register() 方法）
Rule.Register       232 行（da76ceac，删 7 个 Rule Register() 方法）
空壳常量类            59 行
死 Converter          60 行
死重载                54 行
死成员                49 行
MainWindowSettingsHelper  472 行
```

**判据教训**：我原来用「文件名 grep 全仓」找死代码，漏掉了
`Helpers/Hotkey.cs` 这种「类名不出现但 resx 里有同名词」的情况。
正确做法是**枚举所有能让类型被实例化的机制**：反射 Discover()、Activator+typeof 表、
DI 泛型注册、typeof() 出现在特性里、XAML 元素实例化、扩展方法语法。

## 3.2 修的问题

**静默错误 20+ 处**

判据是「这个错误发生时，有没有人能知道」。

最要紧的是 `SettingsManager.SaveSettingsToFile` —— 全项目 276 处引用（274 个真实调用 + 2 处定义），
catch 里只有 `Debug.WriteLine`（Release 下被编译掉）。写盘失败等于完全无声：
用户改了设置、界面显示已改、重开回滚，永远查不出原因。

还有 122 处「catch 里只有 Debug.WriteLine」统一改成写日志。
**排除 `LogHelper.cs` 自己那 4 处**——写日志失败时不能再写日志。

**自动化「保存笔画」是假的**

处理体只有一句 `// TODO`。用户在工作流里配了它，触发后什么都不发生。
现在真接上了，还把它上面两个从没被读过的设置项（`SavePath`/`SaveAsXml`）接通了。

**恢复推荐设置写死 D 盘**

`D:\Ink Canvas\AutoSavedStrokes`。没 D 盘的机器点完就废。

**UIAccess / 置顶维护的语义造假**

`StartTopmostMaintenance` 和 `StopTopmostMaintenance` 方法体一模一样，都是 Pause。

## 3.3 班测反馈的五条（其中两条是我自己引入的）

| # | 问题 | 真实原因 |
|---|---|---|
| 1 | 鼠标状态点页码不生效 | **我上一版把「保持穿透状态」写成了「什么都不做」**，整个方法直接 return。现在状态不变、导航照常 |
| 2 | 翻页后进批注但写不了字 | **也是我引入的**。恢复流程调的 `PenIcon_Click` 在 `IsAnnotating==true` 时走 else 分支，那分支不碰 `EditingMode`。现在显式 `SetCurrentToolMode(Ink)` |
| 3 | 拖动后展开动画消失 | 动画用 `fe.ActualWidth` 判断有无内容，拖动后读到 0 → targets 为空 → 跳过动画。加 `root.UpdateLayout()` |
| 4 | 隐藏与侧边栏动画不连贯 | 浮动栏 0.35s vs 侧边栏 0.175s，同时启动。侧边栏改 0.30s |
| 5 | OOBE 重弹 | **只加了诊断日志，逻辑没改**。四个写入点，光读代码看不出是哪条清的 |

## 3.4 新增功能

**桌面模式淡出独立设置**

原来只有一组参数挂在 `PowerPointSettings` 下，但判定是 `currentMode == 0`——
普通桌面也生效。现在拆成 `Appearance.FloatingBarIdleFadeDesktop*` 和
`PowerPointSettings.FloatingBarIdleFade*` 两套。

界面入口：设置 → 工具栏外观 → 「桌面模式淡出」。

**顺带修了个真 bug**：`StartFloatingBarIdleFade` 在构造函数里就调
`ApplyFloatingBarIdleFadeSettings`，那时配置还没加载。**改了参数重启也不生效。**
修法：`LoadSettings(true)` 之后补一次刷新。

**PPT 翻页组件设置合并**

5 个 Tab → 顶部全局 + 四行（左下/右下/左侧/右侧）。

位序 `[0]=左下 [1]=右下 [2]=左侧 [3]=右侧`，和三个消费者逐一核对过：
`MW_AutoFold.cs:493`、`MW_FloatingBarIcons.Panels.cs:310`、`MW_Settings.cs:514`。

老配置兼容：`XxxUseGlobalSettings` 字段没删，页面不暴露但代码仍读。
**转独立的顺序很关键**：先把生效值抄进该位置字段，再关开关。反过来会让同行四项集体跳回旧值。

## 3.5 编译警告 7 类 → 1 类（这 1 类后来又退回去，见 4.2）

| 项 | 处理 |
|---|---|
| CS0649 死字段 | 真 bug，删 |
| SYSLIB0060 过时 PBKDF2 | 换静态 API，**显式传 SHA1** |
| CS4014 未 await | 加 `_ =` |
| WFO2000 | 改静态缓存（后来又退回，见 4.2——改了会引并发 bug） |
| NU1510 冗余包 | 删 Microsoft.VisualBasic |
| CS8981 小写别名 | `ui` → `Ui` |

---

# 四、还没解决的（照实说，别当成已修）

## 4.1 OOBE 重弹没有根因

四个地方写 `HasShownOobe`：
- `MainWindow.xaml.cs` OnOobeCompleted → true
- `OobePresetWindow.xaml.cs:302` 套用预设 → false（有意）
- `SettingsActionHub.cs:301` 换语言 → false（有意）
- `Settings.cs:297` 默认值 false

还有 `_oobeInterruptedForLanguageChange` 拦着。

现在每次启动记一行：
```
[OOBE] 检查触发条件: HasShownOobe=False 配置文件=Configs\Settings.json 存在=True
```

本机实测是「文件存在但值为 False」。**再真弹一次，拿这行来定位。**

## 4.2 那 2 条 WFO2000 不要改

`WindowOverviewModel.Windows` 的 getter 在锁内复制一份返回。
这个「每次 new」**是必须的**——集合有后台定时器在改，直接返回内部引用会让调用方
撞上 `InvalidOperationException: Collection was modified`。改了反而引入并发 bug。

## 4.3 设置页内存每轮涨 33MB（**已确证是真泄漏，根因未定位到行**）

实测（`--stress-settings`，每轮 24 页全构造）：

| 轮 | 内存 | 增量 |
|---|---|---|
| 基线 | 19MB | — |
| 1 | 55MB | +36 |
| 2 | 88MB | +33 |
| 3 | 120MB | +32 |
| 4 | 152MB | +32 |

每轮增量是固定值（+32~33MB），10 轮实测也是严格线性：56 → 185 → 348MB。

**上一版文档说「不是真泄漏、是按页算的固定开销」，这个判断是错的。**
2026-11-19 用强制 GC 判据推翻了它：

```
[GC 判据] 强制回收前=121MB 后=121MB 回收=0MB
          代计数=122/58/29   （跑了 122 次 Gen0、29 次 Gen2）
```

连做 3 次 `GC.Collect(2, Forced, true, true)` + `WaitForPendingFinalizers` + 让出 UI 线程，
一个字节都收不回来。**这是 100% 确凿的泄漏。**

**已经查实的**（用弱引用 + 轮次标记实测）：

- 每轮 24 页里固定 **10 个**在完整 GC 后仍存活，且**每轮都是同一批**
- 存活页面的共同点：XAML 里都有 `ui:SettingsExpander`（每页 6~38 个）
- 其余 13 页全是 0 个 `SettingsExpander`，全部被回收
- 这个相关性在 24 个页面上是**完美对应**，没有例外

**2026-11-19 这一轮已经排掉的假设**（别再重走）：

| 假设 | 怎么验 | 结果 |
|---|---|---|
| 是 GC 延迟，不是泄漏 | 强制全量回收 3 次 | 回收 0MB → **是真泄漏** |
| 页面对象被静态字段持有 | 扫 ICC 全部程序集的静态字段 | 无 |
| `SpringAnimator._scalarValues` 静态字典钉住控件 | 改成弱引用 key 后重测 | 泄漏量一点没变 |
| 动画时钟累积（转储里 1.8 万个动画对象） | 设置里关掉全部动画后重测 | 泄漏量一点没变 |
| 全局资源字典被写脏 | 数合并字典与条目 | 92 个字典 / 3293 条，**不增长** |
| 布局队列残留 Request | 查 306 个 Request 的 Target | 全是 null（已处理完的空壳） |
| 页面连到长命对象（MainWindow / App 等） | 反向走三层实例字段 | 没连到 |

**唯一确证的因果**（用对照实验，不是相关性）：

```
完全不做布局（nolayout）   5 轮 22MB → 22MB，存活 1/120   ← 不泄漏
只调 Measure              3 轮 55→117MB，回收 0MB        ← 泄漏
Measure + Arrange         3 轮 56→121MB，回收 0MB        ← 泄漏
三步全做（真实路径）       3 轮 56→121MB，回收 0MB        ← 泄漏
```

**2026-11-19 第二轮：已经追到具体控件了。**

从「只构造」到「只 ApplyTemplate」再到「只 Measure」逐级二分，
再对每个控件单独做最小复现（每组造 30 个实例，Measure 后强制全量 GC 三次）：

```
普通 Border            存活  1/30
SettingsCard           存活  1/30   ← SettingsExpander 的父类，不漏
ToggleSwitch           存活  1/30
SimpleStackPanel       存活  1/30
SettingsExpander       存活 30/30   ← 漏
ItemsRepeater 单独      存活 30/30   ← 根因

SettingsExpander 只构造不 Measure    存活  0/30
SettingsExpander 只 ApplyTemplate    存活 30/30   ← 就是这一步
```

**根因：iNKORE 0.11.0 的 `ItemsRepeater`（从 Windows UI Library 移植）自身泄漏。**
`SettingsExpander` 的模板里有一个它，所以用 Expander 的页面全被钉住。
`SettingsCard` 模板里没有，所以不漏——这个差异正好对上。

**试过但无效的修法**（别再试）：

| 修法 | 结果 |
|---|---|
| 给 `SettingsExpander.Items` 显式赋空集合 | 30/30 仍存活，**无效** |
| 在 SettingsWindow 三个创建点遍历页面补 Items | 无效（且探针绕过这三个点，压根没执行） |
| 改 `SpringAnimator` 静态缓存为弱引用 | 本身是真实泄漏，但和这个无关 |

**下一步只有一条路**：绕开 `ItemsRepeater`。

1. 把 `SettingsExpander` 换成 `SettingsCard` + 自己写展开/收起（`Expander` 就能做，
   它的模板里没有 ItemsRepeater）。涉及 10 个页面 107 处 XAML，工作量大但可控
2. 或者给 iNKORE 提 issue / 打本地补丁（替换它的 ItemsRepeater 实现）
3. 或者升级 iNKORE，看新版是否修了（0.11.0 已是本地最新）

实测影响面：每打开一次设置窗口，24 个页面里 10 个泄漏，约 33MB。
关窗不会恢复（`_pages.Clear()` 只是丢掉引用，对象本身被 ItemsRepeater 钉住）。
老师反复开关设置十次就是 330MB，所以这条必须修，不能留着。

**工具这一路为什么走不通**：

`dotnet-dump` 的 `gcroot` 在这个环境下**对任何对象都返回 `Found 0 unique roots`**
——包括明明活着、强制 GC 都收不掉的对象。这个结果和事实矛盾，
说明工具本身在这个 .NET 11 的转储上不可信，不能再拿它当判据。
（试过的对象：SettingsWindow、CanvasPage、SettingsExpander、
DoubleAnimationUsingKeyFrames、Style —— 全部 0 roots。）

**不要再做的**：

- 全程序集反射扫属性（会卡死，被看门狗杀）
- `gcroot`（这个环境下不可信）
- 数转储里的对象数量（那些是症状，不是原因）
- 补 `Items` 为空集合（已证无效）
- 关动画（已证无效）
- 改静态缓存（已证无关）

**探针已经留好的工具**（下次直接用）：

```bash
# 三种布局深度，用来二分定位
./ICCCELite.exe --stress-settings 3 nolayout   # 完全不布局（对照组）
./ICCCELite.exe --stress-settings 3 measure    # 只 Measure
./ICCCELite.exe --stress-settings 3 arrange    # Measure + Arrange
./ICCCELite.exe --stress-settings 3            # 三步全做（默认）
```

跑完会打这几行：`[GC 判据]`（强制回收后剩多少）、`[静态扫描]`、
`[全局资源]`、`[反向查]`。

## 4.4 问题 1/2/3 没做真机验收

这三条都跟 PPT 放映有关，开发机上跑不了完整流程。
**「编译过 + 逻辑对」不等于「功能验收通过」。** 班测才算数。

## 4.5 两个大文件没拆

`MW_TouchEvents.cs` 相关的触摸逻辑、`App.xaml.cs` 的启动流程，耦合度高。
继续拆的收益在递减。

---

# 五、我这一轮犯的错（写给自己）

## 错误 1：读需求时把并列读成因果，或者把「保持」读成「不做」

- 班测说「设置页有问题，**且**都无法用触摸屏翻页」→ 我读成「因为滚不动所以有问题」，查了一上午
- 需求说「穿透态下点页码**保持**即可，不需要遵循后面的逻辑」→ 我写成 `if (!IsAnnotating) return;`

**同一个坑踩了两次。** 判据：需求里出现「保持」「不需要」「保持现状」这类词时，
先问一句**「具体是不要做什么、什么必须照做」**，而不是笼统地 return。

## 错误 2：判断「某设置有没有界面入口」只查了一处

我说「浮动栏淡出设置界面上没有入口」，因为我只 grep 了 XAML。
实际 `PPTPageFlipPreviewPage.xaml.cs` 里有完整的 ToggleSwitch + 两个 Slider。
**差点把已有的 UI 拆掉重做。**

判据：判断某设置项有没有界面入口，必须同时查三处——
`.xaml` 里的控件名、`.xaml.cs` 里的 `Xxx.IsOn = settings.Yyy`、页面 Load 方法。

## 错误 3：脚本把 CRLF 文件压成一行

判了 `crlf` 却用 `'\n'.split()`，1742 行的 `PptBridge.cs` 被压成 117 行。

判据：按行改文件的脚本，split/join 必须用同一个 nl 变量。
CRLF 文件先归一化成 `\n`，处理完再还原。
**脚本跑完行数变化超出预期就立刻 `git checkout` 回滚，不要在坏文件上继续修。**

## 错误 4：把等待时间塞进同步 bash 命令

`sleep 150` + `job_output(wait:true)` 两次撞 120 秒上限。

判据：超过 90 秒的任务用 `run_in_background`，下一轮 `job_output({wait:false})` 查一次，
没完就先做别的事。

## 错误 5：给子智能体报的数字是估的

我报「删了约 1900 行」，实际 1301 行。好几批提交混着估的。

判据：报任何累计数字之前，逐个 `git show --numstat` 累加。
给线索时说「详见 git log」，不附没验过的量级。

## 错误 6：用 `git add -A`

把子智能体正在编辑的文档一起提交进了语义不对的 commit。

判据：多智能体共享工作目录时，**永远不要用 `git add -A` / `git add .`**。
精确列路径。

## 错误 7：把「查过一遍没找到」当成「不存在」

这份文档第一版报了一堆数字，全是凭记忆 + 中途估的，一个都没回查。
复核时逐条现跑命令，改掉了这些：

| 原先写的 | 实际 |
|---|---|
| SaveSettingsToFile 281 个调用点 | 276 处引用（274 调用 + 2 定义） |
| 编译警告 4 条 | 2 条（grep 看到 4 行是重复报告） |
| 暴力测试 212 次操作 | 每轮 60 次，5 轮 = 300 次 |
| 基线 111,367 行 | 111,364 行 |
| src/docs 16 份 md | 18 份 |
| 拆分表 4 个行数 | 各差 1~44 |

**没有一个是编的，全是「我当时那么记得」**。这就是我在自述里写的那个病：
拿记忆当发现。

判据：文档里的每个数字都是「断言」，不是「描述」。写之前现跑一遍命令，
把命令和输出一起贴进去；跑不了的就删掉，或者写明「未复核」。

---

# 六、协作方式（如果还要派子智能体）

## 分工原则

**写作用域必须不相交。** 我在 team_task 里明确写「只能改这两个文件」，
并在给子智能体的 prompt 里重复一遍。

## 它们比我想的强，但交付要自己验

- `audit-struct` 纠正我「MainWindow 35 个 partial 不是 37」——
  我把目录文件数当成了 partial 数
- `audit-debt` 拦住我「18 处 ConvertBack 抛异常不是债，是 WPF 单向绑定标准写法」
- `audit-dead` 指出我的「文件名 grep」方法本身有缺陷
- `docs-keeper` 三次核出我给的判断已过期

**但它们也会出问题**：`nav-refactor` 自曝「用行号切片删代码时把文件尾部截断了一次」。
所以交付后必须自己跑一遍完整验证，别只看它的自测报告。

## 派任务时写清楚

1. 写作用域（改哪几个文件，其它不许碰）
2. 判据（怎么算完成，跑什么命令）
3. **绝对不要做什么**（比如「不许改 Settings.cs 的字段」）
4. 让它把实际命令和输出贴回来，不是「我做完了」

---

# 七、环境事实（记牢，别重复踩）

## 路径

```
仓库      E:/B1807/Documents/icc-lts/src
主项目    E:/B1807/Documents/icc-lts/src/Ink Canvas
文档      src/docs/（18 份 md，进 git；全仓 33 份）
          ../docs/（仓库外，工作过程，8 份）
产物      dist/
```

## 构建

```
SDK   /c/Program Files/dotnet/dotnet.exe
TFM   net11.0-windows10.0.19041.0
输出  bin/Release/x64/net11.0-windows10.0.19041.0/
```

## Git

```bash
GIT_ASKPASS=/e/DSH/tmp/git-askpass.sh \
  git push "https://Baimacao@github.com/Baimacao/community-ltsc.git" ltsc
```

**必须带显式 URL**——全局有 `url.<base>.insteadOf` 会把它改写成只读代理。
token 在 `E:/DSH/home/.github-token`，**不要写进 .git/config**。

## 版本号

改 `InkCanvasForClass.csproj` 里 `SetAssemblyInformationalVersion` 那个 Target：

```xml
<AssemblyInformationalVersion>0.1c-daily+$(_ShortSha7)</AssemblyInformationalVersion>
<AssemblyFileVersion>0.1.0.0</AssemblyFileVersion>
```

**顶层写 `<FileVersion>` 无效**——被 NBGV 覆盖。

## 绝对不能动

```
AppUserModelID = "InkCanvasForClass.LTSC"
互斥体名       = "InkCanvasForClass CE LTSC"
```

## 项目谱系（改署名时用）

```
IC (WXRIW) → ICA (ChangSakura) → ICC (Dubi906w)
→ ICC CE (CJK_mkp) → ICC CE Lite (Baimacao, 鲸仔_JZ)
```

---

# 八、ta 让我遵守的事（原话）

> 「不能留代码债，以后每次完成后必须进行测试，尽可能计算可能性，
>   确保软件的极端稳定性与性能」

> 「肯定要改，只要有技术债一定要改，没有不改的选项」

> 「别忘了触摸屏」+「去确保页面都可以使用触摸屏进行页面滚动」

具体落成这些规矩：

1. **每改完必须实测**，不能只编译通过。并发/状态机类改动要连跑多次看复现率
2. **必须有暴力测试**：快速连点、连按翻页、乱切模式，软件不能卡死/崩/泄漏
3. **报「已完成」前先验证测量本身有效**——测不到东西的数字不算证据
4. **要证明是我的代码起作用，必须有反向对照**——我的代码没跑，现象还会不会出现
5. **改了几次没效果就先加日志打真实数值**，不要继续读代码猜
6. **打包只打程序本体**，运行时生成的都不进包

---

# 九、下一步可以做什么

按优先级：

**A. 等班测反馈** —— 问题 1/2/3 只有真机能验，先等结果

**B. 继续拆文件** —— 收益在递减，但如果 ta 要求就继续：
`MW_TouchEvents.cs` 相关的触摸逻辑、`App.xaml.cs` 的启动流程

**C. 真机验收** —— 翻页笔实物按键（N-03），只能 ta 试

**D. 修设置页内存** —— 要么等 iNKORE 上游，要么评估换库

**E. 自动化不会自动触发** —— 手动能触发，Action 链没问题，问题在 Trigger 评估时机

---

# 十、现在该做的第一件事

跑一遍基线，确认环境没坏：

```bash
taskkill //F //IM ICCCELite.exe 2>/dev/null | head -1
cd "E:/B1807/Documents/icc-lts/src/Ink Canvas"
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvasForClass.csproj" -c Release -p:Platform=x64 --nologo -v q
# # 期望：0 错误，2 个 WFO2000（grep warning 会看到 4 行，是重复报告）

cd bin/Release/x64/net11.0-windows10.0.19041.0
rm -f Logs/*.txt
./ICCCELite.exe --smoke-test --skip-mutex-check 2>&1 | head -2
sleep 32
grep -E "冒烟自测结束" "$(ls -t Logs/*.txt | head -1)"
# 期望：PASS=40 FAIL=0
```

对得上再动手。对不上先查环境。
