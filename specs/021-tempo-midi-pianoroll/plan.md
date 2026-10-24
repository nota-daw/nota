# Implementation Plan: Tempo Track, MIDI Tempo and Piano Roll Event Editing

## Architecture

- Keep tempo points in the project/domain model and persist them via the existing project capture/apply service. Old project files must deserialize without a tempo map and retain their existing BPM.
- Keep transport state authoritative in `nota.engine`. Tempo-map commands cross the C ABI and are queued for the audio thread. Do not run a UI timer that calls `SetBpm` to approximate a tempo map.
- Rework musical-clock math without confusing beat position with elapsed audio samples. Loop splitting, note/controller chase, metronome events, count-in, live MIDI timestamps, clip rendering and plug-in transport snapshots must share the same mapping.
- Extend `MidiFileReader` to capture tempo/controller events while retaining existing note grouping semantics; add a matching writer with deterministic event ordering and the file's PPQ division.
- Reuse `MidiController`, `AutomationPoint`, and existing MIDI clip controller APIs for the Event Line Editor. The controller lane must not invent a second controller-value convention.
- Add focused engine and parser/writer tests first. Add UI behavior using existing `DESIGN.md` tokens and controls; avoid changing build scripts to make tests pass.

## Components

| Area | Files/components |
|---|---|
| Transport and engine | `Transport.h`, `Engine_Render.cpp`, `Engine.cpp`, public C ABI + P/Invoke |
| Project data | `ProjectModel.cs`, `ProjectService.cs`, project migration tests |
| MIDI files | `MidiFileReader.cs`, new MIDI writer, MIDI import/export command integration |
| Arrangement | `ArrangementView.cs`, transport context menu, tempo-track surface and point editor |
| Piano Roll | `PianoRollView.cs`, `PianoRollView.Expression.cs`, `ClipEditorView.cs`, `MainWindow.Detail.cs` |
| Verification | `Nota.SmokeTest` focused parser, transport, project round-trip and UI-contract tests |

## Safety and compatibility

- The native audio callback remains allocation-free; tempo points are prepared on a non-audio thread and swapped as immutable data at a safe boundary.
- Existing single-BPM projects map to a constant-tempo map without introducing an implicit tempo jump.
- The C ABI changes are versioned additively. All managed P/Invoke declarations must match the native signatures.
- Controller curves use IDs 0–127 for CC, 128 for pitch wheel, and 129 for channel pressure; values keep the normalisation specified in `EngineContracts.cs`.
- Avoid a broad rendering rewrite before correctness tests exist. Add viewport culling without excluding notes near the viewport edge.
- Run `scripts/build.sh`, `scripts/build-linux.sh`, `scripts/build-win.ps1` and the smoke test. Investigate any platform-specific failures rather than modifying the build scripts.

## Delivery slices

1. Transport tempo-change continuity and smoke regression.
2. MIDI parser/writer data model, including tempo and controllers.
3. Tempo-track project persistence, transport integration, and editing UI.
4. Event Line Editor controller picker/editing, independent resize/snap, Shift+wheel velocity.
5. Piano Roll seek/follow, vertical layout fix, viewport culling and box-selection optimization.
