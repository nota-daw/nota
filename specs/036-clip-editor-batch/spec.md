# Feature Specification: Clip editor batch — playhead, arrangement ruler, keyboard focus, warp tempo

**Feature Branch**: `feat/clip-editor-batch`

**Created**: 2026-10-22

**Status**: Draft (clarified 2026-10-22)

**Input**: User description: "Clip editor batch (one PR) covering GitHub issues #277, #278, #275, #114, #248, #140." In short: the audio clip editor shows the playhead while stopped (#277); both clip editors can move the playhead from the ruler and by dragging the playhead line, mapped to the arrangement, with session slots handled sensibly (#278); the clip editor ruler shows the clip's arrangement position and follows the transport's bars/time display (#275); grid shortcuts and Space keep working after a click in the lower clip editor while text entry still types (#114); the audio clip's warp tempo is a prominent, labelled, typeable/draggable value next to the Warp toggle, with a tooltip and ×2 / ÷2 (#248); dragging a warp marker redraws the waveform live and commits once on release (#140).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Everything lives in `src/managed/Nota.App`. What the code does today:

- **Audio clip editor** (`AudioClipEditorView.cs`): a props rail (Clip / Playback / Warp
  sections) and a waveform canvas — a 24 px ruler, a 22 px warp-marker strip, then the wave.
  - `OnPlayhead(beats, playing)` hides the arrangement-clip cursor unless `playing` (#277).
    For a Session slot it already follows the slot's own loop clock
    (`SessionSlotPosition`). The piano roll's `SetPlayhead` has no such gate.
  - The ruler has no pointer handling — the playhead can't be moved from this editor (#278).
  - A warped clip shows its **full warp** with Start/End trim brackets; the ruler counts warp
    beats from the warp's own start (bar 1), not the arrangement. An unwarped clip shows the
    whole source sample with a **seconds** ruler from 0 s (#275).
  - Each segment between two warp markers carries a BPM chip in the marker strip (drag to
    scrub, double-click to type). The Warp section has a Warp switch, a mode dropdown,
    "Detect tempo" / "To transients" buttons and a read-only "Detected … BPM" line. There
    is no single clip-wide tempo control next to Warp (#248).
  - Dragging a warp marker moves the marker in the strip, but the waveform is redrawn only
    after release, when `MarkersCommitted` reaches the engine and the stretched peaks are
    fetched again (#140). Warp edits already run the stretch in the background
    (`WarpEdit` + `WarpBuildRequested`).
- **MIDI clip editor** (`PianoRollView.cs`): `RulerLane` seeks on click and scrubs on drag,
  snapped (#50); `MainWindow.Detail.cs` maps `roll.Origin + local` to the arrangement. The
  ruler numbers bars from the clip's own start, except with several clips open
  (`RulerBarOffset`, `ClipSet.Origin`) (#275). The playhead line itself can't be grabbed.
- **Time display**: the transport's position read-out flips bars ↔ m:ss.mmm on click
  (`TransportViewModel.ShowTimeDisplay`). No ruler follows it today — the arrangement ruler
  is bars only.
- **Keyboard** (`MainWindow.Input.cs`): Space / Return run in a tunnel-phase handler so a
  focused control can't steal them; everything else goes through the bubble-phase
  `HandleKeyDown`. Both skip when the key's source is a `TextBox` / `NumericUpDown`.
  `ReleaseTextFocusOnOutsidePress` drops text focus on a press outside the field. The
  reporter of #114 saw grid shortcuts and Space die after a click in the lower clip editor
  with the ADSR shown; the cause is not confirmed.

No engine change is expected: seeking, warp markers, warp length and the stretch all have
C ABI entry points already.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - See and move the playhead from the clip editors (Priority: P1)

A producer is editing an audio clip and wants to hear it from the second hit. They see
where the song position is on the waveform, stopped or playing, click the clip editor's
ruler at that hit, and press Space — playback starts there. In the piano roll they grab
the playhead line itself and drag it back a beat.

**Why this priority**: Two of the six issues (#277 bug, #278) and the most frequent
interaction: every listen-and-edit cycle in the clip editor starts by placing the playhead.

**Independent Test**: Open an arrangement audio clip, stop the transport with the song
position inside the clip, confirm the playhead shows; click the ruler, confirm the
transport position and the arrangement playhead move to the matching place; repeat with a
drag on the playhead line in both editors.

**Acceptance Scenarios**:

1. **Given** an arrangement audio clip at 2.1–4.1 is open and the transport is stopped at
   3.1, **When** the editor is shown, **Then** the playhead is drawn at the clip's 3.1 point
   on the waveform; **When** the position moves outside the clip, **Then** the playhead is
   not drawn.
2. **Given** that clip, **When** the user clicks the clip editor's ruler, **Then** the
   transport moves to the arrangement position under the click (snapped when Snap is on,
   free with Alt held) and the arrangement playhead follows.
3. **Given** the user presses on the ruler and drags, **When** the pointer moves, **Then**
   the playhead scrubs continuously; release leaves it at the last position.
4. **Given** the playhead is visible in the MIDI or the audio clip editor, **When** the
   user presses on the playhead line and drags, **Then** it moves the transport the same
   way as a ruler drag, and the cursor over the line shows it can be dragged.
5. **Given** a reversed audio clip or a warped clip whose Start bracket is trimmed in,
   **When** the user clicks a point on the waveform's ruler, **Then** the transport lands on
   the arrangement moment at which that audio plays.
6. **Given** a clip in a Session slot is open, **When** the user clicks the ruler or drags
   the playhead, **Then** nothing seeks: the ruler and the playhead line show a "not
   available" cursor and a tooltip saying a Session slot plays on its own loop clock; the
   playhead keeps following the slot's position.

---

### User Story 2 - The clip editor ruler reads like the arrangement (Priority: P2)

A producer selects one clip placed at bar 9 and wants to line up what they see in the
editor with the arrangement. The clip editor's ruler reads 9, 9.2, 10 … — the same
numbers as the arrangement ruler — and when they flip the transport read-out to time, the
editor ruler reads in minutes and seconds.

**Why this priority**: #275. Removes a constant mental offset; also makes the seek of
Story 1 predictable ("click at 9.3" means 9.3).

**Independent Test**: Place a MIDI clip and an audio clip at 9.1; open each; read the
ruler labels; flip the transport position display to time and read them again.

**Acceptance Scenarios**:

1. **Given** a single MIDI clip at 9.1–11.1, **When** it opens in the editor, **Then** the
   ruler's first bar reads 9 and the last full bar 10 — the same as a multi-clip selection
   shows today.
2. **Given** a warped audio clip at 9.1 whose Start bracket sits at the warp's beat 0,
   **When** it opens, **Then** the ruler reads 9 at the clip start; with the Start bracket
   trimmed one bar in, the bracket reads 9 and the audio before it reads 8.
3. **Given** an unwarped audio clip at 9.1, **When** it opens, **Then** the ruler labels the
   arrangement moment each part of the sample plays at.
4. **Given** the transport read-out shows time, **When** either clip editor is open, **Then**
   its ruler labels read as time (m:ss, with finer units when zoomed in) at the clip's
   arrangement position; flipping the read-out back returns the labels to bars.
5. **Given** a session-slot clip, **When** it opens, **Then** the ruler numbers the clip
   from its own start, as today (a slot has no arrangement position).
6. **Given** the transport read-out is flipped to time, **When** the arrangement is shown,
   **Then** the arrangement ruler also labels in time, so it and the clip editor rulers
   always read in the same unit; flipping back returns both to bars.

---

### User Story 3 - Keyboard shortcuts keep working after clicking in the clip editor (Priority: P2)

A producer clicks the ADSR in the lower clip editor, then presses 1 / 2 (⌘1 / ⌘2) to change
the grid and Space to play. Both work, exactly as after a click in the arrangement. When
they click into a text field (a typed value, the BPM field, a clip name), typing goes into
the field until they press Enter / Esc or click elsewhere.

**Why this priority**: #114, a bug. Losing transport keys feels like the app has frozen.

**Independent Test**: For each region of the lower clip editor (props rail, waveform /
piano roll, ADSR, envelope, MIDI tools), click it and press the grid shortcuts and Space;
then click a text field there and type a space and digits.

**Acceptance Scenarios**:

1. **Given** the user has clicked anywhere in the lower clip editor that is not a text
   field, **When** they press Space, **Then** the transport plays / stops.
2. **Given** the same, **When** they press the grid shortcuts (⌘1–⌘5 / Ctrl+1–5),
   **Then** the grid changes as it does after a click in the arrangement.
3. **Given** a text field in the clip editor has focus, **When** the user types, **Then** the
   keys reach the field (space included) and no shortcut fires; **When** they press Enter /
   Esc or click outside, **Then** the field lets go and shortcuts work again.
4. **Given** a typed-value box (ADSR read-out, BPM field) was opened and dismissed, **When**
   the user presses Space, **Then** the transport responds — no invisible field keeps the
   focus.

---

### User Story 4 - Find and set the clip's warp tempo (Priority: P3)

A producer imports a loop whose tempo was detected as 87 BPM; it is really 174. Next to
the Warp switch they see "Tempo 87.00 BPM", press ×2, and the loop locks to the grid. Another time they type 120 into the field or drag it up and down.

**Why this priority**: #248. The control exists only as per-segment chips in the marker
strip, which users miss and then believe warping is broken.

**Independent Test**: Open a warped loop; read the tempo value next to Warp; type a value,
drag it, press ×2 and ÷2; check the clip's played length and the marker chips follow;
undo each.

**Acceptance Scenarios**:

1. **Given** a warped clip, **When** the audio clip editor is open, **Then** the Warp
   section shows a labelled tempo value (BPM) next to the Warp switch, with ×2 and ÷2
   buttons and a tooltip explaining that it is the tempo the audio was recorded at.
2. **Given** that value, **When** the user types a new tempo and presses Enter, or drags the
   value up / down, **Then** the clip is re-warped from that tempo and the segment chips and
   the clip's played length update; Esc cancels a typed edit.
3. **Given** the user presses ×2 or ÷2, **Then** the tempo doubles / halves in one step.
4. **Given** any of these edits, **When** the user undoes, **Then** one undo step restores
   the previous tempo (a drag is one step).
5. **Given** a clip with several warp markers at different tempos, **When** the editor shows
   it, **Then** the value shows the tempo of the whole played region and setting it scales
   every segment by the same factor, keeping their relative timing.
6. **Given** an unwarped clip, **When** the editor shows it, **Then** the value shows the
   clip's detected tempo (or "—" if none was found); typing a tempo, dragging it or pressing
   ×2 / ÷2 turns Warp on and warps the clip from that tempo, all in one undo step.

---

### User Story 5 - See the warp while dragging a marker (Priority: P3)

A producer drags a warp marker to line a snare up with beat 2. While they drag, the
waveform on both sides of the marker stretches and squeezes, so they can see the transient
land on the grid line before letting go. On release the clip is re-warped once and one
undo step is recorded.

**Why this priority**: #140. Precision placement without trial-and-error releases.

**Independent Test**: Drag a marker slowly between two grid lines; watch the waveform
follow; release; undo once and confirm the marker returns.

**Acceptance Scenarios**:

1. **Given** a warped clip with markers, **When** the user drags a marker, **Then** the
   waveform between the neighbouring markers redraws on every pointer move to show the
   new stretch, and the BPM chips of the two touched segments update live.
2. **Given** the drag, **When** the user releases, **Then** the engine receives the new
   markers once and one undo step is recorded; the waveform then shows the engine's
   rendered warp.
3. **Given** the drag, **When** the user presses Esc before release (or the pointer capture
   is lost), **Then** the marker and the waveform return to where they were and nothing is
   committed.
4. **Given** the drag, **Then** playback is unaffected until release (no audio glitches,
   no partial re-warp).

### Edge Cases

- Song position exactly on the clip's start or end: the playhead is drawn at the edge
  (inclusive), as the arrangement does.
- A looped arrangement clip longer than one cycle: the editor shows one cycle; the playhead
  is shown during every cycle at its position within the cycle, and a click seeks into the
  **first** cycle of the clip.
- A ruler click left of the clip's played region (warped clip, trimmed Start): the
  transport may land before the clip start — it moves to that arrangement position
  (clamped at 0).
- Seeking while recording: behaves as the arrangement ruler does during recording.
- Seeking while the transport is playing: playback continues from the new position.
- Clip moved in the arrangement while its editor is open: ruler labels and seek mapping
  follow the new position on the next refresh.
- Tempo changes in the song (if the project has a tempo map): the time labels and the
  seconds mapping use the same tempo the transport read-out uses.
- Warp tempo outside a sane range: typed / dragged values clamp to the same BPM range the
  segment chips accept; ×2 / ÷2 that would leave the range does nothing.
- Warp marker dragged onto or past a neighbour: clamped as today; the live preview never
  shows a negative-length segment.
- Very long clips (minutes) while dragging a marker: the live redraw stays smooth (see
  SC-004).

### Clarifications

#### Session 2026-10-22

- Q: Session-slot clips — seek within the slot or disable seeking? → A: Disable seeking,
  with a "not available" cursor and tooltip; the playhead still follows the slot.
- Q: Should the arrangement ruler follow the time display too? → A: Yes, both the
  arrangement ruler and the clip editor rulers switch together.
- Q: Unwarped clip's tempo value? → A: Show the detected tempo; an edit turns Warp on and
  warps from that tempo, one undo step.

## Requirements *(mandatory)*

### Functional Requirements

**Playhead (#277, #278)**

- **FR-001**: The audio clip editor MUST draw the playhead at the current song position
  whenever that position lies inside an arrangement clip's played region, whether the
  transport is playing or stopped.
- **FR-002**: A left-click on the audio clip editor's ruler MUST move the transport to the
  arrangement position under the pointer; a press-and-drag MUST scrub it continuously.
- **FR-003**: Ruler seeks MUST snap to the editor's grid when Snap is on and be free while
  Alt is held, in both clip editors.
- **FR-004**: In both clip editors the playhead line MUST be grabbable (a few pixels either
  side, in the ruler and over the content) and dragging it MUST seek like a ruler drag,
  with a resize/drag cursor on hover.
- **FR-005**: Grabbing the playhead line MUST take precedence over the content gesture
  under it only within the grab zone; outside it, note / marker / envelope editing works
  as today.
- **FR-006**: Editor positions MUST map to the arrangement taking the clip's start, the
  played region (trim / Start bracket), reversal and the clip loop into account.
- **FR-007**: For a clip in a Session slot, ruler clicks and playhead drags MUST NOT seek;
  the editor MUST show a "not available" cursor and a tooltip explaining why, and keep
  showing the slot's playhead.

**Ruler (#275)**

- **FR-008**: For a single arrangement MIDI clip and for an arrangement audio clip, the
  clip editor ruler MUST label positions with the arrangement position they play at.
- **FR-009**: When the transport's position read-out shows time, the clip editor rulers
  MUST label positions as time; when it shows bars, as bars. Flipping the read-out MUST
  update open editors at once.
- **FR-010**: Session-slot clips MUST keep clip-relative labels.
- **FR-011**: Snap and grid behaviour MUST NOT change because the labels changed — grid
  lines stay where they are today.
- **FR-012**: The arrangement ruler MUST also follow the transport's time display, so all
  rulers read in the same unit at any moment.

**Keyboard focus (#114)**

- **FR-013**: After a pointer press anywhere in the lower clip editor that is not a text
  field, Space, Return and the grid shortcuts MUST work as they do after a press in the
  arrangement.
- **FR-014**: While a text field has focus, keys MUST go to the field and no shortcut may
  fire, except the command-palette chord that already works everywhere.
- **FR-015**: Dismissing a typed-value box (Enter, Esc, click-away) MUST release keyboard
  focus so shortcuts work on the next key press.
- **FR-016**: The same MUST hold when the clip editor is in the floating detail window.

**Warp tempo (#248)**

- **FR-017**: The audio clip editor's Warp section MUST show a labelled tempo value (BPM)
  next to the Warp switch for warped clips, with ×2 and ÷2 buttons and a tooltip.
- **FR-018**: The tempo value MUST be editable by typing (Enter commits, Esc cancels,
  click-away commits) and by vertical drag, with fine drag on the usual modifier.
- **FR-019**: Setting the tempo MUST re-time the clip so that its played region plays at the
  project tempo as if recorded at the given tempo, scaling every warp segment by the same
  factor.
- **FR-020**: Each tempo edit (typed commit, whole drag, ×2, ÷2) MUST be one undo step.
- **FR-021**: For an unwarped clip the value MUST show the detected tempo (or "—"); an edit
  of it MUST turn Warp on and warp from the new tempo as one undo step.
- **FR-022**: The existing per-segment BPM chips and "Detect tempo" / "To transients" MUST
  keep working and stay consistent with the new value.

**Live warp drag (#140)**

- **FR-023**: While a warp marker is being dragged, the waveform MUST redraw on each pointer
  move showing the audio re-timed by the marker's new position, and the neighbouring BPM
  chips MUST show the live tempos.
- **FR-024**: The engine MUST receive the new markers once, on release; one undo step MUST
  be recorded per drag.
- **FR-025**: Esc during the drag, or a lost pointer capture, MUST restore the markers and
  waveform without committing.
- **FR-026**: The live preview MUST NOT touch audio playback.

**General**

- **FR-027**: All new controls, cursors and highlights MUST follow the Studio design
  language (DESIGN.md) in every colour theme.
- **FR-028**: New pointer gestures and any shortcut changes MUST be reflected in Preferences
  → Shortcuts.

### Key Entities

- **Clip editor ↔ arrangement mapping**: for an open clip, the relation between an editor
  position (piano-roll beat; warp beat or source time on the waveform) and the arrangement
  position it plays at. Derived from the clip's start, played region, reversal and loop.
- **Clip warp tempo**: the tempo the clip's played audio is treated as recorded at — the
  played region's source length versus its warp length. Edits scale all warp markers'
  beat positions.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In both clip editors, placing the playhead at a chosen hit takes one click
  (or one drag), with no trip to the arrangement — verified on MIDI, warped audio,
  unwarped audio and reversed audio clips.
- **SC-002**: A ruler click lands the transport within one snap step (Snap on) or within
  2 px of the click (Alt) of the clicked label, on all four clip kinds above.
- **SC-003**: For a clip placed at any bar N, the editor ruler's label at the clip start
  reads N (bars) or the arrangement time (time display), matching the transport read-out
  at that moment to the displayed precision.
- **SC-004**: While dragging a warp marker on a 5-minute clip, the waveform keeps up with the
  pointer without visible lag (at least the UI's normal redraw rate), and audio playback
  shows no dropouts.
- **SC-005**: After a click in any region of the lower clip editor that is not a text
  field, 10 of 10 tries of Space and of a grid shortcut take effect.
- **SC-006**: The warp tempo can be found and changed from the Warp section in under 10
  seconds by someone who has not seen it before; ×2 / ÷2 fixes a half/double-time
  detection in one click.
- **SC-007**: Every edit added by this batch is undone by exactly one undo step.

## Assumptions

- One PR covers all six issues; each user story can still be verified on its own.
- "Snap" in the clip editors is the shared clip-editor grid (the piano roll's), as it is
  for markers and trim brackets today; Alt for free placement matches the existing
  gestures there.
- The time display uses the same tempo source and format family as the transport
  read-out; the ruler picks m:ss / s / ms steps by zoom, like the existing seconds ruler.
- A multi-clip MIDI selection keeps its current ruler behaviour (already arrangement-based)
  and also gains the time display.
- The clip's warp tempo for a multi-segment clip is the average over the played region
  (source length ÷ warp length), as Ableton's Seg. BPM does for a single segment.
- The tempo value is shown in the Warp section of the props rail; showing it on the clip in
  the arrangement / session (an idea in #248) is out of scope.
- The live warp preview is a display-only approximation (the existing peaks re-timed
  segment by segment); the engine's real stretch replaces it on release.
- #276 (two-column inspector) is out of scope; the new tempo row must fit the current
  single-column rail.
- No C ABI or engine change is needed; if planning finds one, it goes through
  `nota_engine.h` and the smoke test is extended.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI only; the live warp preview never reaches the engine before release
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change planned
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
