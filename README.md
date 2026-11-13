# ICC-CE LTSC

> 基于 [InkCanvasForClass Community Edition](https://github.com/InkCanvasForClass/community) 的长期支持分支。
> 为课堂场景做减法：砍掉干扰授课的功能，把力气花在稳定和顺手上。

**版本**：1.0a-ltsc  
**上游基线**：`083f694e`（CJK_mkp，2026-07-15，上游 version 1.0.0）

---

## 这个版本和上游有什么不同

### 我们删掉的

| 功能 | 为什么删 |
|---|---|
| 点名 / 随机抽选 | 干扰课堂。原实现与抽选耦合，砍掉后计时器独立成页 |
| 数据上传 / 使用统计 | 本分支**不涉及任何数据获取**，没有需要用户同意的采集行为 |
| 窗口渲染模式切换（WindowChrome） | 稳定性问题，保留会导致异常 |
| 设置里的控件库 Demo 残留 | 不是老师需要的东西 |

### 我们改掉的

| 改动 | 说明 |
|---|---|
| **OOBE 从 7 步压到 3 步** | 大约 30 秒走完，支持随时跳过 |
| **全应用弹簧动画** | 18 处动画换成交互式弹簧引擎，手感统一 |
| **触摸屏滚动** | 设置页支持自然滚动（外层 NavigationView 会吞触摸，已修） |
| **PPT 模式透明遮罩** | 退出时不再残留一条透明带 |
| **退出后不留后台进程** | IACoreHelper 加父进程看门狗，主程序退出它跟着退 |
| **多开不再抢同一份配置** | 任务栏图标和 AppUserModelID 与上游分开，可共存 |
| **设置页全面中文化** | 109 条硬编码文字改成 resx，支持中英切换 |
| **图标独立** | LTSC 有自己的图标，深浅两套跟随主题 |

### 我们没动的

上游的批注、画板、PPT 联动核心功能保持原样。本分支的改动集中在**交互稳定性**和**课堂体验**，不重新发明画布。

---

## 遇到问题去哪反馈

| 渠道 | 地址 |
|---|---|
| **GitHub Issues** | https://github.com/Baimacao/community-ltsc/issues |
| **智教联盟论坛** | https://forum.smart-teach.cn |

反馈时请附上日志文件：程序目录下的 `Logs/Log_日期-时间.txt`。

日志文件名就是启动时间，挑最新那份即可。

---

## 与上游的关系

- 本分支从上游 `083f694e`（2026-07-15）分出，是针对性重构，不是长期漂移
- 上游后续如有修复，会评估后回合
- 本分支的通用改进（弹簧动画引擎、触摸滚动修复等）计划整理后**反哺上游**

如果你在找原版，请去 [上游仓库](https://github.com/InkCanvasForClass/community)。

---

## 开发

改动清单见 [LTSC-CHANGES.md](docs/LTSC-CHANGES.md)，基线记录见 [LTSC-BASELINE.md](docs/LTSC-BASELINE.md)。

**构建环境**：.NET 11 SDK，`net11.0-windows10.0.19041.0`

```
dotnet build "Ink Canvas/InkCanvasForClass.csproj" -c Release -p:Platform=x64
```

---

## 致谢

- [InkCanvasForClass](https://github.com/InkCanvasForClass/community) —— 上游
- 智教联盟 —— 反馈渠道与社区支持

---

## 许可

遵循上游许可。本分支的改动同样开源。
