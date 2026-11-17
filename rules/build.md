# 编译规范

## dotnet 路径

dotnet 不在系统 PATH 中，完整路径：

```
C:\Program Files\dotnet\dotnet.exe
```

## 编译前必须先做

**结束正在运行的程序**，否则产物写不进去，报 `MSB3027` / `MSB3021`。

```bash
taskkill //F //IM ICCCELite.exe
```

## 编译命令

在仓库根目录下：

```bash
cd "Ink Canvas"
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvasForClass.csproj" -c Release -p:Platform=x64 --nologo -v q
```

### 编译单个子项目

```bash
"/c/Program Files/dotnet/dotnet.exe" build "InkCanvas.Controls/InkCanvas.Controls.csproj"
```

## 产物位置

```
Ink Canvas/bin/Release/x64/net11.0-windows10.0.19041.0/
```

## 判据

- **0 error** 才算过。warning 要看是什么，新增的 warning 要处理。
- 编译过了不等于改对了 —— 还要跑冒烟自测和实际验证，见 `docs/BUILD.md`。

## 验证脚本

```bash
node tools/verify.js
```

代码级验证（剥注释、括号配对提取方法体），用于核对某个修复是否真的落在代码里。
