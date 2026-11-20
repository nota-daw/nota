# Feature Specification: Record a Session Performance into the Arrangement

**Feature Branch**: `feat/session-to-arrangement-record`

**Created**: 2026-11-20

**Status**: Draft

**Input**: User description: "GitHub issue #971 (https://github.com/nota-daw/nota/issues/971): Record a Session performance into the Arrangement. When the Arrangement Record is on and the transport runs, Session clip launches/stops, scene launches, follow actions, Session slot takes, mixer/device moves (as automation) and scene tempo/signature changes are written onto the Arrangement at their real times; armed tracks' input recorded as today; replaces Arrangement content over the recorded range on written tracks only; one undo step; Back to Arrangement stays lit afterwards."

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

**Related work**: specs/078 (Discard and retry a take — a pass must be discardable the same
way), specs/086 (several slots / tracks in one take), specs/099 (master Tempo lane), specs/104
(cancel a queued launch — a cancelled launch writes nothing), specs/106 (Take length per
slot), #501 (an Arrangement section starts a Session recording — the other direction).

## Today *(as found in the code, 2026-11-20)*

- **Record needs an armed track.** Pressing Record with nothing armed auto-arms the record
  target (the selected track) and starts a take; if that fails the button pops back up with
  "arm a track".
- **Arrangement Record captures input only.** The take records the armed tracks' input
  (audio, MIDI notes, controllers). Session clips launched during the take play on top of the
  Arrangement and leave nothing on the timeline.
- **A track plays either its Session clip or its Arrangement.** While a slot plays, the
  track's Arrangement content is silent; when the slot stops (Stop, Stop All, an empty slot
  in a scene, a follow action to stop), the track plays its Arrangement again at once. There
  is no "stopped but still held by Session" state. **Back to Arrangement** stops every
  Session clip.
- **Launching a clip from stop rolls a Session-only clock**: the transport runs but the
  Arrangement is not active. Play or Record from stop runs the Arrangement with Session
  clips layered over it.
- **Automation recording already works with Record**: moving a mixer or device control
  while Record is engaged writes that parameter's lane (mouse: until release; hardware:
  latched until the transport stops).
- **Scenes can carry a tempo and a time signature**; a scene launch changes the project
  tempo / signature. Tempo can be automated on the master Tempo lane; the time signature is
  one project value with no lane.
- **Moving material by hand**: ⌘⇧C Copy to Arrangement sends clips, slots or a scene across
  with notes, envelopes, warp, pitch and gain, without timing.

## Clarifications

### Session 2026-11-20

- Q: Is the Session performance always captured by Record, or behind its own switch? → A:
  Always with Record, as Ableton's Arrangement Record — no new switch.
- Q: What does Record do with nothing armed? → A: While a Session clip is playing or queued
  it starts a performance-only pass (no input recorded, nothing auto-armed); with the
  Session silent it auto-arms the record target as today.
- Q: An armed instrument track also plays a Session clip and the user plays over it — what
  lands in the Arrangement? → A: One clip: the played notes are merged into the written
  clip, so it sounds as heard. Audio tracks are unaffected (with monitoring on the slot is
  silent and only the input take is recorded).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Play clips in Session, get them on the timeline (Priority: P1)

The performer has ideas in Session slots: drums, bass, chords. With Record on and the
transport running, they launch the drums, then the bass four bars later, swap chord clips,
and stop the bass. When they stop, each track's Arrangement lane holds clips exactly where
they played: drums from bar 1, bass from bar 5 to where it was stopped, one chord clip after
another. Playing the Arrangement back sounds like the performance.

**Why this priority**: This is the request — turning a Session performance into an
arrangement without copying clips by hand.

**Independent Test**: At 120 BPM 4/4 with launch quantize 1 bar, Record from bar 1, launch a
2-bar drum slot at once, a 1-bar bass slot during bar 4, stop the bass during bar 8, stop the
transport at bar 13. The drum lane holds one clip from bar 1 to bar 13, looping its 2 bars;
the bass lane holds one clip from bar 5 to bar 9. After Back to Arrangement, playback from
bar 1 renders the same as the recorded performance (deterministic clips, see SC-001).

**Acceptance Scenarios**:

1. **Given** Record is on and the transport runs, **When** the user launches a slot, **Then**
   a clip with the slot's content is written to that track's Arrangement lane starting at the
   beat the slot actually started (after launch quantize), not when the button was pressed.
2. **Given** a slot is playing during the pass, **When** it stops (Stop, Stop All, an empty
   slot in a scene, a follow action, another slot launched on the track), **Then** the
   written clip ends at the beat it stopped.
3. **Given** a slot shorter than the time it played, **When** the pass ends, **Then** the
   slot's content is repeated for the whole time it played: an instrument slot as one clip
   holding every repetition, an audio slot as one clip per repetition, back to back
   (Arrangement clips do not loop by themselves).
4. **Given** a scene launch during the pass, **When** it starts clips on several tracks,
   **Then** each of those tracks gets its clip from the same beat.
5. **Given** a follow action or a Repeat launch moves a track from slot to slot, **When** the
   pass ends, **Then** the lane holds one clip per slot played, back to back, each where it
   played.
6. **Given** a slot launched with Legato (it takes over the previous clip's position),
   **When** it is written, **Then** the written clip starts inside the slot's content at that
   position, so it sounds as heard.
7. **Given** a slot was already playing when Record started, **When** the pass ends,
   **Then** its clip is written from the beat Record started, starting inside the slot's
   content at the position it had reached.
8. **Given** a launch was queued and cancelled before its boundary (specs/104), **When** the
   pass ends, **Then** nothing of it is written.
9. **Given** an audio slot and an instrument slot, **When** they are written, **Then** each
   written clip carries what the slot has: notes and controllers, clip envelopes, warp,
   pitch, gain, launch velocity — it sounds the same in the Arrangement.

---

### User Story 2 - Only what was played is replaced (Priority: P1)

The track already has Arrangement material. Over the time a Session clip played on it, the
pass replaces that material with the written clip. Before the first launch and after the
slot stopped, the track played its Arrangement, and that material stays. Tracks that never
played a Session clip during the pass are not touched.

**Why this priority**: Without a clear replace rule the feature destroys work; the rule
"the Arrangement afterwards plays what you heard" is the one users can predict.

**Independent Test**: A track with an 8-bar Arrangement clip from bar 1; Record from bar 1,
launch a slot on that track at bar 3, stop it at bar 5. Afterwards the Arrangement clip is
split: bars 1–3 and 5–9 keep the original, bars 3–5 hold the written clip. A second track
with Arrangement material and no Session launch is unchanged.

**Acceptance Scenarios**:

1. **Given** Arrangement material on a track, **When** a Session clip played on it from beat
   A to beat B during the pass, **Then** the material between A and B is replaced by the
   written clip and the material outside A–B is kept, trimmed at A and B.
2. **Given** a track that played no Session clip during the pass, **When** the pass ends,
   **Then** its Arrangement lane is unchanged (apart from its own input take if armed).
3. **Given** Arrangement automation on a track whose Session clip played, **When** the pass
   ends, **Then** the track's automation lanes are unchanged unless controls were moved
   (automation recording rules apply as today).

---

### User Story 3 - The pass is one take: input, automation, undo, retry (Priority: P2)

The performer records the Session performance and, in the same pass, plays a keyboard part
into an armed track and rides a filter. Everything from the pass — written clips, the input
take, the automation — is one undo step, and ⌘. throws the whole pass away and starts it
again.

**Why this priority**: The pass is one musical act; splitting it across undo steps or
leaving half of it after a retry would surprise the user.

**Independent Test**: Record with one armed instrument track, launch two slots on other
tracks, move a device knob, stop. One Undo removes the written clips, the input take and the
automation together; Redo brings them all back.

**Acceptance Scenarios**:

1. **Given** armed tracks during the pass, **When** the pass runs, **Then** their input is
   recorded into the Arrangement exactly as today, alongside the written Session clips.
2. **Given** an armed instrument track that is also playing a Session clip, **When** the
   user plays notes over it during the pass, **Then** the Arrangement holds one clip: the
   written clip with the played notes and controllers merged in, sounding as heard.
3. **Given** a finished pass, **When** the user presses Undo once, **Then** every clip
   written by the pass, the input takes and the automation recorded in the pass are removed
   and the replaced Arrangement material comes back; Redo restores the pass.
4. **Given** a pass in progress, **When** the user discards and retries the take (⌘.),
   **Then** nothing written by the pass remains and recording starts again as specs/078
   describes for the Arrangement.
5. **Given** a Session slot take recorded during the pass (Session rec or recording into an
   empty slot), **When** the pass ends, **Then** the take is kept in its slot as today and
   the Arrangement holds it too, from the beat it started recording, including the looping
   after it.

---

### User Story 4 - Listen back with one click (Priority: P2)

Stop stops every Session clip (as today, #139), so after a pass every track plays its
Arrangement again: Play from the start plays the recorded arrangement. When Record is turned
off while the transport keeps running, the Session keeps the tracks it plays, and Back to
Arrangement hands them back with one click, as today.

**Why this priority**: Closing the loop between performing and listening, with today's
controls.

**Independent Test**: After a pass ended with Stop, no slot plays and the written clips are on
the tracks; Play from bar 1 plays them.

**Acceptance Scenarios**:

1. **Given** slots were playing when the transport stopped, **When** the pass ends, **Then**
   Session state is as today after a stop (every slot stopped, every track on its
   Arrangement).
2. **Given** the pass ended, **When** the user opens the Arrangement, **Then** every written
   clip is visible on its track with the slot's name.

---

### User Story 5 - Tempo changes from scenes land on the Tempo lane (Priority: P3)

A scene with its own tempo launched during the pass writes the tempo change to the master
Tempo lane at the beat it took effect, so the Arrangement plays back at the same tempos.

**Why this priority**: Only matters for sets that use scene tempos, but without it the
recorded arrangement plays at the wrong speed after the first tempo change.

**Independent Test**: Record at 120 BPM, launch a scene with tempo 100 at bar 5, stop at
bar 9. The master Tempo lane holds 120 up to bar 5 and 100 from bar 5; Arrangement playback
from bar 1 changes tempo at bar 5.

**Acceptance Scenarios**:

1. **Given** no Tempo automation, **When** a scene with a tempo is launched during the pass,
   **Then** the master Tempo lane gets the tempo before the pass up to that beat and the
   scene's tempo from that beat, as an instant change.
2. **Given** existing Tempo automation, **When** a scene with a tempo is launched during the
   pass, **Then** nothing is written: the Tempo lane leads and the scene tempo does not change
   the tempo today, so the lane already plays what was heard.
3. **Given** a scene with a time signature, **When** it is launched during the pass, **Then**
   the signature changes as today and nothing is written for it (the project has one time
   signature, no lane).

---

### Edge Cases

- **Record started from stop with count-in**: the pass starts when the count-in ends;
  launches made during the count-in start on the first beat and are written from there.
- **Record pressed while a Session-only clock runs** (clips launched from stop): Record does
  not change what plays — tracks without a Session clip stay silent as today. Clips already
  playing are written from the current transport beat (User Story 1, scenario 7); tracks
  that played no Session clip keep their Arrangement content (FR-005).
- **A scene tempo written to a project with no Tempo lane** creates the lane; from then on
  the lane leads the tempo, and later scene tempos no longer change it (today's rule for an
  automated tempo).
- **Loop on during the pass**: each loop pass replaces what the previous pass wrote in the
  loop range on the tracks it plays (the last pass wins), the same for every track.
- **A track muted, solo-gated, frozen or monitoring**: its Session clip is written as long as
  the slot plays, whether heard or not — mute is a mixer state, not part of the content.
- **Group tracks**: a group slot launch starts its children; each child track gets its own
  written clip. Return and master tracks have no slots.
- **A slot edited while it plays during the pass** (notes, warp, length): the written clip
  holds the slot's content as it was when the pass ended. [Assumption — per-launch
  snapshots are out of scope.]
- **The slot or its track deleted during the pass**: what was written up to the deletion is
  kept if the track still exists; a deleted track writes nothing.
- **An armed instrument track that also plays a Session clip**: the played notes are merged
  into the written clip over the span the slot played; outside that span they form an input
  take as today. Played notes are not looped with the slot's content.
- **An armed audio track**: its input take is recorded as today and its slot writes nothing
  — with monitoring on the slot was silent anyway; with monitoring off the take wins over the
  slot there, as an audio take replaces material today.
- **A very short play** (a launch stopped before one beat passes): the clip is written with
  the exact played length, however short, so the Arrangement matches what was heard.
- **Tracks added during the pass**: a slot launched on a new track is written to it like any
  other.
- **Stop All / Back to Arrangement during the pass**: every playing clip ends at the beat it
  stopped; the pass carries on and later launches are written.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: While the Arrangement is being recorded and the transport runs, the system MUST
  write each Session clip that plays on a track to that track's Arrangement lane, from the
  beat it started to the beat it stopped. This is part of every Arrangement recording; there
  is no switch to turn it off.
- **FR-002**: The written clip MUST sound as the slot sounded: the slot's notes, controllers,
  per-note expression, clip envelopes, audio, warp, pitch, gain and launch velocity, its
  start offset (Legato, Record started mid-play) and its looping over the played time.
- **FR-003**: Launch timing MUST be the time the clip actually started and stopped (after
  launch quantize, follow actions, Repeat, Gate releases), not the time of the click.
- **FR-004**: Over the span a Session clip played on a track, the pass MUST replace that
  track's Arrangement clips; outside those spans the track's Arrangement content MUST stay,
  trimmed at the span edges.
- **FR-005**: Tracks that played no Session clip during the pass MUST keep their Arrangement
  clips unchanged.
- **FR-006**: Armed tracks' input MUST be recorded into the Arrangement as today, alongside
  the written clips; on an armed instrument track that also plays a Session clip, the played
  notes and controllers MUST be merged into the written clip over the played span.
- **FR-007**: Session slot takes recorded during the pass MUST be kept in their slots as
  today and written to the Arrangement like any played slot, from the beat recording
  started.
- **FR-008**: Control moves during the pass MUST write automation as automation recording
  does today, on tracks playing Session clips as on any other track.
- **FR-009**: A scene tempo that takes effect during the pass MUST be written to the master
  Tempo lane at that beat as an instant change, when the project has no Tempo automation
  (with a Tempo lane the scene tempo does not apply today and nothing is written); a scene
  time signature MUST NOT write anything.
- **FR-010**: Everything a pass writes — Session clips, input takes, automation, Tempo
  points, and the replaced material it removed — MUST undo and redo as one step.
- **FR-011**: Discard and retry (⌘.) during a pass MUST remove everything the pass wrote and
  restore the replaced material, with no undo step, then record again as specs/078 does for
  the Arrangement.
- **FR-012**: With loop on, each loop pass MUST replace what the previous loop pass wrote on
  the tracks it plays.
- **FR-013**: A launch cancelled before its boundary MUST write nothing.
- **FR-014**: While the pass runs, the Arrangement MUST show each Session clip heard so far
  where it will be written — the slot's name, colour and notes (or waveform, one piece per
  repetition for audio), growing with the playhead, over
  the clips it replaces. The clips themselves are written when the pass ends (undo and retry are
  unchanged). (Review of #972: "while recording, the Arrangement shows nothing".)
- **FR-015**: Record with nothing armed MUST start a performance-only pass (Session clips
  and automation written, no input recorded, nothing auto-armed) while a Session clip is
  playing or queued; with no Session clip playing or queued it MUST auto-arm the record
  target as today. A performance-only pass shows a status message saying it records the
  Session performance.
- **FR-016**: After the pass, Session playback state MUST be as today (Stop stops the
  Session; Record off alone leaves it playing) — the pass adds no switch of its own.
- **FR-017**: The behaviour MUST be the same whether the pass is driven from the app, Nota
  Remote, a MIDI controller or MCP, since all launch through the same Session commands.
- **FR-018**: A project saved after a pass MUST reopen with the written clips as ordinary
  Arrangement clips; nothing links them back to their slots.

### Key Entities

- **Pass**: one Arrangement recording from Record on to Record off (or transport stop),
  including its count-in, loop passes and retries; the unit of undo and discard.
- **Played span**: on one track, one slot played from a start beat to an end beat with a
  start offset into the slot; one written clip per span.
- **Written clip**: an ordinary Arrangement clip made from a played span: a copy of the
  slot's content as it was at the end of the pass, positioned and trimmed to the span.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For a performance with only deterministic clips (audio slots, or MIDI slots on
  a deterministic instrument), rendering the Arrangement after the pass matches the render
  of the live performance sample-for-sample within the existing render-comparison tolerance
  of the smoke test.
- **SC-002**: A quantized launch or stop is written exactly on its grid beat; an unquantized
  one (launch quantize None) within one audio block of when it was heard.
- **SC-003**: Turning a 16-bar, 4-track Session performance into an Arrangement takes one
  pass of 16 bars and no further editing, instead of one ⌘⇧C copy and one move per clip.
- **SC-004**: One Undo after a pass returns the project to its exact state before the pass
  (every track's Arrangement clips, automation lanes and Tempo lane).
- **SC-005**: Tracks that played no Session clip have byte-identical Arrangement content
  after the pass.

## Assumptions

- Written clips are copies (notes / audio references), not rendered audio; nothing is
  bounced.
- Ableton Live's Arrangement Record is the reference behaviour; where Nota differs (a stopped
  slot returns the track to its Arrangement at once) the rule is "the Arrangement afterwards
  plays what was heard".
- A slot's content is snapshotted once, at the end of the pass; edits to a slot during the
  pass apply to every span of that slot in the pass.
- The time signature stays one project value; writing signature changes would need a
  signature lane and is out of scope.
- No new MCP tool is needed: MCP already starts recording and launches slots and scenes.
- Nota Remote and Nota NX need no change: they launch through the same commands.
- Group track sends, return tracks and the master have no slots and write nothing beyond
  automation.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — launch/stop beats are reported from the audio thread through a lock-free queue; clips are written on the message thread
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
