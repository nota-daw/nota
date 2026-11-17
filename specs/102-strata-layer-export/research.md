# Research: Export one Strata layer on its own

## R1 — What a layer buffer holds

- **Decision**: Export the layer buffer as stored.
- **Rationale**: `Strata::process` writes `buf * inGain` into `buf_[recL]` at the
  integer loop position (`pos_` advances by exactly 1 per sample while recording,
  regardless of Speed/Reverse), so index 0 is the loop's top and the samples are the
  input after Input gain. Feedback, layer gain and mute are folded in only when the
  layers are summed for playback (`eg[k]`, `muted_[k]`); Speed/Reverse only move the
  read position. A layer stopped part-way keeps zeros elsewhere (`clearLayer` before
  stacking).
- **Alternatives considered**: Render the layer as it plays (speed/reverse/feedback/
  level) — rejected: the result no longer lines up sample for sample with the loop and
  the whole-loop Export already offers "as you hear it". Raw pre-gain input — not kept
  by the device at all.

## R2 — Where to read it from

- **Decision**: `IAudioEngine.DeviceGetState(track, di)` (the persistence blob).
- **Rationale**: Already used by the whole-loop export and by project save, so it is
  exactly what survives save/reopen (FR-006); it holds the per-layer `state` (2 =
  recording), gain, mute and the PCM. During the first pass `loopLen` is 0, so the blob
  carries no audio — the export reports "loop not closed".
- **Alternatives considered**: A new C ABI call that copies one layer — rejected: an
  engine/ABI change for no gain; `DeviceLayerWave` gives only a peak envelope.

## R3 — Availability in the menu

- **Decision**: Decide from the live scope when the menu opens (first pass still
  recording → "loop hasn't closed"; `recLayer == k` → "still recording"), and decide
  again from the blob after the file is chosen.
- **Rationale**: The scope is already read every refresh; the second check covers an
  Undo/Clear/remove while the dialog is open (FR-008).

## R4 — Following the device across the dialog

- **Decision**: Remember the Strata's instance id (scope tail, `S_Id`) and look it up
  on the track again after the picker, as `WatchTempo` does.
- **Rationale**: Devices may be moved/added while the dialog is open; the index alone
  could then point at another device.

## R5 — File format

- **Decision**: 32-bit float stereo WAV at the stored sample rate, via `WavWriter`.
- **Rationale**: Matches the loop export; keeps the recorded samples bit-exact (SC-001).
