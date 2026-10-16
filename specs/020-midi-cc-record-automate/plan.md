# Implementation Plan: Record and automate MIDI CC

**Branch**: `feat/midi-cc-record-automate` | **Date**: 2026-10-16 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/020-midi-cc-record-automate/spec.md`

## Summary

MIDI clips gain **controller curves**: the sustain pedal, the pitch wheel, channel
pressure and any CC 0–127, one curve per controller, recorded with the notes and played
back to the instrument. The piano roll edits them in its lane under the notes. Track
automation gains a **MIDI controller** target, so a lane can drive any of them across clips.

The approach reuses what is already there. The data reuses the automation breakpoint
type. The live path reuses the instrument-wide expression channel (`pitch −1`). Playback
reuses the per-block expression stream (`ExprEv`) that already carries recorded MPE to
instruments, routed tracks and racks. Recording uses a new lock-free queue beside the
recorded-note one, and the message thread folds it into the take.

One new instrument entry point, `Instrument::controller(ctl, value)`, carries every
controller:
- Its default maps the four controllers the built-ins already understand (wheel,
  pressure, CC74, CC64) onto today's `noteExpression(-1, …)`.
- Built-ins with a *Mod Wheel* parameter take CC1.
- The plug-in host forwards the exact MIDI message.

The work lands as three PRs:
1. Recording and playback (engine, ABI, persistence).
2. Piano-roll lanes.
3. CC automation.

Details and rationale: [research.md](research.md).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine, plug-in host), C# / .NET 10 (managed app, Avalonia UI, MCP)

**Primary Dependencies**: None new. JUCE stays in `pluginhost/` and only gains a `controllerEvent` call for arbitrary CCs.

**Storage**:
- The project format goes from v21 to v22.
- MIDI clips and Session slot clips get an optional `Controllers` list: controller id plus breakpoints.
- Older projects load unchanged. An older Nota keeps the unknown field through `[JsonExtensionData]` and round-trips it on save, but does not play it.

**Audio-Thread Impact**:
- *Recording*: `drainLiveMidi` already coalesces live expression per block. Instrument-wide entries for the record track now also push a `RecordedCtl` into a new SPSC queue (`1 << 14`, AR-4/AR-6, no allocation).
- *Playback*: `gatherInstrumentNotes` and `renderSessionSlotRaw` append controller `ExprEv`s into the existing fixed `kMaxBlockExpr` buffer. Curves are `shared_ptr<const>` inside the immutable snapshot (AR-5) and are only read there.
- *Track automation*: `applyAutomation` sends a controller only when its value changed. The last sent value lives in a fixed per-track array on the audio side.
- Chasing and the end-of-clip release are branches on data already read.
- There are no locks, allocations or I/O anywhere on the audio thread.

**Testing**:
- A new `tests/Nota.SmokeTest/MidiControllerTests.cs` covers recording, playback, chase, end-of-clip release, overdub touched-range replace, ABI round-trip and save/load. It renders offline through the C ABI with a probe instrument.
- A device-free `nota_engine_midi_ctl_selftest` backs it.
- `scripts/build.sh` is the gate.
- UI checks use the headless recipe in [quickstart.md](quickstart.md).

**Target Platform**: macOS, Linux, Windows desktop.

**Project Type**: desktop app (native engine + managed UI).

**Performance Goals**:
- Controller timing on playback is within one audio buffer, and within 64 samples inside a block for moving curves, the same step as recorded MPE.
- 2 minutes of continuous wheel movement thins to well under 100 KB in the saved project (SC-005).

**Constraints**:
- Recording republishes the take on the existing poll cadence. Curves are shared and thinned incrementally, so cloning the track per poll does not copy large arrays.
- Live latency of controllers is unchanged.

**Scale/Scope**:
- Up to 130 controller ids per clip, typically 1–4 in use.
- Recorded points are thinned to the gesture: a few hundred for a 2-minute wheel take.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I. Real-time audio safety | ✅ | New SPSC queue for capture. Fixed buffers for playback. Immutable shared curves in the snapshot. Per-track last-sent array preallocated. No alloc, lock or I/O on the audio thread. |
| II. Boundary discipline | ✅ | All access goes through new `nota_midi_clip_ctl_*`, `nota_session_slot_ctl_*` and `nota_*_controller` functions in `nota_engine.h`, plus automation target 5. JUCE is touched only in `pluginhost/PluginHost.cpp`. |
| III. Licensing hygiene | ✅ | New files (`MidiController.h`, `MidiControllerTests.cs`, `PianoRollView.Controllers.cs`) carry the SPDX header. No new dependency. |
| IV. Verification gate | ✅ | Engine behaviour change → `MidiControllerTests.cs` and the selftest in the smoke run. |
| V. One logical change per PR | ✅ | Three PRs (US1, US2, US3), each green on its own. |
| VI. Docs in sync | ✅ | Each PR gets a CHANGELOG `[Unreleased]` entry, FEATURES.md updates (the "aren't recorded" sentence goes) and nota-docs: piano roll, recording, automation pages, EN + RU. No new shortcut is planned, so nota-shortcuts only needs a check. |
| VII. Spec-first | ✅ | spec → clarify → plan (this) → tasks. |
| VIII. Rules not weakened | ✅ | Nothing to justify. |

Post-design re-check: still all ✅ (see [data-model.md](data-model.md) and [contracts/](contracts/)).

## Project Structure

### Documentation (this feature)

```text
specs/020-midi-cc-record-automate/
├── plan.md              # this file
├── research.md          # decisions R1–R11
├── data-model.md        # controller ids, curves, lanes, queues, DTOs
├── quickstart.md        # how to verify each story
├── contracts/
│   ├── c-abi.md         # new nota_engine.h surface
│   ├── instrument.md    # Instrument::controller and who overrides it
│   └── ui-lanes.md      # piano-roll lane + automation picker behaviour
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h        # + NOTA_CTL_*, clip/slot ctl get/set/list, live controller, target 5, selftest
├── src/MidiController.h              # NEW: ctl ids, ControllerCurve (value at beat, step vs linear), thinning
├── src/MidiClip.h                    # MidiClip::controllers
├── src/Instrument.h                  # virtual controller(ctl, value) with default mapping
├── src/NoteExpression.h              # ExprEv dim for controllers (kCtlDimBase)
├── src/MpeInput.h                    # emit every CC / wheel / pressure as a controller on non-member channels
├── src/Automation.h                  # AutomationTarget::MidiController = 5
├── src/Engine.h                      # RecordedCtl queue, take ctl state, live ctl latest values, per-track last-sent
├── src/Engine_Render.cpp             # capture in drainLiveMidi / routed; playback, chase, end release; poll() fold-in
├── src/Engine_Automation.cpp         # applyAutomation target 5; precedence; hardware write latch
├── src/Engine_Tracks.cpp             # split / trim / join / consolidate / clipboard carry controllers
├── src/Engine_Session.cpp            # slot controllers, loop-wrap chase
├── src/Synth.h, BassSynth.h, OperatorSynth.h, Consort.h, VoltSynth.h, …  # CC1 → Mod Wheel param where present
├── src/nota_engine_track.cpp, nota_engine_automation.cpp, nota_engine_session.cpp  # ABI shims
└── pluginhost/PluginHost.cpp         # controller(): exact CC / wheel / pressure to the plug-in

src/managed/
├── Nota.Application/Engine/EngineContracts.cs, IAudioEngine.cs   # MidiController ids, AutomationTarget.MidiController
├── Nota.Infrastructure/Interop/NativeMethods.Tracks.cs, NotaEngine.Tracks.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs, ProjectMigrations.cs  # v22
├── Nota.App/PianoRollView.Expression.cs     # lane tabs gain the controller tab
├── Nota.App/PianoRollView.Controllers.cs    # NEW: clip-wide controller lane (curve + sustain blocks)
├── Nota.App/ClipEditorView.cs               # host wiring (get/set, undo)
├── Nota.App/ArrangementView.Automation.cs   # target picker: "MIDI controllers" submenu; ranges/labels
└── Nota.Mcp/Tools/MidiTools.cs, AutomationTools.cs   # get/set clip controllers; midi_cc automation target

tests/Nota.SmokeTest/MidiControllerTests.cs  # NEW
```

**Structure Decision**: The existing engine/managed split holds. Each story touches the
files below.
- **US1 (PR 1)**: engine, ABI, interop, persistence and smoke. It also includes the read side of the piano roll: no lane yet, but recorded data is saved and plays back.
- **US2 (PR 2)**: piano roll and clip MCP tools.
- **US3 (PR 3)**: automation target, picker, recording, MCP automation.

## Complexity Tracking

No constitution violations; nothing to justify.
