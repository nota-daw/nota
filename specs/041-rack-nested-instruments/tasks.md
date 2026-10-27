---

description: "Tasks: Mosaic and Drum Rack in Instrument Rack chains"
---

# Tasks: Mosaic and Drum Rack in Instrument Rack chains

**Input**: Design documents from `specs/041-rack-nested-instruments/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/c-abi.md, quickstart.md

**Tests**: the smoke test is required by the spec (FR-014) and the constitution (IV).

## Format: `[ID] [P?] [Story] Description`

## Phase 1: Setup

- [X] T001 Build the engine in the worktree (`src/native/nota.engine/build`, Release) so later steps can link

---

## Phase 2: Foundational (blocking)

- [X] T002 Add `RackCore::replaceChainInstrument(int32_t chain, std::shared_ptr<Instrument>)` (keeps devices, MIDI effects, controls, mappings; sets the rack's sample rate on the instrument) in `src/native/nota.engine/src/devices/racks/RackCore.{h,cpp}`
- [X] T003 Teach `RackCore::makeInstrument` kind 17 (`Mosaic`, empty) and kind 4 (`DrumRack`, empty) in `src/native/nota.engine/src/devices/racks/RackCore.cpp`; guard `deserialize` with a thread-local depth so a rack instrument (kind 3/4) is built only at depth 0 ("a rack instrument (kind 3/4) is built only at depth 0")
- [X] T004 Add `Engine::kNestedRackBase = -100000` and resolve `deviceIndex <= kNestedRackBase` in `Engine::rackCoreAt` (the RackCore of the `RackInstrument` in chain `kNestedRackBase - di` of the track's plain Instrument Rack; null otherwise) and `Engine::rackForEdit` (fork the outer via `rackForEdit(t, -1)`, fork the inner `DrumRack` with mono/pedal/ctl state as for a track Drum Rack, `replaceChainInstrument`) in `src/native/nota.engine/src/engine/{Engine.h,Engine_Rack.cpp}`; test the nested range before the MIDI-rack range everywhere `kMidiRackBase` is compared
- [X] T005 Enforce one nesting level in `Engine::rackAddChain` / `rackSetChainInstrument`: kind 3 never; kind 4 only for `di == -1` on a plain `RackInstrument` (not `DrumRack`) in `src/native/nota.engine/src/engine/Engine_Rack.cpp`
- [X] T006 Extend `Engine::findSampleById` to chain Mosaic zones and nested Drum Rack pads (Sampler / Mosaic) in `src/native/nota.engine/src/engine/Engine_Tracks.cpp`

**Checkpoint**: engine accepts both kinds; nested address resolves.

---

## Phase 3: User Story 1 — Mosaic in a chain (P1) 🎯 MVP

**Goal**: Mosaic as a chain instrument with its full editor, undo and persistence.

**Independent Test**: smoke "Rack chains: Mosaic" section + headless shot of the chain editor.

- [X] T007 [US1] Engine: `rackChainMosaicSetProgram(t, di, c, text, undoable)` (undoable: `rackForEdit` + clone + `setProgram` + `replaceChainInstrument`), `rackChainMosaicProgram`, `rackChainMosaicStatus`, `rackChainMosaicZoneSampleId` in `src/native/nota.engine/src/engine/{Engine.h,Engine_Rack.cpp}`
- [X] T008 [US1] ABI: `nota_rackdev_chain_mosaic_set_program / _program / _status / _zone_sample_id` (contracts/c-abi.md) in `include/nota/nota_engine.h`, `src/abi/nota_engine_rack.cpp`, `nota_exports.txt`, `nota_exports.map`
- [X] T009 [P] [US1] Managed interop: `IAudioEngine.RackChainMosaicSetProgram/Program/TryGetStatus/ZoneSampleId(trackId, deviceIndex, chain, …)` in `src/managed/Nota.Application/Engine/IAudioEngine.cs`, `src/managed/Nota.Infrastructure/Interop/{NativeMethods.Rack.cs,NotaEngine.Rack.cs}`
- [X] T010 [US1] Move `RackChainEngineProxy` to `src/managed/Nota.Application/Engine/RackEngineProxies.cs` (public, with a `Create(engine, chain, deviceIndex = -1)` factory) and map `MosaicSetProgram / MosaicProgram / TryGetMosaicStatus / MosaicZoneSampleId` to the chain calls; update `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs`
- [X] T011 [US1] Add "Nota Mosaic" to the add-chain menu (`RackCardView.cs` `ShowAddChainMenu`), the chain instrument menu (`RackCard.Chains.cs`) and the Drum Rack pad instrument menu (`RackCardView.Drum.cs`)
- [X] T012 [US1] MCP: `load_mosaic_preset` gains `chain` (≥0: apply to that Instrument Rack chain via the chain proxy) in `src/managed/Nota.Mcp/Tools/MosaicTools.cs`; the `set_chain_instrument` description lists 17 and 4 in `src/managed/Nota.Mcp/Tools/RackTools.cs`
- [X] T013 [US1] Smoke test "racks: Mosaic chain" (`tests/Nota.SmokeTest/RackNestingTests.cs`): generated WAVs → program → status ready → note in zone audible / outside silent → macro mapped to a Mosaic param → blob round-trip keeps program text → undoable set + undo restores text, in `tests/Nota.SmokeTest/Program.cs`

**Checkpoint**: US1 works on its own.

---

## Phase 4: User Story 2 — Drum Rack in a chain (P2)

**Goal**: a nested Drum Rack with its card in a device window, undo, persistence.

**Independent Test**: smoke "Rack chains: nested Drum Rack" + headless shot of the nested card.

- [X] T014 [US2] Engine: honour `di` in the Drum-Rack-only engine calls already taking it (they all did); swing/humanize are applied only for the track instrument in `computeInstrumentMidi`, so a nested rack's are ignored — verified, no change in `src/native/nota.engine/src/engine/Engine_Rack.cpp`
- [X] T015 [US2] ABI twins (contracts/c-abi.md): `nota_rackdev_add_sampler_chain`, `_set_chain_sampler_sample`, `_add_plugin_instrument_chain`, `_chain_name`, `_set_chain_name`, `_chain_choke/_tune/_decay` + setters, `_swing/_humanize` + setters, `_chain_sampler_info`, `_set_chain_sampler_root`, `_chain_sampler_play_position`, `_chain_instrument_param_id`, `_param_default`, `_scope`, `_voice_count`, `_held_notes`, `_action`; `#define NOTA_RACK_NESTED_BASE (-100000)` in `include/nota/nota_engine.h`, `src/abi/nota_engine_rack.cpp`, export lists
- [X] T016 [P] [US2] Managed interop for T015 as `RackDev*` twins in `IAudioEngine.cs`, `NativeMethods.Rack.cs`, `NotaEngine.Rack.cs`; `RackMacroMapping.NestedRackBase` constant
- [X] T017 [US2] `NestedRackEngineProxy` in `src/managed/Nota.Application/Engine/RackEngineProxies.cs`: `Rack<Name>(t, …)` → `RackDev<Name>(t, nested, …)` by cached reflection, explicit maps for differently-shaped twins (`RackChainZone`, `RackSetChainZone`, `RackTryGetMapping`), `RackDev*(t, -1, …)` → nested, `PluginParam*(t, -1, i)` → nested macros, `TrackInstrumentKind` → 4, automation writes → no-op
- [X] T018 [US2] Open a Drum Rack chain's GUI as the Drum Rack card over the proxy in a `DeviceWindow` (`OpenChainInstrumentGui`, kind 4) in `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs`; in nested mode the Drum card hides swing/humanize and skips MIDI Learn on macros (`RackCardView.Drum.cs`, `RackCardView.cs` `MacroCell`, `RackCard.Macros.cs`/`MacroPanel.cs` where learn is bound)
- [X] T019 [US2] Add "Nota Drum Rack" to the Instrument Rack add-chain and chain instrument menus only (not pads) in `RackCardView.cs`, `RackCard.Chains.cs`
- [X] T020 [US2] Smoke test "racks: nested Drum Rack" (`RackNestingTests.cs`): accept on Instrument Rack, refuse on a Drum Rack pad and deeper; two Sampler pads via the nested address; pad note in zone audible / other note silent; choke/tune/decay round-trip; undo of a nested pad add; blob round-trip keeps pads and audio; outer macro → nested macro → pad decay, in `tests/Nota.SmokeTest/Program.cs`

---

## Phase 5: User Story 3 — MCP / listing (P3)

- [X] T021 [US3] `get_rack` gains `nestedChain` (reads the nested Drum Rack through `NestedRackEngineProxy`); chain kinds 17/4 already show in its chain list, in `src/managed/Nota.Mcp/Tools/RackTools.cs`

---

## Phase 6: Polish

- [X] T022 Headless UI check: chain Mosaic editor and nested Drum Rack card render and edit (scratch harness, not committed)
- [X] T023 [P] CHANGELOG `[Unreleased]` Added entry in `CHANGELOG.md`
- [X] T024 [P] FEATURES.md via the nota-features skill
- [X] T025 [P] nota-docs rack pages EN + RU via the nota-docs skill (`../nota-docs`: devices/racks/instrument-rack, drum-rack; build + check pass, left uncommitted)
- [X] T026 `scripts/build.sh` green
- [X] T027 `/speckit-converge` reports Converged (after T028)

## Dependencies

- T002–T006 before every story. US1 (T007–T013) and US2 (T014–T020) are independent of
  each other after Phase 2, except that T010 (proxy file) precedes T017.
- T026 after all code tasks.

## Parallel examples

- T009 ∥ T008 once T007's signatures are fixed; T016 ∥ T015.
- T023 ∥ T024 ∥ T025.

## Implementation strategy

MVP = Phase 2 + US1 (Mosaic chains). Then US2 (nesting), US3, polish.

## Nota Compliance Re-check

- [X] SPDX header on the new `RackEngineProxies.cs`
- [X] No new dependency
- [X] No audio-thread allocation/lock/I/O added (rack snapshot pattern only)
- [X] All new crossings are C ABI functions in `nota_engine.h`
- [X] Smoke test extended (T013, T020)
- [X] CHANGELOG / FEATURES / docs (T023–T025)
- [X] UI reuses existing cards (DESIGN.md)

## Phase 7: Convergence

- [X] T028 Record why Audio Effect / MIDI Effect Rack chains now ignore the instrument kind in `Engine::rackAddChain` and why MCP `add_effect_rack_chain` defaults to −1 (kind 4 used to mean "no instrument" there and is now the Drum Rack) per plan: nesting rule (unrequested)
