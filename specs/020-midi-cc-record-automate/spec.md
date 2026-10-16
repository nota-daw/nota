# Feature Specification: Record and automate MIDI CC

**Feature Branch**: `feat/midi-cc-record-automate`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: "GitHub issue nota-daw/nota#69 — Record and automate MIDI CC: sustain pedal, pitch wheel, mod wheel, any CC. Goal: record a piano part with the sustain pedal, or a synth line with mod wheel and pitch wheel moves, have them play back from the clip, and draw or edit them afterwards. Requested: record incoming CCs (CC64 sustain, CC1 mod wheel, pitch wheel, channel pressure, any CC) into the MIDI clip along with the notes; edit them in the piano roll — CC lanes under the notes (next to VEL / Bend / Pres / Slide), with a picker for the CC number, the sustain pedal drawn as on/off blocks; automate them — CC as an automation target on instrument tracks (arrangement lanes), so a plug-in or built-in instrument receives the CC from automation; playback sends the CC to the instrument (built-in instruments that understand CC64 / CC1, and hosted plug-ins). Today: live input already reaches instruments (wheel, channel pressure, poly aftertouch, CC64 to plug-ins and Nota Keys). Recording keeps only per-note bend, pressure and slide (MPE); the pitch wheel, channel pressure and sustain pedal aren't recorded, and other CCs are neither recorded nor automatable. Automation lanes target device and mixer parameters only."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [x] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

What exists today, and what this feature builds on:

- **Live controllers.** Instrument-wide controls already reach instruments live through
  `nota_engine_note_expression` / `nota_track_note_expression` with `pitch = -1`
  (`nota_engine.h`): dim 0 the wheel (normalised −1..+1), 1 channel pressure, 2 slide
  (CC74), 3 the sustain pedal (CC64, 0/1). Hosted plug-ins get the wheel, channel pressure,
  poly aftertouch and CC64; other CC numbers are not passed on. The MPE decoder lives in
  `MidiInput.h`; built-ins that read CC64 / the wheel are listed in `FEATURES.md` (Keys,
  Physical, Pentad's mod wheel, Volt's vibrato, …).
- **Recorded expression.** Per-note MPE is stored per note (`NotaNoteData.expr_id` →
  `nota_note_expr_*`) and drawn in the piano roll's VEL / BEND / PRES / SLIDE lane. There
  is no clip-level (instrument-wide) controller data yet; MIDI clips only carry velocity and
  volume envelopes (`nota_midi_clip_env_*`, kind 0/1).
- **Track automation.** Lanes target track volume (0), pan (1), a built-in device
  parameter (2) or a plug-in parameter (3, by stable param id) — no MIDI CC target.
  Automation record is driven by the transport Record button (M9-C).
- **MIDI Learn.** `nota_midi_poll_control_events` drains incoming CCs for MIDI Learn
  (`MidiLearnService.cs`); a learned CC moves a mapped parameter.
- **Recording.** Live and routed ("MIDI In") capture, count-in pickups, loop Overdub /
  Replace and held-note preview (specs 017, 018) define where recorded notes land; recorded
  controllers must follow the same rules.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-16

- Q: When a CC automation lane and clip controller data address the same controller at the same time, which does the instrument follow? → A: The track automation lane; the clip's data for that controller is ignored wherever the lane has points.
- Q: Is a CC bound by MIDI Learn also recorded into the clip and sent to the instrument? → A: Yes — it drives its mapped parameter and is also recorded and passed to the instrument.
- Q: Scope? → A: One spec for recording/playback, piano-roll lanes and CC automation, landed as three pull requests in priority order (Story 1, 2, 3).
- Q: In loop Overdub, what happens to a controller's earlier data when a later pass moves it? → A: The new pass replaces that controller's data over the range it touched (from its first move in the pass to the pass end); other controllers and untouched ranges stay.
- Q: Does a pass with only controller moves and no notes count as a played pass? → A: It replaces only the controllers it moved, over their touched range; notes stay (in Replace mode too).
- Q: Is the sustain pedal stored as on/off or with its 0–127 value? → A: Its full 0–127 value is stored; the lane draws on/off blocks by the ≥ 64 threshold and shows partial (half-pedal) values; plug-ins get the exact value, built-ins treat ≥ 64 as down.
- Q: Do arrangement clips / Session slots draw controller data? → A: No — piano roll only; arrangement clips and slots draw notes as today.
- Q: Is polyphonic aftertouch from a non-MPE keyboard recorded? → A: Yes, as each note's pressure (the same per-note expression MPE records), shown and edited in the PRES lane.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Record the sustain pedal, wheels and CCs with the notes (Priority: P1)

A pianist arms a track with Nota Keys (or a piano plug-in), records a passage using the
sustain pedal, and plays it back: the pedal holds the notes exactly as it did while
playing. A synth player records a line while riding the mod wheel and bending with the
pitch wheel; playback reproduces the vibrato and the bends. A user with a controller that
sends expression (CC11) or a breath controller (CC2) into a plug-in gets those moves back
too.

**Why this priority**: This is the core gap in the issue. Without recording, a performance
with the pedal or wheels cannot be captured at all; editing and automation build on the
recorded data.

**Independent Test**: Record a short phrase with the pedal pressed and released a few
times, the mod wheel moved and one pitch bend; play the clip back and compare by ear and by
the instrument's response with the live take (Keys' notes held/released at the same beats,
the plug-in's mod/bend indicators moving the same way).

**Acceptance Scenarios**:

1. **Given** an armed instrument track and recording running, **When** the player presses and releases the sustain pedal, **Then** the pedal presses and releases are stored in the take at the beats they happened and play back to the instrument at those beats.
2. **Given** recording, **When** the player moves the pitch wheel, mod wheel (CC1) or channel pressure, **Then** the moves are stored in the take and play back to the instrument with the same shape and timing.
3. **Given** recording into a track with a hosted plug-in, **When** the controller sends any other CC number, **Then** that CC is stored and plays back to the plug-in; while playing live, the plug-in receives it as well.
4. **Given** a recorded take with controller data, **When** the clip is moved, copied, split, looped or quantized, **Then** its controller data moves, copies, splits and loops with it (quantize moves notes only), and the project saves and reloads it.
5. **Given** playback starts in the middle of a clip, **When** a controller had already been moved earlier in the clip, **Then** the instrument first receives that controller's value at the start position (e.g. the pedal is already down, the mod wheel already up).
6. **Given** a clip whose sustain pedal is down at the clip's end (or playback stops), **When** the clip ends or the transport stops, **Then** the pedal is released and the wheel returns to centre — no note stays held.
7. **Given** loop recording in Overdub or Replace mode, a count-in pickup, or recording into a Session slot, **When** controllers are moved, **Then** they land in the take by the same rules as the notes (pickups are kept before the record start); in Overdub, and in a pass with no notes in either mode, a pass replaces each controller it moved over the range it touched, leaving other controllers and the notes alone (FR-021).
8. **Given** an ordinary (non-MPE) keyboard that sends polyphonic aftertouch, **When** the player records, **Then** each note's aftertouch is stored as that note's pressure and plays back the same way.

---

### User Story 2 - See and edit controller lanes in the piano roll (Priority: P2)

After recording, the user opens the clip in the piano roll and picks the sustain pedal in
the lane under the notes: the pedal shows as on/off blocks, and they drag a block's edge to
release the pedal earlier before a chord change. They switch the lane to the mod wheel and
redraw a smoother rise; they pick "CC 11" from the lane's picker and draw an expression
swell into an empty clip.

**Why this priority**: Recording is useful on its own; editing turns a good take into a
finished one and lets users draw controllers without a hardware controller.

**Independent Test**: In a clip with and without recorded controllers, switch the lane to
Sustain, Pitch wheel, Mod wheel, Channel pressure and an arbitrary CC; draw, move and delete
values; undo each step; play back and confirm the instrument follows the edited data.

**Acceptance Scenarios**:

1. **Given** the piano roll's lane picker, **When** the user opens it, **Then** it offers, next to VEL / BEND / PRES / SLIDE, Sustain (CC64), Pitch wheel, Mod wheel (CC1), Channel pressure and any CC number 0–127, and the controllers that already hold data in this clip are marked and listed first.
2. **Given** the Sustain lane, **When** it is shown, **Then** the pedal is drawn as on/off blocks (down where the value is ≥ 64), with partial (half-pedal) values visible; the user can draw a block, drag either edge, or delete it.
3. **Given** a continuous lane (pitch wheel, mod wheel, channel pressure, any other CC), **When** the user drags across the lane, **Then** values are drawn along the drag (⌥ for a straight line, like the MPE lanes), points can be moved and added/deleted with a double-click, and right-click offers clearing the lane.
4. **Given** any controller edit, **When** it is made, **Then** it is one undo step and plays back straight away.
5. **Given** the pitch wheel lane, **When** it is shown, **Then** its scale shows centre and the wheel's full up/down travel, and drawn values snap back to exact centre near the middle.

---

### User Story 3 - Automate a CC on an instrument track (Priority: P3)

A user wants a slow filter sweep on a plug-in that only exposes it through CC74 (or wants
the mod wheel up for the whole chorus regardless of clip). They open the automation target
picker on the instrument track, choose MIDI CC → "CC 74", and draw the lane in the
arrangement; the instrument receives the CC as the song plays, across clips.

**Why this priority**: Automation is a separate path from clips and is requested last in
the issue; it reuses the track automation lanes users already know.

**Independent Test**: On an instrument track, add a CC automation lane (pitch wheel,
sustain, CC1, CC74), draw it, play the song and confirm the instrument receives the values;
record it by moving the controller with transport Record engaged.

**Acceptance Scenarios**:

1. **Given** an instrument track's automation target picker, **When** the user opens it, **Then** it offers MIDI controllers — Pitch wheel, Channel pressure, Sustain (CC64), Mod wheel (CC1) and any CC 0–127 — alongside device and mixer parameters.
2. **Given** a CC automation lane, **When** the song plays, **Then** the instrument (built-in or plug-in) receives the lane's values as that controller.
3. **Given** a CC automation lane and the transport Record button engaged, **When** the user moves that controller on the hardware, **Then** the lane is written the same way other automation is recorded.
4. **Given** a track with both a CC automation lane and clip data for the same controller, **When** they overlap in time, **Then** the instrument follows the track automation lane; the clip's data for that controller is ignored wherever the lane has points.

---

### Edge Cases

- A CC that is bound by MIDI Learn to a parameter: it still moves its mapped parameter (and writes that parameter's automation with Record engaged) and is also recorded into the clip and passed to the instrument (FR-020).
- MPE controller: per-note bend / pressure / slide on member channels stay per-note (unchanged); only the master channel's wheel, pressure and CCs become clip controller data.
- Controllers moved while nothing is recording or armed: no data is stored; live behaviour is unchanged.
- A dense controller stream (a fast wheel wiggle, a breath controller sending hundreds of messages per second): recorded faithfully enough to sound the same, without bloating the project; repeated identical values are not stored.
- Two overlapping clips on the same track, or a clip shorter than its pedal press: playback releases the pedal at the clip's end so no note is stuck.
- A clip moved to a track with a different instrument: controller data travels with the clip; an instrument that ignores a controller simply ignores it.
- Built-in instruments that do not understand a given CC (e.g. CC11 on Nota Synth): the data is kept and drawn, the instrument ignores it; no error.
- An Instrument Rack: controller data reaches every chain, like live input today.
- Drum Rack: controller data reaches every pad's instrument.
- A Session slot loops: controller state at the loop end is reset/chased at the loop start the same way it is at a clip start.
- Old projects open unchanged; a project saved with controller data and opened in an older Nota plays without it (the data is kept in the file).

## Requirements *(mandatory)*

### Functional Requirements

**Recording and playback (Story 1)**

- **FR-001**: While recording MIDI onto a track, the system MUST store in the take the sustain pedal (CC64), pitch wheel, channel pressure, mod wheel (CC1) and every other CC number 0–127 received from the input that feeds the track, at the beats they were received.
- **FR-002**: Recorded controllers MUST follow the same capture rules as notes: live and routed ("MIDI In") input, count-in pickups, loop Overdub / Replace, Session slot recording, and track-addressed input (Nota Remote, gamepad) where those send controllers.
- **FR-003**: Playback of a clip MUST send its controller data to the track's instrument — built-in instruments (each acting on the controllers it understands) and hosted plug-ins (all controllers) — at the stored beats, including inside Instrument and Drum Racks.
- **FR-004**: Live input MUST pass every CC number (not only CC64) to hosted plug-ins, so what is recorded matches what the player heard.
- **FR-005**: When playback starts or jumps inside a clip, or a looped clip/slot wraps, each controller with data in the clip MUST first be set to its value at that position (chasing).
- **FR-006**: When a clip ends, playback stops, or the playhead leaves the clip, the sustain pedal MUST be released and the pitch wheel returned to centre if the clip had moved them; no note may stay held because of recorded controller data.
- **FR-007**: Controller data MUST travel with the clip on move, copy, duplicate, split, trim, loop and cross-track moves, and MUST be saved with and restored from the project. Quantize MUST NOT move controller data.
- **FR-008**: Per-note MPE expression on member channels MUST stay per-note as today; clip controller data comes from the master channel / non-MPE input only. Polyphonic aftertouch from a non-MPE input MUST be recorded as each note's pressure, the same per-note expression MPE records.
- **FR-009**: Recording MUST store controller data compactly: unchanged repeated values are not stored, and a continuous move keeps its audible shape.

**Editing (Story 2)**

- **FR-010**: The piano roll's lane under the notes MUST offer Sustain, Pitch wheel, Mod wheel, Channel pressure and any CC number 0–127 next to VEL / BEND / PRES / SLIDE; controllers holding data in the clip MUST be marked and sorted first.
- **FR-011**: The Sustain lane (and any CC the user switches to on/off display) MUST draw the controller as on/off blocks (on at ≥ 64) that can be drawn, resized by either edge and deleted; stored values keep their full 0–127 range and partial values are shown.
- **FR-012**: Continuous controller lanes MUST support drawing along a drag, straight lines with ⌥, moving points, double-click to add/delete, and right-click clear, consistent with the MPE lanes.
- **FR-013**: Every controller edit MUST be one undo step and MUST be heard on the next playback pass without re-opening the clip.

**Automation (Story 3)**

- **FR-014**: Track automation on instrument tracks MUST offer MIDI controllers as targets — Pitch wheel, Channel pressure, Sustain, Mod wheel and any CC 0–127 — with the same lane drawing, selection, paste and undo as existing automation targets.
- **FR-015**: During playback a CC automation lane MUST send its values to the track's instrument as that controller.
- **FR-016**: With transport Record engaged, moving a hardware controller that has a CC automation lane on the armed/selected track MUST record into that lane like other automation recording.
- **FR-017**: When a CC automation lane and clip controller data address the same controller at the same time, the instrument MUST follow the automation lane; the clip's data for that controller is ignored wherever the lane has points.

**Engine interface**

- **FR-018**: Clip controller data and CC automation targets MUST be readable and writable through the public engine interface, so the app, MCP tools and other front-ends can use them; recording, playback, chasing and the end-of-clip release MUST be covered by the engine smoke test.
- **FR-019**: Recording and playback of controllers MUST NOT allocate, lock or do I/O on the audio thread.
- **FR-020**: A CC bound by MIDI Learn MUST keep driving its mapped parameter as today and MUST also be recorded into the take and passed to the instrument like any other CC.
- **FR-021**: In loop Overdub, and in any pass that contains no notes, a pass MUST replace each controller it moved over the range it touched (from its first move in the pass to the pass end); controllers it did not move, untouched ranges, and the take's notes MUST stay.
- **FR-022**: Controller data is shown only in the piano roll; arrangement clips and Session slots draw notes as today.

### Key Entities

- **Clip controller data**: per MIDI clip, a set of controller curves, each identified by its controller (Sustain, Pitch wheel, Channel pressure, CC n). A curve is a list of points (clip-local beat, value); the sustain pedal and other switch-like controllers hold on/off steps. Travels with the clip.
- **CC automation lane**: a track automation lane whose target is a MIDI controller instead of a device or mixer parameter; values in the controller's own range.
- **Controller identity**: Pitch wheel (centre-relative, full up/down travel), Channel pressure (0..1), CC 0–127 (0..127 always stored in full; switch controllers such as CC64 are drawn as on/off at ≥ 64).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A recorded take with sustain pedal, mod wheel and pitch wheel plays back so that every pedal press/release and wheel move lands within one audio buffer of where it was played, for 100% of the events in a 2-minute test take.
- **SC-002**: Starting playback at 10 random positions inside a clip with recorded pedal and mod wheel, the instrument's pedal and wheel state match the clip's state at that position every time.
- **SC-003**: After any clip end, stop or jump, no note stays sounding because of a recorded pedal (0 stuck notes in a 50-stop test).
- **SC-004**: A user can switch the piano-roll lane to the sustain pedal and shorten one pedal block in under 10 seconds, without reading documentation.
- **SC-005**: Recording 2 minutes of continuous wheel movement adds no new audio dropouts and grows the saved project by no more than 100 KB.
- **SC-006**: A CC automation lane drawn on a plug-in's instrument track moves the plug-in's mapped control audibly and visibly, matching the drawn curve within one audio buffer.

## Assumptions

- All controllers are recorded regardless of channel on a non-MPE input (channel is not kept); a single instrument per track receives them on one channel, as live input does today.
- Built-in instruments are not taught new controllers by this feature; each keeps reacting to what it understands today (pedal, wheel, pressure, CC74). Plug-ins receive everything.
- The pitch wheel's range is the instrument's own setting; the clip stores the wheel position, not semitones.
- Existing MIDI Learn bindings, MIDI FX that output CCs (`nota_midi_effect_set_cc_*`) and gamepad mapping keep working unchanged.
- MCP tools for reading/writing controller data and CC automation follow the existing MCP patterns; the exact tool set is decided in the plan.
- The three stories land as separate pull requests (Story 1, then 2, then 3) to keep each change small, per the constitution.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
