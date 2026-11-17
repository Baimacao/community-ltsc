# LTSC 基线记录

> 这份文档回答一个问题：**我们这个版本是从上游哪个点分出来的，怎么验证。**
> 上游一旦更新，必须靠它判断哪些是本分支的、哪些是新来的。

---

## 上游分叉点

| 项 | 值 |
|---|---|
| commit | `083f694efd65b7a3ce890d4f7d0efb7adcf77322` |
| 短 hash | `083f694e` |
| 作者 | CJK_mkp |
| 日期 | 2026-07-15 17:32:59 +0800 |
| 提交信息 | 更新版本号 |
| 上游版本 | 1.0.0 |
| 上游仓库 | https://github.com/InkCanvasForClass/community |

这是上游方的最后一个提交。之后的所有提交都属于本分支。

---

## 分叉后做了什么

> **本节数字 2026-11-17 重测。** 原写「133 个提交 / 343 文件 / +15018 −43766」，
> 那是 2026-11-13 的分叉点统计（当时文档最后一次更新）。
> 之后本分支又走了 92 个提交，四个数字**全部过期**，已按实测改。

分叉后共 **225 个提交**（`git rev-list --count 083f694e..HEAD`）：

| 阶段 | 作者 | 提交数 | 时间范围 | 说明 |
|---|---|---|---|---|
| 早期审计与修复 | whale | 39 | 2026-11-11 → 11-12 | PPT 联动与稳定性 |
| LTSC 重构 | ICC-LTSC | 180 | 2026-11-10 → 11-17 | 改造成 LTSC 分支，主力 |
| 发布与收尾 | Baimacao | 5 | 2026-11-17 | 改名、版本号、sorrow 定版 |
| 文档 | docs-keeper | 1 | 2026-11-17 | 本文件同批的文档合并 |

> 注：`whale` 的提交日期与 `ICC-LTSC` 有重叠，因为 `whale` 的提交是在审计后补录的历史。
> 表格顺序按提交数排，不按时间排。

累计改动：**539 个文件，+42462 / −78357**（`git diff --shortstat 083f694e..HEAD`）。

删除远多于新增，因为砍掉了整块功能（点名、遥测、WindowChrome、自动更新、PPT Agent 等）。

> **原数字留档**（2026-11-13 时点）：133 个提交，343 个文件，+15018 / −43766。
> 当时 whale=39、ICC-LTSC=94。

---

## 怎么验证这份记录是对的

在仓库根目录执行：

```bash
# 1. 确认上游分叉点（这条是历史事实，永久不变）
git log 083f694e -1 --format="%h %an %ad %s" --date=iso
# 期望输出：083f694e CJK_mkp 2026-07-15 17:32:59 +0800 更新版本号

# 2. 确认分叉后没有非本分支的提交
git log 083f694e..HEAD --format="%an" | sort -u
# 2026-11-17 实测：Baimacao / ICC-LTSC / docs-keeper / whale
# （原期望只写「ICC-LTSC、whale」——那是 11-13 时点，之后新增两个作者）

# 3. 确认统计数字（会随时间增长，不是固定值）
git rev-list --count 083f694e..HEAD       # 2026-11-17 实测 225
git diff --stat 083f694e..HEAD | tail -1  # 2026-11-17 实测 539 files changed
```

**第 1 条永久有效；第 2、3 条是随时间变的快照，别当固定值核对。**
原来这里写「三条都对得上，说明这份记录没写错」——这个说法本身有问题：
第 2、3 条只要再提交一次就会「对不上」，那不是记录错了，是记录过期了。
现在只用第 1 条判断记录是否可靠，第 2、3 条改成「当前值应在什么量级」。

---

## 怎么判断某个改动是不是我们的

当用户报告一个问题时，先跑：

```bash
git log 083f694e..HEAD --oneline -- <出问题的文件路径>
```

- **有输出** → 我们动过这个文件，先怀疑自己
- **无输出** → 上游原始代码，问题在上游

这条判据比「凭印象觉得是我们改的」可靠。

---

## 怎么回合上游的新提交

上游更新后，不要直接 `merge`。步骤：

```bash
# 1. 拉上游
git fetch origin

# 2. 看上游新增了什么
git log 083f694e..origin/HEAD --oneline

# 3. 只挑与本分支冲突面小的提交，逐个 cherry-pick
git cherry-pick <hash>
```

**为什么不直接 merge**：本分支删掉了整块功能（点名、遥测），
上游如果还在这些地方改动，merge 会产生大量冲突，且容易把砍掉的东西带回来。

---

## 版本号是怎么来的

> **本节三条结论全部被推翻，2026-11-17 重写。** 原结论与现在的代码不符，
> 旧内容留在本节末尾「原结论与为什么改」里，不删。

上游用 Nerdbank.GitVersioning（配置在 `version.json`），但它只认四段数字，
`0.1a` 这种带字母的写法进不去。

**做法**：不改 `version.json`（仍是 `1.0.0`），在 csproj 的 MSBuild Target 里
覆盖 NBGV 生成的程序集版本属性，位于 `InkCanvasForClass.csproj:612-625`：

```xml
<AssemblyInformationalVersion>0.1a+$(_ShortSha7)</AssemblyInformationalVersion>
<AssemblyFileVersion>0.1.0.0</AssemblyFileVersion>
```

还有一处 `<AssemblyVersion>0.1.0.0</AssemblyVersion>`（:73）。

2026-11-17 实测值（来源：提交 `4958b92e` 的实测记录）：

| 字段 | 值 |
|---|---|
| AssemblyVersion | 0.1.0.0 |
| FileVersion | 0.1.0.0 |
| ProductVersion | `0.1a+<短 hash>` |
| AssemblyName | `ICCCELite` |

### 原结论与为什么改

| 项 | 原结论（2026-11-13） | 现在实际 | 什么时候变的 |
|---|---|---|---|
| ProductVersion | `1.0a-ltsc+<短 hash>` | `0.1a+<短 hash>` | `bea45925` 产品改名 ICC CE Lite，版本号进 0.1a |
| FileVersion | `1.0.0.19`（随构建递增） | `0.1.0.0`（固定） | 同上，改为在 Target 里固定写死 |
| AssemblyName | `InkCanvasForClass`，且「不能改」 | `ICCCELite` | `4958b92e` 进程名改为 ICCCELite |

**为什么第 3 条原来说「不能改」后来却改了**：原文理由是「改了会让检测原版是否在运行的逻辑
失效，也会影响用户已装的快捷方式」。`4958b92e` 的处理方式是**两条都改对**：

- 自检逻辑改成同时查两个名字（`App.xaml.cs` 里 `GetProcessesByName("ICCCELite")` 与
  `GetProcessesByName("InkCanvasForClass")` 合并），老版本仍能被识别；
- 文件关联用 `Process.GetCurrentProcess().MainModule.FileName` 动态取路径，
  没硬编码名字，下次启动自动重新注册成新 exe。

也就是说原结论担心的两个后果都成立，但**都可解**，所以改了。

### 没改的（原文没提，但容易误以为一起改了）

| 项 | 值 | 为什么不动 |
|---|---|---|
| AppUserModelID | `InkCanvasForClass.LTSC` | 用户看不见，改了反而出问题 |
| 互斥体名 | `InkCanvasForClass CE LTSC` | 同上 |
| Copyright | 上游 `CJK_mkp` | 不能改 |

---

## 构建环境

| 项 | 值 |
|---|---|
| SDK | .NET 11（11.0.100） |
| TFM | `net11.0-windows10.0.19041.0` |
| LangVersion | 14 |
| 平台 | x64 |

```bash
dotnet build "Ink Canvas/InkCanvasForClass.csproj" -c Release -p:Platform=x64
```

**注意**：不要用 `--target:Compile`。它跳过 XAML 代码生成，
会产生假的 `CS5001` 和 `InitializeComponent` 找不到的错误。

---

## 打包方式（当前）

**框架依赖**，不只是程序本体。

`runtimeconfig.json` 里写明需要：

- `Microsoft.NETCore.App` 11.0.0
- `Microsoft.WindowsDesktop.App` 11.0.0

**这意味着用户电脑必须装 .NET 11 运行时**，否则双击报错。

> 已实测自包含发布：275 个文件 / 207 MB（zip 后 96 MB），能跑。
> 安装程序的事推后处理，当前先保证程序本体。

**不裁剪（PublishTrimmed）**：WPF 用反射绑 XAML，裁剪后是运行时崩，
不是编译期报错——老师课堂上才会发现。这条路直接排除。
