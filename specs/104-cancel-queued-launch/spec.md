# Feature Specification: Cancel a queued Session launch before its quantized start

**Feature Branch**: `feat/cancel-queued-launch`

**Created**: 2026-11-18

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#480 (approved, milestone 0.61.1): "During a live
performance, I may queue a clip or scene for the next bar and then realize it was the wrong
choice. I would like to cancel that pending launch while the current loop continues at the same
musical position. […] showing the pending launch and offering a deliberate way to cancel that
specific request before its boundary. Canceling should clear its pending state without stopping
the transport or outgoing clips. A later launch should still work normally. A launch that has
already begun should use normal stop behavior instead. Example: a four-bar drum clip is playing.
I queue a bass scene for the next bar, then cancel before the boundary. The bass scene does not
start and the drum continues. A Follow Action belonging only to the canceled launch should not
fire as though it had happened. Please clarify the effects of a second activation and Stop while
a launch is pending."

## Nota Context

**Feature area**:

- [x] Engine (C++, `src/native/nota.engine`) — a track's pending request can be withdrawn, and a
  scene launch remembers what it queued so the whole row can be withdrawn together
- [x] Managed app (C#, `src/managed/*`) — interop for the new call; the MCP Session tools; Nota Remote's hub and phone page
  (`src/managed/Nota.Remote`, `src/web/remote`)
- [x] UI (Avalonia, `src/managed/Nota.Presentation` / `Nota.App`) — the cancel gesture on a
  queued slot and a queued scene, slot and scene menu items
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`BUILD.md`](../../BUILD.md),
[`DESIGN.md`](../../DESIGN.md), [`FEATURES.md`](../../FEATURES.md).

**How it works today** (the baseline this changes): each track holds at most one pending request
— a slot to launch or a stop — which applies at the next launch-quantum boundary. A queued slot
and a queued scene blink in the Caution colour. Activating a queued slot again re-queues the same
launch (no visible change). Stop on a track replaces a queued launch with a queued stop, so the
playing clip stops at the boundary and the queued one never starts. A scene launch queues the
scene's clip on every track and a **stop** on every track whose slot in that scene is empty (unless
that slot's stop button was removed); it also replaces the scene follow action and any pending
scene tempo / signature change. There is no way to withdraw a queued request without queuing a
different one.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Cancel a queued clip launch (Priority: P1)

A clip is playing on a track. The performer queues another clip on the same track (or a clip on
an idle track), sees it blinking, and withdraws it before the boundary. The playing clip keeps
looping at the same position; the queued clip never starts and stops blinking.

**Why this priority**: the core of the request — the smallest unit of "I picked the wrong one".

**Independent Test**: play a clip, launch another slot on the same track with a 1-bar quantum,
cancel it mid-bar; at the boundary the first clip is still playing, uninterrupted, and the second
slot shows as a plain filled slot.

**Acceptance Scenarios**:

1. **Given** clip A is playing on a track and clip B on the same track is queued, **When** the
   performer cancels B before the boundary, **Then** B never starts, A keeps playing with no
   restart, gap or position jump, and B's slot no longer shows as queued.
2. **Given** nothing plays on a track and clip B is queued on it, **When** the performer cancels
   B, **Then** the track stays silent and the transport keeps running.
3. **Given** a launch was canceled, **When** the performer launches B again, **Then** B queues and
   starts at the next boundary as any launch does.
4. **Given** B has already started (the boundary passed), **When** the performer looks for the
   cancel action, **Then** it is not offered; stopping B is done with the normal Stop.

---

### User Story 2 - Cancel a queued scene launch (Priority: P1)

A four-bar drum clip is playing. The performer queues a bass scene for the next bar, then cancels
the scene before the boundary. Nothing that scene would have done happens: its clips don't start,
the tracks it would have stopped keep playing, its follow action does not fire, and its tempo /
signature change does not apply.

**Why this priority**: it is the issue's own example, and a scene launch touches every track —
cancelling only the clips that start would still stop the drum, which is the very failure the
performer wants to avoid.

**Independent Test**: drum clip playing in scene 1; scene 2 holds a bass clip and an empty drum
slot (with its stop button); launch scene 2, cancel it mid-bar; at the boundary the drum still
plays, the bass is silent, and nothing changes in the following bars.

**Acceptance Scenarios**:

1. **Given** the drum plays and scene 2 is queued (bass to start, drum to stop), **When** the
   performer cancels scene 2, **Then** at the boundary the bass does not start and the drum keeps
   playing at the same musical position.
2. **Given** scene 2 has a follow action and a tempo change, **When** scene 2 is canceled before
   its boundary, **Then** the follow action never fires and the tempo / signature stay as they
   were; a scene follow action that was running before the canceled launch carries on as if the
   launch had never been requested.
3. **Given** scene 2 is queued and the performer then launched a different clip on one of its
   tracks, **When** scene 2 is canceled, **Then** only the requests scene 2 still owns are
   withdrawn; the newer, separate launch stays queued.
4. **Given** scene 2 has started, **When** the performer looks for the cancel action, **Then** it
   is not offered.

---

### User Story 3 - Know what a second press and Stop do while a launch waits (Priority: P2)

The performer has a clear, predictable answer for the two other things they might press in a
hurry: the same queued slot / scene again, and Stop.

**Why this priority**: the issue asks for these effects to be settled; live, an accidental double
press must not have a surprising result.

**Independent Test**: queue a clip, press it again, observe; queue a clip, press the track's Stop,
observe; queue a scene, press Stop All, observe.

**Acceptance Scenarios**:

1. **Given** clip B is queued, **When** the performer activates B again, **Then** B stays
   queued, unchanged (as today) — a hurried double press never withdraws a launch; cancelling is
   its own deliberate gesture. The same holds for a queued scene.
2. **Given** clip A plays and clip B on the same track is queued, **When** the performer presses
   the track's Stop, **Then** B is withdrawn and A stops at the boundary (unchanged from today:
   Stop means "this track goes silent", whatever was queued).
3. **Given** a scene is queued, **When** the performer presses Stop All, **Then** every queued
   launch is withdrawn, every playing clip stops at the boundary, and the queued scene's follow
   action and tempo change do not apply.

---

### User Story 4 - Cancel from Nota Remote and MCP (Priority: P3)

The same cancel is available where launches can be made away from the desktop grid.

**Why this priority**: useful parity, but the desktop gesture already solves the issue.

**Independent Test**: queue a scene from the desktop, cancel it from the other surface, observe the
same result as Story 2.

**Acceptance Scenarios**:

1. **Given** a slot or scene is queued, **When** an MCP client asks to cancel it, **Then** the
   result matches Stories 1–2, and the Session snapshot reports it is no longer queued.
2. **Given** a slot or scene is queued, **When** the performer cancels it in Nota Remote,
   **Then** the result matches Stories 1–2 and both the phone and the desktop stop showing it as
   queued.

---

### Edge Cases

- **Cancel at the boundary**: a cancel that arrives in the same instant the boundary applies has
  exactly one outcome — either the launch is withdrawn whole or it has started and stays started;
  never half (a scene with some tracks switched and others not).
- **Clips with their own quanta**: a scene whose clips start at different boundaries has started
  once its first clip starts; from then on it is not cancelled as a scene (its remaining clips
  start as launched; one of them can still be cancelled on its own slot).
- **A row that queues only stops** (an empty row with stop buttons), or a restart of a clip that is
  already playing, is queued too and can be cancelled, though no slot shows the queued state.
- **Transport stopped**: launching with the transport stopped starts the Session clock and the
  launch applies at once, so there is nothing to cancel.
- **Follow action of the playing clip** that is itself about to switch the track: it has queued
  nothing until its time arrives; cancel offers only what is queued.
- **Repeat / Gate launch modes**: cancelling a queued Repeat clip also drops its repeat; a Gate clip
  that is held and queued is withdrawn and releasing the key does nothing further.
- **A queued stop** (not a launch) is not covered: the cancel action is offered for queued launches
  only. A queued stop is undone by launching again, as today.
- **Session recording** that waits for its quantum (record pending) is not a launch and is out of
  scope (see #185).
- **Scene partially overridden**: if every track's request from a scene has since been replaced by
  newer launches or stops, nothing of that scene launch is left to start: its follow action and
  tempo change are dropped as for a cancel.
- **A scene with nothing to queue** (every slot empty with its stop button removed) applies its
  tempo / follow at the boundary as today; there is nothing for a cancel to withdraw.
- **A scene launched by a scene follow action**: cancelling it ends scene follow — the follow
  that chose it has already run out, so the outgoing scene keeps playing without launching another
  one by itself.
- **Every request of a queued scene cancelled one slot at a time**: once none is left, the scene
  launch counts as cancelled (its follow action and tempo change are dropped), as with a scene
  cancel.
- **Undo**: cancelling is a performance action, not an edit; it does not enter the undo history and
  does not mark the project as changed.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The performer MUST be able to cancel a queued launch of one slot before its
  boundary; after the cancel, the track continues exactly as if that launch had never been
  requested (the playing clip, if any, keeps playing with no gap, restart or position change).
- **FR-002**: The performer MUST be able to cancel a queued scene launch before its boundary;
  every request that launch queued and still owns — clip starts and the stops of tracks with an
  empty slot — is withdrawn together, and the scene's follow action and tempo / signature change
  are dropped. A scene follow action that was running before that launch MUST carry on unchanged.
- **FR-003**: Cancelling MUST NOT stop or pause the transport, move the playhead, or stop, restart
  or re-time any playing clip.
- **FR-004**: The cancel action MUST be offered only while there is something queued to cancel on
  that slot / scene; once a launch has started it is not offered, and normal Stop applies.
- **FR-005**: A cancel and the boundary MUST NOT race into a partial result: a slot launch is
  either withdrawn or started; a scene launch is withdrawn on all its tracks or started on all.
- **FR-006**: After a cancel, the canceled slot / scene MUST stop showing as queued within one
  display refresh, and a later launch of it MUST behave like any launch.
- **FR-007**: The desktop Session grid MUST offer the cancel as Alt+click on the launch button of
  a queued slot and of a queued scene, and as "Cancel launch" in the slot and scene context menus
  (shown only while queued). The queued launch button's tooltip names the Alt+click cancel.
- **FR-008**: A second activation of a queued slot or scene MUST behave as settled in Story 3.1,
  and that behaviour MUST be documented.
- **FR-009**: Stop on a track with a queued launch MUST withdraw the launch and stop the track at
  the boundary (today's behaviour, now documented); Stop All MUST also drop a queued scene's
  follow action and tempo / signature change.
- **FR-010**: A Follow Action belonging only to a canceled launch MUST NOT fire; the clip follow
  action of a clip that keeps playing MUST keep counting from its own launch, unaffected.
- **FR-011**: Cancelling MUST NOT create an undo step or mark the project as modified.
- **FR-012**: MCP clients MUST be able to cancel a queued slot or scene launch, with the same
  result as on the grid.
- **FR-013**: Nota Remote MUST offer the cancel on a queued slot and a queued scene of its Session
  grid, with the same result as on the desktop.
- **FR-014**: The user manual's Session page MUST describe the cancel action, the second press and
  Stop while a launch waits.

### Key Entities

- **Queued request (per track)**: what a track will do at the next boundary — start a given slot,
  or stop — plus the launch options it carries. At most one per track.
- **Scene launch**: one request to start a row; it owns the per-track requests it queued (starts
  and stops), the scene follow action it set up, and the scene tempo / signature change, until
  the boundary applies them or they are replaced, withdrawn or stopped.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the issue's example (four-bar drum, bass scene queued then canceled), the drum
  plays without a gap, restart or level change through the boundary in 100% of runs.
- **SC-002**: A canceled scene's follow action fires 0 times over the following 16 bars.
- **SC-003**: A cancel issued at any point before the boundary — including in the last audio
  block before it — leads to either "fully withdrawn" or "fully started", never a mix, across
  repeated automated runs.
- **SC-004**: A performer can cancel a queued launch with a single deliberate action, without
  opening a dialog.
- **SC-005**: The cancel adds no audible artefact and no measurable extra audio-thread work when no
  launch is queued.

## Clarifications

### Session 2026-11-18

- Q: What does a second activation of a queued slot / scene do? → A: Nothing new — it stays
  queued as today; cancelling is a separate deliberate gesture.
- Q: The direct cancel gesture on the desktop grid? → A: Alt+click on the queued launch button
  (slot and scene), plus "Cancel launch" in the context menus.
- Q: Is Nota Remote in scope? → A: Yes, in this pull request, together with MCP.

## Assumptions

- The cancel withdraws only queued launches; queued stops and record-pending takes are out of
  scope (record discard is #185, selected-slot controls #364, MIDI action bindings #103).
- A scene's "previous" follow action is the one that was running when the canceled scene was
  requested; it resumes counting as if never interrupted.
- The Session's launch-quantum, legato and per-clip options need no change.
- No project-file format change: queued state is not saved.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
