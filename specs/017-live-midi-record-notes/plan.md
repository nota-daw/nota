# Implementation Plan: Live MIDI notes while recording

**Branch**: `feat/live-midi-record-notes` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/017-live-midi-record-notes/spec.md`

## Summary

While a MIDI take records, held notes show in the take from their start to the
capture clock and grow tick by tick. New notes fade in with an accent flash over
about 150 ms. Notes played during the count-in, and keys already held when Record is
pressed, are recorded at their real start. The take's clip moves its left edge back to
include them. Before 1.1 the note keeps a head past the clip's left edge, and the part
inside the clip sounds from the clip start.

Engine: a capture clock that also runs during the count-in, seeding of held keys on the
record edge, a seqlock snapshot of held captured notes, a new C ABI read, the clip
left-move in `poll`, and the negative-start render rule. UI: one snapshot read per
recording tick, drawn in the arrangement take, the Session slot and the piano roll, plus
a shared appear-animation tracker. Details and rationale: [research.md](research.md).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app, Avalonia UI)

**Primary Dependencies**: None new. The engine uses only `<atomic>`, and the UI uses Avalonia drawing.

**Storage**: No new storage. Early notes are stored as ordinary clip notes, and a note can have a negative `startBeat` when it starts before 1.1. The project format is unchanged: a note's start is already a double.

**Audio-Thread Impact**:
- Capture now also runs during the count-in.
- Held keys are tracked while the transport rolls or a count-in runs.
- Each block publishes up to 256 snapshot entries (128 per source) through relaxed atomics and a sequence counter.
- There are no allocations, locks or I/O. The fixed arrays are sized at 128 pitches × 2 sources and live in `Engine` (AR-6).
- Note gating in the render accepts a note that starts before the clip and ends inside it. This is a branch on data already read.
- AR-9 holds: the capture clock is derived from `Transport` and the count-in counters.

**Testing**: `tests/Nota.SmokeTest` (offline engine through the C ABI), `scripts/build.sh`. Visual check of the animation via the headless and in-app capture recipe ([quickstart.md](quickstart.md)).

**Target Platform**: macOS, Linux, Windows desktop.

**Project Type**: desktop app (native engine + managed UI).

**Performance Goals**:
- A held note shows up by the next recording tick (≤ 33 ms plus audio latency).
- The animation runs about 150 ms, which is 5 frames at 30 Hz.

**Constraints**:
- No new repaints while idle.
- The preview piggybacks on the existing `RecordingTick` refresh.
- No new timer unless the quickstart shows the animation is choppy.

**Scale/Scope**:
- Up to 128 held pitches per source.
- Three views: arrangement lanes, Session grid and piano roll.
- About 6 engine files, 1 header, 3 interop/app layers and 1 smoke-test file.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Real-time audio safety | ✅ | The seqlock writer is wait-free and uses fixed arrays. No alloc, lock or I/O. Commands still enter through the queue. Clip edits happen on the message thread in `poll` (AR-5 republish). |
| II. Boundary discipline | ✅ | One new C ABI function and a POD struct in `nota_engine.h`. No C++ types cross the boundary, and JUCE is not involved. |
| III. Licensing hygiene | ✅ | No new dependency. New files (`LiveRecordTests.cs`, possibly a small UI helper) carry the SPDX header. |
| IV. Verification gate | ✅ | Smoke tests cover the new read, count-in capture, held-key seeding, clip left move and negative-head render (R8). |
| V. One logical change | ✅ | Everything here makes "what you play while recording is what you see" true. It is still one PR, but tasks keep engine and UI separable. |
| VI. Docs in sync | ✅ | Planned: a CHANGELOG `[Unreleased]` entry, `nota-features` (FEATURES.md) and `nota-docs` (recording page: live notes and count-in pickup). No shortcuts change. |
| VII. Spec-first | ✅ | spec → clarify (2 Q) → this plan. |
| VIII. Rules not weakened | ✅ | Nothing bends I–III. |

Post-design re-check: the design artifacts introduce nothing beyond the table above, so the check still passes.

## Project Structure

### Documentation (this feature)

```text
specs/017-live-midi-record-notes/
├── plan.md              # this file
├── research.md          # Phase 0: decisions R1–R8
├── data-model.md        # Phase 1: held note, snapshot, capture clock, animation entry
├── quickstart.md        # Phase 1: how to validate end-to-end
├── contracts/
│   ├── c-abi.md         # nota_engine_record_held_notes + NotaRecordHeldNote
│   └── ui-preview.md    # what each view draws, animation timing, repaint rules
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h      # + NotaRecordHeldNote, nota_engine_record_held_notes
└── src/
    ├── Engine.h                    # capture clock helper, held-key arrays, snapshot (seq + slots)
    ├── Engine_Render.cpp           # drainLiveMidi / captureRoutedNotes gating + clock,
    │                               # record-edge seeding, snapshot publish, poll() clip left move,
    │                               # negative-start note rule (computeBlockMidi + session paths)
    ├── Engine_Recording.cpp        # recordHeldNotes() reader; take start beat for count-in clock
    ├── Engine_Tracks.cpp           # negative-start rule in the offline/bounce note path (:1807)
    └── nota_engine_track.cpp       # C ABI shim

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs        # + RecordHeldNotes(...)
├── Nota.Infrastructure/Interop/NativeMethods.cs   # + P/Invoke, struct
├── Nota.Infrastructure/Interop/NotaEngine.cs      # + wrapper
├── Nota.Presentation/ViewModels/MainWindowViewModel.cs  # RecordingTick unchanged (already 30 Hz while recording)
└── Nota.App/
    ├── RecordPreview.cs            # NEW: per-tick snapshot + appear-animation tracker shared by views
    ├── MainWindow.axaml.cs         # RecordingTick → RecordPreview.Update, then refreshes as today
    ├── ArrangementView.LaneControl.cs  # draw held notes in the take row (incl. left of the clip)
    ├── SessionView.Grid.cs         # draw held notes in the recording slot
    └── PianoRollView.cs            # held-note overlay when it shows the record clip

tests/Nota.SmokeTest/
├── LiveRecordTests.cs              # NEW (R8)
└── Program.cs                      # register the suite
```

**Structure Decision**: The existing engine split stays: capture lives in `Engine_Render.cpp` and the take lifecycle in `Engine_Recording.cpp`. The C ABI shim sits next to `nota_engine_live_held_notes` in `nota_engine_track.cpp`. On the UI side, one small `RecordPreview` class owns the snapshot and the animation clock, so the three views draw from the same data in the same frame.

## Complexity Tracking

No constitution violations, nothing to justify.
