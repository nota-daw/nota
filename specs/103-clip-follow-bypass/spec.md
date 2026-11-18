# Feature Specification: Bypass one Session clip's Follow configuration without changing it

**Feature Branch**: `feat/clip-follow-bypass`

**Created**: 2026-11-18

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#499: "During a live set, I want to keep one clip playing for
longer while other clips' Follow Actions remain active, then restore that clip's prepared behavior
without rebuilding its A/B choices, probabilities or timing. […] a local bypass for the selected
Session clip's whole Follow configuration. While bypassed, keep its A/B actions, weights, Jump
target and timing intact and show clearly that they are temporarily inactive. Other clips and the
global Follow setting should be unaffected. Save/reopen should preserve the configuration and its
bypass state. […] Please define the next eligible musical boundary; an action whose deadline passed
while bypassed should not fire unexpectedly as a catch-up event. Ordinary clip playback and manual
launching should remain available."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — the per-slot bypass flag and the audio thread's
  follow check; real-time rules apply
- [x] Managed app (C#, `src/managed/*`) — clip-props contract, project save/load, MCP tools
- [x] UI (Avalonia) — the clip inspector's Follow action section and the Session slot
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: a Session clip carries Follow action A and B (None, Stop,
Again, Previous, Next, First, Last, Any, Other, Jump), a relative chance for each, a Jump target
scene and a follow time ("After", 0 = one pass of the clip). Once the playing clip has played its
follow time since it launched, A or B is picked by chance; a None result restarts the count, any
other action relaunches or stops the track exactly at that moment (not quantized). A global Follow
switch in the Session toolbar turns follow off for every clip and scene; the inspector greys the
Follow section and says "off globally" while it is off. The only way to silence one clip today is
to set both actions to None, which loses the prepared configuration.

## Clarifications

### Session 2026-11-18

- Q: After the bypass is turned off past the follow time, when does the Follow action fire? → A:
  at the next multiple of the follow time counted from the start of the clip's follow count (its
  launch), i.e. where it would have fired without the bypass (FR-006).
- Q: How is a bypassed clip marked in the Session grid? → A: a mark on the slot only when the
  clip has a Follow action configured (A or B not None) (FR-008).
- Q: Where else can the bypass be toggled besides the inspector? → A: the slot context menu
  (FR-008a); the grid selects one slot at a time, so it acts on that clip.

## User Scenarios & Testing

### User Story 1 - Hold one clip during a live set (Priority: P1)

A performer has a four-bar lead clip that normally moves to the next scene after two passes. During
a solo they bypass that clip's Follow configuration; the clip keeps looping while the other clips'
Follow actions keep running. When the solo ends they turn the bypass off and the clip's prepared
A/B actions, chances, Jump target and follow time work again, untouched.

**Why this priority**: this is the whole request.

**Independent Test**: set a clip to Next after 2 passes, launch it, bypass its Follow before the
follow time is reached — the clip keeps looping well past its follow time and no other clip
launches on that track; a second track's clip with its own Follow action still advances on time;
turn the bypass off — the inspector shows the same A/B, chances, Jump target and After as before.

**Acceptance Scenarios**:

1. **Given** a playing clip with Follow action Next after 2 passes, **When** its Follow is
   bypassed, **Then** it keeps playing (looping) past that time and nothing is launched or stopped
   on its behalf.
2. **Given** a bypassed clip, **When** another clip on another track reaches its follow time,
   **Then** that clip's Follow action fires as normal.
3. **Given** a bypassed clip, **Then** the global Follow switch is unchanged, and turning the global
   switch off and on does not change the clip's bypass.
4. **Given** a bypassed clip, **When** the performer launches it, stops it, or launches another
   clip on the same track by hand, **Then** that works exactly as for any clip.
5. **Given** a bypassed clip, **When** the bypass is turned off, **Then** its A/B actions, chances,
   Jump target and follow time are exactly what they were before the bypass.

### User Story 2 - Re-enable without a surprise jump (Priority: P1)

The performer turns the bypass off long after the clip's follow time has passed. The clip does not
jump away the instant the bypass goes off; its Follow action fires at the next eligible musical
boundary (defined in FR-006).

**Why this priority**: a catch-up jump in the middle of a phrase is exactly the failure the issue
asks to avoid; without this the bypass is unusable live.

**Independent Test**: a 4-bar clip set to Next after 2 passes, bypassed for 3 passes (48 beats in
4/4), then re-enabled mid-pass: the clip keeps playing and the next scene's clip starts at the
boundary FR-006 defines, not at the moment of re-enabling.

**Acceptance Scenarios**:

1. **Given** a clip bypassed past its follow time, **When** the bypass is turned off, **Then** no
   Follow action fires at that moment.
2. **Given** the same, **Then** the Follow action fires at the next eligible boundary (FR-006) and,
   from then on, as configured.
3. **Given** a clip whose bypass is turned off before its follow time was ever reached, **Then**
   the Follow action fires at its normal follow time, as if it had never been bypassed.

### User Story 3 - See which clips are held (Priority: P2)

The performer selects a clip and sees in the inspector that its Follow configuration is bypassed:
the settings are still visible and editable but read as temporarily inactive, with a clear switch
to restore them. In the Session grid, a bypassed clip is marked so the performer can tell which
clips are being held without selecting each one.

**Why this priority**: the request asks to "show clearly"; the grid mark matters live, where only
one clip is selected at a time.

**Independent Test**: bypass a clip's Follow: the inspector's Follow section reads as inactive with
a "Bypassed" state and its settings still show; the slot in the grid carries a mark; un-bypass —
both go back to normal.

**Acceptance Scenarios**:

1. **Given** a selected clip, **Then** the inspector's Follow action section has a control that
   turns the bypass on and off.
2. **Given** a bypassed clip, **Then** its Follow settings still show their values, read as
   inactive, and can still be edited (the edits are kept for when the bypass ends).
3. **Given** a bypassed clip, **Then** its slot in the Session grid shows a bypass mark when the clip
   has a Follow action configured (A or B is not None); with both actions None it shows no mark.
4. **Given** the global Follow switch is off, **Then** the inspector still shows the clip's own
   bypass state, distinct from "off globally".

### User Story 4 - Keep it in the project (Priority: P2)

The performer saves the set with one clip bypassed; reopening it shows that clip still bypassed
with its configuration intact, and every other clip unbypassed.

**Independent Test**: bypass one clip, save, close, reopen: the same clip is bypassed and its
Follow settings are unchanged; an older project opens with no clip bypassed.

**Acceptance Scenarios**:

1. **Given** a saved project with a bypassed clip, **When** it is reopened, **Then** that clip is
   bypassed and its A/B, chances, Jump target and follow time are as saved.
2. **Given** a project saved before this feature, **When** it is opened, **Then** no clip is
   bypassed.
3. **Given** a bypassed clip, **When** it is duplicated or copied to another slot, or its scene is
   duplicated, **Then** the copy carries the same bypass state and configuration.

### User Story 5 - Drive it from the assistant / MCP (Priority: P3)

An MCP client reads the Session snapshot and sees which clips are bypassed, and can set or clear a
clip's bypass without touching its other Follow settings.

**Independent Test**: call the session snapshot — a bypassed clip reports it; call the set-clip
tool with only the bypass argument — only the bypass changes.

### Edge Cases

- A clip with both actions None can still be bypassed; bypass has no audible effect then, but the
  state is kept and shown.
- Bypass while the clip is not playing: kept; when the clip is launched it plays without following
  until the bypass is turned off.
- Bypass turned on in the same block the follow time is reached: either outcome (fires once, or
  holds) is acceptable, but never both and never a fire after the bypass is visibly on for a block.
- Re-enable while a launch of another clip on the same track is already queued: the queued launch
  wins; the bypassed clip's Follow does not add a second action.
- Re-enable while the transport is stopped: nothing fires; on the next launch the clip follows
  normally from its launch.
- A Repeat-mode clip held down: Repeat retriggering is unaffected by the bypass.
- Undo / redo: toggling the bypass is one undo step like any other clip property edit.
- Scene Follow ("Next after" on a scene) is a different setting and is not affected by a clip's
  bypass.

## Requirements

### Functional Requirements

- **FR-001**: Each Session clip MUST have a Follow bypass state, off by default.
- **FR-002**: While a clip's Follow is bypassed, its Follow actions MUST NOT fire; the clip MUST
  keep playing as configured otherwise (looping, one-shot end, launch mode, legato, quantize,
  velocity).
- **FR-003**: Turning the bypass on or off MUST NOT change the clip's A/B actions, chances, Jump
  target or follow time; those settings MUST stay editable while bypassed.
- **FR-004**: A clip's bypass MUST NOT affect other clips, scene Follow, or the global Follow
  switch; the global switch MUST NOT change any clip's bypass state. A clip follows only when the
  global switch is on AND the clip is not bypassed.
- **FR-005**: Manual launching and stopping of a bypassed clip, and of other clips on its track,
  MUST work as for any clip.
- **FR-006**: When the bypass is turned off while the clip is playing, a Follow action whose time
  passed during the bypass MUST NOT fire at that moment. The next eligible boundary is the first
  multiple of the clip's follow time, counted from where the clip's follow count started (its
  launch, or the last None outcome), that lies after the moment the bypass is turned off — the
  point where it would have fired had it never been bypassed. Example: a 4-bar clip (16 beats in
  4/4) set to Next after 2 passes (32 beats), bypass turned off at beat 50 → Next fires at beat 64.
  If the bypass ends before the follow time was first reached, the action fires at its normal time
  (beat 32).
- **FR-007**: The clip inspector MUST offer a control to bypass / restore the selected clip's
  Follow configuration and MUST show the bypassed state clearly and distinctly from "off globally".
- **FR-008**: The Session grid MUST mark a bypassed clip's slot when that clip has a Follow action
  configured (A or B not None).
- **FR-008a**: A filled slot's context menu MUST offer a Follow bypass toggle for that clip (the
  Session grid selects one slot at a time).
- **FR-009**: The bypass state MUST be saved with the project and restored on open; projects saved
  without it MUST open with no clip bypassed.
- **FR-010**: Copying, duplicating or moving a clip (slot copy, scene duplicate, scene move) MUST
  carry its bypass state.
- **FR-011**: Toggling the bypass MUST be undoable as one step.
- **FR-012**: The MCP Session snapshot MUST report each clip's bypass, and the clip-property tool
  MUST be able to set it on its own.

### Key Entities

- **Session clip Follow configuration**: actions A/B, chances A/B, Jump target, follow time — plus
  the new **Follow bypass** flag, a property of the clip saved with it.

## Success Criteria

### Measurable Outcomes

- **SC-001**: A bypassed clip plays at least 4× its follow time with zero Follow-triggered launches
  or stops on its track, while a non-bypassed clip on another track follows on time in the same
  run.
- **SC-002**: Restoring a bypass leaves 100% of the clip's Follow settings identical to before.
- **SC-003**: After re-enabling past the follow time, the first Follow action lands on the FR-006
  boundary (within one audio block) and never at the moment of re-enabling.
- **SC-004**: A save → reopen round trip preserves every clip's bypass state and settings.
- **SC-005**: A performer can bypass or restore the selected clip in one click from the inspector.

## Assumptions

- The bypass is per clip (per slot), not per track or per scene.
- The control lives in the inspector's Follow action section header and in the slot context menu.
- No keyboard shortcut, MIDI mapping or Nota Remote control is added in this change (MIDI action
  bindings are #103); the state may be exposed to those later.
- The global Follow switch keeps its current behaviour (including how it resumes); only the
  per-clip bypass gets the no-catch-up rule.

## Nota Compliance Checklist

- [x] Every new source file will carry the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact considered: the bypass is a POD field of the immutable slot snapshot
  (AR-5); the audio thread only reads it and keeps its resume point in player state it already
  owns — no alloc / lock / I/O
- [x] Managed/native crossing through the C ABI only (AR-7): the clip-props struct grows one field
- [x] Smoke test extended: bypass holds, other track follows, re-enable fires at the FR-006
  boundary, props round-trip
- [x] `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md and the Session docs page planned
- [x] UI follows `DESIGN.md`
