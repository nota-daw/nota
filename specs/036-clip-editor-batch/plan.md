# Implementation Plan: Clip editor batch — playhead, arrangement ruler, keyboard focus, warp tempo

**Branch**: `feat/clip-editor-batch` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/036-clip-editor-batch/spec.md`

## Summary

Six clip-editor issues in one UI-only change. A pure `ClipTimeMap` maps the audio editor's
content axis to arrangement beats; it fixes the hidden stopped playhead (#277), drives
ruler / playhead-line seeking in both editors (#278, Session slots excluded with a "not
available" cursor) and relabels the rulers in arrangement position, with a shared
bars / time mode that follows the transport read-out across the arrangement, piano roll and
audio rulers (#275). The keyboard fix (#114) starts from a reproduction; the likely causes
are a focused props-rail button re-activated by Space's KeyUp and grid commands landing on
an inert clip-editor grid. A Tempo row (`DragNumber` + ÷2 / ×2) under the Warp switch scales
markers and trim in one undo group, or auto-warps an unwarped clip at the typed tempo via the
existing `AutoWarpClipAtBpm` (#248). The marker drag previews the stretch by remapping the
committed peaks through the old and new markers and commits once on release (#140). No
engine, ABI or file-format change. Details: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill — the
  Tempo row, the playhead-grab cursor, the "not available" state, ruler time labels.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- AR-9 (`Transport` is the only source of musical time): seeks go through `Engine.Seek`;
  the UI only converts positions for display.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia 12 (existing); no new dependency

**Storage**: N/A — no project, preset or settings change; the bars/time mode stays the
transport view model's existing in-memory flag

**Audio-Thread Impact**: None. Seeks use the existing `Engine.Seek`. Warp edits use the
existing marker / trim / auto-warp entry points through `WarpEdit` (deferred background
stretch). The live marker preview is display-only and sends nothing to the engine until
release.

**Testing**: `scripts/build.sh` (smoke test + design checks). New pure types (`ClipTimeMap`,
`ClipTempo`, `WarpRemap`, `RulerLabels`) linked as source into `tests/Nota.SmokeTest`
(`--clipeditor` section), like `ValueText`. UI flows: headless Avalonia harness and the
real app per [quickstart.md](quickstart.md).

**Target Platform**: macOS, Windows, Linux (desktop)

**Project Type**: desktop app

**Performance Goals**: live warp preview redraws at the UI's normal rate on a 5-minute clip
(SC-004): O(columns × log markers) per frame, no allocation per frame beyond what `Render`
does today

**Constraints**: grid lines and snapping unchanged (FR-011); note entry in the piano roll
must not be stolen by the playhead grab (narrow zone, click falls through)

**Scale/Scope**: ~8 files in `src/managed/Nota.App` (AudioClipEditorView, PianoRollView,
ArrangementView.Controls, MainWindow.Detail / .Input / .axaml.cs / .Commands, ClipEditorKit)
plus 4 new small pure files and a smoke-test section

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio-thread code; preview is UI-only |
| II. Boundary discipline | Pass — existing C ABI entry points only (`seek`, warp markers / trim, `auto_warp_bpm`, undo group) |
| III. Licensing hygiene | Pass — new files get the SPDX header; no dependency |
| IV. Verification gate | Pass — `scripts/build.sh`; new smoke-test section for the pure math; no engine behaviour change |
| V. One logical change per PR | **Justified exception** — see Complexity Tracking |
| VI. Docs in sync | Planned — CHANGELOG `[Unreleased]`, nota-features, nota-docs (clip editor + warping pages), nota-shortcuts (playhead drag, ruler click in the audio editor) |
| VII. Spec first | Pass — spec, clarifications, this plan; tasks next |
| VIII. Rules not weakened | Pass |

Post-design re-check (after research / data-model / contracts): unchanged — all gates pass,
the one exception stays as justified.

## Project Structure

### Documentation (this feature)

```text
specs/036-clip-editor-batch/
├── plan.md              # this file
├── research.md          # R1–R7
├── data-model.md        # ClipTimeMap, ClipTempo, WarpRemap, RulerLabels, RulerDisplay
├── quickstart.md        # build, smoke section, in-app checks, screenshots
├── contracts/
│   └── clip-editor-ui.md
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/Nota.App/
├── ClipEditor/                     # NEW folder for the pure helpers
│   ├── ClipTimeMap.cs              # NEW — content ↔ arrangement mapping (R1)
│   ├── ClipTempo.cs                # NEW — played-region tempo, scale, ÷2/×2 (R5)
│   ├── WarpRemap.cs                # NEW — old↔new marker remap for the live preview (R6)
│   └── RulerLabels.cs              # NEW — bars/time labels + RulerDisplay mode (R3)
├── AudioClipEditorView.cs          # playhead gate, Seek, ruler + line gestures, labels,
│                                   # Tempo row, live preview, CancelGesture
├── PianoRollView.cs                # ruler labels (Origin + b, time), playhead-line grab,
│                                   # slot "not available" state
├── ArrangementView.Controls.cs     # ruler time mode
├── ClipEditorKit.cs                # pointer-only buttons not focusable (R4, if confirmed)
├── MainWindow.Detail.cs            # audio editor Seek wiring (arrangement clips only)
├── MainWindow.Input.cs             # Space KeyUp swallow; Esc → editor CancelGesture (R4, R6)
├── MainWindow.Commands.cs          # grid target falls back when the clip grid is inert (R4)
└── MainWindow.axaml.cs             # RulerDisplay ← Transport.ShowTimeDisplay

tests/Nota.SmokeTest/
├── Nota.SmokeTest.csproj           # link the four pure files
└── ClipEditorTests.cs              # NEW — `--clipeditor` section
```

**Structure Decision**: Managed UI only, in `Nota.App`, which already owns both clip
editors and the rulers. The pure helpers sit in a new `ClipEditor/` folder so the smoke
test can link them without Avalonia (precedent: `Controls/ValueText.cs`).

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Principle V: six issues in one PR | The user asked for one batch PR; all six change the same two editors and share the new position mapping (#277 / #278 / #275) and the warp code (#248 / #140). Precedent: #272, #273. | Six PRs would land the shared `ClipTimeMap` three times or serialise them; the review cost is lower as one PR with one commit per issue. |
