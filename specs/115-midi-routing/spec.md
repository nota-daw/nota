# Feature Specification: MIDI routing — hardware ports, channel sets and external outputs

**Feature Branch**: `115-midi-routing`

**Created**: 2026-11-25

**Status**: Draft

**Input**: User description: "Issue #1103 (approved): Route instrument-track MIDI between hardware ports, channels and external outputs — one routing model. Inputs: an instrument track keeps All Inputs / single-device shortcuts and can also save an explicit list of input ports, each with its own (possibly non-contiguous) channel set; monitored and recorded input match exactly; removing a port or channel releases only the notes and held controllers it owned (#762, #732). Outputs: a track can send its post-MIDI-effect note and controller stream, from live input and clips, to one external MIDI output and channel instead of a local instrument; missing or ambiguous endpoints stop sending and show as unavailable; a similarly named device never inherits a route; notes released on stop, removal or route change (#690). Output roles: a route can target a named project role (e.g. "Stage Synth") bound to one physical output; rebinding the role once moves every route that uses it while keeping channels and filters (#946). Overview: a read-only MIDI routes view in the Mixer listing every track's inputs, channels, internal MIDI source and external destination, flagging missing endpoints, filterable by source or destination (#847). Out of scope: third-party MIDI plug-ins (#603), MIDI Clock (#687), audio-transient triggers (#844). Depends on #1011 (distinct port identity beyond 16 inputs). Acceptance: the acceptance examples from #690, #946, #762, #732 and #847 all pass under the combined model."

Closes #1103 (supersedes #690, #732, #762, #847, #946) and #1011.

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

**Builds on**: the per-track MIDI input of
[`086-multi-input-recording`](../086-multi-input-recording/) (All inputs or one device, one
channel 0–16), the track-to-track "MIDI In" route and its owned-note release from
[`093-midi-source-held-notes`](../093-midi-source-held-notes/), and the message-thread MIDI
output used for controller feedback in
[`051-controller-auto-mapping`](../051-controller-auto-mapping/).

## Clarifications

### Session 2026-11-25

- Q: How does a track with an external destination fit the rule that an instrument track always has an instrument? → A: A2 — a new built-in instrument **External MIDI** occupies the instrument slot, chosen through **Replace…**; its card holds the output (or role) and channel; the track's audio devices are silent; the previous instrument is replaced (Undo brings it back); deleting it deletes the track as with any instrument.
- Q: Fix #1011 (ports beyond 16 share a source index) inside this feature or separately first? → A: B — as the first task of this feature, in the same branch and pull request; the PR also closes #1011.
- Q: Which MIDI channel does External MIDI send on when events arrive on several channels? → A: C — the card offers a route channel 1–16 (everything is re-channelled to it) or **Keep channels** (each event keeps its own channel, so MPE and multitimbral modules work); new External MIDI instruments default to channel 1.
- Q: What does Nota send when an output that was unplugged while notes sounded reconnects? → A: B — a Note Off for every note the route still owned and a release of held pedals, then All Notes Off (CC 123) and Sustain off (CC 64 = 0) on each channel the route had sent on, before any new event.
- Q: Where and in what form does the MIDI routes overview open? → A: A — a side panel **MIDI routes** next to the Mixer's channels, opened from a button in the Mixer header and from the View menu; the Mixer stays usable while it is open.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Play an external synth from a track (Priority: P1)

A performer launches a Session MIDI clip, or plays a keyboard through one instrument track,
and wants the notes — after the track's MIDI effects, such as Nota's Arpeggiator — to play a
hardware synth on a chosen MIDI output and channel. When they reopen the project the same
output and channel are ready.

**Why this priority**: Nota cannot drive external MIDI gear from a track at all today. It is
the most requested part of #1103 and the base the output roles (Story 4) build on.

**Independent Test**: Route one track to a named port, channel 5; capture that port. Play a
Session clip through the Arpeggiator, then play live; stop, remove and re-route; unplug and
reconnect. Delivers value on its own without any other story.

**Acceptance Scenarios**:

1. **Given** an instrument track with the Arpeggiator and a Session MIDI clip, and the track's
   instrument replaced by **External MIDI** set to output "Port X", channel 5, **When** the clip plays, **Then** the capture
   on Port X shows the arpeggiated notes on channel 5 with a matching Note Off for every
   Note On, in order and in Nota's musical timing.
2. **Given** the same track (its instrument replaced by **External MIDI** set to Port X,
   channel 5), **When** the user plays the armed or monitored track from a
   keyboard, **Then** the capture shows the same post-effect stream from live input.
3. **Given** notes are sounding through the route, **When** the clip stops, the track is
   removed, or the destination is changed, **Then** every note and held controller (sustain,
   sostenuto, soft pedal) the route started is released at the old output, and only those.
4. **Given** the route's output is unplugged while notes sound, **When** the output
   reconnects with the same identity, **Then** Nota first sends Note Offs for the notes the
   route still owned, then All Notes Off and Sustain off on the route's channels, then
   resumes sending; nothing is sent while it is missing.
5. **Given** the saved output is missing and a different device with a similar name is
   connected, **When** the project opens, **Then** the route shows as unavailable, sends
   nothing, and the similar device receives no traffic.
6. **Given** a track routed externally, **When** the project is saved and reopened, **Then**
   the output and channel mode are restored.
7. **Given** External MIDI in **Keep channels** mode and an MPE controller as input, **When**
   the user plays a chord, **Then** the capture shows each note and its per-note pitch bend
   and pressure on its own member channel; switching to channel 5 sends every later event on
   channel 5.

---

### User Story 2 - Combine selected ports and channels on one track (Priority: P1)

A player uses two controllers for one instrument: Port A channel 3 and Port B channel 7 play
and record into the same track, while every other port and channel stays free for other
tracks. A zone player picks a non-contiguous channel set (3, 7, 11) on one port.

**Why this priority**: Today a track takes one device (or All) and one channel (or all), so
split keyboards and multi-controller rigs cannot be set up. It shares the input model with
the overview (Story 3).

**Independent Test**: Configure Port A ch 3 + Port B ch 7 on one track; play from accepted
and rejected routes; record one take; disable Port A while holding notes on both; save and
reopen with Port A missing.

**Acceptance Scenarios**:

1. **Given** a track with inputs Port A {3} and Port B {7}, **When** notes arrive on Port A
   ch 3 and Port B ch 7, **Then** both are monitored and recorded into one take; a note on
   Port A ch 7 or from Port C is ignored by this track.
2. **Given** a track with one port and the channel set {3, 7, 11}, **When** notes arrive on
   channels 3, 4, 7 and 11, **Then** only 3, 7 and 11 are monitored and recorded, and the
   recorded notes are exactly the monitored ones.
3. **Given** notes held on Port A and Port B, **When** the user disables Port A, **Then**
   Port A's note releases while Port B's keeps sounding.
4. **Given** notes held on channels 7 and 11, **When** the user removes channel 7 from the
   set, **Then** the channel-7 note and its held controllers release; channel 11 keeps
   sounding, and notes on other tracks are untouched.
5. **Given** a saved multi-port input where Port A is now missing, **When** the project
   opens, **Then** Port A is shown as missing by its saved identity and Port B still plays.
6. **Given** the existing choices All inputs and single device / single channel, **When**
   the user picks them, **Then** they behave exactly as before this feature.

---

### User Story 3 - See every MIDI route in one place (Priority: P2)

While troubleshooting a live set, the user opens the **MIDI routes** side panel next to the
Mixer's channels and sees
each instrument track's hardware inputs and channels, internal MIDI In source and external
destination, with missing endpoints flagged, without opening each track's menu.

**Why this priority**: Becomes valuable once Stories 1–2 add more routing per track; it is
read-only and has no audio-path risk.

**Independent Test**: Give track A a controller channel, make A the MIDI In of B, give C a
different input and D an external destination; check every path in the view, filter, unplug,
reorder.

**Acceptance Scenarios**:

1. **Given** tracks A (controller ch 2), B (MIDI In from A), C (another input) and D (external
   output), **When** the view opens, **Then** it shows one row per instrument track with its
   inputs and channels, internal source and destination, direction explicit, tracks by their
   current names.
2. **Given** the view, **When** the user filters to source A, **Then** B's inward route from A
   is shown; filtering by a destination shows the tracks that send to it.
3. **Given** the configured controller is disconnected, **When** the view is open, **Then**
   the saved route is still listed and flagged unavailable.
4. **Given** the user reorders or renames tracks, **When** the view is open, **Then** it
   updates to match; selecting a row reveals its track.
5. **Given** playback is running, **When** the user opens, filters or closes the view,
   **Then** playback and routing are unchanged.

---

### User Story 4 - Rebind an output role once (Priority: P3)

Several tracks play one hardware instrument on different channels through a project output
role "Stage Synth". When the MIDI interface is replaced before a show, the user rebinds the
role once and every route keeps its channel.

**Why this priority**: Saves work in larger setups, but needs Story 1 first.

**Independent Test**: Two tracks → Stage Synth on channels 2 and 5; disconnect the original
port; preview and rebind; verify the replacement receives channels 2 and 5; reopen.

**Acceptance Scenarios**:

1. **Given** two tracks routed to role Stage Synth on channels 2 and 5, bound to Port X,
   **When** Port X disconnects, **Then** the role shows as unavailable and a similarly named
   spare port receives no traffic.
2. **Given** transport stopped, **When** the user starts a rebind of Stage Synth, **Then**
   Nota lists exactly the affected routes before confirming.
3. **Given** the user confirms the rebind to Port Y, **Then** notes owned by those routes are
   released at Port X if still reachable, and only later notes go to Port Y, on channels 2
   and 5.
4. **Given** the rebound project is saved and reopened, **Then** both tracks still use the
   role, with no per-track edit.
5. **Given** transport is playing, **When** the user tries to rebind a role, **Then** the
   rebind is not offered until transport stops.

---

### Edge Cases

- **More than the supported port count**: a port beyond the limit must never be read as
  another port (#1011, fixed in this feature); it shows as unavailable for routing instead.
  Verified with 17 distinct inputs: a track listening to input 16 receives nothing from
  input 17.
- **Ambiguous identity**: two connected outputs match a saved identity equally → the route is
  unavailable until the user picks one; Nota never chooses by name similarity.
- **Overlapping inputs**: the same note number arrives from two accepted port/channel pairs on
  one track → the instrument plays one note (a pitch sounds once), owned by both sources; it
  ends when the last owner releases it or is removed.
- **Same channel number, different ports**: channel 7 on Port A and channel 7 on Port B are
  independent choices.
- **Empty channel set** on a port → treated as that port disabled for the track; the UI does
  not allow saving a port entry with no channel.
- **Coinciding events** from different sources at the same position → merged in a consistent,
  documented order (port order in the track's list, then channel).
- **Track with both MIDI In from another track and hardware inputs** → both feed the track as
  today; the overview shows both.
- **External destination and recording**: recording on an externally routed track still
  records the incoming MIDI into a clip, exactly as with a local instrument.
- **Muted or soloed-out track** with an external destination → sends nothing new and releases
  its owned notes, like a muted instrument stops sounding.
- **Feedback loop**: an output that is the same physical device as one of the track's inputs
  is allowed (hardware echo is the user's setup), but Nota never routes its own output back
  internally.
- **Project opened on another machine** with none of the saved ports → every hardware route
  shows as missing, nothing is sent, and the project otherwise works.
- **Undo** of a routing change behaves like the change itself: owned notes are released.
- **MPE**: with MPE on, a custom channel set limits which member channels are accepted; an
  External MIDI instrument re-channels to its route channel, or keeps member channels in
  **Keep channels** mode.
- **Two routes share an output and channel** (e.g. two tracks on role Stage Synth, channel 2)
  and the output reconnects → both routes come back together: each sends its own Note Offs,
  then one All Notes Off / Sustain off per channel. The reset is skipped only for a channel
  that a route which stayed available (e.g. one bound by a different role to the same port)
  is holding notes on.
- **Channel mode change while notes sound** → treated as a routing change: owned notes are
  released on the channels they were sent on, then new events use the new mode.

## Requirements *(mandatory)*

### Functional Requirements

**Inputs (#762, #732)**

- **FR-001**: An instrument track's MIDI input MUST keep the existing choices — All inputs,
  and one device with all channels or one channel — with unchanged behaviour.
- **FR-002**: An instrument track MUST be able to save an explicit list of input ports, each
  with its own set of channels (any subset of 1–16, contiguous or not).
- **FR-003**: Only events from a listed port on one of that port's channels MUST reach the
  track; a channel number on one port MUST be evaluated independently of the same number on
  another port.
- **FR-004**: What the track monitors and what it records MUST be the same events.
- **FR-005**: Notes and held controllers MUST be owned by the port and channel they came
  from; removing a port or channel, or losing a port, MUST release only what that source
  owned on that track.
- **FR-006**: A missing port in a saved list MUST be shown by its saved identity and MUST
  NOT be satisfied by a different device with a similar name.

**External destination (#690)**

- **FR-007**: Nota MUST offer a built-in instrument **External MIDI**, chosen like any
  instrument (new track, **Replace…**), whose card selects one external destination: a MIDI
  output (or an output role, FR-013) and a channel mode: one channel 1–16 (default 1), to which
  every event is re-channelled, or **Keep channels**, which gives each sounding note its own
  channel MPE-style (member channels 2–16, instrument-wide controllers on channel 1) so
  per-note expression survives. (Plan finding: after the MIDI-effect chain and in clips Nota
  keeps no per-note channel, so sending each note on the channel it was *played* on — for a
  multitimbral module — needs #744 first and is a follow-up.) It produces no audio; the track's
  audio devices, fader and sends stay in place but receive silence. Replacing or deleting it
  follows the existing instrument rules (replace keeps clips and effects; deleting it deletes
  the track; Undo restores the previous instrument).
- **FR-008**: The track MUST send its note and controller stream after its MIDI effects, from
  both clips (Arrangement and Session) and live input, keeping event order and Nota's musical
  timing.
- **FR-009**: The destination picker on the External MIDI card MUST show the selected destination and the available
  outputs; an unavailable or ambiguous destination MUST be shown as such and MUST receive and
  forward nothing.
- **FR-010**: On clip stop, transport stop, track removal, mute, or a deliberate routing change
  (including undo), Nota MUST release the notes and held controllers the route owns at the
  old output while it is reachable, leaving other routes' notes alone.
- **FR-011**: If an output disappears while the route owns notes, Nota MUST, on reconnection
  of the same identity and before any new event, send a Note Off for each owned note, release
  its held pedals, then send All Notes Off (CC 123) and Sustain off (CC 64 = 0) on each channel
  the route had sent on — skipping the CC 123 / CC 64 reset on any channel that another
  available route currently holds notes on at that output, so its notes are left alone.
- **FR-012**: The destination and channel MUST be saved with the project and restored on open.

**Output roles (#946)**

- **FR-013**: The user MUST be able to create named project output roles, each bound to one
  identified physical output, and target a role from a track's destination instead of a port.
- **FR-014**: Nota MUST show both the role name and its bound output, and mark a role as
  unavailable or ambiguous when its output is missing or not uniquely identified.
- **FR-015**: With transport stopped, the user MUST be able to rebind a role: Nota previews
  the affected routes, then applies one confirmed rebind that keeps every route's channel.
- **FR-016**: On rebind, Nota MUST release owned notes at the previous output if reachable and
  send only later events to the new one.
- **FR-017**: Roles and the routes referring to them MUST be saved with the project.

**Overview (#847)**

- **FR-018**: The Mixer MUST offer a read-only **MIDI routes** side panel beside its
  channels, opened and closed from a button in the Mixer header and from the View menu, with
  the Mixer's channels still usable while it is open; it shows one row per
  instrument track: its hardware inputs and channels (or All inputs), its internal MIDI In
  source, and its external destination (port or role and channel).
- **FR-019**: The view MUST mark missing endpoints and removed source tracks, keep route
  direction explicit, name tracks by their current names, and stay in sync with edits.
- **FR-020**: The user MUST be able to filter the view by source or by destination and select
  a row to reveal its track.
- **FR-021**: Opening, filtering or closing the view MUST NOT change playback or routing.

**Cross-cutting**

- **FR-022**: Every routing change MUST be undoable as one step.
- **FR-023**: The routing settings MUST be readable and writable over MCP, extending the
  existing `get/set_track_midi_input` tools and adding the destination and role operations.
- **FR-024**: Projects saved before this feature MUST open with their existing MIDI input
  unchanged; projects using the new routing MUST record a format version so an older Nota
  reports them rather than silently dropping routes.

### Key Entities

- **Input route entry**: one hardware port (by saved identity) with the set of channels the
  track accepts from it. A track has either "All inputs" or a list of these.
- **Port identity**: what Nota saves to recognise a MIDI port across sessions (uid and
  identifying details), never just a display name.
- **External MIDI instrument**: the built-in instrument that holds a track's external
  destination — a port identity or an output role, plus a channel mode (one channel 1–16, or Keep
  channels) — and saves it like any
  instrument's settings.
- **Output role**: a named project-level destination (e.g. "Stage Synth") bound to one port
  identity; referenced by destinations.
- **Owned note state**: the notes and held controllers a given source (input entry, MIDI In
  track, or external route) has started and not yet ended; the unit of release.
- **MIDI route row**: the overview's read-only view of one instrument track's inputs,
  internal source and destination with their availability.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The acceptance examples of #690, #732, #762, #847 and #946 all pass end-to-end
  under the combined model.
- **SC-002**: In a captured output, 100% of Note Ons sent through an external route have a
  matching Note Off, across clip stop, transport stop, track removal, re-route, mute, undo and
  unplug/reconnect.
- **SC-003**: Zero events reach a track from a port or channel not in its input list, and zero
  events reach an output other than the one its route or role is bound to — including a
  similarly named device.
- **SC-004**: Notes recorded on a track equal the notes it monitored, event for event, for
  every input configuration in Story 2.
- **SC-005**: External-route timing stays within the jitter of the platform's MIDI output for
  live input; adding an external route causes no audio dropouts in the smoke-test project.
- **SC-006**: A user can rebind an output role used by several tracks with one confirmation,
  and reach the state "every track routed to the replacement on its own channel" in under
  30 seconds.
- **SC-007**: A user can answer "which tracks receive from this controller, and where does
  track X send?" from the MIDI routes view alone, without opening any track menu.
- **SC-008**: Every project from before this feature opens with identical MIDI input routing.

## Assumptions

- **One destination per track (first version)**: the External MIDI instrument takes the
  instrument slot, as #690 proposes for its first version; playing an external synth and a
  local instrument from one track at once is done today by layering through MIDI In, and a
  combined device is a follow-up.
- **Channel mode**: with a route channel, MPE per-note channels collapse onto it (fine for an
  ordinary synth); users who drive an MPE synth choose **Keep channels**. Driving the parts of
  a multitimbral module from one track by played channel waits for #744; today one External
  MIDI track per part (each on its own route channel) does the job.
- **MPE on input**: with MPE on, the per-port channel set filters member channels the same
  way; documenting the interaction is part of the user docs.
- **Output timing**: events go out at their musical position in the buffer they belong to;
  Nota applies the track's existing MIDI offset (ms) to external routes; hardware latency
  compensation beyond that is out of scope.
- **Port limit**: #1011 (distinct identity beyond the supported number of inputs) is fixed
  as the first task of this feature, in the same pull request (which also closes #1011);
  every later story relies on every routed port having a distinct identity.
- **Partial controller messages** (#1019) stay with the input that sent them; the input route
  model must not reintroduce the cross-port mixing that bug describes.
- **Role rebind only while stopped**, as #946 asks; editing a single track's destination is
  allowed while playing.
- **Overview**: a side panel of the Mixer; read-only in this version (editing routes from it
  is a follow-up).
- **Out of scope**: third-party MIDI plug-ins (#603), MIDI Clock output (#687), audio-
  transient triggers (#844), sending to several outputs from one track, SysEx/program-change
  scheduling to external gear.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — external MIDI output today runs on the message thread only, so the plan must define a lock-free hand-off from the audio thread
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
