# Implementation Plan: Multi-select devices and group them into a rack

**Branch**: `feat/device-multiselect-group` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/044-device-multiselect-group/spec.md`

## Summary

The track's device chain gets a multi-selection (Shift-click range, ⌘/Ctrl-click toggle)
across MIDI effects, the instrument and audio effects. Copy / Cut / Paste / Delete / Duplicate
/ drag / Move to track ▸ act on the whole selection as one undo step. ⌘G groups a contiguous
selection into the matching rack (Audio Effect, Instrument or MIDI Effect Rack), and ⌘⇧G
ungroups any one-chain rack; both are one publish and one undo step, and the track sounds the
same.

Per the clarification, automation lanes, CV links, sidechain keys and Arp CC routes travel
into the rack and back. That needs the engine's one real addition: a **nested parameter
address** (`ParamPath`: host device → chain uid → slot) used by automation, CV, the Arp CC
route (moved into the undo snapshot) and the sidechain feed, resolved on the audio thread by
reading the rack's published snapshot. On top of that: general `groupDevices` /
`ungroupRack` operations replacing the single wrap / unwrap (#257), a multi-entry device
clipboard, project-format fields for nested lanes, and the selection model, menus and keys in
`DeviceChainView`. See [research.md](research.md) R1–R11.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (Group / Ungroup / Paste
  each publish one graph), AR-6 (the nested resolver only reads published snapshots — no
  allocation or lock on the audio thread), AR-7 (new calls only in `nota_engine.h`).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill (multi-selection
  borders, the menu header with a count, disabled items).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 + Avalonia (app, presentation, MCP)

**Primary Dependencies**: None new

**Storage**: Project file — automation-lane, CV-link and Arp-CC-route DTOs gain optional
`Chain` / `Slot` fields (format version bump; old projects load unchanged, R7). The clipboard is
not persisted.

**Audio-Thread Impact**: `applyAutomation`, `applyMidiCcRouting`, the CV apply pass and the
sidechain feed resolve nested targets by scanning the rack's published `RackState` chains
(no allocation, no lock, AR-6). Group / Ungroup / Paste build off-graph and publish once
(AR-5); moved instances are not re-prepared, so tails and held notes continue.

**Testing**: `tests/Nota.SmokeTest` — new `DeviceGroupTests.cs` (engine through the C ABI:
group / ungroup per rack type, render equality before / after, nested lanes / CV / CC /
sidechain, undo, save-reload, multi-clipboard) and `DeviceSelectionTests.cs` (the selection
model, headless). UI checked in the running app per [quickstart.md](quickstart.md).

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: Desktop app (native engine + managed UI)

**Performance Goals**: Group / Ungroup during playback: no dropout (SC-006), the edit under one
UI frame; render before vs after Group within 0.1 dB (SC-003); nested resolve adds no
measurable block time (checked with the existing engine perf gate).

**Constraints**: One undo step per user action (FR-020); racks nest one level (specs/041);
Ungroup only for one-chain racks.

**Scale/Scope**: ~6 engine files (+1 helper), ~6 C ABI calls, ~10 managed files, 1 MCP tool
pair, 2 smoke files

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Real-time audio safety | Pass | Nested resolve reads the published rack snapshot only (R2); structural edits publish once (R3) |
| II. Boundary discipline | Pass | New calls in `nota_engine.h` (contracts/c-abi.md); no JUCE outside `pluginhost/` |
| III. Licensing hygiene | Pass | New files carry the SPDX header; no dependencies |
| IV. Verification gate | Pass | `DeviceGroupTests` covers every new C ABI call; `scripts/build.sh` before the PR |
| V. One logical change | Pass, noted | One issue (#328); the nested address is its prerequisite and lands in the same PR, first commit(s) |
| VI. Docs in sync | Planned | CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs (nota-docs), Shortcuts pane (nota-shortcuts) |
| VII. Spec-first | Pass | spec.md with 3 clarifications, this plan, tasks.md next |
| VIII. Rules not weakened | Pass | — |

Post-design re-check (after Phase 1): unchanged — the contracts add audio-thread reads of
already-published data only, and cross the boundary only through the C ABI.

## Project Structure

### Documentation (this feature)

```text
specs/044-device-multiselect-group/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R11
├── data-model.md        # ParamPath, selection, clipboard, group shape
├── quickstart.md        # end-to-end checks in the app + smoke
├── contracts/
│   ├── c-abi.md         # new / changed nota_engine.h calls
│   ├── ui.md            # gestures, menus, keys, status messages
│   └── mcp.md           # group_devices, ungroup_rack
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # + group/ungroup, multi copy/paste, nested lane/CV info
└── src/
    ├── abi/
    │   ├── nota_engine_device.cpp      # group / ungroup / copy_devices / paste_devices wrappers
    │   └── nota_engine_automation.cpp  # nested lane info + add (for load)
    ├── devices/
    │   ├── MidiDevice.h                # CC destination leaves the device (R4)
    │   └── racks/RackCore.h/.cpp       # setChainInstrumentInstance; child walk helper for the feed
    └── engine/
        ├── ParamPath.h                 # new: the nested address + resolver declaration (R1)
        ├── Automation.h, Modulation.h, Track.h  # lanes, CV links, CC routes carry chain/slot
        ├── Engine.h
        ├── Engine_Rack.cpp             # groupDevices / ungroupRack (R3), remapPathsAfterRackEdit (R6)
        ├── Engine_Automation.cpp       # applyAutomation via resolver; remap helpers take paths
        ├── Engine_Modulation.cpp       # CV apply / remap via paths
        ├── Engine_Render.cpp           # applyMidiCcRouting + sidechain feed walk rack chains (R4, R5)
        └── Engine_Devices.cpp          # multi-entry clipboard (R8), route claims walk racks

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs, EngineContracts.cs   # GroupDevices, UngroupRack, CopyDevices, PasteDevices, lane Chain/Slot
├── Nota.Infrastructure/
│   ├── Interop/NativeMethods.*.cs, NotaEngine.*.cs               # P/Invoke
│   └── Persistence/ProjectService.cs                             # Chain / Slot on lane, CV, CC-route DTOs (R7)
├── Nota.App/
│   ├── DeviceSelection.cs              # new: the selection model (R9)
│   ├── DeviceChainView.Clipboard.cs    # selection gestures, batch ops, ⌘G / ⌘⇧G, menus
│   ├── DeviceChainView.Shell.cs        # CardExtra.Wrapped → list (R10); selection border per card
│   ├── DeviceChainView.Move.cs         # Move to track ▸ for a selection; multi drag-reorder
│   ├── DeviceChainView.Instruments.cs, DeviceChainView.Midi.cs   # click modifiers on those cards
│   ├── ArrangementView.Automation.cs   # nested lane titles ("Rack › Reverb · Mix")
│   └── PreferencesWindow (Shortcuts list)                        # Shift/⌘-click, ⌘G/⌘⇧G in Devices
└── Nota.Mcp/Tools/DeviceTools.cs       # group_devices, ungroup_rack

tests/Nota.SmokeTest/
├── DeviceGroupTests.cs                 # new
├── DeviceSelectionTests.cs             # new
├── Fixtures/                           # an old-format project with an Arp CC lane (R4 migration)
└── Program.cs                          # run them; --device-group
```

**Structure Decision**: The existing engine / managed split. The nested address lives in one
new engine header used by the automation, modulation and render files; the UI work stays in
the `DeviceChainView` partials with the rules factored into `DeviceSelection`.

## Phases (for /speckit-tasks)

1. **Foundation: nested address** — `ParamPath` + resolver; lanes, CV links, Arp CC route
   (moved into the snapshot, with load migration) and the sidechain feed / route claims go
   through it; `remapPathsAfterRackEdit` on rack chain edits; nested lane info in the ABI;
   persistence fields; smoke tests for each consumer. Behaviour today is unchanged (no nested
   paths exist until Group).
2. **Engine Group / Ungroup** — `groupDevices` / `ungroupRack` for the three rack types,
   `setChainInstrumentInstance`, path rewriting, old wrap / unwrap as wrappers; smoke: render
   equality, undo, save-reload.
3. **US1 (P1)** — ⌘G / ⌘⇧G and menu items on audio-effect selections (single and range).
4. **US2 (P1)** — `DeviceSelection`, gestures, borders, multi-entry clipboard (engine + UI),
   batch Delete / Cut / Duplicate / drag / Move to track ▸, confirmation with the instrument.
5. **US3 (P2)** — Instrument Rack group / ungroup from the UI.
6. **US4 (P3)** — MIDI Effect Rack group / ungroup from the UI.
7. **MCP, nested lane titles, Shortcuts, CHANGELOG, FEATURES, docs; build + smoke + perf gate.**

## Complexity Tracking

No constitution violations. The nested address (phase 1) is the large part and exists only
because of the FR-016 clarification; it is kept to one rack level and one shared resolver.
