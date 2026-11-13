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

分叉后共 **133 个提交**，按作者分两段：

| 阶段 | 作者 | 提交数 | 说明 |
|---|---|---|---|
| 早期审计与修复 | whale | 39 | 2026-11-11 至 2026-11-12，PPT 联动与稳定性 |
| LTSC 重构 | ICC-LTSC | 94 | 2026-11-10 至 2026-11-13，改造成 LTSC 分支 |

> 注：两个作者的提交时间有重叠，因为 `whale` 的提交是在审计后补录的历史。

累计改动：**343 个文件，+15018 / −43766**。

删除远多于新增，因为砍掉了整块功能（点名、遥测、WindowChrome 等）。

---

## 怎么验证这份记录是对的

在仓库根目录执行：

```bash
# 1. 确认上游分叉点
git log 083f694e -1 --format="%h %an %ad %s" --date=iso
# 期望输出：083f694e CJK_mkp 2026-07-15 17:32:59 +0800 更新版本号

# 2. 确认分叉后没有非本分支的提交
git log 083f694e..HEAD --format="%an" | sort -u
# 期望输出只有：ICC-LTSC、whale

# 3. 确认统计数字
git rev-list --count 083f694e..HEAD    # 期望 133
git diff --stat 083f694e..HEAD | tail -1  # 期望 343 files changed
```

三条都对得上，说明这份记录没写错。

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

上游用 Nerdbank.GitVersioning（配置在 `version.json`），但它只认四段数字，
`1.0a-ltsc` 这种写法进不去。

**做法**：在 csproj 里显式设 `AssemblyVersion` / `FileVersion`，绕开 NBGV。

实际效果（实测）：

| 字段 | 值 |
|---|---|
| FileVersion | 1.0.0.19（随构建递增） |
| ProductVersion | 1.0a-ltsc+<短 hash> |

`AssemblyName` **没改**，仍是 `InkCanvasForClass`。
改了会让「检测原版是否在运行」的逻辑失效，也会影响用户已装的快捷方式。

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
