# ICC CE Lite 接手提示词（2026-11-20 12:15）

给压缩上下文之后的自己。**先读这份，再动手。**

上一版 11-19 写的。这一版补上了 11-20 这一整天的实战记录，
以及一个刚查实、**还没修**的 bug 的完整交接。

---

# 一、现在是什么状态

```
分支      ltsc
HEAD      f29b96e0（会往后走，用 git log -1 看）
版本      0.1c-daily
远端      已推送，f29b96e0 与本地一致
```

**代码量**：C# 115,161 行 / 353 文件

**质量基线**（每次改完都要对得上）

```
编译错误   0
编译警告   2 条（同一个 WFO2000，改了会引并发 bug，见 4.2）
冒烟       PASS=86 FAIL=0
设置页     24/24
```

---

# 二、构建与验证

## 构建

```bash
taskkill //F //IM ICCCELite.exe 2>/dev/null | head -1
cd "E:/B1807/Documents/icc-lts/src/Ink Canvas"
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvasForClass.csproj" \
  -c Release -p:Platform=x64 --nologo -v q
```

**必须 taskkill。** 输出在 `bin/Release/x64/net11.0-windows10.0.19041.0/`。

## 可用的探针参数

| 参数 | 用途 |
|---|---|
| `--smoke-test` | 86 条断言，最常用 |
| `--dump-settings-pages` | 24 个设置页构造 |
| `--stress-settings N [idle]` | 设置页压力；**判泄漏必须加 idle** |
| `--probe-idle-drain` | 对照：队列抽干深度会不会伪造泄漏 |
| `--probe-gc` | GC 机制 |
| `--probe-pinning` | 队列操作能钉住多大内存 |
| `--probe-settings-cycle N` | 真实开窗路径 |
| `--probe-repeater` | ItemsRepeater vs 原生 ItemsControl |
| `--probe-expander` | 旧版，存活数是假的 |

**探针不会自己退出**，跑完必须 taskkill。

**跑法**：超过 90 秒的用 `run_in_background: true`，下一轮 `job_output({wait:false})` 查一次。
**不要用 `wait:true`** —— 等待时间算在 120 秒上限里。

## 打包

只打 10 个文件 + 说明.md：

```
IACore.dll              ICCCELite.exe
IALoader.dll            ICCCELite.runtimeconfig.json
IAWinFX.dll             InkCanvas.IACoreHelper.exe
ICCCELite.deps.json     InkCanvas.IACoreHelper.exe.config
ICCCELite.dll
ICCCELite.dll.config
```

打完**必须解压到空目录跑一次冒烟**。11-20 之前一直只验构建目录，
这次才发现包内环境有独立的坑（见 §5.3）。

---

# 三、11-20 这一天做了什么

## 3.1 解题（已完）

| # | 问题 | 真因 |
|---|---|---|
| 1 | 关于页版本号显示 1.x.x.x | XAML 根元素缺 `Loaded=` 绑定，处理器从没挂上，显示的是设计期占位符 |
| 2 | 自动隐藏触发崩溃 | 折叠函数第一个 await 之前读 UI 元素，那时还在 Timer 线程 |
| 3 | 没开翻页笔适配也吞 ESC | 钩子进放映就无条件挂，和适配开关无关 |
| 4 | **快翻预览后写不了字** | **见 §4.1，查了十七轮** |
| 5 | 折叠日志刷 5 条 | 日志打在第一行，实际只折 1 次 |
| 6 | 退出放映热键失败日志误导 | 说「被其他程序占用」，实际是自己钩子先吃掉了 |

## 3.2 已完：PPT 导航改成新规则

用户 11-20 11:38 定的新规则，**替换掉原来的「切页后自动回批注」**：

```
点页码  → 一律隐藏笔迹 + 切鼠标穿透（不管当前什么模式）
没翻页  → 点「批注」恢复笔迹显示
翻页了  → 自动恢复笔迹显示，但仍保持鼠标穿透
```

**为什么改规则**：原来那条「切页后 500ms 自动把模式切回批注」的链，
需要在异步窗口里同时把窗口激活、命中测试、选中框、EditingMode 四样都调对。
任何一样没调对就是「显示批注但实际穿透」的半切状态 —— §4.1 那个 bug 就是它。

现在不自动切了。翻完页仍是穿透态，老师要写字自己点批注，走成熟路径。

改动在 `42496210`：
- `OnPPTNavBarPageClickAsync` 去掉 `wasAnnotating` 分支
- `OnPPTSlideShowNextSlide` 去掉 `TryRestoreAnnotationAfterPptNavigation` 调用
- `PenIcon_Click` 开头显式恢复笔迹显示

---

# 四、还没解决的

## 4.1 快翻预览后写不了字 —— 已按新规则绕过，但真因没查实 ⚠️

这是 11-20 花了一整天、**十七轮**没查实的问题。

### 现象

PPT 放映中点页码进预览快翻，翻一页，回到画布，**光标是箭头，划一下穿透到 PPT 翻页**，
界面上显示的是批注模式（工具栏高亮在笔上）。

### 已排除的（每一项都有日志实证）

```
EditingMode=Ink          ✓
IsHitTestVisible=True    ✓
Visibility=Visible       ✓
IsEnabled=True           ✓
尺寸=1920x1080           ✓
Topmost=True             ✓
激活=True（修过）         ✓
父Grid 可见/命中          ✓
WS_EX_TRANSPARENT=False  ✓   ← Win32 层也查过了
```

### 关键日志（Log_2026-11-20-11-29-05）

```
11:29:49  pen → cursor           ← 点页码，切穿透
11:29:52  cursor → pen           ← 自动恢复（当时还在用旧逻辑）
11:29:57  源=Grid                 ← 划的那一下
11:29:57  pen → cursor           ← 紧跟着又被切回鼠标
```

`源=Grid` 说明命中的不是 `inkCanvas`。但 `命中链` 显示是 `ButtonPanel < ButtonBorder`，
那是 iNKORE 按钮模板内部元素 —— 也就是说**那一下命中的是一个按钮**，
可能在翻页栏附近，不在画布中央。

### 现在的处理

按用户新规则（§3.2）绕开了：翻页后就是穿透态，不再自动切批注，
所以不会出现「显示批注实际穿透」的半切状态。

**但真因没找到。** 如果以后又冒出类似症状，从这里继续：
- 用 `[窗口输入]` 日志看 `点=(x,y)` 和 `在画布内=`
- 确认用户划的位置到底是不是画布
- 检查 `InkCanvasSelectionAdorner` 有没有残留（WPF 内置选择框装饰层，
  曾经出现过在 `源=` 里）

## 4.2 那 2 条 WFO2000 不要改

`WindowOverviewModel.Windows` 的 getter 在锁内复制一份返回。
**改了会引 `InvalidOperationException: Collection was modified`。**

## 4.3 笔迹重启后读不出来 —— **下一轮第一件事**

### 根因（已查实，未修）

仓库里有**两套演示文稿 ID 生成逻辑，格式不同**：

**读**（`PPTInkManager.GeneratePresentationId`）永远三段：
```csharp
return $"{presentation.Name}_{presentation.Slides.Count}_{hash}";
```

**写**（`MW_PPT.EventHandlers.GetPresentationStrokeFolderPath`）会退化：
```csharp
if (!string.IsNullOrEmpty(fullName))
    return $"{basePath}/{name}_{slidesTotal}_{hash}";
return $"{basePath}/{name}_{slidesTotal}";     // ← 两段
```

**日志实证**（Log_2026-11-20-12-02-26）：
```
[PPT笔迹] ID=test1.pptx_53_39041F03
文件夹=...\test1.pptx_53_39041F03  存在=False  已存页数=0
```
磁盘上实际是 `test1.pptx_53`（两段）。名字对不上，读不到。

### 哈希其实是稳定的

`HashHelper.GetFileHash` 算的是**文件路径字符串**的 MD5，不是内容。
路径不变哈希就不变。所以跨启动是稳定的，问题纯粹在格式不一致。

### 用户定的方案（原话）

> 两个都要，如果页数有改动弹弹窗问老师改没改，这样就保证笔迹也有，
> 也不怕改动，弹窗要美观（拿出 1017 模型真正的前端能力）

**拆成三件事：**

1. **统一 ID 生成** —— 抽成一个地方，读写都用它
2. **页数变化时弹窗询问** —— 检出「同名同路径但页数变了」时，
   问老师「这份 PPT 改动过吗？笔迹还要不要沿用上次的？」
3. **弹窗要美观** —— 用 iNKORE `ContentDialog`（项目里已有 32 处用法，
   参考 `MW_PresenterKey.cs:183`），不要 MessageBox

### 实现要点

- 统一 ID 建议：`{名字}_{路径哈希}`（去掉页数），页数只用来做「变化检测」
- 老的两段文件夹（`test1.pptx_53`）要做一次迁移，否则老数据还是读不到
- 弹窗只在**检测到页数变化**时出现，不能每次开 PPT 都弹
- 检测逻辑：读文件夹名里的页数 vs 当前 `Slides.Count`

### 现在已有的诊断

`PPTInkManager.InitializePresentation` 里有 `[PPT笔迹]` 日志，
记录名字/页数/ID/文件夹是否存在/已存页数/FullName 是否为空。接着用。

---

# 五、我这一天犯的错（写给自己，很重）

## 错误 1：十七轮都在查「状态对不对」，没查「事件有没有到」

快翻预览那个 bug，我依次改了：
`Visibility` → `IsHitTestVisible` → `EditingMode` → 父容器 → `Topmost` → 窗口激活，
每一项都「确实是个问题、确实改对了」，但**每一项都不是根因**。

**真正该做的第一步是**：在 `Window_PreviewMouseDown` 打一行日志，
看鼠标有没有进 ICC。这条日志一加，立刻就知道事件根本没到画布。

**判据**：查「功能不工作」时，先确认**输入有没有到达**，再查**到达后处理对不对**。
顺序反了会绕很远。

## 错误 2：加了过滤条件的诊断，把最需要看的滤掉了

第一次加 `[触摸入口]` 日志时我写了：
```csharp
if (inkCanvas.EditingMode != Ink && currentToolMode == "pen") { Log(...); }
```
结果一条都没输出。我因此判断「事件没到」——**但可能到了、只是条件不成立**。

**判据**：诊断日志第一版**不要加过滤**。先看全量，再决定收窄。
过滤条件本身就是个假设，用没验证的假设去筛数据，等于自己蒙自己。

## 错误 3：远程控制环境没考虑

用户是在远程控制（GameViewer）上测的，注入的是**鼠标事件**不是触摸。
我前面查的很多都是触摸路径（`Main_Grid_TouchDown`、`InkCanvas_PreviewTouchDown`）。

**判据**：查输入类问题时，先问「这个环境注入的是鼠标还是触摸」。
两条路在代码里是分开的。

## 错误 4：包内环境一直没验

前面几版包都只跑构建目录的冒烟，11-20 才发现**包内（空目录冷启动）会卡死**，
原因是探针里 `Dispatcher.Invoke` 同步等 UI 线程、读正在写的日志文件、
冷启动期改 `Visibility` 引发布局。修了四处才通。

**判据**：打包后必须解压到空目录跑一次。

## 错误 5：反向对照失效了三次才发现

`FoldFloatingBar` 跨线程那条，我写了三版反向对照都「照样 PASS」：
- 第一版：测的是自己抄的 lambda，跟被测代码无关
- 第二版：数 IL 里的 `ret` 个数，旧代码本来就有多个返回路径
- 第三版：反向改动放在 `await` 之后，那里本来就在 UI 线程

**判据**：写完断言先做反向对照——**把被测代码改回旧写法，断言必须变红**。
不变红说明断言测的不是被测代码。

---

# 六、环境事实

```
仓库      E:/B1807/Documents/icc-lts/src
主项目    .../src/Ink Canvas
SDK       /c/Program Files/dotnet/dotnet.exe
TFM       net11.0-windows10.0.19041.0
产物      bin/Release/x64/net11.0-windows10.0.19041.0/
```

## Git 推送

**必须带显式 URL**（全局有 `insteadOf` 会改写成只读代理）：

```bash
GIT_ASKPASS=/e/DSH/tmp/git-askpass.sh \
  git push "https://Baimacao@github.com/Baimacao/community-ltsc.git" ltsc
```

网络会抖（`Connection was reset`），写重试循环。**判断成功要看 `$?`**，
不要 `git push | tail -3` 然后信 `if` —— 那判断的是 `tail` 的退出码。

## 绝对不能动

```
AppUserModelID = "InkCanvasForClass.LTSC"
互斥体名       = "InkCanvasForClass CE LTSC"
```

## 版本号

改 `InkCanvasForClass.csproj` 里 `SetAssemblyInformationalVersion` 那个 Target。
**顶层写 `<FileVersion>` 无效**，被 NBGV 覆盖。

---

# 七、用户的规矩（原话）

> 「不能留代码债，以后每次完成后必须进行测试，尽可能计算可能性，
>   确保软件的极端稳定性与性能」

> 「肯定要改，只要有技术债一定要改，没有不改的选项」

> 「别忘了触摸屏」+「去确保页面都可以使用触摸屏进行页面滚动」

具体落成：

1. **每改完必须实测**，不能只编译通过
2. **必须有暴力测试**：快速连点、连按翻页、乱切模式
3. **报「已完成」前先验证测量本身有效**
4. **要证明是我的代码起作用，必须有反向对照**
5. **改了几次没效果就先加日志打真实数值**，不要继续读代码猜
6. **打包只打程序本体**
7. **版本号和发 Release 只在 ta 说的时候做**

## 用户纠正过我的两件事（很重要）

**退出批注的方法是点「鼠标」，不是再点一次「批注」。**

**改一个「用户抱怨会打扰他」的功能时，先问：他要的是「关掉这个功能」
还是「缩小它的作用范围」？**
11-20 我把「ESC 一直弹提示」理解成「拦截本身是错的」，把整个防误触关掉了，
用户直接报「误触拦截功能失效」。

---

# 八、下一步

**A. 笔迹重启读不出来** ← 下一轮第一件事，§4.3 有完整方案

三件事：统一 ID、页数变化弹窗、弹窗要美观。
弹窗用 iNKORE `ContentDialog`，参考 `MW_PresenterKey.cs:183`。

**B. 快翻预览那个真因**（§4.1）
已经按新规则绕开，不紧急。如果又冒出来，从 `[窗口输入]` 日志继续。

**C. 等班测反馈**

---

# 九、现在该做的第一件事

跑基线确认环境没坏：

```bash
taskkill //F //IM ICCCELite.exe 2>/dev/null | head -1
cd "E:/B1807/Documents/icc-lts/src/Ink Canvas"
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvasForClass.csproj" \
  -c Release -p:Platform=x64 --nologo -v q
# 期望：0 错误，2 个 WFO2000

cd bin/Release/x64/net11.0-windows10.0.19041.0
rm -f Logs/*.txt
./ICCCELite.exe --smoke-test --skip-mutex-check &
sleep 45
grep "冒烟自测结束" "$(ls -t Logs/*.txt | head -1)"
# 期望：PASS=86 FAIL=0
taskkill //F //IM ICCCELite.exe
```

对得上再动手。
