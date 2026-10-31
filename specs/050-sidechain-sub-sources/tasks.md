---
description: "Tasks: sidechain from individual pads, voices and chains"
---

# Tasks: Sidechain from individual pads, voices and chains

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/c-abi.md](contracts/c-abi.md),
[contracts/mcp.md](contracts/mcp.md), [quickstart.md](quickstart.md)

**Tests**: requested by the spec (FR-016) — smoke tests in `tests/Nota.SmokeTest/SidechainPartTests.cs`,
run by `scripts/build.sh` and alone with `--sidechain-parts`.

**Path shorthands**: `E/` = `src/native/nota.engine/src/`, `H` = `src/native/nota.engine/include/nota/nota_engine.h`,
`M/` = `src/managed/`, `T/` = `tests/Nota.SmokeTest/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1…US5 from spec.md

---

## Phase 1: Setup

- [X] T001 Add the test file `T/SidechainPartTests.cs` (SPDX header, `public static IEnumerable<(bool, string)> Run()`, empty for now) and register it in `T/Program.cs` both as the `--sidechain-parts` flag (pattern of `--preset-state`, ~L473) and in the full run (~L15203)
- [X] T002 [P] Add a deterministic drum fixture for the tests: reuse an existing short WAV in `T/Fixtures/` or add a ≤ 50 ms kick-like and hat-like clip there (licence: generated in-repo); note its use in a comment at the top of `T/SidechainPartTests.cs` — done without a fixture: the tests generate their sine-burst WAVs at run time (as `DeviceCopyTests` does)

---

## Phase 2: Foundational (blocks every story)

**Purpose**: part identity, part-aware route slots and the per-block tap hand-off — no instrument fills taps yet.

- [X] T003 [P] `E/devices/Device.h` and `E/devices/Instrument.h`: non-virtual `std::atomic<uint64_t> scPart_{0}` with `uint64_t sidechainPart() const` / `void setSidechainPart(uint64_t)` on both base classes (0 = whole track; comment: "part id, see specs/050")
- [X] T004 [P] `E/devices/Instrument.h`: `struct PartTap { uint64_t part; float* pre; float* post; };` and virtuals `int32_t partCount() const {return 0;}`, `uint64_t partId(int32_t) const {return 0;}`, `int32_t partIndex(uint64_t) const {return -1;}`, `std::string partName(int32_t) const {return {};}`, `void setPartTaps(const PartTap*, int32_t) {}` (audio thread; pointers valid for the coming render only; add, never overwrite)
- [X] T005 `E/engine/Engine.h` + `E/engine/Engine.cpp`: `kMaxRoutes` 8 → 16; add `std::atomic<uint64_t> routeSlotPart_[kMaxRoutes]` (init 0); replace `routeSlotForTrack(id)` with `routeSlotFor(int32_t track, uint64_t part)` (keep `routeSlotForTrack(id)` as `routeSlotFor(id, 0)`); readers load track then part with acquire
- [X] T006 `E/engine/Engine_Devices.cpp` `recomputeRouting()`: claim per distinct `(track, part)` from every consumer (`sidechainSourceTrackId()` + `sidechainPart()`, incl. rack children and the monitoring-audio claim with part 0); a part key claims only when the source track's instrument `partIndex(part) >= 0`; write `routeSlotPart_` before `routeSlotTrackId_` (release)
- [X] T007 `E/engine/Engine_Render.cpp`: `feedDeviceKey`, `feedRackChildKeys` and the instrument React path look up `routeSlotFor(src, consumer.sidechainPart())` (React keeps `routeBus_` = post); the `keying` flag (~L2168) is true when the track owns *any* slot (whole or part)
- [X] T008 `E/engine/Engine_Render.cpp` `renderLeafDry` (LeafPath::Instrument, ~L1927): before `renderInstrumentRaw`, build a stack `PartTap taps[kMaxRoutes]` from the slots with this track id and part ≠ 0 (pre → `routeBusPreNext_[s]`, post → `routeBusNext_[s]`) and call `t.instrument->setPartTaps(taps, n)`; call `setPartTaps(nullptr, 0)` after; nothing else when `n == 0`
- [X] T009 `E/engine/Engine_Devices.cpp` + `E/engine/Engine_Rack.cpp`: every place that copies or clears a key copies / clears the part with the track id — device clone (~L698), move-to-track self-key clear (~L893, ~L1093), clipboard (`deviceClipboardSidechain` stays track-based), rack grouping (`Engine_Rack.cpp` ~L215), rack child clone; `setDeviceSidechainSource` / `setInstrumentSidechainSource` (whole-track setters) reset the part to 0
- [X] T010 `E/engine/Engine.h` + `E/engine/Engine_Devices.cpp`: engine methods `setDeviceSidechainKey(track, dev, src, partIndex) → int` (0 ok, -1 bad, -2 no free route: a new distinct `(src, part)` when no slot is free leaves the key unchanged), `deviceSidechainKey(track, dev, &src, &partIndex)` (-1 whole, -2 missing), instrument variants, `sidechainRoutesFree()`, `sidechainRouteInUse(src, partIndex)`, `trackPartCount(track)`, `trackPartName(track, i)` — converting index ↔ id through the source track's instrument
- [X] T011 `H` + `E/abi/nota_engine_device.cpp` (+ `E/abi/nota_engine_track.cpp` for the track part calls): the specs/050 block exactly as in [contracts/c-abi.md](contracts/c-abi.md) except the rack-child pair (T030)
- [X] T012 [P] `M/Nota.Application/Engine/IAudioEngine.cs`, `M/Nota.Infrastructure/Interop/NativeMethods.Devices.cs`, `M/Nota.Infrastructure/Interop/NotaEngine.Devices.cs`: managed members per [data-model.md](data-model.md) (`TrackPartCount`, `TrackPartName`, `SetDeviceSidechainKey → int`, `DeviceSidechainKey → (int Source, int Part)`, instrument variants, `SidechainRoutesFree`, `SidechainRouteInUse`); stubs in every other `IAudioEngine` implementation (`RackEngineProxies.cs`, `RackCardView.cs` proxy switch: no parts, -1)
- [X] T013 Smoke (in `T/SidechainPartTests.cs`): with no part keys, whole-track keys still duck as before (regression guard; copy the existing Compressor sidechain check shape) and `nota_sidechain_route_capacity` = 16

**Checkpoint**: builds; existing smoke tests pass; nothing user-visible yet.

---

## Phase 3: User Story 1 — Duck the bass from just the kick pad (P1) 🎯 MVP

**Goal**: a Compressor keys off one Drum Rack pad, Pre or Post, and the card shows "Drums · Kick pre".

**Independent test**: quickstart.md automated 1–3; manual 1–4.

### Tests

- [X] T014 [P] [US1] `T/SidechainPartTests.cs` `KickPadOnly()`: Drum Rack (kick + hat Sampler pads from the T002 fixture, kick steps 1/3, hat 2/4), bass track with a sustained deterministic clip + Compressor keyed `(drums, kickIndex)`; offline render; per block `DeviceGainReduction` ≥ 3 dB in kick blocks, < 0.1 dB in hat blocks — once Post, once Pre (FR-016, SC-001)
- [X] T015 [P] [US1] `T/SidechainPartTests.cs` `PreVsPost()`: Utility −20 dB on the kick pad → Pre GR within 0.5 dB of before, Post GR lower (FR-005/006)
- [X] T016 [P] [US1] `T/SidechainPartTests.cs` `GhostKick()`: mute the kick pad → drums track peak in kick blocks drops (no kick in the mix) while bass GR stays ≥ 3 dB; solo the hat → same (FR-007a, SC-005a)

### Implementation

- [X] T017 [US1] `E/devices/racks/RackCore.{h,cpp}`: implement `partCount` (chains with an instrument rack kinds only; 0 for Audio Effect / MIDI racks), `partId(i)` = `chains[i].uid`, `partIndex(uid)` via the live snapshot, `partName(i)` = chain name or its instrument's display name — implemented on `RackInstrument` (the Instrument-facing class over RackCore), so Audio Effect / MIDI racks have no parts by construction
- [X] T018 [US1] `E/devices/racks/RackInstrument.h`: store the per-block taps (`const PartTap* taps_ = nullptr; int32_t nTaps_ = 0;` + `setPartTaps` override) and a helper `const PartTap* tapFor(uint64_t uid) const` (linear scan, nullptr when `nTaps_ == 0`)
- [X] T019 [US1] `E/devices/racks/DrumRack.cpp` `render`: per pad `tap = tapFor(c.uid)`; when muted / solo-gated and `tap == nullptr` keep today's `continue`; otherwise render (tapped-but-silent pads keep `meter` at 0 and skip the add into `out`). After `instrument->render` add `scratch_` into `tap->pre`; in the sum loop add `l*env*gL`, `r*env*gR` with pad gain/pan **without** rack volume `rv` into `tap->post`
- [X] T020 [US1] `M/Nota.App/DeviceCards/SidechainSourcePicker.cs` (new, SPDX header): builder returning a ComboBox + `Func<string> SourceName` + sync action for a consumer given get/set delegates; items "Internal" then per track a row (whole track) and indented part rows from `TrackPartCount/Name`; rebuild on `DropDownOpened`; follow external changes (MCP) while closed; name format "Track · Part"; a `-2` set result shows the "No free routes" tooltip; styling per DESIGN.md (reuse the existing combo's sizes / palette)
- [X] T021 [US1] `M/Nota.App/DeviceCards/Bodies/CompressorDeviceBody.cs`: replace the `srcIds`/`srcNames` combo (~L353–L392) with the picker; `SourceName()` and the Listen readout (~L564) show "Drums · Kick pre/post"; picking any source still turns External on

**Checkpoint**: US1 works end to end in the app and in the smoke test.

---

## Phase 4: User Story 2 — The key stays on the same pad (P1)

**Goal**: reorder / add / delete-other / rename / delete+undo / save+reopen keep the key on its pad.

**Independent test**: quickstart.md automated 4, 5, 9; manual 4–5.

### Tests

- [X] T022 [P] [US2] `T/SidechainPartTests.cs` `StableIdentity()`: key the kick, then move the kick pad to index 0, add a pad, delete the hat, rename the kick — after each, `DeviceSidechainKey` part = the kick's new index and the GR pattern of T014 holds (FR-003, SC-003)
- [X] T023 [P] [US2] `T/SidechainPartTests.cs` `DormantKey()`: delete the kick pad → part -2 and GR < 0.1 dB everywhere; `nota_engine_undo` → part = original index and GR back; replace the Drum Rack with a Sampler → part -2 (FR-004)
- [X] T024 [P] [US2] `T/SidechainPartTests.cs` `SaveReopen()`: save a project with a pad key (Pre) and a whole-track key, reopen → both keys with the same part / tap; open a v27 project (and `T/Fixtures/racks-v8`) → keys whole-track, render unchanged (FR-012/013, SC-004)

### Implementation

- [X] T025 [US2] `M/Nota.Infrastructure/Persistence/ProjectModel.cs`: `DeviceDto.SidechainPart`, `InstrumentDto.SidechainPart`, `NestedSidechainDto.Part` — `int`, default `-1` ("part index at save time on the source track's instrument; -1 whole track (specs/050, v28)")
- [X] T026 [US2] `M/Nota.Infrastructure/Persistence/ProjectService.cs`: `CurrentFormatVersion` 27 → 28; save writes part from `DeviceSidechainKey` / `InstrumentSidechainKey` (a `-2` key saves as `SidechainSource = -1`); load keeps the deferred wiring (~L913–L930) and calls `SetDeviceSidechainKey` / `SetInstrumentSidechainKey` with the part; count `-2` results for T048
- [X] T027 [US2] `M/Nota.Infrastructure/Persistence/ProjectMigrations.cs`: v27 → v28 no-op step with a comment (absent `SidechainPart` = -1)
- [X] T028 [US2] `M/Nota.App/DeviceCards/SidechainSourcePicker.cs`: a part key reading -2 shows "Track · (missing)" in the combo and readouts
- [X] T029 [US2] Verify `E/devices/racks/RackCore.cpp` chain reorder / duplicate / remove keep `uid` on survivors and that `duplicateChain` gives the copy a fresh uid (the key stays on the original); fix if not — verified: survivors keep `uid`; `duplicateChain` rebuilds the copy from the blob, so it gets a fresh one (covered by `StableKey`)
- [X] T030 [US2] `H` + `E/abi/nota_engine_rack.cpp` + `E/engine/Engine_Rack.cpp`: `nota_rack_chain_sidechain_part` / `nota_rack_chain_set_sidechain_part` per contracts; managed `RackChainSidechainPart` / `RackChainSetSidechainPart` in `M/Nota.Application/Engine/IAudioEngine.cs`, `M/Nota.Infrastructure/Interop/NativeMethods.RackDev.cs`, `NotaEngine.RackDev.cs`; `ProjectService.NestedLinks` saves `NestedSidechainDto.Part` and load restores it (keyed devices grouped into a rack keep their pad key)

**Checkpoint**: US1 + US2 = the reported use case, shippable.

---

## Phase 5: User Story 3 — Key off a Nota Rhythm voice (P2)

**Independent test**: quickstart.md automated 6 (Rhythm half).

- [X] T031 [P] [US3] `T/SidechainPartTests.cs` `RhythmVoice()`: Rhythm with voice 1 (kick) on 1/3 and voice 5 (hat) on 2/4 → Compressor keyed `(rhythm, 0)` ducks on voice-1 steps only, Pre and Post; voice FX (Utility −20 dB) on voice 1 changes Post only; `TrackPartCount` = 8, names "1 Kick"…
- [X] T032 [US3] `E/devices/instruments/RhythmMachine.{h,cpp}`: part surface (`partCount` 8, `partId(v)` = v+1, `partIndex(id)` = id−1 when 1..8, `partName(v)` = "{v+1} {voice name}"), `setPartTaps` storing the taps; in `render` per chunk write the panned voice output to `tap->pre[(base+j)*2…]` and `vbuf_[v]` after FX (or the same voice output when the voice has no FX) to `tap->post`; untapped voices unchanged

---

## Phase 6: User Story 4 — Key off an Instrument Rack chain (P2)

**Independent test**: quickstart.md automated 6 (rack half).

- [X] T033 [P] [US4] `T/SidechainPartTests.cs` `InstrumentRackChain()`: Instrument Rack with two Sampler chains on disjoint key zones playing alternate beats → key on chain 0 ducks on its beats only; reorder chains → still chain 0's sound; mute chain 0 → still ducks (ghost)
- [X] T034 [US4] `E/devices/racks/RackInstrument.cpp` `render`: same tap / ghost logic as T019 (no decay VCA; post = after inserts × chain gain/pan, without `rv`); a muted MIDI chain that is tapped renders its events instead of only delivering them

---

## Phase 7: User Story 5 — Every sidechain consumer gets sub-sources (P2)

**Independent test**: quickstart.md automated 7–8, manual 6; each consumer keyed off a pad reacts to it only.

### Tests

- [X] T035 [P] [US5] `T/SidechainPartTests.cs` `FluxReact()`: Flux React keyed `(drums, kick)` via `SetInstrumentSidechainKey` → its React reading moves on kick blocks only
- [X] T036 [P] [US5] `T/SidechainPartTests.cs` `RoutePool()`: 16 distinct sources set → the 17th returns -2 and leaves the key unchanged, `SidechainRoutesFree` = 0; a second consumer of an in-use `(track, part)` returns 0; whole-track `SetDeviceSidechainSource` after a part key → part -1
- [X] T037 [P] [US5] `T/SidechainPartTests.cs` `EveryConsumer()`: for kinds Auto Filter, Auto Gain, Ceiling, Dynamic EQ, Prism, Shutter, Vocoder — key off the kick pad and check the key arrives (device scope / reading non-zero in kick blocks, zero in hat blocks)

### Implementation

- [X] T038 [P] [US5] Switch to `SidechainSourcePicker`: `M/Nota.App/DeviceCards/Bodies/AutoFilterDeviceBody.cs`, `AutoGainDeviceBody.cs`, `AutoShiftDeviceBody.cs`, `CeilingDeviceBody.cs`, `DynamicEqDeviceBody.cs`, `PrismDeviceBody.cs`, `ShutterDeviceBody.cs` (incl. its "External sidechain" switch's first-other-track pick, ~L314), `VocoderDeviceBody.cs` — cards keep their own menus: they take rows from `SidechainSourcePicker.Entries` / `MenuRows` (parts read "Drums · Kick"); Auto Shift is left on whole tracks — its key is the source's MIDI notes, which a pad doesn't have
- [X] T039 [P] [US5] `M/Nota.App/DeviceCards/DeviceParamControls.cs` `SidechainSelector` (plug-ins, generic cards) and `M/Nota.App/DeviceCards/PluginCard.cs` use the picker
- [X] T040 [P] [US5] `M/Nota.App/DeviceCards/Instruments/FluxInstrumentCard.cs`: React source via the picker with `SetInstrumentSidechainKey` / `InstrumentSidechainKey`; no Pre/Post control (always Post)
- [X] T041 [US5] `M/Nota.App/DeviceCards/SidechainSourcePicker.cs`: disable rows when `SidechainRoutesFree() == 0 && !SidechainRouteInUse(track, part)` with tooltip "No free routes"; disable a frozen track's part rows (tooltip: "Frozen — parts don't render") — the dropdown greys such rows; the menu-style cards leave them out
- [X] T042 [US5] `M/Nota.Mcp/Tools/DeviceTools.cs`: `get_device_sidechain` + `sourcePart`, `sourceName`; `set_device_sidechain` + optional `sourcePart` (error text on -2); new `list_sidechain_sources`, `get_instrument_sidechain`, `set_instrument_sidechain` per [contracts/mcp.md](contracts/mcp.md); descriptions name the kick-pad recipe

---

## Phase 8: Polish & cross-cutting

- [X] T043 [P] `CHANGELOG.md` `[Unreleased]` → Added: sidechain keys from a single Drum Rack pad, Instrument Rack chain or Nota Rhythm voice, Pre/Post, ghost parts; Changed: 16 sidechain routes
- [X] T044 [P] `FEATURES.md` via the `nota-features` skill
- [X] T045 [P] User manual EN + RU (`../nota-docs`) via the `nota-docs` skill: sidechain picker, ghost kick recipe; re-shoot the Compressor Sidechain tab — Devices (Sidechain), Compressor, Drum Rack, Flux pages EN + RU in ../nota-docs (uncommitted); the Compressor shot is visually unchanged, not retaken
- [X] T046 Engine perf gate (specs/034) with no part keys and with 4 pad keys — record numbers in the PR (FR-011, SC-005) — the Callgrind gate needs Linux + valgrind (runs in CI on the PR); locally `scripts/bench-engine.sh` before/after on an M4 Max: no change beyond run-order noise (the 16-synth export swing follows whichever build runs second). A third run caught late blocks at 192 tracks · 64-sample buffers that didn't flip with the order: the route-slot scans are now bounded by the used count (research R6), after which the PR is no worse than develop (build/pr-050/issue-190)
- [X] T047 Re-check the Nota Compliance Checklist in [spec.md](spec.md): SPDX headers on `SidechainPartTests.cs` and `SidechainSourcePicker.cs`; no audio-thread allocation in T008/T019/T032/T034
- [X] T048 Project load: when T026 counted `-2` keys, show one notice ("Some sidechain keys were cleared: no free routes") through the existing load-warnings path in `M/Nota.Infrastructure/Persistence/ProjectService.cs`
- [X] T049 `scripts/build.sh` green (smoke incl. `--sidechain-parts`); macOS manual pass of quickstart.md "Manual" with before/after screenshots for the PR (`nota-pr` skill) — build.sh green (SMOKE TEST PASSED, SCAN SCALE PASSED); the picker checked in a headless render (Compressor Sidechain tab + source list, dark / light; build/pr-050), which led to the closed box naming the track ("Drums · Kick"). Not driven in the live app
- [X] T050 `/speckit-converge` → Converged

---

## Dependencies

```text
Setup (T001–T002) → Foundational (T003–T013) → US1 (T014–T021)
US1 → US2 (needs pad keys)            US1 → US4 (shares RackCore / tap helper T017–T018)
Foundational → US3 (Rhythm is independent of racks)
US1 (picker T020) → US5 (T038–T041); T010/T011 → T035–T036, T042
US2 T026 → T048;  all stories → Polish
```

Inside Foundational: T003, T004 ∥ → T005 → T006 → T007, T008 → T009 → T010 → T011 → T012 → T013.

## Parallel examples

- Foundational: T003 ∥ T004; T012 ∥ T013 once T011 is in.
- US1: tests T014 ∥ T015 ∥ T016 (same file, distinct methods — merge-friendly if written by one hand; otherwise sequence), engine T017 → T018 → T019 ∥ UI T020 → T021.
- US3 (T031–T032) can run alongside US1 once Foundational is done.
- US5: T038 ∥ T039 ∥ T040 (different files), then T041; T042 any time after T011.
- Polish: T043 ∥ T044 ∥ T045.

## Implementation strategy

1. **MVP = Foundational + US1 + US2** — the reported "duck the bass from the kick pad" with stable keys and saved projects.
2. Then US3 and US4 (small: one render loop each, reusing the foundation).
3. Then US5 (UI sweep + MCP) and Polish.
4. Commit per phase in the repo's style; the user makes the commits and the PR.

## Phase 9: Convergence

- [X] T051 Show full-pool sources as disabled rows with the "No free routes" hint in the menu-style pickers (Auto Gain, Ceiling, Dynamic EQ, Prism, Shutter, Vocoder, Flux React) instead of leaving them out — `SidechainSourcePicker.MenuRows` and the cards' menu builders in `src/managed/Nota.App/DeviceCards/` per FR-007c (partial)
- [X] T052 Cover the remaining edge cases in `tests/Nota.SmokeTest/SidechainPartTests.cs`: a frozen source track's part key is silent while its whole-track key still works; duplicating the consuming track and copy / paste of the keyed device keep the part key per spec Edge Cases (Frozen source track, Clipboard / duplicate) (partial)
