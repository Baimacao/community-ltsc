# ICC CE Lite 接手提示词（2026-11-19）

给压缩上下文之后的自己，或者下一个接手的人。
**先读这份，再动手。**

上一版是 2026-11-18 写的。这一版删掉了已完成的、补上了新查实的。

---

# 一、现在是什么状态

```
分支      ltsc
HEAD      7887151e（会往后走，用 git log -1 看）
版本      0.1c-daily（日常测试线，不是定版）
上一个定版 sorrow-0.1a
发布      GitHub Release 三个：
          https://github.com/Baimacao/community-ltsc/releases/tag/0.1c-daily-20261119
          https://github.com/Baimacao/community-ltsc/releases/tag/0.1b-daily-20261118
          https://github.com/Baimacao/community-ltsc/releases/tag/sorrow-0.1a
```

**代码量**（只算 `Ink Canvas/` 项目）

| | 现在 |
|---|---|
| C# | 112,645 行 / 353 文件 |
| XAML | 14,680 行 / 65 文件 |

**质量基线**（每次改完都应该是这个数）

```
编译错误   0
编译警告   2 条（同一个 WFO2000，改了会引并发 bug，见 4.2）
冒烟       PASS=57 FAIL=0
设置页     24/24 构造成功
暴力测试   5 轮 × 60 次操作 = 300 次，异常 0，内存 18→20MB
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
| `--smoke-test` | 57 条断言，最常用的验收 | ~35s |
| `--dump-settings-pages` | 构造 24 个设置页，抓 XAML 解析失败 | ~40s |
| `--stress-toolbar N` | 工具栏暴力测试 N 轮，每轮 60 次乱点 | N×1s |
| `--stress-settings N [nolayout\|measure\|arrange]` | 设置页压力测试，带布局深度对照 | N×8s |
| `--probe-expander` | **内存泄漏最小复现**，多控件横向对照 | ~50s |

**探针不会自己退出**，跑完必须 taskkill，否则脚本会一直挂在那里等。

**跑法**（注意：别把 sleep 塞进同步 bash 命令，会撞 run_code 的 120 秒上限）：

```bash
cd bin/Release/x64/net11.0-windows10.0.19041.0
rm -f Logs/*.txt
./ICCCELite.exe --smoke-test --skip-mutex-check 2>&1 | head -2
sleep 32
grep -E "冒烟自测结束" "$(ls -t Logs/*.txt | head -1)"
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

## 发 Release（**只在 ta 说的时候做**）

```bash
# 1. 先提交版本号改动 + 打 tag
git tag -a "0.1x-daily-YYYYMMDD" -m "说明"
GIT_ASKPASS=/e/DSH/tmp/git-askpass.sh git push "https://Baimacao@github.com/Baimacao/community-ltsc.git" ltsc
GIT_ASKPASS=/e/DSH/tmp/git-askpass.sh git push "https://Baimacao@github.com/Baimacao/community-ltsc.git" "0.1x-daily-YYYYMMDD"
```

Release 用 GitHub API 建（token 在 `E:/DSH/home/.github-token`），
脚本模板可以参考 `E:/DSH/tmp/mk_01d.py`（如果还在）。

**我犯过的错：ta 只让「修激光笔」，我自己顺手把版本升到 0.1d 并发了 Release。**
**版本号和发 Release 这两件事，只在 ta 明确说的时候做。**

---

# 三、这一轮做了什么（2026-11-18 ~ 11-19）

## 3.1 修的问题（班测反馈）

| # | 问题 | 真实原因 |
|---|---|---|
| 1 | 鼠标状态点页码不生效 | 上一版把「保持穿透状态」写成了「什么都不做」，整个方法直接 return |
| 2 | 翻页后进批注但写不了字 | 恢复流程调的 `PenIcon_Click` 在 `IsAnnotating==true` 时走 else 分支，那分支不碰 `EditingMode` |
| 3 | 拖动后展开动画消失 | 动画用 `fe.ActualWidth` 判断有无内容，拖动后读到 0 → targets 为空 |
| 4 | 隐藏与侧边栏动画不连贯 | 浮动栏 0.35s vs 侧边栏 0.175s，同时启动。侧边栏改 0.30s |
| 5 | OOBE 重弹 | **已修**，见下 |
| 6 | PPT 放映中开白板，双指手势不能用 | **已修**，见下 |
| 7 | 翻页栏淡出时第一下点不动 | **已修**，见下 |
| 8 | 翻页栏淡出没有透明度设置 | **已修**，见下 |
| 9 | PPT 放映后 ICC 没反应（部分班级） | **已修**，见下 |
| 10 | 激光笔渐隐动画太快 | **已修**，见下 |

### 3.1.1 OOBE 反复重弹

`SaveSettingsToFile` 写盘后从不更新 `App.CachedSettingsJson`，而 `LoadSettings` 读的正是这份缓存。
于是本次运行内写过设置之后，任何一次 `LoadSettings` 都会把刚写的值覆盖回启动时的旧快照。

实际链路：`OnOobeCompleted` 先把 `HasShownOobe` 写成 true 并保存，
紧接着 `LoadSettings(false)` 读到旧快照（false），内存被打回 false，下次启动又弹。

**修法**：写盘成功后同步更新缓存。
**验证**：冒烟里加了 `OOBECACHE-1`，带反向对照——把修复注释掉这条必红。

### 3.1.2 PPT 放映中开白板，双指手势不能用

白板是独立画布，但手势闸门只看 `IsInPPTPresentationMode`。
用户进了白板时他确实还在放映，该值仍为 true，于是手势只能靠
`IsEnableTwoFingerGestureInPresentationMode` 放行（默认 false），
白板自己的 `IsEnableTwoFinger*Board` 三组开关根本没被读到。

**修法**：`currentMode == 1` 时走白板口径，不套用放映规则。
**验证**：`WBTG-1..4`，其中 WBTG-1 是反证（老公式确实会拦）。

### 3.1.3 翻页栏淡出时第一下点不动

判据是 `_lastDown` 引用相等，而 `MouseLeave` 会把它清成 null。
触摸时手指按住稍微滑一点就触发 `MouseLeave`，紧接着的 `MouseUp` 判失败，这一下被丢掉。

翻页栏是全仓唯一只有鼠标事件、没有触摸事件的关键控件。
（对照：`MiniWhiteboardWindow` / `NewStyleTimerWindow` / `PPTQuickPanel` 都写了 `TouchDown`）

**修法**：改用按下标记 `_previousPressed` / `_nextPressed` / `_pagePressed`，
只在 MouseDown 置位、MouseUp 消费，不受 MouseLeave 影响；
同时补 `PreviewTouchDown` / `PreviewStylusDown` 唤醒淡出。

### 3.1.4 翻页栏淡出没有透明度设置

`PPTNavBarAutoHideOpacity` 在 Settings 里一直存在、默认 0.08、也一直在下发，
但从来没暴露到界面上。

**修法**：四语 resx 键 + Designer 属性 + Slider 控件 + 读写绑定。
**验证**：设置页探针里 `PPTPageFlipPreviewPage` 的 sliders 从 16 变 17。

### 3.1.5 PPT 放映后没反应（权限）

ICC 以管理员启动、PowerPoint 是普通权限，Windows 的 UIPI 切断跨完整性级别的 COM
（`GetActiveObject` → `MK_E_UNAVAILABLE`）。这不是偶发，是这个配置下的必然结果。
原来只写一行 Warning 日志，界面上完全静默。

**重要事实**：`app.manifest` 里是 `requestedExecutionLevel="asInvoker"`，
**程序自己从不请求提权**。所以「管理员」只可能来自：
1. 快捷方式属性 → 兼容性 → 勾了「以管理员身份运行」
2. 兼容性疑难解答自动加的 `AppCompatFlags\Layers` 注册表项

**修法**：
- `PptBridge` 加 `IsPermissionFailure` + `PermissionBlocked` 事件。
  三条同时成立才报（没连上 + 确实在放映 + 原因是权限），同一状态只报一次
- 接口 `IPPTLinkManager` 同步声明
- 主窗口收到事件弹提示，写清怎么办
- 新增 `Helpers/ShortcutAdminFlag.cs`，用 ShellLink COM 读写快捷方式的
  `SLDF_RUNAS_USER` 位（`IWshRuntimeLibrary` 没有这个标志位，必须用 `IShellLinkDataList`）
- 设置 → 高级 → 「快捷方式的管理员标记」，点「检查」扫桌面+开始菜单。
  **这个检查放在设置里不放启动流程**，它要扫几百个 lnk（实测 66~98ms）

### 3.1.6 激光笔渐隐动画太快

反馈是「动画不完整」，实际是**淡出太短看不见**。

`InkFadeManager.AddFadingStroke` 里当 `strokeDurationMs > 0` 时，
拿「这一笔写了多久」当淡出动画的长度。激光笔写一笔 100~300ms，
淡出就只剩 20~300ms。设置里的「渐隐时间」（默认 3000ms）只在 else 分支被用到。

**修法**：淡出时长只看用户设置。顺带修了一处：`AnimationDuration` 从来没读过配置，
一直写死 2000ms；现在取 `InkFadeTime` 的一半（下限 200ms，上限 5 秒）。

**验证**：`FADE-DUR-1`，直接调 `AddFadingStroke` 传 150ms 的书写时长，
再读它登记下来的动画时长。修复后 2000ms → PASS；把 bug 改回去 150ms → FAIL。

---

# 四、还没解决的（照实说，别当成已修）

## 4.1 设置页内存：已追到根因，修法待实施 ⚠️

**这是当前最重要的一条。**

### 结论

`iNKORE.UI.WPF.Modern 0.11.0` 的 `ItemsRepeater`（从 Windows UI Library 移植）
**自身泄漏**。它被钉住，连带钉住整个控件树。

`SettingsExpander` 的模板里有一个 `ItemsRepeater`，所以用 Expander 的页面全漏。
`SettingsCard` 模板里没有，所以不漏——这个差异正好对上。

### 证据（最小复现，每组造 30 个实例，Measure 后强制全量 GC 三次）

```
普通 Border            存活  1/30
SettingsCard           存活  1/30   ← SettingsExpander 的父类，不漏
ToggleSwitch           存活  1/30
SimpleStackPanel       存活  1/30
SettingsExpander       存活 30/30
ItemsRepeater 单独      存活 30/30   ← 根因（100 个样本时 100/100）

SettingsExpander 只构造不 Measure    存活  0/30
SettingsExpander 只 ApplyTemplate    存活 30/30   ← 分界点就是这一步
```

模板展开后有 53 个元素、19 种类型，其中 `ItemsRepeater × 1`。

### 影响面

每打开一次设置窗口：24 个页面里 10 个泄漏，约 33MB。**关窗不恢复**
（`_pages.Clear()` 只是丢掉引用，对象本身被 ItemsRepeater 钉住）。
老师反复开关设置十次就是 330MB。

### 试过但无效的修法（别再试）

| 修法 | 结果 |
|---|---|
| 给 `SettingsExpander.Items` 显式赋空集合 | 30/30 仍存活 |
| 在 SettingsWindow 三个创建点遍历页面补 Items | 无效（且探针绕过那三个点，压根没执行） |
| 把 `SpringAnimator` 静态缓存改成弱引用 | 本身是真实泄漏，但和这个无关 |
| 关掉全部动画 | 泄漏量一点没变 |

### 已经排掉的假设（用转储 + 强 GC 判据验过）

- 不是 GC 延迟：强制全量回收 3 次，回收 0MB，代计数 122/58/29
- 不是静态字段持有：扫 ICC 全部程序集静态字段，无
- 不是全局资源字典：92 个合并字典 / 3293 条，不增长
- 不是布局队列：306 个 Request 的 Target 全是 null（已处理完的空壳）
- 不是连着长命对象：反向走三层实例字段，没连到
- 不是动画时钟累积：转储里 1.8 万个动画对象只是症状

### 工具这一路为什么走不通

`dotnet-dump` 的 `gcroot` 在这个环境下**对任何对象都返回 `Found 0 unique roots`**
——包括明明活着、强制 GC 都收不掉的对象。和事实矛盾，工具本身不可信。
（试过 SettingsWindow、CanvasPage、SettingsExpander、DoubleAnimationUsingKeyFrames、Style，全部 0。）

## 4.2 那 2 条 WFO2000 不要改

`WindowOverviewModel.Windows` 的 getter 在锁内复制一份返回。
这个「每次 new」**是必须的**——集合有后台定时器在改，直接返回内部引用会让调用方
撞上 `InvalidOperationException: Collection was modified`。改了反而引入并发 bug。

## 4.3 问题 1/2/3 没做真机验收

这三条都跟 PPT 放映有关，开发机上跑不了完整流程。
**「编译过 + 逻辑对」不等于「功能验收通过」。** 班测才算数。

## 4.4 两个大文件没拆

`MW_TouchEvents.cs` 相关的触摸逻辑、`App.xaml.cs` 的启动流程，耦合度高。
继续拆的收益在递减。

---

# 五、ta 的最新指示：给 iNKORE 打本地补丁

这是 2026-11-19 11:57 定的方向。**下一个自己从这里接着做。**

## 为什么选这条路

三个选项：
1. `SettingsExpander` → `SettingsCard` + 自己写展开收起（涉及 10 页 107 处 XAML）
2. **打本地补丁**（ta 选的）
3. 等上游修（0.11.0 已是本地最新）

## 具体做法（我没开始，以下是我的计划，未验证）

### 路线 A：fork 模板，覆盖 `SettingsExpander` 的默认样式

iNKORE 的 `SettingsExpander` 模板里那个 `ItemsRepeater` 是用来显示展开后子项的。
我们的用法里，`Items` 装的是 `SettingsCard`（静态内容，不需要虚拟化）。

思路：在项目里定义一个新的 `SettingsExpander` 样式，`BasedOn` iNKORE 默认样式，
但把模板里的 `ItemsRepeater` 换成一个普通 `ItemsControl` 或 `StackPanel`。

**难点**：不知道 iNKORE 默认样式的 `x:Key`。需要先找出来。
可以试 `typeof(SettingsExpander)` 作为隐式样式的 key（WPF 默认行为），
或者用 `{x:Type ui:SettingsExpander}`。

**验证方法**：改完后跑 `--stress-settings 3`，看前三轮内存是不是 22MB 不变。

### 路线 B：直接替换 `ItemsRepeater` 的 DLL

用 `ILSpy`/`dnSpy` 反编译 `iNKORE.UI.WPF.Modern.Controls.dll`，
找到 `ItemsRepeater` 的泄漏点，改 IL，重新签一个同版本号的 DLL 放项目里覆盖。

**风险**：反编译改 IL 很容易出错，而且升级 iNKORE 时会丢失。
除非路线 A 走不通，不建议。

### 路线 C：反射替换模板里的元素

运行时找到 `SettingsExpander` 的模板，把 `ItemsRepeater` 那个节点替换掉。
太 hack，不推荐。

## 建议的下一步（第一个动作）

**先找出 iNKORE 默认样式的 key，确认路线 A 可行。**

```csharp
// 在探针里加一小段，把这个打出来
var key = typeof(iNKORE.UI.WPF.Modern.Controls.SettingsExpander);
var style = Application.Current.TryFindResource(key);
Log("隐式样式: " + (style == null ? "null" : style.GetType().Name));
```

如果 `TryFindResource(typeof(SettingsExpander))` 能拿到样式，路线 A 就成立。

## 判据（做完必须验这三条）

1. `--probe-expander` 里 `SettingsExpander` 的存活从 30/30 降到个位数
2. `--stress-settings 3` 内存从 56→121MB 变成 22MB 不变（和 nolayout 对照组一样）
3. `--smoke-test` 仍然 PASS=57 FAIL=0，`--dump-settings-pages` 仍然 24/24

**三条缺一条都算没修好。**

---

# 六、我这一轮犯的错（写给自己）

## 错误 1：读需求时把并列读成因果，或者把「保持」读成「不做」

- 班测说「设置页有问题，**且**都无法用触摸屏翻页」→ 我读成「因为滚不动所以有问题」，查了一上午
- 需求说「穿透态下点页码**保持**即可，不需要遵循后面的逻辑」→ 我写成 `if (!IsNotating) return;`

判据：需求里出现「保持」「不需要」「保持现状」这类词时，
先问一句**「具体是不要做什么、什么必须照做」**，而不是笼统地 return。

## 错误 2：判断「某设置有没有界面入口」只查了一处

我说「浮动栏淡出设置界面上没有入口」，因为我只 grep 了 XAML。
实际 `PPTPageFlipPreviewPage.xaml.cs` 里有完整的 ToggleSwitch + 两个 Slider。

判据：判断某设置项有没有界面入口，必须同时查三处——
`.xaml` 里的控件名、`.xaml.cs` 里的 `Xxx.IsOn = settings.Yyy`、页面 Load 方法。

## 错误 3：脚本把 CRLF 文件压成一行

判了 `crlf` 却用 `'\n'.split()`，1742 行的 `PptBridge.cs` 被压成 117 行。

判据：按行改文件的脚本，split/join 必须用同一个 nl 变量。
CRLF 文件先归一化成 `\n`，处理完再还原。
**脚本跑完行数变化超出预期就立刻 `git checkout` 回滚。**

## 错误 4：把等待时间塞进同步 bash 命令

`sleep 150` + `job_output(wait:true)` 两次撞 120 秒上限。

判据：超过 90 秒的任务用 `run_in_background`，下一轮 `job_output({wait:false})` 查一次。

## 错误 5：给子智能体报的数字是估的

我报「删了约 1900 行」，实际 1301 行。

判据：报任何累计数字之前，逐个 `git show --numstat` 累加。

## 错误 6：用 `git add -A`

把子智能体正在编辑的文档一起提交进了语义不对的 commit。

判据：多智能体共享工作目录时，**永远不要用 `git add -A` / `git add .`**。

## 错误 7：把「查过一遍没找到」当成「不存在」

写文档时报的数字全是凭记忆估的，复核时 6 处全错（281→276、4 条警告→2 条等）。

判据：文档里的每个数字都是「断言」，不是「描述」。
写之前现跑一遍命令，把命令和输出一起贴进去；跑不了的就删掉，或者写明「未复核」。

## 错误 8（2026-11-19 新增）：对照实验里异常被空 catch 吞掉，把「没跑到」当成「修好了」

做 `SettingsExpander` 的 Items 实验时，用 `Activator.CreateInstance(IList)` 造空集合。
`Items` 是接口，`Activator.CreateInstance` 抛异常，被 `catch { }` 吞掉。
那 30 个对象既没设值也没 Measure——**不 Measure 当然不漏**。
我得出了「补空 Items 就不漏」的结论，还据此写了一整套 96 行的修复代码。

**判据**：写对照实验时，异常不许空 catch。
实验里每个 try 必须记录成功/失败次数，并在结论行里打出来（「设值成功=30 失败=0」）。
如果实验组的成功次数不是预期的数量，那组数据作废，不能拿来支持任何结论。

## 错误 9（2026-11-19 新增）：修复接在 A 处，测试走 B 处

把「补空 Items」的修复接在 `SettingsWindow` 的三个页面创建点上。
但探针用 `Activator.CreateInstance` 直接造页面，**绕过了那三个点**。
修复一次都没执行（补空日志一直是 0），我却用探针数据判断「修复无效」。

**判据**：接修复之前先问「这条路径有几条入口，测试用的是哪一条」。
如果修复点只在 A、测试走 B，那测试结果对修复无效。
要么把修复下沉到两条路径都会经过的地方，要么在测试里复用 A 的同一段代码，
**并且用日志确认它真的执行了**（打执行次数，不是打「应该执行」）。

---

# 七、协作方式（如果还要派子智能体）

## 分工原则

**写作用域必须不相交。** 我在 team_task 里明确写「只能改这两个文件」，
并在给子智能体的 prompt 里重复一遍。

## 它们比我想的强，但交付要自己验

- `audit-struct` 纠正我「MainWindow 35 个 partial 不是 37」
- `audit-debt` 拦住我「18 处 ConvertBack 抛异常不是债，是 WPF 单向绑定标准写法」
- `audit-dead` 指出我的「文件名 grep」方法本身有缺陷
- `docs-keeper` 三次核出我给的判断已过期

**但它们也会出问题**：`nav-refactor` 自曝「用行号切片删代码时把文件尾部截断了一次」。
所以交付后必须自己跑一遍完整验证。

---

# 八、环境事实（记牢，别重复踩）

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

## 诊断工具（这一轮装的，还在）

```
~/.dotnet/tools/dotnet-gcdump.exe   堆统计
~/.dotnet/tools/dotnet-dump.exe     转储 + SOS
~/.dotnet/tools/dotnet-trace.exe    ETW 采样（装了但没用过）
```

`dotnet-dump` 的 `gcroot` 在这个环境不可信（见 4.1）。
`dotnet-gcdump report` 只给堆统计，给不了引用链。

## Git

```bash
GIT_ASKPASS=/e/DSH/tmp/git-askpass.sh \
  git push "https://Baimacao@github.com/Baimacao/community-ltsc.git" ltsc
```

**必须带显式 URL**——全局有 `url.<base>.insteadOf` 会把它改写成只读代理。
token 在 `E:/DSH/home/.github-token`，**不要写进 .git/config**。
网络偶尔抖动（`Recv failure: Connection was reset`），写个重试循环就能过。

## 版本号

改 `InkCanvasForClass.csproj` 里 `SetAssemblyInformationalVersion` 那个 Target：

```xml
<AssemblyInformationalVersion>0.1c-daily+$(_ShortSha7)</AssemblyInformationalVersion>
<AssemblyFileVersion>0.1.0.0</AssemblyFileVersion>
```

**顶层写 `<FileVersion>` 无效**——被 NBGV 覆盖。

改版本号时要同步这 9 个文件：
`README.md` `README-en.md` `Manual.md` `UpdateLog.md` `docs/BUILD.md`
`docs/NEXT-SESSION-PROMPT.md` `.github/ISSUE_TEMPLATE/01-bug_report.yml`
`.github/ISSUE_TEMPLATE/03-bug_report.md` `InkCanvasForClass.csproj`

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

# 九、ta 让我遵守的事（原话）

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
7. **版本号和发 Release 只在 ta 说的时候做**

---

# 十、下一步可以做什么

按优先级：

**A. 给 iNKORE 打本地补丁** ← **ta 定的方向，从这里开始**

第一个动作：验证 `Application.Current.TryFindResource(typeof(SettingsExpander))`
能不能拿到默认样式。拿得到就走路线 A（fork 模板换掉 ItemsRepeater）。

判据见第五节。

**B. 等班测反馈** —— 问题 1/2/3 只有真机能验

**C. 真机验收** —— 翻页笔实物按键（N-03），只能 ta 试

---

# 十一、现在该做的第一件事

跑一遍基线，确认环境没坏：

```bash
taskkill //F //IM ICCCELite.exe 2>/dev/null | head -1
cd "E:/B1807/Documents/icc-lts/src/Ink Canvas"
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvasForClass.csproj" -c Release -p:Platform=x64 --nologo -v q
# 期望：0 错误，2 个 WFO2000（grep warning 会看到 4 行，是重复报告）

cd bin/Release/x64/net11.0-windows10.0.19041.0
rm -f Logs/*.txt
./ICCCELite.exe --smoke-test --skip-mutex-check 2>&1 | head -2
sleep 32
grep -E "冒烟自测结束" "$(ls -t Logs/*.txt | head -1)"
# 期望：PASS=57 FAIL=0
```

对得上再动手。对不上先查环境。
