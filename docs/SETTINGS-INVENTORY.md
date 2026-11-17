# ICC CE Lite 设置项清单（2026-11-17 重生成）

> **这一版怎么数的**：不是数 XAML 里的控件，也不是数 `SettingsCard` 的数量——
> 而是「设置页代码里**真正读写到的 `Settings` 属性**」。
> 方法和边界写在文末「这一版没数全的地方」，**先看那一节再用这张表**。

| 项 | 值 |
|---|---|
| 生成于 | 2026-11-17 |
| 设置页总数 | **24**（`ls Windows/SettingsViews/Pages/*.xaml | wc -l`） |
| 页面代码里引用到的**唯一** Settings 项 | **183** |
| 按页相加（同一项被多页用到会重复计） | 186 |
| `Settings` **子类**声明的属性/字段总数 | 313（不含 `Settings` 自身，见「分母对不上」） |

---

## 各页绑定项数

| 页 | 绑定项数 |
|---|---|
| AutomationWorkflowPage | 42 |
| CanvasPage | 22 |
| PowerPointPage | 20 |
| AdvancedPage | 19 |
| ToolbarAppearancePage | 14 |
| WindowPage | 12 |
| PPTPageFlipPreviewPage | 11 |
| BoardAppearancePage | 8 |
| SidebarPage | 7 |
| TimerPage | 7 |
| StartupPage | 6 |
| WhiteboardTipsPage | 6 |
| SecurityPage | 5 |
| StoragePage | 3 |
| ToolbarPage | 2 |
| BoardToolbarPage | 1 |
| HotkeyPage | 1 |
| **合计（按页相加）** | **186** |

### 零绑定项的 7 个页（不是漏数）

| 页 | 为什么是 0 |
|---|---|
| HomePage | 纯导航页，50 个 `SettingsCard` 全是跳转入口，代码里 0 处 `Settings.` 引用 |
| FriendlyLinksPage | 2 个卡片是外链（Inkeys 等），本来就没有对应设置项 |
| AboutPage | 页面主体是作者名单，唯一条目在 XAML 里，代码侧无绑定 |
| PresenterKeyPage | 通过 `PresenterKeyAdapter.PersistToSettings` **间接**写 `Settings.PresenterKey`，不走 `Settings.X` 直接引用 |
| BoardMenuPage / SettingsPage / ToolbarMenuPage | 容器/宿主页，自身不绑定设置项 |

**这 7 个页不是「坏页」**，是它们不直接持有 `Settings` 引用。用「绑定项数」当健康度指标会误判。

---

## 这一版没数全的地方（必读）

### 1. 只数了「静态可解析」的引用

统计口径：把 `Resources/Settings.cs` 里每个 `public` 自动属性与公共字段解析成
「类 → 属性集合」，再去设置页的 `.xaml` / `.xaml.cs` 里匹配
`Settings.<类别>.<属性>` 或 `Settings.<属性>` 形态的引用。

**因此下列情况会被漏掉**：

| 漏掉的情形 | 例子 | 影响 |
|---|---|---|
| 反射 / 字符串键访问 | `SettingsManager.Get("canvas").Get("...")>` | 极少，未逐条排查 |
| 嵌套超过两层的路径 | `Settings.Canvas.Foo.Bar` | 本项目是两层结构，未见三层 |
| 通过中间变量取值 | `var s = Settings.Canvas; s.FitToCurve` | **会有漏**，未逐条核 |
| XAML 里直接绑定 Settings 的写法 | 本版未发现（设置页都走 code-behind） | 若日后引入 `x:Bind` 需重数 |

### 2. 分母对不上，且我没查清

`Settings` 各子类声明的属性/字段共 **313** 个，但设置页只引用了 **183** 个。
差 **130** 个，按理该逐个分类（「非 UI 的内部状态」还是「该有 UI 却没有」）。
**这一版没做这个分类**，所以：

> **不能从「313 vs 183」推出「有 130 个设置项缺失 UI」。**
> 差额里大概率混着大量本就不该有 UI 的内部状态（上次的修改时间、缓存、运行时标志等）。
> 要下这个结论，得逐条看过——**本轮没看，就不说。**

### 3. 与上一版（353 / 473）为什么差这么多

上一版（2026-11-11）数的是 **XAML 里的卡片与标题文本**，会把这些也算成「项」：

```
1. [ ] {x:Static props:AdvancedStrings.TouchMultiplier}  — {x:Static props:AdvancedStrings.TouchMul
2. [ ] (无标题)
3. [ ] (无标题)
```

即：`(无标题)` 占位卡片被计为一项、`SettingsExpander` 的嵌套子卡片被重复计一次
（父卡片一次 + 子卡片各一次）、同一字符串键在多处以不同截断长度重复出现。

而且那份文档**标题写「353 项」、正文写「合计 473 项」**，两个数自相矛盾——

```
$ head -1 docs/SETTINGS-INVENTORY.md
# ICC-CE 设置项全清单（353 项）
$ sed -n '7p' docs/SETTINGS-INVENTORY.md
合计 **473** 项
```

**所以 353 和 473 都不是「设置项数量」，是「XAML 卡片行数」的两种数法。**
本版改用「代码里真实读写的 Settings 属性」，与它们不可比。

### 4. 上一版还有 10 个页在代码里已不存在

上一版列了 28 个页，其中 10 个现在没有对应 `.xaml`：

```
AppearancePage  BackupPage  ClockPage  CloudStoragePage  DebugPage
InkRecognitionPage  NotificationPage  PerformancePage  PrivacyPage  RandomDrawPage
```

反过来，代码里有 7 个页上一版没列：

```
BoardMenuPage  BoardToolbarPage  HotkeyPage  PresenterKeyPage
SettingsPage  TimerPage  ToolbarMenuPage
```

重构（N-01 设置页整体重做）把设置树重排过，页面集合整个换了一遍。

---

## 复现方法

```bash
cd "src"

# 1. 页面数 -> 24
ls "Ink Canvas/Windows/SettingsViews/Pages/"*.xaml | wc -l

# 2. 整张表 + 唯一项总数 + 零绑定页 + 分母 -> 186 / 183 / 7 页 / 313
node docs/count-settings-bindings.js \
     "Ink Canvas/Resources/Settings.cs" \
     "Ink Canvas/Windows/SettingsViews/Pages"
```

> 表格由 `docs/count-settings-bindings.js` 生成（脚本已入库，不是临时文件），不是手抄。
> 脚本读 `Settings.cs` 建「类 → 属性」表，再扫设置页的 `.xaml` / `.xaml.cs` 引用。
>
> **要复核就重跑脚本，别用眼睛数 XAML。**
>
> ⚠️ 别用这条代替：`grep -c 'public .* { get; set; }' Resources/Settings.cs` → **325**。
> 它把 `class Settings` 自身的顶层项也算进去了，与本表「分母 313（仅子类）」口径不同。
