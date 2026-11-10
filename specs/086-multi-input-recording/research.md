# Research: Multi-Channel Input and Simultaneous Multi-Track Recording

**Feature**: [spec.md](spec.md) | **Date**: 2026-11-08 | **Base**: `release/0.59.2` (b7e4f8a9)

What the code does today, and the decisions this plan takes. Paths are relative to
`src/native/nota.engine/src/` unless they start with `src/`.

## Today's state (findings)

| Area | Today | Where |
|------|-------|-------|
| Capture API | `InputCallback(const float* interleaved, n)` — always **2** floats per frame | `audio_io/AudioInput.h` |
| CoreAudio capture | Already renders **all** device channels into `scratch`, then keeps ch 0/1 | `audio_io/macos/CoreAudioInput.mm` `inputProc` |
| miniaudio capture | `config.capture.channels = 2` — miniaudio downmixes | `audio_io/desktop/MiniaudioInput.cpp` |
| ASIO capture | One input pair (`inL_`/`inR_`, `AudioConfig::asioInputPair`) | `audio_io/asio/AsioSession.*`, `audio_io/windows/AsioInput.*` |
| Capture → engine | Input thread pushes `InputFrame{l,r}` into `inputQueue_` (2^17 frames) and `monitorQueue_` (2^14) | `engine/Engine_Recording.cpp` `ensureAudioInput` |
| Track input pick | `inputChannels_` ∈ {Stereo, Mono1, Mono2}; `foldInputChannels` copies one side over the other | `engine/Track.h:300-311` |
| Arrangement take | First armed audio track + first armed instrument track | `Engine_Recording.cpp` `startArrangementTake` |
| Audio take state | One of everything: `audioRecordTrackId_`, `audioCaptureBuf_` (interleaved stereo), `captureWritePos_`, loop-punch fields, `captureChannels_`, `capturePasses_` | `engine/Engine.h` ~2218-2260 |
| Internal source | One `internalRecordSource_`, mix thread feeds the same `inputQueue_` | `Engine_Render.cpp` |
| Session audio take | One `recordSessionTrackId_` / `recordSessionScene_` / `recordSessionAudio_` | `Engine_Session.cpp`, `Engine_Recording.cpp:958` |
| MIDI take | One `recordTrackId_` / `recordTrackRt_`, per-pitch `pendingActive_[128]` | `Engine_Render.cpp` ~1020-1170 |
| Live MIDI routing | Untargeted live events play on **every** armed track already (`takesUntargeted = t.armed() ‖ audition`) | `Engine_Render.cpp:1287` |
| MIDI source | `MidiInput` callback carries `source` (0..15) for MPE decoders; `MidiEvent` drops it | `audio_io/MidiInput.h`, `engine/EngineTypes.h:23` |
| Mono clips | `SampleBuffer::readStereo` plays `channels == 1` centred; `WriteSampleWav` writes `info.Channels` | `media/SampleBuffer.cpp`, `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` |
| Undo grouping | `beginUndoGroup()` / `endUndoGroup()`, `TakePushScope` marks a take's checkpoints | `engine/Engine.h:1377, 1981` |
| Arm | Exclusive click, ⌘/Ctrl additive (#332); linked groups (specs/076) | `src/managed/Nota.App/TrackArming.cs`, `Nota.Presentation.TrackArm` |
| Persistence | `TrackModel.InputChannels int?` (0/1/2), `RecordInput int` | `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs:129-133` |

## Decisions

### R1 — The capture API carries every device channel

- **Decision**: `InputCallback(const float* interleaved, int32_t numFrames, int32_t numChannels)`;
  `AudioInput` gains `channelCount()` and `channelName(i)` (control thread). CoreAudio drops
  its stereo downmix and hands `scratch` as is (names: `kAudioObjectPropertyElementName` per
  channel on the input scope). miniaudio asks for the device's native channel count
  (`capture.channels = 0`, read back from `device.capture.channels`; names: "Input N").
  ASIO creates buffers for every input channel (`ASIOChannelInfo.name` for names);
  `asioInputPair` stops steering capture and stays only as a saved value for old configs.
- **Cap**: `kMaxInputChannels = 64` (SC-002). Devices with more are clipped to 64 and the
  rest are not offered.
- **Rationale**: the narrowest change per backend; CoreAudio already does the work.
- **Alternatives**: a second "multi" input class next to the stereo one — rejected, two
  capture paths to keep alive.

### R2 — One wide capture ring carrying only the channels in use

- **Decision** (as built, PR 1): the input thread copies the **used channel set** — the union
  of channels picked by audio tracks that are armed or monitor the hardware input, plus the
  hardware take in progress — into two SPSC rings (`engine/CaptureRing.h`): the record ring
  (input → message thread, 2^21 floats) and the monitor ring (input → audio thread, 2^18).
  Each push is a **block** with a 4-float header carrying the layout generation and width, so
  a consumer always knows which layout a block's columns follow.
- The layout (`CaptureLayout`) is published by the message thread through a **seqlock** of
  atomics: the input and audio threads copy it without locks or allocation and keep their
  previous copy if they catch a write in progress. No retire lists are needed.
- One layout serves both rings. It may change at any time (arming, monitoring, a re-pick);
  the drain looks older blocks up in a 16-entry history (`layoutFor(generation)`), and a
  block too old to look up keeps its length as silence. The monitor reader adopts a new
  layout at a segment boundary (a short gap) and tracks the generation of the block it is
  in, also after trimming a backlog mid-block.
- Internal resampling has its own stereo ring (`internalQueue_`, producer: audio thread);
  capture markers (specs/078) are a marker block in the record ring and a NaN frame in the
  internal one.
- **Rationale**: the input thread stays trivial and RT-safe (copy + push); per-track splitting
  happens on the message thread where the take buffers live.
- **Alternatives**: a record layout frozen for the take (first draft) — rejected: the history
  lookup is simpler and lets monitoring change mid-take; one ring per track — rejected: N
  producers' worth of bookkeeping on the input thread.

### R3 — A take is a set of lanes

- **Decision**: replace the single-take fields with `std::vector<AudioTakeLane>` (message
  thread) where a lane holds `trackId`, its pick (`first`, `width`), its source (hardware or
  internal track), its buffer (1 or 2 channels), write cursor, loop passes, and — for
  Session — its slot and its own `stopped` flag. Take-wide fields stay single: start beat,
  sample rate, latency correction, gate/epoch, loop geometry, retry markers.
- `drainInputQueue` pops wide frames once and scatters each column into its lanes.
- `stopAudioRecording` materialises every lane inside **one** `beginUndoGroup` /
  `endUndoGroup` under the take's `TakePushScope`, so Undo and Discard and Retry
  (specs/078) treat the take as one unit.
- **Rationale**: keeps the drain/loop-punch/retry code paths (well tested) and makes them
  per lane rather than duplicating them.

### R4 — Internal-source lanes

- **Decision**: the mix thread's tap generalises from one `internalRecordSource_` to a fixed
  array (`kMaxInternalLanes = 16`) of source track ids, writing into a second wide ring
  (producer: audio thread). The message thread drains both rings into their lanes.
- **Rationale**: two producers can't share one SPSC ring; internal sources are rare, so 16 is
  plenty.

### R5 — Mono lanes record mono

- **Decision**: a lane with `width == 1` stores a 1-channel `SampleBuffer`; playback already
  centres it (`readStereo`), save already writes the buffer's channel count. Monitoring a
  mono pick plays it centred (same as today's fold).
- **Check in tasks**: waveform peaks, warp stream, clip editor, export and "Slice to…"
  paths with a 1-channel recorded buffer (imports already produce 1-channel buffers, so
  these paths are expected to work; tasks verify rather than assume).

### R6 — Latency and alignment

- **Decision**: one device → one round-trip correction (`audioInputLatencyFrames() +
  audioOutputLatencyFrames()`) applied to every hardware lane; internal lanes get 0 as
  today. All lanes share the take's first captured frame, so they are sample-aligned by
  construction (SC-001).

### R7 — Session: several slots at once, stopped one by one

- **Decision**: Session audio state moves into the lane (`scene`, `sessionTrackId`).
  Launching an empty slot on an armed track adds a lane to the running take (or starts one)
  at the slot's launch boundary — the lane records from the boundary frame noted during the
  drain (the mechanism specs/078 already uses for `retryKeepFrom_`). Stopping a slot closes
  only its lane at the stop boundary and materialises it; other lanes keep running. Arming a
  track adds its channels to the layout at once (R2), so a slot launched on a track armed
  after the take began records from its boundary like any other.
- MIDI Session slots follow the same per-lane model (R8).

### R8 — MIDI: per-track input filter and per-track MIDI take

- **Decision**:
  - `MidiEvent` gains `source` (int8, `-1` = computer keyboard / gamepad / Remote / UI) and
    `channel` (int8). Hardware sources are identified to the UI by uid; the engine maps
    uid → source index on (re)open.
  - `Track` gains `midiInputSource` (`-2` = All inputs, else a source index; the uid is kept
    managed-side and in the project) and `midiInputChannel` (0 = all, 1–16).
  - Untargeted events reach an armed track only when its filter matches. Non-hardware
    sources (`-1`) match "All inputs" only — the computer keyboard is part of All.
  - MIDI recording becomes per track: `recordTrackRt_` → a fixed array of up to
    `kMaxMidiTakeLanes = 16` lanes with their own `pendingActive_[128]` / `recRoutedActive_`
    state, preallocated. Overdub-into-clip and loop-record-replace apply per lane.
  - MPE: decoders stay per source; a track filtered to one channel of an MPE source shows a
    warning in the menu (spec edge case).
- **Rationale**: playback already fans out to every armed track; only the filter and the
  per-track capture state are new.

### R9 — Project format

- **Decision** (project schema stays backward-readable):
  - `TrackModel.InputFirst int?` (1-based) + `InputWidth int?` (1|2). Missing → derived from
    the old `InputChannels`: 1 → (1,1), 2 → (2,1), 0/null → (1,2). Saved files keep writing
    `InputChannels` when the pick is expressible in it, so 0.59.x can still open them.
  - `TrackModel.MidiInputUid string?` (null = All inputs) + `MidiInputChannel int?`
    (null/0 = All).
- **ABI** (additive, AR-7): `nota_track_set_input(engine, track, first0, width)` /
  `nota_track_get_input(...)`, `nota_audio_input_channel_count/name`,
  `nota_track_set_midi_input(engine, track, source, channel)` / getter,
  `nota_midi_input_source_of_uid`. `nota_track_set_input_channels` stays and maps
  0/1/2 onto the new pick. See [contracts/c-abi.md](contracts/c-abi.md).

### R10 — Audio I/O page storage

- **Decision**: per-device input setup (on/off, names, pair names) lives in the managed app's
  preferences, keyed by `(audio system, device uid)`; the engine only reports channel count
  and device names. Menus read the setup; nothing of it reaches the audio thread.
- **Rationale**: it is presentation data; machine-level, not project-level (spec
  assumption).

### R11 — Memory of long multi-track takes

- **Finding**: the take lives in RAM until Stop (`audioCaptureBuf_` grows on the message
  thread). Mono float at 48 kHz ≈ 11.5 MB/min per track: 8 tracks × 10 min ≈ 0.9 GB; 32
  tracks × 10 min ≈ 3.7 GB.
- **Decision**: keep in-memory capture for this feature (mono lanes halve today's cost);
  pre-reserve each lane's buffer in 30 s chunks to avoid repeated large reallocations on
  the message thread. Streaming takes to disk while recording is a follow-up issue, named
  in the PR.
- **Alternatives**: stream to disk now — rejected for scope: it touches save, undo and the
  sample model.

### R12 — Slicing into pull requests (constitution V)

1. **Capture layer** (R1, R2 without UI): N-channel `AudioInput` on all backends, wide
   rings, track pick `(first, width)` + ABI + migration, MCP. Behaviour unchanged for
   existing picks.
2. **Multi-lane audio takes** (R3–R6): Arrangement multi-track audio, mono files, one undo
   step, retry. Fixes #528 for audio.
3. **Track input menu + Audio I/O page** (UI, R10).
4. **Session multi-slot** (R7).
5. **MIDI input filter + multi-track MIDI** (R8). Fixes #528 for instrument groups.

Each slice builds and passes the smoke test on its own; 1 is the base for the rest; 3, 4,
5 depend on 2 only (3 also on 1).
