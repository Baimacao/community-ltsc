# ICC CE Lite — i18n 四语化改造进度

> 上下文压缩后续接用。最后更新：**2026-11-17**（原文写 2026-11-16 15:12）

## 当前状态

> **本节数字全部重测（2026-11-17）。** 原文写「37 文件 / 1993 key / 梗体分批重写中」，
> 那是 2026-11-16 15:12 的快照。之后 5 个提交把四语结构收口了，五组数字全部变了。
> 旧值留档见本节末「数字变化轨迹」。

分支 `ltsc`（原名 `ltsc/1.7.19.7`）。

### 四语结构（当前实测）

| 语言码 | 文件数 | key 数 | 说明 |
|---|---|---|---|
| `zh-CN` | 31 | 1317 | 简体基准（`*.resx`），含 1 个无多语言变体的 `Resources.resx` |
| `zh-TW` | 30 | 1311 | 繁体中文，本轮新建 |
| `zh-ME` | 30 | 1311 | 梗体中文，**已全量完成** |
| `en-US` | 30 | 1311 | English |

四套 key 集合**完全一致**。2026-11-17 实测（`E:/DSH/tmp/check_all.py`）：

```
不一致总数: 0
默认语言(基准 zh-CN) 的 key 总数: 1311
```

另两个检测脚本同为 0：

```
find_trad3.py   -> 总数: 0     （梗体里混入的繁体字）
find_english.py -> 总: 0       （梗体里写成英文的条目）
```

> `zh-CN` 的 1317 比其余三语的 1311 多 6 条，多出来的是 `Resources.resx` 里的条目
> ——该文件没有 `zh-TW`/`zh-ME`/`en-US` 变体，不参与四语比对，所以「不一致总数 0」仍成立。

### 关键改造

1. **`zh-ME` 拆成两个码**
   原来 `zh-ME` 既当繁体又当梗体（2130 条里 388 条混着繁体字、1742 条纯简体无梗）。
   现在 `zh-TW` 繁体、`zh-ME` 梗体，各司其职。

2. **`zh-TW` 全量生成**（2131 条）
   - 台湾用语表 74 条（`软件`→`軟體`、`网络`→`網路`、`鼠标`→`滑鼠`、`文件夹`→`資料夾`、`插件`→`外掛`）
   - 字形转换走 Windows `LCMapString`（经 `Microsoft.VisualBasic.StrConv`），不手写映射表
   - 补正一对多字（`之后`→`之後`、`这里`→`這裡`、`干净`→`乾淨`）
   - 脚本：`E:/DSH/tmp/build_tw.py`、用语表 `E:/DSH/tmp/tw_terms.json`

3. **语言选择接入**
   - `csproj`：`EmbeddedOnlyCultures` 加 `zh-TW`，加 `EmbeddedResource Update="Properties\*.zh-TW.resx"`
   - `LocalizationHelper`：`EmbeddedOnlyCultures` 和 `SupportedLanguages` 各加 `zh-TW`
   - 设置页语言下拉索引重排：**0=跟随系统 1=简体 2=繁體 3=梗体 4=English**
   - OOBE 语言下拉：原来「繁體中文」标签指向 `zh-ME`（错位），改指 `zh-TW`，另加「梗体中文」项
   - 新增 `Theme_Language_ChineseTraditional` 文案（四语）

## 梗体重写进度

> **本节已整体完成（2026-11-16 15:52，提交 `6756fcbf`）。**
> 原文标题是「梗体重写进度」并列出「已完成 489 条 / 待改约 1504 条」——
> 那是进行中的状态。全量完成后：**1312 条，其中 1034 条改写 + 278 条保留原样**。
> 下面的分批明细留档，是当时的实际改法记录。

### 分批明细（当时已完成的第一批，489 条）

| 文件 | 原 | 现 | 改动 |
|---|---|---|---|
| OobeStrings | 132 | 80 | 改 58 + 删 52 |
| CanvasStrings | 116 | 98 | 改 81 + 删 18 |
| FloatingBarStrings | 203 | 174 | 改 83 + 删 29 |
| SettingsStrings | 82 | 78 | 改 80 + 删 2 |
| NavStrings | 80 | 59 | 改 43 + 删 21 |
| MainWindowStrings | 174 | 158 | 删 16 |
| TimerStrings | 36 | 36 | 全改 |
| HomeStrings | 27 | 27 | 全改 |
| CommonStrings | 13 | 13 | 全改 |
| GestureStrings | 9 | 6 | 改 6 |
| BtnStrings | 3 | 3 | 改 1 |
| MainStrings | 4 | 4 | 改 4 |

### 待改（约 1504 条）—— 已清零

> **这一节已完成，不再是待办。** 保留原文是为了留档当时的剩余清单。
> 全量完成于 `6756fcbf`。想知道现在还剩什么，看本节末尾「收口后实际删掉的整类」。
>
> 原文（2026-11-16 15:12 状态）：剩余约 1504 条未改，按大小排如下。

按大小排：

| 文件 | 未改条数 | 备注 |
|---|---|---|
| ThemeStrings | 143 | **主题预设名是产品设计的一部分**，不能机械翻译 |
| RandomStrings | 117 | 点名相关 |
| PPTStrings | 86 | |
| AutomationStrings | 85 | |
| StorageStrings | 85 | |
| AdvancedStrings | 71 | |
| NotificationStrings | 71 | |
| UpdateStrings | 69 | |
| PluginStrings | 72 | 插件系统已删，可能整类可删 |
| CloudStorageStrings | 63 | |
| HotkeyStrings | 55 | |
| FeedbackStrings | 54 | 反馈页已删，可能整类可删 |
| SecurityStrings | 49 | |
| PerformanceStrings | 43 | 性能页已删，可能整类可删 |
| StartupStrings | 42 | |
| 其余小文件 | 约 200 | |

**整类可删的候选**（页面/功能已不存在）：
`FeedbackStrings`（162 key 三语）、`PluginStrings`（216）、`CloudStorageStrings`（189）、`PerformanceStrings`（129）、`AnnouncementStrings`（57）、`PopupStrings`（空）、`UpdateCenterPanelStrings`（空）

#### 收口后实际删掉的整类（2026-11-17 核实）

上面 7 个候选**全部已删**，提交 `f37f8d8d`（N-07 清空壳 resx）：

```
for g in FeedbackStrings PluginStrings CloudStorageStrings PerformanceStrings \
         AnnouncementStrings PopupStrings UpdateCenterPanelStrings; do
  ls "Ink Canvas/Properties/$g.resx"
done
# 七个全部 "No such file or directory"
```

连 `.resx` × 4 语言 + `.Designer.cs` 一起清掉，`Properties/` 下的 resx 从 149 个降到 121 个。

### 数字变化轨迹（为什么对不上旧文档）

| 时点 | 提交 | zh-CN 文件/key | zh-ME 文件/key | 发生了什么 |
|---|---|---|---|---|
| 2026-11-16 15:12（文档写就） | `c26e5656` | 38 / 1999 | 37 / 1993 | 梗体进行中，489/1993 已改 |
| 清死文案 | `eb13677b` | 38 / 1313 | 37 / 1307 | 删 686 条死文案（1993 → 1307） |
| 梗体全量完成 | `6756fcbf` | 38 / 1318 | 37 / 1312 | 1312 条收口（1034 改写 + 278 保留） |
| 清空壳 resx | `f37f8d8d` | 31 / 1326 | 30 / 1320 | 删 7 个空壳类，文件数 -7 |
| 现在 | HEAD | 31 / 1317 | 30 / 1311 | 又删 9 条（隐私协议等） |

**所以文档里「37 文件 / 1993 key / 待改 1504 条」三个数都不是写错了，
是写完之后又被 5 个提交改掉了。**

## 梗体风格约定（ta 定）

**「有梗写梗，没梗口语」，程度是中等玩梗**：

- ✅ 好：`退出`→`溜了溜了`、`全屏`→`全屏起飞`、`隐私`→`小秘密`、`工具栏`→`装备栏`、`首页`→`大本营`、`组件属性`→`这个组件怎么设`、`最小宽度`→`最窄多少`
- ❌ 过头：`确定`→`冲！`（改回`就这么定`）
- ❌ 不是梗还拗口：`开/关`→`启/闭`（改回`开/关`）
- ❌ 叠词太多：`窗口`→`小窗窗`（改回`窗口`）
- ✅ 保持原样：按钮名（批注/白板/鼠标/笔/橡皮/撤销/重做）、技术术语（尺寸/对齐/透明度）、功能标签（新密码/全屏截图/切换摄像头）

**错误信息和功能标签保持准确，不做梗。**

## 三个检测脚本（每次改完必跑）

| 脚本 | 用途 | 期望输出 |
|---|---|---|
| `E:/DSH/tmp/check_all.py` | 四套语言 key 一致性 | `不一致总数: 0` |
| `E:/DSH/tmp/find_trad3.py` | 梗体里混入的繁体字 | `总数: 0` |
| `E:/DSH/tmp/find_english.py` | 梗体里写成英文的条目 | `总: 0` |

辅助脚本：

- `E:/DSH/tmp/dump_batch.py <类名>` — 导出简/梗对照
- `E:/DSH/tmp/dump_only_diff.py <类名>` — 只列未改条目
- `E:/DSH/tmp/dead_base.py <类名>` — 查死文案（源码零引用）
- `E:/DSH/tmp/apply_meme.py <json>` — 批量写入 zh-ME
- `E:/DSH/tmp/del_keys.py` + `del_keys2.py` — 删除 key（后者处理紧凑格式的 zh-TW）

## 踩过的坑

1. **繁体检测误报**（两版都错）
   - v1/v2 手写繁体字表，把 `秒/超/窗/查/版/安/限/滑/截/透` 这些**简繁同形**的字也列进去，误报 687 条和 357 条
   - v3 正解：先用 `zh-CN` 收集所有出现过的汉字，再从繁简表里排除 → 剩 178 个「真繁体」→ 检出 30 条全部真实

2. **`zh-TW` 删除静默失败**
   - 生成时用 ElementTree 序列化，写成了紧凑格式（`</value></data><data ...` 同一行）
   - `del_keys.py` 的正则要求 `</data>` 后跟换行，匹配不到但**报告成功**（文件长度没变）
   - 补 `del_keys2.py`（XML 解析方式删）才真正删掉

3. **带特殊符号的 key**
   - `CanvasAndInkPanel_1920×1080Hint` 的 `×` 在 Designer 里被转成 `_1080`（属性名 `CanvasAndInkPanel_1920_1080Hint`），常规脚本清不掉，需单独处理

4. **字符串替换要验证真的改到**
   - 多次出现「脚本报成功但文件没变」，必须用 `includes()` 或文件长度逐条验证

## 编译与验证命令

```bash
# 构建前必须先停程序，否则 exe 被锁 MSB3027
powershell.exe -NoProfile -Command "Get-Process ICCCELite -ErrorAction SilentlyContinue | Stop-Process -Force"

cd "E:/B1807/Documents/icc-lts/src/Ink Canvas"
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvasForClass.csproj" -c Release -p:Platform=x64 --nologo -v q
```

## 本轮其它已完成的工作

| 项 | 提交 |
|---|---|
| N-05 PPT 翻页组件深浅同步（判定源改用应用主题 + 切换时刷新 UserControl），删黑底设置项 | `f399f9b8` |
| N-06 界面 logo 跟随深浅主题（资源放 Light/Dark.xaml，6 处 Image 改 DynamicResource，新增 `icc-dark.ico`） | `496dd40a` |
| 计时器重做（折叠成纯数字、小窗模式、数字加粗、冒号对齐） | `3941dda3`~ `da82d3f4` |
| 删除旧版计时窗口 `CountdownTimerWindow`（860 行）+ 两个样式分叉开关 | `e6f2518b` |
| 管理员重启修复（`Environment.ProcessPath` 取路径） | `b55a127f` |

## 下次继续时

> **这一节已过期：四语化改造已收口，没有「下一批」了。**
> 原文第 3 条让下一个人「继续 `ThemeStrings`（143 条）」，但 `6756fcbf` 已把梗体全量做完，
> `ThemeStrings` 在那批里一并处理了。保留原文留档。

原文（2026-11-16 15:12）：

1. 读这份文档
2. 跑三个检测脚本确认当前状态
3. 继续 `ThemeStrings`（143 条）—— 注意主题预设名是产品设计，需要逐个看，不能机械翻
4. 每批做完：编译 → 跑三脚本 → 提交

**现在的状态**：i18n 四语化已完成。若日后新增文案，流程仍是第 2、4 条（跑脚本 + 编译验证）。

---

## 附：本文件仍然有效的部分

以下内容与进度无关，是**方法论沉淀**，不随进度过期，改文案时仍然适用：

| 章节 | 为什么还留着 |
|---|---|
| 「梗体风格约定」 | 新增文案要按这个调性写，是长期约定 |
| 「三个检测脚本」 | 每次改完必跑，脚本仍在 `E:/DSH/tmp/`（已核实三个都存在） |
| 「踩过的坑」 | 繁体检测误报、zh-TW 删除静默失败、特殊符号 key —— 再碰会再踩 |
| 「编译与验证命令」 | 构建前停进程否则 MSB3027，命令仍有效 |

**只有「当前状态」「梗体重写进度」「下次继续时」三块是进度快照，会过期。**
