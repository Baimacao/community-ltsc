# fork 分支归档

> 2026-11-17：把这 10 条上游分支从 fork 里删掉，只留 `ltsc`。
> 这里记下删除时的 SHA。这些提交在上游 `origin` 里都还在，需要时重新拉即可。

## 为什么删

它们全是上游的分支（2025-12 到 2026-11 期间的最后提交），本分支一条都没用过。
留在 fork 里只会让仓库看起来还挂在上游的开发线上。代码本身没丢 —— 都还在上游。

## 恢复某一条

```bash
# 从上游取回来
git fetch origin <分支名>:refs/remotes/origin/<分支名>

# 推回 fork
git push "https://Baimacao@github.com/Baimacao/community-ltsc.git" \
  origin/<分支名>:refs/heads/<分支名>
```

## 删除时的 SHA

| 分支 | SHA |
|---|---|
| `1.7` | `c7427eb22c5717c74de71ff29b4cbb19da070c32` |
| `dusk` | `e75210714bfcbb5d97e0b5106743cc9ceff244b0` |
| `feat/booth` | `e3268a7cfa6b64d4ede7eaee5c7b2b3f3ec287cc` |
| `feature/palm-eraser-refactor` | `a9c3b8263384b734bda69a964c2b741a22846a76` |
| `net6` | `e087e48842d728ba2238d2d8b15d6550ba14af0f` |
| `net10` | `a8c34f206fbba1d81bc07955923f9876dd79b5e4` |
| `net10-shit` | `3f217c1de88050e635644d447d90adc1c2b1e6fd` |
| `net10-wpfui` | `96c54c6ac1b49a9dce144b1c41fddd97b5b87c8b` |
| `net472` | `578ffb7c56f1ceea1703bb7f111962e5b315d1bf` |
| `unstable-0607` | `f67b4db4baccfe0b0b1d1e92324a1cad576044cc` |

## 保留

| 分支 | 说明 |
|---|---|
| `ltsc` | 默认分支，本项目的全部工作 |

`ltsc` 在删除这些分支时指向 `79b1a44a`（提交「整理成独立的 ltsc 分支仓库」）。
