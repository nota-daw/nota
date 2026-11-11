# Data Model: Private Cue Mix on a Separate Hardware Output Pair

**Feature**: [spec.md](spec.md) | Decisions: [research.md](research.md)

## Engine (C++)

### OutputRouting — `audio_io/OutputRouting.h` (new)

| Field | Type | Rule |
|-------|------|------|
| `mainFirst` | int16 | 0-based device channel; even for width 2; default 0 |
| `mainWidth` | int8 | 1 or 2; default 2 |
| `cueFirst` | int16 | 0-based device channel, or −1 = no Cue output (default) |
| `cueWidth` | int8 | 1 or 2; default 2 |

Packed into one `std::atomic<uint64_t>` on the backend; read once per callback.

**Resolution** against `deviceChannels` (pure function `resolveRouting`, same on all threads):

1. Main pair not fully on the device → Main = (0, min(2, deviceChannels)), flag `MainFellBack`.
2. A width-2 pick whose second channel is missing → width 1.
3. Cue pair not on the device, or overlapping resolved Main → Cue off, flag `CueUnavailable`.

`kMaxOutputChannels = 64`.

### Track (existing) — new flag

| Field | Type | Notes |
|-------|------|-------|
| `cue_` | `std::atomic<bool>` | relaxed; copied by `cloneTrack` and track duplication; valid on audio, instrument, group and return tracks |

### Engine (existing) — new state

| Field | Type | Notes |
|-------|------|-------|
| `cueSeg_` | `float*` | this segment of the caller's Cue buffer; null offline / in export |
| `cueGain_` | `std::atomic<float>` | linear, 0 … 2.0 (+6 dB), default 1 |
| `clickDest_` | `std::atomic<int32_t>` | 0 Main, 1 Cue, 2 Main + Cue |
| `clickGain_` | `std::atomic<float>` | linear, 0 … 2.0, default 1 (× `kMetroGain`) |
| `cuePeakL/R_`, `cueRmsL/R_` | `std::atomic<float>` | Cue meter, post Cue level |

## Managed (C#)

### OutputSetup — `Nota.Application/OutputSetups.cs` (new), machine-level

Stored in `Settings.OutputSetups : Dictionary<string, OutputSetup>`, keyed by
`InputSetups.KeyOf(system, outputUid, asioDriver)`.

| Field | Type | Default |
|-------|------|---------|
| `MainFirst` | int (1-based in JSON, like spec 086 inputs) | 1 |
| `MainWidth` | int | 2 |
| `CueFirst` | int? (null = no Cue output) | null |
| `CueWidth` | int | 2 |
| `Click` | enum ClickDestination {Main, Cue, MainAndCue} | Main |
| `ClickDb` | double (−∞ … +6) | 0 |

Validation: Main and Cue never overlap on save (FR-004); an ASIO setup with no entry
seeds `MainFirst` from `AudioConfig.asioOutputPair` (FR-005).

### Project file — format v32

| Where | Field | Type | Missing / older |
|-------|-------|------|-----------------|
| `TrackDto` | `Cue` | bool? | false |
| `MixerDto` | `CueVolume` | float? (linear) | 1.0 |

### MIDI Learn

| `MidiTargetKind` | Kind | Scope | Applies |
|------------------|------|-------|---------|
| `TrackCue` | toggle (button) | track (by index, like `TrackMute`) | `SetTrackCue(!cue)` |
| `CueVolume` | continuous | global | `SetCueVolume` (no undo step) |

## State transitions

- **Track Cue**: off ⇄ on (UI button, MIDI, project load). Deleting a track or turning it
  into a group drops the flag; freezing keeps it (the frozen buffer is tapped).
- **Cue output availability** (derived): Available ⇄ Unavailable on device change / routing
  edit. Unavailable never changes `clickDest_` or any track's flag.
