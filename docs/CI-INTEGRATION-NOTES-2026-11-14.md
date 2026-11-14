# ClassIsland 联动调研笔记（2026-11-14）

> 对应需求：R-05
> **本文只记录查证到的事实。** 猜的地方标「未验证」。

---

## 一、查证来源

| 项 | 值 |
|---|---|
| 官网 | https://www.classisland.tech/ |
| 文档站 | https://docs.classisland.tech/ |
| 仓库 | https://github.com/ClassIsland/ClassIsland （2.9k star，GPL-3.0） |
| 插件示例 | https://github.com/ClassIsland/ExamplePlugins |
| 技术栈 | Avalonia + .NET，跨平台（Windows / Linux / macOS） |

---

## 二、插件接口（**已从官方示例源码确认**）

### 2.1 入口

```csharp
using ClassIsland.Core.Abstractions;
using ClassIsland.Core.Attributes;

[PluginEntrance]
public class Plugin : PluginBase
{
    public override void Initialize(HostBuilderContext context, IServiceCollection services) { }
    public override void OnShutdown() { }
}
```

- 入口类是 `PluginBase` 的子类，标 `[PluginEntrance]`
- 两个可重写方法：`Initialize`（启动）、`OnShutdown`（退出）
- 用标准的 `IServiceCollection` 做 DI 注册

### 2.2 项目配置

```xml
<TargetFramework>net8.0-windows</TargetFramework>
<UseWPF>true</UseWPF>
<EnableDynamicLoading>True</EnableDynamicLoading>
<PlatformTarget>x64</PlatformTarget>

<PackageReference Include="ClassIsland.PluginSdk" Version="1.0.0">
  <ExcludeAssets>runtime</ExcludeAssets>
</PackageReference>
```

- **SDK 包名**：`ClassIsland.PluginSdk`，示例里是 **1.0.0**
- `EnableDynamicLoading=True` 必须开
- 要带 `manifest.yml` 到输出目录

### 2.3 设置页注册

```csharp
services.AddSettingsPage<ExampleSettingsPage>();
```

命名空间：`ClassIsland.Core.Extensions.Registry`

### 2.4 配置读写

```csharp
Settings = ConfigureFileHelper.LoadConfig<Settings>(Path.Combine(PluginConfigFolder, "Settings.json"));
Settings.PropertyChanged += (s, e) => ConfigureFileHelper.SaveConfig<Settings>(...);
```

命名空间：`ClassIsland.Shared.Helpers`
`PluginConfigFolder` 是 `PluginBase` 给的属性。

---

## 三、示例仓库里有什么

| 示例 | 用途 |
|---|---|
| `HelloWorldPlugin` | 最小插件（就是上面那个） |
| `PluginWithNotificationProviders` | 通知提供者 |
| `PluginWithSettingsPage` | 带设置页 + 配置持久化 |

**注意**：三个示例里**没有自动化触发器/规则的示例**。
这意味着「在 CI 自动化里加 ICC 状态触发器」这件事，
示例代码没覆盖，**需要查文档或读 ClassIsland 主仓库源码**。

---

## 四、对 R-05 的影响

### 4.1 可以做的是什么

从源码能确认的部分：
- 能写插件
- 能加设置页
- 能持久化配置
- 能拿到 `IServiceCollection`，理论上能注册任何 CI 内部服务

### 4.2 仍然不知道的

| # | 问题 | 为什么重要 |
|---|---|---|
| 1 | **自动化触发器怎么注册？** | 需求要求「在 CI 的自动化中加入触发器」 |
| 2 | `ClassIsland.PluginSdk` 当前最新版本 | 示例写的 1.0.0 可能已过时 |
| 3 | CI 的课程/时间点事件怎么订阅 | 「上课时清空笔迹」要挂在这个事件上 |
| 4 | **课表交换格式**（需求里的「智教联盟专门格式」） | 完全未知，用户没给名称 |

### 4.3 一个重要的架构判断

需求写「做一个 CI 插件与 ICC CE Lite 通信」。
从插件接口看，CI 插件是跑在 **CI 进程内**的 .NET 程序集，不是独立进程。

那么「通信」有两种可能：

| 方案 | 含义 | 复杂度 |
|---|---|---|
| **A. 插件直连 ICC** | 插件里直接起管道/HTTP 连 ICC | 要写两端的协议 |
| **B. 插件调 ICC 的命令行/IPC** | 复用 ICC 已有的 `IACore IPC` 或命令行 | 要看 ICC 现在暴露了什么 |

ICC 侧现在有 `Helpers/FileAssociationManager.StartIpcListener`（文件关联用的 IPC 监听器），
**可能可以复用**，但需要先读它的实现确认（未验证）。

---

## 五、我建议的下一步（不需要你确认就能做）

1. 查 `ClassIsland.PluginSdk` 在 NuGet 上的**最新版本**
2. 读 ClassIsland 主仓库里 `Automation` 相关的源码，搞清触发器怎么注册
3. 读 ICC 现有的 IPC 监听器实现，看能不能复用

**唯一需要你给的**：课表交换格式的名称或链接。

---

## 六、⚠️ 一处需要你知道的风险

ClassIsland 是 **GPL-3.0** 许可。

如果我们写一个 CI 插件去调 ICC，这个插件本身要不要开源、会不会传染 ICC 的许可，
这是法律问题，**我不能替你判断**。

需要你决定：CI 插件是否独立开源发布。

---

*本文档随查证更新。未验证的结论不要当已确认用。*
