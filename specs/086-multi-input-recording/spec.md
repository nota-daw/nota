# Feature Specification: Multi-Channel Input and Simultaneous Multi-Track Recording

**Feature Branch**: `feat/multi-input-recording`

**Created**: 2026-11-08

**Status**: Draft

**Input**: User description: "Multi-channel audio input and simultaneous multi-track audio recording (GitHub issues #353 and #528). Today the audio input layer opens only the first two device channels (Ext 1 / Ext 2 / Ext 1/2), and a take records only the first armed audio track (plus the first armed instrument track); other armed audio tracks silently record nothing, which is also why linked group tracks (#528) don't all record. Goal: every hardware input channel of the selected device is selectable per audio track (mono channel N or stereo pair N/N+1, with device channel names), and every armed audio track records its own input at once in one pass — in Arrangement and in Session (two sources into two slots at the same boundary, stopping one slot must not affect the other; acceptance case from ultralooper in #353). Takes, retry, undo, latency compensation and saved projects must handle several simultaneous audio takes; old projects' Ext 1/Ext 2/Ext 1/2 picks migrate. Decide in clarify whether an Audio I/O setup page (enable/name inputs, as in #353 option 2) is in scope or only the per-track selector (option 1)."

Issues: [nota-daw/nota#353](https://github.com/nota-daw/nota/issues/353) (approved),
[nota-daw/nota#528](https://github.com/nota-daw/nota/issues/528) (approved — a symptom of the
same limit: linked tracks arm together, but only the first armed audio track records).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Today's state, for the plan: the capture layer delivers only a stereo pair (the device's
first two inputs) on every backend (CoreAudio, miniaudio, ASIO); a track's input is one of
three values (Ext 1, Ext 2, Ext 1/2); a take starts one audio capture (the first armed audio
track) and one MIDI capture (the first armed instrument track); a Session audio take records
into one slot. Monitoring already reads the shared input per track.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-11-08

- Q: How does the user arm several audio tracks, given that Arm is exclusive today (#332)? → A: Keep it: a click arms exclusively, ⌘/Ctrl-click adds a track; the Arm tooltip and the user docs name the modifier.
- Q: Is a mono input recorded to a mono file or, as today, to a stereo file with two equal channels? → A: A mono file; the clip plays centred as today and pans normally.
- Q: Do several armed instrument tracks also record MIDI in the same pass, or is that out of scope? → A: In scope: every armed instrument track records its MIDI in the same pass (so #528 is fixed for instrument groups too).
- Q: Where does each armed instrument track take its MIDI from when several are armed? → A: A per-track MIDI input: All inputs (default), or one MIDI device, plus a channel (All or 1–16); saved with the project.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Pick any hardware input for a track (Priority: P1)

A musician with a four-input interface opens an audio track's input menu and sees every
input of the selected input device: each channel as a mono input (named as the device names
it, e.g. "Mic 3", or "Input 3" when the device gives no name) and the stereo pairs. They put
a microphone on input 3 and pick it; the track meters, monitors and records input 3 only.

**Why this priority**: Without it, inputs 3 and above cannot be used at all — the base the
rest builds on. Alone it already helps (one source on any input, one pass at a time).

**Independent Test**: With a 4+ input device (or a virtual multi-channel device), pick
input 3 on a track, play a signal into input 3 only, record; the clip holds that signal and
nothing from inputs 1–2.

**Acceptance Scenarios**:

1. **Given** an input device with 8 channels, **When** the user opens an audio track's input
   menu, **Then** it lists 8 mono inputs and the 4 stereo pairs (1/2, 3/4, 5/6, 7/8), with the
   device's channel names where it reports them.
2. **Given** a track set to mono input 3, **When** the user records, **Then** the clip is a mono
   recording of input 3 that plays centred (as Ext 1 sounds today), and the track's meter showed input 3 while armed.
3. **Given** a track set to stereo pair 3/4, **When** the user records, **Then** the clip is
   stereo with input 3 left and input 4 right.
4. **Given** the user picks an input, **When** they look at the track header, the mixer strip
   and the Session I/O row, **Then** each shows the same short input label.

---

### User Story 2 - Record several tracks in one pass (Priority: P1)

A band records a drum kit and a bass at once: four audio tracks, each on its own input, all
armed. One press of Record starts every armed track; Stop ends them all. Each track gets its
own clip of its own input, all starting at the same beat and sample-aligned.

**Why this priority**: The heart of #353 and the fix for #528. It is what users tried first
and found missing.

**Independent Test**: Arm two audio tracks on different inputs, record 4 bars, stop; both
tracks hold a clip of the same length and start, each with only its own input. Undo removes
both in one step.

**Acceptance Scenarios**:

1. **Given** track A armed, **When** the user ⌘/Ctrl-clicks Arm on track B, **Then** both are
   armed; a plain click on Arm of track C leaves only C armed.
2. **Given** audio tracks A (input 1) and B (input 2) both armed, **When** the user records
   and stops, **Then** A and B each have a new clip of its own input, both starting at the
   record-start beat and of equal length.
3. **Given** two tracks in a linked group (#528), **When** the user arms one (so both arm) and
   records, **Then** both tracks record.
4. **Given** two armed tracks on the *same* input, **When** the user records, **Then** both get
   a clip of that input (doubling a source onto two tracks is allowed).
5. **Given** armed audio tracks and two armed instrument tracks, **When** the user records,
   **Then** every armed audio track records its input and each instrument track gets its own
   MIDI clip of what it received, all starting at the record-start beat.
6. **Given** instrument track P on "KeyLab, All channels" and track Q on "Launchkey, channel
   1", both armed, **When** two players play and the user records, **Then** P's clip holds
   only the KeyLab's notes and Q's clip only the Launchkey's channel-1 notes.
7. **Given** a multi-track take with loop recording on, **When** the loop wraps, **Then** each
   track keeps its own take lanes, as one track does today.
8. **Given** a finished multi-track take, **When** the user presses Undo once, **Then** every
   clip of that take goes; Redo brings them all back.
9. **Given** a multi-track take in progress, **When** the user uses Discard and Retry
   (specs/078), **Then** every track's partial take is dropped and all armed tracks record
   again.
10. **Given** a multi-track take, **When** the user saves and reopens the project, **Then** every
   clip plays back at the same place and in sync with the others.

---

### User Story 3 - Record several Session slots at once (Priority: P2)

A vocalist and a cellist each have an audio track with its own input in Session view. They
arm both and launch an empty slot on each to start at the same bar; one four-bar take lands
in both slots. Stopping the cello's slot early leaves the vocal take untouched and still
recording.

**Why this priority**: The acceptance case from #353 (comment by ultralooper); Session is the
live-looping side of Nota, but Arrangement covers the common studio need first.

**Independent Test**: Two Session tracks on different inputs, record into one slot on each
from the same launch boundary, stop one slot, then the other; save, reopen, both clips are
there, aligned, each with its own source.

**Acceptance Scenarios**:

1. **Given** two armed Session tracks on different inputs, **When** the user launches empty
   slots on both for the same boundary, **Then** both record from that boundary.
2. **Given** both slots recording, **When** the user stops one slot, **Then** that slot keeps its
   take and the other keeps recording, unchanged.
3. **Given** both takes finished, **When** the user saves and reopens, **Then** both slots hold
   their clips, with distinct sources and aligned starts.

---

### User Story 4 - Old projects and missing inputs (Priority: P2)

A user opens a project saved with Ext 1 / Ext 2 / Ext 1/2, or moves to a device with fewer
inputs than the project uses.

**Why this priority**: Nothing that works today may break; projects travel between machines.

**Independent Test**: Open a project saved by the current release with each of the three old
picks; the tracks show input 1, input 2 and pair 1/2. Open a project that uses input 6 on a
2-input device; the track shows its input as unavailable and records nothing from it.

**Acceptance Scenarios**:

1. **Given** a project saved with Ext 1, Ext 2 or Ext 1/2, **When** it opens, **Then** the tracks
   use mono input 1, mono input 2 or pair 1/2.
2. **Given** a track set to input 6 and a device with 2 inputs, **When** the project opens,
   **Then** the track keeps its input 6 setting, shows it as unavailable, and an armed take on
   it records no clip and says why; other tracks record normally.
3. **Given** that device is swapped for one with 8 inputs, **When** the user arms the track,
   **Then** input 6 works again with no re-picking.

---

### User Story 5 - Set up inputs on an Audio I/O page (Priority: P3)

A user with a 32-input interface uses only six of them. In Preferences → Audio they open the
Audio I/O page, see every input of the device, switch off the ones they never use and name
the rest ("Kick", "Snare", "Vox", "Gtr L/R"). From then on every audio track's input menu
lists only those inputs, by those names.

**Why this priority**: The per-track menu (Story 1) already makes every input usable; the
page keeps the menu short and readable on large interfaces (#353 option 2, as in Studio One).

**Independent Test**: On a device with 8+ inputs, switch off inputs 5–8 and name input 1
"Vox"; a track's input menu lists inputs 1–4 and the pairs 1/2, 3/4, with input 1 shown as
"Vox", and the track header shows "Vox" for a track on input 1.

**Acceptance Scenarios**:

1. **Given** the Audio I/O page, **When** the user opens it, **Then** it lists every input of the
   selected input device with its device name, an on/off switch, and a name field.
2. **Given** inputs 5–8 switched off, **When** the user opens a track's input menu, **Then**
   inputs 5–8 and any pair containing them are not listed.
3. **Given** input 1 named "Vox", **When** the user looks at the input menu, a track header, the
   mixer strip, the Session I/O row and the detail panel, **Then** each shows "Vox".
4. **Given** a stereo pair 3/4, **When** the user names the pair "Gtr", **Then** the menu offers
   "Gtr" for the pair, and inputs 3 and 4 still appear as mono picks under their own names.
5. **Given** a track already set to input 6, **When** the user switches input 6 off, **Then** the
   track keeps input 6, shows it as switched off, and does not record it until the input is
   switched on again or the track picks another input.
6. **Given** the user clears a custom name, **When** they look at the menu, **Then** the input
   shows the device's name again.
7. **Given** a fresh install or a newly selected device, **When** the user has not visited the
   page, **Then** every input is on with its device name (Story 1 works with no setup).
8. **Given** inputs set up for device X, **When** the user switches to device Y and back to X,
   **Then** X's on/off switches and names come back.

### Edge Cases

- The input device changes, or is unplugged, while a multi-track take is recording: every
  track's take ends together and keeps what it captured so far, as one track does today.
- The input and output devices differ (separate devices, not ASIO): every captured input
  shares one clock and one latency correction, so tracks stay aligned with one another.
- A device with many inputs (e.g. 32 or 64 channels): the menu stays usable (grouped mono /
  stereo), and arming all of them does not cause dropouts.
- A track's input is a stereo pair on a device with an odd number of inputs: the last input
  is offered as mono only.
- A track records from another track (internal source) while others record hardware: all
  record in the same pass.
- Disk too slow / full during a multi-track take: the take stops for every track with a
  clear message; what was captured so far is kept.
- An armed track whose input is unavailable or switched off: it does not block the others
  (see Story 4, Story 5).
- The user switches off every input: track menus offer only internal sources (other tracks)
  and say that hardware inputs are switched off in Preferences.
- Two armed instrument tracks on All inputs: both record the same notes (layering), as the
  user asked for by leaving them on All.
- An MPE controller with a single-channel filter would lose its per-note channels: the menu
  says so, and MPE devices are expected on All channels.
- A track's MIDI device is unplugged mid-take: that track stops receiving; its notes so far
  stay, held notes get note-offs; the other tracks keep recording.
- The device renames or reorders its channels (driver update): the user's names stay tied to
  the input numbers.
- Arming more tracks than the user expects: the record button / status shows how many tracks
  will record.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Nota MUST capture every input channel of the selected input device, not only
  the first two.
- **FR-002**: An audio track's hardware input MUST be selectable as any single input (mono)
  or any stereo pair of adjacent inputs starting on an odd input (1/2, 3/4, …).
- **FR-003**: The input menu MUST show the user's name for an input (Audio I/O page) if set,
  else the device's own channel name where the device reports one, else "Input N"; the short label (header, mixer, Session I/O, detail
  panel) MUST name the pick ("In 3", "In 3/4" or the device name).
- **FR-004**: A mono pick MUST be recorded to a mono file (one channel), and played back and
  monitored centred, as Ext 1 sounds today; the track's pan moves it as for any mono clip.
  A stereo pair is recorded to a stereo file.
- **FR-005**: Pressing Record MUST start a take on **every** armed track at once: each armed
  audio track captures its own input, each armed instrument track records the MIDI it
  receives (into its own clip, with overdub into a clip under the playhead as today).
- **FR-005a**: Arming stays as today (#332): a click on Arm arms that track exclusively,
  ⌘/Ctrl-click arms it in addition to the armed ones, a linked group (specs/076) arms
  together. The Arm button's tooltip MUST name the ⌘/Ctrl-click, and the user docs MUST
  describe arming several tracks for one pass.
- **FR-005b**: An instrument track MUST have a MIDI input: **All inputs** (default — every
  connected MIDI device and the computer keyboard, as today) or one device; and a channel
  filter: **All channels** (default) or one of 1–16. It receives, monitors and records only
  that input. MIDI from another track (MIDI In) stays as today. The input is saved with the
  project; a device that is not connected keeps the pick, is shown as unavailable, and the
  track receives nothing from it until it is connected again.
- **FR-006**: All clips of one multi-track take MUST start at the same beat and be
  sample-aligned with one another, with the same latency correction applied to each.
- **FR-007**: A multi-track take MUST be one undo step; Discard and Retry (specs/078) MUST
  drop and restart every track's take; loop recording MUST keep take lanes per track.
- **FR-008**: Linked group tracks (#528) MUST all record when armed together, whether they
  are audio or instrument tracks.
- **FR-009**: Session view MUST allow audio recording into several slots on different tracks
  at once; stopping one slot MUST NOT stop, change or discard another.
- **FR-010**: A track's input pick MUST be saved with the project; projects saved with Ext 1,
  Ext 2 and Ext 1/2 MUST open as mono input 1, mono input 2 and pair 1/2.
- **FR-011**: A pick the current device does not have MUST be kept, shown as unavailable,
  and record nothing on that track without stopping the others; the take MUST say which
  tracks did not record and why.
- **FR-012**: Input meters and monitoring MUST follow each track's own pick while armed.
- **FR-013**: The MCP tools for a track's input MUST accept and report any input / pair and a
  track's MIDI input (device and channel), and tools MUST list the audio device's inputs
  (number and names) and the connected MIDI devices.
- **FR-014**: Recording all inputs of an 8-input device onto 8 tracks MUST NOT cause dropouts
  at the buffer sizes where recording one track does not today.
- **FR-015**: New audio tracks MUST default to the first switched-on mono input (input 1
  with no setup, as today).
- **FR-016**: Preferences → Audio MUST have an Audio I/O page listing every input of the
  selected input device with its device name, an on/off switch and an editable name; stereo
  pairs (1/2, 3/4, …) MUST be nameable too.
- **FR-017**: Track input menus MUST list only switched-on inputs and only pairs whose two
  inputs are both on.
- **FR-018**: Switching an input off MUST NOT change any track's pick; a track on a
  switched-off input behaves like one on an unavailable input (FR-011) and shows why.
- **FR-019**: The on/off switches and names MUST be kept per device and survive restarts and
  device switches; a device never set up starts with every input on and unnamed.
- **FR-020**: Renaming or switching inputs MUST take effect in every open menu and label at
  once, with no restart and without interrupting playback or a take in progress.

### Key Entities

- **Input channel**: one input of the selected device — its number, its device name (or
  "Input N"), and whether it is currently available.
- **Input setup**: per device, for each input and each stereo pair — on/off and an optional
  user name. Kept with the user's preferences, not with the project.
- **Track input pick**: mono input N, stereo pair N/N+1, or another track (internal source,
  unchanged); saved with the project.
- **Track MIDI input**: All inputs or a device (by its name), and All channels or one
  channel; saved with the project.
- **Multi-track take**: one Record press — the set of tracks it records, one start beat, one
  latency correction; one undo step and one retry unit.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With an 8-input device, a user can record 8 sources onto 8 tracks in one pass;
  all 8 clips start at the same sample (0 samples of offset between tracks).
- **SC-002**: Every input of a device with up to 64 inputs is selectable on a track.
- **SC-003**: 100% of projects saved with Ext 1 / Ext 2 / Ext 1/2 open with the equivalent
  input and record the same signal as before.
- **SC-004**: Recording 8 tracks at once at a 128-sample buffer produces no more dropouts in a
  10-minute take than recording 1 track does.
- **SC-005**: Issue #528's reproduction (two linked tracks, record) yields two clips.
- **SC-006**: The #353 Session acceptance case (two slots, stop one early, save, reopen)
  passes.
- **SC-007**: On a 32-input device with 6 inputs switched on, a track's input menu shows
  only those 6 inputs and their pairs, under the user's names, with no restart.

## Assumptions

- Input names and on/off switches belong to the machine's setup (per device), not to the
  project: a project opened on another machine shows that machine's names. The project
  stores only the input numbers.
- Routing matrices and input-to-output routing (part of #353 option 2's wish list) are out
  of scope; the page only switches and names inputs.
- One input device at a time (the one selected in Preferences); aggregating several devices
  is out of scope (macOS users can use an aggregate device).
- Stereo pairs are odd-aligned (1/2, 3/4, …) as most interfaces label them; arbitrary pairs
  (e.g. 2/3) are out of scope.
- Input routing to outputs, cue mixes (#483) and multi-channel outputs are out of scope.
- No hard cap on how many tracks record at once beyond the device's input count and disk
  speed.
- The device's channel names come from the platform driver; generic "Input N" is acceptable
  where none exist (common with miniaudio on Linux).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
