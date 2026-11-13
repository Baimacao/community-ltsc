# ICC-CE LTSC

> ICC-CE 的长期支持分支。
> 目标只有一个：**在教室里稳定跑**。

| 项 | 值 |
|---|---|
| 版本 | 1.0a-ltsc（FileVersion 1.0.1.0） |
| 上游基线 | `083f694e`（上游 1.0.0） |
| 上游仓库 | https://github.com/InkCanvasForClass/community |
| 本分支仓库 | https://github.com/Baimacao/community-ltsc |

---

## 这个版本修了什么

### 崩溃与泄漏

| 问题 | 影响 |
|---|---|
| 退出后残留 `InkCanvas.IACoreHelper.exe` | 反复开关会积累多个后台进程 |
| 跨线程访问 `DynamicRenderer` 的判据只认英文 | 非英文系统上拦不住，可能崩 |
| 墨迹转形状每笔漏一个内核句柄 | 长时间批注后句柄数持续上涨 |

### 功能异常

| 问题 | 影响 |
|---|---|
| 和原版装一起时任务栏合并、配置互相覆盖 | 两个版本不能共存 |
| 触摸屏滑不动设置页 | 平板上等于没法改设置 |
| 退出 PPT 放映后留一条半透明遮罩 | 挡着下面的窗口，点不掉 |
| PPT 翻页栏有时不出现 | 放映时看不到页数 |
| PPT 连接失败没有任何提示 | 出问题查不到原因 |

### 可用性

- 设置页按使用场景重新归组，调试项收进折叠组
- 硬编码的英文字符串收进资源文件，支持中英切换
- 界面动画换成弹簧引擎，连续操作不再跳帧
- 删掉日志里每 5 秒一条的开发期探针（原占日志一半体积）
- 首次启动引导的背景样式下拉只显示 3 个常用项
- 工具栏里点了没反应的「说明书」入口已删除

细节见 [docs/LTSC-CHANGES.md](docs/LTSC-CHANGES.md)。

---

## 遇到问题去哪反馈

**先看日志**。出问题时日志里通常有原因：

```
<程序目录>\Logs\Log_YYYY-MM-DD-HH-mm-ss.txt
```

文件名是启动时间。反馈时把最近一份日志附上，能省很多来回。

反馈渠道：

- 智教联盟：https://forum.smart-teach.cn
- GitHub Issues：https://github.com/Baimacao/community-ltsc/issues

---

## 与上游的关系

本分支从上游 `083f694e`（2026-07-15）分出，
只做修复与改进，不改上游的功能设计。

上游如果修了同样的问题，可以直接用；
本分支修的问题也会整理出来回给上游，见 [docs/LTSC-CHANGES.md](docs/LTSC-CHANGES.md) 第六节。

---

## 开发

| 项 | 值 |
|---|---|
| SDK | .NET 11（11.0.100） |
| TFM | `net11.0-windows10.0.19041.0` |
| LangVersion | 14 |
| 平台 | x64 |

```bash
dotnet build "Ink Canvas/InkCanvasForClass.csproj" -c Release -p:Platform=x64
```

**注意**：不要用 `--target:Compile`。它会跳过 XAML 代码生成，
报出假的 `CS5001` 和 `InitializeComponent` 找不到。

基线怎么核对、怎么回合上游的新提交，见 [docs/LTSC-BASELINE.md](docs/LTSC-BASELINE.md)。

---

## 致谢

本分支站在上游 ICC-CE 和 Ink Canvas 的肩膀上。
所有原作者的贡献见程序内「关于」页。

---

## 许可

沿用上游许可证，见仓库根目录 `LICENSE`。
