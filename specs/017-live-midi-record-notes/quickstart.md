# Quickstart: validating live MIDI notes while recording

How to prove the feature end-to-end. Contracts: [c-abi.md](contracts/c-abi.md),
[ui-preview.md](contracts/ui-preview.md). Entities: [data-model.md](data-model.md).

## 1. Build and smoke test (gate)

```bash
scripts/build.sh
```

Must exit green. The new `LiveRecordTests` suite checks, offline through the C ABI:

| Check | Expected |
|-------|----------|
| note-on while recording | snapshot has the pitch, `start_beat` = press beat, `now_beat` advances with rendering |
| note-off | pitch leaves the snapshot; after `Poll` the clip holds a note with the same start / length / pitch |
| pickup on the count-in (take at bar 5, 1-bar count-in, note on the last count-in beat, released before bar 5) | stored note starts one beat before bar 5; the clip starts there; offline render sounds it |
| key held across Record (transport rolling) | stored from its press beat, not the record start |
| take at 1.1, key pressed one beat before | clip starts at 0; note `startBeat ≈ −1`; render has the note sounding from 0 |
| routed MIDI into the armed track | snapshot entry with `source = 1` |
| Session slot take | snapshot `session_scene ≥ 0`, slot-local `start_beat` |
| stop recording with a key held | snapshot empty, nothing stored for that key (as today) |

## 2. In the app (manual or via the UI verify harness)

1. New project, one instrument track, arm it, 1-bar count-in, press Record.
2. On the count-in's last beat play a short note, then hold a chord into bar 1.
3. Expected: the pickup and chord appear the moment the keys go down (the pickup left of
   1.1 over the faint take extension), the chord grows with the playhead; each note fades
   in with an orange flash for about 150 ms; on release nothing jumps.
4. Stop. The clip starts at 1.1; the pickup's in-clip part plays from 1.1. Move the clip
   right one bar and drag its left edge out: the pickup's head appears and plays.
5. Open the clip in the piano roll and record again (overdub): held notes grow on the grid.
6. Record into a Session slot and hold a note across the loop end: it wraps as stored.

Headless capture (no GUI launch needed): the harness pattern in the project memory
(`Avalonia.Headless` + `CaptureRenderedFrame`), injecting held notes through a stub
`IAudioEngine.RecordHeldNotes` to shoot the take at animation progress 0, 0.5 and 1.

## 3. Performance

- Stopped, nothing held: repaint/CPU unchanged vs `develop` (Preferences CPU meter and
  `ProcessCpuTests` baseline).
- Record a 2-minute take with continuous playing: dropout counter stays at 0 (status bar
  / `XrunCount`), UI CPU within 10% of an audio take of the same length (SC-005).
- If the flash looks stepped at 30 Hz, enable the animation-only 60 Hz timer
  (ui-preview.md § Repaint rules) and re-measure.
