# Feature Specification: Remote Macros as a controller surface with an LED display

**Feature Branch**: `feat/remote-led-display`

**Created**: 2026-11-24

**Status**: Draft

**Input**: User description: "Nota Remote Macros screen becomes a controller-style surface with an
LED dot-matrix display: Track / Device / Page buttons that mirror the MIDI controller focus
(spec 107, #929); a mini LED display (proposed 128 × 48, 16 brightness levels) above the eight
knobs, where each page of each built-in device draws a simplified version of its card view; the
frame is rendered by Nota and sent over the Remote protocol, clients only draw the dots. First
wave: Volt and Reverb." Mockup: https://claude.ai/artifact/G2Wspdbj83aCbm5ybsxg4c.

**Issue**: [nota-daw/nota#1030](https://github.com/nota-daw/nota/issues/1030) (approved). It was opened
together with the pull request, at the maintainer's request; the spec and code came first.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — display rendering in shared code, Remote hub and protocol
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Also: the Remote web app (`src/web/remote`, Macros screen) and its protocol. Nota NX (sibling
repo) can consume the same messages later; it is not part of this spec.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

### What exists today

- **Remote Macros** (specs/014, 090, 107): eight knobs for one entry of a flat list of the
  track's "macro devices". A rack gives its eight macros; a built-in instrument, a built-in
  effect or a plug-in gives its parameters in engine order, eight at a time, each block a
  separate list entry ("Reverb · 1/3"). A sheet picks the entry. No picture of the device.
- **Controller pages** (specs/051): a controller's knobs walk named pages per device type —
  edited pages → the built-in catalog (`DevicePages.json`, e.g. Volt: Main · Osc · Filter · LFO;
  Reverb: Main · More) → a plug-in's params eight per page. Page ◀ / ▶ step them.
- **Controller focus in Remote** (specs/107): Nota sends `foc` (track, device, page, page count,
  page name); with "Follow Nota's selection" on, Macros switches to the controller's device.
  Track ◀ / ▶ select tracks, `device.focusPrevious/Next` step devices on a track.
- **Desktop card views**: every built-in device card draws its own view (Volt's filter
  response over the live spectrum, envelopes, oscillator shape; Reverb's decay tail in dB with
  the measured tail of your audio, decay across frequency). They live in the Avalonia app and
  read the engine's existing measurement taps (instrument output tap, effect scope rings, tail
  history) on the UI thread.

## Clarifications

### Session 2026-11-24

- Q: Which pages does the phone walk? → A: The controller's pages for the device (edited → catalog → plug-in default), so phone and hardware always show the same eight knobs; each page's picture is chosen by what is on it (FR-002).
- Q: Matrix size and depth? → A: 128 × 48 with 16 brightness levels (FR-010).
- Q: Scope? → A: Every built-in device gets its own display in this spec (instruments, audio effects, MIDI effects); racks and plug-ins show the bars (FR-025).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Walk tracks, devices and pages from the phone (Priority: P1)

A player has the phone on a stand next to the keyboard. On the Macros screen they press
Track ▶ to move to the next track, Device ▶ to move from the Volt to the Reverb on it, and a
page button to go from Volt's first page to its Filter page. The eight knobs show that page's
parameters. If a MIDI controller is connected, its focus moves with the phone, and pressing
Track / Device / Page on the controller moves the phone the same way.

**Why this priority**: it turns Macros from a parameter list into a surface you play, and it
is useful on its own, without the display.

**Independent Test**: with two tracks (Volt → Reverb each), press Track, Device and page
buttons on the phone and check the knobs, the header names and Nota's selection after each
press; repeat from a controller (or the controller test port) and check the phone follows.

**Acceptance Scenarios**:

1. **Given** Macros on track "Bass" (Volt → Reverb) showing Volt page 1, **When** Device ▶ is
   pressed, **Then** the knobs show Reverb's page 1 and the device label reads "Reverb · 2/2".
2. **Given** Volt with pages Main · Osc · Filter · LFO, **When** the "Filter" page button is
   pressed, **Then** the eight knobs show that page's parameters (an unset slot shows an empty
   knob) and the Filter button is marked as current.
3. **Given** the phone follows Nota's selection and a controller is connected, **When** Track ▶
   is pressed on the phone, **Then** Nota selects the next track, the controller's knobs drive
   its focused device, and the phone shows that device.
4. **Given** the same, **When** Page ▶ is pressed on the controller, **Then** the phone's page
   changes within one screen refresh.
5. **Given** the last track (or the last device, or the last page) is current, **When** the
   matching ▶ is pressed, **Then** nothing changes and the button looks disabled.
6. **Given** the phone does not follow Nota's selection, **When** Track ▶ is pressed on the
   phone, **Then** only the phone's track changes; Nota's selection and the controller stay.
7. **Given** a phone that may only play notes (control not allowed), **When** a navigation
   button is pressed, **Then** the usual "notes only" toast shows and nothing moves.

---

### User Story 2 - See the device on an LED display (Priority: P1)

Above the knobs sits a dot-matrix display, like the screens on hardware synths. On Volt's
filter page it shows the filter's response curve over the live spectrum; on Reverb's main page
it shows the decay tail in dB with the pre-delay and early reflections, and the measured tail
of the audio running under it. Turning a knob redraws the picture at once, and the top row
shows the knob's name and value ("CUTOFF 1.20 KHZ") for about a second.

**Why this priority**: the picture is what makes turning a knob on glass meaningful; it is the
core of the request.

**Independent Test**: open Macros on a Volt track and on a Reverb, play notes, turn each knob on
each page and compare the display with the desktop card's view: same shape, same direction of
change, header shows the value.

**Acceptance Scenarios**:

1. **Given** Volt's page with the filter controls, **When** Cutoff is turned up, **Then** the
   response curve's corner moves right and the top row shows "CUTOFF" and the new value.
2. **Given** a Volt track playing, **When** the filter page shows, **Then** a spectrum under the
   curve moves with the sound; with nothing playing it falls quiet.
3. **Given** Reverb's main page, **When** Decay is raised, **Then** the tail line reaches its
   floor later; **When** Pre-delay is raised, **Then** the start of the line moves right.
4. **Given** Reverb with audio hitting it, **Then** the measured tail of the latest hit draws as
   a fainter line under the set tail.
5. **Given** Reverb Freeze on, **Then** the display shows the held tail and a frame, as the card
   does.
6. **Given** no knob has moved for about a second, **Then** the top row shows the device and
   page name again.

---

### User Story 3 - Every device gets a sensible display (Priority: P2)

Any device without a display of its own — a plug-in, or a built-in device whose display comes in
a later wave — still shows something useful: eight value bars for the eight knobs, labelled
1–8, with the device and page name on top.

**Why this priority**: the screen never goes blank, and later waves add pictures device by
device without changing the protocol or the phone.

**Independent Test**: open Macros on a plug-in and on a built-in effect without its own
display; the display shows eight bars that follow the knobs.

**Acceptance Scenarios**:

1. **Given** a plug-in on the track, **When** its page shows, **Then** the display shows eight
   bars matching the knob values, empty knobs as empty bars.
2. **Given** a rack, **Then** its macros show as eight bars named by the macros.

---

### User Story 4 - The display travels over the API (Priority: P2)

The picture is not drawn by the phone. Nota renders a small frame for the device page and
sends it to the phone, which only lights the dots. A second client (another phone, a tablet in
landscape, later Nota NX, an MCP tool or a controller with a screen) shows exactly the same
pixels for the same device and page.

**Why this priority**: it is the architectural requirement in the request; without it every
client would re-implement every device view.

**Independent Test**: connect two phones to the same device page; both show identical frames
(same sequence number, same bytes); a page nobody watches renders nothing.

**Acceptance Scenarios**:

1. **Given** two phones on the same device page, **Then** both receive the same frame bytes.
2. **Given** no phone shows Macros, **Then** Nota renders no frames and sends no frame traffic.
3. **Given** a static picture (nothing playing, no knob moving), **Then** no new frames are sent
   after the first.

### Edge Cases

- The focused device is deleted, moved or replaced while it shows: the phone moves to the
  track's default device (instrument, else first device) and its page 1, as Macros does today.
- A track with no devices: the display shows "NO DEVICES" and the knobs are empty; Device ◀ ▶ and
  page buttons are disabled.
- A group track or a return: Macros shows what it shows today (nothing to control) — the
  display shows "NO DEVICES".
- More pages than fit on one row of buttons (plug-ins with many params): the row scrolls
  sideways; the current page stays in view.
- The phone loses the connection: the display keeps the last frame dimmed until it reconnects,
  then receives a full frame.
- A knob is mapped with MIDI Learn on the phone (shows what it drives instead): the display
  still draws the device page; the mapped knob's overlay shows the mapping's name.
- A slow network: frames that cannot be sent in time are dropped, the newest one wins; the
  knobs never wait for frames.
- Phone in landscape or tablet: the display keeps its aspect and scales; it never pushes the
  knobs below the screen edge.

## Requirements *(mandatory)*

### Functional Requirements

**Navigation**

- **FR-001**: The Macros screen MUST offer Track ◀ / ▶ and Device ◀ / ▶ buttons and one button
  per page of the current device.
- **FR-002**: A device's pages on the phone MUST be the controller's pages for that device
  (edited → catalog → plug-in default, e.g. Volt Main · Osc · Filter · LFO; Reverb Main · More),
  so phone and hardware always show the same eight knobs on the same page.
- **FR-003**: The device list on the phone MUST follow the track's chain order (MIDI effects,
  instrument, audio effects), one entry per device, replacing today's one-entry-per-eight-params
  list.
- **FR-004**: With "Follow Nota's selection" on, the phone's track, device and page MUST be the
  controller focus: a press on the phone moves Nota's selection and the controller's focus; a
  move from the controller or from Nota moves the phone.
- **FR-005**: With "Follow Nota's selection" off, navigation MUST stay on the phone only.
- **FR-006**: A navigation button at the end of its range MUST be shown disabled and do nothing.
- **FR-007**: Navigation MUST respect the phone's permission (notes-only phones cannot navigate
  devices, as they cannot turn knobs today).
- **FR-008**: Macros MUST have a **Follow** switch, on by default and kept per phone. On, Macros
  follows the hardware controller — to another track (while Macros shows; opening Macros catches
  up), another device, another page — and the phone's Track / Device / Page buttons move the
  controller. Off, Macros stays where the phone puts it and the controller is left alone.
- **FR-009**: With the phone on its side, the eight knobs MUST sit in one row under the display, the
  steppers, Follow and the pages beside it, so the display takes as much of the screen as it can.
- **FR-017**: The display MUST open full screen (a corner button), with the page steps and the
  device and page name under it, and close back to Macros.

**Display**

- **FR-010**: The Macros screen MUST show a dot-matrix display above the knobs: a fixed grid of
  128 × 48 dots with 16 brightness levels, one colour, dark unlit dots visible.
- **FR-011**: The display's top row MUST show the device and page name, and while a knob turns
  (from the phone, a controller, the mouse or automation the user is recording) the knob's name
  and value for about one second after the last change.
- **FR-012**: Volt MUST draw, per page, the picture that matches the page's controls: filter
  response with cutoff marker over the live output spectrum; amp and filter envelopes;
  oscillator waveform(s); LFO shapes moving at their rate; the mod matrix as a grid; macros as
  bars — whichever apply to the page's parameters.
- **FR-013**: Reverb MUST draw the decay tail in dB (pre-delay start, −60 dB at the decay time,
  early reflection ticks, a fainter high-frequency tail under HF damp), the measured tail of the
  latest hit under it, and on its tone page the decay across frequency with the low and high
  cut marked; Freeze shows the held tail with a frame.
- **FR-014**: A device without its own display MUST show eight value bars for the eight knobs,
  with the device and page name on top.
- **FR-015**: The display MUST react to a parameter change within one display refresh, and
  moving content (spectrum, LFOs, measured tail) MUST refresh at up to 30 frames per second.
- **FR-016**: The display's colour MUST be the track's colour (the Remote rule "values carry the
  track's colour"), on a dark bezel in every Remote theme.

**Delivery**

- **FR-020**: Nota MUST render each frame itself, once per device page being watched, and send
  the frame to every phone that shows that page; the phone MUST only draw the dots it receives.
- **FR-021**: Nota MUST render frames only while at least one phone shows the page, and send a
  frame only when it differs from the last one sent to that phone.
- **FR-022**: Frames MUST be compact enough that a busy animated page stays under 100 KB/s per
  phone.
- **FR-023**: Rendering and reading measurements MUST NOT run on the audio thread or add work to
  it beyond the taps that already exist.
- **FR-024**: A client that does not know the frame message MUST keep working (older phones,
  Nota NX ignore unknown messages).
- **FR-025**: Every built-in device (the 17 instruments, 26 audio effects and 6 MIDI effects
  of the controller catalog) MUST have its own display for each of its pages, a picture that
  explains that page's controls (filter / EQ curves, envelopes, transfer curves, tails, delay
  taps, modulation sweeps, note views); racks and plug-ins show the bars (FR-014).
- **FR-026**: Where a device card draws live data the display needs, the display MUST use the
  same source: an instrument's output spectrum, a dynamics device's gain reduction, Reverb's
  measured tail; a device without a dedicated measurement shows the track's output level where
  its picture moves with the sound.

### Key Entities

- **Device page**: a named set of up to eight parameter slots of one device, the unit the knobs,
  the page buttons and the display work in.
- **Display frame**: a fixed grid of dots with a brightness level each, plus which track,
  device and page it shows and a sequence number.
- **Device display**: for one kind of device, how each of its pages turns parameter values and
  live measurements into a frame; the bars display is the default.
- **Display subscription**: a phone watching one device page; frames exist only while there is
  at least one.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: From the phone alone, a player reaches any page of any device on any track in at
  most (tracks + devices + 1) presses, without opening a sheet.
- **SC-002**: After a knob moves, the display shows the change in under 100 ms on the local
  network.
- **SC-003**: For Volt and Reverb, a user shown the desktop card and the phone display side by
  side for each page matches them correctly every time.
- **SC-004**: An idle Macros screen (nothing playing, nothing moving) sends no frames after the
  first; a busy animated page stays under 100 KB/s per phone.
- **SC-005**: No new audio dropouts with three phones watching animated pages (same glitch count
  as with Macros closed in a 5-minute play-through).
- **SC-006**: Two phones on the same page show byte-identical frames.

## Assumptions

- The phone is portrait on a stand or in hand; landscape and tablets scale the same layout.
- Live pictures use measurements Nota already takes for the desktop cards (instrument output
  tap, effect scope rings, reverb tail history); no new engine measurement is needed for Volt
  and Reverb.
- The display colour is the track colour; an option for amber or white is out of scope.
- MCP access to frames and screens on hardware controllers are later work; the frame format is
  chosen so they can reuse it.
- Nota NX picks the frames up later; this spec changes only the web Remote.
- Macros' existing per-knob behaviour (automation dot, MIDI Learn mapping on the phone, touch
  phases for automation latch) stays as it is.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
