---

description: "Task list for multi-select devices and group them into a rack"
---

# Tasks: Multi-select devices and group them into a rack

**Input**: Design documents from `specs/044-device-multiselect-group/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: The constitution (IV) requires smoke coverage for engine behaviour changes and
every new C ABI call, so smoke tasks are included.

**Organization**: Grouped by user story; the nested address (Phase 2) and the engine group /
ungroup (Phase 3) block every story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 audio-effect group, US2 multi-select, US3 Instrument Rack, US4 MIDI Effect Rack

## Path Conventions

- Engine: `src/native/nota.engine/` (`E/` below = `src/native/nota.engine/src/engine/`)
- C ABI: `src/native/nota.engine/include/nota/nota_engine.h`, wrappers in `src/native/nota.engine/src/abi/`
- Managed: `src/managed/<Project>/`; Smoke: `tests/Nota.SmokeTest/`

---

## Phase 1: Setup

- [X] T001 Create `src/native/nota.engine/src/engine/ParamPath.h` (SPDX header) with `struct ParamPath { int32_t host = -1; uint64_t chainUid = 0; int32_t slot = -1; }`, `isNested()`, equality; add it to the engine target in `src/native/nota.engine/CMakeLists.txt` if headers are listed

---

## Phase 2: Foundational — the nested parameter address (R1, R2, R4, R5, R6, R7)

**Purpose**: Let automation, CV links, Arp CC routes and sidechain keys point at a device
inside a rack chain. No behaviour changes until Group creates such paths.

- [X] T002 Add `RackCore` audio-thread-safe lookups in `src/native/nota.engine/src/devices/racks/RackCore.h`: `chainIndexOfUid(uint64)`, `childDevice(uint64 chainUid, int32 slot)` → `Device*`, `childMidi(uint64, slot)` → `MidiDevice*`, `childInstrument(uint64)` → `Instrument*`, all reading `live()` without allocation; plus `chainUidAt(int32 index)` for the message thread
- [X] T003 Add `Engine::resolveParamOwner(Track&, host, chainUid, slot, Device*&, MidiDevice*&, Instrument*&)` in `E/Engine_Automation.cpp` (declared in `E/Engine.h`): chainUid 0 = today's lookup (host ≥ 0 audio effect, −1 instrument, MIDI per target kind); otherwise the rack at `host` (`RackDevice`, `RackInstrument` at −1, or `MidiRack` among MIDI effects) and its child per T002
- [X] T004 Add `chainUid` + `slot` to `AutomationLane` in `E/Automation.h`; route `applyAutomation` in `E/Engine_Automation.cpp` through `resolveParamOwner` for DeviceParam / MidiDeviceParam / PluginParam lanes when `chainUid != 0`
- [X] T005 Add `sourceChainUid/sourceSlot` and `targetChainUid/targetSlot` to `CvLink` in `E/Modulation.h` (copy-assignment in `Modulation.cpp` too); make `targetValid/targetGet/targetSet` and the Param-source read in `applyModulation` (`E/Engine_Modulation.cpp`) resolve nested paths
- [X] T006 Move the Arp CC destination into the snapshot: add `struct CcRoute { int32_t arpHost; uint64_t arpChainUid; int32_t arpSlot; ParamPath target; int32_t param; }` and `std::vector<CcRoute> ccRoutes` on `Track` (`E/Track.h`, cloned with the track); rewrite `applyMidiCcRouting` in `E/Engine_Render.cpp` to iterate routes, resolving the Arp (track MIDI effect, or a MIDI effect in a `MidiRack` / Instrument Rack chain) and the target; keep `MidiDevice::ccValue/ccDepth` as the source
- [X] T007 Re-implement the existing Arp CC set/get engine methods (`setMidiEffectCcDest` and getters behind `nota_*_cc_dest*` in `src/native/nota.engine/src/abi/` and `E/Engine_Devices.cpp`) on `Track::ccRoutes` with an undo checkpoint; drop `MidiDevice::ccDestDevice/setCcDest` use in the engine (`src/native/nota.engine/src/devices/MidiDevice.h` keeps the virtuals only if devices still need them)
- [X] T008 Make MIDI-effect and device remaps keep `ccRoutes` valid: in `remapAutomationAfterDeviceChange` (`E/Engine_Automation.cpp`) or a sibling `remapCcRoutesAfterDeviceChange`, shift / drop routes whose Arp host (midi=true) or target host (midi=false) moved or was removed; call it everywhere `remapAutomationAfterDeviceChange` is called (`E/Engine_Devices.cpp`, `E/Engine_Rack.cpp`)
- [X] T009 Replace `Engine::deviceIsReferenced` users: `deviceIsReferenced` reads `ccRoutes` instead of `md->ccDestDevice()` (`E/Engine_Rack.cpp`)
- [X] T010 Sidechain inside racks (R5): add `RackCore::forEachChild(fn)` over `live()` chains (devices, MIDI, instrument); in `E/Engine_Render.cpp` (`processLeafDevices`, the `LeafPath::Instrument` branch and the two other sidechain feeds ~2428 / ~2501) feed keyed children of `RackDevice` devices and of a `RackInstrument` instrument; extend the route-bus claims in `E/Engine_Devices.cpp` (~1370) to claim children's sources
- [X] T011 Rack-edit consistency (R6): add `Engine::remapPathsAfterRackEdit(Track& nt, int32 host, uint64 chainUid, int32 removedSlot, int32 fromSlot, int32 toSlot, bool chainRemoved, bool midiSlot)` in `E/Engine_Rack.cpp` that drops / shifts nested lanes, CV links and CC routes; call it from rack chain delete, chain-device delete / move / cross-chain move (specs/042 paths in `E/Engine_Rack.cpp`), and from replacing the rack's chain instrument
- [X] T012 C ABI for nested lane / link / route addresses in `include/nota/nota_engine.h` + `src/abi/nota_engine_automation.cpp` (and the CV / CC abi files): `nota_track_automation_lane_path`, `nota_track_add_nested_automation_lane`, CV-link path get/set, CC-route path get/set (chain index ↔ uid conversion via T002) — signatures per [contracts/c-abi.md](contracts/c-abi.md)
- [X] T013 [P] Managed interop for T012: `src/managed/Nota.Infrastructure/Interop/NativeMethods.Automation.cs`, `NotaEngine.Automation.cs` (+ CV / devices files), `src/managed/Nota.Application/Engine/IAudioEngine.cs`, `EngineContracts.cs`
- [X] T014 Persistence (R7): `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs` — `AutomationLaneDto.Chain/Slot`, CV DTO `SourceChain/SourceSlot/TargetChain/TargetSlot`, track-level `CcRoutes` list (`ArpHost, ArpChain, ArpSlot, TargetHost, TargetChain, TargetSlot, Param`); `ProjectService.cs` saves them (chain **index**), loads them after devices are rebuilt, reads the legacy per-MIDI-effect `CcDestDevice/CcDestParam` when `CcRoutes` is absent; bump the format version with a comment
- [X] T015 Smoke: `tests/Nota.SmokeTest/DeviceGroupTests.cs` (SPDX) part 1 — an existing project with an Arp CC lane loads and still drives its target; undo / redo of a CC destination change restore it (the route is in the snapshot); register `--device-group` in `tests/Nota.SmokeTest/Program.cs` and add the class to the full run

**Checkpoint**: Build + smoke green; nothing visible changed.

---

## Phase 3: Foundational — engine Group / Ungroup and the multi-device clipboard (R3, R8)

- [X] T016 `RackCore::setChainInstrumentInstance(chain, std::shared_ptr<Instrument>, bool prepare=false)` in `src/native/nota.engine/src/devices/racks/RackCore.h/.cpp` (bumps the chain's instrument generation like `setChainInstrument`)
- [X] T017 `Engine::groupDevices(trackId, midiFirst, midiCount, withInstrument, fxFirst, fxCount)` in `E/Engine_Rack.cpp`: validate (codes −2…−7 of contracts/c-abi.md: contiguity, "MIDI run ends at last MIDI effect / audio run starts at 0" with the instrument, no rack in the run, no plug-in into a `MidiRack`, instrument not an Instrument Rack, instrument track), build the rack off-graph moving instances with `prepare=false`, then in one cloned track: replace the run, rewrite lanes / CV links / CC routes / (sidechain needs nothing) to nested paths, shift indices of later devices (`remapAutomationAfterDeviceChange`, CC routes), publish once (`republishWithTrack`), then fix CV links (`remapCvLinksAfterDeviceChange` generalised to rewrite into paths), `recomputePdc()`
- [X] T018 Instrument branch of T017: new `RackInstrument` with chain 0 = MIDI effects (`addChainMidiDeviceInstance`), instrument (T016), audio effects (`addChainDeviceInstance`, prepare=false); swap via the `replaceTrackInstrument` internals inside the same publish; `RackInstrument::adoptUid` not used (new device)
- [X] T019 `Engine::ungroupRack(trackId, family, index, int32_t* outDropped)` in `E/Engine_Rack.cpp`: one-chain `RackDevice` / `MidiRack` / `RackInstrument` (not `DrumRack`), splice the chain's instances back (instrument via the replace internals), rewrite nested paths to track-level, shift later indices, report macro-mapping / non-default chain-mix drops, publish once
- [X] T020 Rewrite `wrapDeviceInRack` / `unwrapRack` as wrappers over T017 / T019 (no more `deviceIsReferenced` refusal) in `E/Engine_Rack.cpp`
- [X] T021 Multi-device clipboard in `E/Engine_Devices.cpp` + `E/Engine.h`: `std::vector<ClipEntry> clip_` replacing `clipDevice_ / clipMidi_ / clipInstrument_` (single-copy calls fill one entry); `copyDevices(track, midiIdx, withInstrument, fxIdx)`, `clipboardCount()`, `clipboardKind()` returns 4 for several; `pasteDevices(track, midiAt, fxAt, int* skipped)` clones all first (−3 on any failure, nothing changed), then inserts MIDI effects / replaces the instrument / inserts audio effects in one cloned track and one publish; rack-chain paste (specs/042) takes the fitting entries
- [X] T022 C ABI: `nota_track_group_devices`, `nota_track_ungroup_rack`, `nota_track_copy_devices`, `nota_device_clipboard_count`, `nota_track_paste_devices` in `include/nota/nota_engine.h` + `src/abi/nota_engine_device.cpp`; update the comment on `wrap/unwrap` and `clipboard_kind`
- [X] T023 [P] Managed interop for T022: `NativeMethods.Devices.cs`, `NotaEngine.Devices.cs`, `IAudioEngine.cs` (`GroupDevices`, `UngroupRack`, `CopyDevices`, `DeviceClipboardCount`, `PasteDevices`) and any test doubles implementing `IAudioEngine`
- [X] T024 Smoke `DeviceGroupTests.cs` part 2: for each rack type — group, render a bar before / after (peak & RMS within 0.1 dB), rack contents and params identical, undo restores, ungroup restores; each refusal code; an automated / CV-linked / keyed / Arp-CC-driven device keeps acting after group and after ungroup; save → reload of a grouped project with a nested lane; multi copy / paste incl. instrument replace and skips on an audio track; update `DeviceCopyTests.cs` expectations that relied on the old wrap refusal

**Checkpoint**: Engine complete; every new ABI call covered by smoke.

---

## Phase 4: User Story 1 — group audio effects into an Audio Effect Rack (P1) 🎯 MVP

**Goal**: ⌘G / menu groups one or a contiguous range of audio effects; ⌘⇧G ungroups.
**Independent test**: quickstart step 1.

- [X] T025 [US1] `CardExtra.Wrapped` → `List<CardExtra>? Wrapped` in `src/managed/Nota.App/DeviceChainView.Shell.cs` (R10); keep the single-device restore path working through the list
- [X] T026 [US1] Rewrite `GroupSelectedDevice` / `UngroupSelectedDevice` in `src/managed/Nota.App/DeviceChainView.Clipboard.cs` on `GroupDevices` / `UngroupRack`: extras moved into / out of the rack's `Wrapped` list, indices after the run shifted (`ExtrasMoved` / `ExtrasRemoved`), the new rack (or restored devices) selected, status lines and refusal messages per [contracts/ui.md](contracts/ui.md)

---

## Phase 5: User Story 2 — multi-select and batch actions (P1)

**Goal**: Shift-click / ⌘-click selection across MIDI effects, instrument and audio effects;
batch Copy / Cut / Paste / Delete / Duplicate / drag / Move to track ▸; Group on a range.
**Independent test**: quickstart steps 3–4.

- [X] T027 [P] [US2] `src/managed/Nota.App/DeviceSelection.cs` (SPDX): ordered members of `(ChainKind, index)` + anchor; `Click`, `ShiftClick`, `ToggleClick`, `Clear`, `Contains`, chain-order ranges given counts (MIDI 0..m−1 → instrument → audio 0..n−1); `GroupShape(midiCount, fxCount)` → AudioRack / MidiRack / InstrumentRack / Refused(reason) per [data-model.md](data-model.md) § Group shape; `Remap(kind, Func<int,int>)` after edits
- [X] T028 [P] [US2] Smoke `tests/Nota.SmokeTest/DeviceSelectionTests.cs` (SPDX): ranges across sections, toggles, anchor, every GroupShape row incl. refusals; run under `--device-group`
- [X] T029 [US2] Replace `_selChainKind / _selDeviceIndex / _instrumentSelected` with a `DeviceSelection` in `DeviceChainView.Clipboard.cs`; `HasDeviceSelection` / `HasEffectSelection` / `SelectDevice` / `ClearDeviceSelection` on top of it; card borders from `Contains` in `DeviceChainView.Shell.cs`, `DeviceChainView.Instruments.cs`, `DeviceChainView.Midi.cs`, `DeviceChainView.Fold.cs`
- [X] T030 [US2] Click modifiers: plain / Shift / ⌘ (Ctrl on Windows / Linux) on card headers and bodies in the same files; Esc and empty-space click clear; right-click on an unselected card selects only it
- [X] T031 [US2] Batch Copy / Cut / Paste via `CopyDevices` / `PasteDevices` (status "Pasted N, skipped M"); Delete / Cut / Duplicate over the selection inside `UndoGroupBegin/End`, highest index first, extras remapped; confirmation dialog when the selection holds the instrument and another device (always) in `DeviceChainView.Clipboard.cs`
- [X] T032 [US2] Drag a selected card's header: move every selected card of that section to the drop gap in order, inside one undo group, in `DeviceChainView.Clipboard.cs` (drag section) / `DeviceChainView.Move.cs`
- [X] T033 [US2] Move to track ▸ for a selection in `DeviceChainView.Move.cs`: every selected device via `TransferDevice` inside one undo group, the instrument last with the specs/043 confirmation (always when more than the instrument is selected)
- [X] T034 [US2] Context menu for a selection: "N devices" header, Save preset only for one device, Group into rack enabled from `GroupShape`, Ungroup rack on a one-chain rack of any type, Delete / Delete track; ⌘G on a range calls Group with the run (`DeviceChainView.Clipboard.cs`)

---

## Phase 6: User Story 3 — Instrument Rack (P2)

**Independent test**: quickstart step 5.

- [X] T035 [US3] Group with the instrument from the UI: the `InstrumentRack` shape calls `GroupDevices(…, withInstrument: true, …)`; extras of the instrument (key −1) and the grouped effects into the new instrument card's `Wrapped`; the rack card is selected and unfolded (`DeviceChainView.Clipboard.cs`, `DeviceChainView.Instruments.cs`)
- [X] T036 [US3] Ungroup on a one-chain Instrument Rack instrument (menu + ⌘⇧G): `UngroupRack(family 2)`; restored MIDI effects / instrument / audio effects selected with their extras

---

## Phase 7: User Story 4 — MIDI Effect Rack (P3)

**Independent test**: quickstart step 6.

- [X] T037 [US4] Group / Ungroup for MIDI-effect runs and `MidiRack` cards in `DeviceChainView.Clipboard.cs` / `DeviceChainView.Midi.cs` (extras keyed by `ExtraKey(Midi, i)`)

---

## Phase 8: Polish & cross-cutting

- [X] T038 [P] Nested lane titles in `src/managed/Nota.App/ArrangementView.Automation.cs` ("‹Rack› › ‹Device› · ‹Param›") from `AutomationLanePath` and the rack chain device / param name calls
- [X] T039 [P] MCP `group_devices` and `ungroup_rack` in `src/managed/Nota.Mcp/Tools/DeviceTools.cs` per [contracts/mcp.md](contracts/mcp.md)
- [X] T040 [P] Preferences → Shortcuts: Shift-click, ⌘/Ctrl-click, ⌘G, ⌘⇧G in the Devices panel (nota-shortcuts skill)
- [X] T041 `CHANGELOG.md` `[Unreleased]` Added entry; FEATURES.md (nota-features skill); user docs (nota-docs skill)
- [X] T042 `scripts/build.sh` green (smoke incl. `--device-group`, perf gate); run quickstart in the app with `NOTA_DATA_DIR`, all four themes

---

## Implementation notes (deviations, 2026-10-27)

- T006–T008: the Arp's CC target stays on the Arp, gaining a nested (chain uid, slot) part; a
  rewrite (group, ungroup, an edit inside a rack) replaces the Arp with a clone, the existing
  undo-safe pattern of the device-removal path, instead of moving routes into `Track`. Saved
  per MIDI effect (`CcDestChain/CcDestSlot`) and, for Arps inside racks, in the track's
  `NestedCcRoutes`; chain children's sidechain keys in `NestedSidechains` (v25).
- T021: a rack chain's Paste of several devices came with T045 (`nota_rackdev_chain_paste_devices`).
- T025: `CardExtra.Wrapped` stays for the one-device case (undo / redo swaps it, as before); a
  multi-device group keeps the extras in `WrappedMany`, restored by Ungroup and (T046) by Undo.
- T043: Arps inside racks (a MIDI Effect Rack, Instrument Rack chains) get their CC target
  rewritten too: the rack is forked with a clone of the Arp (`rewriteRackArps`).
- T027: `DeviceSelection` lives in `Nota.Presentation` (the smoke test doesn't reference
  `Nota.App`); its section enum is `DeviceSection` (`ChainSection` is the palette's).
- T033: multi-device drags out of the panel onto a track header still carry one device.
- T041: user docs (nota-docs, the sibling repo) not updated yet.

## Dependencies & Execution Order

- Phase 1 → Phase 2 → Phase 3 → user stories. Phase 2's T002–T003 first; T004–T011 depend on
  them; T012–T014 after T004–T008; T015 last.
- US1 (Phase 4) needs Phase 3. US2 needs Phase 3 (clipboard) and builds on US1's Group code.
  US3 and US4 need US2's selection (T027–T030).
- Polish after the stories; T038–T040 can run in parallel.

### Parallel opportunities

- T013 with T014 (different managed files) once T012's signatures are fixed.
- T023 with T024 once T022 is in.
- T027 + T028 (new files) alongside T025–T026.
- T038, T039, T040 together.

## Implementation Strategy

MVP = Phases 1–4 (nested address, engine group / ungroup, ⌘G on audio-effect runs): the most
common case (#328's headline) works with links travelling. Then US2 (multi-select and batch
actions), US3, US4, polish. Each phase ends with a build + smoke run.

---

## Phase 9: Convergence

- [X] T043 Extend `tests/Nota.SmokeTest/DeviceGroupTests.cs` to call `nota_rack_chain_midi_cc_dest` / `nota_rack_chain_midi_set_cc_dest`: group an Arp whose CC lane drives an audio effect into a MIDI Effect Rack, check its CC target and that it still drives the effect after a render, then save → reload and check `NestedCcRoutes` brings it back per Constitution IV (partial)
- [X] T044 Name the new rack's chain after its first device (the instrument for an Instrument Rack) in `groupDevices`, `src/native/nota.engine/src/engine/Engine_Rack.cpp`, and assert the name in `DeviceGroupTests.cs` per FR-015 (partial)
- [X] T045 Let a rack chain's Paste take a multi-device clipboard: the entries that fit the chain's sections, the instrument skipped, one undo step (`rackChainDevicePaste` / `rackChainMidiPaste` in `src/native/nota.engine/src/engine/Engine_Rack.cpp`), with a smoke check per FR-005 / spec Edge Cases (partial)
- [X] T046 After Undo / Redo of a multi-device Group, give the restored cards their A/B snapshots and preset labels back from the rack card's `WrappedMany` (match by signature in `ExtraFor`, `src/managed/Nota.App/DeviceChainView.Shell.cs`) per FR-013 / FR-020 (partial)
- [X] T047 Add render checks for the MIDI Effect Rack and Instrument Rack groups (same notes / level within 0.1 dB against an ungrouped twin engine, a deterministic instrument such as Sampler) in `DeviceGroupTests.cs` per SC-003 (partial)

---

## Phase 10: Convergence

- [ ] T048 After ⌘Z / ⌘⇧Z of a device edit made in the Devices panel (group, ungroup, a batch delete or paste), select the devices the edit is undone back to, instead of letting the selection fall away (`src/managed/Nota.App/DeviceChainView.Clipboard.cs`) per FR-020 (partial)
