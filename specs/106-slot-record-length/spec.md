# Feature Specification: Next-Take Length for an Empty Session Slot

**Feature Branch**: `feat/slot-record-length`

**Created**: 2026-11-18

**Status**: Draft

**Input**: User description: "GitHub issue #503 (https://github.com/nota-daw/nota/issues/503): Prepare the next recording length for a specific empty Session slot. During a live set, the user wants to prepare different take lengths for particular empty (or cleared) Session slots without changing the song tempo, resizing existing clips, or switching the global Fixed recording length before each take. A visible next-take length is attached to a chosen empty slot, with an explicit way to inherit the global Fixed setting instead. Accept a supported bars/beats value, show its limits and explain when it takes effect. Keep the pending recording instruction distinct from the length of a clip that already contains media. Example: at a stable tempo and meter, prepare slot A for three bars and slot B for eight; recording A starts on the declared quantized boundary and finishes after its prepared duration without changing B, existing clips or the project tempo. Canceling before the start must not resize earlier media. Define what happens to the prepared value after a kept, interrupted or retried take, and keep the pending-state policy consistent across Undo and save/reopen. First version uses direct slot selection; controller actions and color/group targeting are out of scope. Related: #315 (editing length of an existing MIDI clip) and #501 (scheduling a take from an Arrangement section) are separate."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Related work**: specs/078 (Discard and retry a take), specs/086 (several slots recording at
once), specs/105 / #481 (Follow Action when a recorded take lands — touches the same take
landing path), bug #678 (a retried take with a fixed record length ends at once — must be
fixed for the retry scenarios here to hold), #315 (length of an existing MIDI clip), #501
(takes scheduled from an Arrangement section).

## Today *(as found in the code, 2026-11-18)*

- The Session toolbar has one **Fixed** setting for the whole project: Off, 1, 2, 4 or 8 bars.
  It is saved with the project and can be set over MCP (in beats).
- A take recorded into an empty slot is as long as Fixed when Fixed is on; with Fixed off it
  runs until stopped and is cut to whole launch quanta.
- Fixed converts bars to beats as four beats per bar, whatever the project's time signature.
- An empty slot already carries one property of its own, its **Stop button**, shown in the
  empty-slot inspector and saved with the project.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Prepare a length for one empty slot and record it (Priority: P1)

During a live set the performer selects an empty slot, sets **Take length** in its inspector to
3 bars, and later records into it. The take starts on the usual launch boundary and stops by
itself after exactly 3 bars, then loops like any fixed-length take. The global Fixed setting,
the tempo, other slots and existing clips are untouched.

**Why this priority**: This is the whole request — one slot, one length, no global switching.

**Independent Test**: Set global Fixed to Off, prepare slot A for 3 bars, record into A with
launch quantization 1 bar at 120 BPM 4/4: the clip is 3 bars (12 beats) long, recording
stopped on its own, and global Fixed still reads Off.

**Acceptance Scenarios**:

1. **Given** an empty slot A prepared for 3 bars and global Fixed Off, **When** the user
   records into A, **Then** recording starts on the next launch boundary and the slot stops
   recording and starts looping after exactly 3 bars.
2. **Given** slot A prepared for 3 bars and slot B for 8 bars on another track, **When** the
   user records A, **Then** A's take is 3 bars, B still shows 8 bars, global Fixed and the
   project tempo are unchanged, and no other clip changes length.
3. **Given** slot A prepared for 3 bars and global Fixed set to 4 bars, **When** the user
   records into A, **Then** the take is 3 bars — the slot's own length wins over the global one.
4. **Given** a prepared empty slot, **When** the user looks at the grid, **Then** the slot
   shows its prepared length, distinct from the look of a slot holding a clip.
5. **Given** an empty slot prepared for 3 bars on an audio track, **When** the user records
   into it, **Then** the audio take is 3 bars long, the same as for an instrument track.

---

### User Story 2 - Inherit the global setting, or record open-ended (Priority: P1)

Every empty slot starts out as **Global** (it follows the toolbar's Fixed setting, exactly as
today). The user can also mark a slot **Open** — record until stopped — even while global
Fixed is on, and can return a slot to Global at any time.

**Why this priority**: The issue asks for an explicit way to inherit the global value; without
it the feature changes today's behaviour for existing projects.

**Independent Test**: In a project from before this feature, every empty slot reads Global and
records exactly as before; switch one slot to Open with global Fixed 2 bars and record: it runs
until stopped.

**Acceptance Scenarios**:

1. **Given** a project saved before this feature, **When** it is opened, **Then** every empty
   slot's take length is Global and recording behaves exactly as before.
2. **Given** a slot set to Global and the toolbar Fixed changed from 2 to 4 bars, **When** the
   user records into that slot, **Then** the take is 4 bars, and the slot's inspector shows
   "Global (4 bars)" while it waits.
3. **Given** global Fixed 2 bars and a slot set to Open, **When** the user records into it,
   **Then** the take runs until stopped and is cut to whole launch quanta, as with Fixed Off.
4. **Given** a slot prepared for 3 bars, **When** the user picks Global, **Then** the slot shows
   no prepared length of its own any more.

---

### User Story 3 - The prepared length across cancel, retry, keep, Undo and save (Priority: P2)

The prepared length is an instruction for the slot's *next* take, never the length of media.
Cancelling a pending take before it starts, discarding and retrying it, keeping it, undoing
it, and saving and reopening the project all treat the instruction the same predictable way.

**Why this priority**: The issue explicitly asks for these rules; they make the feature safe
to rely on during a set, but the core value (Story 1) works without them being exercised.

**Independent Test**: Prepare slot A for 3 bars, record, keep the take, Undo: A is empty again
and still prepared for 3 bars. Save, reopen: the prepared value is there.

**Acceptance Scenarios**:

1. **Given** slot A prepared for 3 bars with a take pending (waiting for its boundary or in
   count-in), **When** the user cancels before the take starts, **Then** A is empty, still
   prepared for 3 bars, and no other clip in the project changed length.
2. **Given** a take into slot A recording against its prepared 3 bars, **When** the user
   discards and retries it, **Then** the retried take records a full 3 bars from its own start.
3. **Given** a take into slot A that reached its prepared 3 bars and was kept, **When** the
   user looks at A, **Then** A shows the clip's own length (3 bars), not a pending
   instruction; the prepared value stays with the slot, hidden while the clip is there, and
   shows again (3 bars) if the clip is later deleted, moved away or the take undone.
4. **Given** a take into slot A prepared for 8 bars, **When** the user stops it after 5 bars,
   **Then** the take is kept like any take stopped early (cut to whole launch quanta) and the
   prepared value follows the same rule as for a take that ran its full length.
5. **Given** a kept take in slot A, **When** the user presses Undo, **Then** A is empty again
   with the prepared value it had right before the take started.
6. **Given** several slots with different prepared values, **When** the project is saved and
   reopened, **Then** each slot shows the same value it had (prepared length, Global or Open).
7. **Given** the user changes a slot's prepared length, **When** they press Undo,
   **Then** the slot returns to its previous setting — changing it is an Undo step of its
   own, like toggling the slot's Stop button, and Redo applies it again.

---

### User Story 4 - Set a slot's next-take length from an assistant (Priority: P3)

The MCP Session tools can read and set a slot's prepared take length (bars and beats, Global or
Open), the same way they already read and set the global record length.

**Why this priority**: Nota keeps its MCP surface in step with Session features, but the issue
is about the performer's hands-on workflow.

**Independent Test**: Over MCP set slot (track 2, scene 3) to 3 bars, read the Session snapshot
back: it reports 3 bars for that slot and the global value unchanged.

**Acceptance Scenarios**:

1. **Given** an empty slot, **When** an MCP client sets its take length to 3 bars, **Then** the
   slot's inspector shows 3 bars and recording into it produces a 3-bar take.
2. **Given** a slot that holds a clip, **When** an MCP client sets its take length, **Then** the
   call is refused with a message saying the slot is not empty.

---

### Edge Cases

- **Slot holds a clip**: the take-length control is not offered (the clip inspector shows clip
  length, which this feature does not change — see #315). A value prepared earlier is not
  shown while the slot holds a clip.
- **Overdub into a filled slot**: unaffected; overdubbing keeps the clip's own length.
- **Time signature other than 4/4**: a prepared value is counted in bars and beats of the
  meter in effect when the take starts (3 bars in 7/8 = 21 beats of that meter). If the meter
  changes between preparing and recording, the bar count is kept, not the beat count.
- **Tempo change between preparing and recording**: the take is still the prepared number of
  bars/beats; its duration in seconds follows the tempo, and the tempo is never changed by
  this feature.
- **A scene launch with a tempo or meter of its own**: the take is measured in the meter
  that is in effect when the take starts.
- **Several slots recording at once (specs/086, scene record)**: each slot stops after its own
  prepared length; slots set to Global use the global value; Open slots run until stopped.
- **Value below one launch quantum** (e.g. 1 beat with launch quantization 1 bar): accepted;
  the take starts on the launch boundary and lasts the prepared length.
- **Value out of range** (zero, negative, longer than the maximum): refused in the inspector
  with the limits shown; an MCP call gets an error naming the limits.
- **Scene inserted, deleted, moved or duplicated**: the prepared value travels with its slot's
  row as the slot's Stop button does; deleting the scene drops it; a new scene's slots are
  Global.
- **Clips copied, cut, pasted, moved or dragged in from the Arrangement**: the prepared value
  belongs to the slot (track × scene), not to the clip. A clip landing in a prepared slot hides
  the value; a clip moved or cut away leaves the source slot showing its own value. Empty slots
  cannot be copied today, so a value never travels by the clipboard.
- **Track deleted or slot emptied by deleting its clip**: deleting a track drops its values;
  deleting a clip leaves an empty slot that shows the value it was prepared with before.
- **Recording started from Nota Remote, a MIDI controller or the record button in the
  toolbar**: the slot's prepared value applies no matter what started the take; only
  *setting* the value from those is out of scope.
- **Arrangement recording**: unaffected.

## Clarifications

### Session 2026-11-18

- Q: After a kept take, is the prepared value used up or does it stay with the slot? → A: It
  stays with the slot, hidden while a clip is there, and shows again when the slot is empty.
- Q: Is changing a slot's prepared length an Undo step? → A: Yes, an Undo/Redo step of its own
  that marks the project modified, like the slot's Stop button.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every empty Session slot MUST carry a next-take length setting with three modes:
  **Global** (follow the toolbar's Fixed setting — the default), **Open** (record until
  stopped), or a **fixed length** in bars and beats.
- **FR-002**: The empty-slot inspector MUST show the setting and let the user change it: the
  three modes, quick picks for common bar counts (1, 2, 3, 4, 8, 16 bars), and entry of any
  bars-and-beats value within the limits.
- **FR-003**: The inspector MUST show the limits — from 1 beat to 64 bars — and refuse a value
  outside them, saying why.
- **FR-004**: The inspector MUST explain when the setting takes effect: it applies to the next
  take recorded into this slot; the take starts on the launch boundary and stops after the
  prepared length. While Global it MUST show the value it would currently resolve to
  (e.g. "Global (4 bars)" or "Global (Open)").
- **FR-005**: An empty slot with a fixed or Open setting MUST show it in the Session grid in a
  way that cannot be mistaken for a clip; a Global slot looks as it does today.
- **FR-006**: A take recorded into an empty slot MUST use the slot's setting when it is a fixed
  length or Open, and the global Fixed setting when it is Global; this holds for instrument
  and audio tracks and for any way the take was started.
- **FR-007**: A take with a fixed length MUST start on the same launch boundary a take does
  today and stop recording by itself once it has recorded the prepared length, then loop.
- **FR-008**: Preparing, recording and keeping a take MUST NOT change the project tempo, the
  time signature, the global Fixed setting, any other slot's setting, or the length of any
  existing clip.
- **FR-009**: Cancelling a take before it starts (while waiting for its boundary or during
  count-in) MUST leave the slot empty with its prepared setting unchanged and MUST NOT change
  any existing media.
- **FR-010**: Discarding and retrying a take (specs/078) MUST record a full prepared length
  measured from the retried take's own start, and keep the slot's setting unchanged.
- **FR-011**: After a take is kept — whether it ran its full length or was stopped early —
  the slot shows the clip, not the prepared setting; the setting MUST stay attached to the
  slot unchanged and show again once the slot is empty (clip deleted or moved away, take
  undone).
- **FR-012**: Undoing a kept take MUST restore the slot to empty with the setting it had
  right before the take started.
- **FR-013**: Changing a slot's setting MUST be an Undo/Redo step of its own and MUST mark
  the project modified.
- **FR-014**: Each slot's setting MUST be saved with the project and restored on reopen;
  projects saved before this feature MUST open with every slot set to Global.
- **FR-015**: The setting MUST belong to its slot (track × scene): scene moves, duplicates and
  deletes carry or drop it with the row; clip copy, paste, move and Arrangement-to-Session
  drops MUST neither carry it to another slot nor overwrite the destination slot's own value.
- **FR-016**: The MCP Session tools MUST let a client read and set a slot's setting (Global,
  Open, or a length) and MUST refuse to set it on a slot that holds a clip or with a value
  outside the limits.
- **FR-017**: The overdub length of a filled slot and the length of existing clips MUST be
  unaffected by this feature.

### Key Entities

- **Next-take length (per slot)**: the instruction attached to one Session slot (track ×
  scene) for its next fresh take. Mode: Global, Open or Fixed; for Fixed, a count of bars and
  beats in the meter in effect when the take starts. Exists only while the slot is empty as
  far as the user sees, but stays attached while the slot holds a clip; never describes media.
- **Global Fixed setting**: the existing project-wide Session record length (Off / bars) that
  Global slots resolve to.
- **Take**: a recording into one slot; its length is decided by the resolved next-take length
  when it starts.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With two slots prepared for 3 and 8 bars, a performer records both takes
  without touching the toolbar, and each clip comes out exactly its prepared length (to the
  beat), with tempo, meter, global Fixed and all other clips unchanged.
- **SC-002**: Preparing a slot's length takes at most two clicks for the quick picks (select
  slot, pick a value) and is visible on the grid without opening the inspector.
- **SC-003**: Every pre-feature project opens and records exactly as before (all slots Global).
- **SC-004**: Across cancel, retry, keep, Undo and save/reopen, the prepared value matches the
  documented rule in 100% of the acceptance scenarios above.
- **SC-005**: A cancelled pending take never changes the length of any existing clip.

## Assumptions

- The quick picks (1, 2, 3, 4, 8, 16 bars) and the limits (1 beat to 64 bars) are reasonable
  defaults for live looping; the plan may tune them.
- A "stopped early" take is the existing behaviour of stopping a take by hand: it is kept and
  cut to whole launch quanta; this feature does not change that.
- The global Fixed setting keeps its current choices and its four-beats-per-bar conversion;
  fixing that conversion is outside this issue.
- Setting the value from Nota Remote, MIDI controllers, colour or group targeting is out of
  scope; recording from them still honours the slot's value.
- Bug #678 (retry with a fixed length ends the take at once) is fixed before or together with
  this feature, since Story 3 scenario 2 depends on it.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
