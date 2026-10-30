# Feature Specification: Track headers on the right

**Feature Branch**: `065-track-headers-right`

**Created**: 2026-10-30

**Status**: Draft

**Input**: User description: "Option to put the track headers on the right side of the arrangement (GitHub issue nota-daw/nota#72). Have the track list (headers with name, mute/solo/arm, fader…) on the right of the arrangement instead of the left, as in Ableton Live, so the timeline starts at the left edge next to the browser. A setting (Preferences → View, or a View menu toggle): Track headers: Left / Right. With Right, the header column, its footer (returns / master) and the related overlays (drop ring, insertion line) move to the right edge; the lanes keep scrolling as today. Today the header column is always on the left: ArrangementView lays out a fixed-width (HeaderW = 228) header canvas before the lanes. Related: resizable track height and header width (#52)."

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

**Existing behaviour this builds on**: the arrangement shows a resizable header column
(specs/015) on the left of the lanes, with header-column chrome above it (Overview,
Sections and Zoom labels), a pinned return/master footer under it (specs/045) and a
spacer next to the horizontal scrollbar. Arrangement view options already persist per user
(View ▸ Toggle sections, Clip names; Settings → Appearance → Waveform).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Put the track headers on the right (Priority: P1)

A producer coming from Ableton Live wants the timeline to start at the left edge, next to
the browser, with the track headers on the right. They switch the setting to Right and
the whole header column — track headers, the return/master headers in the footer and the
header-column chrome above and below — moves to the right edge. Everything works as
before: selecting, reordering, renaming, mute/solo/arm, faders, row and column resizing,
dropping from the browser.

**Why this priority**: it is the whole request.

**Independent Test**: switch the setting to Right with a project of several tracks and
returns; check the column sits on the right edge, the lanes start at the left edge, and
every header gesture still works.

**Acceptance Scenarios**:

1. **Given** the headers on the left, **When** the user chooses Right, **Then** the
   header column, the footer headers and the chrome strips move to the right edge at once,
   with no restart, and the lanes start at the left edge of the arrangement.
2. **Given** the headers on the right, **When** the user drags a header to reorder,
   **Then** the insertion line shows in the header column on the right and the drop lands
   in the gap shown.
3. **Given** the headers on the right, **When** the user drags a browser item over a
   track or return header, **Then** the drop ring shows over that header on the right.
4. **Given** the headers on the right, **When** the user drags the seam between lanes
   and headers, **Then** the edge follows the pointer (dragging left widens the column)
   within the same limits as today, and a double-click resets the width.
5. **Given** the headers on the right, **When** the user scrolls, zooms or plays,
   **Then** the lanes behave as on the left (ruler, overview, sections and playhead stay
   aligned with the lanes).

---

### User Story 2 - The choice sticks and is easy to reach (Priority: P2)

The user sets the side once and expects it to stay: across restarts and in every project.
They can reach it from the View menu (and the command palette) as well as from Settings.

**Why this priority**: a layout preference that resets is useless, but it is secondary
to the layout working.

**Independent Test**: switch to Right from the View menu, restart the app, open another
project; the headers are still on the right and Settings shows Right.

**Acceptance Scenarios**:

1. **Given** Right chosen, **When** the app restarts or another project opens,
   **Then** the headers are on the right.
2. **Given** the View menu, **When** the user picks "Track headers on the right",
   **Then** the side flips, the menu item shows a check when on the right, and Settings →
   Appearance shows the same choice.
3. **Given** Settings → Appearance, **When** the user picks Left or Right under Track
   headers, **Then** the arrangement follows at once and the View menu check agrees.

### Edge Cases

- A narrow window still squeezes the header column to its minimum before the lanes drop
  under their minimum, on either side.
- Header width and row heights saved with a project are unaffected by the side; a project
  opens with its saved width on whichever side the user prefers.
- With no tracks, the empty header column backdrop and its divider are on the chosen side.
- The divider between headers and lanes is always on the edge that faces the lanes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to choose the side of the arrangement's track-header
  column: Left (default) or Right.
- **FR-002**: With Right, the track header column, the return/master footer headers, the
  header-column chrome strips (Overview, Sections, Zoom labels and the spacer by the
  horizontal scrollbar) and the reorder insertion line and browser drop rings MUST sit on
  the right edge; the lanes, ruler, overview, sections lane and scrollbar MUST fill the
  rest from the left edge.
- **FR-003**: Every header and lane gesture available on the left MUST work the same on
  the right (select, multi-select, reorder, rename, context menus, mute/solo/arm, faders,
  pan, row resize, header-column resize, browser drops, keyboard routing to the headers).
- **FR-004**: The header/lane divider and the column-resize seam MUST be on the edge that
  faces the lanes; dragging the seam moves that edge with the pointer, within the existing
  width limits.
- **FR-005**: Changing the side MUST apply immediately, without restarting or reopening
  the project.
- **FR-006**: The choice MUST persist per user across restarts and projects.
- **FR-007**: The choice MUST be reachable from Settings → Appearance (Track headers:
  Left / Right) and from a View menu toggle that is also in the command palette; both
  MUST reflect the current state.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Switching sides takes one action from the View menu or Settings and is
  visible immediately.
- **SC-002**: With headers on the right, all header gestures listed in FR-003 succeed in
  a manual pass, with no misplaced overlays.
- **SC-003**: The chosen side survives 100 % of restarts and project switches.
- **SC-004**: With the default (Left), the arrangement looks and behaves exactly as
  before.

## Assumptions

- The side is a per-user view preference (like the waveform style), not saved in the
  project; header width and row heights stay per project (specs/015).
- The contents of each header card are not mirrored — only the column moves (Ableton's
  right-side headers read left to right too).
- The Session and Mixer views are out of scope.
