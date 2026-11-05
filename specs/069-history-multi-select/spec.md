# Feature Specification: Delete several versions at once

**Feature Branch**: `069-history-multi-select`

**Created**: 2026-11-01

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#228: "The ability to select multiple versions with
Ctrl+click and a range of versions with Shift+click and/or marquee selection would make it
easier to delete a selection of versions with one action instead of deleting multiple
versions one by one."

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [x] Managed app (C#, `src/managed/*`) — the History tab
- [x] UI (Avalonia) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: the browser's History tab (`HistoryView`) lists the
open project's versions as a tree. One version is selected at a time; its details and Switch /
Open copy sit in the footer, and its context menu deletes it after a confirmation. The tab's
options menu deletes every unnamed version at once. `IProjectHistory.DeleteMany` already
deletes a batch of versions (refusing one that holds the current version), linking each
survivor up to the nearest kept ancestor and freeing the audio only the deleted ones used.

## User Scenarios & Testing

### User Story 1 - Delete a hand-picked set of versions (Priority: P1)

A producer's history holds dozens of versions; "Delete unnamed versions" keeps some they no
longer want and deletes some they do. They pick the ones to go and delete them in one action.

**Independent Test**: In a project with several versions, ⌘/Ctrl-click three of them, choose
**Delete 3 versions…** in the footer and confirm; the three are gone, the rest of the tree is
linked up, and the status bar reports how much was freed.

**Acceptance Scenarios**:

1. **Given** one version is selected, **When** the user ⌘/Ctrl-clicks another, **Then** both
   are selected; ⌘/Ctrl-clicking a selected one deselects it.
2. **Given** one version is selected, **When** the user Shift-clicks another, **Then** every
   version between them is selected.
3. **Given** more than one version is selected, **Then** the footer shows "N versions", the
   time span they were saved in, and a **Delete N versions…** button.
4. **When** the user right-clicks a selected row of a multiple selection, **Then** the menu is
   headed "N versions" and offers **Delete N versions…**. Right-clicking a row outside the
   selection selects just that row and shows its usual menu.
5. **When** the user confirms the deletion, **Then** all the chosen versions are deleted in
   one history edit and the status bar reads "Deleted N versions · X freed".

### Edge Cases

- **The current version is in the selection**: it can't be deleted. The footer says "The
  current version stays.", the button and menu item count only the others, and the menu item
  explains it in its tooltip. With only the current version and one other selected, the one
  other version is deleted through the single-version confirmation.
- **Only the current version would be deleted**: not possible — a selection of two always holds
  another version.
- **All saved in the same minute**: the time span shows that one time.
- **Switch, double-click and Return** act on a single selected version only.
- **A refresh** (save, star, rename) keeps every selected version that still exists selected.
- **Starred only**: selecting and deleting work the same on the flat starred list.

## Requirements

### Functional Requirements

- **FR-001**: The History list MUST support ⌘/Ctrl-click (toggle one) and Shift-click (a run)
  selection; a plain click selects one version as before.
- **FR-002**: With more than one version selected, the footer MUST show the count, the saved
  time span and a delete button for them.
- **FR-003**: The context menu of a row inside a multiple selection MUST act on the selection.
- **FR-004**: Deleting MUST go through one confirmation and one `DeleteMany` call, and report
  the bytes freed.
- **FR-005**: The current version MUST NOT be deleted; the UI MUST say it stays.
- **FR-006**: Labels MUST follow DESIGN.md (sentence case; destructive menu items in the
  danger ink via the `danger` class).

## Success Criteria

- **SC-001**: Deleting N chosen versions takes one action and one confirmation, not N of each.

## Assumptions

- Marquee (drag) selection is not added: the issue asks for Ctrl- and Shift-click "and/or"
  marquee, and the list's rows already take drags as clicks.
- Batch star / name / note are out of scope; the issue is about deleting.
- No change to `IProjectHistory`: `DeleteMany` and its smoke checks already cover the batch.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header (no new source file)
- [x] No new dependency
- [x] Audio-thread impact: none — the History tab on the UI thread
- [x] No new managed/native crossings
- [x] Smoke-test impact: the batch delete is covered by `HistoryTests`; the design checks scan the new labels
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (the footer's own layout, MenuKit header, danger delete item)
