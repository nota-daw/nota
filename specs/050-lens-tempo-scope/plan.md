# Implementation Plan: Tempo-synced scope in Nota Lens

**Branch**: `feat/lens-tempo-scope` | **Spec**: [spec.md](spec.md) | **Issue**: nota-daw/nota#119

## Summary

Lens learns the transport (it already gets `setTransport` / `setTransportInfo` like every
insert), accepts a sidechain key, and keeps a min/max history of the analysis channel for the
main signal and the key, each bin stamped with its beat. The message-thread analysis turns that
history into a synced frame (this pass and the previous one on the beat grid) or, with Sync
off, adds a key trace on the existing trigger. The card draws both. A pop-out window hosts the
same body at any size.

## Engine (`devices/effects/Lens.h/.cpp`)

- **Params** (appended, normalized 0..1 like the rest):
  - 37 Sync (≥ .5 on, default 0)
  - 38 Span: 7 steps, 1/4 beat … 4 bars, default 1 bar (step 4)
  - 39 Key (≥ .5 show, default 1)
  - 40 Key Gain: −24..+24 dB, default 0 (.5)
  - 41 Layout: 0 Overlay, 1 Stacked
- **Transport**:
  - `setTransport(beatStart, spb, playing)` keeps the block's start beat, samples per beat and
    playing flag.
  - `setTransportInfo(ti)` keeps the beats per bar (`tsNum × 4 / tsDenom`).
  - Both are audio thread only, read in `process()`.
- **Key**:
  - `acceptsSidechain()` is true.
  - `setSidechain(ptr, frames)` keeps the pointer for the next `process()`.
  - The key track id is stored like the Compressor's.
  - `process()` writes the key's L/R into two more raw rings (`ringKL_`, `ringKR_`, same size and
    head as the main rings), so the triggered key trace lines up sample for sample. No key:
    zeros, and `keyLive_` false.
  - The engine's sidechain taps hand over the source's previous block, so with a key routed the
    capture (rings + history) runs one block behind: `process()` holds the block and captures it
    with the next call's key. Without a key nothing is held.
- **History**:
  - `binLen_ = max(16, round(sr / 1500))` samples per bin.
  - `kBins = 65536` bins (≈ 43 s at any rate).
  - Per bin: main min/max and key min/max of the analysis channel (Source / Mid-Side), its
    start beat (double), and a flags byte (playing, key present).
  - Fixed arrays allocated in `setSampleRate`. The audio thread writes and publishes the head
    with release; readers take acquire and read under `mx_`. Freeze stops writing, as for the
    rings.
- **Synced frame** (`analyzeSync()`, under `mx_` in `ensureAnalyzed`):
  - **Inputs**: the newest bin's beat B, span S (beats, the bar from beats per bar), window start
    W0 = floor(B / S) × S, kSync = 1024 columns.
  - **Walk**: back from the newest bin while bins are playing and each is one bin's worth of
    beats before the next (a seek either way, a loop wrap or a stop ends the walk, so the pass
    restarts).
  - **Placement**: a bin with beat ≥ W0 goes into this pass's column. One with W0 − S ≤ beat < W0
    goes into the previous pass's column, but only right of the playhead, and marked "previous".
  - **Free mode**: the newest bin not playing means a rolling window of S beats at the current
    tempo by bin count, newest at the right.
  - **Output**: header [mode, playhead fraction, bpm, beats per bar, span beats, W0, key present,
    0] + per column (main min, main max, key min, key max, age 0 = this pass / 1 = previous /
    NaN = empty) → `layerWave(L_Sync)`.
- **Key trace (Sync off)**: `analyzeScope` builds `keyTrace_` from the key rings over the same
  window as the main trace → `layerWave(L_KeyTrace)`.
- **Telemetry**: `M_KeyLive` (1 = a key arrived in the last blocks) appended to `scopeRead`,
  `kScope` 21. The MCP text (id 2) adds sync, span and key.
- **Audio**: untouched; the new work is a min/max per sample and two more ring stores.

## UI

- **`LensScopeView`**:
  - **Sync mode**: draws the synced frame. Beat lines (bar lines stronger) and the sweep line.
    Previous-pass columns at 45 % alpha. The main as a min/max band in the graph's ink, the key
    as teal (× key gain). Overlay or Stacked.
  - **Sync off**: draws the key trace over the main trace (teal) when Key is on and a key is
    live.
- **`LensDeviceBody`**:
  - Scope Analyze tab: **Sync** switch, **Span** segments, a **KEY** row (key source picker like
    Prism's, Key switch, Key gain, Layout).
  - An expand button in the centre tab bar opens `LensWindow`.
- **`LensWindow`**: a resizable window that builds the same body with its own refresher list on a
  60 Hz timer, in a viewbox-free layout where the graph takes the extra space. Closed when the
  device goes (the device chain closes open Lens windows on rebuild if the device is gone).

## Testing (`--lens-sync`)

- Click every beat at 120 BPM, Sync 1 bar: peaks within 1 % of the beat lines.
- A single-sample spike keeps full height at 4 bars.
- Stop → free mode. Seek → the pass restarts (no columns from before the seek).
- Key routed from a click track: key columns follow it; key gain doesn't change the main.
- Sync off + key: the key trace lines up with the main for the same signal.
- Output bit-identical with and without Sync/key; older projects get the defaults.
