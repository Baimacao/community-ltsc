# CSES 课程表交换规范 —— 调研笔记（2026-11-14）

> 对应需求：R-05（ClassIsland 联动）
> 用户答复 Q5-1：「CSES 规范」
> **本文全部来自规范原文，未推测。**

---

## 一、CSES 是什么

| 项 | 值 |
|---|---|
| 全名 | **The Course Schedule Exchange Schema** |
| 用途 | 通用的**课程表交换格式**，用于不同软件之间交换课程表 |
| 维护方 | SmartTeachCN（智教联盟） |
| 仓库 | https://github.com/SmartTeachCN/CSES |
| 协议 | MIT |
| 文件格式 | **YAML**（`xxx.yml` / `xxx.yaml`） |
| 编码 | **UTF-8** |
| 当前版本 | **v1**（正式）；v2 仍在修订，未生效 |

规范明确写了「可以轻松转换为其他格式，如 **ClassIsland 格式**」，
正是我们要用的。

---

## 二、v1 数据结构（完整）

```yaml
version: 1                    # 必填，只能为 1

subjects:                     # 科目表
  - name: 数学                 # 必填，任意 string
    simplified_name: 数         # 可选，适合中文科目名（CI 等紧凑课表需要）
    teacher: 李梅               # 可选
    room: "101"               # 可选

schedules:                    # 每天一张课表
  - name: 星期一                # 任意 string
    enable_day: 1             # 必填，1-7 = 周一到周日
    weeks: all                # 必填，all / odd / even
    classes:                  # 当天的课程数组
      - subject: 数学          # 必填，引用 subjects 里的 name
        start_time: "08:00:00" # 必填，HH:MM:SS
        end_time: "09:00:00"   # 必填，HH:MM:SS
```

### 字段约束（来自 cses.schema.json）

| 字段 | 类型 | 约束 |
|---|---|---|
| `version` | int | 只能为 1 |
| `subject.name` | string | 必填 |
| `subject.simplified_name` | string | 可选，中文科目缩写 |
| `subject.teacher` | string | 可选 |
| `subject.room` | string | 可选 |
| `schedule.enable_day` | int | 1–7，周一到周日 |
| `schedule.weeks` | string | `all` / `odd` / `even` |
| `class.subject` | string | 必须存在于 subjects |
| `class.start_time` | string | 正则 `([01]\d\|2[0-3]):([0-5]\d):([0-5]\d)` |
| `class.end_time` | string | 同上 |

### 有 Schema 可以做校验

`https://raw.githubusercontent.com/SmartTeachCN/CSES/main/cses.schema.json`
JSON Schema draft-07，可以直接用来验证老师给的课表文件。

---

## 三、对 R-05 的意义

### 3.1 已经能确定的

1. 读课表 = 解析 YAML，字段全在上面
2. 可以**先校验**再解析（用 Schema），避免坏文件导致崩溃
3. CI 那边如果要转成自己的格式，CSES README 说得很明确是支持的

### 3.2 「连堂」怎么判定 —— 现在有数据依据了

CSES 里**没有「连堂」这个字段**。

但可以从 `classes` 数组里判断：
同一天里，相邻两条 `class`，如果
- `subject` 相同
- 前一条 `end_time` == 后一条 `start_time`

那就是连堂。

**但这只是我根据数据结构推的判定方法，不是你给的规则。**
需求里 Q5-4「连堂判定规则」仍然需要你确认：
是否就用这个判据，还是另有规则（比如允许 1–2 分钟课间也算连堂）。

### 3.3 还要确认的

- 课表文件从哪来？老师手放一个 yml？还是 ICC/CI 去某个路径扫？
- 多周轮换（`weeks: odd/even`）怎么和真实日期对上？需要知道「第几周」的起点。

---

## 四、⚠️ 一处必须提醒的

规范里 `enable_day` 是 **1–7 表示周一到周日**。
这和 .NET 的 `DayOfWeek` 枚举（Sunday=0）**不一致**，
转换时要减 1 或映射，否则会整体错一天。

---

*本文件只记录规范事实。连堂判定的「建议」标了是我的推断，未确认前不写进实现。*
