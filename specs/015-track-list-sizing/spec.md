# Feature Specification: Resizable track height and track-list width

**Feature Branch**: `015-track-list-sizing`

**Created**: 2026-10-15

**Status**: Draft

**Input**: User description: "Arrangement: resizable track height and track-list width (GitHub issue nota-daw/nota#52). Users want to fit the track list to the project: see many tracks at once on a big song, or more controls per track on a small one. Requested: (1) Track height — resize individual tracks by dragging the bottom edge of a track, and resize all tracks at once. (2) Track list width — drag the edge between the track headers and the lanes. (3) Track headers that show more or less depending on size: compact when small (name, mute, solo, arm), more controls when large; and/or toggles for which controls are visible. Today both are fixed: RowHeight = 64 and HeaderW = 228 in ArrangementView.cs. Reported by a user."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The arrangement view lives in `src/managed/Nota.App/ArrangementView.cs`. Saving the
sizes with the project may touch the project file (wherever per-track view state such
as colour or group collapse is stored today) — the plan decides where.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## Clarifications

### Session 2026-10-15

- Q: Does this feature include toggles for which header controls are visible, or only headers that adapt to their height? → A: Only size-adaptive headers; visibility toggles are a separate, later issue.
- Q: Where are track heights and the track-list width stored — in the project file or in app preferences? → A: Both in the project file.
- Q: Does a dragged track height change continuously or jump between fixed sizes? → A: Continuously, with a light snap to the compact / standard / large preset heights.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Resize a track's height (Priority: P1)

A producer working on a dense song wants to see the vocal track's waveform big while
keeping the twenty drum and FX tracks short. They hover the bottom edge of the vocal
track's row (header or lane), the cursor changes to a vertical-resize cursor, and they
drag down; the row grows, its clips grow with it, and every row below moves down. They
then shrink the hi-hat track by dragging its bottom edge up.

**Why this priority**: This is the core of the request and delivers value alone — the
user can fit many tracks on screen or enlarge the one they are editing.

**Independent Test**: Open a project with several tracks, drag one track's bottom edge
up and down, and confirm only that row changes height, clips and automation in its lane
follow the new height, and rows below shift accordingly.

**Acceptance Scenarios**:

1. **Given** a project with tracks at the default height, **When** the user drags the bottom edge of one track's row down by 40 px, **Then** that row is 40 px taller, the rows below move down by 40 px, and no other row changes height.
2. **Given** a track row, **When** the user drags its bottom edge past the minimum or maximum height, **Then** the row stops at the limit and does not grow or shrink further.
3. **Given** a resized track, **When** the user double-clicks its bottom edge, **Then** the row returns to the default height.
4. **Given** several tracks are multi-selected, **When** the user drags the bottom edge of one of them, **Then** every selected track takes the same new height.
5. **Given** a resized track, **When** the user saves, closes and reopens the project, **Then** the track has the height it had when saved.

---

### User Story 2 - Resize all tracks at once (Priority: P1)

A user with a 40-track project wants an overview of the whole song. With one gesture they
make every track short; later they make them all tall again to mix.

**Why this priority**: The issue asks for it explicitly, and resizing forty tracks one by
one is not a viable alternative.

**Independent Test**: In a project with mixed track heights, apply the "all tracks"
resize and confirm every track row ends up at the same height.

**Acceptance Scenarios**:

1. **Given** tracks of various heights, **When** the user holds Alt (Option on macOS) while dragging any track's bottom edge, **Then** every track row takes the new height together, live during the drag.
2. **Given** any project, **When** the user picks a size preset (compact / standard / large) for all tracks from the arrangement's track context menu, **Then** every track row takes that preset's height.
3. **Given** the "all tracks" resize was applied, **When** it finishes, **Then** group rows keep their slim title-bar look and the return/master rows are unchanged.

---

### User Story 3 - Resize the track-list width (Priority: P2)

A user with long track names, or one who wants more room for the lanes on a small laptop
screen, drags the vertical seam between the track headers and the lanes left or right.

**Why this priority**: Requested in the issue and independent of height, but less
impactful than height: the default width already fits all current controls.

**Independent Test**: Drag the header/lane seam and confirm all headers (including group,
return and master rows) widen or narrow together, and the lane area takes the remaining space.

**Acceptance Scenarios**:

1. **Given** the default layout, **When** the user hovers the seam between the headers and the lanes, **Then** a horizontal-resize cursor appears; **When** they drag it, **Then** every header column (tracks, groups, returns, master, and the toolbar strip above them) takes the new width and the lanes area adjusts.
2. **Given** the seam is dragged past the minimum or maximum width, **Then** the width stops at the limit.
3. **Given** a narrowed track list, **When** a track name no longer fits, **Then** it is ellipsised as today, and controls that do not fit are dropped rather than overlapping.
4. **Given** the user double-clicks the seam, **Then** the width returns to the default.
5. **Given** a changed width, **When** the project is saved and reopened, **Then** the width is restored.

---

### User Story 4 - Headers that adapt to their size (Priority: P2)

When a track is short, its header shows only what fits on one line — the name and the
mute, solo and record-arm buttons. At the default height it looks as it does today. When
a track is tall, the header has room for more and uses it.

**Why this priority**: Without it, short rows would clip controls into an unusable state;
height resizing (US1/US2) depends on it to look right at small sizes.

**Independent Test**: Resize a track through the size range and confirm the header switches
between the compact, standard and large layouts at the documented thresholds, with no
control ever cut off or overlapping.

**Acceptance Scenarios**:

1. **Given** a track resized below 56 px (compact tier), **Then** its header shows the colour spine, name, type tag (if it fits), mute, solo and (where applicable) record-arm, on one line; the volume/pan row and input selector are hidden; the level meter remains visible.
2. **Given** a track in the standard tier (56–111 px, e.g. 64 px), **Then** its header shows exactly today's controls.
3. **Given** a track resized to 112 px or more (large tier), **Then** its header additionally shows controls that do not fit in the standard layout (see FR-011), and existing controls (fader, meter) may use the extra space.
4. **Given** a hidden control in the compact layout, **When** the user needs it, **Then** it is still reachable from the track's context menu or the mixer (nothing becomes unreachable).

---

### Edge Cases

- **Group rows** are slim title bars (and open to a full row only while selected). Their slim bar is not resizable; a selected group opens to the height the group would have as a track (default, or its own stored height).
- **Collapsed groups**: hidden child rows keep their stored heights and show them again on expand.
- **Return and master rows** (the footer) keep their fixed slim height; only the track-list width applies to them.
- **Drag targets overlap**: the bottom-edge resize zone (a few pixels around the row seam) must not steal the existing track reorder drag (title strip), clip moves, or clip edge trims in the lane; resize wins only in the thin seam band.
- **Scrolling during a resize**: growing a row near the bottom of the view keeps the dragged edge under the pointer (the view scrolls or the content extends), and shrinking never leaves blank space above the last row.
- **New tracks** are created at the default height (not the height of the selected track).
- **Duplicated / pasted tracks** carry the source track's height.
- **Undo**: resizing is a view change and is not part of the undo history (consistent with zoom and scroll).
- **Old projects** without stored sizes open at today's defaults; projects saved with sizes open in older Nota builds without error (the extra data is ignored).
- **Window too narrow**: the track-list width never leaves the lanes area narrower than a usable minimum; if the window shrinks, the lanes area shrinks first, then the headers clamp.
- **Nota Remote and other views** (session view, mixer) are not affected.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Users MUST be able to change a single track's height by dragging the bottom edge of its row, from either the header or the lane, with a vertical-resize cursor shown over the edge.
- **FR-001a**: The dragged height MUST change continuously (any value within the limits) but snap to a preset height (compact / standard / large) when the pointer comes within a few pixels of it.
- **FR-002**: Track height MUST be limited to 30–256 px; the presets are compact 30 px, standard 64 px (the default, today's height) and large 128 px.
- **FR-003**: Double-clicking a track's bottom edge MUST reset that track (or the multi-selection it belongs to) to the default height.
- **FR-004**: Dragging the bottom edge of a track that is part of a multi-selection MUST resize every selected track to the same height.
- **FR-005**: Holding Alt/Option while dragging any track's bottom edge MUST resize all tracks together.
- **FR-006**: The arrangement's track context menu MUST offer a "Track height" choice with compact / standard / large presets, applying to the selected tracks, plus an "all tracks" variant.
- **FR-007**: Clips, waveforms, MIDI note previews and automation drawn in a track's lane MUST scale to the row's height.
- **FR-008**: Users MUST be able to change the track-list width by dragging the vertical seam between headers and lanes, with a horizontal-resize cursor; the width applies to every header column (tracks, groups, returns, master, and the toolbar strip above the headers).
- **FR-009**: Track-list width MUST be limited to a minimum (enough for the compact header: spine, a short name, M/S/arm and the meter, ~160 px) and a maximum (~480 px); the default stays 228 px; double-clicking the seam resets it.
- **FR-010**: Track headers MUST choose their layout by height: **compact** below 56 px (name + M/S/arm on one line, meter kept), **standard** from 56 px to below 112 px (today's layout), **large** from 112 px (standard plus more); no control may be clipped or overlap at any height or width within the limits. (Thresholds may be tuned in planning if a layout needs a few more pixels, keeping each preset height inside its own tier.)
- **FR-011**: The large layout MUST add at least a labelled, taller volume fader and dB readout and a wider pan control, and for audio tracks keep the input selector and monitor on their own row rather than squeezed beside M/S/arm. Controls that need new engine capabilities are out of scope.
- **FR-012**: Each track's height and the track-list width MUST be saved with the project and restored when it is opened; projects without this data MUST open at the defaults.
- **FR-013**: Resizing MUST NOT be recorded in undo history and MUST NOT mark audio/engine state as changed (it may mark the project as having unsaved changes).
- **FR-014**: Existing header and lane gestures (reorder drag, select, context menu, clip move/trim, automation editing, drag-and-drop of files/devices onto tracks) MUST keep working; the resize zone is limited to a thin band at the row seam.
- **FR-015**: Resizing MUST stay smooth during the drag on a 40-track project (no full rebuild of every header per pointer move if that causes visible lag).
- **FR-016**: The resize handles and layouts MUST follow the Studio design language (`DESIGN.md`): neutral seams, no new accent use; the seam highlights subtly on hover.

### Key Entities

- **Track view size**: per track, its row height (default when absent). Belongs to the project, travels with duplicate/paste.
- **Track-list width**: one value per project for the header column width.
- **Header layout tier**: compact / standard / large, derived from the row height (not stored).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With all tracks set to compact, at least twice as many tracks are visible at once as at the default height in the same window.
- **SC-002**: A user can resize all tracks of a 40-track project with a single gesture (one drag or one menu pick).
- **SC-003**: At every height and width within the limits, no header control is clipped or overlaps another (verified by screenshots at the minimum, default and maximum of each).
- **SC-004**: Sizes survive save/reopen in 100% of cases; projects from previous versions open with unchanged layout.
- **SC-005**: Resizing feels immediate — the row follows the pointer with no visible lag on a 40-track project.
- **SC-006**: All existing arrangement interactions (reorder, clip edit, automation, drag-and-drop) still pass their current manual / automated checks.

## Assumptions

- Per-control visibility toggles ("show/hide which controls") are **out of scope** for this feature (confirmed in Clarifications); the size-adaptive header covers the request. Toggles follow as a separate issue if users still ask for them.
- Sizes (per-track heights and the track-list width) are stored in the project file (confirmed in Clarifications), not as an app-wide preference; a new project starts at the defaults.
- Group slim bars, return and master rows keep their fixed heights; only track rows resize.
- No keyboard shortcuts are added; the new mouse gestures (edge drag, Alt-drag, double-click reset) are listed in Preferences → Shortcuts.
- The session view, mixer and Nota Remote are unchanged.
- The engine needs no change unless the project file is written by the engine, in which case the plan adds an opaque view-state field without touching the audio path.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — view-only change, no audio-thread impact
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — only relevant if persistence goes through the engine
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
