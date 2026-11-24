# Feature Specification: Controller auto-mapping — profiles, device-follow knobs, Mackie Control

**Feature Branch**: `feat/controller-auto-mapping`

**Created**: 2026-10-29

**Status**: Draft

**Input**: User description: GitHub issue [nota-daw/nota#100](https://github.com/nota-daw/nota/issues/100) —
"Controller auto-mapping: Mackie Control, device-follow knobs, controller profiles". A user plugs in a
MIDI keyboard or controller (Launchkey, MPK mini, KeyLab, X-Touch Mini…) and wants its knobs, faders,
pads and transport buttons to control Nota right away. Today the only option is MIDI Learn, one
control at a time, and a mapping stays on the one parameter it was learned on — it does not follow
the user when they switch tracks or devices.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Main area: Managed app (profile loading, role dispatch, device-follow, pickup, the control-surface
protocol), on top of a new engine capability: today the C ABI only *receives* MIDI
(`nota_midi_input_*`, `nota_midi_poll_control_events`). Sending MIDI and SysEx to a controller's
output port — needed for feedback, DAW-mode init and device identification — does not exist yet.
UI: Preferences → controllers, the on-screen "controlled by the surface" indicator, the device page
editor. Builds on MIDI Learn (`src/managed/Nota.App/MidiLearnService.cs`).

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-29

- Q: What does spec 051 take through to implementation, and what moves to separate specs? → A: The whole issue stays in 051 (Stories 1–6), delivered in priority order P1 → P2 → P3 as several pull requests under this one spec.
- Q: How does a user without a shipped profile create one (Story 5)? → A: An in-app Controller setup wizard (move each control, Nota detects message and encoder mode) that saves a profile file; init SysEx and LED feedback are added only by editing the file.
- Q: Where are user-edited device parameter pages stored? → A: Globally per device type / plug-in identity by default, and each saved project also carries a copy of the pages for the devices it contains; inside that project its copy wins, so the project behaves the same on another machine.
- Q: Do the 8 knobs of a generic (non-MCU) controller always drive the selected device, or can they switch to mixer modes? → A: Knob modes — Device (default), Volume, Pan, Send A/B/… over the current bank of eight tracks; switched by mode buttons the profile declares, or from Nota.
- Q: Which controllers get shipped profiles, and on what hardware are they verified? → A: Arturia KeyLab Essential 61 is the hardware reference (verified on the real device); the other seed profiles (Launchkey, MPK mini, X-Touch Mini, generic MCU) come from public charts and are verified with an emulator on a virtual MIDI port.


### Session 2026-11-19

- Q: How does a user change what a hardware control drives without editing files? → A: From the
  right-click menu of the on-screen control (a device parameter, a mixer fader / pan, a button):
  a **Controller** submenu says which hardware control drives it now, **Unbind** removes just this
  parameter, and **Bind to ▸** lists the controller's controls by name to pick one from — no
  "move a control to learn". A device parameter goes into that knob's slot of the current page;
  a mixer fader / pan or a button gets the picked control pinned to it, whatever the bank.
- Q: Where are these changes kept? → A: Device parameter slots as edited device pages (global +
  the project's copy, FR-018a). Pins and unbinds that name a track live in the project (tracks
  differ between projects); those that don't (transport, master, commands) are global and the
  project carries a copy, like pages.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A known controller works on plug-in (Priority: P1)

A user plugs in a controller Nota ships a profile for. Nota recognises it, says so in a short notice,
and its transport buttons, faders and knobs immediately do something sensible: play / stop / record
/ loop drive the transport, faders drive the volumes of the tracks in the current bank of eight,
knobs drive the selected device (see Story 2). The keybed and pads keep playing notes exactly as
before. No MIDI Learn, no setup.

**Why this priority**: This is the whole promise of the issue — "plug in and it works". Without
recognition and roles, nothing else in this spec has anything to attach to.

**Independent Test**: With a seed-profile controller (or a virtual MIDI port that reports the same
port name / identity reply), start Nota, press Play on the controller, move fader 1 — the transport
starts and track 1's volume follows; play the keys — notes reach the armed instrument as before.

**Acceptance Scenarios**:

1. **Given** Nota is running and no controller is connected, **When** the user connects a controller
   matching a shipped profile, **Then** within a few seconds Nota shows "<Controller> connected —
   using the <profile> profile" and the profile's roles are live without restarting Nota.
2. **Given** a recognised controller, **When** the user presses its Play / Stop / Record / Loop
   buttons, **Then** the transport does the same as the on-screen buttons.
3. **Given** a recognised controller with faders, **When** the user moves fader N, **Then** the
   volume of the N-th track of the current bank follows, with one undo step per movement (as
   MIDI-Learn moves do today), and it is written to automation when automation is armed.
4. **Given** a recognised controller, **When** the user plays its keys or pads, **Then** the notes
   reach the armed instrument tracks exactly as an unprofiled MIDI keyboard's would.
5. **Given** a controller whose port name and identity match no profile, **When** it is connected,
   **Then** it behaves exactly as today (notes and MIDI Learn only) — nothing is mapped implicitly.
6. **Given** a recognised controller, **When** the user turns its profile off in Preferences,
   **Then** it reverts to plain MIDI (notes + MIDI Learn) and the choice is remembered.

---

### User Story 2 - Knobs follow the selected device (Priority: P1)

Whatever device the user selects — a built-in instrument, an audio effect, a MIDI effect, a hosted
plug-in — the controller's eight knobs control that device's eight most important parameters. The
device card shows which parameters the knobs are on (a coloured marker, like Ableton's "blue hand"),
and page buttons step to the next / previous page of eight. Selecting another device or another track
moves the knobs there instantly.

**Why this priority**: This is what makes a cheap 8-knob controller useful for sound design — the
one thing MIDI Learn cannot do, because a learned mapping is welded to a single parameter.

**Independent Test**: Select Nota Volt, turn knob 1 — its first page's first parameter moves and the
card shows the knob marker on the first eight parameters; select a Compressor on another track — the
same knob now moves the compressor's threshold; press "next page" — the marker moves to the second
page.

**Acceptance Scenarios**:

1. **Given** a built-in device is selected, **When** the user turns knob N, **Then** parameter N of
   that device's current page changes, through the same path as the on-screen control (undo,
   automation recording, modulation display).
2. **Given** a device is selected, **When** the user presses the profile's page-next / page-prev
   controls, **Then** the knobs move to the next / previous page and the card's marker follows; at
   the last page "next" does nothing.
3. **Given** a hosted plug-in is selected and has no user-defined pages, **When** the user turns the
   knobs, **Then** they drive the plug-in's parameters in order, eight per page.
4. **Given** the user selects a different track, **When** they turn a knob, **Then** it controls the
   device that is selected (or, if none, the instrument — else the first device) on that track.
5. **Given** a device with fewer than eight parameters on a page, **When** the user turns a knob
   with no parameter assigned, **Then** nothing changes and nothing errors.
6. **Given** the knobs are in Pan mode (switched by a profile mode button or from Nota),
   **When** the user turns knob N, **Then** the pan of the N-th track of the current bank changes;
   switching back to Device mode returns the knobs to the focused device's current page.
7. **Given** the selected device is deleted, **When** the user turns a knob, **Then** the knobs move
   to the track's next device by the same rule as 4, or do nothing if the track is empty.

---

### User Story 3 - No parameter jumps (soft takeover) (Priority: P2)

On a controller whose knobs and faders have no motors, a physical control's position rarely matches
the value it now controls (after a device switch, a bank switch, an undo or automation). Instead of
the parameter jumping to where the knob happens to be, the knob has to "pick up" the value first:
until it crosses the current value nothing changes, and the on-screen control shows where the knob
is relative to the value.

**Why this priority**: Without it, device-follow (Story 2) and banking make every switch a sound
glitch. It matters as soon as Stories 1–2 exist, but they are usable without it.

**Independent Test**: Map knob 1 to a parameter at 80 %, park the physical knob at 10 %, select
another device and back; turn the knob up slowly — the parameter does not move until the knob passes
80 %, then follows it.

**Acceptance Scenarios**:

1. **Given** an absolute knob whose position differs from the target value, **When** the user moves
   it without crossing the value, **Then** the value does not change and the control shows a pickup
   hint (where the knob is vs. the value).
2. **Given** the same knob, **When** its movement crosses (or lands within a small tolerance of) the
   value, **Then** the value picks up and follows from there on.
3. **Given** a relative encoder or a motorised fader, **When** it is moved, **Then** pickup does not
   apply — relative controls never jump, motorised faders are moved to the value by feedback.
4. **Given** pickup is switched off for a profile in Preferences, **When** a knob moves, **Then** the
   value jumps to the knob's position (today's MIDI Learn behaviour).

---

### User Story 4 - Mackie Control surface with feedback (Priority: P2)

A user with a Mackie-Control-capable surface (Behringer X-Touch / X-Touch One, iCON, the "DAW /
Mackie" mode of many keyboards) selects it as a Mackie Control surface. Its eight channel strips show
eight tracks: faders drive volume, V-pots drive pan, buttons drive mute / solo / arm / select, the
scribble strips show track names and values, the LEDs light for the real state, motor faders move
when the volume changes in Nota (mouse, automation, undo). Bank buttons shift by eight tracks,
channel buttons by one; the jog wheel moves the playhead; transport buttons drive the transport.

**Why this priority**: One protocol covers a wide range of mid-range hardware and is the industry
norm for mixer control. It is a larger, separable piece (it needs MIDI output and a full feedback
loop), so it follows the profile/device-follow core.

**Independent Test**: With an MCU device (or an MCU emulator on a virtual port), move fader 3 on the
surface — track 3's volume follows; drag track 3's fader in Nota — the surface's motor fader follows;
press Bank → — strips show tracks 9–16 with their names.

**Acceptance Scenarios**:

1. **Given** an MCU surface is connected and enabled, **When** Nota starts or the surface connects,
   **Then** the strips show the first eight tracks' names, fader positions, pan and mute / solo /
   arm / select LEDs matching Nota's state.
2. **Given** the surface is live, **When** a track's volume, pan, mute, solo, arm or selection
   changes in Nota (mouse, automation playback, undo, MCP), **Then** the surface reflects it
   (motor fader, V-pot ring, LED, scribble strip) without the user touching it.
3. **Given** the user touches and moves a motor fader, **When** automation is playing on that
   track's volume, **Then** the touch takes over the automation the same way a mouse drag does, and
   Nota does not fight the user's hand with motor feedback while the fader is touched.
4. **Given** more than eight tracks, **When** the user presses Bank ← / → or Channel ← / →, **Then**
   the strips shift by eight / one track and all feedback is refreshed; the current bank is shown
   in Nota (the tracks under the surface are marked).
5. **Given** the surface is live, **When** the user turns the jog wheel, **Then** the playhead moves
   (scrubs while playing is not required), and the timecode / beats display on the surface shows the
   playhead position if the surface has one.
6. **Given** the surface's MIDI output port is missing or disconnected, **When** the user operates
   it, **Then** input still works and Nota shows that feedback is unavailable.

---

### User Story 5 - Make it mine: user profiles and device pages (Priority: P3)

A user whose controller has no shipped profile, or who wants a different layout, can create a
profile: either by writing / copying a profile file into the user profiles folder, or with an
in-app "Controller setup" that asks them to move each control (knob 1… knob 8, fader 1…, Play…)
and writes the profile for them. Separately, the user can edit which parameters a device's pages
contain (reorder, add, remove), and those pages are used by every instance of that device.

**Why this priority**: Shipped profiles cover only a handful of controllers and the default page of
a plug-in is often its least useful parameters; this opens the feature to everyone else. It is not
needed for the first useful slice.

**Independent Test**: Run Controller setup for an unprofiled controller, assign knobs 1–8 and Play;
reconnect it — it is recognised and works like a shipped profile. Edit a plug-in's page 1 to hold
four chosen parameters; select another instance of that plug-in — the knobs drive the chosen four.

**Acceptance Scenarios**:

1. **Given** a profile file is placed in the user profiles folder, **When** a matching controller
   is connected (or the user presses "Reload profiles"), **Then** it is recognised, and a user
   profile wins over a shipped profile that matches the same controller.
2. **Given** an invalid or partly invalid profile file, **When** profiles load, **Then** Nota skips
   the invalid parts, keeps working, and names the file and the problem in Preferences.
3. **Given** the user edits a device's pages, **When** they select any instance of that device type
   (in this or another project), **Then** the knobs follow the edited pages; "Reset pages" restores
   the shipped (built-in) or default (plug-in) pages.
5. **Given** a project saved with edited pages for a plug-in, **When** another user (whose global
   pages for that plug-in differ or are absent) opens it, **Then** the knobs follow the project's
   pages in that project, and that user's global pages are unchanged.
4. **Given** Controller setup is run, **When** the user moves a control for a role, **Then** Nota
   detects its message type, channel, number and encoder mode (absolute vs. the relative variants)
   from the movement, and asks only when it cannot tell.

---

### User Story 6 - HUI surfaces (Priority: P3)

A user with an older HUI-only surface selects HUI instead of Mackie Control and gets the same
mixer-strip behaviour (Story 4) to the extent HUI provides it.

**Why this priority**: The issue marks HUI optional; few current devices are HUI-only.

**Independent Test**: As Story 4, with a HUI device or emulator.

**Acceptance Scenarios**:

1. **Given** a HUI surface is enabled, **When** the user moves fader N / presses mute N, **Then**
   the same thing happens as on an MCU surface, and Nota's state is fed back to the surface.

---

### User Story 7 - Rebind from the control's menu (Priority: P2)

A user right-clicks a knob on a device card, a mixer fader or pan knob, or a transport button and
sees which hardware control drives it ("Knob 4 · Arturia KeyLab Essential 61 mk3 (DAW)"). They
choose **Unbind** and the hardware control stops driving that parameter, or **Bind to ▸ Knob 2**
(or Fader 3, Loop, …) and from then on that hardware control drives it. **Back to default**
undoes their change.

**Why this priority**: The automatic mapping is a guess; the user must be able to correct it at
the point where they see it, without the page editor or a profile file.

**Independent Test**: With a profiled controller connected: unbind knob 4 on a device, turn the
hardware knob 4 — nothing moves; bind the parameter to knob 2 — hardware knob 2 moves it; on the
mixer bind track 3's fader to hardware fader 1, shift the bank — fader 1 still drives track 3;
unbind Play — the hardware Play button no longer starts the transport.

**Acceptance Scenarios**:

1. **Given** knob 4 of the focused device's page drives Drive, **When** the user picks
   Controller ▸ Unbind on the Drive knob, **Then** the slot is empty, the number disappears from
   the card, and the hardware knob 4 does nothing on this page — in every project (and in the
   saved copy of this one).
2. **Given** a device parameter not on the current page, **When** the user picks Bind to ▸ Knob 2,
   **Then** it takes knob 2's slot on the current page (the parameter that was there leaves it).
3. **Given** a profiled controller with faders, **When** the user picks Bind to ▸ Fader 1 on track
   3's mixer fader, **Then** hardware fader 1 drives track 3's volume whatever the bank, and no
   longer moves the bank's first track; the binding is saved with the project.
4. **Given** the hardware Play button is a transport role, **When** the user picks Unbind on Nota's
   Play button, **Then** pressing hardware Play does nothing in Nota (the message reaches MIDI
   Learn like any unprofiled control); Back to default restores it.
5. **Given** a parameter with no controller binding, **When** its menu opens, **Then** the
   Controller submenu offers only Bind to ▸ — and is absent when no controller is connected.

---

### User Story 8 - Play the Session from the controller (Priority: P2)

A user in the Session view moves through the clip grid with the controller (the big encoder or
arrow buttons), launches the selected clip with a button and stops it with the same button or a
stop button, without touching the mouse.

**Independent Test**: Session view with clips; turn the KeyLab's main encoder — the selection moves
down the track's slots; press the encoder — the clip launches; press again — it stops; the track
buttons move to the next track.

**Acceptance Scenarios**:

1. **Given** the Session view shows, **When** the jog wheel turns one step right, **Then** the
   slot below is selected; the track buttons select the next / previous track's slot.
2. **Given** a stopped clip is selected, **When** the launch button is pressed, **Then** it
   launches (queued to the launch quantum); pressed again while queued it is withdrawn, while
   playing its track stops.
3. **Given** the Arrangement shows, **When** a Session button is pressed, **Then** nothing happens
   (and the jog wheel moves the playhead as before).
4. **Given** a controller Nota doesn't know, **When** the user runs Set up…, **Then** it offers the
   Session buttons as a group to learn.

---

### Edge Cases

- **Two identical controllers** connected: each is recognised separately (port names differ by an
  index); both work; their bank / page state is independent.
- **Hot-plug**: unplugging a controller drops its roles without errors; replugging restores them,
  including the bank / page it was on, and re-sends DAW-mode init and the full feedback state.
- **Port claimed by a control surface vs. notes**: messages a profile assigns to a role (e.g. MCU
  transport buttons, which are note messages) are consumed and never play notes on an instrument;
  everything the profile does not claim (keys, pads in note mode, pitch wheel, mod wheel, sustain)
  passes through as today.
- **Conflict with MIDI Learn**: a control that has an explicit MIDI Learn mapping drives that mapping
  and not its profile role — the user's explicit choice wins.
- **Encoder modes**: an encoder sending relative messages is never treated as absolute (no jumps to
  0 / 127); relative steps accelerate with the step size the encoder reports.
- **14-bit and NRPN**: a 14-bit fader or NRPN control drives its target at full resolution; a
  controller that sends only the coarse half of a 14-bit pair still works at 7-bit resolution.
- **Device without pages** (a plug-in exposing zero parameters, an empty rack): knobs do nothing,
  the card shows no marker.
- **Racks**: selecting a rack controls its macros (the rack's page is its macros); selecting a
  device inside a rack controls that device.
- **Send mode with fewer sends than requested** (no return tracks, or stepping past the last
  send): Send mode is unavailable / stops at the last send; a knob on a track without that send
  does nothing.
- **Bank beyond the last track**: strips with no track are blank (fader down, LEDs off, empty
  scribble strip) and ignore input.
- **Project switch / new project**: banks and pages reset to the first bank / first page; profiles
  and pickup state carry over.
- **Track reordering / deletion while banked**: the surface follows the visible order; feedback is
  refreshed.
- **Controllers that need SysEx to enter DAW mode** and never answer the identity request:
  recognition by port name alone is enough to send the init message.
- **Komplete Kontrol S-series** (closed protocol): plain MIDI mode only — notes and MIDI Learn, no
  profile is shipped and none is promised.
- **Feedback storms**: a value change caused by the controller itself is not echoed back in a way
  that makes a non-motorised controller or a motor fader oscillate.

## Requirements *(mandatory)*

### Functional Requirements

**Profiles and recognition**

- **FR-001**: Nota MUST load controller profiles from two sources: profiles shipped with the app,
  and a user profiles folder inside Nota's data directory. A user profile MUST take precedence over
  a shipped one that matches the same controller.
- **FR-002**: A profile MUST identify its controller by MIDI port name (exact or pattern) and/or by
  the manufacturer / family / model fields of the MIDI Identity Reply. Nota MUST send an Identity
  Request to a newly connected controller that has an output port, and MUST fall back to port-name
  matching when no reply arrives within a short timeout.
- **FR-003**: A profile MUST be able to declare, per physical control: its message (CC, note,
  pitch-bend, channel, NRPN, 14-bit CC pair), its role (knob N, fader N, encoder N, button N, pad
  N, transport play / stop / record / loop / rewind / forward, page next / prev, bank next / prev,
  track next / prev, knob mode device / volume / pan / send next, jog, or a command button that runs one of Nota's
  commands — undo, redo, save, metronome, tap tempo, quantize …), whether it is motorised, and — for knobs / encoders — its encoder mode:
  absolute, relative two's complement, relative sign-bit, or relative binary offset.
- **FR-004**: A profile MUST be able to declare the SysEx (or other MIDI) messages to send when the
  controller connects (e.g. switch to DAW mode) and when Nota releases it (restore normal mode), and
  the messages that drive its LEDs / rings for a role (on / off / value).
- **FR-005**: Profiles MUST be human-readable, documented text files (one file per controller) so
  users can write and share them; the format MUST be versioned so future fields do not break older
  files.
- **FR-006**: When a controller is recognised, Nota MUST tell the user which profile it applied, and
  Preferences MUST list every connected controller with its profile (or "none"), an on / off switch
  per controller, and any profile load errors.
- **FR-007**: Nota MUST ship seed profiles for at least four popular controllers plus a generic
  Mackie Control profile, built only from manufacturers' public MIDI implementation documents (no
  data derived from other DAWs' remote scripts; community data only after an AGPL-compatibility
  check).

**Roles and dispatch**

- **FR-008**: Transport roles MUST behave exactly like the corresponding on-screen transport
  controls.
- **FR-009**: Fader roles MUST control the volume (and, for encoder / V-pot roles in mixer mode,
  the pan) of the tracks in the current bank of eight; bank / track roles MUST shift the bank by
  eight / one. Nota MUST show which tracks are under the controller.
- **FR-010**: Every value a controller changes MUST go through the same path as the on-screen
  control: one undo step per movement, automation writing when armed, automation take-over on
  touch.
- **FR-011**: Messages claimed by a role MUST be consumed (never reach instruments as notes / CC);
  unclaimed messages MUST behave exactly as today.
- **FR-012**: An explicit MIDI Learn mapping on a control MUST override that control's profile
  role.
- **FR-013**: Relative encoders MUST be decoded per their declared mode; 14-bit CC and NRPN
  controls MUST be decoded at their full resolution.

**Device-follow knobs**

- **FR-014**: Every built-in device (instruments, audio effects, MIDI effects, racks) MUST declare
  one or more pages of up to eight key parameters, in an order chosen for playing (most important
  first).
- **FR-015**: A hosted plug-in without user-defined pages MUST default to its parameters in the
  plug-in's own order, eight per page.
- **FR-016**: Knob roles MUST control the current page of the *focused device*: the selected
  device; else the selected track's instrument; else that track's first device. Focus MUST follow
  selection changes immediately, including track selection in the Arrangement or Mixer while the
  Devices panel is hidden (the focused device is then resolved from that track's chain by the
  same rule).
- **FR-016a**: Knob roles MUST support modes: **Device** (FR-016, the default), **Volume**,
  **Pan** and **Send** (one send at a time, A, B, … stepped by the send-mode control) — the last
  three acting on the current bank of eight tracks (FR-009). The mode MUST be switchable by the
  mode controls a profile declares and from Nota (the controller entry in Preferences / the
  on-screen controller indicator), MUST be shown in Nota, and MUST be kept per controller for the
  session. Switching mode re-arms pickup (FR-019) for every knob.
- **FR-017**: The focused device's card MUST show which parameters the knobs are on and the page
  number; page roles MUST step pages.
- **FR-018**: Users MUST be able to edit a device type's pages (add, remove, reorder parameters,
  add pages) and reset them. Edited pages MUST be stored globally per device type (per plug-in
  identity for plug-ins), so they apply to every instance in every project, and an edit MUST also
  update the copy in the current project.
- **FR-018a**: A saved project MUST carry a copy of the edited pages for the device types it
  contains. While that project is open, its copy MUST win over the global pages (so the project
  behaves the same on another machine or for a collaborator); opening it MUST NOT change the
  user's global pages. Device types without edited pages use the shipped / default pages.

**Soft takeover**

- **FR-019**: For absolute, non-motorised controls Nota MUST apply pickup: after the target's value
  changes by any other means (focus / bank / page switch, mouse, automation, undo), the control MUST
  NOT change the value until its position crosses the value or comes within a small tolerance of it.
- **FR-020**: While a control has not picked up, Nota MUST show the control's position relative to
  the value on the target control.
- **FR-021**: Pickup MUST be switchable off per controller (then the value jumps, as with MIDI Learn
  today).

**Mackie Control / HUI surfaces**

- **FR-022**: Nota MUST support the Mackie Control protocol for channel strips (fader, V-pot,
  mute, solo, rec-arm, select, scribble strip text, V-pot ring, meters where the surface has
  them), transport, bank / channel shift, jog, and the time display.
- **FR-023**: Nota MUST send feedback for every state it shows on a surface whenever that state
  changes in Nota by any means, and send the full state on connect, bank change and project load.
- **FR-024**: While a motor fader is touched, Nota MUST NOT drive it with feedback, and MUST treat
  the touch as the start / end of a movement for automation and undo.
- **FR-025**: A surface MUST be selectable as Mackie Control explicitly (by port) in Preferences
  even when it is not auto-recognised.
- **FR-026**: HUI SHOULD be supported for the same strip, transport and bank functions (Story 6,
  lowest priority; may ship later than MCU).

**User profiles**

- **FR-030**: Nota MUST offer a Controller setup wizard that walks the user through the roles
  (knobs, faders, buttons, transport, page / bank), learns each role from one movement or press —
  detecting message type, channel, number, 14-bit / NRPN and encoder mode — and saves the result as
  a profile file in the user profiles folder. Init / release messages and LED feedback are not part
  of the wizard; users add them by editing the saved file, which Nota reloads without a restart.
  - **FR-030a** (2026-11-19, hand test): each step shows a picture of the control it wants (knob,
    fader, button with its icon, jog wheel) and what the controller sends as it arrives; a learned
    step moves on by itself (Next only for an encoder whose encoding is ambiguous); a whole group
    can be skipped ("No more knobs"), Back redoes the previous step, and a control already learned
    is ignored and named. The groups are knobs, faders + master, transport, track / bank / page,
    Session buttons (Story 8) and extra buttons (undo, redo, metronome, save, tap tempo) + jog.
  - **FR-030b**: an encoder (or knob) can be a two-way command control — one command per step
    right, another per step left (`commandLeft`); the wizard learns the Session's clip and track
    walks this way. The window follows Nota's dialog look: a strip of groups (done ticked, the
    current one picked, a click goes there), a progress line, the step in a well with the picture in
    a round sunken dial, what arrives as a readout with an activity light, the learned controls as
    chips (a click redoes one), Cancel / Finish (primary) in a footer under a seam.
  - **FR-030c** (2026-11-19): every pair of opposite actions — previous / next track, bank and page,
    rewind / forward, the Session's up / down and left / right, undo / redo, tempo − / + — can be
    one encoder or knob (`roleLeft` / `commandLeft`) **or** two buttons. The wizard asks for a pair
    in one step: turn an encoder and both halves are learned; press a button and it is the first
    half, then it asks for the second button.
- **FR-035**: A controller MUST be able to walk the Session view's clip selection (up / down /
  left / right), launch or stop the selected clip, stop the selected track, launch the selected
  scene and stop all clips — as command buttons (palette commands `session.*`), available while
  the Session view shows. While it shows, the jog wheel walks the selected track's clips and the
  track buttons the tracks (Story 8).

**General**

- **FR-027**: Controller handling MUST NOT affect audio: no audible dropouts or added latency for
  notes when controllers send dense message streams or receive dense feedback.
- **FR-028**: Profile assignments, per-controller on / off, pickup setting and edited device pages
  MUST persist across restarts (pages also per project, see FR-018a). Bank and page positions are session state and are not saved in
  projects.
- **FR-029**: Headless clients (MCP) MUST be able to list connected controllers and their profiles
  and read the focused device and its current page, consistent with how MIDI Learn is exposed today.

- **FR-031**: The right-click menu of every on-screen control a controller can drive (device
  parameters, mixer faders / pan / mute / solo / arm, transport buttons) MUST carry a Controller
  submenu while a controller is connected, naming the hardware control that drives it now, if any
  (Story 7).
- **FR-032**: **Unbind** MUST stop that hardware control driving that parameter only: a device
  parameter leaves its slot of the page (an edited page, FR-018a); a bank fader / knob skips that
  track; a role with no track (transport, master, a command) releases the hardware control to
  plain MIDI / MIDI Learn.
- **FR-033**: **Bind to ▸** MUST list the connected controllers' declared controls by name (Knob N,
  Fader N, Play, Loop, Undo …) — for a device parameter the knobs, which put it into that slot of the
  current page; for anything else the absolute controls, which are pinned to it whatever the bank
  and leave their role. Relative encoders and Mackie / HUI strips are not offered for pins.
- **FR-034**: **Back to default** MUST remove the user's change for that parameter. Pins and unbinds
  that name a track MUST be saved with the project; those that don't MUST be global with a copy in
  the project (as FR-018a).

### Key Entities *(include if feature involves data)*

- **Controller profile**: identity matchers (port name patterns, identity reply fields), a list of
  controls (message, role, encoder mode, motorised, feedback message), init / release messages,
  options (pickup default, protocol = generic / MCU / HUI), format version, origin (shipped / user).
- **Connected controller**: a physical device seen on an input (and optionally output) port, the
  profile applied to it (or none), enabled flag, its bank position, page position and knob mode
  (Device / Volume / Pan / Send N).
- **Control role**: the logical function a physical control has (knob N, fader N, transport X…),
  independent of which controller provides it.
- **Device parameter page**: an ordered list of up to eight parameter references for a device type;
  built-in defaults ship with each device; user overrides stored globally per device type / plug-in
  identity, with a copy saved in each project that uses that device type (the project copy wins
  inside that project).
- **Focused device**: the device the knob roles currently drive, derived from selection.
- **Pickup state**: per control × target, whether the control has caught the value and the last
  physical position.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With any seed-profile controller, a user goes from plugging it in to controlling the
  transport, track volumes and the selected device's parameters in under 10 seconds, with zero
  configuration steps.
- **SC-002**: Switching the focused device or page re-targets all eight knobs before the user's
  next knob movement is processed (perceived as instant; under 50 ms).
- **SC-003**: With pickup on, switching devices / banks 20 times and moving every knob produces
  zero parameter jumps larger than the pickup tolerance.
- **SC-004**: On an MCU surface, a change made in Nota (mouse, automation, undo) is visible on the
  surface (motor fader, LED, scribble strip) within 100 ms, and a full bank switch refreshes all
  eight strips within 200 ms.
- **SC-005**: Driving all eight faders and knobs continuously with feedback on causes no audio
  dropouts in the smoke-test session and no measurable increase in live-note latency.
- **SC-006**: At least four shipped controller profiles plus the generic Mackie Control profile are
  available at release, each checked against its manufacturer's published MIDI implementation. The
  Arturia KeyLab Essential 61 profile passes Stories 1–3 on the real device; the others pass them
  with an emulator on a virtual MIDI port.
- **SC-007**: A user without a shipped profile can create a working profile for an 8-knob,
  transport-button controller (via Controller setup or by editing a copied profile file) in under
  5 minutes.

## Assumptions

- Delivery: this one spec covers the whole issue and ships as several pull requests in priority
  order — P1 (Stories 1–2), then P2 (Stories 3–4), then P3 (Stories 5–6). Each pull request leaves
  the app releasable; later stories may slip to a later release without reopening the spec.
- Seed devices: **Arturia KeyLab Essential 61 mk3** (hardware reference — the maintainer owns one), Novation
  Launchkey (MK3/MK4), Akai MPK mini (MK3), Behringer X-Touch Mini; plus the generic Mackie Control
  profile, which also covers devices in "Mackie / DAW" mode. Devices without hardware at hand are
  verified with an emulator on a virtual MIDI port.
- "Selected device" is the device selection that already exists in the device chain view; no new
  selection concept is introduced. Device lock ("keep knobs on this device regardless of
  selection") is out of scope for this spec.
- Pickup tolerance and the relative-encoder acceleration curve are fixed sensible defaults, not
  user settings.
- Feedback for non-MCU profiles is limited to what a profile declares (button LEDs, encoder rings);
  no display (scribble / OLED) support for generic profiles in this spec.
- Mackie Control mixer mode covers regular, group and return tracks in the track-list order; the
  master is reachable on the MCU master fader where the surface has one.
- Komplete Kontrol S-series and other closed protocols get plain MIDI only.
- MIDI 2.0 / MIDI-CI Property Exchange self-description is out of scope (possible follow-up).
- Gamepad and Nota Remote sources are unaffected; they keep using MIDI Learn.
- Platforms: macOS, Windows and Linux, wherever MIDI input already works; feedback needs an output
  port, which on all three requires new engine support (MIDI and SysEx output).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
      (MIDI output should reuse the existing CoreMIDI / WinMM / RtMidi backends; seed profile data
      must come from public manufacturer documents — record sources per profile)
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
      (MIDI output and SysEx must be sent off the audio thread)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
      (MIDI output / SysEx send via a virtual or loopback port, where the platform allows)
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
