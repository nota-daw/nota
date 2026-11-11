# Feature Specification: Private Cue Mix on a Separate Hardware Output Pair

**Feature Branch**: `feat/483-cue-output`

**Created**: 2026-11-10

**Status**: Draft

**Input**: User description: "Issue #483 (approved): Route a private cue mix to a separate hardware output pair. On a multi-output interface, assign one physical stereo pair to Main and another to a private Cue mix (e.g. Main → outs 1/2, Cue → outs 3/4). First scope: cue one selected track or clip, show clearly which signals feed each destination, independent Cue level that never changes the audience mix or the recorded input. Define the cue tap point, whether effects are heard in cue, and expected latency of Cue relative to Main. Save/reopen keeps identifiable routes and levels. If the cue outputs disappear, show them unavailable or ask for deliberate reassignment — never silently fold the private cue into Main. Additionally (from the maintainer): the metronome must be routable to the Cue output only, so the performer hears the click in Cue without it going to Main/audience. Closes #483."

Issue: [nota-daw/nota#483](https://github.com/nota-daw/nota/issues/483) (approved).

## Clarifications

### Session 2026-11-10

- Q: Is a cued track a copy (stays in Main, PFL) or exclusive (leaves Main, heard only
  by the performer)? → A: Copy (PFL). Cue never changes Main; a private guide track is
  made by muting it in Main, which the pre-fader, pre-mute tap still hears in Cue.
- Q: How many tracks can be in Cue at once? → A: Several; each is toggled on its own and
  Cue carries their sum at one shared Cue level (no per-track Cue levels).
- Q: Where are the click destination and click level saved? → A: On this machine, per
  audio device, next to the Main/Cue pair assignment; the project keeps only metronome
  on/off (as today).
- Q: Which Cue controls can MIDI Learn map? → A: Each track's Cue toggle and the Cue
  level, like track mute/solo, saved with the project's mappings; the click destination
  stays a rig setting (not mappable); Nota Remote buttons are a follow-up.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Today's state, for the plan:

- **Outputs**: every backend opens the device as one stereo pair. CoreAudio and miniaudio
  always use outputs 1/2. ASIO alone lets the user pick one output pair (spec 049), and
  that pair carries everything. There is no output channel count or channel names in the
  C ABI. Spec 086 did the input-side equivalent (every input channel, names, a per-device
  Audio I/O page) and listed cue mixes (#483) and multi-channel outputs as out of scope.
- **Mix**: tracks → groups → returns → master → metronome/count-in clicks → file/preset
  preview voice → master devices → master volume → clamp → device buffer. There is no
  cue, PFL/AFL or headphone bus. Solo is plain solo-gating.
- **Metronome**: a fixed-gain sine click added *before* the master devices and master
  fader, so master FX and the master fader change it today. On/off is saved per project;
  count-in bars is an app setting; count-in clicks play even with the metronome off.
- **Device events**: only ASIO reports device resets/failures; CoreAudio and miniaudio
  report none, and nothing handles a change in output channel count.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md),
[`specs/086-multi-input-recording`](../086-multi-input-recording/spec.md) (the input-side
model this feature mirrors), [`specs/049-asio-backend`](../049-asio-backend/spec.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Send Main and Cue to different output pairs (Priority: P1)

A performer has a four-output interface. Outputs 1/2 go to the PA, outputs 3/4 go to
their in-ear monitors. In the audio settings they pick "Main → 1/2" and "Cue → 3/4".
Everything the audience hears keeps coming out of 1/2; outputs 3/4 carry only what the
performer puts in Cue.

**Why this priority**: Without a second physical destination there is no private mix at
all — every other story depends on it.

**Independent Test**: With a multi-output device (or a virtual multi-channel device),
assign Main to 1/2 and Cue to 3/4, play the project, and measure: 1/2 carries the full
mix, 3/4 is silent until something is put in Cue.

**Acceptance Scenarios**:

1. **Given** a device with four or more outputs, **When** the user opens the audio
   settings, **Then** they see the device's output pairs by number and, where the driver
   provides them, by name, and can choose one pair for Main and one for Cue.
2. **Given** Main on 1/2 and Cue on 3/4, **When** the project plays, **Then** the
   audience mix appears only on 1/2 and nothing from Main leaks onto 3/4.
3. **Given** a stereo-only device, **When** the user opens the audio settings, **Then**
   Main is 1/2, Cue is shown as "No cue output" and can't be set to the Main pair, and the
   app behaves exactly as today.
4. **Given** the user tries to pick the same pair for Main and Cue, **Then** the choice is
   refused with a plain explanation (a private cue can never share the audience pair).
5. **Given** Main is moved to 3/4 (say the PA is wired there), **When** the project
   plays, **Then** the audience mix comes out of 3/4 and 1/2 is silent.

---

### User Story 2 - Put one track in Cue with its own level (Priority: P1)

During a set the performer wants to hear a guide vocal track (or the vocal they're
recording) in their in-ears. They press **Cue** on that track. It now plays in the Cue
output at the Cue level. Turning the Cue level up or down never changes what the
audience hears, and never changes what is recorded.

**Why this priority**: This is the core of the issue — a private, independently levelled
listen.

**Independent Test**: Put a track in Cue; sweep the Cue level from silent to full while
recording a mic and printing the Main output. Main's level and the recorded take are
bit-identical to a run with no Cue.

**Acceptance Scenarios**:

1. **Given** Cue is assigned, **When** the user turns Cue on for a track, **Then** that
   track plays in the Cue output and the track's Cue state is visible in the mixer strip
   and in the track header.
2. **Given** one track is in Cue, **When** the user turns Cue on for another track,
   **Then** both are in Cue and the Cue output carries their sum; turning Cue off on one
   leaves the other in Cue.
3. **Given** a track is in Cue, **When** the user moves the Cue level, **Then** the Cue
   output follows and the Main output and any recording are unchanged.
4. **Given** a track is in Cue, **When** the user moves that track's fader, pan, mute or
   solo, **Then** the Cue output is unchanged (Cue is taken before the fader — see
   FR-007).
5. **Given** a track is in Cue, **Then** what it sounds like in Cue includes the track's
   own device chain (instrument, effects) and excludes sends, returns, group processing,
   master devices and the master fader.
6. **Given** the cued track is in Session view, **When** the user launches one of its
   clips, **Then** that clip is heard in Cue (cueing a clip = cueing the track it plays
   on).
7. **Given** a track is in Cue, **Then** it keeps playing in Main exactly as before —
   Cue is a copy (pre-fader listen), not a move. Keeping a signal out of the audience
   mix is done with the track's own mute or fader, which leave Cue untouched.

---

### User Story 3 - Click only in the performer's ears (Priority: P1)

The performer wants the metronome and the count-in in their in-ears only. In the
metronome options they set the click destination to **Cue**. The click is then heard
only on the Cue pair — the audience never hears it, and it is never recorded or bounced.

**Why this priority**: Requested by the maintainer as part of this issue; a click leaking
into the PA is the most common live-set failure this feature prevents.

**Independent Test**: Set click → Cue, play with the metronome on and a count-in. Print
Main and Cue: the click is on Cue only; Main contains no click energy.

**Acceptance Scenarios**:

1. **Given** Cue is assigned, **When** the user sets the click destination, **Then**
   they can choose **Main** (today's behaviour), **Cue**, or **Both** (Main and Cue).
2. **Given** click → Cue, **When** the metronome plays or a count-in runs, **Then** the
   click appears on the Cue output only.
3. **Given** click → Cue, **Then** the click is not affected by master devices, the
   master fader or anything else on the Main path, and has its own click level.
4. **Given** click → Cue, **When** the user moves the Cue level, **Then** the click
   follows the Cue level together with the cued track.
5. **Given** click → Cue and no track in Cue, **Then** the Cue output carries the click
   alone.
6. **Given** no Cue output is assigned, **Then** the destination choice shows only
   **Main** and the click plays on Main.
7. **Given** click → Cue is set for the stage interface, **When** the user switches to
   the laptop's built-in output (a different device with no Cue), **Then** the click
   follows that device's own setting (Main by default); switching back restores click →
   Cue. Any project opened on that machine uses the same click routing.

---

### User Story 4 - Cue output disappears (Priority: P2)

Mid-rehearsal the performer unplugs the interface and the laptop falls back to its
built-in stereo output, or opens the project on another machine with only two outputs.
The private cue must not suddenly play through the speakers.

**Why this priority**: The safety promise of the issue — a private signal never reaches
the audience by accident.

**Independent Test**: Assign Cue 3/4 with a cued track and click → Cue; switch to a
stereo device. Main carries the cued track only at its own fader level (as before) and
no click; the UI
shows Cue unavailable; restoring the device restores Cue without user action.

**Acceptance Scenarios**:

1. **Given** Cue → 3/4, **When** the current device no longer has outputs 3/4,
   **Then** Cue is shown as unavailable (in Settings → Audio → Outputs and on the mixer's
   Cue strip, which also shows where the click goes), and nothing that was routed to Cue
   is moved to Main.
2. **Given** Cue is unavailable, **When** the user deliberately picks another pair (or
   picks Main as the click destination), **Then** the new route applies.
3. **Given** Cue was unavailable, **When** a device with the assigned pair returns,
   **Then** Cue resumes on that pair.
4. **Given** Main's assigned pair disappears, **Then** Main falls back to 1/2 (the
   audience must keep hearing the show) and the UI says so; Cue is never chosen as the
   fallback for Main if that would put Main onto the performer's ears without notice.

---

### User Story 5 - See what feeds each destination (Priority: P2)

Before going on stage the performer checks, at a glance, what goes where: which pair is
Main, which pair is Cue, which tracks are in Cue, where the click goes, and the Cue level.

**Why this priority**: The issue asks to "show clearly which signals feed each
destination"; confidence before a show matters as much as the routing itself.

**Independent Test**: With a route set up, a new user can answer "what will the
audience hear and what will I hear?" from the mixer alone in under 30 seconds.

**Acceptance Scenarios**:

1. **Given** Cue is assigned, **Then** the mixer shows a Cue section next to Master with
   the Cue output pair, the Cue level, a Cue meter, the cued tracks' names (or their
   count when they don't fit) and the click destination.
2. **Given** a track is in Cue, **Then** its Cue button is lit in its mixer strip and
   track header.
3. **Given** Main and Cue are on non-default pairs, **Then** the Master and Cue sections
   both show their output pair labels (e.g. "Out 3/4 · Phones").

---

### User Story 6 - Save and reopen (Priority: P2)

The performer saves the set and opens it the next day. Which tracks are in Cue and the
Cue level are back as they were; the output pairs and the click destination and level
are back on the same interface, for every project.

**Independent Test**: Set up Cue, save, close, reopen; compare every value.

**Acceptance Scenarios**:

1. **Given** a saved project with cued tracks and a Cue level, **When** it is reopened,
   **Then** both are restored.
2. **Given** the Main/Cue pair assignment, click destination and click level for an
   interface, **When** the app restarts with the same interface, **Then** they are all
   restored, whichever project is open.
3. **Given** a project from a build without this feature, **When** it is opened,
   **Then** nothing is in Cue, the click goes to Main, and it sounds exactly as before.
4. **Given** a project saved with this feature, **When** it is opened in an older build,
   **Then** it opens and plays (the new fields are ignored).

---

### Edge Cases

- A cued track is deleted or turned into a group → it leaves Cue; the other cued tracks
  stay; no error. A frozen cued track stays in Cue and is heard as its frozen audio.
- A group or return track is cued → allowed; Cue carries that bus's own device-chain
  output before its fader (same tap as a track).
- The cued track is muted, or another track is soloed → Cue is unaffected (pre-fader,
  pre-mute, pre-solo tap); only Main follows mute/solo.
- The cued track has input monitoring on → the monitored input is heard in Cue (that's
  the "hear my vocal while recording it" case).
- Recording from **Master** (resampling) → captures Main only, never Cue or a Cue-only
  click.
- Export / bounce → renders Main only; Cue and a Cue-only click never appear in a file.
  Export of stems likewise ignores Cue.
- File browser / preset preview voice → stays on Main as today (out of scope to move).
- Stopped transport → a cued track with input monitoring or a live instrument still
  sounds in Cue; the click stays silent as today.
- The device has an odd number of outputs (e.g. 5) → the last output is offered as a mono
  pick ("Out 5") that plays the stereo sum on one channel, as ASIO does today.
- The device's sample rate or buffer changes → routes stay; Cue and Main remain aligned.
- Main and Cue are on two different devices → not supported in this scope (one device;
  see Assumptions).
- Very high output channel counts (e.g. 64-channel interface) → all pairs listed; only
  the two assigned pairs carry signal, the rest are written silent.

## Requirements *(mandatory)*

### Functional Requirements

**Outputs**

- **FR-001**: The engine MUST open as many output channels of the selected device as
  needed to reach the assigned Main and Cue pairs, on every backend (CoreAudio, WASAPI /
  PulseAudio / ALSA through the existing backend, ASIO), and MUST write silence to every
  other open output.
- **FR-002**: The app MUST expose the selected device's output channel count and, where
  the driver provides them, output channel names.
- **FR-003**: Users MUST be able to assign Main to any output pair of the device (default
  1/2) and Cue to any other output pair or to "No cue output" (default).
- **FR-004**: Main and Cue MUST NOT be assigned to the same or overlapping channels.
- **FR-005**: The existing ASIO "Output pair" setting MUST become the Main pair, with the
  user's current choice migrated unchanged.

**Cue source and tap**

- **FR-006**: Users MUST be able to toggle Cue on any number of tracks (audio,
  instrument, group or return) independently; the Cue output MUST be the sum of all
  cued tracks' Cue taps, scaled by the one Cue level. A group and one of its member
  tracks may both be cued; each contributes its own tap (no de-duplication).
- **FR-007**: The Cue tap MUST be after the track's own device chain and before its
  fader, pan, mute and solo gating (pre-fader listen). Sends, returns, groups, master
  devices and the master fader MUST NOT shape the Cue signal.
- **FR-008**: Putting a track in Cue MUST NOT change its contribution to Main (Cue is a
  copy). A track muted or faded down in Main MUST still be heard in Cue.
- **FR-009**: Users MUST be able to set a Cue level (from silent to the mixer faders' top,
  +3.5 dB; default 0 dB)
  that scales the whole Cue output and MUST NOT affect Main, any recording, export, or
  any meter other than the Cue meter.
- **FR-010**: Main and Cue MUST be rendered in the same audio block so that a signal
  present in both (e.g. a cued track in copy mode, or click → Main + Cue) leaves both
  output pairs on the same sample, plus any latency the master device chain itself
  reports for Main (Cue does not pass through it). No extra buffering MAY be added to
  Cue.

**Click**

- **FR-011**: Users MUST be able to set the click destination (metronome and count-in
  together) to Main, Cue, or Main + Cue. Default: Main.
- **FR-012**: A click routed to Cue MUST bypass the Main path (master devices, master
  fader) and MUST be scaled by the Cue level; a click routed to Main MUST behave exactly
  as today.
- **FR-013**: Users MUST be able to set a click level (off, −18 … +6 dB in steps, default
  0 dB = today's loudness), applied to the click on every destination.

**Safety**

- **FR-014**: When the assigned Cue pair does not exist on the current device, everything
  routed to Cue (the cued track's Cue copy, a Cue-only click) MUST go silent and be shown
  as unavailable; it MUST NOT be moved to Main (a cued track that is muted in Main stays
  muted in Main).
- **FR-015**: When the assigned Main pair does not exist, Main MUST fall back to outputs
  1/2 and the UI MUST say so; the saved assignment MUST be kept so it resumes when the
  device returns.
- **FR-016**: Recording (from hardware inputs, from a track, or from Master), export and
  bounce MUST never contain the Cue signal or a Cue-only click.

**Visibility and persistence**

- **FR-017**: The mixer MUST show a Cue section next to Master with the Cue output pair
  label, Cue level, a Cue meter, the names (or count) of the cued tracks and the click destination; tracks
  MUST show a Cue toggle in their mixer strip and track header.
- **FR-018**: The Main/Cue pair assignment, the click destination and the click level
  MUST be saved per machine and per device (like the input setups of spec 086), so a
  project moved to another machine never routes to an output that doesn't exist there.
  A device with no saved setting uses Main 1/2, no Cue, click → Main at 0 dB.
- **FR-019**: The project MUST save which tracks are in Cue and the Cue level. Older
  projects open with nothing in Cue; older builds MUST still open new projects.
- **FR-020**: All Cue, click and output-pair changes from the UI MUST reach the audio
  thread without allocation, locks or I/O there (AR-4/AR-6).
- **FR-021**: MIDI Learn MUST offer each track's Cue toggle and the Cue level as targets,
  behaving like track mute (toggle) and volume (continuous), saved with the project's
  other mappings. The click destination is not a MIDI Learn target.

### Key Entities

- **Output assignment**: per device on this machine — the Main pair (first channel,
  width) and the Cue pair (first channel, width, or none).
- **Cue state** (per project): the set of cued tracks (possibly empty) and the Cue level.
- **Click routing** (per machine, per device, stored with the output assignment):
  destination (Main / Cue / Main + Cue) and click level.
- **Output availability**: whether each assigned pair exists on the current device —
  derived, never saved.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With Main on 1/2 and Cue on 3/4, sweeping the Cue level from silent to full
  changes the Main output and a simultaneous recording by exactly zero samples (null test).
- **SC-002**: With click → Cue, the Main output of a 16-bar run with metronome and
  count-in contains no click (null test against the same run with the metronome off).
- **SC-003**: A signal sent to both Main and Cue arrives on both output pairs on the same
  sample when the master chain adds no latency.
- **SC-004**: After switching from a 4-output device to a stereo device, Main is
  bit-identical to a run with no Cue at all: no Cue-only click, and a cued track muted
  in Main stays silent in Main.
- **SC-005**: A user sets up Main 1/2, Cue 3/4, a cued track and click → Cue from scratch
  in under 1 minute, and can state what each pair carries from the mixer alone.
- **SC-006**: Save → reopen restores 100% of the Cue and click settings; projects from
  the previous release render identically (null test).
- **SC-007**: Audio-thread cost of Cue on a 64-track project stays within 2% of the
  same project without Cue, with no new xruns at a 64-frame buffer.

## Assumptions

- Main and Cue live on the **same** audio device (one interface); routing across two
  devices (aggregate devices aside) is out of scope.
- Several tracks can be in Cue at once (summed); per-track cue levels, a cue-to-main
  blend knob, AFL, and several cue mixes (one per band member) are out of scope for this
  first step and can follow from this model.
- Nota Remote controls for Cue and MCP tools beyond what the mixer already exposes are a
  follow-up, not this scope.
- Per-track output to arbitrary hardware outputs (multitrack outs, stems to a desk) is
  out of scope — only Main and Cue are destinations.
- The file browser / preset preview voice stays on Main.
- Count-in and metronome share one destination and one level (they are the same click).
- Output pair names follow the same rules as input names in spec 086 (driver names where
  available, user-editable names are a possible follow-up, not this scope).
- The issue's "cue a clip" is satisfied by cueing the track the clip plays on.
- The Cue level follows the mixer faders (silent to +3.5 dB); the click level is a list of
  dB steps in Settings (off, −18 … +6 dB). The engine accepts up to +6 dB for both.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
