---

description: "Task list for recording and automating MIDI CC (#69)"
---

# Tasks: Record and automate MIDI CC

**Input**: Design documents from `/specs/020-midi-cc-record-automate/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The constitution (IV) and FR-018 require smoke coverage of recording, playback, chase, the end-of-clip release and the new C ABI. The UI is checked with the headless capture recipe in quickstart.md.

**Organization**: Tasks are grouped by user story. Each story is one pull request (spec Clarifications): US1 → PR 1, US2 → PR 2, US3 → PR 3.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US3)

## Path Conventions

- Engine: `src/native/nota.engine/src/`, C ABI `src/native/nota.engine/include/nota/nota_engine.h`, plug-in host `src/native/nota.engine/pluginhost/PluginHost.cpp`
- Managed: `src/managed/Nota.Application/`, `src/managed/Nota.Infrastructure/`, `src/managed/Nota.App/`, `src/managed/Nota.Mcp/`
- Smoke tests: `tests/Nota.SmokeTest/`

---

## Phase 1: Setup

**Purpose**: Test scaffolding the stories add to.

- [X] T001 Create `tests/Nota.SmokeTest/MidiControllerTests.cs`:
  - Start it with the two-line AGPL SPDX header.
  - Add a `static class MidiControllerTests` exposing `IEnumerable<(bool, string)> Run()` and a `Render(NotaEngine, int blocks)` helper modelled on `MpeTests.Render` (`tests/Nota.SmokeTest/MpeTests.cs:26`).
  - Register it in `tests/Nota.SmokeTest/Program.cs` next to `MpeTests.Run()`. There are two registration sites, around L379 and L14647; add it at both, matching how `LiveRecordTests` is registered.

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The controller id space, the curve type, the instrument entry point and the wire through the engine. Every story builds on these. They ship in PR 1.

**⚠️ CRITICAL**: No user-story work can begin until this phase is complete.

- [X] T002 [P] Create `src/native/nota.engine/src/MidiController.h` (SPDX header, `namespace nota`) with:
  - **Constants**: `kCtlPitchWheel = 128`, `kCtlChannelPressure = 129`, `kCtlCount = 130`, `kCtlDimBase = 16`.
  - **Helpers**:
    - `ctlIsReserved(ctl)`: true for CC 6, 38, 96–101, 120–127 and any ctl > 129.
    - `ctlIsSwitch(ctl)`: CC 64–69.
    - `ctlRest(ctl)`: 0 for all.
    - `ctlRange(ctl)`: wheel −1..+1, others 0..1.
  - **`struct ControllerCurve { int32_t ctl; std::vector<AutomationPoint> points; float valueAt(double beat) const; }`**:
    - Points are sorted by beat, clip-local, may be < 0, at most one per beat.
    - Step ctls take the last point ≤ beat.
    - Linear ctls reuse `AutomationLane::shape`.
    - Held before the first and after the last point.
  - **`thinCtlCurve(points, tol)`**: RDP over `AutomationPoint`, mirroring `thinExprCurve` in `MidiClip.h`.
  - **`dedupeSwitch(points)`**: drops points equal to the previous value.
- [X] T003 Extend `MidiClip` in `src/native/nota.engine/src/MidiClip.h` with `std::vector<std::shared_ptr<const ControllerCurve>> controllers;`. There is at most one per ctl, sorted by ctl, and empty means none. Add the helpers `const ControllerCurve* controller(int32_t ctl) const` and `void setController(int32_t ctl, std::vector<AutomationPoint> pts)` (empty removes). Include `MidiController.h` (depends on T002).
- [X] T004 [P] Add `virtual void controller(int32_t ctl, float value)` to `src/native/nota.engine/src/Instrument.h`. The default maps 128 → `noteExpression(-1, ExprBend, v)`, 129 → `(-1, ExprPressure, v)`, 74 → `(-1, ExprSlide, v)` and 64 → `(-1, ExprSustain, v)`, and ignores everything else (contracts/instrument.md).
- [X] T005 [P] Override `controller` in `src/native/nota.engine/src/RackInstrument.h` to forward to every chain's instrument, the same way its `noteExpression` override does (L60). This also covers `DrumRack.h`.
- [X] T006 [P] Make sustain continuous on the wire. In `src/native/nota.engine/src/MpeInput.h` `controlChange` case 64, emit `v / 127.0f` instead of `0|1`. Check that `Keys.h` L191, `PhysicalSynth.h` and every other `ExprSustain` reader threshold at `>= 0.5f`.
- [X] T007 Route controllers through the live path in `src/native/nota.engine/src/MpeInput.h`.
  - Add `virtual void mpeController(int32_t ctl, float value)` to `MpeInput::Sink`.
  - On non-member channels: emit the wheel (0xE0) and channel pressure (0xD0) as `mpeController(128|129, …)`, and CC74 as `mpeController(74, …)`, instead of `mpeExpression(-1, …)`.
  - CC64 on any channel goes to `mpeController(64, v/127)`.
  - In `controlChange`'s `default:` branch, emit every other non-reserved CC as `mpeController(cc, v/127)`. On member channels CC74 stays per-note.
  - Update the selftest expectations in `src/native/nota.engine/src/nota_engine_audio.cpp` (~L149–181) so `nota_mpe_selftest` still passes.
  - Depends on T002 and T006.
- [X] T008 In `src/native/nota.engine/src/Engine.h` and `Engine_Recording.cpp`:
  - Implement `MpeSink::mpeController` and new `Engine::controller(ctl, v)` / `Engine::trackController(trackId, ctl, v)`. They push `MidiEvent{on=false, pitch=-1, velocity=v, trackId, expr=kCtlDimBase+ctl}` through the same producer locks as `noteExpression`.
  - Rewrite `Engine::noteExpression` / `trackNoteExpression` with `pitch == -1 && dim <= 3` to the matching controller (128/129/74/64).
  - Store each live controller's latest value and a touch counter in `std::atomic<float> liveCtl_[kCtlCount]` and `std::atomic<uint32_t> liveCtlTouch_[kCtlCount]`. These are used by take seeding (T018) and automation writing (T046).
- [X] T009 Dispatch controller dims in `src/native/nota.engine/src/Engine_Render.cpp`:
  - In `playEvents` (L124), send `xev[x].dim >= kCtlDimBase` to `inst.controller(dim - kCtlDimBase, value)` and keep `noteExpression` for the rest.
  - In `applyLiveExpression` (L723) and the slot monitor lambda in `renderSessionSlotRaw` (~L949), do the same split.
  - In `drainLiveMidi`'s coalescing (L540), key controllers by (track, expr), which already works since `pitch` is −1.
- [X] T010 [P] Override `controller` in the plug-in host, `src/native/nota.engine/pluginhost/PluginHost.cpp`, next to `noteExpression` (L711):
  - 0..127 → `juce::MidiMessage::controllerEvent(1, ctl, u7(v))`, with CC64 exact (no threshold).
  - 128 → `pitchWheel(1, jlimit(0,16383, lround((v+1)*8191.5)))`.
  - 129 → `channelPressureChange(1, u7(v))`.
  - Remove the pitch −1 branches from `noteExpression` that the default `controller` now covers, so nothing is sent twice.
- [X] T011 [P] Map CC1 to the *Mod Wheel* parameter in built-ins that have one. Override `controller` in `src/native/nota.engine/src/BassSynth.h`, `OperatorSynth.h` and `Consort.h`, and in any other synth whose parameter ids include `"modwheel"` (find them with `grep -n '"modwheel"' src/native/nota.engine/src/*.h`). For `ctl == 1`, set `ModWheel` to `v` with the same write automation uses, then call `Instrument::controller(ctl, v)`.
- [X] T012 Add the C ABI declarations from [contracts/c-abi.md](contracts/c-abi.md) to `src/native/nota.engine/include/nota/nota_engine.h`:
  - `NOTA_CTL_PITCH_WHEEL` / `NOTA_CTL_CHANNEL_PRESSURE` / `NOTA_CTL_COUNT`;
  - `nota_engine_controller` / `nota_track_controller`;
  - `nota_midi_clip_ctl_list/get/set`, `nota_session_ctl_list/get/set`;
  - `nota_engine_midi_ctl_selftest`.

  Implement the live pair in `src/native/nota.engine/src/nota_engine_core.cpp` next to `nota_engine_note_expression`. Add the new symbols to `src/native/nota.engine/nota_exports.map` and `nota_exports.txt`.
- [X] T013 [P] Add the managed contracts:
  - `src/managed/Nota.Application/Engine/EngineContracts.cs`: a `public static class MidiController` with `PitchWheel = 128`, `ChannelPressure = 129`, `Count = 130`. It provides `Name(ctl)` (standard CC names: "Mod wheel", "Breath", "Volume", "Pan", "Expression", "Sustain", …, "CC 3" when unnamed), `ShortName(ctl)`, `IsSwitch(ctl)`, `IsReserved(ctl)` and `Range(ctl)`. The values must match data-model.md exactly.
  - `IAudioEngine.cs`: `SendController(int ctl, float value)`, `SendTrackController(int trackId, int ctl, float value)`, `GetMidiClipControllers(track, clip) → int[]`, `GetMidiClipController(track, clip, ctl) → NotaAutomationPoint[]`, `SetMidiClipController(track, clip, ctl, NotaAutomationPoint[])`, plus `GetSessionControllers` / `GetSessionController` / `SetSessionController`.
- [X] T014 Add P/Invokes in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs` and implementations in `NotaEngine.Tracks.cs` for every T012 function, following the `nota_midi_clip_env_get/set` pattern: count-then-fill (`out = null`). Also implement them in any test or fake `IAudioEngine` the build requires (find with `grep -rln ": IAudioEngine" src tests`). Depends on T012 and T013.

**Checkpoint**: Controllers flow live from MIDI input to built-ins and plug-ins (FR-004), and the ABI compiles. Nothing is recorded yet.

---

## Phase 3: User Story 1 — Record the sustain pedal, wheels and CCs with the notes (Priority: P1) 🎯 MVP · PR 1

**Goal**: Takes store every controller next to the notes, play them back with chase and end-of-clip release, carry them through clip edits, and save and load them (FR-001–FR-009, FR-018–FR-021).

**Independent Test**: Record a phrase with the pedal, mod wheel and a bend. Play it from the start and from the middle; the instrument follows and no note hangs after stop (quickstart PR 1, steps 1–7).

### Tests for User Story 1

> Write first. They fail until the implementation lands.

- [X] T015 [P] [US1] In `tests/Nota.SmokeTest/MidiControllerTests.cs`, add `RoundTrip()`:
  - Set CC64 (step), CC1 and wheel curves on a MIDI clip through `SetMidiClipController`. Read back with `GetMidiClipControllers` / `GetMidiClipController`.
  - Removal with 0 points works.
  - Reserved CCs are refused: 6, 38, 96–101, 120–127, and ctl > 129.
  - Undo restores a set.
  - Check 2 in contracts/c-abi.md.
- [X] T016 [P] [US1] In `MidiControllerTests.cs`, add `Recording()`, modelled on `MpeTests.Recording` (`MpeTests.cs:184`):
  - Arm, record, then `SendTrackController` CC64 1→0, a wheel ramp −1→1 over 2 beats, and CC1 steps. Stop.
  - The take holds those curves at the expected beats within one block. The wheel ramp is thinned to ≤ 16 points. CC64 has exactly two points.
  - Recording starts with the pedal already down: a point at the take start with value 1.
  - Checks 3 and 11: poly aftertouch 0xA0 through `nota_engine_note_expression(pitch, 1, v)` lands in the note's pressure curve.
- [X] T017 [P] [US1] In `MidiControllerTests.cs`, add `Playback()`, `Chase()`, `EndRelease()`, `OverdubFold()`, `PedalOnlyReplace()` and `Persistence()` (checks 4–8 and 10):
  - **Probe**: read what reached the instrument with `nota_track_delivered_controller` (added to the ABI for this; no test-only instrument kind needed).
  - **Persistence**: save and load the project through `ProjectService`. A v21 file loads with no controllers.

### Implementation for User Story 1

- [X] T018 [US1] Capture on the audio thread, in `src/native/nota.engine/src/Engine.h` and `Engine_Render.cpp`:
  - Add `struct RecordedCtl { int32_t ctl; double beat; float value; int32_t pass; }` with `using RecordedCtlQueue = SpscRingBuffer<RecordedCtl, 1u << 14>;` and an `Engine::recordedCtl_` member.
  - At the end of `drainLiveMidi`, when `rec && timed`, push every coalesced `liveExpr_` entry with `expr >= kCtlDimBase` whose `trackId < 0 || trackId == recTrack`, at `nowBeat` (slot-local for Session takes) with `recordPass_`.
  - On `takeStarted`, seed every ctl whose `liveCtl_` value is off rest (`!= ctlRest`, wheel `!= 0`) as a point at `nowBeat`.
  - In `renderInstrumentRaw`, when `captureRouted`, push the source's controller `ExprEv`s (dim ≥ `kCtlDimBase`) at `(blockStart + off) / spb + countInClockOffset()`.
  - Allocate nothing, and drop on a full queue the way `recorded_` does (AR-6).
- [X] T019 [US1] Fold the queue into the take in `Engine::poll()` in `src/native/nota.engine/src/Engine_Render.cpp`, after `collectRecordedExpr()`. Keep message-thread per-take state, `std::array<CtlFold, kCtlCount> ctlFold_` (pending raw points, last committed point, replace range start, range pass), and reset it in `setRecording` (`Engine_Recording.cpp:245`).
  - **Conversion**: each poll converts absolute beats to clip-local beats.
  - **Thinning**:
    - Switch ctls use `dedupeSwitch`.
    - Linear ctls use `thinCtlCurve`, anchored on the last committed point, with tolerances CC 0.004, wheel 0.002, pressure 0.004 (research R5).
  - **Replace range** (FR-021): remove the clip curve's existing points in `[rangeStart, latest]`, insert the new points, and publish a new `shared_ptr<const ControllerCurve>`.
  - **Pickups**: when the pickup left-edge move shifts `clip.notes` (L306), shift every controller point by the same delta.
  - **Session takes**: points use `fmod` into `[0, L)`, as notes do.
  - Republish with `republishWithTrackRaw` in the same call that publishes the notes (no extra undo step). A poll that has only controller points must still republish: relax the `batch.empty()` early return.
- [X] T020 [US1] Close replace ranges in `src/native/nota.engine/src/Engine_Render.cpp` and `Engine_Recording.cpp`:
  - **Loop wrap**: `onLoopWrap` (L1128) pushes a marker (`RecordedCtl{ctl=-1, beat=loopEnd, pass}`). `poll` closes every open range at the loop end.
  - **Record stop**: in `setRecording(false)`, close open ranges at the stop beat with a junction point carrying the old curve's value there.
  - **Replace mode**: when `replacePending` removes earlier-pass notes, also remove this take's earlier-pass controller points. Track them per pass in `ctlFold_`.
  - Controllers never call `triggerPassReplace`, so a pedal-only pass keeps the notes (clarification 2).
- [X] T021 [US1] Emit clip controllers on playback in `gatherInstrumentNotes` (`src/native/nota.engine/src/Engine_Render.cpp:655`). For each active clip with `controllers`, push `ExprEv{off, -1, kCtlDimBase+ctl, value}` into `xev`:
  - at each point inside `[blockStart, blockStart+frames)`;
  - every 64 samples while a linear curve moves (mirror `noteExprEvents`, L154);
  - at offset 0 when `chaseNotes_`, or when the block contains the clip start, with `valueAt` there (FR-005).
  - Skip a ctl that has a track automation lane with points and `target == MidiController`. Use a precomputed per-track bitset; for PR 1 it is always empty and is filled in T045.
  - Respect `kMaxBlockExpr`.
- [X] T022 [US1] Release at clip end and on stop, in `src/native/nota.engine/src/Engine_Render.cpp` and `Track.h`:
  - Add audio-side `float lastCtl_[kCtlCount]` (NaN-initialised) on `Track`, written from `playEvents` for controller dims.
  - In `gatherInstrumentNotes`, when a clip's end falls in the block and its CC64 or wheel value at the end is off rest, emit `ExprEv` CC64 = 0 and wheel = 0 at the end offset. Sort the reset ahead of a following clip's chase at the same offset by using a stable sort with resets inserted first.
  - On transport stop, find where `allNotesOff` is sent on stop (`grep -n "allNotesOff" src/native/nota.engine/src/Engine_Render.cpp`). Send `controller(64, 0)` and `controller(128, 0)` to every instrument whose `lastCtl_` for those is off rest (FR-006).
- [X] T023 [US1] Play slot controllers in `renderSessionSlotRaw` (`src/native/nota.engine/src/Engine_Render.cpp:888`):
  - For iterations `i0..i0+1`, emit the slot clip's controllers like T021.
  - At each loop start `it * L` inside the block, emit a chase of `valueAt(0)`.
  - On a one-shot slot's end, release like T022.
- [X] T024 [US1] Carry controllers through clip edits in `src/native/nota.engine/src/Engine_Tracks.cpp` (data-model.md):
  - **`splitClip`**: the left clip keeps points `< b` plus a point at `b` with `valueAt(b)`. The right clip gets a point at 0 with `valueAt(b)`, then the rest shifted by −b.
  - **`trimClip`** and left-edge moves: shift points with the clip start, as notes are shifted.
  - **`splitClipsAtRange`**, **`consolidateRange`** and the join path near L1804 (`stitchEnvelopes`): stitch curves per ctl, with a rest-value point where a source clip ended inside the result.
  - **`copyClip` / `pasteClip` / block paste / cross-track move**: the vector copies with the clip, so verify it, and fix any place that rebuilds a `MidiClip` field by field.
  - **Quantize**: unchanged.
- [X] T025 [US1] Carry controllers between slot and arrangement in `src/native/nota.engine/src/Engine_Session.cpp`: `nota_session_slot_to_arrangement`, `pasteClipBlockToSession` and any arrangement → slot path. Points are shifted into or out of the slot's `[0, L)`, and points outside are dropped.
- [X] T026 [US1] Implement the clip and slot controller ABI in `src/native/nota.engine/src/nota_engine_track.cpp` and `nota_engine_session.cpp`, through new `Engine` methods in `Engine_Automation.cpp` next to `getMidiClipEnvelope` / `setMidiClipEnvelope` (L741):
  - `list` returns sorted ctls.
  - `get` uses count-then-fill.
  - `set` sorts the points, keeps the last point per beat, refuses reserved ctls (`NOTA_ERR_*` as the env setters do), and takes one undo checkpoint.
- [X] T027 [US1] Implement `nota_engine_midi_ctl_selftest` in `src/native/nota.engine/src/Engine_Automation.cpp` (device-free, numbered checks, 0 = pass). It covers:
  - `valueAt` step and linear;
  - split and trim carry;
  - chase value;
  - end release emission;
  - precedence skip (with a lane injected directly);
  - the overdub touched-range fold, run on synthetic `RecordedCtl`s.

  Call it from `MidiControllerTests.Run()` (check 1).
- [X] T028 [P] [US1] Persistence v22 in `src/managed/Nota.Infrastructure/Persistence/`:
  - **Model**: add `ControllerCurveDto { int Ctl; AutomationPointDto[] Points }` to `ProjectModel.cs`, and add `ControllerCurveDto[]? Controllers` to `MidiClipDto` (L341) and `SessionSlotDto` (L378).
  - **Save**: in `ProjectService.cs`, write the controllers next to the clip envelopes (L194–204) and the slot notes.
  - **Load**: restore them next to L638–641 and in the slot load. Drop reserved or unknown ctls with a `warnings.Add`.
  - **Version**: bump `CurrentFormatVersion` to 22 (L34) and add a no-op step in `ProjectMigrations.cs` if the file requires one per version.
  - Older Nota builds keep unknown fields through `[JsonExtensionData]`, so nothing else is needed.
- [X] T029 [US1] Verify that note edits keep controllers: `src/managed/Nota.App/ClipEditorView.cs` note edits (set-notes calls) must not rebuild the `MidiClip` without its `controllers`. Check the engine side of `nota_set_notes` in `src/native/nota.engine/src/nota_engine_track.cpp` and fix it if needed. Controllers are not drawn in PR 1 (FR-022 and US2).
- [X] T030 [US1] Add MCP tools in `src/managed/Nota.Mcp/Tools/MidiTools.cs`, following the existing tool style and argument validation:
  - `get_clip_controllers(track, clip)`: the list plus the points per ctl, with names from `MidiController.Name`.
  - `set_clip_controller(track, clip, ctl, points)`: an empty list removes the curve.
  - `ctl` accepts a number, "pitch_wheel", "channel_pressure", "sustain" or "mod_wheel".

  If the MCP golden tests list tools (`tests/Nota.SmokeTest/AiGoldenTests.cs`, `McpTestSupport.cs`), update them.
- [X] T031 [US1] Run `MidiControllerTests` through `scripts/build.sh` and fix until checks 1–8, 10 and 11 pass.

**Checkpoint**: PR 1 is complete. Recording and playback work end to end and are saved with the project.

---

## Phase 4: User Story 2 — See and edit controller lanes in the piano roll (Priority: P2) · PR 2

**Goal**: A fifth lane tab with a controller picker. The sustain pedal is drawn as blocks, every other controller as an editable curve, and each gesture is one undo step (FR-010–FR-013, contracts/ui-lanes.md).

**Independent Test**: In clips with and without recorded data, switch to Sustain, Mod wheel, Pitch wheel, Channel pressure and CC 74. Draw, move and delete, undo each step, and confirm playback follows (quickstart PR 2).

- [ ] T032 [US2] Extend `LaneMode` in `src/managed/Nota.App/PianoRollView.Expression.cs` (L33) with `Controller`, and add a `_laneCtl` field.
  - **Default**: the first ctl with data in the clip, else 64. Remember it per editor session, like `_laneMode`.
  - **Height**: `SetLaneMode(Controller)` swaps in the new lane control at `ExprH`.
  - **Tab**: `LaneTabs` (L134) gains a fifth tab labelled with `MidiController.ShortName(_laneCtl)`, keeping the existing tab styling.
- [ ] T033 [US2] Add the controller picker to the fifth tab in `src/managed/Nota.App/PianoRollView.Expression.cs`. A click on the active tab or its chevron opens a menu:
  - first, the ctls with data in this clip, each marked with the automation picker's dot (reuse `AutoDot()` from `ArrangementView.Automation.cs:612`, or lift it into a shared helper in `ClipEditorKit.cs`);
  - then Sustain, Mod wheel, Pitch wheel, Channel pressure, Expression (CC11) and Breath (CC2);
  - then **Other CC ▸** with 0–127 in groups of 16, labelled "CC 7 — Volume", skipping reserved CCs.

  Labels are in sentence case.
- [ ] T034 [US2] Create `src/managed/Nota.App/PianoRollView.Controllers.cs` (SPDX header, `partial class PianoRollView`) with a `ControllerLane` control. It reads the curve through a host delegate `Func<int, NotaAutomationPoint[]>? GetController` and writes through `Action<int, NotaAutomationPoint[]>? SetController`, mirroring `CreateExpression`.
  - **Time mapping**: the same as the notes over the whole clip. A pickup before the clip start is drawn faintly.
  - **Ink**: neutral (`NotaPalette.VizInk`, `CurvePen`). Only the hovered point or block, and the selection, use the accent.
- [ ] T035 [US2] Switch controllers (CC64–69) in `PianoRollView.Controllers.cs`:
  - **Drawing**: blocks where the value is ≥ 0.5. Fill height shows partial values (clarification 3).
  - **Drag on empty lane**: draws a block that snaps to the grid; ⌘ or Ctrl turns snap off.
  - **Drag an edge**: resizes the block.
  - **Drag the body**: moves the block.
  - **Delete**: double-click, or Delete with the block selected.
  - **Right-click**: *Clear <name>*.
  - A block is serialised as two step points: on at its start, off at its end.
- [ ] T036 [US2] Continuous controllers in `PianoRollView.Controllers.cs`:
  - **Drawing**: a curve with points. The range lines are 0–127 with a midline label. The pitch wheel draws a centre line and ±full travel, and points within 2% of centre snap to 0.
  - **Freehand drag**: replaces the points under the stretch; ⌥ draws a straight line.
  - **Points**: drag a point to move it between its neighbours; double-click adds or deletes a point.
  - **Right-click**: *Clear <name>*.
  - **Hover readout**: the value in the controller's units ("64", "+0.42", "on / off").

  Reuse the point-drag and freehand helpers from `PianoRollView.Expression.cs`. If they are private to the MPE lane, lift them into a shared helper.
- [ ] T037 [US2] Wire the host in `src/managed/Nota.App/ClipEditorView.cs`:
  - Set `GetController` / `SetController` to `IAudioEngine.GetMidiClipController` / `SetMidiClipController` for arrangement clips, and the `Session*` variants for slots.
  - `SetController` is called once on gesture release (one undo step, FR-013).
  - Refresh the lane after undo and redo and when the take republishes during recording, the same way notes refresh.
- [ ] T038 [US2] Add a precedence note to the controller lane. When the track has a target-5 automation lane with points for `_laneCtl`, show the one-line note "Track automation overrides this lane" in `PianoRollView.Controllers.cs`. The data comes from `IAudioEngine.AutomationLaneInfo`. It is inert until US3 adds the target; no US3 dependency for compiling, because the enum value is added in T042.
- [ ] T039 [US2] Visual check following quickstart PR 2. Run the headless Avalonia render, or the env-guarded in-app capture with `NOTA_DATA_DIR`, for Sustain blocks and a CC curve in Studio Dark and Studio Light. Then run the `DesignTokenCheck*` tests in the smoke suite (labels, numbers, radius allow-lists). Fix violations, or add allow-list rows only where the design guide permits.

**Checkpoint**: PR 2 is complete. Controllers are visible and editable.

---

## Phase 5: User Story 3 — Automate a CC on an instrument track (Priority: P3) · PR 3

**Goal**: Track automation lanes can target a MIDI controller and take precedence over the clip's data. Hardware moves record into them with Record engaged (FR-014–FR-017).

**Independent Test**: Add a CC74 lane on a plug-in instrument track and draw it across two clips; the plug-in follows. Add a Mod wheel lane and record it with the hardware wheel. Where the lane has points, it overrides clip data (quickstart PR 3).

### Tests for User Story 3

- [ ] T040 [P] [US3] In `MidiControllerTests.cs`, add `Automation()` (check 9 plus lane playback):
  - A target-5 lane on CC1 with points 0→1 drives the probe's `controller(1, …)` across two clips and is sent only on change.
  - The clip's own CC1 is ignored where the lane has points.
  - Persistence round-trips the lane.
  - Adding a target-5 lane on an audio track, or for a reserved CC, fails.
- [ ] T041 [P] [US3] In `MidiControllerTests.cs`, add `AutomationRecord()`:
  - Turn on `nota_engine_set_automation_record(1)`, arm the track with a CC1 lane, play, and send `SendTrackController(1, …)` values. `poll` writes them into the lane, latched until stop.
  - While the lane exists, the take captures no CC1 into the clip.

### Implementation for User Story 3

- [ ] T042 [P] [US3] Add the target:
  - `MidiController = 5` to `AutomationTarget` in `src/native/nota.engine/src/Automation.h` (L19) and in `src/managed/Nota.Application/Engine/EngineContracts.cs` (L252).
  - Document target 5 in the M9 comment block of `nota_engine.h` (L318).
- [ ] T043 [US3] Accept target 5 in `Engine::addAutomationLane` (`src/native/nota.engine/src/Engine_Automation.cpp:490`): `deviceIndex = -1`, `paramIndex = ctl`. Refuse it on non-instrument tracks and for reserved ctls, and reuse an existing lane for the same ctl. Make `automationLaneInfo`, `remapAutomationAfterDeviceChange` (unaffected, ctl lanes have no device) and `currentTargetValue` handle it. `currentTargetValue` returns `liveCtl_[ctl]`, or the track's `lastCtl_` when the controller is untouched.
- [ ] T044 [US3] Play target-5 lanes in `applyAutomation` (`src/native/nota.engine/src/Engine_Automation.cpp:25`): `v = lane.valueAt(beat)`. If `t.instrument` exists and `v` differs from `t.lastCtl_[ctl]`, call `t.instrument->controller(ctl, v)` and update `lastCtl_`. Clamp to `ctlRange`.
- [ ] T045 [US3] Precedence. When a track snapshot is built or published (`cloneTrack` or the automation setters), compute a per-track `std::bitset<kCtlCount> ctlAutomated` of ctls that have a target-5 lane with points. Store it on `Track` in `src/native/nota.engine/src/Track.h`. `gatherInstrumentNotes` (T021) and `renderSessionSlotRaw` (T023) skip clip emission for those ctls.
- [ ] T046 [US3] Record target-5 lanes from hardware, in `Engine::poll()` (`src/native/nota.engine/src/Engine_Render.cpp:209`) in the automation block. When `rec && playing`:
  - for each ctl whose `liveCtlTouch_` changed since the last poll, and where the armed record track has a target-5 lane for it, call `beginAutomationWrite(track, 5, -1, ctl, nullptr, /*latched*/true)` once;
  - `sampleAutomationWritesAt` then reads `currentTargetValue`;
  - stop ends the write through the existing `finishAllWrites`.

  Also make T018 capture skip ctls in the record track's `ctlAutomated` (automation wins).
- [ ] T047 [US3] Add the target picker in `src/managed/Nota.App/ArrangementView.Automation.cs`:
  - **Submenu**: in the picker (L355), add **MIDI controllers ▸** after the instrument's parameters, on instrument tracks only. It lists the T033 entries without clip dots and with the automated-target dot. Leaf key `"C:{ctl}"`; apply sets `t.AutoTarget = MidiController` and `t.AutoParamIndex = ctl`.
  - **Labels**: extend the label switch (L27) with `MidiController.ShortName(ctl)`.
  - **Range**: extend the range switch (L40): the wheel is (−1, 1), others (0, 1). Display 0–127 for CCs.
  - **Value**: extend the current-value switch (L56).
  - **Matching**: extend the target-matching code (L83, L114, L126, L147).
- [ ] T048 [US3] Persist target-5 lanes in `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` (L258–271 save, L685–707 load). They go through the existing `AutomationLaneDto` with `Target = 5`, `DeviceIndex = -1` and `ParamIndex = ctl`. Verify the generic `AddAutomationLane` path covers them; add a warning on refusal.
- [ ] T049 [US3] Add MCP support in `src/managed/Nota.Mcp/Tools/AutomationTools.cs`: accept `target: "midi_cc"` with `ctl` (number or name, as T030) to create, read and write lanes, and report target-5 lanes with their controller name. Update the golden tool lists if they exist.
- [ ] T050 [US3] Visual check of the picker submenu and a drawn CC74 lane (quickstart PR 3), with the same capture recipe and `DesignTokenCheck*` run as T039.

**Checkpoint**: PR 3 is complete. All three stories work.

---

## Phase 6: Polish & Cross-Cutting Concerns

**Purpose**: Release-quality wrap-up. Repeat the parts relevant to each PR before it is opened.

- [ ] T051 [P] FEATURES.md via the `nota-features` skill. **PR 1**: replace "The pitch wheel, channel pressure and sustain pedal aren't recorded" (FEATURES.md ~L303) with the recording capability, including all CCs live to plug-ins, CC1 → Mod Wheel on built-ins and poly aftertouch as per-note pressure. **PR 2**: the controller lanes. **PR 3**: the MIDI controller automation target (Automation section, ~L405).
  - PR 1 done 2026-10-16: new "MIDI controllers in clips" bullet; the "aren't recorded" sentence removed; MCP section updated.
- [ ] T052 [P] User docs, EN and RU, via the `nota-docs` skill: the recording page (PR 1), the piano roll lanes page (PR 2) and the automation page (PR 3), with screenshots from T039 and T050.
  - PR 1 done 2026-10-16: `control/midi` (new "Sustain pedal, mod wheel and other controllers" section) and `editing/recording` ("Pedal, wheels and controllers", loop pass rule), EN + RU; `coverage.yml` updated; `npm run build` / `npm run check` clean. No screenshots: no UI change in PR 1. Not committed in ../nota-docs.
- [ ] T053 [P] `CHANGELOG.md` `[Unreleased]`, one entry per PR, in the repo's existing style: Added for recording and playback of controllers, then controller lanes, then CC automation. Changed for the sustain pedal now reaching plug-ins with its exact value, and every CC passed to plug-ins live.
  - PR 1 done 2026-10-16: Added "Record the sustain pedal, the wheels and any CC"; Changed "Hosted plug-ins get every MIDI controller".
- [ ] T054 Run `nota-shortcuts` to confirm no binding changed; mark N/A if none did. The lane gestures reuse the existing ones.
  - PR 1: N/A, no binding changed.
- [ ] T055 Performance pass: record 2 minutes of continuous wheel movement and check SC-005 (project growth ≤ 100 KB, no new dropouts in `TimingBudget`). Check that the per-poll republish stays cheap, since curves are shared.
  - PR 1 done 2026-10-16: `MidiControllerTests.LongTake` (2 min of wheel + mod wheel over a played part, ~30 Hz polls). First run: 7502 points / +1.1 MB, because each poll thinned its own short run. Fixed: a moving curve keeps its last thinned segment back between polls (committed at range end/stop, capped at 512 points); the controller DTO became parallel arrays rounded to 1e-6 beat / 1e-4 value. Now 1690 points / +84.5 KB; render mean 2.6 → 2.9 µs per 256-frame block, worst 34 µs of 5333 µs.

### Nota closing tasks *(PR gates, for every PR)*

- [ ] T056 Run `scripts/build.sh` until native, managed and the smoke test are green.
  - PR 1: green 2026-10-16 (`scripts/build.sh` exit 0, smoke passed, no new compiler warnings).
- [ ] T057 Check that every new source file carries the AGPL-3.0-only SPDX two-line header: `MidiController.h`, `PianoRollView.Controllers.cs`, `MidiControllerTests.cs` and any probe instrument.
  - PR 1: checked (`MidiController.h`, `MidiControllerTests.cs`).
- [ ] T058 New dependency: none → N/A.
  - PR 1: N/A.
- [ ] T059 User-visible change → `CHANGELOG.md` entry (T053).
- [ ] T060 FEATURES.md and docs (T051, T052); shortcuts (T054).
- [ ] T061 Engine behaviour change → smoke coverage (T015–T017, T027, T040, T041).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (T001)**: none.
- **Foundational (T002–T014)**: depends on Setup and blocks every story.
  - T003 needs T002.
  - T007 needs T002 and T006.
  - T008 needs T007.
  - T009 needs T004 and T008.
  - T012 → T014; T013 → T014.
- **US1 (T015–T031)**: after Foundational.
  - T018 → T019 → T020.
  - T021 → T022 → T023.
  - T026 needs T003 and T012.
  - T027 needs T019 and T021–T024.
  - T031 needs all of US1.
- **US2 (T032–T039)**: needs US1's ABI (T026) and interop (T014). The rest of US1 is not needed.
- **US3 (T040–T050)**: needs Foundational and T021 (the precedence hook). It is independent of US2. T038 lights up once T042 lands.
- **Polish (T051–T061)**: per PR, after that story.

### Within Each User Story

- Tests are written first and fail before the implementation.
- Engine first, then ABI, then interop and persistence, then UI and MCP.

### Parallel Opportunities

- **Foundational**: T002, T004, T005, T006, T010, T011 and T013 touch different files.
- **US1**: T015, T016 and T017 (one test file, separate methods; parallel only if they are merged carefully, otherwise sequential). T028 and T030 run alongside the engine work (T018–T027) once T014 is done.
- **US3**: T040 and T041 alongside T042.
- **Polish**: T051, T052 and T053.

---

## Parallel Example: Foundational

```bash
Task: "Create MidiController.h (T002)"
Task: "Instrument::controller default (T004)"
Task: "RackInstrument forwards controller (T005)"
Task: "Plug-in host controller override (T010)"
Task: "CC1 → Mod Wheel in built-ins (T011)"
Task: "Managed MidiController + IAudioEngine (T013)"
```

## Parallel Example: User Story 1

```bash
Task: "Persistence v22 DTOs and save/load (T028)"
Task: "MCP get/set_clip_controller (T030)"
# while the engine capture/fold/playback chain T018 → T023 runs sequentially
```

---

## Implementation Strategy

### MVP First (User Story 1 = PR 1)

1. T001, then Foundational T002–T014.
2. US1, T015–T031.
3. **Stop and validate**: run quickstart PR 1 and `scripts/build.sh`. Then the closing tasks and PR 1.

### Incremental Delivery

1. PR 1 (US1): recording and playback; data saved; MCP access.
2. PR 2 (US2): piano-roll lanes.
3. PR 3 (US3): CC automation target, recording and precedence.

Each PR leaves `develop` releasable.

---

## Notes

- **Audio-thread rule** (AR-6): T018, T021–T023 and T044 must stay allocation-free and lock-free. Use fixed arrays, SPSC queues and snapshot reads only.
- **Commits**: the user makes their own commits. Leave changes for review and do not commit or push.
- **Re-verify line numbers**: the line numbers above are from `develop` @ d724fb5.

## Phase 7: Convergence

Assessed 2026-10-16 for **PR 1 scope only** (Setup, Foundational, US1; FR-001–FR-009,
FR-018–FR-022 as they apply to recording and playback). US2 and US3 are intentionally unbuilt
and not assessed.

- [X] T062 Flush the held-back controller tail when a take ends without `setRecording(false)`. In `Engine::finalizeSessionRecord` (`src/native/nota.engine/src/Engine_Session.cpp`), before `recordSessionScene_` is cleared, drain `recordedCtl_` and fold into the slot clip with every open range committed in full: set `ctlStopPending_` and call `foldRecordedCtl(slot.midi, L)` on a cloned track, then `republishWithTrackRaw`. In `Engine::setTrackArmed` (`src/native/nota.engine/src/Engine_Recording.cpp`), disarming the record track does the same into the arrangement take before `recordClipIndex_` is reset. Today the last segment of a moving curve (up to one poll's worth) is lost on both paths, per FR-002 / US1/AC7 (partial)
- [X] T063 Add smoke coverage for Session slot controller recording and playback in `tests/Nota.SmokeTest/MidiControllerTests.cs`: record the mod wheel and the pedal into a slot (`RecordSessionSlot`), stop, check `GetSessionController` holds them loop-local; then launch the slot and check `DeliveredController` follows the curve and the pedal value is chased at each loop start. Covers T062's flush, per FR-018 / FR-005 / US1/AC7 (partial)
- [X] T064 Add smoke coverage for routed ("MIDI In") controller capture and count-in controller pickups in `tests/Nota.SmokeTest/MidiControllerTests.cs`. (a) A source track with a clip carrying a CC1 curve routed into an armed destination: recording the destination prints the curve into its take. (b) With a 1-bar count-in, the pedal pressed during the count-in lands before the record start and the take's clip starts earlier to hold it. Per FR-002 / FR-018 (partial)
- [X] T065 Add the v21-project check from contracts/c-abi.md check 10 to `MidiControllerTests.Persistence`: write a minimal v21 `project.json` (no `Controllers` fields) to a temp bundle, `ProjectService.Load` + `Apply`, and assert it loads with no warnings and no controllers, per FR-007 (partial)
- [X] T066 Bring `research.md` up to date with what PR 1 settled: R5 tolerances are CC/pressure 0.008 and wheel 0.012 (not 0.004 / 0.002), the replace-range junction step (`kCtlJunction`), and the held-back last segment between polls (capped at 512 points). R9's `ControllerCurveDto` is parallel `Beats` / `Values` / `Curves?` arrays, rounded on save. "Before the first point no value" belongs in R4. Per plan: research R4/R5/R9 (contradicts)

## Phase 8: Convergence

Re-assessed 2026-10-16 for **PR 1 scope** (Setup, Foundational, US1). PR 1 itself has no
remaining gaps. One finding concerns the US3 tasks that PR 1's design changed underneath them.

- [ ] T067 [US3] Before implementing T043 / T044, re-point them and research R7 at the per-instrument delivered value PR 1 built. There is no `Track::lastCtl_`: the last controller value delivered to an instrument is `Instrument::deliveredCtl[kCtlCount]` (relaxed atomics, NaN = none), written by `deliverExpr` in `src/native/nota.engine/src/Engine_Render.cpp`. `applyAutomation` should compare and update that (store, then call `controller`), and `currentTargetValue` falls back to it, per plan: R7 / data-model Track (contradicts)
