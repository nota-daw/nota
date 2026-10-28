# Implementation Plan: Piano roll Fold

**Branch**: `feat/piano-roll-fold` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)
**Input**: Feature specification from `/specs/045-piano-roll-fold/spec.md`

## Summary

One **Fold** switch in the piano roll toolbar. On a melodic track it hides out-of-scale rows; on a
Drum Rack track it shows one named row per pad. Rows that hold notes are never hidden. All vertical
layout goes through a new pure `PianoRowMap`, so folded and unfolded share one code path. Moves
step by visible rows. The flag is per track: engine track UI metadata (like the track colour),
saved as an optional project field, and exposed to MCP.

## Technical Context

**Language/Version**: C# / .NET 10 (app), C++20 (engine: one metadata field only)
**Primary Dependencies**: Avalonia (existing), no new dependency
**Storage**: Project track field `PianoFold` (optional; no format bump)
**Testing**: `tests/Nota.SmokeTest`, a new `FoldTests` (`--piano-fold`); macOS hands-on check
**Target Platform**: macOS, Windows, Linux desktop
**Project Type**: Desktop app over a native engine
**Performance Goals**: A fold of 2,000 notes rebuilds and redraws within 16 ms
**Constraints**: No audio-thread involvement; the flag is control-thread-only
**Scale/Scope**: `PianoRowMap` (new), `PianoRollView` (12 mapping sites, drag, transpose, keys
column), `ClipEditorView` toolbar, `DrumPatternView` pad scan (shared), engine/ABI/Managed/MCP
getters and setters, `ProjectService`, docs EN+RU

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass: the flag is never read on the audio thread; no graph change |
| II. Boundary discipline | Pass: two plain C functions on `nota_engine.h` |
| III. Licensing | Pass: new files carry the SPDX header; no dependency |
| IV. Verification gate | Pass: `FoldTests` added to the smoke run |
| V. One logical change | Pass: one feature (Fold for both modes, per #344/#345's one-button rule) |
| VI. Docs in sync | Plan: piano roll page EN+RU, shortcuts pane, FEATURES.md, CHANGELOG |
| VII. Spec first | This spec |
| VIII. Safety rules | Not touched |

Re-check after design: no change.

## Project Structure

### Documentation (this feature)

```text
specs/045-piano-roll-fold/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/abi-and-mcp.md
└── tasks.md
```

### Source Code

```text
src/native/nota.engine/src/engine/Track.h            pianoFold beside colorIndex
src/native/nota.engine/src/engine/Engine*.{h,cpp}     set/get + clone paths copy it
include/nota/nota_engine.h, src/abi/nota_engine_track.cpp   nota_track_set/get_piano_fold
src/managed/Nota.Application/Engine/IAudioEngine.cs   SetTrackPianoFold / GetTrackPianoFold
src/managed/Nota.Infrastructure/...                   NativeMethods + NotaEngine wrappers
src/managed/Nota.Infrastructure/Persistence/          ProjectModel.PianoFold, Capture/Apply
src/managed/Nota.Presentation/PianoRowMap.cs          new: the row map (pure)
src/managed/Nota.Presentation/DrumPads.cs             new: pad scan shared with DrumPatternView
src/managed/Nota.App/PianoRollView*.cs                map-driven layout, row-step moves
src/managed/Nota.App/ClipEditorView.cs                Fold switch beside Scale
src/managed/Nota.Mcp/Tools/                           set_piano_fold / get_piano_fold
tests/Nota.SmokeTest/FoldTests.cs                     new
```

**Structure Decision**: The map and the pad scan live in Nota.Presentation, so the smoke test
covers them without a window. The view only consumes the map.

## Complexity Tracking

None.
