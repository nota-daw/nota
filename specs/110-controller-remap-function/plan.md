# Implementation Plan: Remap one controller function without redoing the wizard

**Branch**: `feat/controller-remap-function` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/110-controller-remap-function/spec.md` (issue #973)

## Summary

Preferences → MIDI → controller → **Set up** opens a new **mapping editor** when the controller has
a generic profile: every function the wizard knows, grouped, each with its control; pick a row,
press a control, Finish. Conflicts are resolved inline (Move / Cancel), rows can be cleared or
reverted, any palette command can be added on a spare button (+ Command…), a shipped profile is
saved as a same-id user copy (Back to shipped profile removes it), and **Learn everything again**
runs the existing wizard pre-filled with the current controls.

Technical approach: a pure, testable **`ProfileMapping`** in `Nota.Application/Controllers` holds
the profile's control list plus pending edits and writes the result back into the **original JSON
document** (so init / release, LED feedback, `motorised`, `source`, unknown fields and hand edits
survive). The wizard's function list moves into a shared App-side **`SetupFunctions`** catalog used
by both windows; the wizard's learn heuristics move into a shared **`ControlCatcher`**. No engine
change.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill (dialog look, orange in
  three strengths, Geist tabular figures, sentence case).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- Existing behaviour → [specs/051](../051-controller-auto-mapping/spec.md) FR-030 / 030a–c and its
  profile contract `specs/051-controller-auto-mapping/contracts/controller-profile.md`.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing); `System.Text.Json.Nodes` (BCL) — no new dependency

**Storage**: controller profile JSON files in the user profiles folder (`<DataDir>/controllers`);
shipped profiles stay embedded resources

**Audio-Thread Impact**: None. The editor is UI + files; MIDI reaches it through the existing
capture path (`ControllerService.Capture` / `TakeCaptured`, already used by the wizard), which
claims the port while the window is open.

**Testing**: `tests/Nota.SmokeTest` (`ControllerRemapTests.cs`: mapping edits, conflicts, JSON
round-trip preserving feedback / init, shipped-copy override, command rows); headless Avalonia
harness for screenshots; real KeyLab Essential 61 mk3 (DAW profile) hand check

**Target Platform**: macOS / Windows / Linux desktop

**Project Type**: desktop app

**Performance Goals**: learn feels instant (the wizard's 350–600 ms "still" windows); ~40 rows
render without virtualisation

**Constraints**: profile format unchanged (format 1); MCU / HUI profiles not editable

**Scale/Scope**: 3 new App files, 1 new Application file, wizard + service + Preferences edits,
~40 new strings

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | ✅ #973 `approved`, milestone 0.67.1 |
| I Real-time safety | ✅ no audio-thread code |
| II Boundary | ✅ no ABI change |
| III Licensing | ✅ SPDX headers on new files, no dependency |
| IV Verification | ✅ smoke tests for the pure model; build.sh before PR |
| V One change | ✅ one feature, one PR (+ docs PR) |
| VI Docs | CHANGELOG, FEATURES.md, nota-docs Controllers page, RU strings via nota-translations |
| VII Spec-first | ✅ this plan |
| VIII | ✅ nothing bent |

Re-check after design: unchanged — pass.

## Project Structure

### Documentation (this feature)

```text
specs/110-controller-remap-function/
├── plan.md, research.md, data-model.md, quickstart.md
├── contracts/mapping-editor.md   # window layout, states, keys; profile write rules
└── tasks.md
```

### Source Code

```text
src/managed/Nota.Application/Controllers/
├── ProfileMapping.cs           # NEW: FunctionKey, edit model, conflicts, JSON write-back
└── ProfileWriter.cs            # shared control-entry writer reused by ProfileMapping

src/managed/Nota.App/Controllers/
├── SetupFunctions.cs           # NEW: the wizard's function list (groups, halves, icons, names)
├── ControlCatcher.cs           # NEW: learn heuristics (still / press-release / encoder vs button)
├── ControllerMappingWindow.cs  # NEW: the editor (+ command picker flyout)
├── ControllerSetupWindow.cs    # uses SetupFunctions + ControlCatcher; seeded (pre-filled) mode
└── ControllerService.cs        # ProfileJson(), HasShipped(id), RemoveUserProfile()

src/managed/Nota.App/PreferencesWindow.Controllers.cs   # Set up → editor when a generic profile
src/managed/Nota.Application/Localization/en.json        # strings (RU in nota-translations)

tests/Nota.SmokeTest/ControllerRemapTests.cs            # NEW
```

**Structure Decision**: the edit model is UI-free and lives next to `ProfileLoader` /
`ProfileWriter` so the smoke test drives it directly; anything with icons, timers or Avalonia
stays in `Nota.App/Controllers`.

## Complexity Tracking

None.
