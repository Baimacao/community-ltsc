# ICC CE Lite

> An independent branch forked from InkCanvasForClass Community Edition.
> One goal only: **survive a full class period on a classroom touchscreen.**

When a whiteboard stutters mid-lesson, those forty minutes are gone. Every change in this
branch serves that one purpose — not adding features, but pinning down the places that break.

| | |
|---|---|
| Version | `0.1b-daily` (suffixed with the commit id, e.g. `0.1b-daily+4d4f6e3`) |
| Executable | `ICCCELite.exe` (installs side by side with the original) |
| Upstream baseline | `083f694e` (InkCanvasForClass/community, 2026-07-15) |
| Target framework | .NET 11 (`net11.0-windows10.0.19041.0`) |

---

## What this branch changes

### Stability

Getting slower as the lesson goes on, strokes drifting late in a long session, settings
reverting after a restart — these are not "the occasional hiccup", they make the class unusable.

| Problem | Impact |
|---|---|
| `InkCanvas.IACoreHelper.exe` left running after exit | Repeated launches pile up background processes |
| The cross-thread `DynamicRenderer` guard only matched English | Never triggered on non-English systems; could crash |
| Ink-to-shape leaked one kernel handle per stroke | Handle count climbed the longer you annotated |
| A 500 ms timer ran a cross-process UIA full-window enumeration with no re-entrancy guard | **The main reason a lesson gets slower the longer it runs** |
| PPT ink COM calls and whole-directory file reads inside a lock | The UI hitches on every ink insert |
| Config cleanup deleted user-set values as "obsolete properties" | Settings silently reverted on restart, with nothing in the log |

### Touch input

This one hurts: the app is built for touchscreens, yet some things only a mouse could reach.

| Problem | Impact |
|---|---|
| Settings pages missing `PanningMode` | Unscrollable on tablets — you could get in but not through, so settings were unchangeable |
| Interactions depended on mouse hover | Fingers have no hover; those features never fired on touch |

### Presenter remote

| Problem | Impact |
|---|---|
| Keys handled twice when the adapter was enabled | Blackout twice, exit several steps at once |
| Bare ESC registered as a global hotkey | Starved the whole system of ESC — other apps never saw it |
| Key bindings lived only in memory | Rebinding was lost on restart |

### Settings and localization

- Four cultures: `zh-CN`, `zh-TW`, `zh-ME` (meme), `en-US` —
  1323 keys in the base, 1317 in each translation (the difference is a developer list that only exists in Chinese)
- Settings pages reduced from 25 to 24, navigation regrouped
- Removed a set of switches that did nothing when clicked

### Removed on purpose

The following were **deliberately deleted**, not forgotten:

| Removed | Why |
|---|---|
| Auto-update | Whole feature gone; only legacy launch-argument detection and residue cleanup remain |
| Telemetry and its privacy agreement | No upload code ever existed; the agreement described capabilities that were no longer present |
| Plugin marketplace | Depended on a decommissioned service |
| Random student picker | Unclear boundaries, feature removed entirely |

---

## Relationship to upstream

This branch forked from upstream `083f694e` (2026-07-15).

- **Fixes and improvements only** — no redesign of upstream features
- If upstream fixed the same issue, the two can be compared directly
- Changes are organized so they can be offered back, see [docs/LTSC-CHANGES.md](docs/LTSC-CHANGES.md)

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

**A release package contains exactly 10 program files.** The output directory holds forty-odd
files; the rest are either embedded into the main executable by Costura or generated at runtime.
See [docs/BUILD.md](docs/BUILD.md) for which is which.

### Smoke test

```bash
./ICCCELite.exe --smoke-test
```

Results go to `Logs/Log_*.txt`; search for `[探针]`. **All 56 checks currently pass.**

This is not decoration: it verifies things you cannot see by reading code — whether a cache
actually hits, whether an error actually gets logged. Run it after changing code; it beats
staring at the screen and guessing.

---

## Documentation

| Document | Contents |
|---|---|
| [docs/LTSC-CHANGES.md](docs/LTSC-CHANGES.md) | Every change against upstream, grouped |
| [docs/LTSC-BASELINE.md](docs/LTSC-BASELINE.md) | How the baseline is verified and how to merge upstream commits |
| [docs/MASTER-PLAN.md](docs/MASTER-PLAN.md) | Full plan and execution record |
| [docs/BUILD.md](docs/BUILD.md) | Build and packaging |
| [docs/WINUI3-FEASIBILITY.md](docs/WINUI3-FEASIBILITY.md) | UI framework migration feasibility (verdict: not yet) |

---

## Credits

Built on the shoulders of upstream ICC CE and IC.

Lineage: IC (WXRIW) → ICA (ChangSakura) → ICC (Dubi906w) → ICC CE (CJK_mkp) → ICC CE Lite (Baimacao, 鲸仔_JZ).
