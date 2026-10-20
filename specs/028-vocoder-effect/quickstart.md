# Quickstart: verify Nota Vocoder

How to prove the feature end-to-end. Contracts: [device-params.md](contracts/device-params.md),
[mcp.md](contracts/mcp.md).

## Prerequisites

- Release engine build in `build/` (see the engine build dirs note in BUILD.md — a −O0
  engine causes xruns and invalidates SC-003 / SC-004).
- A speech or sung WAV clip (deterministic; synth notes drift between renders) and a
  sustained chord clip.

## 1. Build and smoke test (gate)

```bash
scripts/build.sh
```

Expect the Vocoder block of the smoke test to pass: kind 26, name, 15 params, defaults,
round-trip, clone, the four render checks and the determinism check (contract § Behaviour
guarantees).

## 2. The P1 flow in the app

1. New project. Track 1: audio, the speech clip.
2. Browser → Audio effects → Modulation → **Nota Vocoder** onto track 1. Play.
3. Expect: a robot voice at C3 (Saw) at about the speech's level, following the syllables;
   the key meter and the graph's bars move with it. **Pitch** changes the note without
   clicks; **Noise** gives a whisper (Tilt and Pitch inactive).

## 3. A pad that talks (Carrier = Track)

1. Track 2: audio, the chord clip.
2. On track 1's vocoder pick **Track 2** in the carrier picker (Carrier switches to Track),
   tap **Pre**. Mute track 2.
3. Expect: the chord speaks the words at about the chord's level; muting track 2 keeps it
   working; **None** in the picker → track 1 heard unchanged. The picker does not list
   track 1 or groups containing it.

## 4. Shaping (US3) and click-free edits (SC-003)

- At a 128-sample buffer, sweep every knob and drag the Low / High edges in the graph
  during playback: no clicks, no xruns (check the CPU / xrun indicator).
- Bands 8 → 32: graph shows 32 bars; Formant ±12 st changes size, not pitch; Unvoiced
  makes "s" / "t" audible; Hold freezes the bars.

## 5. Recall (US4, SC-005)

- Save, close, reopen: same sound, same modulator and tap. Export twice → null test
  residual ≤ −90 dBFS.
- Delete track 2 → picker shows None; undo → Track 2 again.
- Automate Formant → audible sweep. Load each factory preset → distinct, peaks < 0 dBFS.

## 6. Design (FR-012, SC-007)

- Visual check of the card per the `nota-ui-verify` recipe, in Studio Dark, Studio Light,
  Ember Graphite, Ember Paper; compare with `design/vocoder/index.html`.
- `RunStudio`, `RunThemes`, `RunLabels`, `RunIcons` and `--palette` pass in the smoke test.

## 7. MCP

- `set_device_sidechain(track1, idx, track2, tapPre: true)` → `get_device_sidechain`
  returns `Accepts: true, SourceTrackId: track2`.
