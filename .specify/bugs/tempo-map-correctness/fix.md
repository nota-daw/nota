<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Tempo-map correctness across editing and capture

- **Slug**: tempo-map-correctness (user selected, reused from assessment)
- **Fixed**: 2026-11-12
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Replace the remaining single-tempo conversions in editing, recording, frozen playback,
capture sizing and waveform placement with integrated map endpoints. Refresh the tempo
lane after transport BPM edits and use the ruler's denominator-aware bar length for grids.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | modified | Mapped split/trim/carve/range offsets, natural audio spans and destination placement; mapped freeze sizing and validated backward-compatible frozen timing blobs. |
| `src/native/nota.engine/src/engine/Engine_Recording.cpp` | modified | Mapped loop capacity, entry offset, partial/full passes, latency and take materialization; device-free recording regression checks. |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | modified | Read frozen PCM using immutable capture timing; direct elapsed-sample path when maps match. |
| `src/native/nota.engine/src/engine/Engine.h`, `Track.h`, `Track.cpp`, `Engine.cpp`, `Engine_Takes.cpp` | modified | Immutable capture/freeze/take timing, graph cloning, take placement and comp boundaries. |
| `src/native/nota.engine/include/nota/nota_engine.h`, `src/native/nota.engine/src/abi/nota_engine_track.cpp` | modified | Additive range-bounce C API; existing entry points preserved. |
| `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs`, `src/managed/Nota.Application/Engine/IAudioEngine.cs`, `src/managed/Nota.Infrastructure/Audio/TrackFreezer.cs` | modified | Pass both range endpoints into capture sizing. |
| `src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.Tempo.cs`, `ArrangementView.Vms.cs`, `ArrangementView.LaneControl.cs` | modified | Lane refresh, mapped grid bar length, warped overview/detail waveform coordinates and cache tolerance. |
| `src/managed/Nota.Application/Editing/SnapGrid.cs`, `PhraseLines.cs` | modified | Fractional quarter-note bar lengths, including 3/8. |
| `tests/Nota.SmokeTest/TempoMapCorrectnessTests.cs`, `Program.cs` | added/modified | End-to-end regressions run in focused tempo mode and the full smoke suite. |
| `tests/Nota.TempoUiTest/Program.cs` | modified | Real lane gestures, 6/8 and 3/8 snapping, and eight rendered waveform cases. |
| `CHANGELOG.md` | modified | Retain both branches' entries without conflict markers or duplicate articulation entries; add the corrections. |

## Tests Added or Updated

- Forward/reversed mapped splits and trims, undo, range slices, paste and duplicate
  destination spans.
- Device-free loop recording entered at beat 0 and beat 5, complete/partial takes,
  occupied range and comp source boundaries.
- Mapped freeze capacity, early/late note onsets, serialization/reload, malformed
  metadata rejection, playback after map changes and legacy frozen PCM compatibility.
- Nonzero range bounce capacity and an ending note beyond the former truncated range.
- BPM-only refresh followed by a lane gesture; fractional bar snapping in 6/8 and 3/8.
- Rendered sine-burst position in smooth/solid waveforms, forward/reversed playback,
  overview and detail zoom, compared with the incorrect linear position.

## Local Verification

- `dotnet run --project tests/Nota.SmokeTest -c Release -- --tempo`: pass.
- `dotnet run --project tests/Nota.TempoUiTest -c Release`: 40 checks pass.
- The same UI checks against PR head 420649a6 fail all eight waveform position cases
  and the four BPM/grid cases. Matched before/after screenshots captured at 1100x600.
- Full Windows build/smoke and Linux performance gate are recorded in `test.md`.
- Documentation/features/shortcuts audit: these fixes restore existing documented
  capabilities and bindings. No new user workflow, label, dependency or shortcut.

## Deviations from Assessment

- Expanded the recording fix into `Engine_Takes.cpp` and `Track.cpp`: mapped loop
  geometry also needs mapped take/comp frame boundaries to avoid corrupting later comps.
- Checked paste/duplicate destination spans because moving the same natural-rate
  recording across tempo segments changes its length in beats.
- Merged develop e191a57b (browser improvements); its changelog changes are retained.

## Follow-ups

- No mandatory extension hooks are registered (`.specify/extensions.yml`: `hooks: {}`).
- Review hosted cross-platform CI after pushing; compare native performance against develop.
