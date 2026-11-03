# Implementation Plan: Open the Detail panel on double-click, not on every selection

**Branch**: `071-detail-panel-double-click` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/071-detail-panel-double-click/spec.md`

## Summary

A plain selection (click, keyboard, side effect of another gesture) stops opening a closed
Detail panel; an open (or popped-out) panel keeps following the selection exactly as today.
Double-click becomes the way in: a track header opens Devices, a clip or a filled Session
slot opens Clip. The arrangement header double-click on an instrument track stops opening
the plug-in window (it stays on **Open editor** on the plug-in's card). A new Preferences
switch "Open Detail panel on select" (off by default) restores the old behaviour.

Approach (see [research.md](research.md)): every path that makes the docked panel visible
goes through `MainWindow.ShowDetail(...)`. The three selection-follow entry points
(`OnTrackSelected`, `OnSessionSlotSelected`, `FollowClipSelection`) run inside a
"follow-only" scope; inside it `ShowDetail` returns early when the panel is hidden and the
preference is off. Content (device chain, clip editor, tab buttons) still updates, so
opening the panel later shows the current selection. Explicit callers (double-click,
View menu, "Show devices", the Devices/Clip tabs, browser drops, Convert) are outside the
scope and open the panel as today.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md): the only new visual is one switch row in
  Preferences → Appearance, built with the existing `SwitchRow` + `Caption` helpers.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing); no new dependency

**Storage**: one new bool in the user settings file (`Nota.Application/Settings.cs` →
`OpenDetailOnSelect`), persisted by the existing settings service

**Audio-Thread Impact**: None — UI-only change; no engine call is added or changed

**Testing**: no managed unit-test project exists; verification is a headless Avalonia
harness that drives the real `MainWindow` with synthetic pointer events (see
[quickstart.md](quickstart.md)), plus `scripts/build.sh` (build + smoke test) as the PR gate

**Target Platform**: macOS, Windows, Linux desktop (same code path everywhere)

**Project Type**: desktop app

**Performance Goals**: no measurable change — the gate is a bool check per selection

**Constraints**: popped-out Detail window behaviour unchanged; Session/Arrangement parity

**Scale/Scope**: ~6 files in `src/managed/Nota.App` + `Nota.Application/Settings.cs`,
`Languages/fr.json`, CHANGELOG, FEATURES.md, Preferences → Shortcuts, user docs (nota-docs)

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status |
|-----------|--------|
| 0. Approved issue first | ✅ #308 carries `approved` (added by a core contributor, 2026-11-02) |
| I. Real-time audio safety | ✅ N/A — no audio-thread code touched |
| II. Boundary discipline | ✅ No C ABI change; `OpenPluginEditor` call removed from one UI gesture only |
| III. Licensing hygiene | ✅ No new dependency; any new `.cs` file gets the SPDX header (none planned) |
| IV. Verification gate | ✅ `scripts/build.sh` before PR; no engine behaviour change, so no smoke-test extension |
| V. One logical change | ✅ Single behaviour change + its opt-out switch |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]` (Changed), FEATURES.md (nota-features), Preferences → Shortcuts (nota-shortcuts), user docs EN+RU (nota-docs), `fr.json` strings |
| VII. Spec-first | ✅ spec → clarify (done in specify) → this plan → tasks |
| VIII. Safety rules not weakened | ✅ |

Post-design re-check: unchanged — the design adds no engine, ABI or dependency surface.

## Project Structure

### Documentation (this feature)

```text
specs/071-detail-panel-double-click/
├── plan.md              # This file
├── research.md          # Phase 0
├── data-model.md        # Phase 1 (one setting + panel state rules)
├── quickstart.md        # Phase 1 (headless verification scenarios)
├── contracts/
│   └── gestures.md      # Phase 1 (the mouse-gesture contract)
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   └── Settings.cs                    # + OpenDetailOnSelect (bool, default false)
└── Nota.App/
    ├── MainWindow.Detail.cs           # follow-only scope; ShowDetail gate; OnTrackSelected,
    │                                  #   OnSessionSlotSelected wrapped; + OpenTrackDevices(id)
    ├── MainWindow.axaml.cs            # wire Timeline.TrackDevicesRequested → OpenTrackDevices;
    │                                  #   FollowClipSelection wrapped
    ├── MainWindow.Menu.cs             # view.toggleDetail opening re-shows the checked tab
    ├── ArrangementView.cs             # header double-click: select + TrackDevicesRequested
    │                                  #   (replaces OpenPluginEditor for instrument tracks)
    ├── PreferencesWindow.cs           # Appearance → "Detail panel" switch row; Shortcuts rows
    └── Languages/fr.json              # new strings
CHANGELOG.md, FEATURES.md              # Changed entry / feature line
../nota-docs                           # EN + RU pages that say "click a track to see its devices"
```

**Structure Decision**: managed UI only, all inside `Nota.App` plus one settings property in
`Nota.Application`. No new files in source.

## Complexity Tracking

No constitution violations.
