# Feature Specification: Piano roll vertical zoom

**Feature Branch**: `019-piano-roll-vertical-zoom`

**Created**: 2026-10-16

**Status**: Clarified

**Input**: User description: "Piano roll: vertical zoom (GitHub issue nota-daw/nota#43). Users want to edit notes comfortably in the clip editor (piano roll): see more of the keyboard range at once, or make rows taller to hit notes precisely. Add vertical zoom to the piano roll — e.g. Cmd/Ctrl + Alt + mouse wheel, a trackpad zoom gesture, or dragging on the keys column — plus a \"fit to notes\" option. Today the row height is fixed (RowH = 12 in PianoRollView.cs); only horizontal zoom exists."

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

## Clarifications

### Session 2026-10-16

- Q: When you zoom rows vertically in one clip, should that row height apply only to that clip, or to every clip you open? → A: Per clip — each clip remembers its own row height; a clip never opened before starts at the default (12 px).
- Q: When you open a clip for the first time, should the piano roll automatically fit the view to its notes, or keep today's behaviour? → A: Keep today's behaviour — default row height (12 px), centred on the clip's notes; Fit to notes runs only when the user invokes it.
- Q: When you press on the keys column and drag, what should the drag do? → A: A vertical drag zooms: down makes rows taller, up makes them shorter, and the pressed key stays where it was; a horizontal drag does nothing.
- Q: Which keyboard shortcut should trigger "Fit to notes" in the piano roll? → A: ⇧F (Shift+F) — it must take priority over the computer-keyboard note map (which today also fires on Shift+F) while the piano roll has focus, so it fits without playing a note; elsewhere Shift+F is unchanged.
- Q: What row-height limits should vertical zoom have (min / default / max, at 100% UI scale)? → A: 6 / 12 / 36 px.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Zoom rows with the wheel (Priority: P1)

A user editing a clip in the piano roll holds ⌘ + ⌥ (Ctrl + Alt on Windows/Linux) and turns
the mouse wheel (or scrolls on a trackpad). Rows get taller or shorter, and the pitch under
the pointer stays under the pointer, so they can zoom into a dense chord or out to see a
wide melodic range without losing their place.

**Why this priority**: This is the core of the request — without a way to change row height
nothing else matters. It mirrors the existing ⌘/Ctrl + wheel horizontal zoom, so it is the
gesture users will try first.

**Independent Test**: Open a clip, ⌘⌥-scroll over a note: rows change height, the note stays
under the pointer, the keys column, note grid and all note-height-dependent drawing (notes,
held-key highlights, scale tints, expression curves) stay aligned.

**Acceptance Scenarios**:

1. **Given** a clip open in the piano roll at default row height, **When** the user ⌘⌥-scrolls up over pitch E4, **Then** rows get taller and E4 stays under the pointer.
2. **Given** rows at the maximum height, **When** the user ⌘⌥-scrolls to zoom in further, **Then** nothing changes (no jump, no error).
3. **Given** rows at the minimum height, **When** the user ⌘⌥-scrolls to zoom out further, **Then** nothing changes.
4. **Given** any vertical zoom level, **When** the user clicks, drags, resizes, draws or box-selects notes, **Then** the pitch picked is the one visibly under the pointer.
5. **Given** a zoomed view, **When** the user plain-wheel scrolls / ⌘-wheel zooms / Shift-wheel scrolls, **Then** those keep their current meanings (vertical scroll, horizontal zoom, horizontal scroll).

---

### User Story 2 - Fit to notes (Priority: P2)

A user opens a clip whose notes span two octaves somewhere in the middle of the keyboard.
They invoke **Fit to notes** and the piano roll sets the row height and vertical scroll so
that every note in the clip is visible, with a little margin above and below.

**Why this priority**: Explicitly requested; gives a one-step way back to a useful view
after zooming around, and makes the most of the screen for any clip.

**Independent Test**: Open a clip with notes from C3 to C5, zoom in somewhere else, invoke
Fit to notes: C3..C5 are all visible and fill most of the visible height.

**Acceptance Scenarios**:

1. **Given** a clip with notes spanning C3..C5 and the view zoomed in on C6, **When** the user invokes Fit to notes, **Then** all notes C3..C5 are visible and the range fills the visible height (within the row-height limits) with about one row of margin above and below.
2. **Given** a clip whose note span is too wide to fit even at minimum row height, **When** the user invokes Fit to notes, **Then** rows go to minimum height and the view is centred on the notes.
3. **Given** a clip with a single note (or one pitch), **When** the user invokes Fit to notes, **Then** rows go no taller than the maximum and the note is centred.
4. **Given** an empty clip, **When** the user invokes Fit to notes, **Then** the view returns to the default row height centred around C5 (the existing empty-clip default).
5. **Given** some notes are selected, **When** the user invokes Fit to notes, **Then** the fit uses all notes in the clip, not just the selection.

---

### User Story 3 - Drag on the keys column (Priority: P3)

A user presses on the keys column at the left of the piano roll and drags vertically. Dragging
one way makes rows taller, the other way shorter, anchored on the key where the drag started.

**Why this priority**: A discoverable mouse-only alternative for users without a wheel or who
don't know the modifier gesture; familiar from other DAWs. Nice to have once US1 exists.

**Independent Test**: Press on the keys column and drag up/down: rows zoom, the key pressed
stays under the pointer's start height; releasing ends the zoom.

**Acceptance Scenarios**:

1. **Given** the default view, **When** the user presses on the C4 key and drags downward, **Then** rows get taller and C4 stays at the press height.
2. **Given** the default view, **When** the user presses on the keys column and drags upward, **Then** rows get shorter.
3. **Given** a drag on the keys column, **When** the user releases, **Then** the zoom stays as it was at release and no note is created, changed or played.

---

### User Story 4 - Pinch on the trackpad (Priority: P3)

On a trackpad that reports a pinch/magnify gesture, the user pinches over the note grid
with ⌥ held and rows zoom, anchored at the pointer.

**Why this priority**: Requested as an option; useful for laptop users, but the ⌘⌥ + two-finger
scroll of US1 already covers trackpads, so this is the least critical path.

**Independent Test**: On a macOS trackpad, ⌥ + pinch over the grid changes row height anchored
at the pointer; without ⌥ the pinch does whatever it does today.

**Acceptance Scenarios**:

1. **Given** a clip open on a Mac with a trackpad, **When** the user holds ⌥ and pinches out over the grid, **Then** rows get taller anchored at the pointer.
2. **Given** a platform/device that reports no pinch gesture, **When** the user pinches, **Then** nothing breaks; US1–US3 still work.

---

### Edge Cases

- Zoom out until the whole 88-key range is shorter than the visible area: the range stays pinned to the top (no empty gap above A0..C8 jumping around) and scrolling does nothing.
- Zoom while a note drag / resize / box selection is in progress: the zoom is ignored until the gesture ends, or the gesture stays consistent with the new row height — never a note landing on a pitch the user didn't point at.
- Live MIDI recording / held-key highlights while zoomed: highlights follow the new row height.
- Pitch labels in the keys column at small row heights: labels that would not fit are thinned (e.g. only C-notes labelled) rather than overlapping.
- Re-opening a clip: its last vertical zoom comes back together with its scroll and horizontal zoom.
- Opening a clip never edited before: default row height, centred on its notes (today's behaviour) — no automatic Fit to notes.
- Window/panel resize: row height stays; only the visible range changes.
- Vertical zoom does not touch the velocity lane or the ruler heights.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The piano roll MUST support a variable row height of 6 px minimum to 36 px maximum, with 12 px as the default (at 100 % UI scale).
- **FR-002**: ⌘ + ⌥ + wheel (Ctrl + Alt + wheel on Windows/Linux) over the note grid or keys column MUST zoom rows vertically, anchored on the pitch under the pointer.
- **FR-003**: Existing wheel meanings MUST NOT change: plain wheel scrolls vertically, ⌘/Ctrl + wheel zooms horizontally, Shift + wheel and horizontal wheel scroll horizontally.
- **FR-004**: Pressing and dragging vertically on the keys column MUST zoom rows, anchored on the key pressed (it stays at the press height); down = taller, up = shorter; horizontal movement is ignored. The drag MUST NOT create, edit or audition notes.
- **FR-005**: Where the platform reports a trackpad pinch/magnify gesture, ⌥ + pinch over the grid MUST zoom rows anchored at the pointer.
- **FR-006**: A **Fit to notes** command MUST set the row height and vertical scroll so all of the clip's notes are visible with about one row of margin above and below, limited by FR-001's bounds; an empty clip returns to the default height centred around C5. Fit to notes runs only when the user invokes it, never automatically on opening a clip.
- **FR-007**: Fit to notes MUST be reachable from the piano roll (a control in its header/toolbar area) and from the command palette, and MUST be bound to ⇧F (Shift+F) while the piano roll has focus. Pressing ⇧F there MUST fit without also playing a note through the computer-keyboard note map; outside the piano roll, Shift+F keeps its current behaviour.
- **FR-008**: Everything drawn or hit-tested per row (keys, lanes, scale tints, octave lines, notes, note edges, held-key highlights, expression curves, box selection, scroll-to-note behaviour) MUST follow the current row height.
- **FR-009**: The vertical zoom MUST be remembered per clip alongside the existing per-clip scroll and horizontal zoom, for the lifetime of the app session (same scope as those today). It is never shared between clips: zooming one clip leaves every other clip's row height unchanged, and a clip opened for the first time starts at the default row height.
- **FR-010**: Vertical zoom MUST NOT be an undoable edit and MUST NOT mark the project as modified.
- **FR-011**: Keys-column labels MUST stay legible at every row height (thin out labels when rows are too short to fit them).
- **FR-012**: The Preferences → Shortcuts list MUST show the new vertical-zoom gestures and the Fit to notes shortcut.

### Key Entities

- **Piano roll view state (per clip)**: horizontal zoom, horizontal scroll, vertical scroll, and now row height. View-only; not part of the project document.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can see the full 88-key range at once at minimum zoom on a piano roll at least 540 px tall (88 × 6 px = 528 px).
- **SC-002**: At maximum zoom, rows are 3× the current default height, so a single pitch is three times easier to hit.
- **SC-003**: Fit to notes brings every note of a clip into view in one action, for any clip whose span fits at minimum row height.
- **SC-004**: In 100 % of pointer edits at any zoom level, the edited pitch is the one visibly under the pointer (no off-by-one-row hits).
- **SC-005**: Re-opening a clip shows the same vertical zoom and position the user left it at.

## Assumptions

- Fit to notes is vertical only; horizontal "fit to clip length" already happens on first open and is out of scope.
- Fit to notes considers all notes in the clip, ignoring the selection (a "fit to selection" variant is out of scope).
- Persistence of vertical zoom follows today's per-clip view state: in memory for the session, not saved into the project file.
- The pinch gesture (US4) depends on what the UI toolkit reports per platform; on platforms without it, US1–US3 suffice.
- No engine, ABI or audio-thread change; the feature is entirely in the piano-roll UI.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI-only, no audio-thread involvement
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no crossings added
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change; no smoke-test extension needed
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
