# Data Model: Multi-Channel Input and Simultaneous Multi-Track Recording

**Spec**: [spec.md](spec.md) | **Research**: [research.md](research.md)

## Engine (C++, `src/native/nota.engine/src/`)

### InputDevice channels (control thread)

| Field | Type | Notes |
|-------|------|-------|
| `channelCount` | int32 | 0 when no device is open; clipped to `kMaxInputChannels = 64` |
| `channelName(i)` | string | device name or "" (UI falls back to "Input N") |

Read from `AudioInput` after `start`; refreshed on device change. Never read on the audio
or input thread.

### Track input pick (`Track`, atomics as today)

| Field | Type | Rule |
|-------|------|------|
| `inputFirst_` | int32, 0-based | `0 ≤ first < kMaxInputChannels` |
| `inputWidth_` | int32 | 1 (mono) or 2 (stereo pair); width 2 ⇒ `first` is even |
| `recordInputSource_` | int32 | unchanged: 0 = hardware, > 0 = another track |
| `midiInputSource_` | int32 | `-2` All inputs (default), ≥ 0 a hardware source index |
| `midiInputChannel_` | int32 | 0 = all, 1–16 |

Available ⇔ `first + width ≤ channelCount`. An unavailable pick is kept, the track records
nothing (FR-011). Replaces `inputChannels_` (`kInputStereo/Mono1/Mono2`); the old setter maps
0 → (0,2), 1 → (0,1), 2 → (1,1).

### CaptureLayout (seqlock, `engine/CaptureRing.h`)

| Field | Type | Notes |
|-------|------|-------|
| `count` | int32 | number of device channels carried in the rings, ≤ 64 |
| `channel[64]` | int8 | device channel per ring column |
| `columnOf[64]` | int8 | device channel → column, −1 = not carried |
| `generation` | uint32 | bumps on every publish; every ring block carries it |

One layout for both rings; readers copy it (seqlock) and keep their copy on a torn read.
The message thread keeps the last 16 in a history for the drain.

### AudioTakeLane (message thread)

| Field | Type | Notes |
|-------|------|-------|
| `trackId` | int32 | |
| `column` / `width` | int32 | ring column of the pick's first channel; 1 or 2 |
| `internalSource` | int32 | 0 = hardware, else source track (internal ring column) |
| `buf` | vector<float> | `width` channels interleaved; reserved in 30 s chunks |
| `writePos` | size_t | frame cursor |
| `passes` | vector<LoopPass> | loop-punch passes (specs/067), per lane |
| `scene` | int32 | Session slot, −1 for Arrangement |
| `keepFrom` | int64 | first frame to keep (Session launch boundary / retry) |
| `stopAt` | int64 | −1 running; else the frame its slot stopped at |

### AudioTake (message thread; replaces the single-take fields)

| Field | Notes |
|-------|-------|
| `lanes` | vector<AudioTakeLane>, ≤ 64 hardware + 16 internal |
| `startBeat`, `sampleRate`, `latencyFrames` | shared by every lane (FR-006) |
| `loop` geometry, `wrapped`, `origin` | shared |
| `retry` markers / epochs | shared (specs/078) |

State: `Idle → CountIn → Rolling → (lane stopped)* → Stopped → materialised` (one undo
group). Discard: `Rolling → Aborted` drops every lane.

### MidiTakeLane (audio thread, preallocated `kMaxMidiTakeLanes = 16`)

`trackId`, `pendingActive[128]`, `recRoutedActive[128]`, `clipIndex` (message-thread side),
`scene`. `recordTrackRt_` becomes the lane table's published count + ids.

### MidiEvent (`engine/EngineTypes.h`)

Adds `int8_t source` (−1 = non-hardware: computer keyboard, gamepad, Remote, UI) and
`int8_t channel` (0–15). Filter match:
`(track.src == All || track.src == e.source) && (track.ch == 0 || track.ch == e.channel + 1)`;
`e.source == −1` matches only All.

## Managed (C#)

### TrackModel (`Nota.Infrastructure/Persistence/ProjectModel.cs`)

| Property | Type | Migration |
|----------|------|-----------|
| `InputFirst` | int? (1-based) | null ⇒ from `InputChannels`: 0/null → 1, 1 → 1, 2 → 2 |
| `InputWidth` | int? | null ⇒ from `InputChannels`: 0/null → 2, else 1 |
| `InputChannels` | int? | still written when the pick is (1,2), (1,1) or (2,1), so 0.59.x opens it |
| `MidiInputUid` | string? | null = All inputs |
| `MidiInputChannel` | int? | null/0 = All |

### InputPick (`Nota.Application`, replaces `InputChannels` enum in the API)

`record struct InputPick(int First, int Width)` (0-based first). `IAudioEngine`:
`SetTrackInput(int trackId, InputPick)`, `GetTrackInput(int trackId)`,
`AudioInputChannelCount`, `AudioInputChannelName(int i)`,
`SetTrackMidiInput(int trackId, string? uid, int channel)`, `GetTrackMidiInput(...)`.
`SetTrackInputChannels` stays as an adapter until callers move.

### InputSetup (preferences, per `(audioSystem, deviceUid)`)

| Field | Type | Default |
|-------|------|---------|
| `Enabled[i]` | bool per channel | true |
| `Name[i]` | string? per channel | null (device name) |
| `PairName[k]` | string? per odd-aligned pair | null ("In 2k+1/2k+2" or both device names) |

Display name of a pick: user name → device name → "Input N" (mono); pair name → "In a/b".
Menu lists: enabled mono channels; pairs whose both channels are enabled.
