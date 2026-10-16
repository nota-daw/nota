# Feature Specification: Live MIDI notes while recording

**Feature Branch**: `feat/live-midi-record-notes`

**Created**: 2026-10-16

**Status**: Draft

**Input**: User description: "MIDI recording: show notes in the clip while keys are held, not only on release (GitHub issue nota-daw/nota#56). Today a recorded MIDI note appears on the grid only after the key is released. Notes should appear the moment a key is pressed and grow to the right up to the playhead while held, as in most DAWs; on release the note gets its final length. Applies to arrangement recording and the clip editor/piano roll, to live keyboard input and routed MIDI ("MIDI In") capture, and to Session slot recording (loop wrap). A newly appearing note plays a short appear animation (fade/accent flash ~120–200 ms). Constraints: no audio-thread allocation or locking — the engine publishes a snapshot of held captured notes (pitch, start beat, velocity) via a new C ABI read; the UI must not raise idle repaint cost (redraw only while notes are held or animating, prefer redrawing only the take region, stay on the existing ~30 Hz tick unless animation looks choppy)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Today the engine keeps a held recorded note only in audio-thread capture state
(`pendingActive_` / `pendingStart_` / `pendingVel_` for live input, `recRoutedActive_` /
`recRoutedStart_` / `recRoutedVel_` for routed MIDI, in `Engine.h`) and hands it to the
message thread as a finished note on note-off (`Engine_Render.cpp`, `drainLiveMidi` and
`captureRoutedNotes`; `nota_engine_poll` materialises it into the clip). Audio recording
already exposes its growing take to the UI (`nota_engine_audio_record_*`); MIDI has no
equivalent. The UI side is the arrangement lanes (`ArrangementView.LaneControl.cs`), the
Session grid (`SessionView.Grid.cs`) and the piano roll (`PianoRollView.cs`). The
arrangement deliberately repaints only its overlay on the playhead tick, not the lane
surface (see #55) — this feature must not undo that.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-16

- Q: Which notes played before recording starts go into the take — only keys still held at the start, or also notes played and released during the count-in? → A: Keys still held at the start plus every note started during the count-in, even if released before the start (e.g. a pickup).
- Q: How is a note that starts before the clip's start stored and played? → A: The clip's left edge moves back to the earliest note's start; before 1.1 (the song start) the clip starts at 1.1 and the note's head is kept past the clip's left edge, to reappear and sound once the clip's left edge is extended (after moving the clip right, if it sits at 1.1).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See held notes grow in the arrangement (Priority: P1)

A player arms an instrument track, starts recording and plays a slow chord, holding it
for two bars. The moment the keys go down, three notes appear in the take at the
playhead's position and stretch to the right with the playhead for as long as the chord
is held. When the keys are released, the notes stop growing and keep exactly the length
they had — nothing jumps or reappears.

**Why this priority**: This is the issue itself. Without it the player records blind
during long notes and pads and cannot tell whether the keyboard is reaching the track.

**Independent Test**: Record onto a MIDI track in the arrangement, hold a key for several
beats and watch the take; release and compare the drawn note with the note stored in the
clip afterwards.

**Acceptance Scenarios**:

1. **Given** an armed instrument track and recording running, **When** the player presses a key, **Then** a note at that pitch appears in the take within one screen refresh of the playhead tick, starting at the beat where the key went down.
2. **Given** a key held while recording, **When** the playhead moves on, **Then** the drawn note's right edge follows the playhead.
3. **Given** a held note being drawn, **When** the key is released, **Then** the note stops at the release position and its drawn start, length and pitch equal the note saved in the clip.
4. **Given** several keys pressed and released at different times, **When** they overlap, **Then** each held note grows independently and each finishes at its own release.
5. **Given** a key held while recording, **When** recording is stopped (or transport stops) before the key is released, **Then** the in-progress note disappears or is finalised exactly as today's recording behaviour finalises it — the preview never leaves a note behind that the clip does not contain.
6. **Given** recording with a count-in starting at bar 5, **When** the player plays a pickup note on the count-in's last beat and releases it before bar 5, **Then** the note is recorded and shown on that beat before bar 5, the take's clip starts at that note, and playback sounds it.
7. **Given** recording with a count-in starting at 1.1, **When** the player presses a key one beat before 1.1 and holds it into the take, **Then** the clip starts at 1.1, the note sounds from 1.1, and its first beat is kept before the clip's left edge (it appears and sounds once the clip is moved right and its left edge extended).

---

### User Story 2 - Short appear animation (Priority: P2)

When a held note first shows up it fades in with a brief accent flash, so the player's eye
catches each new note even in a busy take.

**Why this priority**: Requested together with the feature and cheap on its own, but the
feature is fully usable without it.

**Independent Test**: Record a few staccato notes and a held note; each new note visibly
fades or flashes in once, over roughly 150 ms, then settles to the normal note colour.

**Acceptance Scenarios**:

1. **Given** recording is running, **When** a new note appears, **Then** it animates in over 120–200 ms and then draws like any other recorded note.
2. **Given** a note that is already shown, **When** it keeps growing or is released, **Then** it does not animate again.
3. **Given** very fast playing (many notes per second), **When** notes appear, **Then** each animates independently and the take stays readable — no lingering flashes.

---

### User Story 3 - Held notes in the piano roll while recording (Priority: P2)

A player has the recording take open in the clip editor (piano roll) and records; held
notes appear and grow on the piano-roll grid the same way they do in the arrangement.

**Why this priority**: The issue names both the arrangement and the clip editor; the
arrangement is where most recording happens, so the editor follows.

**Independent Test**: Open the clip being recorded into in the piano roll, record, hold a
key, and confirm the note grows on the grid and matches the saved note on release.

**Acceptance Scenarios**:

1. **Given** the piano roll shows the clip being recorded into, **When** a key is held, **Then** the note is drawn on the grid from its start to the playhead and grows with it.
2. **Given** the piano roll shows a different clip, **When** recording runs elsewhere, **Then** the piano roll draws no preview notes.

---

### User Story 4 - Routed MIDI and Session slots (Priority: P3)

A user records an instrument track fed from another track through "MIDI In", or records
into a Session slot; held notes preview the same way. In a Session slot, a note held across
the slot's loop end is drawn continuing from the loop start, the way it will be stored.

**Why this priority**: Same feature on less common record paths; without it the behaviour
would be inconsistent, but the main path already delivers the value.

**Independent Test**: (a) Route a MIDI source into an armed track, record, hold a note on
the source; (b) record into a Session slot and hold a note across the loop boundary.

**Acceptance Scenarios**:

1. **Given** an armed track fed by routed MIDI, **When** the source plays a held note, **Then** the note appears and grows in the take as in Story 1.
2. **Given** recording into a Session slot, **When** a key is held, **Then** the slot's take shows the growing note.
3. **Given** a note held across the Session slot's loop end, **When** the loop wraps, **Then** the preview matches how the note is stored on release (no note drawn outside the slot's length).

---

### Edge Cases

- The same key is pressed again before its previous press was recorded as released (re-trigger): the preview follows what the engine stores — one note per press, never a stuck preview note.
- More keys held than any reasonable limit (e.g. a sustain-pedal-free glissando over all 128 pitches): every held pitch previews; nothing is dropped silently.
- Recording starts while keys are already held: they are recorded and previewed from where they actually started (FR-012), not from the record start.
- A note played and released entirely within the count-in (a pickup): recorded and shown at its real position before the record start (FR-012).
- Notes played before pressing Record while the transport was already rolling and released before the start: not recorded (only keys still held at the start are captured).
- Playback loops in the arrangement while a key is held: the preview behaves like the stored note does in the same situation.
- Track-addressed notes (Nota Remote) for a different track than the take's: no preview, matching what is recorded.
- The take or its track is scrolled out of view: no drawing work for it; when scrolled back the held notes are right.
- Recording stopped by the punch/record length or count-in ending: no preview note outlives the recording.
- Idle (stopped, nothing held, no animation): the app repaints no more than it does today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: While recording MIDI, every key currently held that will be recorded into the take MUST be visible in the take, drawn from its start beat to the current playhead.
- **FR-002**: A held note MUST appear no later than the next UI playhead refresh after the key is pressed.
- **FR-003**: On release, the drawn note MUST end at the release position, and the stored note MUST replace the preview with no visible jump, gap or duplicate.
- **FR-004**: The preview MUST cover live input (computer keyboard, MIDI controllers, Nota Remote addressed to the take's track) and routed MIDI ("MIDI In") capture, in arrangement recording and Session slot recording.
- **FR-005**: In Session slot recording, a held note crossing the slot's loop end MUST be drawn the way it will be stored when released.
- **FR-006**: The preview MUST show in the arrangement take, in the Session slot being recorded, and in the piano roll when it shows the clip being recorded into.
- **FR-007**: The preview MUST show exactly the notes the engine will record — no preview for notes that will not be recorded, and no preview left after recording stops.
- **FR-008**: A newly appearing note MUST play a single appear animation (fade-in with a brief accent flash) lasting 120–200 ms; growth and release MUST NOT re-trigger it.
- **FR-009**: Reading the held notes MUST NOT allocate, lock or do I/O on the audio thread, and MUST NOT affect recorded timing.
- **FR-010**: The app MUST NOT repaint more while idle than it does today; extra repainting is allowed only while notes are held or an animation is running, and SHOULD be limited to the take's area rather than the whole track surface.
- **FR-011**: The engine MUST expose the held captured notes (pitch, start beat, velocity) through the public engine interface so any front-end can draw them, and this read MUST be covered by the engine smoke test.
- **FR-012**: Notes played before the record start MUST be recorded into the take and previewed at the position where they actually started sounding — never moved to the record start. This covers keys still held when recording starts (pressed during the count-in, or before Record was pressed while the transport was rolling) and every note started during the count-in, including ones released before the record start.
- **FR-013**: When an early note (FR-012) starts before the take's clip, the clip's left edge MUST move back to that note's start so the whole note is inside the clip and sounds on playback. The clip never starts before the song start (1.1); a note starting earlier keeps its head stored before the clip's left edge, the part inside the clip sounds from the clip start, and the head reappears and sounds once the clip's left edge is extended (after moving the clip right, if it sits at 1.1).

### Key Entities

- **Held recorded note**: a note whose key is down during recording and that will be written into the take on release — pitch, start beat (in the take's timeline; loop-relative for Session slots), velocity, and which take it belongs to. Exists only while held.
- **Recording take**: the clip (arrangement) or slot (Session) being recorded into; owns the held notes' position on screen. An arrangement take's start can move left during recording to include early notes (FR-013).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A pressed key shows as a note in the take within 50 ms of the press at the default refresh rate (plus audio buffer latency), in 100% of presses in a 2-minute test take.
- **SC-002**: After release, the drawn note and the stored note match in start, length and pitch to within one screen pixel at the current zoom, for every note in the test take.
- **SC-003**: Recording a 2-minute take with continuous playing causes no new audio dropouts compared with the same take recorded before the change.
- **SC-004**: With the transport stopped and nothing held, the app's repaint rate and CPU use are unchanged from before the change.
- **SC-005**: While holding notes during recording, UI CPU use stays within 10% of recording audio with its live waveform today.
- **SC-006**: Every new note visibly animates in once; no note keeps animating longer than 200 ms.

## Assumptions

- The animation is always on; no Preferences toggle is added. It respects the Studio design language: the accent (orange) flash fades to the track's normal note colour.
- Velocity is carried in the held-note data for parity with stored notes and possible velocity shading; the preview draws notes the same way the take draws stored notes.
- Recorded MPE expression curves are not drawn during the hold; they attach to the note on release as today.
- The existing ~30 Hz playhead tick drives the growth and animation; a faster rate is used only if the animation looks choppy in practice (decided in the plan).
- The audio take's live waveform path is the model for how the take region is redrawn during recording; the plan confirms whether the MIDI preview can share it.
- Apart from early notes (FR-012, FR-013), what gets recorded and when is unchanged; the rest of the feature is display only.
- Session slots have a fixed loop and no timeline before their start, so an early note recorded into a slot starts at the slot's start (as today); FR-013's clip extension applies to arrangement takes.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
