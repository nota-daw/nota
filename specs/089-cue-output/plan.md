# Implementation Plan: Private Cue Mix on a Separate Hardware Output Pair

**Branch**: `feat/483-cue-output` (from `develop`) | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/089-cue-output/spec.md`
(issue [#483](https://github.com/nota-daw/nota/issues/483)).

## Summary

Open every output channel of the device and let the user put Main and a private Cue on
any two output pairs. The engine renders two stereo buses per block — Main as today, and
Cue: the sum of every cued track's signal after its devices and before its fader, pan,
mute and solo, at its own Cue level. The metronome and count-in click can go to Main, Cue
or both, with a click level. Output placement and the "Cue output gone" safety live in one
backend-side routing helper; routing is hot (no device restart). Settings for outputs and
the click are per machine and device; cued tracks and the Cue level are saved in the
project (v32). Cue toggles and the Cue level are MIDI-learnable. Details:
[research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-4, AR-5, AR-6, AR-7, AR-9.
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine, audio backends), C# / .NET 10 (managed app), Avalonia UI.

**Primary Dependencies**: CoreAudio (macOS), miniaudio (Windows/Linux, vendored), ASIO
interface (Windows, existing). **No new dependency.**

**Storage**: project bundle v32 (`TrackDto.Cue`, `MixerDto.CueVolume`); `midimap.json`
(two new target kinds); `settings.json` (`OutputSetups` per device); `audio.json`
unchanged (`asioOutputPair` kept for migration).

**Audio-Thread Impact**: Yes.
- Backend callback: reads one packed routing atomic, scatters two preallocated stereo
  buffers onto the device channels, zeros the rest (no alloc/lock).
- Engine: one preallocated `cueBus_`; a per-track `cue_` atomic checked in the serial
  accumulate step; muted/solo-gated cued tracks are rendered for Cue only; Cue level,
  NaN squash, clamp and meter after the master section; click writes to Main and/or Cue.
  All state is relaxed atomics (same pattern as mute/solo) — no commands, no snapshots.
- Cost when no track is cued and no click goes to Cue: one branch per leaf and a cleared
  stereo buffer per block.

**Testing**: `tests/Nota.SmokeTest` (`CueOutputTests`, new test entry points
`nota_engine_render_device_block_cue`, `nota_audio_scatter_test`); manual hardware pass per
[quickstart.md](quickstart.md).

**Target Platform**: macOS (CoreAudio), Windows (WASAPI via miniaudio, ASIO), Linux (miniaudio).

**Project Type**: desktop app (native engine + managed UI).

**Performance Goals**: SC-007 — ≤ 2 % render cost on a 64-track project, no new xruns at
64 frames; Main and Cue on the same sample (SC-003).

**Constraints**: RT safety (constitution I); C ABI only (II); output channels ≤ 64.

**Scale/Scope**: 2 destinations (Main, Cue); any number of cued tracks; ≤ 64 device outputs.

## Constitution Check

*GATE: checked before Phase 0 and again after Phase 1.*

| Principle | Status | How |
|-----------|--------|-----|
| 0 Approved issue | ✅ | #483 carries `approved` (checked 2026-11-10) |
| I Real-time safety | ✅ | Preallocated `cueBus_` and backend scratch; atomics only; routing resolved with integer math in the callback; device-alive listener only raises a flag |
| II Boundary discipline | ✅ | Additive C ABI ([contracts/c-abi.md](contracts/c-abi.md)); `pluginhost/` untouched |
| III Licensing | ✅ | No new dependency; new files (`OutputRouting.h`, `OutputSetups.cs`, `PreferencesWindow.Outputs.cs`, `MixerView.Cue.cs`, `CueOutputTests.cs`) get the SPDX header |
| IV Verification gate | ✅ | 16 smoke cases ([quickstart.md](quickstart.md)); `scripts/build.sh` per slice |
| V One logical change per PR | ⚠️ → ✅ | Delivered as 3 PRs (below), each green on its own |
| VI Docs in sync | ✅ | CHANGELOG per PR; `nota-features`; `nota-docs` (Preferences → Audio, Mixer); `nota-shortcuts` not needed (no new key) |
| VII Spec-first | ✅ | This spec/plan; tasks next |
| VIII No weakened rules | ✅ | Nothing relaxed |

Post-design re-check: no violations; Complexity Tracking empty.

## Delivery slices (one PR each)

| # | PR | Content | Depends on | Closes |
|---|----|---------|-----------|--------|
| 1 | Multi-channel output + Main pair | R1–R3: `RenderCallback(main, cue, n)`, `OutputRouting.h` scatter + resolve, all three backends open every output, output channel count/names, hot routing, macOS device-alive event, ABI, `OutputSetups` + Preferences → Audio → Outputs (Main only, Cue picker hidden), ASIO pair migration. Behaviour unchanged on stereo devices | — | — |
| 2 | Cue bus + click routing | R4–R5: `Track::cue_`, `cueBus_`, taps (leaf/group/return, muted/solo exemption), Cue level/meter, click destination + level, ABI, smoke cases; Cue picker + click rows in Outputs | 1 | — |
| 3 | Cue in the mixer, project and MIDI Learn | Cue strip, track Cue toggles on every surface, unavailable/fallback states, project v32, MIDI Learn kinds, EN + RU strings, docs | 2 | #483 |

## Project Structure

### Documentation (this feature)

```text
specs/089-cue-output/
├── spec.md
├── plan.md              # this file
├── research.md          # findings + decisions R1–R7
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   └── ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (touched)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h              # contracts/c-abi.md
├── src/abi/nota_engine_audio.cpp           # output routing, channel names, scatter test
├── src/abi/nota_engine_track.cpp           # set/get cue
├── src/abi/nota_engine_core.cpp            # cue volume/meter, click dest/volume, render_device_block_cue
├── src/audio_io/
│   ├── AudioBackend.h                      # RenderCallback(main, cue, n); setOutputRouting; outputChannelCount/Name
│   ├── OutputRouting.h                     # NEW: routing, resolve, scatter
│   ├── macos/CoreAudioBackend.mm           # N-channel client format, names, device-alive listener
│   ├── desktop/MiniaudioBackend.cpp        # native channel count
│   └── asio/AsioSession.*, windows/AsioBackend.*   # cue pair, main from routing
└── src/engine/
    ├── Track.h                             # cue_ flag
    ├── Engine.h, Engine.cpp                # cueBus_, gains, meters, start(), cloneTrack
    ├── Engine_Tracks.cpp                   # setTrackCue, duplicate copies the flag
    └── Engine_Render.cpp                   # render/processBlock(main, cue), taps, click routing, cue post
src/managed/
├── Nota.Application/OutputSetups.cs, Settings.cs     # NEW per-device output setup
├── Nota.Application/Engine/IAudioEngine.cs
├── Nota.Infrastructure/Interop/NativeMethods*.cs, NotaEngine*.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs   # v32
├── Nota.App/PreferencesWindow.cs, PreferencesWindow.Outputs.cs (NEW), OutputSetupAccess.cs (NEW)
├── Nota.App/MixerView.cs, MixerView.Compact.cs, MixerView.Cue.cs (NEW), SessionView.Mixer.cs,
│   TrackInspectorView.cs, ArrangementView.cs           # Cue strip + toggles
├── Nota.App/MidiLearn.cs, MidiLearnService.cs, MidiMapView.cs   # TrackCue, CueVolume
└── Nota.Application/Localization/en.json, ru.json
tests/Nota.SmokeTest/CueOutputTests.cs (NEW), fixtures
CHANGELOG.md, FEATURES.md
```

**Structure Decision**: existing layout; no new projects.

## Risks

- **WASAPI shared mode** may expose only the mix format's channels (often 2) for a
  multi-output interface: Cue then shows unavailable with a hint to use ASIO / exclusive.
- **CoreAudio aggregate / large devices** (64 ch): scatter cost is linear in channels —
  measured in SC-007.
- **Bluetooth / AirPods** route: 2 channels — today's behaviour, Cue unavailable.
- **Hiding Cue toggles on a stereo device** may hide a project's saved Cue state; the Cue
  strip says "Cue output not on this device" whenever any track is cued.

## Complexity Tracking

None.
