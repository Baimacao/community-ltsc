# REMOVED.md — LTSC 1.7.19.7 删除项与可逆性说明

> 基线：tag `1.7.19.7` / commit `083f694e`
> 分支：`ltsc/1.7.19.7`
> **本文件每一条都能靠 `git revert` 完整恢复**，因为所有删除都发生在本分支，
> 上游原样保留在 tag 里。

## 恢复总则

```bash
# 单条恢复
git log --oneline --grep="D-01"     # 按决策 ID 找到 commit
git revert <commit>                  # 恢复该决策

# 整体回到 1.7.19.7
git checkout 1.7.19.7
```

**没有任何一条删除涉及 Public 契约**：`InkCanvas.PluginSdk/` 下的 28 个公开类型
在本分支中一个都没删。`INotificationService` 接口保留，其实现被替换为不联网的极简版本。

---

## 删除清单（含 commit 哈希）

| 决策 | 删除内容 | 规模 | 用户可感知 | commit |
|---|---|---|---|---|
| C-02 | 4 处 `currentMode == 2` 死分支（**修**，非删） | 4 处 | 无 | `6717af72` |
| D-04 | 进程保护：长期文件锁 + 写入路径强制 GC | 635 行 → 直通 | 设置页开关消失 | `2ca96305` |
| D-08 | 补 PPT 事件退订（**修**，非删） | +40 行 | 无 | `7acae231` |
| C-01+C-04 | `Notification.AnimationMode`、`UseMinimalistGrabHandle`、`ShowGrabHandleChevron` | 3 个死配置 | 无 / 设置页少一项 | `9b5c98b0` |
| D-07 | 旧版浮动栏 UI 开关 + 10 个 Legacy 图标常量 | 270 → 202 行 | 设置页开关消失 | `3b155f81` |
| D-03 | 崩溃处置时序与映射（**修**，非删） | 2 个文件 | 崩溃时会弹窗了 | `bf657c7c` |
| D-06 | 智能模式（视频区域鼠标穿透） | 289 → 87 行 + 144 行 region | 设置项消失 | `65be19a9` |
| D-05 | UI 动效重写（**改**，非删） | 4 个文件 | 更顺滑，可全局关闭 | `b7cf56af` |
| N-01 | 公告中心 / 通知提供商 / 动态通知气泡 | 14 个文件删除 | 通知设置大幅精简 | `5dc1b157` |
| D-02 | PPT Agent（VSTO/命名管道）架构 | 3 个文件 + 701 行 | 架构下拉少一项 | `ee4561c2` |
| D-01 | 自动更新全链路 | 2792 + 1285 行 | 「更新」页消失 | `7069f706` |

合计：**53 files changed, 738 insertions(+), 9228 deletions(-)**，净减 8490 行。

## 三种恢复路径的区别

1. **配置开关恢复**（不改代码）：本次没有「只改默认值」的项，都是整块删除。
2. **`git revert`**：适用于所有整块删除。每条 commit message 都写了 `决策: XXX`。
3. **插件化**：`InkCanvas.PluginSdk` 提供 `IPlugin` + 9 个服务接口
   （`IAppRestartService` / `IWindowService` / `IPowerPointService` / `IEventService` /
   `ISettingsService` / `IHotkeyService` / `INotificationService` /
   `IFileAssociationService` / `IWindowOverviewService`）。
   被删功能若要回归而不进主程序，可走插件路线。
   **注意：这条路只是可行，未被验证**——本分支没做过实际插件化移植。

## 明确「不删」的项

| 项 | 原因 |
|---|---|
| `InkCanvas.PluginSdk/` 全部 | 删了会破坏 `.icpx` 生态的二进制兼容性，任务书明令禁止 |
| `InkCanvas.PPTAgent.Contracts` 项目 | `PowerPointAddIn` 仍引用它，整包删除会让独立 sln 编译失败 |
| `InkCanvas.PowerPointAddIn` 独立项目 | 只是不再被主程序分发，代码保留 |
| `CrashWindow` / 心跳 / 熔断 | D-03 是**修**它们，不是删 |
| 墨迹全家桶（平滑/压感/识别/保存/撤销） | 一行没动 |
| `Settings.Startup` 的更新相关字段 | 删了会让老配置反序列化丢字段，且现已无读取方 |
| `Properties/UpdateStrings.*` 等 resx | 删了会连带 `Strings.cs` 等文件编译失败 |

## D-01 的特别说明：去掉更新后，用户怎么知道版本

1. **设置 → 关于**：`AboutPage.xaml.cs:103-112` 直接从程序集读版本号并显示
   （`Assembly.GetExecutingAssembly().GetName().Version`），**不依赖更新模块**，
   删除自动更新后仍然工作。
2. **程序文件版本**：`InkCanvasForClass.exe` 的属性页。

因此不存在「静默失效、用户不知道自己在什么版本」的情况。
