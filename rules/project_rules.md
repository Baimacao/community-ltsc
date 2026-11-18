# Project Rules Index

本目录包含了 Ink Canvas 项目的开发规范与规则，按功能模块分类。

## 项目结构

> 2026-11-18 核对了实际 csproj，改了三处：
> 删掉 `InkCanvas.PluginSdk`（插件系统在 7a30c93f 时整个删了，目录只剩构建残留），
> 目标框架从 net6 更新到实际值，补上漏掉的 Contracts 和 PowerPointAddIn。

| 项目 | 目标框架 | 说明 |
|------|---------|------|
| `Ink Canvas/` | net11.0-windows10.0.19041.0 | 主应用（WPF） |
| `InkCanvas.Controls/` | net8.0-windows10.0.19041.0 | 自定义控件库（LabeledSettingsCard 等） |
| `InkCanvas.IACoreHelper/` | .NET Framework 4.7.2（x86） | IACore 辅助进程，不引用输出 |
| `InkCanvas.PPTAgent.Contracts/` | netstandard2.0 | PPT 通信协议定义 |
| `InkCanvas.SettingsTreeView/` | net8.0-windows10.0.19041.0 | 设置目录树查看器（独立 WPF 应用） |
| `InkCanvas.PowerPointAddIn/` | .NET Framework 4.7.2 | PowerPoint 加载项，独立 sln，不参与主解决方案 |

## 规则目录

| 规则文件 | 说明 |
|---------|------|
| [xaml_controls.md](./xaml_controls.md) | XAML 控件使用规范 |
| [settings_pages.md](./settings_pages.md) | 设置页面开发规范 |
| [popups_menus.md](./popups_menus.md) | 弹出菜单/工具栏规范 |
| [toolbar.md](./toolbar.md) | 浮动工具栏开发规范 |
| [build.md](./build.md) | 编译规范 |
| [general.md](./general.md) | 通用开发规范 |
| [Ink Canvas 设置完整目录.md](./Ink%20Canvas%20设置完整目录.md) | 设置页面完整目录树 |

## 快速导航

- [设置页面开发规范](./settings_pages.md) - 添加/删除设置项、设置页面布局
- [弹出菜单规范](./popups_menus.md) - 笔菜单、橡皮擦菜单等 Popup 开发
- [工具栏规范](./toolbar.md) - 浮动工具栏按钮管理
- [编译规范](./build.md) - 编译前清理流程
- [设置完整目录](./Ink%20Canvas%20设置完整目录.md) - 所有设置页面的树状结构
