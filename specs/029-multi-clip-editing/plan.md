# Implementation Plan: Multi-Clip Editing in the Clip Editor

**Branch**: `feat/multi-clip-editing` | **Date**: 2026-10-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/029-multi-clip-editing/spec.md`

## Summary

Open several selected arrangement MIDI clips together in the Clip editor (GitHub
nota-daw/nota#48). The piano roll keeps one flat note list, now in *editor beats* (offset
from the earliest open clip) with a parallel per-note owner; a pure `ClipSet` model in
`Nota.Application/Editing` owns coordinates, the owner/"reach" rules for moving notes
across seams, and per-clip split/merge. A one-clip set is today's editor (parity by
construction). Cross-clip edits are committed per clip inside the engine's existing undo
group, so one Undo restores every clip — no engine or C ABI change. A clip strip under the
ruler switches the active clip and toggles reference (read-only) clips. See
[research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill (clip strip,
  subdued / reference note looks, header).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- Engine untouched; [`ARCHITECTURE.md`](../../ARCHITECTURE.md) AR rules hold trivially.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: N/A — the open clip set and reference flags are editor-session state; notes
persist through the existing per-clip engine lists.

**Audio-Thread Impact**: None. Notes reach the engine through the existing
`SetClipNotes` / `SetClipNotesLive` calls; grouping uses the existing
`nota_engine_undo_group_begin/end` on the message thread.

**Testing**: `tests/Nota.SmokeTest` — new `MultiClipTests.cs` (`--multiclip`) for pure
logic + one grouped-undo engine check; existing `--notes` must stay green; headless
Avalonia render for the strip and note looks.

**Target Platform**: macOS, Linux, Windows (desktop app).

**Project Type**: Desktop app (Avalonia) over a native engine.

**Performance Goals**: SC-003 — 8 clips / 2,000 notes drag as smoothly as one clip with
2,000 notes: owner lookups O(1); live drag pushes only clips whose notes changed.

**Constraints**: Single-clip and Session-slot editors behave exactly as today (FR-002).

**Scale/Scope**: ~1 new Application file, ~1 new App control, changes in
`PianoRollView*.cs`, `ClipEditorView.cs`, `ClipPropsView.cs`, `MidiToolsView.cs`,
`MainWindow.Detail.cs`, `MainWindow.Commands.cs`, `ArrangementView*.cs`,
`NoteOverwrite.cs`.

## Constitution Check

| Principle | Status |
|-----------|--------|
| I. Real-time audio safety | Pass — no audio-thread code touched |
| II. Boundary discipline | Pass — no ABI change; existing undo-group call reused |
| III. Licensing hygiene | Pass — new files get SPDX headers; no dependency |
| IV. Verification gate | Pass — smoke test extended (`MultiClipTests`, grouped undo); `scripts/build.sh` before PR |
| V. One logical change per PR | Pass — one feature; docs in the same PR as per repo practice |
| VI. Docs in sync | Planned — CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs`, `nota-shortcuts` (strip gestures, palette command) |
| VII. Spec-first | Pass — spec + clarifications done |
| VIII. Rules not weakened | Pass |

Post-design re-check: unchanged — Phase 1 introduced no engine, ABI or dependency change.

## Project Structure

### Documentation (this feature)

```text
specs/029-multi-clip-editing/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── piano-roll-api.md
│   └── ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Application/Editing/
│   ├── ClipSet.cs              # NEW: OpenClip, ClipSet, ClipNotes — coords, reach, owner rule, split/merge
│   └── NoteOverwrite.cs        # owner-aware Settle/Fate overloads (null owners = today)
└── Nota.App/
    ├── PianoRollView.cs        # _owner list, SetClips, active/reference, per-clip commit, paste routing, bounds
    ├── PianoRollView.ClipStrip.cs  # NEW: the clip strip control
    ├── PianoRollView.Expression.cs # notes by owner (velocity lane colours, reference skipped)
    ├── ClipEditorView.cs       # header "+N clips", ActiveClipChanged → EnvOverlay rebind
    ├── ClipPropsView.cs        # start/length/loop of the active clip
    ├── MidiToolsView.cs        # per-clip tool runs (R6)
    ├── MainWindow.Detail.cs    # BuildClipEditor(set), CommitClips + undo group, reload/geometry/selection-follow
    ├── MainWindow.Commands.cs  # "Edit selected clips" command
    ├── MainWindow.axaml.cs     # playhead via Origin
    └── ArrangementView*.cs     # SelectedMidiClips(), context-menu item

tests/Nota.SmokeTest/
├── MultiClipTests.cs           # NEW (--multiclip)
└── Program.cs                  # register the suite
```

**Structure Decision**: Managed-only change. Pure, testable rules go to
`Nota.Application/Editing` next to `NoteOverwrite`/`SnapGrid`; view and host changes stay
in `Nota.App` where the piano roll lives today.

## Delivery order (feeds /speckit-tasks)

1. `ClipSet` + owner-aware `NoteOverwrite` + `MultiClipTests` (pure, test-first).
2. Roll internals: `_owner`, editor beats, `SetClips`, per-clip commit; single-clip parity
   (`--notes` green). Host `BuildClipEditor(set)` + `CommitClips` with undo group. → Story 1 + core of Story 2.
3. Cross-clip editing details: draw target, move reach/owner, paste routing, select-all /
   marquee across clips, playhead/seek. → Story 2.
4. Clip strip, active switching, reference lock, note looks. → Story 3 (+ FR-008).
5. Inspector / envelopes / header / clip tools per clip / velocity lane. → Story 4.
6. Host robustness: selection-follow off in multi, geometry/reload/dead-clip handling.
7. Docs: CHANGELOG, FEATURES (`nota-features`), user docs (`nota-docs`), Shortcuts
   (`nota-shortcuts`); `scripts/build.sh`; `/speckit-converge`.

## Complexity Tracking

None — no constitution violations.
