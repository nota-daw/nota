---

description: "Task list for the racks redesign, the MIDI Effect Rack and MIDI effects in Instrument Rack chains"
---

# Tasks: Racks redesign and MIDI Effect Rack

**Input**: Design documents from `/specs/040-racks-redesign/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. Constitution IV requires the smoke test to cover every engine behaviour change, and quickstart Q2 lists the sections.

**Organization**: Tasks are grouped by user story. P1 stories come first (US1, US2, US5), then P2 (US3, US4, US6).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US6)
- Engine root `E = src/native/nota.engine`; app root `A = src/managed/Nota.App`; smoke tests `T = tests/Nota.SmokeTest`. Paths below are written in full.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Freeze today's behaviour before anything changes, and scaffold the test files.

- [X] T001 On the current `develop` engine (before any engine edit), add a one-off generator in `tests/Nota.SmokeTest/RackRedesignTests.cs` (`--rack-fixtures`). It writes to `tests/Nota.SmokeTest/Fixtures/racks-v8/`:
  - an Instrument Rack (3 chains, key/vel zones, names, mix, chain devices, 4 macros with **different curves on two targets of one macro**, an inverted range, glide 0.3) as `instrument.bin` plus a settings dump `instrument.txt`. Its audio is not compared: the built-in synths free-run, so renders differ between runs;
  - an Audio Effect Rack in Select mode (2 chains of deterministic Utility / EQ-3, select ranges via velLo/velHi, dry/wet 0.7, pdc on, a macro target) as `effect.bin`, `effect.txt` and a 1 s 48 kHz render of a generated tone clip, `effect.f32`.

  Commit the fixtures with the tests.
- [X] T002 [P] Create test scaffolds with the SPDX header and empty `Run()` returning `(bool ok, string label)` lists: `tests/Nota.SmokeTest/RackRedesignTests.cs`, `tests/Nota.SmokeTest/MidiRackTests.cs`, `tests/Nota.SmokeTest/RackChainMidiTests.cs`
- [X] T003 Wire the sections into `tests/Nota.SmokeTest/Program.cs`. The full run prints `racks: midi rack routing`, `racks: midi rack no stuck notes`, `racks: midi rack persistence`, `racks: instrument mono/glide`, `racks: mapping rules`, `racks: v8 compatibility`, `racks: key params table`, `racks: chain MIDI effects`. Add a standalone `--racks` switch following the `--device-copy` pattern

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Shared engine fields, blob v9, mapping rules and the empty shared card that every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

### Engine core

- [X] T004 Extend `RackCore` in `src/native/nota.engine/src/devices/racks/RackCore.h`:
  - `RackChain.midiDevices` (`std::vector<std::shared_ptr<MidiDevice>>`);
  - `ChainControls` fields `selLo`/`selHi` (0–127, default 0–127), `transpose` (−48…+48, default 0), `velScale` (0–2, default 1);
  - rack atomics `mono_` (default off), `merge_` (default on), `thru_` (default on);
  - constants `kRackControl = −3` and `kMidiDeviceBase = −1000` ("chain MIDI device j = −1000 − j").

  Clamp every setter. Lo/hi setters never let an edge cross the other ("lo ≤ hi enforced on set").
- [X] T005 Implement in `src/native/nota.engine/src/devices/racks/RackCore.cpp`:
  - copy the new fields in `forkFrom` (chain `midiDevices` shared, like `devices`);
  - `addChainMidiDevice` / `removeChainMidiDevice` / `moveChainMidiDevice` / `chainMidiDeviceAt` / `chainMidiDeviceCount` (kinds 0–5 via `makeMidiDevice`; kind 6 refused; snapshot swap, AR-5);
  - chain `setRates` / `forwardTempo` reach chain MIDI devices.
- [X] T006 Blob **v9** in `src/native/nota.engine/src/devices/racks/RackCore.cpp` `serialize`/`deserialize`:
  - append per chain `selLo, selHi, transpose, velScale, midiDeviceCount, { kind, paramCount, params[], stateSize, state[], bypassed }`;
  - append per rack `mono, merge, thru`;
  - readers of v ≤ 8 use the defaults (`selLo 0, selHi 127, transpose 0, velScale 1`, mono off, merge on, thru on);
  - bump the version comment list.
- [X] T007 Mapping rules in `src/native/nota.engine/src/devices/racks/RackCore.cpp`:
  - `addMacroMapping` moves an existing mapping of the same (chain, device, param) to the new macro and returns its index;
  - `applyMacro` drives `deviceIndex = kRackControl, param 0` → `chainSelect` (0–1) and `deviceIndex ≤ kMidiDeviceBase` → chain MIDI device params;
  - removing a chain or chain MIDI device drops its mappings and re-indexes the rest.
- [X] T008 Generic MIDI-effect state ABI:
  - `nota_midi_effect_get_state` / `nota_midi_effect_set_state` (checkpoint flag) in `src/native/nota.engine/include/nota/nota_engine.h` and `src/native/nota.engine/src/abi/nota_engine_device.cpp`;
  - backing `Engine` methods in `src/native/nota.engine/src/engine/Engine_Devices.cpp` + `Engine.h`. Reuse `cloneMidiDevice`'s state path.
- [X] T009 Selector mapping target and mapping-move ABI semantics:
  - update `nota_rack_add_macro_mapping` / `nota_rackdev_add_macro_mapping` docs in `src/native/nota.engine/include/nota/nota_engine.h` (device −3 = selector; same param moves);
  - add `#define NOTA_RACK_MIDI_DEVICE_BASE (-1000)` and `NOTA_RACK_CONTROL (-3)`;
  - curves stay on the existing `nota_*_mapping_curve` calls (no change to `*_mapping_info`).

### Managed plumbing shared by all racks

- [X] T010 [P] P/Invoke the T008/T009 calls in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Devices.cs` + `src/managed/Nota.Infrastructure/Interop/NotaEngine.Devices.cs`; add `MidiEffectGetState`/`MidiEffectSetState` to `src/managed/Nota.Application/Engine/IAudioEngine.cs`
- [X] T011 Extend `IRackAccess` in `src/managed/Nota.App/DeviceCards/Rack/IRackAccess.cs`:
  - `RackKind`;
  - select range get/set (`SelectRange(chain)` / `SetSelectRange`);
  - `MappingCurve` for all mappings;
  - `MoveChain`, `DuplicateChain`;
  - default-implemented MIDI-section members (`ChainMidiCount`, `AddChainMidi`, …) returning "unsupported".

  Map the Audio Effect Rack's select range onto `velLo/velHi` inside `RackDevAccess` (research D2) so the card never knows.
- [X] T012 [P] Create `src/managed/Nota.App/DeviceCards/Rack/RackKindProfile.cs`: per kind (Instrument, Audio, MIDI) the zone tab label ("Zones" / "Select"), whether velocity zones / select rows / selector row exist, routing modes (none · Parallel/Serial/Select · Parallel/Select), right-tab label ("Rack out" / "Output"), Rack-out rows, chain Volume/Pan vs Volume(velocity %)/Transpose, the chains-foot hint text, and allowed drop kinds per section (research D11)
- [X] T013 [P] Create `src/managed/Nota.Presentation/Racks/RackKeyParams.cs` (UI-free, so the smoke test can check it): a static table (device family, kind) → up to three key parameter **names**, resolved to indices against the device's own names at run time, for every rack-eligible instrument, audio effect and MIDI effect kind (names as in the mockup where they exist, e.g. Volt Cutoff/Reso/Drive, Valve Gain/Tone/Mix, Arp Rate/Gate/Octaves). A missing kind falls back to its first three parameters
- [X] T014 Create the shared card skeleton `src/managed/Nota.App/DeviceCards/Rack/RackCard.cs` (region placeholders in `RackCard.Macros.cs`, `.Chains.cs`, `.Zones.cs`, `.MacroTab.cs`, `.RightPanel.cs`):
  - view state per rack instance, kept across rebuilds and theme switches: centreTab, rightTab, selectedChain, selectedMacro, mappingMacro, probePitch (data-model "View state");
  - three `DeviceCardKit.Section` columns (~116 · fill · ~160) and the 18 px status line, all inside `BuildCardShell` from `src/managed/Nota.App/DeviceChainView.Shell.cs`;
  - segmented tab strips with sentence-case labels via `NotaText.Label`;
  - empty region builders that the story phases fill in.

  No hex, no `IrCap`, no `Opacity` (contracts/ui-rack-card.md § Frame).
- [X] T015 [P] Smoke: in `tests/Nota.SmokeTest/RackRedesignTests.cs` add:
  - `racks: v8 compatibility` — load the T001 fixtures into fresh racks; the settings dumps must match and the Audio Effect Rack render must match `effect.f32` within 1e-5 (SC-004);
  - `racks: mapping rules` — move-on-add, selector target −3 drives `chainSelect`, chain delete drops mappings;
  - `racks: key params table` — every rack-eligible kind has ≤ 3 in-range indices.

**Checkpoint**: Engine fields, blob v9 and mapping rules are tested; the empty shared card is there. Stories can start.

---

## Phase 3: User Story 1 - Layer and split instruments in the reworked Instrument Rack (Priority: P1) 🎯 MVP

**Goal**: The Instrument Rack uses the shared card: chains, compact device strip, zones with hover probe, Chain panel, and Rack out with volume, working glide and mono.

**Independent Test**: Open a three-chain Instrument Rack; chains, zones and the Chain / Rack out panels change the sound and show it in the card (spec US1; quickstart Q4 steps 1, 4, 5, 6).

### Tests for User Story 1

- [X] T016 [P] [US1] Smoke `racks: instrument mono/glide` in `tests/Nota.SmokeTest/RackRedesignTests.cs`:
  - mono on: one sounding pitch per chain, last-note priority, release returns to the previous held note;
  - glide 0.5 on a Volt chain: per-note bend starts at (prev − new) semitones and reaches 0 after ~250 ms;
  - a chain with a non-MPE instrument retriggers without bend;
  - poly mode is unchanged against a fixture render.

### Implementation for User Story 1

- [X] T017 [US1] Mono note stack in `src/native/nota.engine/src/devices/racks/RackInstrument.cpp`/`.h` (fixed 128-entry stack, no allocation). Glide via block-rate `noteExpression(pitch, ExprBend, …)` ramps over `glide × 500 ms`, only for chains whose instrument `supportsMpe()` (research D4)
- [X] T018 [US1] ABI `nota_rack_set_mono` / `nota_rack_mono` in `src/native/nota.engine/include/nota/nota_engine.h` + `src/native/nota.engine/src/abi/nota_engine_rack.cpp` + `src/native/nota.engine/src/engine/Engine_Rack.cpp`. Add `nota_rack_duplicate_chain` and `nota_rackdev_duplicate_chain`: insert after, copy devices, controls and the chain's mappings, as one undo step through `rackForEdit`
- [X] T019 [P] [US1] Interop for T018 in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Rack.cs`, `NotaEngine.Rack.cs`, `NativeMethods.RackDev.cs`, `NotaEngine.RackDev.cs` and `src/managed/Nota.Application/Engine/IAudioEngine.cs`
- [X] T020 [US1] Macro column (display and turning only; mapping comes in US2) in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Macros.cs`:
  - 2 × 4 cells with a 22 px `Knob`, name, mono value ("—" when unmapped), target-count badge;
  - knob contract: vertical drag, fine modifier, double-click resets, `GestureBegin/End` automation write on the rack's macro params, `MidiLearn.Bind`, live follow that skips while dragging.
- [X] T021 [US1] Chains tab in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Chains.cs`.
  - Chain list:
    - 34 px rows: colour stripe from the chain palette index (resolved at render time); name in accent ink when chosen;
    - M/S keys with `NotaPalette.Mute`/`Solo` fills and `InkOn`; mono zone summary (`C-1–A#4 · v96+`); level bar;
    - "+ Chain"; chains-foot hint.
  - Device strip of the chosen chain: 96 px compact cards with power dot, name, kind badge, `RackKeyParams` sliders (`SliderTrack`, `VizInk` at rest), "Open" and `Glyph.Grip` reorder; chevron glyphs between cards; a dashed drop target.
  - Browser drops follow `RackKindProfile` rules; refused drops show a tooltip.
- [X] T022 [US1] Zones tab in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Zones.cs`.
  - Ruler C0…C8.
  - Per-chain 24 px rows: key bar and velocity bar with drag edges (whole values, edges never cross), mono labels in note names.
  - Hover probe: teal 1 px line on every row; chains hit at velocity 100 get a teal wash and name. The footer shows the hint and the result ("C4 · vel 100 → Volt + Pentad" / "nothing").
- [X] T023 [US1] Right panel in `src/managed/Nota.App/DeviceCards/Rack/RackCard.RightPanel.cs`.
  - Chain tab:
    - colour dot, name with double-click rename, "n / N";
    - Volume (dB) and Pan (L/C/R) via `DeviceCardKit.SliderRow`;
    - Keys/Vel rows that open the Zones tab;
    - Duplicate and Delete (Delete disabled with one chain).
  - Rack out tab for Instrument: Volume, Glide (ms, tooltip "Glides in mono mode"), switch "Mono (last note)".
- [X] T024 [US1] Status line summary for Instrument in `src/managed/Nota.App/DeviceCards/Rack/RackCard.cs`: chains · probe result or zone summary · "N macros mapped". Nothing on the right
- [X] T025 [US1] Route `BuildInstrumentRackBody` in `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs` to `RackCard` (Instrument profile). Delete the `Ir*` builders (`InstrumentRackMain`, `IrMacroStrip`, `IrChainList`, `IrDeviceArea`, `IrZoneMap`, `IrRackRail`, `IrCap`…) that nothing else uses. Keep `RackCardView.Drum.cs` and `MacroPanel.cs` compiling
- [X] T026 [US1] MCP: `set_rack_mono(trackId, on)` and `get_rack` gains `mono` in `src/managed/Nota.Mcp/Tools/RackTools.cs`. The `set_rack_glide` description says it is audible in mono mode

**Checkpoint**: An Instrument Rack can be built, split and played from the new card (mapping still read-only).

---

## Phase 4: User Story 2 - Map macros directly in the chain (Priority: P1)

**Goal**: Map in place (no Done screen). The Macros tab has ranges, inversion, curves and "Mixed".

**Independent Test**: In any rack, map two params to one macro, set ranges and a curve, turn the macro; both params follow (spec US2; quickstart Q4 steps 2–3).

### Tests for User Story 2

- [X] T027 [P] [US2] Extend `racks: mapping rules` in `tests/Nota.SmokeTest/RackRedesignTests.cs`:
  - the curve of a macro reads "mixed" when its targets differ;
  - setting a macro curve updates every target in one undo step;
  - min > max inverts the response;
  - the T001 fixture's mixed macro still renders identically.

### Implementation for User Story 2

- [X] T028 [US2] Mapping mode in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Chains.cs` and `RackCard.Macros.cs`:
  - Map key: accent key while on. Map switches to the Chains tab and sets `mappingMacro`; selecting another macro moves mapping to it.
  - Compact cards in mapping mode: teal dashed frame (not this macro) or solid frame (this macro), "copy" cursor, and a scrollable list of **all** params of the device.
  - Click toggles the mapping (default range ±0.3 around the current value). A param mapped to another macro moves (engine T007).
  - Header line "Mapping · Macro N · Name — click to add or remove".
  - Outside mapping, a mapped param shows the teal "M n" tag, ignores drags and opens the Macros tab on its macro when clicked.
- [X] T029 [US2] Esc ends mapping: register it with the app's key handling in `src/managed/Nota.App/MainWindow.MenuKeys.cs`, active only while a rack card is mapping, so other Esc uses are untouched. Leaving the Chains tab also ends mapping
- [X] T030 [US2] Macros tab in `src/managed/Nota.App/DeviceCards/Rack/RackCard.MacroTab.cs`.
  - Header:
    - slot "Macro N", name with double-click rename, mono value;
    - "Curve" segmented Lin · Exp · Log · S. "Mixed" means no key chosen plus a caption; picking a curve applies it to every target in one undo step;
    - a 30 × 22 `NotaGraph` preview (`VizInk` curve, accent dot; none when Mixed);
    - Map key.
  - Target rows:
    - colour stripe, "chain › device ›" and param name;
    - a 150 px range bar with min/max handles and a current-position marker;
    - range text in the param's units, unmap via `Glyph.Close`.
  - One Ink-5 empty-state line.
  - New targets get the macro's curve, or Lin when the macro is Mixed (research D5).
- [X] T031 [US2] Remove `InstrumentRackMacroMap`, `IrMappingRow` and `_macroMapMode` from `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs`
- [X] T032 [P] [US2] MCP in `src/managed/Nota.Mcp/Tools/RackTools.cs` and `src/managed/Nota.Mcp/Tools/EffectRackTools.cs`:
  - `set_macro_curve(trackId, deviceIndex, macro, curve)`;
  - `set_mapping_range(trackId, deviceIndex, index, min, max)`;
  - per-macro `curve` with `"mixed"` in `get_rack` / `get_effect_rack`;
  - add-mapping moves an existing mapping (contracts/mcp-tools.md).

**Checkpoint**: US1 + US2 make a complete Instrument Rack. This is the MVP.

---

## Phase 5: User Story 5 - The racks look right in every theme (Priority: P1)

**Goal**: The shared card passes the design guards and reads correctly in all four themes, and recolours live.

**Independent Test**: Capture the card in each theme, switch live, run the guards (quickstart Q5, SC-003).

- [X] T033 [US5] Run the smoke test design sections (`RunStudio`, `RunThemes`, `RunLabels`, `RunIcons` in `tests/Nota.SmokeTest/DesignTokenCheck*.cs`). Fix every hit in `src/managed/Nota.App/DeviceCards/Rack/RackCard*.cs` **without** adding lines to any `*AllowList.txt`
- [X] T034 [US5] Audit `src/managed/Nota.App/DeviceCards/Rack/RackCard*.cs` for colour snapshots:
  - no `new SolidColorBrush(slot.Color)` outside `Render`;
  - no `static readonly` palette-derived brush except slots and `Wash`;
  - chain colours resolved per render.

  Confirm that view state (tabs, chosen chain and macro) survives a theme switch.
- [X] T035 [US5] Headless capture harness (scratch project, not committed). Capture the Instrument Rack card × 3 centre tabs × 2 right tabs × {Studio Dark, Studio Light, Ember Graphite, Ember Paper}, plus a live switch without rebuild. Check each frame against the DESIGN.md readability bar and keep the sheet for the PR

**Checkpoint**: The P1 slice (US1, US2, US5) can ship as the first UI PR.

---

## Phase 6: User Story 3 - Route audio through the reworked Audio Effect Rack (Priority: P2)

**Goal**: The Audio Effect Rack uses the shared card with a Select tab, the selector and the Rack-out routing modes.

**Independent Test**: A two-chain Audio Effect Rack; switch the modes and drag the selector; audio and status line agree (spec US3).

- [X] T036 [P] [US3] Smoke in `tests/Nota.SmokeTest/RackRedesignTests.cs`: duplicate chain on an Audio Effect Rack copies its devices and mappings; a selector mapped to a macro switches the active chain mid-render
- [X] T037 [US3] Select tab for the Audio profile in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Zones.cs`:
  - ruler 0/32/64/96/127;
  - Selector row (accent handle, drag to set, mappable via Map);
  - per-chain select-range bars;
  - outside Select mode the bars use the next ink step and the footer says "Ranges apply when Chains run is set to Select";
  - footer result "selector 30 → Crunch" / "silence".
- [X] T038 [US3] Rack out for the Audio profile in `src/managed/Nota.App/DeviceCards/Rack/RackCard.RightPanel.cs`:
  - "Chains run" Parallel · Serial · Select with a one-line explanation;
  - Dry/wet (%) and Gain (dB, centred);
  - switch "Latency compensation".

  Chain rows show `sel lo–hi` in Select mode and "N devices" otherwise.
- [X] T039 [US3] Status line for Audio in `src/managed/Nota.App/DeviceCards/Rack/RackCard.cs` (mode · chain names · dry/wet · macros). Route `BuildEffectRackBody` in `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs` to `RackCard` (Audio profile) and delete the `Ae*` builders
- [X] T040 [P] [US3] MCP: `add_effect_rack_mapping` accepts `deviceIndex = −3` (selector) in `src/managed/Nota.Mcp/Tools/EffectRackTools.cs`

**Checkpoint**: Instrument and Audio Effect Racks share one card.

---

## Phase 7: User Story 4 - Split and transform notes with the new MIDI Effect Rack (Priority: P2)

**Goal**: A new MIDI effect (kind 6) with zoned chains of MIDI effects, Parallel/Select routing, merge and thru. Saved, undoable, presettable and scriptable.

**Independent Test**: A MIDI Effect Rack with two chains on an instrument track; notes across the split go to the right chain; save, reopen and undo restore it (spec US4; quickstart Q3).

### Tests for User Story 4

- [X] T041 [P] [US4] Smoke `racks: midi rack routing` in `tests/Nota.SmokeTest/MidiRackTests.cs`: feed fixed events through a rack on an instrument track and compare the instrument's note-ons with expected lists for:
  - key and velocity zones; mute and solo;
  - Parallel vs Select (selector + select ranges);
  - transpose +12 and −48 clamping;
  - velScale 100 % / 50 % (at least 1) / 0 % (no notes);
  - merge on/off with two chains sending the same pitch;
  - thru on/off for a note outside every zone;
  - a bypassed rack passes notes through.
- [X] T042 [P] [US4] Smoke `racks: midi rack no stuck notes` in `tests/Nota.SmokeTest/MidiRackTests.cs`: 1 000 random note-on/off interleaved with zone, mode, mute and transpose edits and chain deletes (fixed seed). At the end the instrument holds no notes (`heldNotes == 0`) (SC-005)
- [X] T043 [P] [US4] Smoke `racks: midi rack persistence` in `tests/Nota.SmokeTest/MidiRackTests.cs`:
  - state blob → new rack → identical `get_midi_rack`;
  - project save/reopen;
  - undo/redo of every structural call;
  - user preset save/load;
  - track duplicate and MIDI-effect copy/paste (SC-006).

### Implementation for User Story 4

- [X] T044 [US4] Create `src/native/nota.engine/src/devices/racks/MidiRack.h/.cpp`: `class MidiRack : public MidiDevice, public RackCore`, `midiKind() 6`, "Nota MIDI Effect Rack". `process` follows research D3:
  - routing by zones + select;
  - `routeMask[128]` per input pitch;
  - per-chain ping-pong buffers, members only, 16 chains × 2 × 1024 events;
  - transpose and velScale;
  - per-chain held-output bitsets;
  - merge counters and thru;
  - reconcile on a snapshot change: note-offs for pitches of chains that are gone;
  - sorted output capped at `maxOut`.

  Also: `reset` / `timeJump` fan-out; macros as params 0–7; `getState/setState` = blob; `clone`; Serial mode refused. No allocation, lock or I/O in `process` (AR-6).
- [X] T045 [US4] Register kind 6 in `makeMidiDevice` in `src/native/nota.engine/src/devices/DeviceFactory.cpp`. New racks start with one empty chain. Cap chains at 16 (`addChain` returns <0 at the limit)
- [X] T046 [US4] Engine methods in `src/native/nota.engine/src/engine/Engine_Rack.cpp` + `Engine.h`:
  - `midiRackAt(track, midiIndex)`;
  - `midiRackForEdit`: fork + republish for undo, like `rackForEdit`, carrying bypass and fold;
  - the structural and value operations behind the ABI.
- [X] T047 [US4] `nota_midirack_*` ABI from `specs/040-racks-redesign/contracts/c-abi.md` in `src/native/nota.engine/include/nota/nota_engine.h` + `src/native/nota.engine/src/abi/nota_engine_rack.cpp`: structure, chain controls, rack, macros and mappings. Plain MIDI indices are translated to `kMidiDeviceBase − j` internally. Add the new symbols to `src/native/nota.engine/nota_exports.txt` and `nota_exports.map`
- [X] T048 [P] [US4] P/Invoke + wrapper: create `src/managed/Nota.Infrastructure/Interop/NativeMethods.MidiRack.cs` and `src/managed/Nota.Infrastructure/Interop/NotaEngine.MidiRack.cs`; add the `MidiRack*` members to `src/managed/Nota.Application/Engine/IAudioEngine.cs`
- [X] T049 [US4] Persistence:
  - `MidiDeviceDto.State` (optional blob ref) in `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs`;
  - in `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs`, write the state when non-empty and restore it after `AddMidiEffect`;
  - bump `CurrentFormatVersion` 23 → 24; files without `State` load unchanged;
  - add a no-op entry in `ProjectMigrations.cs` if versions are listed there.
- [X] T050 [US4] Presets: user presets for MIDI effects (kind 6 with its state blob) in `src/managed/Nota.Infrastructure/Persistence/PresetService.cs` (`CaptureMidiEffect`, state restored on apply); the card menu's "Save preset" addresses a MIDI effect as `PresetService.MidiEffectBase − i` (it used to pass the MIDI index as an audio device index)
- [X] T051 [P] [US4] Catalog: add "Nota MIDI Effect Rack" (MIDI, kind 6, "rack", "midirack split layer", "Splits notes across chains of MIDI effects.") in `src/managed/Nota.Application/Palette/BuiltinDeviceCatalog.cs`, and a "Racks" category on the MIDI tab in `src/managed/Nota.Application/Palette/DeviceCategories.cs`
- [X] T052 [US4] Add `MidiRackAccess` (on `nota_midirack_*`) to `src/managed/Nota.App/DeviceCards/Rack/IRackAccess.cs`. In `src/managed/Nota.App/DeviceChainView.Midi.cs`, build kind 6 as `RackCard` (MIDI profile) inside `BuildCardShell`, width 700
- [X] T053 [US4] MIDI profile UI:
  - in `RackCard.Zones.cs`: Selector row and per-chain select bars on the Zones tab, inactive outside Select mode;
  - in `RackCard.RightPanel.cs`:
    - Chain tab: Volume as velocity % (0–200, 100 = unchanged) and Transpose (st, −48…+48);
    - Output tab: "Chains run" Parallel · Select + explanation, switches "Merge duplicate notes" and "Pass through outside zones";
  - in `RackCard.Chains.cs`: an activity bar instead of the level meter;
  - status line: mode · "N chains → merged MIDI" · probe result.

  Refuse adding chains at 16 with a tooltip.
- [X] T054 [US4] MCP: create `src/managed/Nota.Mcp/Tools/MidiRackTools.cs` with the 22 tools in `specs/040-racks-redesign/contracts/mcp-tools.md` (zones accept note names). `list_midi_fx_kinds` / `add_midi_fx` gain kind 6 in `src/managed/Nota.Mcp/Tools/MidiFxTools.cs`. Register the tool type where the others are registered
- [X] T055 [P] [US4] MCP scenario "split arp above C3, chords below, accents on hard hits" as the `racks: midi rack MCP` section in `tests/Nota.SmokeTest/MidiRackTests.cs` (AiGoldenTests covers the AI models, not MCP)

**Checkpoint**: All three rack kinds exist and share the card.

---

## Phase 8: User Story 6 - MIDI effects inside an Instrument Rack chain (Priority: P2, issue #329)

**Goal**: Instrument Rack chains run MIDI effects → instrument → audio effects, sample-accurate, saved and scriptable.

**Independent Test**: An Arp in one chain of a two-chain Instrument Rack arpeggiates only that chain. Save, preset and reopen keep it (spec US6; quickstart Q4b).

### Tests for User Story 6

- [X] T056 [P] [US6] Smoke `racks: chain MIDI effects` in `tests/Nota.SmokeTest/RackChainMidiTests.cs`:
  - an Arp in chain 0 produces the same note-ons (pitch and sample offset) as the same Arp on a plain track over 8 bars, including a loop wrap and a stop/start;
  - chain 1 receives the raw notes;
  - zones and mono run before chain MIDI effects;
  - save/reopen, rack preset, track duplicate, device copy/paste, undo/redo and `add_rack_chain_midi_fx` all round-trip;
  - a chain MIDI effect param mapped to a macro follows it.

### Implementation for User Story 6

- [X] T057 [US6] Add `virtual bool rendersBlockEvents() const`, `virtual void renderBlock(float* out, int32_t frames, const MidiEv* evs, int n, const ExprEv* xev, int xn, double beatStart, double spb, bool playing, …live hook…)`, `virtual void midiReset()` and `virtual void midiTimeJump(double)` to `src/native/nota.engine/src/devices/Instrument.h` (defaults: false / no-op)
- [X] T058 [US6] In `src/native/nota.engine/src/engine/Engine_Render.cpp`:
  - make `playEvents` call `inst.renderBlock(...)` when `inst.rendersBlockEvents()`;
  - extract its segment loop into a shared helper the rack reuses, without changing behaviour for other instruments;
  - call `midiReset` / `midiTimeJump` wherever the track's MIDI chain gets `reset` / `timeJump` (stop, seek, loop wrap, Session slot switch).
- [X] T059 [US6] In `src/native/nota.engine/src/devices/racks/RackInstrument.cpp`/`.h`:
  - `rendersBlockEvents()` = any chain has MIDI devices, read from the live snapshot; Drum Rack returns false;
  - `renderBlock` per chain: zone filter (mono first) → chain MIDI devices on member scratch (2 × 1024 events) with the block clock → the shared segment helper on the chain instrument → chain audio devices → sum;
  - glide only for chains without MIDI devices (research D4/D15);
  - `midiReset` / `midiTimeJump` fan out to the chain MIDI devices;
  - no allocation.
- [X] T060 [US6] ABI `nota_rack_chain_midi_*` (contracts/c-abi.md § Instrument Rack additions) in `src/native/nota.engine/include/nota/nota_engine.h` + `src/native/nota.engine/src/abi/nota_engine_rack.cpp` + `src/native/nota.engine/src/engine/Engine_Rack.cpp`, with edits through `rackForEdit`. Update the export lists
- [X] T061 [P] [US6] Interop for T060 in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Rack.cs`, `NotaEngine.Rack.cs` and `src/managed/Nota.Application/Engine/IAudioEngine.cs`. Implement the MIDI-section members of `InstrumentRackAccess` in `src/managed/Nota.App/DeviceCards/Rack/IRackAccess.cs`
- [X] T062 [US6] Chain strip MIDI section for the Instrument profile in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Chains.cs`:
  - MIDI effects (own dashed drop target, "MIDI" kind badge) · instrument · audio effects;
  - a MIDI effect dropped anywhere on the strip lands in the MIDI section;
  - reorder only within the section;
  - the chain's Add menu lists MIDI effects;
  - "Open" shows the full MIDI-effect card from `src/managed/Nota.App/DeviceCards/Midi/MidiDeviceCardFactory.cs` bound to the chain device.

  Mapping uses device `−1000 − j`.
- [X] T063 [US6] Browser drops of MIDI effects onto an Instrument Rack chain: handled by the card's own drop targets (chain row, list, MIDI section) in `RackCard.Chains.cs`; chain devices have no copy/paste today, so chain MIDI effects follow (none)
- [X] T064 [P] [US6] MCP in `src/managed/Nota.Mcp/Tools/RackTools.cs`:
  - `add_rack_chain_midi_fx`, `remove_rack_chain_midi_fx`, `move_rack_chain_midi_fx`;
  - `get_rack_chain_midi_fx_params`, `set_rack_chain_midi_fx_param`, `set_rack_chain_midi_fx_bypass`;
  - `get_rack` lists `midiEffects`;
  - `add_macro_mapping` accepts `midiFx`.

**Checkpoint**: Issue #329 is done; every story works on its own.

---

## Phase 9: Polish & Cross-Cutting Concerns

- [ ] T065 Theme pass over all three racks: repeat T033–T035 for the Audio and MIDI profiles and the Instrument MIDI section (4 themes × 3 racks sheet for the PR, quickstart Q5)
- [ ] T066 [P] Perf gate for the `playEvents` hook: `scripts/perf-gate.sh develop` reports no regression (quickstart Q4c)
- [ ] T067 Run quickstart.md Q1–Q6 end to end and record the results in the PR description

### Nota closing tasks *(always include — these are the PR gates)*

- [ ] T068 Run `scripts/build.sh` (and on CI `scripts/build-linux.sh` / `scripts/build-win.ps1`) until native + managed + smoke test are green
- [ ] T069 AGPL-3.0-only SPDX two-line header on every new source file: `MidiRack.h/.cpp`, `RackKindProfile.cs`, `RackKeyParams.cs`, `RackCard*.cs`, `NativeMethods.MidiRack.cs`, `NotaEngine.MidiRack.cs`, `MidiRackTools.cs`, the three new test files
- [ ] T070 New dependency: none → N/A in the PR
- [ ] T071 `CHANGELOG.md` `[Unreleased]` entries: Added — MIDI Effect Rack; MIDI effects in Instrument Rack chains (#329); mono (last note) and working glide on the Instrument Rack; the selector as a macro target. Changed — redesigned Instrument and Audio Effect Rack cards, macro mapping without a separate screen
- [ ] T072 `nota-features` (FEATURES.md), `nota-docs` (EN + RU rack pages, a new MIDI Effect Rack page, screenshots in all themes as the docs use them) and `nota-shortcuts` (Esc ends macro mapping in the Preferences → Shortcuts list in `src/managed/Nota.App/PreferencesWindow.cs`)
- [ ] T073 Engine behaviour change: covered by T015, T016, T027, T036, T041–T043, T055 and T056. Confirm every section prints in the full smoke run

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (1)**: T001 must run on the unmodified engine, before T004–T009.
- **Foundational (2)**: depends on Setup; blocks all stories.
- **US1 (3)** → **US2 (4)**: US2 builds on the US1 card regions (macro column, chain strip).
- **US5 (5)**: after US1 + US2; re-run in Polish (T065) for the other racks.
- **US3 (6)**: after US2 (shared mapping and Macros tab); independent of US4 and US6.
- **US4 (7)**: engine and ABI tasks T041–T051 depend only on Foundational; UI tasks T052–T053 after US2.
- **US6 (8)**: engine tasks T056–T061 depend only on Foundational (T004–T007); UI tasks T062–T063 after US1.
- **Polish (9)**: after the stories being shipped.

### Within each story

Tests first (they should fail), then engine → ABI → interop → managed → UI → MCP.

### Suggested PR stack (Constitution V)

1. Engine core + ABI + smoke: T001–T009, T015, T016–T018, T036 (engine part), T041–T047, T056–T060.
2. Managed plumbing + persistence + MCP: T010, T019, T026, T032, T040, T048–T051, T054–T055, T061, T064.
3. Shared card + themes: T011–T014, T020–T025, T028–T031, T033–T035, T037–T039, T052–T053, T062–T063, T065.
4. Docs: T071–T072.

Each PR runs T068.

---

## Parallel Examples

```bash
# Foundational, after T004–T007 land:
Task: "T010 P/Invoke midi-effect state in NativeMethods.Devices.cs / NotaEngine.Devices.cs"
Task: "T012 RackKindProfile.cs"
Task: "T013 RackKeyParams.cs"
Task: "T015 v8 compatibility + mapping rules + key params smoke sections"

# US4 tests together:
Task: "T041 midi rack routing"   Task: "T042 no stuck notes"   Task: "T043 persistence"

# US4 and US6 engine work in parallel (different files):
Task: "T044 MidiRack.h/.cpp"     Task: "T059 RackInstrument renderBlock"
```

---

## Implementation Strategy

### MVP (P1)

1. Phases 1–2.
2. US1 (Instrument Rack on the new card) → US2 (in-place mapping) → US5 (themes).
3. **Stop and validate**: quickstart Q2 (v8, mono/glide, mapping sections), Q4 steps 1–6 for the Instrument Rack, Q5 for the Instrument card.

### Incremental delivery

4. US3: the Audio Effect Rack moves onto the card.
5. US4: MIDI Effect Rack.
6. US6: MIDI effects in Instrument Rack chains (#329).
7. Polish: all-rack theme sheet, perf gate, docs.

---

## Notes

- Never add lines to `tests/Nota.SmokeTest/*AllowList.txt` (Constitution VIII, SC-003).
- No allocation, lock or I/O on the audio thread in `MidiRack::process`, the mono/glide path or `renderBlock` (AR-6).
- `RackCardView.Drum.cs` / `MacroPanel.cs`: compile fixes only. The Drum Rack is out of scope.
- The user makes the commits: leave changes uncommitted for review.
