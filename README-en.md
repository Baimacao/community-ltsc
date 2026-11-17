# ICC CE Lite

> An independent branch forked from InkCanvasForClass Community Edition.
> One goal only: **run stably on classroom touchscreens.**

| | |
|---|---|
| Version | `0.1a` (suffixed with the commit id, e.g. `0.1a+4d4f6e3`) |
| Executable | `ICCCELite.exe` (can coexist with the original) |
| Upstream baseline | `083f694e` (InkCanvasForClass/community, 2026-07-15) |
| Target framework | .NET 11 (`net11.0-windows10.0.19041.0`) |

---

## What this branch changes

### Stability

| Problem | Impact |
|---|---|
| `InkCanvas.IACoreHelper.exe` left running after exit | Repeated launches accumulate background processes |
| The cross-thread `DynamicRenderer` guard only matched English | Never triggered on non-English systems, could crash |
| Ink-to-shape leaked one kernel handle per stroke | Handle count climbed during long sessions |
| A 500 ms timer ran a cross-process UIA full-window enumeration with no re-entrancy guard | Gets slower the longer a lesson runs |
| PPT ink COM calls and whole-directory file reads inside a lock | UI hitching while inserting ink |
| Config cleanup deleted user-set values as "obsolete properties" | Settings silently reverted on restart, with nothing in the log |

### Touch input

| Problem | Impact |
|---|---|
| Settings pages missing `PanningMode` | Unscrollable on tablets, i.e. settings could not be changed |
| Interactions depended on mouse hover | Touchscreens have no hover, so features never fired |

### Presenter remote

| Problem | Impact |
|---|---|
| Keys handled twice when the adapter was enabled | Blackout twice, exit several steps at once |
| Bare ESC registered as a global hotkey | Starved the whole system of ESC — other apps never saw it |
| Key bindings lived only in memory | Rebinding was lost on restart |

### Settings and localization

- Four cultures: `zh-CN`, `zh-TW`, `zh-ME` (meme), `en-US` — 1311 keys, all four identical
- Settings pages reduced from 25 to 24, navigation regrouped
- Removed a set of switches that no longer did anything

### Removed on purpose

| Removed | Why |
|---|---|
| Auto-update | Whole feature gone; only old-launch-argument detection and residue cleanup remain |
| Telemetry and its privacy agreement | No upload code existed; the agreement described capabilities that were no longer present |
| Plugin marketplace | Depended on a decommissioned service |
| Random student picker | Unclear boundaries, feature removed entirely |

---

## Relationship to upstream

This branch forked from upstream `083f694e` (2026-07-15).

- **Fixes and improvements only** — no redesign of upstream features
- Fixes are organized so they can be offered back, see [docs/LTSC-CHANGES.md](docs/LTSC-CHANGES.md)

Licensed under GPL-3.0, see [LICENSE](LICENSE).
The copyright line records the full lineage and overrides no upstream contributor.

---

## Building

Requires the .NET 11 SDK.

```bash
cd "Ink Canvas"
dotnet build InkCanvasForClass.csproj -c Release -p:Platform=x64
```

Output lands in `bin/Release/x64/net11.0-windows10.0.19041.0/`.

See [docs/BUILD.md](docs/BUILD.md) for what belongs in a release package.

### Smoke test

```bash
./ICCCELite.exe --smoke-test
```

Results go to `Logs/Log_*.txt`; search for `[探针]`. All 17 checks currently pass.

---

## Credits

Built on the shoulders of upstream ICC CE and IC.

Lineage: IC (WXRIW) → ICA (ChangSakura) → ICC (Dubi906w) → ICC CE (CJK_mkp) → ICC CE Lite (Baimacao, 鲸仔_JZ).
