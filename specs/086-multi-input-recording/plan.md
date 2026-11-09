# Implementation Plan: Multi-Channel Input and Simultaneous Multi-Track Recording

**Branch**: `feat/multi-input-recording` (from `release/0.59.2`) | **Date**: 2026-11-08 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/086-multi-input-recording/spec.md`
(issues [#353](https://github.com/nota-daw/nota/issues/353), [#528](https://github.com/nota-daw/nota/issues/528)).

## Summary

Capture every input channel of the selected device and let each audio track pick a mono
input or an odd-aligned stereo pair; turn the single audio take and single MIDI take into
sets of per-track lanes so every armed track records in one pass — Arrangement and
Session, with one undo step, retry, loop take lanes and one latency correction. Mono picks
record mono files. Instrument tracks get a MIDI input filter (device + channel). A
Preferences → Audio I/O page switches inputs on/off and names them. Approach: N-channel
`AudioInput` on all backends → one wide SPSC ring carrying only the channels in use →
message-thread drain scatters columns into per-track lanes (details in
[research.md](research.md)).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-4, AR-5, AR-6, AR-7, AR-9.
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine, audio backends), C# / .NET 10 (managed app, MCP), Avalonia UI.

**Primary Dependencies**: CoreAudio (macOS), miniaudio (Windows/Linux, already vendored),
ASIO driver interface (Windows, already used), CoreMIDI / RtMidi (already used). **No new
dependency.**

**Storage**: project bundle (`TrackModel` gains `InputFirst/InputWidth/MidiInputUid/MidiInputChannel`;
mono takes saved as 1-channel WAV); per-device input setup in the app preferences.

**Audio-Thread Impact**: Yes.
- Input thread: copies the used channel columns into a wide preallocated SPSC ring (no
  alloc/lock; layout read through an atomic pointer to an immutable `CaptureLayout`, AR-5/AR-6).
- Audio thread: monitor ring read per column; MIDI filter check per live event; MIDI take
  lanes in a fixed preallocated table (16); internal-source lanes in a fixed table (16)
  feeding a second wide ring.
- Message thread: drain/scatter, lane buffers (reserved in 30 s chunks), materialisation in
  one undo group.

**Testing**: `tests/Nota.SmokeTest` (new hooks: test input device, `nota_take_record_test_n`,
`nota_engine_push_midi_for_test`); manual hardware pass per [quickstart.md](quickstart.md).

**Target Platform**: macOS (CoreAudio), Windows (WASAPI via miniaudio, ASIO), Linux (miniaudio).

**Project Type**: desktop app (native engine + managed UI).

**Performance Goals**: 8 tracks × 128-sample buffer, 10 min, no more xruns than 1 track
(SC-004); 0-sample offset between lanes (SC-001); up to 64 inputs (SC-002).

**Constraints**: RT-safety (constitution I); C ABI only (II); memory ≈ 11.5 MB/min per mono
track while recording (takes stay in RAM until Stop, research R11).

**Scale/Scope**: ≤ 64 device inputs, ≤ 64 hardware + 16 internal audio lanes, ≤ 16 MIDI lanes per take.

## Constitution Check

*GATE: checked before Phase 0 and again after Phase 1.*

| Principle | Status | How |
|-----------|--------|-----|
| 0 Approved issue | ✅ | #353 and #528 carry `approved` (2026-11-08) |
| I Real-time safety | ✅ | Input thread: copy + push into a preallocated ring; layouts immutable, swapped atomically, retired off-thread; MIDI/internal lanes preallocated; no new locks or allocation on the audio/input threads |
| II Boundary discipline | ✅ | Additive C ABI only ([contracts/c-abi.md](contracts/c-abi.md)); `pluginhost/` untouched |
| III Licensing | ✅ | No new dependency; new files get the SPDX header |
| IV Verification gate | ✅ | 12 new smoke cases ([quickstart.md](quickstart.md)); `scripts/build.sh` per slice |
| V One logical change per PR | ⚠️ → ✅ | The feature is large; delivered as 5 PRs (below), each building and passing on its own |
| VI Docs in sync | ✅ | CHANGELOG per PR; `nota-features`, `nota-docs` (recording, Audio I/O page), `nota-shortcuts` (⌘-click arm hint) |
| VII Spec-first | ✅ | This spec/plan; tasks next |
| VIII No weakened rules | ✅ | Nothing relaxed |

Post-design re-check: no violations; Complexity Tracking empty.

## Delivery slices (one PR each)

| # | PR | Content | Depends on | Closes |
|---|----|---------|-----------|--------|
| 1 | Capture layer | R1/R2: N-channel `AudioInput` (CoreAudio, miniaudio, ASIO), channel names, wide record + monitor rings, `(first,width)` pick, ABI, `InputPick` in `IAudioEngine`, project migration, MCP `set/get_track_input`, `list_audio_inputs`. Existing behaviour unchanged | — | — |
| 2 | Multi-track audio take | R3–R6: `AudioTake` lanes, mono lanes, one undo group, retry, loop lanes, skipped-track report, internal-source lanes | 1 | — |
| 3 | Input menu + Audio I/O page | track input menu (all surfaces), Preferences → Audio I/O, Arm tooltip, record-status line | 1, 2 | #353 |
| 4 | Session multi-slot | R7: per-lane Session slots, independent stop | 2 | — |
| 5 | MIDI input + multi-track MIDI | R8: `MidiEvent.source/channel`, track filter, MIDI take lanes, MIDI input menu, MCP | 2 | #528 |

## Project Structure

### Documentation (this feature)

```text
specs/086-multi-input-recording/
├── spec.md
├── plan.md              # this file
├── research.md          # findings + decisions R1–R12
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   └── mcp-and-ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (touched)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h            # new entry points (contracts/c-abi.md)
├── src/abi/nota_engine_audio.cpp, …      # ABI shims for the new entry points
├── src/audio_io/
│   ├── AudioInput.h                      # N-channel callback, channelCount/Name
│   ├── macos/CoreAudioInput.mm           # drop the stereo downmix, channel names
│   ├── desktop/MiniaudioInput.cpp        # native channel count
│   ├── windows/AsioInput.*, asio/AsioSession.*   # every input channel, names
│   └── MidiInput.h, macos/MidiInput.mm, desktop/MidiInput_rtmidi.cpp  # source uid ↔ index
└── src/engine/
    ├── EngineTypes.h                     # wide ring types, MidiEvent.source/channel
    ├── Track.h                           # input pick, MIDI input filter
    ├── Engine.h                          # AudioTake/lanes, layouts, MIDI lane table
    ├── Engine_Recording.cpp              # ensureAudioInput, drain/scatter, start/stop/abort
    ├── Engine_Render.cpp                 # monitor columns, internal lanes, MIDI filter + lanes
    ├── Engine_Session.cpp                # per-lane slot record/stop
    └── Engine_TakeRetry.cpp              # retry over lanes
src/managed/
├── Nota.Application/Engine/              # InputPick, IAudioEngine additions
├── Nota.Infrastructure/Interop/          # NativeMethods + NotaEngine wrappers
├── Nota.Infrastructure/Persistence/      # TrackModel fields + migration
├── Nota.App/                             # RecordInputPick → full menu, MIDI input menu,
│                                         # PreferencesWindow Audio I/O page, TrackArming tooltip,
│                                         # MainWindow.Detail label, transport status
├── Nota.Application/Localization/        # en.json + ru keys
└── Nota.Mcp/Tools/MixerTools.cs          # new tools
tests/Nota.SmokeTest/                     # MultiInputRecordTests, MidiInputFilterTests, fixtures
```

**Structure Decision**: existing layout; no new projects. Lane logic may move to a new
`src/engine/Engine_TakeLanes.cpp` if `Engine_Recording.cpp` grows past readability.

## Risks

- **ASIO buffers for many inputs**: some drivers cap total buffers; open what the driver
  allows and report the clipped count.
- **miniaudio on WASAPI shared mode** may report the mix format (2 channels): use what it
  reports and say so on the Audio I/O page.
- **RAM on long multi-track takes** (R11): documented; streaming takes to disk is a
  follow-up issue.

## Complexity Tracking

None.
