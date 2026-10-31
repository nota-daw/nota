# Implementation Plan: Sidechain from individual pads, voices and chains

**Branch**: `feat/sidechain-sub-sources` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md) |
**Issue**: nota-daw/nota#190

## Summary

A sidechain route slot is keyed by *(source track, part)* instead of a track. A part is a Drum Rack
pad or Instrument Rack chain (its runtime `RackChain::uid`) or a Rhythm voice (`v + 1`); 0 means the
whole track. Right before a source track's instrument renders, the engine hands it the Pre/Post
buffers of the slots that name its parts (`setPartTaps`, a per-block stack array — no allocation, no
lock); the rack / Rhythm render adds each tapped part into them in the loop that already sums it. A
tapped part that is muted or solo-gated still renders for its taps. The pool grows from 8 to 16 slots
and set calls report when it is full. The C ABI addresses parts by current index; persistence saves
that index (format v28) and resolves it back to the new chain's uid on load. One shared source
picker replaces the per-card track combos and lists parts indented under their track.

Details and alternatives: [research.md](research.md). Shapes: [data-model.md](data-model.md).
Interfaces: [contracts/c-abi.md](contracts/c-abi.md), [contracts/mcp.md](contracts/mcp.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed, Avalonia UI, MCP)

**Primary Dependencies**: none new

**Storage**: project file format v27 → v28 (`SidechainPart` on `DeviceDto` / `InstrumentDto`,
`Part` on `NestedSidechainDto`); no-op migration

**Audio-Thread Impact**: yes — new per-part tap writes inside `DrumRack::render`,
`RackInstrument::render`, `RhythmMachine::render`, fed by a per-block pointer hand-off from
`renderLeafDry`. Buffers are the engine's preallocated route buses (16 × 4 × kMaxBlock stereo,
≈ 8 MiB, allocated in the `Engine` constructor). No allocation, lock or I/O on the audio thread
(AR-6); key selection changes go through `recomputeRouting` on the message thread writing atomics,
as today; rack structure keeps its immutable snapshots (AR-5). With no part keys the render pays
one `n > 0` branch per instrument.

**Testing**: `tests/Nota.SmokeTest` (new `SidechainPartTests.cs`), engine perf gate (specs/034)

**Target Platform**: macOS, Windows, Linux (desktop app)

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: no measurable change without part keys (SC-005); a part key costs no more
than a whole-track key (one stereo add per tap)

**Constraints**: one block of key latency (unchanged); first rack level only; Rhythm voices 0..7

**Scale/Scope**: up to 16 distinct sources; a Drum Rack typically 16 pads (tap lookup is a scan of ≤ 16
taps, only when the track has part keys); 11 sidechain consumers in UI

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time safety | ✅ Taps use preallocated engine buffers handed per block; no alloc / lock / I/O; slot table stays atomics written on the message thread (R3, R6) |
| II. Boundary discipline | ✅ New calls are plain C in `nota_engine.h` (contracts/c-abi.md); no JUCE outside `pluginhost/` |
| III. Licensing | ✅ No new dependency; new files (`SidechainPartTests.cs`, `SidechainSourcePicker.cs`) get the SPDX header |
| IV. Verification gate | ✅ Smoke test extended (quickstart.md) and covers every new ABI call |
| V. One logical change | ⚠️ Large but one feature; delivered as ordered commits (engine → ABI → persistence → UI → MCP → docs) in one PR, as the issue asks |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user manual EN+RU (nota-docs) |
| VII. Spec-first | ✅ This spec / plan / tasks |
| VIII. No weakened rules | ✅ |

Post-design re-check: unchanged — the design keeps every audio-thread write inside buffers owned by
the rendering track, so parallel leaf render stays race-free.

## Project Structure

### Documentation (this feature)

```text
specs/050-sidechain-sub-sources/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/c-abi.md, contracts/mcp.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                    # specs/050 block (contracts/c-abi.md)
└── src/
    ├── devices/Device.h, Instrument.h            # scPart_; part surface; setPartTaps
    ├── devices/racks/RackCore.{h,cpp}            # partCount/partId/partIndex/partName
    ├── devices/racks/DrumRack.cpp                # taps + ghost render
    ├── devices/racks/RackInstrument.{h,cpp}      # taps + ghost render; tap table
    ├── devices/instruments/RhythmMachine.{h,cpp} # voice taps
    ├── engine/Engine.h, Engine.cpp               # kMaxRoutes 16; routeSlotPart_; routeSlotFor
    ├── engine/Engine_Devices.cpp                 # recomputeRouting; key set/get; clone/move copy
    ├── engine/Engine_Rack.cpp                    # rack-child key part; grouping copy
    ├── engine/Engine_Render.cpp                  # feed by (track, part); setPartTaps hand-off; keying
    └── abi/nota_engine*.cpp                      # new entry points
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs       # new members
├── Nota.Infrastructure/Interop/NativeMethods.Devices.cs, NotaEngine.Devices.cs (+ RackDev)
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs, ProjectMigrations.cs
├── Nota.App/DeviceCards/SidechainSourcePicker.cs # NEW shared picker
├── Nota.App/DeviceCards/DeviceParamControls.cs, PluginCard.cs
├── Nota.App/DeviceCards/Bodies/{Compressor,AutoFilter,AutoGain,AutoShift,Ceiling,DynamicEq,Prism,Shutter,Vocoder}DeviceBody.cs
├── Nota.App/DeviceCards/Instruments/FluxInstrumentCard.cs
└── Nota.Mcp/Tools/DeviceTools.cs
tests/Nota.SmokeTest/SidechainPartTests.cs        # NEW (+ registration in Program.cs)
CHANGELOG.md, FEATURES.md, ../nota-docs           # docs
```

**Structure Decision**: existing layout; one new managed file for the picker, one new test file.

## Delivery order

1. Engine core: part id + `Instrument` part surface; `scPart_`; slot table (16, part-aware);
   `recomputeRouting`; consumers fed by `(track, part)`; `keying` for part slots.
2. Capture: `setPartTaps` hand-off; Drum Rack / Instrument Rack / Rhythm taps; ghost render.
3. C ABI + smoke tests 1–8 (engine-only, no UI) — MVP for US1–US4 at the engine level.
4. Persistence v28 + smoke test 9.
5. Shared picker; switch all 11 consumers; Listen / source names.
6. MCP tools.
7. Docs: CHANGELOG, FEATURES.md, nota-docs; PR screenshots.

## Out of scope / follow-ups

- Sidechain keys for devices inside rack chains (rack card UI) — separate issue.
- Hosted plug-ins with multiple output buses as sources.
- Making key edits undoable (today's behaviour is kept, FR-015).
- Nested racks below the first level.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|---|---|---|
| Principle V: one PR spans engine, persistence, UI, MCP | The issue's acceptance needs the whole path; a half (engine without picker) is not user-visible | Splitting into PRs would ship an ABI with no caller or a picker with no engine support |
