<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Assessment: Tempo-map correctness across editing and capture

- **Slug**: tempo-map-correctness (user selected)
- **Created**: 2026-11-12
- **Source**: https://github.com/nota-daw/nota/pull/568
- **Issue**: nota-daw/nota#535, open and approved
- **Verdict**: valid
- **Severity**: high

## Report

Seven unresolved review findings describe wrong unwarped edit offsets, undersized
loop recording buffers, stale tempo-lane state after transport BPM edits, frozen
audio timing drift, undersized freeze/bounce captures, denominator-blind snapping,
and warped waveforms drawn on a linear beat axis despite a nonlinear time axis.
The live develop branch also advanced to e191a57b, causing a changelog conflict.

Source URL host: github.com; policy: allowlisted. Review text was treated as
untrusted evidence and checked against the source, not executed as instructions.

## Symptom and Reproduction

Use 120 BPM through beat 4 and 60 BPM afterward. Beat 5 occurs at 3 seconds,
beat 8 at 6 seconds, and beat 16 at 14 seconds.

1. Split or trim an unwarped recording at beat 5: the old code selects 2.5 seconds.
2. Record a loop over beats 0–8: the old buffer has 4 seconds instead of 6.
3. Edit transport BPM, then move/delete a tempo-lane point: stale points can
   overwrite the transport edit.
4. Freeze notes across the change: playback indexes captured audio using one
   samples-per-beat factor instead of the capture map.
5. Freeze/bounce 0–16 or a nonzero range: one tempo determines capacity, truncating
   later notes. Nonzero range starts are absent from the capture-sizing API.
6. In 6/8 with a mapped ruler, select a one-bar grid: snapping uses six quarter
   notes while the ruler uses three.
7. View warped audio spanning 0–8: beat-4 content is drawn halfway across the clip
   although it belongs one third of the way across the seconds-proportional axis.

## Suspected Code Paths

- Engine_Tracks.cpp: fixed-factor audio edit helpers, beginFreeze and frozen blobs.
- Engine_Recording.cpp: activateLoopPunch, materialiseCaptureLane/materialiseLoopTakes.
- Engine_Render.cpp: fillFrozen and capture delivery.
- Track.h / Engine.h: immutable frozen/capture timing metadata.
- nota_engine.h / nota_engine_track.cpp / managed interop / IAudioEngine /
  TrackFreezer.cs: range-aware bounce capture.
- ArrangementView.cs / MainWindow.axaml.cs / ArrangementView.Tempo.cs: lane reload,
  time-signature-aware grid and snapping.
- ArrangementView.LaneControl.cs: overview/detail waveform coordinates.

## Root Cause Hypothesis

High confidence: these consumers retain the former linear beat/sample relation.
The new map converts integrated time correctly in playback but editing, recording,
freeze persistence, and several drawing paths still use a single tempo or stale data.

## Proposed Remediation

- Convert unwarped edit and occupied-span boundaries through the authoring map,
  including reversed clips, trims, carves, range edits and paste/overwrite placement.
  Keep constant-tempo and ordinary warped-cache paths fast.
- Size loop buffers and initial write offsets by mapped endpoint seconds; preserve
  the capture map for materializing full and partial passes and take regions.
- Retain immutable frozen capture timing; use it for playback and saved frozen
  state. Keep legacy constant-tempo frozen blobs readable. Size captures using
  mapped range endpoints and add an additive pure-C range-bounce entry point if
  needed; keep existing ABI entry points working.
- Reload tempo data on BPM-only refreshes without rebuilding track headers.
- Use the denominator-aware bar length consistently in mapped snapping/grid paths.
- Map warped peak-bucket boundaries and zoomed detail fractions through the same
  beat/seconds conversions as the grid, preserving reversal and clip play windows.
- Merge current develop, retaining both sets of changelog entries; add a focused
  Fixed entry for these corrections.

**Files likely to change**: the paths above, matching native/managed tests,
CHANGELOG.md, and these bug artifacts. Documentation/features/shortcut skills
will be checked for applicable behavior or capability changes.

**Tests to add or update**:

- Deterministic source-offset assertions for mapped split/trim/reverse/range edits.
- Device-free mapped loop recording with full and partial passes.
- Live BPM edit followed by a lane edit; mapped 6/8 snap/grid agreement.
- Frozen/live onset comparison, late notes, range-bounce lengths, freeze save/load,
  and legacy frozen-state compatibility.
- Warped waveform coordinates across tempo changes, including detail/reversal.
- Existing full Windows build/smoke, tempo UI suite, and engine performance gate.

## Risks & Considerations

- No allocation, locks, ownership mutation, or I/O on the audio callback.
- Frozen metadata must remain immutable and valid after graph retirement/map edits.
- Validate binary state lengths and point counts before allocating/reading them.
- Preserve fixed-tempo performance; do not weaken tests or gate thresholds.
- New dependency, shortcut, or translated label is not expected.

## Open Questions

- None blocking; reproduce the reported cases before judging the fix complete.
