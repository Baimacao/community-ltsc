---
name: (Markdown Template) Bug 报告 | Bug Report
about: 反馈软件缺陷或异常 | Report a bug to help us improve
title: "[Version x.x.x] <your title>"
type: Bug
---

提之前请先做两件事：

1. 确认用的是最新构建（版本号在 设置 → 关于 里，形如 `0.1b-daily`）
2. **把日志带上**：程序目录下 `Logs/` 里最新的那个文件。
   比截图有用得多——这个分支专门把几百处「出错了没人知道」改成了写日志，
   不看日志等于白改

> 这份是 Markdown 模板，可以随便改结构。不想手写的话下面有个表单版，点进去更省事。

---

### 软件版本 | App Version（必填）

<!--- 设置 → 关于 里那一行，形如 0.1b-daily -->

### 操作系统及版本 | OS & Version（必填）

<!--- 例如 Windows 10 22H2 64 位 -->

### 白板设备型号 | Whiteboard Model（触控问题必填）

<!--- 例如 希沃 F86EC。不同设备的触摸报点行为不一样，触控类问题没有这一项基本没法查 -->

### 问题描述 | Description（必填）

<!--- 说清「什么情况下、出现了什么」。只写「卡」或者「坏了」定位不了 -->

### 复现步骤 | Steps to Reproduce（必填）

<!--- 能否稳定复现？试了几次里几次出问题？ -->

1.
2.
3.

复现率：__ / __ 次

### 期望结果 | Expected Behavior（必填）

<!--- 你认为应该是什么样 -->

### 其他补充信息 | Additional Info（可选）

<!--- 崩溃日志（Crashes/ 目录）、配置文件、特殊环境等 -->
