# Quickstart: validating tempo as master automation

## Prerequisites

- macOS build: `scripts/build.sh` (Release engine in `build/`, see BUILD.md).
- A #568-format project for migration: create it on `develop` at `45e7281b` (before this
  feature) with one stepped, one linear and one smooth segment, plus a point at 400 BPM in a
  second copy; keep both under `tests/Nota.SmokeTest/fixtures/tempo-v33/`.

## Automated

```bash
scripts/build.sh
```

Must pass, including:

- `nota_engine_tempo_lane_selftest` = 0 (lane→map derivation, jumps, first point after 0,
  clamp, section move, undo/redo, unwarped clip reprojection).
- Smoke `--tempo`: the #568 correctness/rendering checks re-pointed at lane-driven maps;
  drawn vs. rendered vs. exported timing within one sample at 48 kHz (SC-004).
- Smoke migration: the v33 fixtures open with every bar start of the first 200 bars within
  1 ms of the old map (SC-002); the 400 BPM fixture opens clamped with `tempoClamped` set.
- Smoke master automation (PR 2): master pan and a master EQ band gain lane render and export
  as drawn; deleting the EQ removes its lane; undo restores both (SC-007).
- Linux performance gate: no regression on fixed-tempo scenarios (SC-006).

## Manual / headless UI

1. New project → the first track sits directly under the ruler/sections; no tempo row (SC-001).
2. Master row → target picker → Tempo: flat line at the project tempo, BPM labels, range
   fields show tempo ±40.
3. Draw 120 at bar 1, 90 at bar 9 with a curve, a 140 jump at bar 13. Play: bars widen
   through the ritardando, the metronome follows; the transport field is read-only and shows
   the tempo under the playhead.
4. Drag a point while playing: timing changes without a transport restart.
5. Ctrl-drag over the timeline: nothing tempo-related happens.
6. Delete all Tempo points: the transport field is editable again.
7. Open both v33 fixtures: tempo is on the master Tempo lane; the 400 BPM one shows the
   clamp notice once.
8. MCP: `get_overview` → `masterTrackId`; `set_automation_points` on its tempo lane changes
   playback; `set_tempo` errors while automated.

Screenshots for the PR come from the headless Avalonia harness (memory: Nota UI verify
recipe): before (develop, #568 tempo row) / after (master Tempo lane).
