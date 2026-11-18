# Implementation Plan: Toolbar overflow

**Branch**: `feat/toolbar-overflow` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/097-toolbar-overflow/spec.md` (issue #520)

## Summary

The transport bar already sheds controls on a narrow window (#303, `MainWindow.TransportFit.cs`);
a shed control is just hidden. This feature adds a never-shed overflow button (⋯) at the right
end of the bar. Clicking it opens an ordinary Nota menu (a `MenuFlyout` built with `MenuKit`):
first one entry per shed control, in bar order, each doing what the control does; then a fixed
quick-functions group with the interface-scale presets. Which entries appear for a given fit is
decided by a small pure model in `Nota.Presentation` (testable in the smoke test); the App
layer builds the menu from it at open time and wires each entry to the handler the bar
control already uses.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: the button is a
  `tp-icon` key like the other tool keys; the menu is a stock `MenuKit` menu (spec 034).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only).

**Primary Dependencies**: Avalonia 12.1.4 (existing). No new dependency.

**Storage**: the interface scale is the existing `AppSettings.InterfaceScale` (settings file);
nothing new is stored.

**Audio-Thread Impact**: None — UI-only change. Entries call the same view-model / engine
setters the bar controls call (tempo, master volume through `MixGesture`, transport flags).

**Testing**: `tests/Nota.SmokeTest` — new `ToolbarOverflowTests` on the pure model (entries per
shed step, bar order, no duplicates, info-only parts omitted); the existing localization and
menu source checks cover the new keys and menu code. In-app check per
[quickstart.md](quickstart.md).

**Target Platform**: macOS, Windows, Linux.

**Project Type**: desktop app.

**Performance Goals**: the menu opens within one frame; the fit pass gains no extra measure.

**Constraints**: the shed order and never-shed set stay exactly as today (FR-003); the
overflow button joins the never-shed set.

**Scale/Scope**: one new button, one menu, ~15 entry kinds; one new glyph; ~25 new English
strings.

## Constitution Check

| Principle | Status |
|-----------|--------|
| 0. Approved issue first | ✅ #520 carries `approved` |
| I. Real-time audio safety | ✅ no audio-thread code touched |
| II. Boundary discipline | ✅ no ABI / JUCE change |
| III. Licensing hygiene | ✅ new files carry the SPDX header; no dependency |
| IV. Verification gate | ✅ `scripts/build.sh` + smoke test before the PR |
| V. One logical change per PR | ✅ #520 only |
| VI. Documentation in sync | ✅ CHANGELOG, FEATURES.md, nota-docs (transport bar page), shortcuts unaffected |
| VII. Spec first | ✅ this spec |
| VIII. Safety rules not weakened | ✅ |

Post-design re-check: unchanged — all green.

## Project Structure

### Documentation (this feature)

```text
specs/097-toolbar-overflow/
├── spec.md
├── plan.md              # this file
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   └── overflow-menu.md # the menu's entries, per shed control
└── tasks.md             # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Presentation/
│   └── ToolbarOverflow.cs                 # NEW pure model: parts, shed order, bar order, entries for a fit
├── Nota.Application/Localization/en.json  # new "overflow.*" keys
└── Nota.App/
    ├── MainWindow.axaml                   # OverflowBtn in the bar's last column
    ├── MainWindow.TransportFit.cs         # shed order from the model; remembers how many steps are shed; marks the button
    ├── MainWindow.TransportOverflow.cs    # NEW: build + show the overflow menu
    ├── MainWindow.axaml.cs                # master fader factory shared with the menu; tap/time-sig helpers reused
    ├── SnapChip.cs                        # grid menu items reusable as a submenu
    ├── PreferencesWindow.cs               # scale picker follows NotaUiScale.Changed
    └── Controls/Glyph.cs                  # GlyphKind.More (three dots)

tests/Nota.SmokeTest/
└── ToolbarOverflowTests.cs                # NEW
```

**Structure Decision**: the decision "which entries for which fit" is pure data and goes in
`Nota.Presentation` so the smoke test (which does not reference `Nota.App`) can check it; all
Avalonia code stays in `Nota.App` beside the existing fit code.

## Complexity Tracking

No violations.
