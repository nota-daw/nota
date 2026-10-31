# Implementation Plan: Racks redesign and MIDI Effect Rack

**Branch**: `040-racks-redesign` | **Date**: 2026-10-26 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/040-racks-redesign/spec.md`

## Summary

Rebuild the Instrument Rack and Audio Effect Rack cards as one shared card from
`nota-design/Nota Racks.html` (macro column · Chains / Zones-or-Select / Macros tabs ·
Chain / Rack out panel · status line). Mapping happens in place (no separate map view).
Add a new **MIDI Effect Rack**: a MIDI effect (kind 6) whose chains of built-in MIDI effects
take notes by key, velocity and select zones, and whose outputs merge into the
instrument. The engine reuses `RackCore` (snapshots, macros, per-target curves, blob,
undo-by-fork). It adds MIDI chains, a separate select range, chain transpose and velocity
scale, merge and thru, and a selector as macro target. The Instrument Rack gets mono plus
a glide that works. Its chains can also hold MIDI effects, running MIDI effects →
instrument → audio effects like a track ([nota-daw/nota#329](https://github.com/nota-daw/nota/issues/329)).
The rack does this through a new block-render hook on `Instrument`, so timing stays
sample-accurate (research D15). Everything is drawn from Studio tokens, so all four themes work and
recolour live (research D1–D14).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill (DSP class, kind registration across C++ and C#, device-card UI).
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 + Avalonia 12 (app, MCP)

**Primary Dependencies**: none new (Avalonia, existing engine code; JUCE untouched, plug-ins not hosted in MIDI racks)

**Storage**: project file (`MidiDeviceDto.State` blob ref, format version +1); rack blob v9; user presets for kind 6

**Audio-Thread Impact**: `playEvents` gains one virtual check per block: instruments that render block events themselves (an Instrument Rack whose chains hold MIDI effects) take the whole block. In that case the rack runs each chain's MIDI effects and segment-renders that chain on member scratch (2 × 1024 events), with no allocation. New `MidiRack::process` runs on the audio thread inside the track's MIDI chain. It uses only member-preallocated buffers (16 chains × 2 × 1024 events, plus 128-entry routing and merge tables): no allocation, locks or I/O (AR-6). Structural edits publish a new `RackState` snapshot via `SnapshotHistory` (AR-5). Values are relaxed atomics on `ChainControls` (AR-4 pattern as existing racks). Instrument Rack mono/glide uses a fixed 128-entry note stack and block-rate `noteExpression` bend ramps.

**Testing**: `tests/Nota.SmokeTest` (engine through the C ABI, MCP, design guards), headless Avalonia captures for UI/themes (quickstart Q4–Q5)

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: a 3-chain MIDI rack adds no audible latency (same block, sample-accurate offsets kept); no xruns at 64-sample buffers; card rebuild on edit stays within one frame for interaction feedback

**Constraints**: real-time safe; existing projects with Instrument / Audio Effect racks load and sound identical; zero new design allow-list entries

**Scale/Scope**: 3 rack kinds share one card; 6 MIDI effect kinds in MIDI chains; ~40 rack-eligible device kinds need a key-param entry; ~25 new ABI calls; ~22 new MCP tools

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I. Real-time audio safety | Pass | Preallocated members, snapshot swap for structure, atomics for values, no allocation in `MidiRack::process`, mono/glide or the chain MIDI-effect path (research D3, D4, D15); perf gate run for the `playEvents` hook |
| II. Boundary discipline | Pass | New calls are plain C in `nota_engine.h` ([contracts/c-abi.md](contracts/c-abi.md)); no C++ types cross; JUCE untouched |
| III. Licensing hygiene | Pass | New files carry the SPDX header; no new dependency |
| IV. Verification gate | Pass | Smoke test extended: routing, stuck notes, persistence, mono/glide, mapping rules, v8 compatibility, key params, design guards (quickstart Q2) |
| V. One logical change per PR | Watch | Deliver as a stack of PRs in task order: (1) engine core + ABI + tests, (2) managed access + MCP + persistence, (3) shared card + themes, (4) docs. Each builds and passes on its own |
| VI. Docs in sync | Pass | CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs` (EN + RU rack pages, new MIDI rack page, screenshots), `nota-shortcuts` (Esc ends mapping) |
| VII. Spec-first | Pass | spec + clarifications + this plan; tasks next |
| VIII. Rules not weakened | Pass | No allow-list growth, no audio-thread exceptions |

*Post-design re-check (after Phase 1)*: still passing. The design adds no global state and
no new thread. The only shared-format change is the backward-compatible blob v9.

## Project Structure

### Documentation (this feature)

```text
specs/040-racks-redesign/
├── plan.md              # this file
├── research.md          # Phase 0: findings + decisions D1–D14
├── data-model.md        # Phase 1: rack / chain / mapping fields, blob v9, view state
├── quickstart.md        # Phase 1: validation runs
├── contracts/
│   ├── c-abi.md         # new C ABI
│   ├── mcp-tools.md     # new / changed MCP tools
│   └── ui-rack-card.md  # the shared card's layout, interaction and labels
├── checklists/requirements.md
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                 # + midi_effect state, nota_midirack_*, nota_rack_set_mono, selector target
├── src/devices/racks/
│   ├── RackCore.h/.cpp                        # RackChain.midiDevices; ChainControls sel/transpose/velScale;
│   │                                          # mono/merge/thru; mapping target −3; move-on-add; blob v9
│   ├── RackInstrument.h/.cpp                  # mono note stack + glide via per-note bend;
│   │                                          # renderBlock: per-chain MIDI FX → instrument → FX (#329)
│   ├── RackDevice.cpp                         # selector mapping target (−3)
│   └── MidiRack.h/.cpp                        # NEW: MidiDevice + RackCore, routing/merge/thru (kind 6)
├── src/devices/Instrument.h                   # rendersBlockEvents/renderBlock, midiReset/midiTimeJump hooks (#329)
├── src/devices/DeviceFactory.cpp              # makeMidiDevice(6)
├── src/engine/Engine_Rack.cpp, Engine.h       # midiRackAt / midiRackForEdit (fork for undo), rack ops
├── src/engine/Engine_Devices.cpp              # MIDI effect state get/set, clone path
├── src/engine/Engine_Render.cpp               # playEvents → renderBlock hook; reset/loop-wrap fan-out to rack chains
└── src/abi/nota_engine_rack.cpp, nota_engine_device.cpp   # new C entry points

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs    # + MidiRack*, MidiEffectGet/SetState, RackSetMono, curves
├── Nota.Presentation/Racks/RackKeyParams.cs  # NEW: curated ≤3 key params per kind, by name (UI-free, smoke-tested)
├── Nota.Application/Palette/BuiltinDeviceCatalog.cs, DeviceCategories.cs   # "Nota MIDI Effect Rack" (MIDI · Racks)
├── Nota.Infrastructure/Interop/NativeMethods.MidiRack.cs, NotaEngine.MidiRack.cs   # NEW P/Invoke + wrapper
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs, PresetService.cs  # MidiDeviceDto.State, format +1, presets
├── Nota.Mcp/Tools/MidiRackTools.cs            # NEW; RackTools / EffectRackTools / MidiFxTools updated
└── Nota.App/
    ├── DeviceCards/Rack/
    │   ├── IRackAccess.cs                     # extended surface + MidiRackAccess
    │   ├── RackKindProfile.cs                 # NEW: per-kind labels, zones, modes, rack-out rows, drop rules
    │   ├── RackCard.cs                        # NEW: shared card shell parts + view state
    │   ├── RackCard.Macros.cs / .Chains.cs / .Zones.cs / .MacroTab.cs / .RightPanel.cs   # NEW parts
    │   ├── RackCardView.cs                    # Ir*/Ae*/macro-map builders removed; entry points delegate to RackCard
    │   └── RackCardView.Drum.cs, MacroPanel.cs   # Drum Rack: compile fixes only
    ├── DeviceChainView.Midi.cs                # kind 6 → RackCard inside BuildCardShell
    └── (shortcut registration for Esc-ends-mapping)

tests/Nota.SmokeTest/
├── MidiRackTests.cs                           # NEW: routing, stuck notes, persistence, MCP
├── RackRedesignTests.cs                       # NEW: mono/glide, mapping rules, v8 fixtures, key params
├── RackChainMidiTests.cs                      # NEW (#329): chain Arp timing = track Arp, zones before MIDI FX, persistence, copy, undo, MCP
├── Fixtures/racks-v8/*.bin                    # NEW: saved v8 blobs (Instrument + Audio racks)
└── Program.cs                                 # wire sections

CHANGELOG.md, FEATURES.md, ../nota-docs (EN + RU)
```

**Structure Decision**: Engine work stays in `devices/racks/` next to the existing racks
(one new class). Managed work stays in `DeviceCards/Rack/`, split into partial files by
card region so no file grows past the current 1.8 k-line `RackCardView.cs`. MCP gets a
new tool class like the existing per-device tool files.

## Phases (for /speckit-tasks)

1. **Engine core**: `RackCore` fields + blob v9 + mapping move/selector target; `MidiRack`;
   factory; Instrument Rack mono/glide; chain MIDI effects in Instrument Racks (`renderBlock`
   hook, reset/loop fan-out, ABI `nota_rack_chain_midi_*`) → US6, FR-030/032/034; Engine fork-for-edit for MIDI racks; ABI; smoke tests
   (routing, stuck notes, persistence, v8 fixtures). → US4 engine, FR-019, FR-026, FR-029.
2. **Managed plumbing**: interop, `IAudioEngine`, persistence (`State`, format bump),
   presets, catalog/Browser entry, MCP tools (MIDI rack + rack chain MIDI effects) + AI golden
   scenario. → US4, US6, FR-022, FR-025, FR-034.
3. **Shared card**: `RackKindProfile`, `RackKeyParams` (+ test), `RackCard` parts (macros,
   chains + in-place mapping, zones/select + probe, macros tab + Mixed, right panel, status
   line), Instrument chain strip with a MIDI section + drop/Add-menu rules, wire Instrument /
   Audio / MIDI entry points, remove old builders, Esc shortcut. → US1, US2, US3, US4, US6 UI
   (FR-031, FR-033).
4. **Themes + guards**: design-guard run with no allow-list growth, headless 4-theme
   captures, live-switch check. → US5, SC-003.
5. **Docs**: CHANGELOG, FEATURES (`nota-features`), user docs EN + RU + screenshots
   (`nota-docs`), Shortcuts (`nota-shortcuts`).

## Complexity Tracking

No constitution violations to justify.
