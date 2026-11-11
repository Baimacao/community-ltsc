# Inkeys3 PPT 联动架构笔记

> 来源：`E:\DSH\tmp\inkeys-src`（Alen-CRL/Inkeys），commit `7362ec4`
> 读于 2026-11-11。**只记录设计思路，不复制代码。**
> 目的：ICC 的 PPT 联动反复出问题（页码 -1、多实例选错、退出状态残留），
> 这里记清楚 Inkeys 是怎么解决的。

---

## 一、根本差异：进程模型

| | ICC | Inkeys |
|---|---|---|
| PPT 数据来源 | 自己作为 COM 加载项/客户端连 PowerPoint | 独立进程 `PptCOM`，独立的 COM 服务器 |
| 数据传递 | COM 事件回调（`SlideShowNextSlide` 等） | **共享内存指针** |
| 生命周期 | 跟 ICC 主进程绑死 | 独立，PPT 崩了不影响宿主 |
| 代码量 | 散布在 `MW_PPT.cs` + `PPTManager` | 集中一个 1740 行的 `PptCOM.cs` |

**最关键的一条**：Inkeys 不用事件，用**轮询 + 指针**。

```csharp
public unsafe bool Initialization(int* TotalPage, int* CurrentPage, int* OffSignal)
```

宿主（智绘教主程序）分配三个 `int` 的内存，把指针交给 COM 服务器。
PptCOM 在独立线程的死循环里不停往里面写值，主程序随时读。
**没有事件订阅，没有 `-= ` 忘记退订，没有「退出放映后回调还挂着」。**

---

## 二、多实例选哪个：三级优先级

这是 ICC 反复出问题的地方。Inkeys 的解法（`PptCOM.cs:760-820`）：

```
0  这个 Application 连 ActivePresentation 都拿不到     → 忽略
1  有 ActivePresentation                              → 候选
2  上面还拿得到 SlideShowWindow（在放映）              → 优先
3  ssWindow.Active 为真 或 窗口真的在前台              → 最高
```

**两条细节值得抄**：

1. **`Active` 的类型兼容**（`:799-805`）：
   ```csharp
   // MS PPT 返回 int (-1 = msoTrue), WPS 可能返回 bool (true)
   object val = ssWindow.Active;
   if (val is int && (int)val == -1) isActive = true;
   else if (val is bool && (bool)val == true) isActive = true;
   ```
   同一个属性，Office 和 WPS 返回不同类型。不判类型直接强转就炸。

2. **`Active` 不可靠时用 PID 兜底**（`:816`）：
   WPS 播非全屏时 `Active` 可能一直是 false，这时再用
   `IsSlideShowWindowActive(ssWindow)` 判窗口是否真在前台。
   日志里那行 `[Fix] App process has focus via PID check` 说明这个坑踩过。

---

## 三、换绑策略：不轻易重绑

`PptCOM.cs:936-948`：

```
if 当前没绑定 && 找到可用的          → 绑定
else if 找到优先级更高的
    if 不是同一个 Application
        if !ShouldKeepCurrentBindingForSamePresentation(bestApp)   ← 关键
            → 重绑
```

**`ShouldKeepCurrentBindingForSamePresentation` 的用意**（注释原文）：
> 同一演示文稿可能有多个 ROT/COM wrapper，当前绑定仍可读时不重绑。

翻译：PowerPoint 在 ROT（Running Object Table）里可能给同一个演示文稿挂多个
COM 对象。**只要当前绑的那个还能读出数据，就别换。** 换了就是自找抖动。

ICC 之前页码 -1 的原因之一就是这个：认到「另一个 handle」就重绑，
重绑过程中一读就是 -1。

---

## 四、关于「重置设置 / 退出状态残留」

Inkeys 把 `*pptCurrentPage = -1` 当了**哨兵值**：
- 读不到就写 -1，宿主看到 -1 就知道「现在不可用」
- 明确的收尾点（`:210/`:273/`:613-614/`:914-915/`:1263-1264/`:1272-1273`）
  都成对清 -1

**好处**：状态永远只有「有效值」或「-1」两种，不会残留上一次放映的页码。
ICC 之前 `_currentSlideShowPosition` 不清，退出后再读还是旧值 → 导致联动状态错乱。

---

## 五、能抄什么、抄不了什么

### 能抄
1. **哨兵值策略**：所有 PPT 状态字段，失效即置 -1/当前值，成对清理。
   ICC 只需改 `MW_PPT.cs` 里那几个字段的读写约定，不用动架构。
2. **`Active` 的类型兼容判断**：ICC 也接 WPS，这个坑一模一样。
3. **优先级分级**：ICC 现在只判「是不是在放映」，可以加一级「谁的窗口在前台」。
4. **不轻易重绑**：只要当前实例还能读出正确页码，就不换。

### 抄不了
1. **独立进程 + 共享内存**：ICC 是加载项架构，改这个等于重写整个 PPT 模块。
   收益没有大到值得这样做。
2. **`unsafe` 指针**：ICC 全项目没开 `AllowUnsafeBlocks`，为这个开不值得。
   用普通字段 + 锁等价实现即可。

---

## 六、对 ICC 的具体落点

| ICC 现状 | 对应问题 | 参考 Inkeys |
|---|---|---|
| `PPTManager.GetCurrentSlideNumber()` 返回 -1 | 页码读不到 | 加哨兵 + 优先级，别让 -1 混进合法值 |
| `0e97cb09` 我加的「实例优先级评分」 | 多实例选错 | 我加的是 2 级，Inkeys 是 3 级 |
| `_currentSlideShowPosition` 退出不清 | 状态残留 | 成对清 -1 |
| 只判 `IsInSlideShow` | WPS 兼容 | 加 `Active` 类型判断 + PID 兜底 |

---

## 七、没做完的

- [ ] 只读了 `PptCOM.cs` 约 400 行关键路径，`NextSlideShow`/`PreviousSlideShow`/
      `GetPptHwnd` 还没细看
- [ ] 智绘教主程序那一侧怎么调用 PptCOM（ROT 注册、`Initialization` 调用时机）没看
- [ ] `HandleBusyException` / `BusyRetryTimeout`（PPT 忙时重试）的完整策略没看

**这三块要等真修 ICC 的 PPT 问题时再读，不是现在必须。**
