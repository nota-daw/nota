---

description: "Task list for the event lane CC lanes and velocity wheel"
---

# Tasks: Event lane — MIDI CC lanes and the velocity wheel

**Input**: [spec.md](spec.md), [plan.md](plan.md); the lane contract is specs/020
[contracts/ui-lanes.md](../020-midi-cc-record-automate/contracts/ui-lanes.md).

`App/` means `src/managed/Nota.App/`.

## Phase 1: User Story 1 — controller lanes (P1)

Implements specs/020 T032–T037 and T039 (T038 waits for the CC automation target, specs/020 US3).

- [X] T001 [US1] `App/PianoRollView.Expression.cs`: `LaneMode.Controller`, `_laneCtl`, a fifth gutter tab (13 px rows so five fit the 72 px row) and the picker menu (data ctls first with the automation glyph; Sustain, Mod wheel, Pitch wheel, Channel pressure, Expression, Breath; Other CC ▸ in groups of 16 without reserved ctls).
- [X] T002 [US1] New `App/PianoRollView.Controllers.cs`: host delegates `ControllersInClip` / `GetController` / `SetController`, the per-ctl cache (dropped in `Load` and on active-clip change), and the `ControllerLane` control: clip-local ↔ editor time, switch blocks (draw with snap, resize edges, move body, double-click delete, partial fill height), continuous curves (freehand stroke replacing its stretch, ⌥ line, point drag, double-click add/delete, wheel centre snap), right-click *Clear <name>*, hover readout, one write per gesture.
- [X] T003 [US1] `App/MainWindow.Detail.cs`: wire the delegates for arrangement clips (the active clip, checked live with `TryGetClipInfo`) and Session slots; refresh the arrangement / Session view after a write.
- [X] T004 [US1] Mark specs/020 T032–T037 and T039 done.

## Phase 2: User Story 2 — velocity wheel (P2)

- [X] T005 [P] [US2] New `src/managed/Nota.Application/Editing/VelocityWheel.cs` (step accumulator) and the two `Settings` properties.
- [X] T006 [P] [US2] New `tests/Nota.SmokeTest/VelocityWheelTests.cs` (direction, multiplier, 0× off, accumulation, cap, clamp 1–127) and its call in `Program.cs`.
- [X] T007 [US2] `App/PianoRollView.cs` `NoteGrid.OnPointerWheelChanged`: Shift over an editable note → velocity burst (seeded undo, live stream, 0.6 s idle end); otherwise unchanged scrolling.
- [X] T008 [US2] `App/MainWindow.axaml.cs` seeds the roll's static wheel settings; `App/PreferencesWindow.cs` Shortcuts page gets a "Velocity wheel" section (Reverse switch, multiplier 0×–2×) that saves and applies them.

## Phase 3: Polish

- [X] T009 Shortcuts pane rows (nota-shortcuts): Shift + wheel over a note; the controller lane gestures.
- [X] T010 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` (nota-features).
- [X] T011 `scripts/build-linux.sh`: native, managed and the smoke test (velocity wheel and design-token checks pass; the render-pool burst guard fails on this VM on unmodified `develop` too — rerun alone with `--perf` on both, results in the PR).
- [X] T012 Before/after screenshots of the lane gutter, a Sustain and a CC lane and the Preferences section (nota-pr), on `pr-assets` under `issue-169/`.
