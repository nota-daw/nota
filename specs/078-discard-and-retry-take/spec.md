# Feature Specification: Discard and Retry a Take

**Feature Branch**: `feat/discard-and-retry-recording`

**Created**: 2026-11-04

**Status**: Draft

**Input**: User description: "Abort current recording and start over (issue #185): a dedicated, assignable "Undo & Restart" (discard take and retry) command. While recording, a single key press stops recording, discards the take(s) just recorded (no undo entry left behind, audio files removed), returns the playhead to the position recording started from (including preroll/count-in), and immediately starts recording again. Inspired by Logic's Cmd+Period "Discard Recording and Return to Last Play Position" and Studio One's "Undo & Restart". Consider the comment on the issue about Session-view slot recording."

Issue: [nota-daw/nota#185](https://github.com/nota-daw/nota/issues/185) (approved).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The take is decided on the engine's message thread (where a stopped take is materialised
into the arrangement and the undo history); the command, its shortcut and its feedback
live in the managed app (command catalog, transport view-model, MCP).

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Throw the take away and go again (Priority: P1)

A guitarist records a passage in the Arrangement, starting from stop with a one-bar
count-in. Halfway through they fluff a chord. They press one key: the take vanishes, the
playhead jumps back to where the take started, the count-in clicks again and recording is
already running when it ends. They play the passage again without touching the mouse.

**Why this priority**: This is the whole request — guitar tracking repeats the same
passage dozens of times, and today a retry is Stop → Undo → reposition → Record.

**Independent Test**: Arm an audio track, record from stop with count-in, trigger the
command mid-take; the arrangement holds no trace of the first take, the playhead is back at
the take start, the count-in plays and a new take is capturing. Stop: only the second take
is in the arrangement.

**Acceptance Scenarios**:

1. **Given** an armed audio track recording from stop with a count-in set, **When** the user
   triggers Discard and retry, **Then** the take in progress is dropped, the playhead
   returns to the beat where the take started, the count-in plays again and recording
   resumes when it ends.
2. **Given** the same, but with the count-in off, **When** the user triggers the command,
   **Then** recording restarts immediately from the take's start beat.
3. **Given** the retried take is stopped normally, **When** the user presses Undo once,
   **Then** the arrangement returns to its state before the *first* (discarded) take — the
   discarded take never appears in the undo or redo history.
4. **Given** an armed instrument track recording MIDI (alone or together with an audio
   track), **When** the user triggers the command, **Then** the notes and controller data
   of the take — and any clip the take created — are dropped too, and both tracks record
   again.
5. **Given** the record button is engaged and the user moved a control (writing
   automation) during the take, **When** the user triggers the command, **Then** the
   automation written during the take is dropped as well.

---

### User Story 2 - Retry a loop-recorded take (Priority: P2)

With the transport loop on, a player records several passes over a four-bar loop, decides
the whole attempt is bad and wants to start again from the first pass.

**Why this priority**: Loop recording is the other common way to track a part; the
command must not leave half of a multi-pass take behind.

**Independent Test**: Loop on, record three passes, trigger the command; no pass and no
take lane from this recording remains, earlier takes on the track are untouched, recording
restarts from where it started.

**Acceptance Scenarios**:

1. **Given** loop recording has completed two or more passes, **When** the user triggers
   the command, **Then** every pass of this recording is discarded (no new takes in the
   track's take lanes) and takes from earlier recordings stay as they were.
2. **Given** loop recording in Replace mode removed notes of earlier passes, **When** the
   user triggers the command, **Then** the clip is back to what it held before this
   recording began.

---

### User Story 3 - Reach it the way that fits the session (Priority: P2)

The user finds the command where other transport commands live: a default keyboard
shortcut, the Transport menu, and the command palette; an assistant driving Nota over MCP
can trigger it too.

**Why this priority**: The issue asks for a dedicated key command; a command nobody can
find is no command.

**Independent Test**: Trigger the command once from each entry point while recording; each
discards and restarts the take. The Shortcuts page in Preferences lists it.

**Acceptance Scenarios**:

1. **Given** recording is running, **When** the user presses the default shortcut
   (⌘. on macOS, Ctrl+. elsewhere), **Then** the take is discarded and restarted.
2. **Given** nothing is recording, **When** the user triggers the command, **Then**
   nothing in the project changes and the status line says there is no take to discard.
3. **Given** the command palette is open, **When** the user searches "retry", "discard" or
   "undo restart", **Then** the command is found.

---

### User Story 4 - Retry a take recorded into a Session slot (Priority: P3)

A cellist records a four-bar take into an empty Session slot while a drum clip on another
track keeps playing. They make a mistake and trigger the same command. Only that slot's
take is dropped; the drums and every other playing clip carry on; the slot shows it is
waiting and starts capturing again at the next launch-quantize boundary.

**Why this priority**: Raised in the issue's discussion; Session recording is a separate
path from the Arrangement take, and live looping is exactly where a retry must not stop the
music.

**Independent Test**: With a clip playing on one track, record into an empty slot on an
armed track, trigger the command mid-take; the other clip never stops, the slot returns to
pending and records again from the next quantize boundary; Stop leaves one playable clip
in the slot, and save/reopen keeps only that take.

**Acceptance Scenarios**:

1. **Given** a slot recording while other Session clips play, **When** the user triggers the
   command, **Then** the slot's in-progress take is discarded, the other clips keep playing
   uninterrupted, and the transport keeps running.
2. **Given** the same, **When** the retry is waiting for the next launch-quantize boundary,
   **Then** the slot is shown as pending (waiting to record), not as recording, until
   capture actually starts.
3. **Given** the slot held no clip before the take, **When** the retried take is stopped,
   **Then** the slot holds only the new take, it plays, and after save and reopen it is the
   only take stored for that slot.
4. **Given** launch quantize is off, **When** the user triggers the command, **Then** the
   slot records again immediately.
5. **Given** a fixed Session record length is set, **When** the retried take reaches that
   length, **Then** it ends and plays like any fixed-length take.

---

### Edge Cases

- **Take punched in while already playing** (recording engaged mid-playback, so no
  count-in applied): the retry restarts at the position playback was started from, so
  the player hears the run-up again; recording runs from there. The new take therefore
  begins earlier than the discarded one did.
- **Command during the count-in** (nothing captured yet): the count-in starts over from the
  beginning; nothing is discarded because nothing was recorded.
- **Command pressed repeatedly in quick succession**: each press discards whatever the
  current attempt captured and restarts; the project ends up exactly as before the first
  attempt plus nothing.
- **Recording could not start** (nothing armed, no audio input): the command behaves as on
  a stopped transport — nothing changes, the status line explains.
- **Loop turned on in the middle of a take**: the whole take is discarded, as for any other.
- **Armed track deleted or disarmed during the take**: the retry restarts recording on
  whatever is armed now; if nothing is, it stops and explains, as Record does.
- **Edits made by other means during the take** (an MCP client or the user editing another
  track while recording runs): [assumption] they are kept; only what the take itself wrote
  is dropped.
- **Unsaved-changes state**: a project that was clean before the discarded take is clean
  again after it (the discard alone does not mark the project modified).

## Clarifications

### Session 2026-11-04

- Q: Does the command cover Session slot recording too? → A: Yes — Arrangement and Session
  (User Story 4, FR-012–FR-014).
- Q: Where does a retry restart when the take was punched in during playback? → A: Where
  playback was started, so the run-up plays again (FR-005).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST offer a single command, "Discard and retry take", available
  while a take is recording or counting in.
- **FR-002**: The command MUST drop everything the take in progress recorded — the audio
  capture, every loop pass and the take lanes it would add, MIDI notes and controller and
  expression data, clips the take created, notes a Replace-mode pass removed, and
  automation written while the record button was engaged — leaving those tracks exactly as
  they were when the take started.
- **FR-003**: The discarded take MUST leave no undo or redo step: after the command, Undo
  behaves as if the discarded take had never been recorded.
- **FR-004**: The discarded take MUST never reach disk — no audio file is written for it,
  and saving afterwards stores nothing of it.
- **FR-005**: After discarding an Arrangement take, the playhead MUST return to the
  position the transport was started from for this attempt — the take's start beat when the
  take started the transport, the Play position when the take was punched in during
  playback — and recording MUST start again there on the same armed tracks without further
  input.
- **FR-006**: A take that started from stop MUST get the count-in again on retry when a
  count-in is set, and its capture MUST stay gated until the count-in ends, exactly as for
  a fresh Record from stop.
- **FR-007**: The retried take MUST behave as an ordinary take: stopping it materialises it
  as one undo step, it can be retried again, and it saves and reopens like any take.
- **FR-008**: The command MUST have a default shortcut (⌘. on macOS, Ctrl+. on Windows and
  Linux), an entry in the Transport menu and the command palette, and be listed on the
  Preferences → Shortcuts page.
- **FR-009**: The command MUST be callable through the MCP server as a tool that reports
  whether a take was discarded.
- **FR-010**: Triggered with no take recording, the command MUST change nothing in the
  project or transport and MUST show a short status message.
- **FR-011**: Edits to the project that the take did not make (another track edited during
  the take) MUST survive the discard.
- **FR-012**: While a Session slot is recording, the command MUST discard only that slot's
  in-progress take (the slot returns to what it held before), leave every other Session
  clip, the Arrangement and the transport running, and re-record into the same slot from
  the next launch-quantize boundary (immediately when quantize is off).
- **FR-013**: While a Session retry waits for its boundary, the slot MUST be shown as
  pending, not recording, until capture actually starts.
- **FR-014**: Nota records one take at a time — an Arrangement take or a Session slot take;
  the command MUST act on whichever is recording, by its rule (FR-005 or FR-012).

### Key Entities

- **Take attempt**: one press of Record (or one retry) up to Stop or the next retry: the
  tracks it records, the beat it started at, whether it started the transport and counted
  in, and the project state just before it began — what a discard returns to.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Retrying a take takes one key press instead of the current four steps (Stop,
  Undo, reposition, Record).
- **SC-002**: After any number of retries followed by Stop, the project contains exactly one
  new take, and one Undo returns it to the state before the first attempt — verified in an
  automated end-to-end test.
- **SC-003**: A take started from stop is retried from the same musical position —
  sample-aligned with the discarded one; a Session retry starts on the launch-quantize
  grid.
- **SC-006**: A Session retry never interrupts the other clips that are playing.
- **SC-004**: Saving after a discarded take writes no audio for it: the project folder
  holds the same audio files as if the discarded take had never been recorded.
- **SC-005**: No audible glitch or dropout is introduced by the retry beyond the transport
  restart itself.

## Assumptions

- Nota has no music-playing pre-roll; the issue's "preroll" is Nota's count-in (the
  clicks-only bars before a take started from stop).
- "Assignable" means a command in the catalog with a default shortcut, reachable from
  menu, palette and MCP. User-remappable shortcuts do not exist in Nota yet and are out of
  scope.
- The command acts only on the take in progress. Discarding a take after it was stopped is
  ordinary Undo and stays as it is.
- The default shortcut ⌘. / Ctrl+. is free in the command catalog today.
- Nota Remote (phone) and the Cardputer remote do not get a button in this change.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
