# Implementation Plan: Undo for Device Parameters and Arrangement Sections

**Branch**: `fix/undo-device-params-sections` | **Date**: 2026-10-15 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/016-device-param-section-undo/spec.md`

## Summary

Device parameters join the value steps specs/009 introduced for volume and pan: three new
target kinds (audio effect, MIDI effect, normalized plug-in / instrument parameter). The
engine opens a value gesture from the automation-write touch every on-screen control
already sends, so all cards are covered by one engine change; MIDI Learn, Nota Remote and
MCP use the managed gesture helpers. Arrangement sections move into the engine's graph as
project-level data next to scenes and the pan law: an edit publishes a new graph (a normal
snapshot step), the project file stores them, and an older project's sidecar is read once.
See [research.md](research.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed)

**Primary Dependencies**: none new

**Storage**: `project.json` gains `sections` (optional member, no format bump)

**Audio-Thread Impact**: none. Gestures, history and sections are message-thread only;
the audio thread never reads `Graph::sections`. Undo writes parameters through the same
setters a hand move uses.

**Testing**: `scripts/build.sh` → smoke test gains `ParamSectionUndoTests` (`--paramundo`)

**Constraints**: exact values; history depth 128 shared

**Scale/Scope**: engine ~250 lines; managed ~250 lines across ~15 files; smoke ~300 lines

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ no audio-thread change |
| II. Boundary discipline | ✅ C ABI only: extended `NotaMixTarget`, `NotaSection` + 5 functions |
| III. Licensing hygiene | ✅ new files get the SPDX header; no dependency |
| IV. Verification gate | ✅ engine behaviour change → smoke test |
| V. One logical change | ✅ "undo covers device parameters and sections" (#30, #64) |
| VI. Docs in sync | ⏳ CHANGELOG, FEATURES.md, user docs (EN + RU) |
| VII. Spec-first | ✅ |
| VIII. Rules not weakened | ✅ |

## Design

### 1. Engine — parameter value steps

- `Engine.h`: `MixKind` += `DeviceParam = 3, MidiDeviceParam = 4, PluginParam = 5`;
  `MixTarget` / `MixChange` gain `deviceIndex`, `paramIndex` (default −1, appended so
  existing aggregate initialisers keep meaning).
- `Engine.cpp`: `mixValue` / `applyMix` read and write the three kinds through
  `deviceGetParam/SetParam`, `midiEffectGetParam/SetParam`, `pluginParamGet/Set` after
  checking the device exists (else "gone"); `mergeMix` matches on all four fields.
- `Engine_Automation.cpp`: `beginAutomationWrite` (unlatched, parameter target, no touch
  open for it) → `beginMixGesture` and remember the touch; `endAutomationWrite` → end it.
  Both before the existing early returns. `clearUndoHistory` drops open touches.
- Undo comment blocks in `Engine.h` and `nota_engine.h` updated.

### 2. Engine — sections

- `Graph.h`: `struct Section { double startBeat, lengthBeats; std::string name; }`,
  `std::vector<Section> sections` on `Graph`, copied in `inheritGraphLevel`.
- `Engine`: `setSections(std::vector<Section>, bool checkpoint)` (normalise, compare, copy
  the authoring graph, `publish` or `publishRaw`), getters. `reset()` publishes an empty
  graph, which clears them.
- C ABI per [contracts/c-abi.md](contracts/c-abi.md) in `nota_engine_core.cpp`.

### 3. Managed

- `EngineContracts.cs`: `MixKind` values, `MixTarget(Kind, TrackId, DeviceIndex = -1,
  ParamIndex = -1)` + helpers; `SectionInfo` record. `IAudioEngine`: `Sections`,
  `SetSections(list, checkpoint)`. Interop: `NotaMixTarget` layout, section P/Invokes.
- Persistence: `ProjectDocument.Sections`, capture / apply, legacy sidecar migration,
  sidecar removed on save.
- `ArrangementView.Sections.cs`: working copy committed to the engine per edit,
  `ReloadSections()` from the engine; the sidecar code goes. `MainWindow`: reload after
  undo / redo / load / new; Remote reads the same list.
- Gaps (research R2): `GenericMidiBody`, `ArpGrid`, device shell A/B, `ValueEntry`.
- MIDI Learn: parameter targets use `MixMovement` like volume / pan. Remote: macro and XY
  touches use the per-touch gesture. MCP: `Mutate` in an undo group; single-param tools via
  `MixEdit.Step`; `list_sections` / `set_sections`.

### 4. Smoke test + docs

`tests/Nota.SmokeTest/ParamSectionUndoTests.cs` covering every contract guarantee and the
spec's FR-010 list; `Program.cs` full run + `--paramundo`. CHANGELOG, FEATURES.md, user
docs.

## Project Structure

```text
specs/016-device-param-section-undo/  spec, plan, research, data-model, contracts/, quickstart, tasks
src/native/nota.engine/include/nota/nota_engine.h
src/native/nota.engine/src/{Graph.h, Engine.h, Engine.cpp, Engine_Automation.cpp, nota_engine_core.cpp}
src/managed/Nota.Application/Engine/{EngineContracts.cs, IAudioEngine.cs}
src/managed/Nota.Infrastructure/Interop/{NativeMethods.cs, NotaEngine.cs}
src/managed/Nota.Infrastructure/Persistence/{ProjectModel.cs, ProjectService.cs}
src/managed/Nota.App/{ArrangementView.Sections.cs, MainWindow.*.cs, MidiLearnService.cs, DeviceChainView.Shell.cs, Controls/ArpGrid.cs, Controls/ValueEntry.cs, DeviceCards/Midi/GenericMidiBody.cs}
src/managed/Nota.Remote/{RemoteParams.cs, RemoteHub.Apply.cs}
src/managed/Nota.Mcp/{EngineTools.cs, Tools/*}
tests/Nota.SmokeTest/{ParamSectionUndoTests.cs (new), Program.cs}
```

## Out of Scope / Known Gaps

- Devices inside racks and drum-kit pads, rack macros dragged on the rack's macro strip
  slider, rack chain levels (no automation target today).
- Bypass, sidechain routing, device presets.
- A structural edit made by another source during an open gesture merges into it
  (specs/009, unchanged).

## Complexity Tracking

No constitution violations.
