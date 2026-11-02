# Feature Specification: Docked Mixer

**Feature Branch**: `054-docked-mixer`

**Created**: 2026-10-30

**Status**: Draft

**Input**: User description: "Dock the Mixer inside Arrangement / Session / Modular, keep the pop-out window (GitHub issue nota-daw/nota#39). Today the Mixer only opens as a separate floating window (View → Mixer, ⌘⇧M). On a single screen that window covers the arrangement or has to be juggled; on a multi-monitor setup the pop-out is useful and should stay. Let the full Mixer (strips, Stereo map, pan law footer) dock as a panel inside the main window in Arrangement, Session and Modular views — e.g. a bottom panel that can be shown or hidden. Keep the current pop-out window as an option for people with a second monitor, with a way to switch between docked and floating. Session view's own I/O · Sends · Mixer grid sections are out of scope."

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

**Existing behaviour this builds on**: the Mixer (Strips | Stereo map views, pan law
footer) opens only as its own floating window from View → Mixer / ⌘⇧M; the strips/map
choice is remembered per user (specs/008). The main window already has a docked bottom
panel (Devices / Pattern / Clip) that can be popped out into its own window and docked
back — the docked Mixer should feel like a sibling of that pattern.

## Clarifications

### Session 2026-10-30

- Q: How should the docked Mixer coexist with the bottom Devices / Pattern / Clip panel? → A: Its own panel, stacked with it; either, both or neither can be shown (FR-004).
- Q: What should the docked Mixer do when the panel is shorter than a full strip needs? → A: Strips adapt to the height — lower-priority sections collapse, the core controls always stay (FR-014).
- Q: Should the Mixer come back shown after an app restart or when a project opens? → A: Yes, per user — shown/hidden, mode and height are restored from user settings, not the project (FR-009).
- Q: With both bottom panels shown, which sits directly under the main view? → A: The Mixer; the Devices / Clip panel goes under it, at the bottom of the window (FR-004).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Mix without leaving the main window (Priority: P1)

A producer on a single laptop screen is arranging a song and wants to balance levels. They
show the Mixer and it appears docked at the bottom of the main window, under the
arrangement, instead of a window that covers what they are working on. They adjust faders,
pans, mutes and sends while still seeing and editing the arrangement above. When done,
they hide the Mixer and the arrangement gets its space back.

**Why this priority**: This is the core request of the issue — on one screen the floating
window gets in the way. Delivering just this is already a viable improvement.

**Independent Test**: With a project open in Arrangement view, show the Mixer docked;
verify all strips, the Strips | Stereo map switch and the pan law footer are present and
working, the arrangement stays visible and editable above, and hiding the Mixer restores
the previous layout.

**Acceptance Scenarios**:

1. **Given** a project in Arrangement view with the Mixer hidden, **When** the user
   chooses View → Mixer (or presses ⌘⇧M) with the docked mode active, **Then** the full
   Mixer appears as a panel inside the main window and the arrangement remains visible and
   interactive.
2. **Given** the docked Mixer is shown, **When** the user moves a fader, pan, mute, solo,
   or send, **Then** the change takes effect exactly as in the floating Mixer (audible,
   undoable, MIDI-learnable) and meters keep moving.
3. **Given** the docked Mixer is shown, **When** the user switches between Strips and
   Stereo map, **Then** the docked panel shows the chosen view and the choice is
   remembered as today.
4. **Given** the docked Mixer is shown, **When** the user drags the boundary between the
   Mixer and the view above it, **Then** the Mixer resizes, within sensible minimum
   heights for both parts, and the height is remembered.
5. **Given** the docked Mixer is shown, **When** the user chooses View → Mixer / ⌘⇧M again
   or uses the panel's hide control, **Then** the Mixer hides and the view above reclaims
   the space.

---

### User Story 2 - Docked Mixer in every main view (Priority: P2)

A user switches between Arrangement, Session and Modular while mixing. The docked Mixer
stays in place under whichever view is active, so they do not have to reopen it.

**Why this priority**: The issue explicitly names all three views; switching views must
not lose the mixer, but the value of Story 1 exists even in Arrangement alone.

**Independent Test**: Show the docked Mixer, switch to Session and then to Modular;
verify the Mixer stays shown with the same view (Strips / Stereo map), height and scroll
position, and that each main view remains usable above it.

**Acceptance Scenarios**:

1. **Given** the docked Mixer is shown in Arrangement, **When** the user switches to
   Session or Modular, **Then** the Mixer remains docked and shown, with the same view
   and height.
2. **Given** the docked Mixer is shown in Session view, **When** the user looks at the
   Session grid, **Then** Session's own I/O · Sends · Mixer sections are unchanged and
   both stay in sync (a change in one is reflected in the other).

---

### User Story 3 - Switch between docked and floating (Priority: P2)

A user with a second monitor prefers the Mixer on the other screen. From the docked
Mixer they pop it out into the floating window and move that to the second monitor. Later
they dock it back into the main window. The app remembers which mode they used last.

**Why this priority**: The issue requires keeping the pop-out for multi-monitor users;
without a switch, docking would be a regression for them.

**Independent Test**: Pop the docked Mixer out, verify the floating window shows the
same Mixer and the docked panel disappears; dock it back from the window and verify the
reverse; restart the app and verify View → Mixer opens it in the last-used mode.

**Acceptance Scenarios**:

1. **Given** the docked Mixer is shown, **When** the user activates its pop-out control,
   **Then** the Mixer moves into the floating window (same state, meters live) and the
   docked panel is removed from the main window.
2. **Given** the floating Mixer window is open, **When** the user activates its dock
   control, **Then** the window closes and the Mixer appears docked in the main window.
3. **Given** the user last used the floating mode, **When** they later choose View →
   Mixer (also after restarting the app), **Then** the Mixer opens floating; likewise for
   docked.
4. **Given** the floating Mixer window is open, **When** the user closes it normally,
   **Then** the Mixer is hidden (not re-docked) and the floating mode stays the
   remembered mode.

---

### Edge Cases

- The main window is too short to fit both the view and the Mixer at their minimum
  heights: the Mixer is capped so the view above keeps its minimum height; a remembered
  height larger than fits is clamped, not discarded.
- The bottom Devices / Clip panel is also shown when the Mixer is docked: both stack
  (FR-004); if the window is too short for the main view, the Mixer and the Devices /
  Clip panel at their minimum heights, the panels shrink to their minimums first and the
  main view keeps its minimum height.
- The Devices / Clip panel is popped out into its own window while the Mixer is docked
  (or the other way round): each panel's docked/floating state is independent.
- The user drags the docked Mixer very low: strip sections collapse progressively
  (FR-014) and the drag stops at the height where the strip core still fits; dragging
  back up restores the sections, including any the user had expanded by hand.
- A project with many tracks: the docked Mixer scrolls horizontally like the floating
  one; it never forces the main window wider.
- A project is closed or another project is opened while the Mixer is docked: the panel
  stays shown and reflects the new project's tracks.
- Escape pressed while the docked Mixer has focus: it does not hide the docked Mixer (in
  the main window Escape keeps its existing meanings); in the floating window Escape still
  closes it as today.
- Transport keys (Space / Return) and other global shortcuts work while a control in the
  docked Mixer has focus, as they do in the floating window.
- The command palette opened while focus is in the docked Mixer behaves as it does from
  the main window.
- UI scale and colour theme changes apply to the docked Mixer live, as to the rest of the
  main window.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The app MUST offer a docked Mixer: the full Mixer (all channel strips,
  returns and master, the Strips | Stereo map switch, the pan law footer) shown as a panel
  at the bottom of the main window.
- **FR-002**: The docked Mixer MUST be available in Arrangement, Session and Modular
  views, and its shown/hidden state, view (Strips / Stereo map) and height MUST carry over
  when the user switches between those views.
- **FR-003**: View → Mixer and its shortcut (⌘⇧M, Ctrl+Shift+M elsewhere) MUST toggle the
  Mixer in the current mode (docked or floating); the menu item and the command palette
  entry keep working in both modes.
- **FR-004**: The docked Mixer MUST be its own panel, independent of the existing bottom
  Devices / Pattern / Clip panel: either, both or neither can be shown. When both are
  shown they stack at the bottom of the main window — the Mixer directly under the main
  view, the Devices / Clip panel under the Mixer — each with its own draggable boundary
  and its own remembered height. Showing or hiding one MUST NOT change the other's
  visibility, tab, or (where space allows) height.
- **FR-005**: The user MUST be able to resize the docked Mixer by dragging its top
  boundary; the height MUST respect minimum heights for the Mixer and the view above and
  MUST be remembered across sessions.
- **FR-006**: The docked Mixer MUST provide a control to hide it and a control to pop it
  out into the floating window.
- **FR-007**: The floating Mixer window MUST keep its current behaviour (own window,
  movable to another monitor, Strips | Stereo map in its title bar, Escape closes it) and
  MUST gain a control to dock it back into the main window.
- **FR-008**: The Mixer MUST exist in only one place at a time; moving between docked and
  floating MUST keep its state (view, scroll position, meters running, MIDI-learn
  mappings, selection) without a visible rebuild delay.
- **FR-009**: The app MUST remember, per user and across restarts, the last-used mode
  (docked or floating), the docked height, and whether the Mixer was shown. On app start
  the Mixer is restored in that state (shown docked, shown floating, or hidden); opening
  another project does not change it. On first use after this change the default is
  docked and hidden.
- **FR-010**: Every mixer interaction available in the floating Mixer (faders, pan, mute,
  solo, arm, sends, routing, MIDI learn, context menus, typed value entry, undo) MUST work
  identically in the docked Mixer.
- **FR-011**: Global keys (transport, undo/redo, view switching, command palette) MUST
  keep working while focus is inside the docked Mixer.
- **FR-012**: Session view's own I/O · Sends · Mixer grid sections MUST remain unchanged
  and stay in sync with the docked Mixer.
- **FR-014**: Channel strips MUST adapt to the available height, in both docked and
  floating modes. The core of a strip — name, meter, fader, pan, mute / solo / arm — is
  always shown. As height shrinks, the other sections (I/O routing, sends, device
  slots, and any similar extra rows) collapse in a fixed priority order; a collapsed
  section stays reachable (e.g. by expanding it or through the strip's menu), and the
  sections come back as height grows. Below the height at which the core fits, the
  panel cannot be made smaller. The Stereo map and pan law footer scale to the height
  the same way and stay usable at the minimum height.
- **FR-013**: The docked panel MUST follow the Studio design language (docked panel on a
  seam, sentence-case labels, accent only for what is live or chosen), matching the
  existing bottom Devices / Clip panel.

### Key Entities

- **Mixer placement preference** (per user): last-used mode (docked / floating), docked
  height, shown / hidden, and — as today — the Strips / Stereo map view. Stored in the
  user's settings, not in the project file.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On a single screen, a user can show the Mixer, adjust a track's level, and
  continue editing the arrangement without moving or closing any window — zero window
  management steps (previously at least one: move or close the floating window).
- **SC-002**: Switching between docked and floating takes one action each way and
  completes with no perceptible delay (under 0.2 s), with meters running throughout.
- **SC-003**: Switching between Arrangement, Session and Modular with the docked Mixer
  shown preserves its view and height in 100% of switches.
- **SC-004**: After an app restart, the Mixer comes back shown or hidden, docked or
  floating, at the height last used, in 100% of cases.
- **SC-005**: All mixer interactions covered by the floating Mixer pass the same manual
  checks in the docked Mixer (no feature missing in either mode).
- **SC-006**: Audio performance is unchanged: showing the docked Mixer adds no audio
  dropouts compared with showing the floating one.

## Assumptions

- The Mixer content itself (strips, Stereo map, footer) is reused — the same controls
  and behaviour in both modes; the only change inside it is the height-adaptive strip
  layout (FR-014).
- The shortcut is ⌘⇧M (the issue text says ⌘M; the app binds ⌘⇧M today) — no new
  shortcut is added; pop-out / dock are reachable from controls on the panel/window and
  from the command palette.
- Default mode for existing and new users is docked, since that is what the issue asks
  for; multi-monitor users switch once and the choice is remembered.
- Shown/hidden is one state for the whole main window, not separate per view.
- No engine or C ABI change is required; this is a presentation-layer change.
- Out of scope: Session view's I/O · Sends · Mixer grid sections, docking the Mixer to
  left/right sides, multiple Mixer instances, tabbing the Mixer into other windows.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI-only re-hosting, no engine change
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no new crossings
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine behaviour change
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
