# Feature Specification: Follow Action When a Recorded Take Lands

**Feature Branch**: `feat/take-landed-follow`

**Created**: 2026-11-23

**Status**: Draft

**Input**: GitHub issue nota-daw/nota#481 (approved): "Trigger a Session Follow Action when its
recorded take is ready to play. […] an optional Follow Action for one identified Session recording
slot, triggered once when that take has successfully finalized and become playable. A Stop request,
the end of capture, and a committed playable clip are distinct moments; playable here does not
necessarily mean durably saved to disk. Schedule the destination at a chosen musical boundary. Do
not run it for a stopped count-in, a discarded take or a failed finalization. Preserve the
configured rule after reopening without replaying an action that already fired for a past take.
[…] arm an empty bass slot and keep a four-bar recording. At the next allowed bar after that clip
becomes playable, queue the Chorus scene exactly once. If the count-in is stopped instead, no
Chorus launch is queued. A later successful take can trigger its own action once."

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

**Related work**: specs/103 (per-clip Follow bypass), specs/106 (per-slot next-take length — the
same "property of an empty slot" pattern and the same take-landing path), specs/078 (discard and
retry a take, #185), specs/086 (several slots recording at once), specs/109 (Session performance
recorded into the Arrangement), #480 (a waiting scene launch can be cancelled or replaced), #103
(command bindings) and #335 (controller workflow) — the last two are out of scope here.

## Today *(as found in the code, 2026-11-23)*

- A Session **clip** carries Follow actions A/B (None, Stop, Again, Previous, Next, First, Last,
  Any, Other, Jump), chances, a Jump target and a follow time. They fire on the audio thread once
  the clip has played its follow time, exactly at that moment (not quantized), and only act on
  slots of the clip's own track. A global **Follow** switch turns them all off; a clip can be
  bypassed on its own (specs/103).
- A **scene** can follow on to the next scene after a number of beats.
- An **empty slot** carries two properties of its own today: its Stop button and its next-take
  length (Global / Open / Fixed, specs/106). It has no Follow settings: nothing in the UI or MCP
  sets Follow on an empty slot.
- Recording into an empty slot:
  - may wait for the launch boundary, and from stop may play a count-in; stopping during the
    count-in or before the boundary cancels the take and nothing is kept;
  - an **audio** take has no clip until capture ends; the clip is then built in memory (its
    sample file is written only when the project is saved) and launched on the next launch
    boundary;
  - a **MIDI** take gets an empty clip at record start that plays and loops while recording;
    when capture ends the take is kept (an open take is cut to whole launch quanta);
  - a fixed-length take ends by itself; an open take ends on a Stop request; a global stop ends
    every take without relaunching;
  - a take can fail to land (input could not start, nothing was captured, a retried take stopped
    before its boundary), and a landed take can be discarded or retried (specs/078).
- Nothing today reacts to the moment a take lands.

## Definitions

- **Stop request** — the user (or MCP, a controller, Nota Remote) asks a recording to stop.
- **End of capture** — input stops being written into the take.
- **Take lands** — the take has been kept as a clip in its slot that can be played: audio is in
  memory and the clip has its final length, or the MIDI take has been cut to its final length.
  Saving the project to disk is not part of landing.
- **Landing rule** — the optional Follow action attached to a slot that runs when a take recorded
  into that slot lands.

## Clarifications

### Session 2026-11-23

- Q: Which destinations can the landing rule launch? → A: Scene launches only — Launch next
  scene or Launch scene X; track-only Follow actions and Stop Session are out of scope (FR-001).
- Q: Does the rule fire when a take lands because the whole Session or transport was stopped? →
  A: No — a global stop never fires the rule (FR-005).
- Q: Does the rule obey the global Follow switch? → A: Yes — while it is off the rule does not
  fire and the inspector says so (FR-002, FR-005).
- Q: Does a MIDI take with no notes count as landed? → A: No — it does not fire the rule (FR-005).
- Q: What happens to a rule whose target scene is deleted? → A: It turns Off and the inspector
  says why; moving or renaming the target scene keeps the rule pointing at it (FR-012).
  *Amended at plan time:* the inspector simply reads Off afterwards — the rule keeps no history
  (that is what keeps it from ever replaying), so there is nothing to say "why" from.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Launch a scene once a kept take is ready (Priority: P1)

A performer arms an empty bass slot and attaches a landing rule: "When the take lands, launch
scene Chorus on the next bar". They record four bars and keep them. On the first bar boundary
after the bass clip is ready to play, the Chorus scene launches — exactly once.

**Why this priority**: This is the whole request.

**Independent Test**: At 120 BPM 4/4, set an empty slot's landing rule to launch scene "Chorus"
at the next 1 bar, set the slot's take length to 4 bars, record: the scene launches on the first
bar boundary after the take lands, and only one scene launch happens.

**Acceptance Scenarios**:

1. **Given** an empty slot with the rule "launch Chorus, next 1 bar" and a 4-bar take length,
   **When** the take records its 4 bars and lands, **Then** the Chorus scene launch is queued for
   the first 1-bar boundary at or after the landing moment, and it launches there exactly once.
2. **Given** the same slot with an open take length, **When** the user stops the recording,
   **Then** nothing is queued at the Stop request; the launch is queued only once the take has
   landed (cut to whole launch quanta), on the first boundary after landing.
3. **Given** an audio track and a MIDI track each with a slot carrying a rule, **When** each take
   lands, **Then** each rule queues its launch the same way.
4. **Given** a rule of "launch next scene", **When** the take lands, **Then** the scene below the
   slot's own scene launches; from the last scene it wraps to the first, as scene follow does.
5. **Given** a rule that fired, **When** the scene launches, **Then** it behaves as any other
   scene launch (each track's slot in that scene starts, empty slots stop their track unless
   their Stop button is removed, scene tempo/meter apply on the boundary).

---

### User Story 2 - Nothing fires for a take that did not land (Priority: P1)

If the take does not become a playable clip — count-in stopped, take cancelled before its
boundary, input could not start, nothing was captured, the take discarded — the rule does not run.

**Why this priority**: The issue's core safety argument: a timer could advance the song after a
failed take; this must not.

**Independent Test**: Start recording from stop with count-in on, stop during the count-in: no
scene launch is queued and the transport stays stopped.

**Acceptance Scenarios**:

1. **Given** a slot with a rule and count-in on, **When** the user stops during the count-in,
   **Then** no launch is queued.
2. **Given** a slot with a rule waiting for its launch boundary, **When** the user stops before
   the take starts, **Then** no launch is queued.
3. **Given** a slot with a rule, **When** the take fails to land (input could not start, nothing
   captured, a retry stopped before its boundary), **Then** no launch is queued.
4. **Given** a take discarded or retried while it records (specs/078), **When** that attempt
   ends, **Then** nothing is queued for it — only a take that lands fires.
5. **Given** a take that landed and queued its launch, **When** the user undoes the take (or deletes
   its clip) before the launch boundary, **Then** the queued launch is cancelled.
6. **Given** a take that landed and whose scene already launched, **When** the user undoes the
   take, **Then** the scene launch is not reversed and nothing fires again.

---

### User Story 3 - Each successful take fires once; nothing replays (Priority: P2)

The rule stays on the slot. A later take that lands (a retry, or a new take after the clip was
removed) fires its own action once. Undo, Redo, save and reopen never fire it.

**Why this priority**: Required by the issue so the rule can be relied on across a set and across
sessions, but Story 1 delivers value on its own.

**Independent Test**: Record a take that fires the rule, retry it: the retried take fires once
more when it lands. Save, reopen: the rule is still shown and nothing launches on open.

**Acceptance Scenarios**:

1. **Given** a take that landed and fired, **When** the user retries it and the retry lands,
   **Then** the rule fires once for the retried take.
2. **Given** a take that landed and fired, **When** the user presses Undo and then Redo, **Then**
   nothing fires.
3. **Given** a project with rules on several slots, some of which already fired, **When** it is
   saved and reopened, **Then** every rule is shown as configured and nothing is launched or
   queued on open.
4. **Given** a slot holding a landed clip, **When** the user overdubs into it, **Then** the rule
   does not fire (an overdub does not land a new take).
5. **Given** a slot whose clip is deleted, **When** the user records a new take there and it
   lands, **Then** the rule fires once.

---

### User Story 4 - Configure and see the rule (Priority: P2)

In the empty-slot inspector, next to the Stop button and Take length, the user sets **When the
take lands**: Off (default), Launch next scene, or Launch scene … (a chosen scene), and **On**:
the boundary (Global launch quantization, or a value from the launch quantization list). The grid
shows that an empty slot carries a rule.

**Why this priority**: Needed to use the feature; the value is in Stories 1–3.

**Independent Test**: Select an empty slot, set the rule to "Launch scene Chorus, 1 bar": the
slot shows a landing mark in the grid; set it back to Off: the mark is gone.

**Acceptance Scenarios**:

1. **Given** an empty slot, **When** the user sets a rule, **Then** the inspector shows it and
   explains when it fires ("once, when a take recorded here is ready to play, on the next …").
2. **Given** a slot with a rule, **When** the user looks at the grid, **Then** the slot shows a
   mark that cannot be mistaken for a clip or for a prepared take length.
3. **Given** the global Follow switch is off, **When** the user looks at the rule, **Then** the
   inspector says it is off globally, and a landing take does not fire it.
4. **Given** the user changes a rule, **When** they press Undo, **Then** the previous rule returns;
   the change is an Undo/Redo step of its own and marks the project modified.

---

### User Story 5 - Set the rule from an assistant (Priority: P3)

The MCP Session tools read and set a slot's landing rule, the same way they set a slot's Stop
button and take length.

**Why this priority**: Nota keeps MCP in step with Session features; the request is hands-on.

**Independent Test**: Over MCP set slot (track 2, scene 1) to "launch scene 3, 1 bar", read the
Session snapshot: it reports the rule.

**Acceptance Scenarios**:

1. **Given** an empty slot, **When** an MCP client sets its landing rule, **Then** the inspector
   shows it and a take landing there fires it.
2. **Given** a target scene that does not exist or an unknown boundary, **When** an MCP client sets
   the rule, **Then** the call is refused with a message naming the problem.

---

### Edge Cases

- **Take stopped by a global stop** (transport or Session stop): the takes land without being
  relaunched; the rule does **not** fire, because the user asked the music to stop.
- **Several slots recording at once** (specs/086): each slot's rule fires for its own take when
  that take lands. If two rules queue launches for the same boundary, the later request replaces
  the earlier one, as any later scene launch does (#480).
- **A scene launch already waiting** when the take lands (launched by hand or by scene follow):
  the rule's launch replaces it, like any later launch request; a later manual launch replaces
  the rule's launch the same way.
- **Clip Follow actions and scene follow** keep working as today; the landed clip's own Follow
  actions (if any) are unrelated to the slot's landing rule.
- **Fixed-length take ending exactly on a boundary**: the take lands a moment after capture ends,
  so the launch goes on the first boundary at or after the landing moment — typically the next one.
- **Take into a slot that already holds a clip**: only a take into an empty slot (a fresh take)
  fires the rule; an overdub, or an audio take recorded over a clip, does not.
- **Empty take**: an audio take with nothing captured does not land. A MIDI take with no notes is
  kept as an empty clip today; it counts as not landed and does not fire.
- **Target scene moved, inserted around or renamed**: the rule follows the scene it names.
  **Target scene deleted**: the rule turns Off (the inspector reads Off, the grid mark goes).
- **Rule's own scene row moved, duplicated or deleted, or its track deleted**: the rule travels or
  drops with its slot, as the Stop button and take length do; a duplicated row copies it.
- **Clip copied, moved, pasted or dragged in from the Arrangement**: the rule belongs to the slot,
  not the clip; it is neither carried to another slot nor overwritten.
- **Slot holds a clip**: the rule is not shown in the clip inspector and cannot be edited there; it
  stays attached and is shown again when the slot is empty. A retry of the landed take honours it.
- **Session performance recorded into the Arrangement** (specs/109): the launch caused by the rule
  is recorded like any other scene launch.
- **Recording started from Nota Remote, a MIDI controller or MCP**: the rule fires the same way;
  setting it from Remote or controllers is out of scope (#103, #335).
- **Arrangement recording**: unaffected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Every Session slot MUST carry an optional landing rule with a destination — **Off**
  (default), **Launch next scene**, or **Launch scene** (a chosen scene) — and a boundary —
  **Global** launch quantization or one of the launch quantization values.
- **FR-002**: The empty-slot inspector MUST show the rule, let the user change it and explain when
  it fires; while the global Follow switch is off it MUST say the rule is off globally.
- **FR-003**: An empty slot with a rule MUST show a mark in the Session grid distinct from a clip
  and from a prepared take length.
- **FR-004**: When a take recorded into a slot **lands** (see Definitions), the slot's rule MUST
  queue its scene launch for the first chosen boundary at or after the landing moment — once per
  landed take, never at the Stop request or the end of capture.
- **FR-005**: The rule MUST NOT fire for a stopped count-in, a take cancelled before it started, a
  take that failed to start or captured nothing (including a MIDI take with no notes), a retry
  stopped before its boundary, an overdub, a take ended by a global (Session or transport) stop,
  or while the global Follow switch is off.
- **FR-006**: A take discarded or retried while recording MUST NOT fire for that attempt. Removing
  a landed take (Undo, or deleting its clip) while its queued launch still waits MUST cancel that
  launch; removing it after the launch MUST NOT reverse the launch or fire again.
- **FR-007**: Only a fresh take (the slot was empty when it started) fires the rule. A later take
  into the same slot that lands (a retry, or a new take after the clip
  was removed) MUST fire the rule once for that take.
- **FR-008**: Undo, Redo, saving and reopening a project MUST NOT fire a rule or queue a launch.
- **FR-009**: The scene launch queued by a rule MUST behave as any scene launch, including being
  replaced by a later launch request and being cancellable while waiting (#480).
- **FR-010**: Changing a rule MUST be an Undo/Redo step of its own that marks the project modified.
- **FR-011**: Rules MUST be saved with the project and restored on reopen; projects saved before
  this feature MUST open with every rule Off.
- **FR-012**: A rule MUST belong to its slot (track × scene) and follow the slot through scene
  moves, duplicates and deletes and track deletes; clip copy, move, paste and Arrangement drops
  MUST neither carry nor overwrite it. A rule naming a deleted scene MUST turn Off.
- **FR-013**: The MCP Session tools MUST read and set a slot's rule and refuse unknown scenes or
  boundaries with a clear message; the Session snapshot MUST report it.
- **FR-014**: Existing clip Follow actions, scene follow, per-clip bypass, take length and the
  recording behaviour itself MUST be unchanged.

### Key Entities

- **Landing rule (per slot)**: destination (Off / Launch next scene / Launch scene X) and boundary
  (Global / quantization value). Belongs to a slot (track × scene); configuration only — it holds
  no record of past firings, so nothing can replay.
- **Take landing**: the one moment a take into a slot becomes a playable clip; at most one per take.
- **Queued scene launch**: the existing waiting-launch state the rule feeds into.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the issue's example (4-bar bass take, rule "launch Chorus, next bar"), Chorus
  launches on the first bar boundary after the take lands in 100% of runs, exactly once.
- **SC-002**: Across stopped count-in, cancelled, failed, empty, discarded-before-boundary and
  globally-stopped takes, no launch is ever queued (0 launches).
- **SC-003**: Reopening a project, Undo and Redo never cause a scene launch.
- **SC-004**: Setting a rule takes at most three choices in the inspector (select slot, pick
  destination, pick boundary) and is visible on the grid without opening the inspector.
- **SC-005**: Every pre-feature project opens and plays exactly as before.

## Assumptions

- Out of scope for this version: track-only Follow actions (Stop, Again, Previous… on the slot's
  own track) and Stop Session as destinations; they can come in a follow-up issue.
- "Launch next scene" from the last scene wraps to the first, as scene follow does today.
- Setting the rule from Nota Remote, controllers or command bindings is out of scope (#103, #335);
  takes started from them still fire it.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
