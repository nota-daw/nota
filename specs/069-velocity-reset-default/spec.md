# Feature Specification: Reset a note's velocity to the default

**Feature Branch**: `069-velocity-reset-default`

**Created**: 2026-11-01

**Status**: Draft

**Input**: User description: "Piano roll velocity lane: add a way to put a note's velocity back to the default a freshly created note gets. Today the velocity lane (the vertical stem with a cap under each note) only lets you set a value by clicking or dragging; once a velocity is changed there is no way to return to the default. Proposed: double-click a note's stem resets that note, or every selected note when the stem belongs to the selection, to the default velocity 0.8 (about 102 of 127), as one undo step. A right-click on a stem offers the same reset in a menu ("Reset velocity"), like the "Clear" menus of the lane's other tabs. The default stays 0.8 and does not follow the 'last touched note' velocity that newly drawn notes reuse."

## Nota Context

**Feature area**: UI (Avalonia, `src/managed/Nota.App/PianoRollView.cs`, the velocity lane) — managed only, no engine or ABI change.

**Existing behaviour this builds on**:

- The velocity lane under the piano roll draws one stem per note with a small cap. A press on a stem sets the note's velocity where it was clicked (one stem), or moves the whole selection by the drag distance (several); ⇧-click adds a stem to the selection. Nothing resets a velocity.
- A note drawn on the grid takes the length and velocity of the note last touched (#44). Before any note was touched, that velocity is 0.8. The playable keys also use 0.8.
- Elsewhere in Nota a double-click puts a control back to its default: device-card knobs, Mosaic's velocity curve, Chord's velocity offset, Velocity's random amount.
- The lane's other tabs (Bend, Pres, Slide, CC) draw curves with points, where a double-click already adds or removes a point. They have a right-click menu instead: "Clear <tab>" and "Clear All Expression" (the controller tab: "Clear <controller>"), which returns the value to rest (bend 0, pressure 0, slide centred). The velocity lane has no right-click menu.
- Velocity edits are committed as undo steps through the same path as every other note edit.

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md), [`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md), [`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing

### User Story 1 - Put one velocity back (Priority: P1)

A user has dragged a note's velocity stem somewhere and wants the standard level back. They double-click that stem: the note returns to the default velocity, 0.8 (102 of 127), and the stem, the note's shade and the sound follow. Undo restores the velocity it had before the double-click.

**Why this priority**: it is the whole request — today the only way back is to guess the height by eye.

**Independent Test**: draw a note, drag its stem to the top, double-click the stem; the stem sits at the default height, and ⌘Z brings the stem back to the top in one step.

**Acceptance Scenarios**:

1. **Given** a note at any velocity, **When** the user double-clicks its stem in the velocity lane, **Then** its velocity becomes 0.8.
2. **Given** a note already at 0.8, **When** the user double-clicks its stem, **Then** nothing changes and no undo step is added.
3. **Given** a reset, **When** the user undoes once, **Then** the note has the velocity it had before the double-click.

---

### User Story 2 - Put a selection back (Priority: P2)

With several notes selected, double-clicking the stem of one of them resets all selected notes to 0.8 at once, so a whole phrase that was shaped by hand can be flattened to the default level in one gesture.

**Why this priority**: the lane already moves a selection as a group; resetting should follow the same rule, but a single note is the common case.

**Independent Test**: select three notes with different velocities, double-click one of the three stems; all three read 0.8 and one undo restores all three.

**Acceptance Scenarios**:

1. **Given** a selection of several notes, **When** the user double-clicks the stem of a selected note, **Then** every selected note goes to 0.8, as one undo step.
2. **Given** a selection, **When** the user double-clicks the stem of a note outside it, **Then** only that note is reset and the selection becomes that note, as a click on the stem already does.

---

### User Story 3 - Reset from a menu (Priority: P2)

A user who prefers a menu, or who does not know the double-click, right-clicks a stem in the velocity lane and chooses **Reset velocity**. It does the same as the double-click: the note under the pointer, or every selected note when it belongs to the selection, goes to 0.8, in one undo step. This is the same gesture as the lane's other tabs, where a right-click offers "Clear".

**Why this priority**: it makes the reset findable without reading anything and keeps the lane consistent across its tabs.

**Independent Test**: right-click a stem; a menu offers **Reset velocity**; choosing it sets the note to 0.8 and one undo restores it.

**Acceptance Scenarios**:

1. **Given** a note at any velocity other than the default, **When** the user right-clicks its stem and chooses **Reset velocity**, **Then** its velocity becomes 0.8.
2. **Given** a selection, **When** the user right-clicks the stem of a selected note and chooses **Reset velocity**, **Then** every selected note goes to 0.8 in one undo step.
3. **Given** a right-click on empty space in the lane, **When** the menu would open, **Then** no menu opens.
4. **Given** every note it would reset is already at 0.8, **When** the menu opens, **Then** **Reset velocity** is shown disabled.

---

### User Story 4 - The reset is documented (Priority: P3)

Preferences → Shortcuts lists the double-click and the right-click on a velocity stem, and the user manual's piano-roll page describes them, so the gesture can be learned without trying things at random.

**Why this priority**: the double-click has no visible control, so it depends on being written down.

**Independent Test**: open Preferences → Shortcuts → Piano roll; a row describes the double-click on a velocity stem, and one describes the right-click, in English and in French.

**Acceptance Scenarios**:

1. **Given** Preferences → Shortcuts, **When** the user reads the Piano roll group, **Then** rows name the double-click and the right-click on a velocity stem and what they do.

### Edge Cases

- A double-click is two presses. The first press still sets the velocity where it landed, as it does today; the double-click then overrides it. One undo after the double-click returns to the velocity from before the double-click, not to the value the first press produced.
- A double-click on empty space in the lane (no stem within reach) does nothing, as a single click does today.
- Stems of a locked reference clip, or ghost notes, are not on offer in the lane and are not reset.
- Notes that start together share one x position; the stem picked for the double-click is the same one a single click would pick.
- The other lane tabs (Bend, Pres, Slide, CC) are untouched: their double-click still adds or removes a point, and their right-click menus are unchanged. The new gestures apply to the Vel tab only.
- Drum Rack and folded views use the same lane and behave the same way.
- A velocity edit made while the clip plays follows the live path: the reset is heard on the next note, with no glitch.

## Requirements

- **FR-001**: A double-click on a note's stem in the piano roll's velocity lane sets that note's velocity to the default, 0.8 (102 of 127).
- **FR-002**: When the stem belongs to the selection, every selected note is reset; otherwise only that note is, and it becomes the selection.
- **FR-003**: The reset is one undo step that returns every affected note to the velocity it had before the double-click, including when the first press of the double-click had already changed one.
- **FR-004**: The default is a fixed value, independent of the "last touched note" velocity that newly drawn notes reuse. After a reset, that last-touched value is the default, like after any velocity edit.
- **FR-005**: A note already at the default is left as it is and adds no undo step.
- **FR-006**: A right-click on a note's stem in the velocity lane opens a menu with one item, **Reset velocity**, that does what the double-click does (FR-001 to FR-005, same note-or-selection rule, same undo behaviour). The item is disabled when every note it would reset is already at the default. A right-click on empty space opens no menu.
- **FR-007**: Locked and ghost notes are never reset; the other lane tabs and the note grid are unaffected.
- **FR-008**: Preferences → Shortcuts lists both gestures in the Piano roll group, FEATURES.md and the user manual (EN and RU) describe it, `CHANGELOG.md` `[Unreleased]` has an Added entry, and all new interface text goes through `Loc` with an English key and a French translation.

## Success Criteria

- **SC-001**: A user can put any note's velocity back to the default with one double-click, or with a right-click and one menu choice, with no typing.
- **SC-002**: After a reset on any number of selected notes, a single undo restores all of them.
- **SC-003**: The smoke test covers the reset (single note, selection, already-default, undo, the menu item and its disabled state), and `scripts/build.sh` passes, including the localization checks.

## Assumptions

- "The default" is 0.8, the value a first-ever drawn note and the playable keys already use; it is not a user setting in this feature. A setting could follow if users ask for another default.
- The gestures are the double-click, matching the rest of Nota, and a right-click menu, matching the lane's other tabs. No new keyboard shortcut is added.
- The Drum pattern editor (a separate step grid with its own accent levels) is out of scope.
- The other tabs (Bend, Pres, Slide, CC) already return to rest through their "Clear" menus, and their double-click is taken by adding or removing points, so they are not changed.
- The French wording of the new Shortcuts row follows the glossary of specs/068-ui-language.
