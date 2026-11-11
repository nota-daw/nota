# Feature Specification: Settings reorganisation — every setting where you'd look for it

**Feature Branch**: `feat/settings-reorganisation`

**Created**: 2026-11-10

**Status**: Draft

**Input**: User description: "Settings reorganisation (issue #427, approved; scope widened in the issue on 2026-11-10). Reorganise the Preferences/Settings window sidebar so every setting lives where a user would look for it. Behaviour of every setting stays exactly the same — only where it lives changes. New sidebar: Devices group → Audio, Outputs (new), Inputs (new), MIDI, Gamepads. New "Remote control" group → Remote, AI control (MCP) (new page). Plug-ins group unchanged. General group → Appearance (the look only), Language (new), Editing (new), Library, Shortcuts, Updates. With ASIO one driver is both input and output. Outputs gets no per-channel naming/routing. Pages are selected by stable Id rather than position. EN + RU strings; nota-docs (EN + RU) and Settings screenshots updated."

**Issue**: nota-daw/nota#427 — "Settings: reorganise the pages — Inputs/Outputs, MCP, Language
and Editing get their own" (approved). Originally asked to split Appearance (MCP and Piano roll
out); on 2026-11-10 the issue was widened to the whole sidebar, with the table below.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Moves existing sections only: the Inputs list (spec 086), the colour theme and skins (specs
011, 087), the accent (spec 084), the MCP server switch, the piano-roll labels, split fades
and the detail-panel switch (#308). No engine change, no new setting, no stored-setting change.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md),
[`../086-multi-input-recording/spec.md`](../086-multi-input-recording/spec.md).

## The new sidebar

| Group | Page | Holds | Was |
|---|---|---|---|
| **Devices** | Audio | Audio system (Windows: WASAPI / ASIO, ASIO driver and its panel), sample rate, buffer size, exclusive mode (Windows), latency, CPU check | Audio |
| | **Outputs** *(new)* | Output device; test tone | Audio |
| | **Inputs** *(new)* | Input device; every input with its switch and name; stereo pairs | Audio |
| | MIDI | unchanged | — |
| | Gamepads | unchanged | — |
| **Remote control** *(new group)* | Remote | unchanged | Devices |
| | **AI control (MCP)** *(new)* | Enable, port, status, copy config | Appearance |
| **Plug-ins** | Plug-ins, Downloads | unchanged | — |
| **General** | Appearance | Colour theme (with skins), accent, interface scale, text size, waveform style, track-header side | Appearance |
| | **Language** *(new)* | Interface language | Appearance |
| | **Editing** *(new)* | Piano roll: label every note · Arrangement: fade audio clips when splitting · Detail panel: open on select | Appearance |
| | Library, Shortcuts, Updates | unchanged | — |

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Find a setting on the page its name suggests (Priority: P1)

A user wants to turn on the MCP server, switch the interface language, or make the piano roll
label every note. They open Settings and find each one on a page whose name says what it is —
"AI control (MCP)", "Language", "Editing" — instead of under "Appearance".

**Why this priority**: This is the reason for the issue: settings that aren't about the look
are hidden under Appearance, and that page only grows.

**Independent Test**: Open Settings; the sidebar shows the groups and pages of the table above in
that order; each moved setting is on its new page, and Appearance holds only the look.

**Acceptance Scenarios**:

1. **Given** Settings is open, **When** the user reads the sidebar, **Then** it shows Devices
   (Audio, Outputs, Inputs, MIDI, Gamepads), Remote control (Remote, AI control (MCP)),
   Plug-ins (Plug-ins, Downloads), General (Appearance, Language, Editing, Library, Shortcuts,
   Updates), each page with its icon and subtitle.
2. **Given** the AI control (MCP) page, **When** the user switches the server on, edits the
   port and presses Copy config, **Then** the server starts, the status line, dimming and the
   copied JSON behave exactly as they did on Appearance.
3. **Given** the Editing page, **When** the user toggles "Label every note", "Fade audio clips
   when splitting" or "Open the detail panel", **Then** the piano roll, splitting and the detail
   panel react exactly as before, and the choice survives a restart.
4. **Given** the Language page, **When** the user picks another language, **Then** the same
   restart-to-apply behaviour as before follows.
5. **Given** the Appearance page, **When** the user reads it, **Then** it shows only colour
   theme (with skins), accent, interface scale, text size, waveform style and track-header
   side, and its subtitle no longer mentions AI control.

---

### User Story 2 - Set up inputs and outputs on their own pages (Priority: P1)

A user plugging in an interface picks the output device and plays the test tone on Outputs,
then picks the input device and switches on and names the inputs on Inputs, without scrolling
past sample rate, buffer and CPU check.

**Why this priority**: The Audio page carries device, format, the per-input list (which can be
dozens of rows on a large interface) and the tests on one page; inputs and outputs are what a
user comes for most often.

**Independent Test**: With a multi-input interface, choose output and input devices on their
pages; the Inputs list follows the input device; the Audio page shows format, latency and CPU
check only.

**Acceptance Scenarios**:

1. **Given** the Outputs page (WASAPI / CoreAudio / Linux), **When** the user picks an output
   device, **Then** the engine switches to it as before, and the test tone plays on it.
2. **Given** the Inputs page, **When** the user picks another input device, **Then** the input
   list below rebuilds for that device, with the names and switches kept for it (spec 086).
3. **Given** the Audio page, **When** the user changes sample rate or buffer size, **Then**
   latency updates there as before, and nothing about devices or inputs is on that page.
4. **Given** Windows with ASIO chosen on the Audio page, **When** the user opens Outputs or
   Inputs, **Then** the page says the ASIO driver on the Audio page is both input and output
   (with a way to go there), Outputs still offers the test tone, and Inputs still lists the
   driver's inputs with their switches and names.
5. **Given** no input device (or one with no inputs), **When** the user opens Inputs, **Then**
   it shows the same "no inputs" caption as today's Inputs section.

---

### User Story 3 - Everything that opens a Settings page still lands on the right one (Priority: P2)

Commands and links that open Settings on a page — the command palette's "Settings: …"
entries, the audio-format warning, Downloads, the skin preview — open the page that now holds
what they point at.

**Why this priority**: Without it, the move breaks existing entry points silently (wrong page,
or nothing).

**Independent Test**: Run each palette Settings command and each in-app link; each opens the
expected page.

**Acceptance Scenarios**:

1. **Given** the command palette, **When** the user runs "Settings: Appearance", "Settings:
   Audio" … **Then** each opens its page; new commands "Settings: Outputs", "Settings: Inputs",
   "Settings: AI control (MCP)", "Settings: Language", "Settings: Editing" open theirs, and
   searching "language", "mcp", "piano roll", "input" finds the page that now holds it.
2. **Given** the Downloads badge and "install pack" links, **When** used, **Then** the Downloads
   page opens on the requested source and its badge sits on the Downloads item, as before.
3. **Given** a remote-pairing request or an update, **When** Nota asks to show Remote or
   Updates, **Then** the right page opens even though its position in the list changed.

---

### Edge Cases

- A Settings page asked for by an id that no longer exists (an old caller) → nothing breaks;
  the window opens on its first page.
- Audio device changes while Settings is open (hot-plug, device lost): Outputs and Inputs
  refresh their device lists and the Inputs list the way Audio does today.
- Switching between Audio, Outputs and Inputs while a test tone or CPU check runs: they stop
  on leaving their page, as today on leaving Audio.
- Interface scale / text size / language changes rebuild the window: the page the user was on
  stays selected.
- Russian titles and subtitles fit the sidebar width without clipping.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The Settings sidebar MUST show the groups and pages of "The new sidebar" table,
  in that order, with titles and subtitles in English and Russian.
- **FR-002**: Every setting MUST keep its current behaviour, stored value and default; only its
  page changes. No setting is added or removed.
- **FR-003**: Outputs MUST hold the output-device picker and the test tone; Inputs MUST hold the
  input-device picker and the Inputs list of spec 086 (inputs, switches, names, stereo pairs);
  Audio MUST keep the audio system (Windows), sample rate, buffer size, exclusive mode
  (Windows), latency and CPU check.
- **FR-004**: When ASIO is the audio system, Outputs and Inputs MUST explain that the ASIO
  driver chosen on Audio is the device for both, offer a way to go to Audio, and keep the test
  tone (Outputs) and the input list (Inputs). The ASIO output pair moves to Outputs and the
  ASIO input pair to Inputs; the driver picker and its panel stay on Audio.
- **FR-005**: AI control (MCP) MUST be its own page in the new Remote control group, next to
  Remote, holding the MCP switch, port, status and Copy config as they are now.
- **FR-006**: Language MUST be its own page in General, holding the language picker and its
  caption.
- **FR-007**: Editing MUST be its own page in General with three sections: Piano roll (label
  every note), Arrangement (fade audio clips when splitting), Detail panel (open on select).
- **FR-008**: Appearance MUST hold only colour theme (with skins), accent, interface scale, text
  size, waveform style and track-header side, with a subtitle that matches.
- **FR-009**: Pages MUST be chosen by a stable id, not by their position: every caller that
  opens a page (command palette, audio-format menu, the track input menu's "Audio I/O setup"
  → Inputs, Downloads and its badge, Remote, Updates, skin preview) MUST open the page that
  now holds its content, and text that names a Settings page ("Choose a device in Settings →
  Audio" → Outputs) MUST name the new one.
- **FR-010**: The command palette MUST offer a "Settings: …" command for every page, including
  the five new ones, with search keywords that follow the moved settings.
- **FR-011**: The user documentation (nota-docs, EN + RU) and the Settings screenshots MUST
  show the new pages; CHANGELOG `[Unreleased]` and FEATURES.md MUST note the move.

### Key Entities

- **Settings page**: a stable id (what callers ask for), a title, a subtitle, an icon, the group
  it belongs to, and the sections it shows.
- **Settings group**: a heading in the sidebar and its pages in order.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Each of the moved settings (MCP, language, piano-roll labels, split fades,
  detail panel, output device, input device, input list) is reached in two clicks from opening
  Settings: the page named for it, then the control.
- **SC-002**: Appearance holds 6 settings instead of 11; Audio no longer contains device
  pickers or the input list.
- **SC-003**: 100% of in-app entry points into Settings (palette commands and links) open the
  page that holds their content.
- **SC-004**: Every setting keeps its stored value across the update — no user has to set
  anything again.

## Assumptions

- Outputs has no per-channel naming or routing: that needs engine work and is a separate
  feature; the page starts with the device and the test tone.
- With ASIO the driver picker stays on Audio (it chooses the audio system's device as a whole);
  Outputs / Inputs point there instead of duplicating it.
- The velocity wheel stays on Shortcuts (it documents a gesture); the Shortcuts page is not
  reorganised here.
- Remote and the MCP server both are "something outside Nota controls it", hence their own
  group; the group is called "Remote control" ("Удалённое управление").
- The new pages reuse existing sidebar icons where one fits; a missing icon is drawn in the
  existing glyph style.
- Skins (spec 087) land first; this branch sits on top of `feat/user-skins` and is rebased onto
  `develop` once skins merge.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: none — UI-only move; engine calls stay the same
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: no engine behaviour change → no smoke-test change
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned, FEATURES.md and nota-docs (EN + RU) via the nota-features / nota-docs skills
- [x] UI change → follows `DESIGN.md` (Studio design language)
