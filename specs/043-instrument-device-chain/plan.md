# Implementation Plan: The instrument as a first-class device in the chain

**Branch**: `feat/instrument-device-chain` | **Date**: 2026-10-27 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/043-instrument-device-chain/spec.md`

## Summary

The track's instrument card becomes selectable and gets Replace…, Copy, Cut, Paste, Delete and
Move to track ▸; deleting it deletes the track (confirmed when the track has clips). Every
device header gets a visible Remove (×) control. Any device can move (or, with ⌥, copy) to
another track from its menu or by dragging its header onto a track header in the Arrangement
or Session view, in one undo step that carries its automation. Session view takes instrument
drops: on a column to replace, right of the last column to add a track.

Engine: the shared device clipboard gains an instrument family (reusing `cloneInstrument`), one
`replaceTrackInstrument` path (racks allowed as the new instrument), and a single-publish
`transferDevice` for cross-track moves. Managed: selection / menus / Remove control in
`DeviceChainView`, a `DeviceDrag` payload for drags out of the panel, drop targets in
`ArrangementView` and `SessionView`, `DeviceInsertService.DropOnSession` and a forced-replace
placement, two MCP tools. See [research.md](research.md).

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (each edit publishes one
  immutable graph), AR-6 (nothing new on the audio thread), AR-7 (only `nota_engine.h`).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill (the Remove control,
  the instrument selection ring, drop rings on headers).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 + Avalonia (app, presentation, MCP)

**Primary Dependencies**: None new

**Storage**: None new — moved / pasted devices are saved like any other; the clipboard is not persisted

**Audio-Thread Impact**: None on the audio thread itself. Every edit builds a new graph on the
message thread and publishes it once (AR-5); a cross-track move publishes both tracks in the
same snapshot, so the device never plays on two tracks or none. Old instruments are released by
the existing snapshot reclamation, never on the audio thread (AR-6).

**Testing**: `tests/Nota.SmokeTest` (new `InstrumentChainTests.cs`, engine through the C ABI);
UI checked in the running app per [quickstart.md](quickstart.md)

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: Desktop app (native engine + managed UI)

**Performance Goals**: An instrument replace / move during playback: no dropout on other tracks
(SC-006); the edit itself under one UI frame plus the instrument's own construction time

**Constraints**: One undo step per user action (FR-008, SC-005); the macOS DnD single-item rule
(R5)

**Scale/Scope**: ~3 engine methods + 4 C ABI calls; ~8 managed files touched; 2 MCP tools; 1 smoke file

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Notes |
|-----------|--------|-------|
| I. Real-time audio safety | Pass | Message-thread graph edits, one publish each (R2, R4); no audio-thread code changes |
| II. Boundary discipline | Pass | Four new C functions in `nota_engine.h` (contracts/c-abi.md); no JUCE outside `pluginhost/` |
| III. Licensing hygiene | Pass | New files carry the SPDX header; no dependencies |
| IV. Verification gate | Pass | `InstrumentChainTests` covers every new C ABI call; `scripts/build.sh` before the PR |
| V. One logical change | Pass, noted | Four issues, one theme: the device chain's instrument and device lifecycle share one replace / transfer path |
| VI. Docs in sync | Planned | CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs (nota-docs), Shortcuts pane for ⌥-drag (nota-shortcuts) |
| VII. Spec-first | Pass | spec.md with clarifications, this plan, tasks.md next |
| VIII. Rules not weakened | Pass | — |

Post-design re-check (after Phase 1): unchanged — the contracts add no audio-thread work and
cross the boundary only through the C ABI.

## Project Structure

### Documentation (this feature)

```text
specs/043-instrument-device-chain/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R9
├── data-model.md        # clipboard families, transfer, drag payload
├── quickstart.md        # end-to-end checks in the app + smoke
├── contracts/
│   ├── c-abi.md         # new nota_engine.h calls
│   ├── ui.md            # menus, Remove control, drops, confirmation
│   └── mcp.md           # set_track_instrument, move_device_to_track
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h          # + copy/paste instrument, transfer device; set_builtin_instrument takes 3/4
└── src/
    ├── abi/nota_engine_device.cpp      # C wrappers
    └── engine/
        ├── Engine.h                    # clipInstrument_, replaceTrackInstrument, transferDevice
        ├── Engine_Devices.cpp          # clipboard family 3, replace path, transfer
        └── Engine_Automation.cpp       # lane hand-over helper for transfer

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs        # + CopyInstrument, PasteInstrument, TransferDevice
├── Nota.Infrastructure/Interop/                   # NativeMethods.Devices.cs, NotaEngine.Devices.cs
├── Nota.Presentation/DeviceInsertService.cs       # InsertPlacement.ReplaceInstrument, DropOnSession
├── Nota.App/
│   ├── DeviceChainView.Clipboard.cs   # instrument selectable, menu: Replace…, Move to track ▸, Delete → track
│   ├── DeviceChainView.Shell.cs       # Remove control in the header
│   ├── DeviceChainView.Fold.cs        # Remove control on the folded strip
│   ├── DeviceChainView.Instruments.cs # instrument card: CanDelete, selection ring, drag hook
│   ├── DeviceDrag.cs                  # new: the out-of-panel drag payload (R5)
│   ├── ArrangementView.cs             # device drops on rows / footer
│   ├── SessionView.Grid.cs            # instrument + device drops on headers, slots, the area right of the last column
│   ├── MainWindow.Dnd.cs              # OnSessionDrop instruments, OnDeviceDropOnTrack, delete-track wiring
│   ├── DeviceCards/Rack/RackCardView.cs # SlotDelBtn restyled to match
│   └── PreferencesWindow (Shortcuts list) # ⌥-drag gesture
└── Nota.Mcp/Tools/                    # InstrumentTools.cs (set_track_instrument), DeviceTools.cs (move_device_to_track)

tests/Nota.SmokeTest/
├── InstrumentChainTests.cs            # new
└── Program.cs                         # run it; --instrument-chain
```

**Structure Decision**: The existing engine / managed split. Engine work stays in the device
files beside the #238 clipboard; UI work stays in the `DeviceChainView` partials plus the two
views that already take Browser drops.

## Phases (for /speckit-tasks)

1. **Engine foundation** — `replaceTrackInstrument`, racks in `setTrackBuiltinInstrument`,
   instrument clipboard family, `transferDevice` with lane hand-over; C ABI; interop; smoke tests.
2. **US1 (P1)** — instrument selectable; Replace…, Copy, Cut, Paste, Delete (→ track, confirmed).
3. **US2 (P1)** — Remove control on full and folded cards, rack compact cards restyled.
4. **US3 (P2)** — Session instrument drops (headers, slots, right of last column).
5. **US4 (P2)** — Move to track ▸ menu; drag onto Arrangement / Session headers, ⌥ = copy.
6. **MCP, docs, shortcuts, CHANGELOG, FEATURES; build + smoke.**

## Complexity Tracking

No constitution violations.
