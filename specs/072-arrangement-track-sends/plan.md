# Implementation Plan: Send levels in the arrangement track headers

**Branch**: `feat/arrangement-track-sends` | **Date**: 2026-11-02 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/072-arrangement-track-sends/spec.md` (issue nota-daw/nota#143)

## Summary

Put one inline send knob per return in the arrangement track header, and make sends a
first-class mix value everywhere: undoable through the value-gesture mechanism (specs/009)
and automatable through a new track automation target. The engine gains `MixKind::TrackSend`
(value undo, bus in `deviceIndex`) and `AutomationTarget::Send` (lane per bus, played back by
writing the existing atomic send level at block rate). Return deletion remaps send lanes the
same way it remaps send levels. The managed side gains `MixTarget.Send` /
`AutomationTarget.Send`, routes every send control (header, Mixer window, Session mixer,
Sends card, MCP `set_send`) through a gesture + automation write, adds "Send A · <return>"
entries to the lane target menu, and draws the send row in standard (≥ 88 px) and large
headers behind a per-project "Sends" toggle. Visual design: [mockup.html](mockup.html) (rendered in the PR).

## Nota: Read Before Planning

Consulted: `ARCHITECTURE.md` (AR-4/5/6/7), `DESIGN.md` + `nota-design` (inline knob,
accent only while live, sentence-case labels, mono read-outs), `CONTRIBUTING.md`.

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app, Avalonia UI)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: project file — automation lanes are already saved generically (target +
device index + points), so a send lane round-trips with no format change; the Sends toggle
rides in the existing `track-view.json` sidecar (`showSends`, omitted when default).

**Audio-Thread Impact**: send automation is applied in `applyAutomation` (already on the
audio thread, block rate) by storing the track's existing `std::atomic<float>` send level —
the same pattern as volume/pan. No allocation, no lock, no new command. Lane add/remove and
return compaction stay structural edits that publish a new graph (AR-5) on the message
thread. Value undo stays message-thread only.

**Testing**: `tests/Nota.SmokeTest` — new `SendAutomationTests` (`--sends`): send gesture
undo/redo + no-change, MCP `set_send` one step, send lane playback moves the send level,
record-write creates a lane and undoes with the value, save/load keeps the lane on its bus,
deleting a return drops/remaps send lanes and undo restores them. Design token checks
(`RunStudio`, `RunLabels`, …) cover the new UI. UI verified by screenshots of the real app.

**Target Platform**: macOS, Windows, Linux desktop.

**Project Type**: desktop app (native engine + managed UI).

**Performance Goals**: header rebuild cost unchanged in order (≤ 4 extra knobs per card);
the live-follow refresher reads sends on the existing meter tick, skipping knobs in the hand.

**Constraints**: header widths 160–480 px, four returns max (`kMaxReturns`).

**Scale/Scope**: ~5 engine files + ABI header, ~9 managed files, 1 smoke-test file.

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | #143 carries `approved` |
| I Real-time safety | Pass — atomic store from the existing automation pass; structural edits on the message thread |
| II Boundary | Pass — one new `NOTA_MIX_TRACK_SEND` kind and target value 5 documented in `nota_engine.h`; no new functions |
| III Licensing | Pass — new files carry the SPDX header; no dependency |
| IV Verification | Smoke test extended (`SendAutomationTests`) |
| V One change | One feature: sends from the arrangement, with the undo/automation it needs |
| VI Docs | CHANGELOG `[Unreleased]`, FEATURES.md (nota-features), user docs (nota-docs) |
| VII Spec-first | This spec / plan / tasks |
| VIII | No rule bent |

Post-design re-check: unchanged — pass.

## Design

### Engine (`src/native/nota.engine`)

- `EngineTypes.h`: `MixKind::TrackSend = 8` (7 stays internal `DeviceNotes`); `deviceIndex` = bus.
- `Engine.cpp` `mixValue` / `applyMix`: read / write `Track::send(bus)`; a bus outside
  `0..kMaxReturns-1` or a missing track → no value / skip.
- `nota_engine_core.cpp` `nota_mix_gesture_begin`: accept `NOTA_MIX_TRACK_SEND` (8) besides 0–6.
- `Automation.h`: `AutomationTarget::Send = 5` (`deviceIndex` = bus, `paramIndex` −1).
- `Engine_Automation.cpp`: `applyAutomation` → `t.setSend(bus, clamp(v, 0, 1))`;
  `currentTargetValue`, `sameTarget` and `findLaneFor` match Send by bus;
  `addAutomationLane` keeps the bus for Send (one lane per bus).
- `Engine_Tracks.cpp`: the "automation follows across tracks" filters treat Send like
  Volume/Pan (track-mix lanes); `compactReturns` drops Send lanes to a deleted bus and
  renumbers the rest along with the send levels (cloning the track when lanes change).
- `nota_engine.h`: document target 5 and `NOTA_MIX_TRACK_SEND`; drop "sends" from the
  "not covered by undo" sentence.

### Managed

- `EngineContracts.cs`: `AutomationTarget.Send = 5`; `MixKind.TrackSend = 8`;
  `MixTarget.Send(trackId, bus)`.
- `Nota.App/SendControl.cs` (new): one helper that wires any send control (knob or mini
  fader) — `MixGesture(MixTarget.Send)` + `Begin/EndAutomationWrite(Send, bus)` + value
  changes through the gesture — so header, Mixer window, Session mixer and Sends card
  behave alike.
- `ArrangementView.cs` `BuildTrackCard`: send row (`BuildSendRow`) for non-return cards when
  the project has returns, the toggle is on and the row is tall enough; large tier with
  sends puts volume and pan on one row; narrow cells fall back to knob-only. Live-follow in
  the existing refresher (skip `Dragging`). Top strip: "Sends" toggle chip, hidden without
  returns.
- `ArrangementView.Sizing.cs`: `showSends` in `TrackViewDto`; `SendsRowMinStandard = 88`.
- `ArrangementView.Automation.cs`: label / range (0..1) / current value / quick-access key /
  menu entries / `FirstAutomatedTarget` for Send; "Send A · <return>".
- `MixerView.cs`, `SessionView.Mixer.cs`, `DeviceChainView.cs`: send controls via `SendControl`.
- `Nota.Mcp/Tools/TrackTools.cs` `SetSend`: `MixEdit.Step(… MixTarget.Send)`;
  `AutomationTools.cs`: "send" target name (bus in `device_index`).
- Localization: new strings in the language tables the localization check requires.

## Project Structure

### Documentation (this feature)

```text
specs/072-arrangement-track-sends/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md · tasks.md
├── contracts/abi.md
├── mockup.html
└── checklists/requirements.md
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                 # NOTA_MIX_TRACK_SEND, target 5 docs
└── src/
    ├── abi/nota_engine_core.cpp               # accept the new mix kind
    └── engine/{EngineTypes.h, Engine.cpp, Automation.h, Engine_Automation.cpp, Engine_Tracks.cpp}
src/managed/
├── Nota.Application/Engine/EngineContracts.cs
├── Nota.App/{SendControl.cs (new), ArrangementView.cs, ArrangementView.Sizing.cs,
│             ArrangementView.Automation.cs, MixerView.cs, SessionView.Mixer.cs, DeviceChainView.cs}
└── Nota.Mcp/Tools/{TrackTools.cs, AutomationTools.cs}
tests/Nota.SmokeTest/{SendAutomationTests.cs (new), Program.cs}
CHANGELOG.md · FEATURES.md
```

**Structure Decision**: the existing engine / managed split; no new projects.

## Complexity Tracking

None.
