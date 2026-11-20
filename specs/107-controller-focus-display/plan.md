# Implementation Plan: Controller focus you can see — in Nota and in Remote

**Branch**: `feat/controller-focus-display` | **Date**: 2026-11-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/107-controller-focus-display/spec.md`
(issue [nota-daw/nota#929](https://github.com/nota-daw/nota/issues/929)).

## Summary

Make the hardware controller's focus visible and reachable. Nota gets a title-bar readout of
the lead controller's focus (track · device · page, or mode · bank), a "controller" mark on the
focused card, Track ◀ / ▶ that *select* tracks (the bank follows), and "Next / Previous device"
commands that walk the track's chain. Remote's state gains the Session selection (outlined on
the Scenes grid) and the controller focus (a header line; Macros follows it when the phone
follows Nota). All managed / web — no engine change.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: the readout is a
  `chip` text, the card mark uses the orange *lines* strength (the focus overlay's family).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- Builds directly on [specs/051](../051-controller-auto-mapping/plan.md) (ControllerService,
  FocusedDevice, RoleRouter, ControllerFocusOverlay) and
  [specs/102](../102-remote-session-grid/contracts/session-message.md) (the `ses` message).

## Technical Context

**Language/Version**: C# / .NET 10 (Nota.Application, Nota.App, Nota.Remote, Nota.Mcp);
TypeScript + Svelte 5 (`src/web/remote`).

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: None new. Profiles (embedded JSON) gain `command` bindings only.

**Audio-Thread Impact**: None — UI thread (controller drain, commands, overlay) and the Remote
hub's UI-thread tick only. No new engine calls.

**Testing**: `tests/Nota.SmokeTest` (ControllerTests: chain order, track step + bank follow,
device step; RemoteTests: `ses.sel`, `foc`, macro follow), web `vitest` (`src/web/remote/test`),
headless UI harness for the readout / card mark screenshots, real KeyLab check.

**Target Platform**: macOS / Linux / Windows desktop; Remote in phone / tablet browsers.

**Project Type**: desktop app + embedded web client.

**Performance Goals**: readout update within one frame of the change; Remote within 0.5 s
(hub tick sends `foc` on change only; `ses` already 15 Hz on the Scenes screen).

**Constraints**: protocol additive (old clients ignore unknown fields / message types).

**Scale/Scope**: ~10 files managed, 4 web files, 1–2 profiles, docs EN + RU.

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | #929 carries `approved` |
| I Real-time safety | No audio-thread code touched |
| II Boundary | No C ABI change |
| III Licensing | New files get SPDX headers; no dependency |
| IV Verification | Smoke tests extended (controller + Remote) |
| V One change | One feature, one PR (+ docs PR in nota-docs) |
| VI Docs | CHANGELOG, FEATURES.md, nota-docs (controllers, remote), nota-shortcuts (2 new commands) |
| VII Spec-first | This spec / plan / tasks |
| VIII | No rule bent |

Post-design re-check: unchanged — PASS.

## Design

### 1. Readout (FR-001–FR-003, FR-005)

- `ControllerService.FocusSummary()` → `ControllerFocusSummary` (data-model.md) for the lead
  controller (first active with knobs — same rule as `SyncControllerIndicator`).
- `MainWindow.Controllers.SyncControllerIndicator` renders it into the existing chip:
  `KeyLab · Bass · Twin · 2 / 4` (Device mode) or `KeyLab · Volume · 9–16`; trimmed with an
  ellipsis (MaxWidth), full text in the tooltip. Raised by the existing `FocusChanged` /
  `ControllersChanged`, plus `BankChanged` and track renames (project refresh).
- The chip click today opens the knob-mode menu; it becomes a menu with **Show device** (opens
  the Devices panel on the focused track, selecting the focused device) above the modes. The
  Session lower panel is never switched by a controller (FR-005).

### 2. Card mark (FR-004)

- `DeviceChainView` sets an attached property `ControllerFocusOverlay.CardAddress`
  (DeviceAddress) on each top-level card it builds (MIDI effects, instrument, audio effects;
  racks as one card).
- `ControllerFocusOverlay.Render` finds the card whose address equals the focus (rack paths
  compare by their top-level address) and draws an orange *line* along the card's top edge plus
  a small "controller" tag at its right end (DESIGN.md orange-lines strength, `AccentLine`
  token). Nothing new per card type.

### 3. Track ◀ / ▶ select the track (FR-006–FR-009)

- `RoleRouter`: TrackNext / TrackPrev emit a new `ControllerActionKind.TrackStep` (Index ±1)
  instead of BankStep ±1 (both in `Route` and the encoder-pair path).
- `ControllerTrackStep.Next(order, selected, delta)` (Nota.Application, pure, testable):
  the next id in the *selectable order* — `ArrangementView.HierarchyOrder` minus returns and
  minus members of collapsed groups; no selection → first track; ends stop.
- `ControllerService.Execute(TrackStep)`: compute the target, call new hook
  `SelectTrack(int id)` (wired by MainWindow), then `ControllerState.BankFollow(index, count)`
  moves the bank minimally to contain it (FR-007) and raises `BankChanged`.
- MainWindow `SelectTrack`: in the Session → `SessionView.SelectSlot(id, currentScene)` (which
  raises SlotSelected → FollowSessionSlot → TrackSelected chain as a click does); elsewhere
  → `Timeline.SelectTrackFromOutside(id)` + scroll into view. The old "Session also moves the
  slot" special case in `Execute(BankStep)` goes away.
- Bank ◀ / ▶ stay BankStep ±8.

### 4. Device stepping (FR-010–FR-012)

- `ChainOrder.Of(engine, trackId)` (Nota.Application/Controllers, pure over IAudioEngine):
  `[Midi 0..n-1, Instrument (if the track has one), Audio 0..m-1]` as DeviceAddress.
  `ChainOrder.Step(order, current, delta)` → address or null at the ends; `current` is the
  focus' top-level address (a rack-inner focus steps from its rack).
- Commands `device.focusNext` / `device.focusPrevious` in `CommandCatalog` (group Devices,
  no default key) bound in `MainWindow.Commands`: compute from `_controllers.Focused`, else from
  `FocusedDevice.DefaultFor(selected track)`; then
  `_deviceChain.SelectAddress(address)` when the panel shows that track (selects the card,
  rebuild → `ControllerFocusChanged` → focus moves, page 0 via `FocusMoved`); when the panel
  holds another track, `ShowDevices(track, takeOverPanel:false)` first (content only, the panel
  isn't opened — FR-005, US3-3).
- `DeviceChainView.SelectAddress(DeviceAddress)`: public wrapper over `SelectDevices` +
  `Rebuild` for the top-level sections.
- Wizard (ControllerSetupWindow) extra-buttons page: one more `Half` pair
  "Previous device / Next device" bound as `command` roles.
- Profiles (research R4): KeyLab Essential mk3 DAW — bind if the real device shows a free
  button pair (checked on hardware, T-hw); Launchkey MK3, X-Touch Mini, MCU — unchanged.

### 5. Remote (FR-013–FR-017)

- `IRemoteHost` gains `RemoteSessionSelection SessionSelection { get; }` and
  `RemoteControllerFocus? ControllerFocus { get; }` (records in RemoteContracts.cs). MainWindow
  implements them (SessionView exposes its `_sel` when effectively visible; ControllerService's
  summary).
- `BuildSession` adds `"sel"` (contracts/remote-focus.md). The Scenes screen outlines it.
- New message `foc` built once per tick into the Snapshot (`Focus` lazy piece), sent to every
  phone whose last `foc` differs. Header shows it as one muted line under the track name; hidden
  when `on:false`.
- Macro follow: in `Push`, when `c.Follow` and the focus key changed since this phone last saw
  it, and the focus track is `c.TrackId`, map the address to the phone's macro list index
  (`RemoteMacroDevice.DeviceIndex`: −1 instrument / rack, ≥0 audio effect; MIDI effects have
  none) and set `c.MacroDevice`. A manual pick on the phone stays until the focus changes again.
- Mock (`mock/nota-mock.mjs`) sends `sel` and `foc` so the web app and Nota NX can be checked
  without Nota.

### 6. MCP (FR-018)

- `IControllerAccess.StepDevice(int delta, string? portUid)` and `SessionSelection()`; tools
  `step_controller_device` and `get_session_selection` (contracts/mcp.md). The existing
  `get_controller_focus` already reports device and page.

## Project Structure

### Documentation (this feature)

```text
specs/107-controller-focus-display/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md
├── contracts/remote-focus.md   # ses.sel + foc message
├── contracts/mcp.md            # two tools
└── tasks.md                    # /speckit-tasks
```

### Source Code

```text
src/managed/Nota.Application/Controllers/
├── RoleRouter.cs               # TrackStep action
├── ControllerState.cs          # BankFollow
├── ChainOrder.cs               # NEW: chain order + step
└── ControllerTrackStep.cs      # NEW: selectable track order + step
src/managed/Nota.Application/Palette/CommandCatalog.cs   # device.focusNext / Previous
src/managed/Nota.App/
├── Controllers/ControllerService.cs        # TrackStep, FocusSummary, StepDevice, hooks
├── Controllers/ControllerFocusOverlay.cs   # card mark, CardAddress attached property
├── Controllers/ControllerSetupWindow.cs    # wizard pair
├── Controllers/Profiles/arturia.keylab-essential-61-mk3-daw.json  # if hardware allows
├── MainWindow.Controllers.cs   # readout, Show device menu, SelectTrack hook
├── MainWindow.Commands.cs      # device commands
├── MainWindow.Remote.cs        # IRemoteHost: SessionSelection, ControllerFocus
├── DeviceChainView*.cs         # CardAddress on cards, SelectAddress
└── SessionView.cs              # current selection accessor
src/managed/Nota.Remote/
├── RemoteContracts.cs          # records + host members
└── RemoteHub.State.cs          # ses.sel, foc, macro follow
src/managed/Nota.Mcp/            # Abstractions + ControllerTools (+ App adapter)
src/web/remote/src/
├── lib/app.svelte.ts           # foc state, ses.sel type
├── components/Header.svelte    # focus line
└── screens/Scenes.svelte, Macros.svelte
src/web/remote/mock/nota-mock.mjs
src/managed/Nota.Application/Localization (en strings) # new keys
tests/Nota.SmokeTest/ControllerTests.cs, RemoteTests.cs
```

**Structure Decision**: existing projects only; the two pure helpers live beside FocusedDevice
in Nota.Application so the smoke test can reach them (Nota.App isn't referenced by it).

## Complexity Tracking

None.
