# Implementation Plan: Move, copy and paste devices in rack chains

**Branch**: `feat/rack-nested-instruments` (spec 042 follows 040 and 041 on the same branch) | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/042-rack-device-clipboard/spec.md`

## Summary

Rack chains get the track chain's device handling:
- **header drag** with a drop bar to reorder within a section;
- **drop on another chain's row** to move a device there, mappings and all;
- **selection, with Copy / Cut / Paste / Duplicate / Delete** from the menu and from ⌘C / ⌘X / ⌘V / ⌘D / Delete.

The track chain gains Duplicate (⌘D) too.

The clipboard is the engine's existing one (`clipDevice_` / `clipMidi_`), so a copy taken on the track chain pastes into a rack chain and the other way round.

Engine work is small and structural (no DSP):
- rack copy, paste at a position, duplicate, and move to another chain;
- each as one rack fork, so each is one undo step.

The bulk is managed UI: the rack lane's drag, selection, menus and key routing in `DeviceChainView`. See [research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (rack edits are immutable snapshots via `rackForEdit`), AR-6 (nothing new on the audio thread), AR-7 (new C ABI functions only).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: the drop bar and selection reuse the track chain's tokens (`AccentBright` bar, the selected-device look).
- **Shortcuts** → the `nota-shortcuts` skill (⌘D in Preferences → Shortcuts).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine: `RackCore`, `Engine_Rack.cpp`, `abi/nota_engine_rack.cpp`); C# / .NET 10 (`Nota.Infrastructure` interop, `Nota.Application` `IAudioEngine`, `Nota.App` rack card and device chain view).

**Primary Dependencies**: none new (Avalonia as today).

**Storage**: none new. Moves and pastes change the rack blob through existing persistence; no format change.

**Audio-Thread Impact**: none new. Every operation is a structural rack edit on the message thread: `rackForEdit` forks the rack (AR-5) and republishes; the audio thread sees a new snapshot. Instrument Rack MIDI chains reconcile held notes in `syncChains` when a chain's MIDI effects change (specs/040 review fix); MIDI Effect Racks cut held notes on a structure change. No allocation or lock is added on the audio thread (AR-6).

**Testing**: `tests/Nota.SmokeTest`, a new section `racks: chain device clipboard`. It covers:
- move within a chain and to another chain, with mappings and undo;
- copy and paste between the track chain and rack chains (audio effect, MIDI effect, hosted plug-in where available), checking params and state;
- every refusal code;
- sidechain stripping;
- duplicate on both chains;
- one undo step each.

The UI (drag, selection, menu, keys) is checked headlessly by driving the card's handlers, plus a theme sheet.

**Target Platform**: macOS / Windows / Linux desktop (as the app).

**Project Type**: desktop app (native engine + managed UI).

**Performance Goals**: an edit lands within one UI frame plus the snapshot publish (well under 50 ms on a 16-chain rack).

**Constraints**:
- one undo step per user action;
- no change to rack blob v9 or the project format;
- the track chain's existing behaviour stays as it is, apart from the added Duplicate.

**Scale/Scope**: about 8 C ABI functions, 4 `RackCore` methods, and the rack card and device chain view (around 400 lines of C#).

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass: structural edits via snapshots only; nothing on the audio thread |
| II. Boundary discipline | Pass: new calls are pure C ABI in `nota_engine.h`; no JUCE outside `pluginhost/` |
| III. Licensing hygiene | Pass: no new dependency; new files (if any) get the SPDX header |
| IV. Verification gate | Planned: `scripts/build.sh` plus the new smoke section |
| V. One logical change per PR | Note: lands on the 041 branch per the user's flow; can be split into its own PR at review time |
| VI. Docs in sync | Planned: CHANGELOG, FEATURES, nota-docs rack pages (EN + RU), Shortcuts list |
| VII. Spec-first | Pass: spec 042 with clarifications |
| VIII. Safety rules not weakened | Pass |

No violations, so no Complexity Tracking entries. Re-checked after Phase 1: unchanged.

## Project Structure

### Documentation (this feature)

```text
specs/042-rack-device-clipboard/
├── spec.md
├── research.md
├── plan.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   └── ui.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                 # new nota_rackdev_chain_* clipboard/move calls, nota_device_clipboard_sidechain
├── src/devices/racks/RackCore.h/.cpp          # insertChainDeviceInstance, insertChainMidiDeviceInstance,
│                                              # moveChainDeviceToChain, moveChainMidiDeviceToChain
├── src/engine/Engine.h, Engine_Rack.cpp       # rackChain{Device,Midi}{Copy,Paste,Duplicate,MoveTo}
├── src/engine/Engine_Devices.cpp              # deviceClipboardSidechain()
└── src/abi/nota_engine_rack.cpp, nota_engine_device.cpp

src/managed/
├── Nota.Infrastructure/Interop/NativeMethods.Rack.cs, NotaEngine.Rack.cs   # P/Invoke + wrappers
├── Nota.Application/Engine/IAudioEngine.cs                                # the new calls
├── Nota.App/DeviceCards/Rack/IRackAccess.cs                               # Copy/Paste/Duplicate/MoveToChain per rack kind
├── Nota.App/DeviceCards/DeviceCardContext.cs                              # SelectRackDevice / RackSelection
├── Nota.App/DeviceCards/Rack/RackCard.Chains.cs                           # header drag + drop bar, chain-row drop,
│                                                                          # selection look, card menu, chain-row Paste
├── Nota.App/DeviceChainView.Clipboard.cs                                  # rack selection routing, ⌘D, Duplicate item
├── Nota.App/MenuKit.cs                                                    # DuplicateKey
└── Nota.App/PreferencesWindow.cs                                          # ⌘D row

tests/Nota.SmokeTest/RackClipboardTests.cs (new), Program.cs (section hook)
```

**Structure Decision**: The engine keeps the clipboard and does every structural edit as one rack fork. The managed side holds only the selection and routes keys and menus. The rack card is the only UI that draws rack chain devices, so the drag, selection and menus live in `RackCard.Chains.cs`, and `DeviceChainView` stays the single key handler.

## Complexity Tracking

None.
