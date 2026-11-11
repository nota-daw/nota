# Research: Private Cue Mix on a Separate Hardware Output Pair

**Feature**: [spec.md](spec.md) | **Date**: 2026-11-10 | **Base**: `develop` (7a4f0af6)

What the code does today and the decisions this plan takes. Paths are relative to
`src/native/nota.engine/src/` unless they start with `src/` or `tests/`.

## Today's state (findings)

| Area | Today | Where |
|------|-------|-------|
| Render contract | `RenderCallback(float* out, int32_t n)` — interleaved **stereo** | `audio_io/AudioBackend.h:19-22` |
| CoreAudio output | AUHAL client format `mChannelsPerFrame = 2`, no channel map → device outs 1/2 | `audio_io/macos/CoreAudioBackend.mm:158-176` |
| CoreAudio events | Only an overload listener; no device-alive listener | `CoreAudioBackend.mm:137` |
| miniaudio output | `playback.channels = 2` (miniaudio maps to the device) | `audio_io/desktop/MiniaudioBackend.cpp:54` |
| ASIO output | Buffers for **every** output already; `outL_/outR_` from `asioOutputPair`, others held at zero | `audio_io/asio/AsioSession.cpp:135-170, 312-360` |
| Engine entry | `Engine::render(out, n)` → `processBlock(out, n)`; segments of ≤ `kMaxBlock` (16384) | `engine/Engine_Render.cpp:940, 2203, 2283` |
| Mix order | mixGraph → metronome/count-in (per segment) → tone → preview → master devices → master volume, NaN squash, clamp, meters | `Engine_Render.cpp:2300-2395` |
| Leaf tap | Post-device, pre-fader buffer is `post` (freeze capture already reads it); fader/pan into `mixTmp_` | `Engine_Render.cpp:2957-3012` |
| Group / return tap | `scratch_` after devices + PDC, before fader | `Engine_Render.cpp:3139, 3204` |
| Muted / solo-gated leaf | **Not rendered at all** (`continue`) unless capturing, monitoring or keying a sidechain | `Engine_Render.cpp:2718, 2752-2771` |
| Muted group | Skipped | `Engine_Render.cpp:3118` |
| Parallel leaves | Leaves render in parallel into `leafBuf`; accumulation into buses is serial | `Engine_Render.cpp:2949-2968` |
| Track flags | Relaxed atomics on `Track` shared by authoring and published graphs; `cloneTrack` copies flags | `engine/Track.h:214-221, 441`, `engine/Engine.cpp:685-695` |
| Click | `renderClick` adds a 1.5/1 kHz sine × `kMetroGain = 0.5` into `out` | `Engine_Render.cpp:30, 3275-3330` |
| Master meter | `masterPeak/Rms` atomics → `nota_engine_master_meter(NotaMeter*)` | `Engine.h:1917`, `nota_engine.h:2555` |
| Test render | `nota_engine_render_device_block(out, frames)` | `nota_engine.h:2573` |
| MIDI Learn | `MidiTargetKind` (TrackMute, TrackSolo, MasterVolume, …); `midimap.json` sidecar; tracks by index | `src/managed/Nota.App/MidiLearn.cs:75-110`, `MidiLearnService.cs` |
| Per-device settings | `Settings.InputSetups[KeyOf(system, uid, asioDriver)]` | `src/managed/Nota.Application/InputSetups.cs:36`, `Settings.cs:206` |
| Undo of mute/solo | Not undoable (no `MixKind`); volume/pan are via `MixGesture` | `engine/EngineTypes.h:108-121` |
| Project format | v31; `TrackDto.Mute/Solo/Monitor`; `MixerDto{PanLaw}` | `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs:81, 119-156` |

## Decisions

### R1 — Engine renders two stereo buses; the backend places them on device channels

- **Decision**: `RenderCallback(float* main, float* cue, int32_t n)`. Both are interleaved
  stereo scratch buffers the backend preallocates (max block × 2). The engine always fills
  `main`; it fills `cue` with the Cue bus (or zeros). The backend then writes `main` to the
  Main pair, `cue` to the Cue pair, and zero to every other open output.
- The scatter is one shared, header-only helper, `audio_io/OutputRouting.h`:
  `scatterOutputs(const float* main, const float* cue, float* deviceInterleaved, int32_t
  deviceChannels, int32_t n, const OutputRouting&)`, used by CoreAudio and miniaudio
  (interleaved device buffers); ASIO reuses its per-channel `fromFloat` with the same
  routing. A width-1 pick (odd last channel) gets the L+R sum × 0.5, as ASIO does today.
- **Rationale**: the engine stays device-agnostic and offline export is unchanged (it simply
  passes `cue = nullptr`). One routing rule for three backends.
- **Alternatives**: engine renders straight into the N-channel device buffer — rejected,
  leaks device layout into `processBlock` and every segment offset; a second backend stream
  for Cue — rejected, two clocks and a drift problem against FR-010.

### R2 — Backends open every output channel of the device (≤ 64)

- **CoreAudio**: read the output channel count from `kAudioDevicePropertyStreamConfiguration`
  (output scope) and set the AUHAL client format to that many channels (capped at
  `kMaxOutputChannels = 64`); names from `kAudioObjectPropertyElementName` per element on
  the output scope, else "Out N". A stereo device stays at 2 — today's behaviour.
- **miniaudio**: `playback.channels = 0` (native), read back `device.playback.channels`;
  names "Out N". WASAPI shared mode may report only the mix format's channel count —
  shown as is (risk noted in plan).
- **ASIO**: already creates buffers for all outputs; add `cueL/cueR` next to `outL/outR`,
  names from `ASIOChannelInfo.name`. `asioOutputPair` becomes the Main pair's saved value
  (FR-005), migrated into the per-device output setup on first run.
- **Device loss on macOS**: add a `kAudioDevicePropertyDeviceIsAlive` listener that raises
  the existing "reset requested" device event (1), so the app's poll re-applies and the
  routing is re-evaluated against the new channel count (User Story 4).

### R3 — Routing is hot, not a device restart

- **Decision**: `OutputRouting{mainFirst, mainWidth, cueFirst, cueWidth}` is published to the
  backend as four relaxed atomics packed in one `std::atomic<uint64_t>` and read once per
  callback. Changing Main or Cue never restarts the device (no glitch on stage).
- **Resolution against the device** (in the backend, per callback, pure integer math):
  Main pair missing → Main = 1/2 (FR-015). Cue pair missing, or overlapping the resolved
  Main → Cue not written (silent) (FR-014). Width 2 on the last odd channel → width 1.
- **State back to the app**: `nota_audio_output_routing_state()` returns flags
  (main fell back / cue unavailable), computed on the control thread from the same rules.

### R4 — The Cue bus in the engine

- **Buffer**: no member buffer — `processBlock(main, cue, n)` sums straight into the caller's
  stereo Cue buffer (the backend's preallocated scratch). Per segment `cueSeg_ = cue + done*2`;
  `cue` is null offline, in export and in plain device-block tests — then nothing below runs.
- **Leaf tap**: in the **serial** accumulate step (`Engine_Render.cpp` ~2977-3012) after
  `processLeafDevices`, if `t.cue()` add `post` into `cueSeg_`. Doing it in the
  serial part avoids a race with the parallel leaf workers.
- **Muted / solo-gated leaves**: the skip at 2752-2771 gains one more exemption —
  `t.cue()` — and such a track then contributes **only** to Cue (`w.toMix = false` keeps it
  out of Main, sends and groups). Same for a muted group (3118): rendered for its Cue tap
  only. This is what makes "mute in Main, hear in Cue" work (Clarification 1).
- **Group / return tap**: add `scratch_` into `cueSeg_` at 3139 / 3204 when the bus is cued.
- **Frozen tracks**: `post` is the frozen buffer — the tap is the same.
- **Cue level, safety, meter**: after the master section (`finishCue`), `cue × cueGain` → NaN squash →
  clamp ±1 → `cue` out; peak/RMS into `cuePeak/Rms` atomics. Cue never passes through
  master devices, master volume, `renderTone` or `renderPreview`.
- **Latency (FR-010)**: Main and Cue come from the same block, so they leave on the same
  sample; per-track PDC still applies before the tap. A master device with latency delays
  Main only — documented, not compensated (adding delay to Cue would make the performer
  hear their own monitored input later; the performer prefers the lower latency).
- **RT safety**: one `bool cue_` atomic per Track (copied by `cloneTrack` and `deepCloneTrack`);
  `cueGain_`, `clickDest_`, `clickGain_` atomics on Engine; no allocation, no locks, no commands
  needed (same pattern as mute).
- **Groups**: a cued group's own mute doesn't silence its children (`grpFeedMute`); they feed
  its Cue tap while the group stays out of its parent, and their sends, sidechain and record
  taps stay off. Children still follow solo, so a group's Cue is what reaches its bus.

### R5 — Click routing

- `renderMetronome` / `renderCountIn` / `renderClick` take `(float* main, float* cue, …)`
  and a gain: `kMetroGain × clickGain_`. Destination Main writes `main` as today
  (pre-master: master devices and volume still apply — unchanged behaviour); Cue writes
  `cueSeg_` (then Cue level applies); Main + Cue writes both.
- When the Cue output is unavailable (R3) the click aimed at Cue is still rendered into the
  Cue bus and simply not written to any output — never redirected to Main (FR-014).
- Export already turns the metronome off; Cue never reaches export (no `cue` buffer there).

### R6 — Settings and persistence

- **Machine, per device** (FR-018): `Settings.OutputSetups[key]` where `key` reuses
  `InputSetups.KeyOf` with the **output** device uid (`device:{uid}` / `asio:{driver}`).
  `OutputSetup{MainFirst=0, MainWidth=2, CueFirst=-1, CueWidth=2, Click=Main, ClickDb=0}`.
  Pushed to the engine on start, device change and every edit.
- **Project** (FR-019): format **v32** — `TrackDto.Cue bool?` (null = false) and
  `MixerDto.CueVolume float?` (null = 1.0). Older builds ignore the fields (extension data
  is preserved; they just play without Cue).
- **MIDI Learn** (FR-021): new `MidiTargetKind.TrackCue` (toggle, track-scoped like
  TrackMute) and `CueVolume` (continuous, like MasterVolume). Saved in `midimap.json`.
- **Undo**: Cue toggles follow mute/solo — not undoable. The Cue level is a listening
  level, not part of the mix, so it is not an undo step either (same reasoning as the
  rejected `MixKind::TrackSend`, `engine/EngineTypes.h:116`); it is still saved.

### R7 — Testing

- New test entry `nota_engine_render_device_block_cue(engine, main, cue, frames)` renders
  both buses without a device; `nota_audio_scatter_test(...)` drives `scatterOutputs` with a
  given routing and channel count (pure function, also covers missing-pair fallback).
- Smoke cases: cue tap pre-fader/pre-mute; Main null-test unchanged by Cue level (SC-001);
  click → Cue absent from Main (SC-002); same-sample alignment (SC-003); scatter fallback
  (SC-004); project round-trip v32 + v31 load (SC-006).
