# ICC CE Lite

> 从 InkCanvasForClass Community Edition 分出的独立分支。
> 目标只有一个：**在教室的触摸屏上稳定跑。**

| 项 | 值 |
|---|---|
| 当前版本 | `0.1a`（跟随提交号，形如 `0.1a+4d4f6e3`） |
| 程序名 | `ICCCELite.exe`（与原版可共存） |
| 上游基线 | `083f694e`（InkCanvasForClass/community，2026-07-15） |
| 目标框架 | .NET 11（`net11.0-windows10.0.19041.0`） |

---

## 这个分支做了什么

### 稳定性

| 问题 | 影响 |
|---|---|
| 退出后残留 `InkCanvas.IACoreHelper.exe` | 反复开关会积累多个后台进程 |
| 跨线程访问 `DynamicRenderer` 的判据只认英文 | 非英文系统上拦不住，可能崩 |
| 墨迹转形状每笔漏一个内核句柄 | 长时间批注后句柄数持续上涨 |
| 500ms 定时器做跨进程 UIA 全窗口枚举，且无重入门闩 | 一节课越上越卡 |
| PPT 墨迹的 COM 调用与整目录读盘在锁内 | 插图时界面卡顿 |
| 配置清理把用户存的设置当「过期属性」删掉 | 改完设置重启就丢，且日志看不出原因 |

### 触摸屏

| 问题 | 影响 |
|---|---|
| 设置页缺 `PanningMode` | 平板上滚不动，等于改不了设置 |
| 交互依赖鼠标悬停（hover） | 触摸屏没有 hover，功能触发不了 |

### 翻页笔

| 问题 | 影响 |
|---|---|
| 适配开启时按键被响应两次 | 黑屏黑两遍、退出退好几步 |
| 裸 ESC 注册成全局热键 | 抢走整个系统的 ESC，别的程序收不到 |
| 按键配对只在内存里 | 改完键重启就丢 |

### 设置与多语言

- 四套语言：简体 `zh-CN`、繁体 `zh-TW`、梗体 `zh-ME`、英文 `en-US`，共 1311 条，四份完全一致
- 设置区从 25 页压到 24 页，导航重新分组
- 清掉一批不再生效的开关（遥测等级、更新前备份、PPT 调试探针等）

### 移除的东西

这些都是**主动删除**，不是漏掉：

| 移除 | 原因 |
|---|---|
| 自动更新 | 整套功能移除，只保留识别旧版启动参数与清理残留目录 |
| 遥测与隐私协议 | 没有任何上传代码，协议描述的是已不存在的能力 |
| 插件市场 | 依赖已下线的服务 |
| 点名（随机抽人） | 边界不清，功能整体移除 |

---

## 和上游的关系

本分支从上游 `083f694e`（2026-07-15）分出。

- **只做修复与改进**，不重新设计功能
- 上游修了同样问题的话可以直接对照
- 本分支的修复也整理成了可以回给上游的形式，见 [docs/LTSC-CHANGES.md](docs/LTSC-CHANGES.md)

许可证沿用上游 GPL-3.0，见 [LICENSE](LICENSE)。
版权栏如实记录谱系，不覆盖任何上游贡献者。

---

## 构建

需要 .NET 11 SDK。

```bash
cd "Ink Canvas"
dotnet build InkCanvasForClass.csproj -c Release -p:Platform=x64
```

产物在 `bin/Release/x64/net11.0-windows10.0.19041.0/`。

打包规则（哪些文件进包、哪些是运行时生成的不该进）见 [docs/BUILD.md](docs/BUILD.md)。

### 冒烟自测

```bash
./ICCCELite.exe --smoke-test
```

结果写进 `Logs/Log_*.txt`，搜 `[探针]`。当前 17 项全过。

---

## 文档

| 文档 | 内容 |
|---|---|
| [docs/LTSC-CHANGES.md](docs/LTSC-CHANGES.md) | 相对上游的全部改动，按类归档 |
| [docs/LTSC-BASELINE.md](docs/LTSC-BASELINE.md) | 上游基线怎么核对、怎么回合新提交 |
| [docs/MASTER-PLAN.md](docs/MASTER-PLAN.md) | 完整计划与执行记录 |
| [docs/BUILD.md](docs/BUILD.md) | 构建与打包 |

---

## 致谢

本项目站在上游 ICC-CE 与 Ink Canvas 的肩膀上。

往上追溯：InkCanvasForClass（WXRIW）→ ICA（ChangSakura）→ ICC（Dubi906w）→ ICC-CE（CJK_mkp）→ ICC CE Lite（Baimacao）。
