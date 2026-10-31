# Implementation Plan: Nota Twin — a morphing two-filter analog poly

**Branch**: `feat/morph-poly-synth` | **Date**: 2026-10-30 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/055-morph-poly-synth/spec.md`

## Summary

A new built-in instrument, **Nota Twin (kind 18)**: up to 12 analog-modelled voices — two
polyBLEP oscillators with a continuous Wave and a wavefolding Metal, noise, a new
Steiner-Parker-style multimode filter and the shared ladder, a series ↔ parallel route, Brute
feedback, three envelopes, three LFOs and seeded drift. Every sound parameter has an **A and a
B value per part**, exposed as separate plugin-params (`lo.a.cut1`, `lo.b.cut1`), so the
**Morph** between them — and automation that writes to the edited side — uses the existing
param, automation, preset and undo machinery unchanged. A 64-slot matrix per part, eight
macros, an internal FX chain built from Nota's Chorus / Flanger / Phaser / Delay / Reverb
devices, a transport-locked polyphonic **sequencer / arpeggiator** with motion lanes (pattern
data as an immutable snapshot, edited through the existing instrument action channel), and
**Single / Layer / Split** with the 12 voices shared between two parts. One new C ABI function.
Delivered in three slices (P1 → P2 → P3), each a shippable state. Decisions: [research.md](research.md).

## Nota: Read Before Planning

Consulted: [`ARCHITECTURE.md`](../../ARCHITECTURE.md) AR-4/5/6/7/9; the `nota-instrument`
skill (DSP class, registration, card design); [`DESIGN.md`](../../DESIGN.md) + `nota-design`
for the card; [`CONTRIBUTING.md`](../../CONTRIBUTING.md) for headers and dependencies.

## Technical Context

**Language/Version**: C++20 (engine DSP, registration, C ABI); C# / .NET 10 (interop, catalog,
presets, MCP, Remote, device card in Avalonia).

**Primary Dependencies**: none new. Reuses `pentad::` oscillator core + HIIR decimators
(already a listed dependency, fetched by CPM for Pentad / Consort), `dsp::LadderFilter`, `dsp::SvfFilter` (fallback),
`dsp/poly` voice management, and the Chorus / Flanger / Phaser / Delay / Reverb devices.

**Storage**: project file and presets as for other built-ins — normalized params by id;
Twin adds a pattern block to its state blob and to its presets' `StateBase64` (research R11).

**Audio-Thread Impact**: new per-voice DSP in `render()`, allocation-free after construction
(voices, FX devices, matrix tables, pattern storage pre-sized; the delay / reverb buffers are
allocated in the constructor like Consort's). Param values arrive as atomics through the
plugin-param interface; pattern edits publish an immutable snapshot (AR-5) the audio thread
picks up with an atomic pointer load; retired snapshots are freed on the message thread
(RackCore scheme). Musical time only from `setTransport` / `TransportInfo` (AR-9). No locks,
no I/O.

**Testing**: `tests/Nota.SmokeTest` (new Twin section, contract items 1–5); render-comparison
tests on deterministic (seeded) output; `tests/perf/nota_perf.cpp` scenarios
(`instrument-18`, `twin-full-12`, `twin-layer-6x6`, `pentad-full-16`) and
`scripts/perf-gate.sh` for the shared-ladder change; managed unit tests where the preset
remap / "To clip" logic lives.

**Target Platform**: macOS (arm64 + x86_64), Windows x64, Linux x64 — as every built-in.

**Project Type**: desktop app (native engine + managed app).

**Performance Goals**: SC-001 — 12 voices, everything on, no dropouts at 128 samples on the
reference Mac and ≤ 2 × Pentad at 16 voices; SC-006 — idle ≈ empty track; SC-009 — step onsets
on the sample grid.

**Constraints**: append-only param order and action / scope ids; per-voice morph without
per-sample param interpolation (R3); ≤ 1 050 params must not slow the app's param lists (R2a).

**Scale/Scope**: ≈ 5–6 k lines of engine C++ (Consort ≈ 2.3 k is the reference for one
voice architecture; Twin adds parts, matrix, sequencer), one large device card, ≥ 40 presets,
≈ 38 registration sites.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|-----------|--------|-----|
| I. Real-time safety | ✅ | pre-sized everything; params as atomics; patterns as immutable snapshots (AR-5); no lock / alloc / I/O in `render()`; transport from `setTransport` (AR-9) |
| II. Boundary discipline | ✅ | one new C function `nota_engine_add_twin_track`; everything else through existing generic param / action / state / scope calls; no JUCE |
| III. Licensing | ✅ | no new dependency; HIIR is already a listed dependency (`LICENSES/third-party.md`); every new file gets the two-line SPDX header; no Arturia names or material |
| IV. Verification gate | ✅ | smoke test extended (5 contract checks); perf scenarios added |
| V. One logical change per PR | ⚠️ → ✅ | the feature is large: shipped as **three PRs**, one per slice (P1 / P2 / P3), each green and usable on its own; the ladder 12 dB tap ships with P1 (no other instrument uses that primitive) |
| VI. Docs in sync | ✅ | CHANGELOG, FEATURES.md (`nota-features`), user docs EN + RU (`nota-docs`), shortcuts if the card adds any (`nota-shortcuts`) — per slice |
| VII. Spec-first | ✅ | this spec / plan / tasks |
| VIII. Rules not weakened | ✅ | no exceptions requested |

**Post-design re-check (after Phase 1)**: still passes. The design added no ABI beyond the one
function, no shared mutable state with the audio thread beyond atomics and snapshots, and the
param-per-side model keeps automation / undo inside existing mechanisms.

## Delivery slices

| Slice | Stories | Ships | PR |
|-------|---------|-------|----|
| 0 | — | `LadderFilter` 12 dB tap — no instrument uses the primitive yet, so it ships with P1 | (in PR 1) |
| P1 | US1, US2, US3 | Twin with one part: voice, A/B morph, expression, fixed routes, a first card (Osc · Filter · Env · LFO · Morph strip, pad), ~12 starter presets, kind registration everywhere, smoke + perf | PR 1 |
| P2 | US4, US5, US6, US7 | 64-slot matrix + macros, FX section, ≥ 40 presets, full card (Matrix · FX tabs, S size, graphs, themes), MCP `read_twin` / `set_twin_route` / side tools | PR 2 |
| P3 | US8, US9 | sequencer / arp / motion lanes / patterns in presets / "To clip"; Lower / Upper parts, Split / Layer, voice share, load-into-part; MCP pattern tools | PR 3 |

The **param layout is designed for P3 from P1**: P1 already declares both parts and both sides
(Upper silent in Single) so no P1 project needs migration — later slices only append.

## Project Structure

### Documentation (this feature)

```text
specs/055-morph-poly-synth/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R15 decisions
├── data-model.md        # params, routes, patterns, state blob
├── quickstart.md        # validation guide
├── contracts/
│   ├── c-abi.md         # new ABI function, action ids, scope layout, smoke contract
│   └── mcp-tools.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                 # + nota_engine_add_twin_track
├── src/abi/nota_engine_track.cpp              # + the ABI function
├── src/engine/Engine.h, Engine_Devices.cpp    # add track, clone, makeBuiltinInstrument
├── src/devices/DeviceFactory.cpp              # kind 18
├── src/devices/racks/RackCore.{h,cpp}         # makeInstrument kind 18
├── src/devices/instruments/
│   ├── Twin.h / Twin.cpp                      # instrument: params, parts, voice pool, render, state
│   ├── TwinVoice.h / TwinVoice.cpp            # one voice: oscs, Metal, mixer, filters, Brute, envs
│   ├── TwinMod.h / TwinMod.cpp                # morph evaluation, matrix, LFOs, sources/dests tables
│   └── TwinSeq.h / TwinSeq.cpp                # patterns snapshot, sequencer, arp, motion (P3)
├── src/dsp/primitives/
│   ├── SteinerFilter.h / .cpp                 # new multimode filter (R5)
│   └── LadderFilter.h / .cpp                  # + 12 dB tap (slice 0)
├── src/devices/midi_fx/Arpeggiator.*          # factor note-order helper if needed (R10)
└── CMakeLists.txt

src/managed/
├── Nota.Infrastructure/Interop/NativeMethods.Tracks.cs, NotaEngine.Tracks.cs
├── Nota.Infrastructure/Persistence/
│   ├── FactoryPresetCatalog.cs                # Twin presets (+ TwinSeq text form)
│   ├── PresetService.cs                       # Twin: NamedParams + pattern StateBase64; load into part
│   └── ProjectService.cs
├── Nota.Infrastructure/Audition/PresetAudition.cs
├── Nota.Application/Engine/IAudioEngine.cs
├── Nota.Application/Palette/BuiltinDeviceCatalog.cs, DeviceCategories.cs
├── Nota.Application/Twin/                     # side / part copy, pattern → clip (pure logic, unit-testable)
├── Nota.Presentation/DeviceInsertService.cs, Racks/RackKeyParams.cs
├── Nota.App/DeviceCards/InstrumentCardFactory.cs
├── Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs   # the card (tabs, A/B, parts)
├── Nota.App/Controls/TwinViews.cs             # waveform, dual-filter response, matrix grid, step grid, pad
├── Nota.App/DeviceCards/Rack/*, DeviceChainView.Shell.cs, NotaDocs.cs, PreferencesWindow.cs, Theme/NotaGeometry.cs
├── Nota.Mcp/Tools/InstrumentTools.cs (+ TwinTools.cs)
└── Nota.Remote/RemoteParams.cs

tests/
├── Nota.SmokeTest/Program.cs (+ TwinTests.cs)
└── perf/nota_perf.cpp
```

**Structure Decision**: one instrument split across four engine translation units (instrument
/ voice / modulation / sequencer) so each slice touches its own files; managed logic that is
not view code (side copy, part remap, pattern → clip) goes to `Nota.Application/Twin/` so it is
unit-testable without the UI. Every registration site mirrors Consort's.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| ~1 050 params on one instrument (others ≤ 160) | per-side params are what makes FR-024 (automation per side), presets, undo and CV work with no new mechanism | a hidden B-side blob would need a parallel automation / undo / preset path for side B |
| Three PRs for one feature (Principle V) | P1 alone is a full instrument; P3's sequencer and parts are each large | one PR would be unreviewable; finer splits would ship unplayable intermediate states |
