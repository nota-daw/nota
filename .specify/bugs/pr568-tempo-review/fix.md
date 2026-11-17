# Bug Fix: PR #568 tempo-map review findings

- **Slug**: pr568-tempo-review
- **Fixed**: 2026-11-11
- **Assessment**: ./assessment.md
- **Issue**: nota-daw/nota#535
- **Status**: applied

## Summary

All eight Greptile findings were valid and have been addressed. Mapped warp caches now
include clip placement in their identity and use the existing deferred worker. Rendering
keeps elapsed DSP time separate from the musical clock, and presentation history is
re-anchored in sample time across mapped loop seams. The first Time Scale gesture freezes
its original coordinate system. Project loading restores the BPM readout without editing
the loaded map, and every tempo entry path now shares the engine's 999 BPM upper bound.
Arrangement navigation, overview gestures, and audio-resize geometry now use the
authoritative nonlinear axis helpers.

## Changes

| Area | Change |
|------|--------|
| Mapped audio | Added `tempoWarpStartBeat`; moving a clip invalidates its mapped cache. |
| Cache building | Mapped plans are rendered by the sliced/background warp builders and installed only if their map, placement, and inputs still match. |
| DSP clocks | `mixGraph` receives musical and elapsed sample positions separately; unsynchronised modulation uses elapsed time. |
| Loops | Presentation re-anchoring now operates in sample space for mapped and unmapped playback. |
| Time Scale gesture | Captures origin seconds and seconds-per-pixel at pointer down, including the legacy beat-axis case. |
| Project load | `RestoreBpm` updates the transport field without sending `SetBpm`. |
| Bounds | Tempo maps, transport commands, and the editor consistently accept 20–999 BPM. |
| Arrangement geometry | Reveal/center, overview pan/zoom, and audio resize use `BeatToX`, `XToBeat`, and `ScrollBeatForX`. |

## Tests Added or Updated

- Tempo UI: first Time Scale drag on the legacy 60 BPM axis uses the correct seconds scale.
- Tempo engine: 999 BPM is accepted and 1000 BPM is rejected.
- Tempo follow: restoring a saved BPM readout does not mutate an already loaded tempo map.
- Existing tempo UI checks continue to cover stepped rendering at zoom/scroll, mapping,
  snapping, cancellation, selection, curves, collapse, and resize.

## Deviations from Assessment

- None.
