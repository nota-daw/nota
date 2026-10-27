---

description: "Tasks for spec 042: move, copy and paste devices in rack chains"
---

# Tasks: Move, copy and paste devices in rack chains

**Input**: Design documents from `specs/042-rack-device-clipboard/`:
- [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md);
- the contracts [c-abi.md](contracts/c-abi.md) and [ui.md](contracts/ui.md);
- [quickstart.md](quickstart.md).

**Tests**: The smoke test is the project's gate (constitution IV), and the plan asks for a section `racks: chain device clipboard`. So each story gets smoke tasks, written before its implementation where the engine is involved.

**Organization**: by user story: US1 reorder and move between chains (P1), US2 the clipboard inside racks (P1), US3 across the track and rack chains (P2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 / US2 / US3

---

## Phase 1: Setup

- [X] T001 Create `tests/Nota.SmokeTest/RackClipboardTests.cs`:
  - AGPL-3.0-only SPDX two-line header;
  - an `internal static class RackClipboardTests` with `Sections()` yielding `("racks: chain device clipboard", …)`, following `RackChainMidiTests.cs`;
  - register it in the `--racks` list in `tests/Nota.SmokeTest/Program.cs` (`RackSections()`).
- [X] T002 [P] Add `MenuKit.DuplicateKey` (`Cmd(Key.D)`) next to `CopyKey` / `CutKey` / `PasteKey` in `src/managed/Nota.App/MenuKit.cs`.

---

## Phase 2: Foundational (blocks US1 cross-chain, US2, US3)

**Purpose**: the engine primitives and the managed selection every story uses.

- [X] T003 In `src/native/nota.engine/src/devices/racks/RackCore.h/.cpp`, add `int32_t insertChainDeviceInstance(int32_t chain, std::shared_ptr<Device> dev, int32_t at, bool prepare = true)` and `int32_t insertChainMidiDeviceInstance(int32_t chain, std::shared_ptr<MidiDevice> dev, int32_t at)`.
  - Each is one `copyState` / `commit`, and returns the new index.
  - Insert at `at`; −1 or past the end means the end.
  - Shift that chain's mappings at or after `at` up by one: for audio, `deviceIndex >= at`; for MIDI, `isMidiTarget` with `midiTargetIndex >= at`, rewritten with `midiTarget(j+1)`.
  - The MIDI variant refuses kinds outside 0..5, like `addChainMidiDeviceInstance`.
- [X] T004 In `RackCore.h/.cpp`, add `int32_t moveChainDeviceToChain(int32_t chain, int32_t idx, int32_t toChain)` and `int32_t moveChainMidiDeviceToChain(…)`.
  - Each is one fork: erase from `chain`, append to `toChain`'s matching section.
  - The device's mappings are rewritten to `(toChain, newIndex)`; `chain`'s mappings after `idx` shift down.
  - Returns the new index, or −1 for a bad address or `toChain == chain`.
- [X] T005 In `src/native/nota.engine/src/engine/Engine.h` and `Engine_Rack.cpp`, add these on top of `rackCoreAt` / `rackForEdit` and the shared clipboard `clipDevice_` / `clipMidi_`:
  - `rackChainDeviceCopy` / `rackChainMidiCopy` set the clipboard to `cloneDevice` / `cloneMidiDevice` of the chain device, and clear the other slot.
  - `rackChainDevicePaste(t, di, c, at)` / `rackChainMidiPaste` return codes per data-model.md:
    - −1: nothing to paste or a bad address;
    - −2: the wrong family, or the rack has no such section — a MIDI Effect Rack has no audio effects; an Audio Effect Rack and a Drum Rack pad have no MIDI effects;
    - −3: a `RackDevice` or `MidiRack` clone;
    - −4: a hosted plug-in where `!chainsHostPlugin`-style hosting isn't allowed (match `IRackAccess.HostsPlugins` per rack kind);
    - −5: MIDI past `RackInstrument::kMaxMidiChains` for `di == -1`.
  - An audio clone gets `setSidechainSourceTrackId(-1)` before the insert.
  - `rackChainDeviceDuplicate` / `rackChainMidiDuplicate` insert a clone at idx+1 without touching the clipboard.
  - `rackChainDeviceMoveTo` / `rackChainMidiMoveTo` keep the 16-chain rule for MIDI in Instrument Racks.
  - After any insert into an Audio Effect Rack, call `recomputePdc()` as the existing chain-device add does.
- [X] T006 Add `bool deviceClipboardSidechain() const` in `src/native/nota.engine/src/engine/Engine_Devices.cpp` and `Engine.h`: true when `clipDevice_` has `sidechainSourceTrackId() >= 0`.
- [X] T007 Add the C ABI functions exactly as in contracts/c-abi.md, with comments:
  - declare them in `src/native/nota.engine/include/nota/nota_engine.h`;
  - implement them in `src/native/nota.engine/src/abi/nota_engine_rack.cpp` (`nota_rackdev_chain_*`) and `src/native/nota.engine/src/abi/nota_engine_device.cpp` (`nota_device_clipboard_sidechain`).
- [X] T008 [P] Add the P/Invoke declarations and wrappers listed in contracts/c-abi.md ("Managed mirror") in:
  - `src/managed/Nota.Infrastructure/Interop/NativeMethods.Rack.cs`;
  - `src/managed/Nota.Infrastructure/Interop/NotaEngine.Rack.cs`;
  - `src/managed/Nota.Application/Engine/IAudioEngine.cs`.
- [X] T009 Extend `IRackAccess` in `src/managed/Nota.App/DeviceCards/Rack/IRackAccess.cs`:
  - add default members `CopyChainDevice` / `CopyChainMidi`, `PasteChainDevice` / `PasteChainMidi` (`int at`), `DuplicateChainDevice` / `DuplicateChainMidi`, `MoveChainDeviceTo` / `MoveChainMidiTo` (default −1/false);
  - implement them for `InstrumentRackAccess` (di −1), `EffectRackAccess` (di) and `MidiRackAccess` (−1000 − mi, MIDI only);
  - the Rhythm / Drum accesses keep the defaults unless their pad strip already shows effects.
- [X] T010 Add the rack device selection to `src/managed/Nota.App/DeviceCards/DeviceCardContext.cs` and `src/managed/Nota.App/DeviceChainView.cs` / `DeviceChainView.Clipboard.cs`:
  - a `RackDeviceSel(int Rack, int Chain, bool Midi, int Index)` record;
  - `DeviceCardContext.RackSelection` (get) and `SelectRackDevice(RackDeviceSel?)`;
  - in `DeviceChainView`, setting a rack selection clears `_selDeviceIndex`, and selecting a track device clears the rack selection (FR-005);
  - `Show(track)` clears both.

**Checkpoint**: the engine and interop build, and `scripts/build.sh` passes with no behaviour change.

---

## Phase 3: User Story 1 — Reorder and move a chain's effects (P1) 🎯 MVP

**Goal**: drag by the header with a drop bar inside a section; drop on another chain's row to move there; **Move left** / **Move right** in the menu.

**Independent Test**: quickstart Q1 + Q2; the smoke checks below.

### Smoke for US1

- [X] T011 [US1] In `tests/Nota.SmokeTest/RackClipboardTests.cs`:
  - Audio Effect Rack chain 0 = EQ-3, Compressor, Reverb. Move Reverb to 0, check the order, check that Undo restores it. Map a macro to the Compressor's Threshold, move the Compressor, and check that the macro still drives it.
  - Move the Compressor to chain 1 with `RackDevChainDeviceMoveTo`. Check it is at the end of chain 1 with its params, that the macro still drives it, and that one Undo puts it back in chain 0.
  - The same for a MIDI effect between Instrument Rack chains. A move into chain 16 returns −5.

### Implementation for US1

- [X] T012 [US1] Replace `RcGripReorder` with header drag in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Chains.cs`, modelled on `DeviceChainView.HookDeviceDrag`.
  - On header press, capture; after 5 px, dim the card to 0.55 and show a 3-px `NotaPalette.AccentBright` drop bar at the gap among the same section's cards in the lane strip (tag the cards with `(chain, slot, idx)`).
  - On release, `MoveChainDevice` / `MoveChainMidi` to the gap (`to = gap > from ? gap - 1 : gap`), keep the device selected, then `DeferRebuild()`.
  - The grip glyph starts the same drag.
  - The instrument card never drags.
- [X] T013 [US1] Add chain-row drop for a dragged lane device in `RackCard.Chains.cs`.
  - While a lane drag is active, the pointer over a chain row (hit-test the chain list rows) shows that row's drop outline (`NotaPalette.AccentBright` border, as for Browser drops) only if the chain can take it.
  - "Can take it" means a different chain; a MIDI effect needs `_rp.TakesMidiEffects`, and for the Instrument Rack chain index < 16.
  - On release over the row, call `MoveChainDeviceTo` / `MoveChainMidiTo`, set `Sel` to that chain, select the moved device, and `DeferRebuild()`.
- [X] T014 [US1] Add **Move left** / **Move right** (`GlyphKind.StepLeft` / `StepRight`, disabled at the ends) to `RcCardMenu` in `RackCard.Chains.cs` for effect and MIDI cards, calling `MoveChainDevice` / `MoveChainMidi`.

**Checkpoint**: US1 works on its own: quickstart Q1, Q2, and T011 passing.

---

## Phase 4: User Story 2 — Copy, cut, paste, duplicate and delete inside racks (P1)

**Goal**: rack chain devices can be selected, with Copy / Cut / Paste / Duplicate / Delete in the menu and on ⌘C / ⌘X / ⌘V / ⌘D / Delete.

**Independent Test**: quickstart Q3 + Q5; the smoke checks below.

### Smoke for US2

- [X] T015 [US2] In `RackClipboardTests.cs`:
  - Copy chain 0's Reverb with `RackDevChainDeviceCopy`, then `RackDevChainDevicePaste` into chain 1 at −1. Check the params are equal and the copy has no mappings.
  - Paste at 0 lands first and shifts chain 1's mapping indices. Check with a mapping on chain 1's first device.
  - Duplicate lands at idx+1 and leaves the clipboard kind unchanged.
  - Each step is one Undo.
  - Refusals: −2 for a MIDI effect into an Audio Effect Rack chain, −3 for a rack, −5 for MIDI into Instrument Rack chain 16.
  - A MIDI rack: copy and paste an Arp between its chains, comparing params and `MidiEffectGetState`-equivalent params.

### Implementation for US2

- [X] T016 [US2] In `RackCard.Chains.cs`:
  - A left-click on an effect or MIDI card header selects it through `_ctx.SelectRackDevice(new(rackIndex, c, slot == RcSlot.Midi, idx))`, keeping the rack index from `BuildRackCard`.
  - Draw the selected card with the track chain's selected look (`NotaPalette.Accent` border, 1.5 px, as `DeviceChainView` draws `_selDeviceIndex`).
  - Clear the rack selection when the chosen chain changes.
- [X] T017 [US2] Add the rack-device commands (Copy, Cut, Paste, Duplicate, Delete) in a new partial `src/managed/Nota.App/DeviceCards/Rack/RackCard.Clipboard.cs` (SPDX header).
  - Work on the selection through `IRackAccess`. Each runs inside `E.BeginUndoGroup()` / `EndUndoGroup()`, so Cut (copy + remove) is one step.
  - Paste goes after the selected index in the same chain and section, and goes to the MIDI section when the clipboard kind is 2.
  - Map refusal codes to the data-model.md status messages through `_ctx.Status`, and add " · sidechain dropped" when `E.DeviceClipboardSidechain` was true before the paste.
  - Select the pasted or duplicated device, then `DeferRebuild()`.
- [X] T018 [US2] Extend `RcCardMenu` in `RackCard.Chains.cs` with groups via `MenuKit.AddGroups`: Copy / Cut / Paste (enabled when `E.DeviceClipboardKind` fits) / Duplicate, then Delete as `MenuKit.Danger`, with `MenuKit` key hints. The existing **Remove** item becomes **Delete**.
- [X] T019 [US2] Add **Paste** to the chain row menu (`RcChainMenu`) and to the lane "+" target's add menus (`ShowAddDeviceMenu` / `RcAddMidiMenu`). It pastes at the end of the matching section and is disabled when the clipboard is empty or doesn't fit. Files: `RackCard.Chains.cs` and `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs`.
- [X] T020 [US2] Route keys in `src/managed/Nota.App/DeviceChainView.Clipboard.cs` `OnKeyDown`: when `_rackSel` is set, ⌘C / ⌘X / ⌘V / ⌘D / Delete / Backspace call the selected rack card's commands, through a delegate the card registers on the context (`DeviceCardContext.SetRackCommands`). Typing in a TextBox never reaches it (the existing focus guard).

**Checkpoint**: US1 and US2 both work: quickstart Q1–Q3 and Q5 inside racks.

---

## Phase 5: User Story 3 — Copy between the track chain and rack chains (P2)

**Goal**: one clipboard both ways, plus Duplicate on the track chain.

**Independent Test**: quickstart Q4 + Q6; the smoke checks below.

### Smoke for US3

- [X] T021 [US3] In `RackClipboardTests.cs`:
  - Track Delay → `TrackCopyDevice` (existing) → `RackDevChainDevicePaste` into an Instrument Rack chain: the params are equal, and the rendered output of a test tone through both is equal within 1e-4 (SC-002).
  - Rack chain EQ → `RackDevChainDeviceCopy` → `TrackPasteDevice`: it lands on the track with equal params.
  - Track Arp → MIDI Effect Rack chain and Instrument Rack chain.
  - A keyed Compressor: `DeviceClipboardSidechain` is true; the pasted chain copy has `DeviceSidechainSource` −1 (read through the chain); the track source keeps its key.
  - Track `TrackDuplicateDevice` / `TrackDuplicateMidiEffect` are one Undo each.

### Implementation for US3

- [X] T022 [US3] In `src/managed/Nota.App/DeviceChainView.Clipboard.cs`:
  - Paste is enabled when the engine clipboard has something (`_engine.DeviceClipboardKind != 0`), not only when `_clip` is set, so a rack copy pastes onto the track chain.
  - Keep `_clip`'s preset label only when it came from the track.
  - Selecting a track device clears the rack selection (T010).
- [X] T023 [US3] Add **Duplicate ⌘D** to the track device menu (between Paste and the rack group) and ⌘D to `OnKeyDown`, calling `DuplicateDevice` / `DuplicateMidiEffect`. Then `ExtrasMoved`-style bookkeeping for the A/B extras, select the copy, and `Rebuild()`. File: `DeviceChainView.Clipboard.cs`.
- [X] T024 [US3] When the rack selection is set and the clipboard came from the track (or the other way round), paste goes to where the user last clicked (FR / US3-5). Verify in `DeviceChainView.Clipboard.cs` that the routing from T020 covers it with no extra state.

**Checkpoint**: all three stories work: quickstart Q1–Q6.

---

## Phase 6: Polish & Cross-Cutting

- [X] T025 [P] Shortcuts (nota-shortcuts skill): in `src/managed/Nota.App/PreferencesWindow.cs` "Devices (after a click on a device)", add `⌘ D` "Duplicate the device". Mention that the copy / cut / paste rows work in rack chains too.
- [X] T026 [P] `CHANGELOG.md` `[Unreleased]`: under Added, moving, copying, cutting, pasting and duplicating devices in rack chains, plus copy between track and rack chains; under Added or Changed, Duplicate ⌘D on the track chain.
- [X] T027 [P] `FEATURES.md` (nota-features): extend the racks bullet and the device chain bullet.
- [X] T028 nota-docs (EN + RU) in the docs repo: the rack pages (instrument-rack, audio-effect-rack, midi-effect-rack) get a "Move and copy devices" paragraph; the device chain page mentions Duplicate ⌘D; regenerate the shortcuts page.
- [X] T029 Theme check: render the rack card with a selected device, a drag in progress (drop bar) and a row drop outline in all four themes with the headless harness; fix any token issue (quickstart Q7, FR-017).
- [X] T030 Run `scripts/build.sh` (native, managed, full smoke). Confirm the `racks: chain device clipboard` section prints and passes.
- [X] T031 Remove the obsolete `RcGripReorder` code path and the "drag the grip" wording in comments. Re-check the Nota Compliance list in spec.md.

---

## Dependencies & Execution Order

- **Setup (T001–T002)** → **Foundational (T003–T010)** → the stories.
- **US1** within a chain (T012, T014) needs only existing engine calls and could start right after Setup. The cross-chain move (T013) needs T004, T005, T007–T009.
- **US2** needs T003, T005–T010.
- **US3** needs US2's selection and commands (T016–T020), because paste routing is shared.
- **Polish** comes after the stories are done.

## Parallel Opportunities

- T002 runs alongside T001. T008 (interop) can run alongside T003–T007 once the ABI signatures are fixed (contracts/c-abi.md).
- Within US1: T014 alongside T012 (different methods, same file: run them one after the other if one agent edits).
- In Polish: T025, T026 and T027 together.

## Implementation Strategy

1. **MVP**: Setup, then T012 + T014 (reorder inside a chain with the existing engine calls). That fixes "can't move" first.
2. Foundational engine and interop, then T013 (move to another chain), then US2, then US3.
3. Polish: docs, shortcuts, themes, the full build.
