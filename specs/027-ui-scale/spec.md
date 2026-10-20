# Feature Specification: App-wide Interface Scale

**Feature Branch**: `027-ui-scale`

**Created**: 2026-10-19

**Status**: Draft

**Input**: User description: "App-wide UI scale setting in Preferences (GitHub issue nota-daw/nota#38: "UI scaling / font size setting — text too small to read on a 17" laptop"). Add a global interface scale with presets 75 / 100 / 125 / 150 / 200 % (75% added on top of the issue's list, for users who want more content on small/low-res screens). Currently Nota has no app-wide scale: only the timeline, piano roll and audio editor zoom their own content. The setting persists across launches and applies to the whole UI."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Source**: GitHub issue [nota-daw/nota#38](https://github.com/nota-daw/nota/issues/38)
(labels: accessibility, enhancement). A user on a 17" laptop finds the whole UI small
and some text unreadable. Exact resolution and OS display scaling were not reported.

**Current state (for reference)**:

- Nota has no app-wide scale. Everything is drawn at one fixed size, multiplied only
  by the operating system's display scaling.
- Only the timeline (arrangement), the piano roll and the audio editor zoom — and they
  zoom their *content* (time and pitch), not the interface around it.
- Preferences → Appearance has a **UI** section with *Colour theme* and *Waveform*.
  This feature adds its third row there.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Make the whole interface bigger (Priority: P1)

A producer on a 17" laptop finds Nota's text too small to read. They open
Preferences → Appearance and set **Interface scale** to 125 %. Every window — main
window, Preferences, device detail windows, dialogs, menus, tooltips — redraws larger
at once: text, buttons, knobs, track headers, device cards, spacing. Nothing is cut
off and nothing overlaps. They quit and relaunch; Nota opens at 125 %.

**Why this priority**: This is the issue. It makes Nota usable for this user.

**Independent Test**: With a project open, switch the scale to 150 % and check that
every surface grows in proportion and stays usable. Relaunch and confirm the scale
is kept.

**Acceptance Scenarios**:

1. **Given** Nota at 100 %, **When** the user picks 125 % in Preferences → Appearance,
   **Then** every open Nota window redraws at 1.25× size without a restart, and the
   project, playback and selection are untouched.
2. **Given** Nota at 150 %, **When** the user quits and relaunches, **Then** Nota opens
   at 150 % from the first frame of the main window.
3. **Given** Nota at 200 %, **When** the user opens a menu, a context menu, a tooltip
   or a dialog, **Then** it appears at 200 % too.
4. **Given** Nota at 200 % on a window smaller than its content, **When** the user
   works normally, **Then** panels that already scroll or fold keep doing so and every
   control can still be reached.

---

### User Story 2 - Fit more on a small screen (Priority: P2)

A producer on a low-resolution or small screen wants to see more tracks and more of
the device chain at once. They set the scale to **75 %**. Everything draws smaller,
so more fits in the same window, and text stays sharp.

**Why this priority**: Added on top of the issue at the maintainer's request; the
same control serves both directions, so it costs little.

**Independent Test**: Set 75 %, compare how many tracks and device cards fit in the
same window size with 100 %.

**Acceptance Scenarios**:

1. **Given** Nota at 100 %, **When** the user picks 75 %, **Then** every window
   redraws at 0.75× and more content fits on screen.
2. **Given** Nota at 75 %, **When** the user hovers or clicks any control, **Then**
   it responds exactly where it is drawn.

---

### User Story 3 - Content zoom still works on top (Priority: P3)

The user has the interface at 150 % and zooms the arrangement and the piano roll as
before. The content zoom works the same; the interface scale multiplies it.

**Why this priority**: Existing behaviour that must not break.

**Independent Test**: At 150 %, zoom the arrangement, piano roll and audio editor in
and out; zoom level and the point under the cursor behave as at 100 %.

**Acceptance Scenarios**:

1. **Given** the interface at 150 %, **When** the user zooms the arrangement with the
   usual gesture, **Then** it zooms around the same point as at 100 %.
2. **Given** a project saved at 100 %, **When** it is opened at 200 %, **Then** its
   content zoom levels are the same; only the interface is larger.

### Edge Cases

- **Main window bigger than the screen after scaling up**: the window is kept within
  the screen it is on (resized to fit); nothing ends up off-screen. Open windows keep
  their on-screen size when the scale changes; their contents reflow.
- **Scale too large for the screen** (e.g. 200 % on a 1440 × 900 screen): the window's
  minimum size is capped at the screen, so the window still fits; parts of the layout
  may then be tighter than designed. The setting stays available — the user chose it.
- **Remembered window sizes and panel widths** (track list, browser, detail windows):
  they keep their *logical* size, so at 200 % a remembered panel is twice as wide on
  screen. If that no longer fits, the existing minimum/maximum rules apply.
- **Third-party plugin editor windows** are drawn by the plugin and are not scaled by
  this setting.
- **Nota Remote** (the phone web UI) is not affected.
- **Exported images, audio and project files** do not depend on the scale.
- **OS display scaling** keeps working; Nota's scale multiplies it (150 % OS × 125 %
  Nota = 187.5 % overall).
- **Settings file with an unknown or invalid scale value** (edited by hand, or from a
  newer version): Nota uses 100 %.
- **Splash screen** shows before settings load and may stay at 100 %.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Preferences → Appearance → UI MUST offer an **Interface scale** setting
  with exactly these presets: 75 %, 100 %, 125 %, 150 %, 200 %. The default is 100 %.
- **FR-002**: Changing the scale MUST apply at once to every open Nota window, without
  a restart and without changing the project, transport or selection.
- **FR-003**: The scale MUST apply to the whole Nota interface: text, icons, controls,
  custom-drawn views (arrangement, mixer, piano roll, device cards, meters, graphs),
  spacing and hit areas; menus, context menus, tooltips, popups and dialogs; windows
  opened after the change.
- **FR-004**: Everything MUST stay in proportion — no clipped text, no controls
  overlapping, and pointer hits landing where controls are drawn — at every preset.
- **FR-005**: The scale MUST persist across launches and be applied before the main
  window first appears.
- **FR-006**: The existing content zooms (arrangement, piano roll, audio editor) MUST
  keep working unchanged on top of the interface scale.
- **FR-007**: Text and lines MUST stay sharp at every preset (drawn at the final size,
  not stretched from a bitmap).
- **FR-008**: An unknown or invalid stored value MUST fall back to 100 %.
- **FR-009**: The setting MUST NOT affect audio, the engine, project files, exports,
  Nota Remote or third-party plugin editors.

### Key Entities

- **Interface scale**: one app-wide user preference; one of 75 / 100 / 125 / 150 /
  200 %. Stored with the other Appearance preferences, not in the project.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: At 125 % the smallest text in the main window is 1.25× its 100 % size;
  at 200 % it is 2×; at 75 % it is 0.75×.
- **SC-002**: Switching the scale takes effect in under 1 second, with no restart.
- **SC-003**: At every preset whose scaled minimum window size fits the screen (on a
  1920 × 1080 screen: 75–150 %), the user can reach and operate every control in the
  main window, Preferences and a device detail window — none is clipped beyond reach.
  Larger presets on a smaller screen keep the window on screen and every menu usable.
- **SC-004**: The chosen scale is the one in use after 10 out of 10 relaunches.
- **SC-005**: Audio and CPU load do not change when the scale changes.

## Assumptions

- A single global scale answers the issue; a separate font-size setting is out of
  scope (the issue says "and/or"; global scale grows text and controls together and
  keeps layouts balanced).
- Presets only — no free slider or custom value; the five presets cover the reported
  need and keep the layouts testable.
- No keyboard shortcut for the scale in this feature (Cmd/Ctrl +/- are left to the
  content zooms); it can be added later through the shortcut system.
- The control follows the existing picker style in the Appearance pane (DESIGN.md,
  Studio).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
