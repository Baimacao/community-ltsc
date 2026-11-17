# 构建与打包

## 环境

| 项 | 要求 |
|---|---|
| SDK | .NET 11（本机 `C:\Program Files\dotnet\sdk\11.0.100`） |
| 平台 | x64 |
| 系统 | Windows 10 19041 及以上 |

## 构建

```bash
cd "Ink Canvas"
dotnet build InkCanvasForClass.csproj -c Release -p:Platform=x64
```

产物目录：

```
bin/Release/x64/net11.0-windows10.0.19041.0/
```

### 构建前必须做的事

**先结束正在运行的程序。**

```bash
taskkill //F //IM ICCCELite.exe
```

否则会报 `MSB3027` / `MSB3021`（文件被占用）。这两个错误跟代码无关，是产物写不进去。

## 打包

### 只打这 10 个文件

```
IACore.dll
IALoader.dll
IAWinFX.dll
ICCCELite.deps.json
ICCCELite.dll
ICCCELite.dll.config
ICCCELite.exe
ICCCELite.runtimeconfig.json
InkCanvas.IACoreHelper.exe
InkCanvas.IACoreHelper.exe.config
```

输出目录里有四十多个文件，**其余全是 Costura 会内嵌进主程序、或者运行时才需要的**，不要进包。

### 绝对不能进包的东西

判据：**程序第一次启动会自己生成的东西，一律不进包。**

| 目录 / 文件 | 内容 |
|---|---|
| `Configs/` | 用户设置（含个人偏好、路径） |
| `Logs/` | 运行日志 |
| `Saves/` | 墨迹存档、截图 |
| `Backups/` | 设置自动备份 |
| `Crashes/` | 崩溃记录 |
| `Automations/` | 自动化配置 |
| `Plugins/` 及 `Plugin*` | 插件与缓存 |
| `*.pdb` | 调试符号 |

### 打出来的包

```
ICC-CE-Lite-<版本>.7z
```

### 打包后必须验证

不要只检查文件数够不够 —— **把包解压到一个全新的空目录，从那里启动**。

```bash
mkdir /tmp/pkgtest && cd /tmp/pkgtest
7z x ICC-CE-Lite-0.1a.7z
./ICCCELite.exe
```

确认：

1. 进程起来且只有 1 个实例
2. 日志里 `[Error]` 数为 0
3. `Crashes/` 是空的
4. 只生成了预期的那几个目录

只测构建输出目录不算数 —— 压缩/解压环节可能丢东西。

## 冒烟自测

```bash
./ICCCELite.exe --smoke-test
```

结果写进 `Logs/Log_*.txt`，搜 `[探针]`。

判据是 **FAIL=0**，不是「大部分过了」。

如果某条测试报错，先区分两件事：

- **产品缺陷** —— 改代码
- **测试本身测不出东西** —— 改测试

C-5 那条曾经长期 FAIL，读数两边都是 0，实际是测试无效（`Process.GetCurrentProcess()` 返回伪句柄，测不出泄漏）。改成真实文件句柄后才有区分度，并且加了一条「对照组确实能测出泄漏」保护性判据。

**测不出东西的测试比没有测试更糟**，因为它会给人虚假的安心。

## 版本号

由 Nerdbank.GitVersioning（NBGV）根据 `version.json` 和 git 提交号生成，**不要手改 csproj 里的 FileVersion**，会被覆盖。

人看的版本形如 `0.1a+4d4f6e3`：

- `0.1a` 是 `AssemblyInformationalVersion` 前缀，定义在 csproj
- `4d4f6e3` 是构建时的提交短 SHA

查当前版本：

```powershell
(Get-Item ICCCELite.dll).VersionInfo.ProductVersion
```

## 常见问题

### 编译报 `CS0103: 当前上下文中不存在名称 xxx`

通常是批量删代码时多删了。去 `git show HEAD:<文件>` 对比原始内容。

### 改完 git diff 出现几千行变更

换行符被重写了。这个仓库是 CRLF 为主的混合状态，用脚本改文件时必须按字节操作：

```python
with io.open(path, 'rb') as f:
    b = f.read()
lines = b.split(b'\r\n')   # 不是 split('\n')
...
with io.open(path, 'wb') as f:
    f.write(b'\r\n'.join(lines))
```

判据：`git diff --numstat` 的增删行数和实际改动量对不上，就是整文件被重写了。
