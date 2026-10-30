# Feature Specification: Section renaming — cancel edits and restore automatic names

**Feature Branch**: `cursor/section-automatic-names-d981`

**Created**: 2026-10-30

**Status**: Implemented

**Input**: GitHub issue [#106](https://github.com/nota-daw/nota/issues/106): "Escape cancels an
uncommitted section rename; Enter commits. Add Reset to Default that restores automatic naming
mode. Number automatic names left-to-right across all sections, including custom-named ones,
preserve stable identities and support undo/redo of both text and naming mode."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Ember)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md), [`specs/016`](../016-device-param-section-undo/spec.md)
(sections as undoable project data).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Back out of a rename (Priority: P1)

A producer double-clicks a section, starts typing, changes their mind and presses Escape. The
section keeps exactly the name it had — custom or automatic — and no undo step is recorded.
Enter commits what was typed as one undo step.

**Independent Test**: rename a section, press Escape — the name is unchanged and Undo has
nothing new; rename again, press Enter — the name changes and one Undo restores it.

**Acceptance Scenarios**:

1. **Given** a section named "Chorus", **When** the user types "Bridge" and presses Escape,
   **Then** it is still "Chorus" and no undo step was added.
2. **Given** an automatically named section, **When** the user types and presses Escape,
   **Then** it stays automatic (it keeps renumbering with its position).
3. **Given** any section, **When** the user types "Bridge" and presses Enter, **Then** it is
   named "Bridge" and one Undo restores the previous name and naming mode.
4. **Given** the rename box, **When** the user clears it (or leaves only spaces) and presses
   Enter, **Then** the section returns to automatic naming.

### User Story 2 - Reset to the default name (Priority: P1)

A custom-named section can be returned to automatic naming in one step: a **Reset to default**
button in the rename box, a **Reset to default name** entry in the section's context menu, and
a **Reset section name** command in the command palette (for the section under the playhead).

**Independent Test**: name the second of three sections "Chorus", reset it — it reads
"Section 2" again; Undo brings "Chorus" back, Redo resets it again.

**Acceptance Scenarios**:

1. **Given** a custom-named section, **When** the user picks Reset, **Then** it shows its
   automatic name for its position and later renumbers with it.
2. **Given** an automatically named section, **When** the user opens its menu, **Then** the
   Reset entry is disabled (nothing to reset).
3. **Given** a reset, **When** the user undoes it, **Then** the previous custom name and custom
   mode come back; Redo returns it to automatic.
4. **Given** the playhead inside a custom-named section, **When** the user runs **Reset section
   name** from the command palette, **Then** that section is reset; with no section under the
   playhead the command says so and changes nothing.

### User Story 3 - Accurate automatic numbering (Priority: P1)

Automatic names are "Section 1", "Section 2", … by each section's position in the whole
arrangement, left to right, counting custom-named sections too. If the second section is
"Chorus", the third is still "Section 3". Automatic names follow inserts, deletes, duplicates
and moves; custom names never change on their own.

**Acceptance Scenarios**:

1. **Given** sections [auto, "Chorus", auto], **Then** they read "Section 1", "Chorus",
   "Section 3".
2. **Given** that list, **When** a new section is drawn before the first, **Then** the
   automatic ones read "Section 2" and "Section 4" and the new one "Section 1".
3. **Given** an automatic section, **When** it is duplicated, **Then** the copy is automatic
   too; a custom section's copy keeps the custom name.
4. Numbering does not depend on zoom, scroll, the lane being hidden or the selection.

### User Story 4 - The rename box says which kind of name it is (Priority: P2)

The rename box tells automatic and custom names apart without adding anything to the
arrangement: for an automatic section the box is empty with the automatic name as its
placeholder and an "Automatic name" caption; for a custom one the name is selected and the
caption reads "Custom name".

### Edge Cases

- Leading / trailing spaces of a typed name are trimmed; a name that is all spaces is automatic.
- Closing the rename box by clicking elsewhere discards the edit (same as Escape).
- Enter with the text unchanged records no undo step.
- A section reloaded by undo / MCP while the rename box is open is found again by its span; if
  it no longer exists, the edit is dropped.
- Typing exactly the automatic name (e.g. "Section 3") makes it a custom name — the user chose
  it; Reset is how to get back to automatic.
- Projects saved by earlier builds stored every name as text, including "Section N" defaults:
  those load as custom names (nothing is reinterpreted); Reset makes them automatic.
- MCP: `set_sections` with an empty name makes an automatic section; `list_sections` returns
  the displayed name and whether it is automatic. Nota Remote shows the displayed names.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: Escape (and dismissing the rename box) MUST leave the section's name and naming
  mode unchanged and record no undo step.
- **FR-002**: Enter MUST commit the typed name as one undo step; an empty or whitespace-only
  name MUST return the section to automatic naming.
- **FR-003**: A section MUST be either automatic or custom; that state MUST be part of the
  project (saved, loaded, undone and redone with the name).
- **FR-004**: An automatic section MUST display "Section N", N its 1-based position among all
  sections ordered by start, counting custom-named sections.
- **FR-005**: New sections (drawn, "Add section here") MUST start automatic; a duplicate MUST
  keep its source's naming mode.
- **FR-006**: Reset MUST be offered in the rename box, the section context menu and the command
  palette, MUST make the section automatic as one undo step, and MUST be reachable by keyboard.
- **FR-007**: The rename box MUST show whether the current name is automatic or custom.
- **FR-008**: Sections MUST keep their identity (span and order in the project) when their
  displayed name changes; renumbering MUST NOT rewrite stored data.
- **FR-009**: MCP and Nota Remote MUST see the displayed names.

## Success Criteria *(mandatory)*

- **SC-001**: Escape after any rename edit leaves the project byte-identical and the undo
  history unchanged.
- **SC-002**: Resetting a custom name takes one action from the section's menu or rename box.
- **SC-003**: For any arrangement, every automatic name equals "Section" + its position.
- **SC-004**: One Undo after a rename or reset restores both the previous text and mode.

## Assumptions

- Automatic naming is stored as an empty name, so no engine, C ABI or file-format change is
  needed: the engine already keeps names in its undoable project snapshot.
- Nota has no user-rebindable shortcuts (Settings → Shortcuts is a reference list), so
  "assignable in shortcut settings" is out of scope; Reset is reachable from the command
  palette and by keyboard inside the rename box. A rebinding system is a separate feature.
- Older projects' stored "Section N" texts stay custom (see Edge Cases).
- The user manual lives in the separate `nota-docs` repository. Its Arrangement, command
  palette, MCP and shortcuts pages are updated there as a follow-up (nota-docs skill), not in
  this repository's change.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header — one new test file, with header
- [x] No new dependency
- [x] Audio-thread impact considered — none: managed UI and data only
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7) — unchanged
- [x] Smoke-test impact assessed: naming rules, reset undo/redo, save/load and MCP covered in `tests/Nota.SmokeTest`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned
- [x] UI change → follows `DESIGN.md` (Ember design language)
