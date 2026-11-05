# Implementation Plan: Fade audio clips when they are split

**Branch**: `cursor/split-crossfade-2773` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/068-split-fades/spec.md`

## Summary

An audio split writes a 4 ms ADSR edge at the cut: release on the left piece, attack on the right. The handles that already draw those stages are the control the user drags. A settings switch (default on) gates the automatic path. **Fade at splits** raises a shorter edge up to that length on an existing join, even when the switch is off, and never shortens a longer one. MIDI and every non-split edit stay hard cuts.

## Nota: Read Before Planning

Consulted [`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR-5 snapshots, AR-6 audio thread, AR-7 C ABI, AR-9 Transport), [`DESIGN.md`](../../DESIGN.md), and [`CONTRIBUTING.md`](../../CONTRIBUTING.md). No new dependency. No JUCE. No new musical-time source: the fade length in beats is `0.004 * authoringBpm / 60`, and `authoringBpm_` is the message-thread tempo mirror `setBpm` already keeps (default 120).

## Technical Context

**Language/Version**: C++20 engine, C# / .NET 10 app

**Primary Dependencies**: None new

**Storage**: App settings JSON (`SplitFades`, default true). The fade itself is the clip's ADSR, already stored in the project.

**Audio-Thread Impact**: None — authoring-thread clip data. The render path already applies ADSR (`ClipAdsr::gainAt` / `applyEnv`) and does not gain a branch, an allocation, a lock, or I/O. The fade is written into the graph snapshot the split already publishes.

**Testing**: `tests/Nota.SmokeTest/SplitFadeTests.cs` (`--split-fades` and the full smoke run). `WarpSplitTests` turns the flag off so its sample-exact warped split stays valid. The existing ADSR split fixture expects the left release to stay 0 when attack and decay already fill the piece, and a short attack on the right.

**Target Platform**: Linux, macOS, Windows (the engine and the app). This change is built and smoke-tested on Linux.

**Project Type**: Desktop audio app

**Performance Goals**: No extra work on the audio thread. A split still publishes one snapshot.

**Constraints**: No overlap between clips on a track, so this cannot be a constant-power overlap. Do not take Shift+X. Do not fade inside `audioSlice` / `audioCarve` themselves — delete, duplicate, and insert time call those too.

**Scale/Scope**: One flag, one command, one settings switch, the two split entry points.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

- AR-5: the fade is fields on the clip inside the snapshot `splitClip` / `splitClipsAtRange` / `fadeTouchingSplits` already publish. One `pushUndo` each.
- AR-6: no audio-thread code added.
- AR-7: `nota_engine_set_split_fades`, `nota_engine_split_fades`, `nota_clips_fade_splits`.
- AR-9: tempo comes from the existing authoring BPM mirror, not a new clock.
- AGPL header on the new smoke file. No new dependency.

Re-check after design: the same. Pass.

## Project Structure

### Documentation (this feature)

```text
specs/068-split-fades/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── spec.md
└── tasks.md
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # the three ABI functions
├── src/abi/nota_engine_track.cpp       # wrappers
└── src/engine/Engine.h                 # flag + fadeTouchingSplits
    Engine_Tracks.cpp                   # raiseSplitEdge, the two split paths
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs
├── Nota.Application/Settings.cs
├── Nota.Application/Palette/CommandCatalog.cs
├── Nota.Infrastructure/Interop/NativeMethods.Tracks.cs
├── Nota.Infrastructure/Interop/NotaEngine.Tracks.cs
└── Nota.App/   PreferencesWindow.cs, MainWindow.axaml.cs, MainWindow.Commands.cs,
                MainWindow.Menu.cs, ArrangementView.cs, ArrangementView.LaneControl.cs,
                Languages/fr.json
tests/Nota.SmokeTest/SplitFadeTests.cs, WarpSplitTests.cs, Program.cs
```

**Structure Decision**: The fade is clip ADSR written on the authoring thread at the two split sites and by one new batch command. The app only stores the preference and forwards it.

## Phase 0 — Research

See [research.md](research.md). Decision: visible ADSR edges, 4 ms, preference default on, palette and context menu, no shortcut.

## Phase 1 — Design

See [data-model.md](data-model.md) and [quickstart.md](quickstart.md).

`raiseSplitEdge` only raises an edge that is shorter than the default, capped at half the piece and at the room the other stages leave. `splitClip` and `splitClipsAtRange` call it only when `splitFades_` is set, and only on edges that the cut created. `fadeTouchingSplits` calls it on every abutting pair (within 1e-4 beats) where at least one clip is selected, ignoring the preference. Both publish one snapshot.
