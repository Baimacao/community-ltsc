# ICC CE Lite — i18n 四语化改造进度

> 上下文压缩后续接用。最后更新：2026-11-16 15:12

## 当前状态

分支 `ltsc/1.7.19.7`，最后提交 `635baec8`。

### 已完成的四语结构

| 语言码 | 文件数 | key 数 | 说明 |
|---|---|---|---|
| `zh-CN` | 37 | 1993 | 简体基准（`*.resx`） |
| `zh-TW` | 37 | 1993 | 繁体中文，本轮新建 |
| `zh-ME` | 37 | 1993 | 梗体中文，分批重写中 |
| `en-US` | 37 | 1993 | English |

四套 key 集合**完全一致**，校验脚本 `E:/DSH/tmp/check_all.py`，输出「不一致总数: 0」。

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

### 已完成（489 条）

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

### 待改（约 1504 条）

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

1. 读这份文档
2. 跑三个检测脚本确认当前状态
3. 继续 `ThemeStrings`（143 条）—— 注意主题预设名是产品设计，需要逐个看，不能机械翻
4. 每批做完：编译 → 跑三脚本 → 提交
