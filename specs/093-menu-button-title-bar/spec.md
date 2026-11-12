# Feature Specification: Menu button and a hideable menu bar

**Feature Branch**: `feat/menu-button-title-bar`

**Created**: 2026-11-12

**Status**: Draft

**Issue**: [nota-daw/nota#518](https://github.com/nota-daw/nota/issues/518)

**Input**: User description: "Issue #518: Main window on Windows/Linux — project name in the title bar, a Menu button in its place, and a setting to hide the menu bar. (1) Title-bar caption shows the project ("Nota — My song", "Nota — Untitled"), same as the OS window title; the project name leaves the transport bar and the transport-fit logic that moves it to the caption on narrow windows goes away. (2) A Menu button at the start of the transport bar opens the whole application menu (File, Edit, Grid, View, Transport, Help as submenus) with the same items, check marks, shortcuts and enabled state as the menu bar, built from the same command catalogue. (3) The menu row is hidden by default. (4) A "Show menu bar" setting in Settings → Appearance and as a check item in View (reachable from the Menu button) brings the row back; whether the Menu button stays when the bar is shown is to decide in review. Keyboard: Alt / F10 opens the Menu button's menu when the row is hidden; menu shortcuts keep working either way. macOS: no change. Related: #41 (out of scope), #427 (Appearance page split)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Related specs / issues**: [038 style guide](../038-style-guide/spec.md) (the new look,
#296, which already took the project name out of the transport bar);
[088 settings reorganisation](../088-settings-reorganisation/spec.md) (#427, where the
Appearance page lives); #41 (hiding the native title bar — out of scope).

**Where things stand on `develop`** *(what this spec changes, as opposed to what the issue
text assumed when it was filed)*:

- The transport bar no longer shows the project name: since the new look (#296) it starts
  with the view switch, and there is no longer any logic that moves the name to the caption
  on narrow windows. Part (1) of the issue therefore reduces to the caption text itself.
- Windows: Nota draws its own 32-px title bar whose caption is the project name alone
  ("My song"); the OS window title is "Nota — My song". Below it sits the in-window menu row
  (File / Edit / View / Transport / Help, Grid inside Edit), then the transport bar.
- Linux: the OS draws the window frame and its title ("Nota — My song"); Nota's own title bar
  is hidden. Below the frame sit the in-window menu row and the transport bar.
- macOS: the menu lives in the system menu bar; Nota's title bar shows the project name.

## Clarifications

### Session 2026-11-12

- Q: Should the Menu button stay while the menu row is shown? → A: No — it hides while the row is shown (FR-011).
- Q: What should the Menu button look like? → A: Icon only (☰), a square key the size of the view-switch keys; "Menu" in its tooltip and accessible name (FR-007).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The whole menu from a Menu button, with the menu row gone (Priority: P1)

On Windows or Linux a user opens Nota and the work area starts higher: there is no menu row
between the title bar and the transport. At the left end of the transport bar, before the view
switch, sits a Menu button. Clicking it opens one menu with File, Edit, View, Transport and
Help as submenus, holding exactly what the menu row held — the same items, in the same order,
with the same check marks, shortcut hints and greyed-out states, and Open recent with the
recent projects. Picking an item does what the menu-row item did.

**Why this priority**: This is the point of the issue — a full row of height returned to the
work without losing any command. Hiding the row is only safe once the button gives the same
menu.

**Independent Test**: On Windows (or Linux), start Nota with default settings: no menu row is
shown, a Menu button leads the transport bar, and every item of the old menu row is reachable
through it and behaves the same.

**Acceptance Scenarios**:

1. **Given** a fresh install on Windows or Linux, **When** the main window opens, **Then**
   no menu row is shown and the transport bar starts with a Menu button.
2. **Given** the Menu button, **When** the user clicks it, **Then** a menu opens below it
   with the submenus File, Edit, View, Transport and Help, in that order.
3. **Given** the Menu button's menu, **When** the user compares any submenu with the menu
   row's (shown via the setting), **Then** both list the same items in the same order with
   the same separators, check marks, shortcut hints and enabled state.
4. **Given** no recent projects, **When** the user opens Menu → File → Open recent, **Then**
   the greyed-out "No recent projects" line shows, as it does in the menu row. (Menu commands
   themselves are never greyed today — a command that can't act explains why in the status
   line — and the Menu button keeps that.)
5. **Given** Snap to grid is on, **When** the user opens Menu → Edit → Grid, **Then** Snap to
   grid carries a check mark; picking it turns snapping off and the next opening shows it
   unchecked.
6. **Given** recently opened projects, **When** the user opens Menu → File → Open recent,
   **Then** the same recent list as the menu row's is shown and picking one opens it.
7. **Given** the menu row hidden, **When** the user presses a menu shortcut (for example
   Ctrl+S, Ctrl+Z), **Then** the command runs exactly as it did with the row shown.
8. **Given** a narrow window, **When** the transport bar sheds parts to fit, **Then** the
   Menu button stays, like the view switch and the transport keys.

---

### User Story 2 - Bring the menu row back (Priority: P2)

A user who prefers a classic menu row turns on "Show menu bar" — in Settings → Appearance or
as a check item in Menu → View — and the row reappears under the title bar at once. The choice
is remembered across restarts. Turning it off hides the row again.

**Why this priority**: The row going away by default changes a habit; people who want it back
need a one-step way to get it, from where they would look for it.

**Independent Test**: Turn "Show menu bar" on from Menu → View: the row appears; restart:
it is still there; turn it off in Settings → Appearance: the row disappears and the View item's
check mark follows.

**Acceptance Scenarios**:

1. **Given** the menu row hidden, **When** the user picks Menu → View → Show menu bar,
   **Then** the menu row appears immediately and the item shows a check mark.
2. **Given** the menu row shown, **When** the user unticks Show menu bar in Settings →
   Appearance, **Then** the row disappears immediately and the View item loses its check mark
   (and the reverse: the Settings switch follows a change made from the View menu, including
   while the Settings window is open).
3. **Given** "Show menu bar" on, **When** Nota is restarted, **Then** the menu row is shown
   from the first frame.
4. **Given** the menu row shown, **When** the user looks at the transport bar, **Then** the
   Menu button is not there (the row already gives the menu) and the view switch leads the bar
   again; turning the row off brings the button back.

---

### User Story 3 - Open the menu from the keyboard (Priority: P2)

A keyboard user on Windows or Linux taps Alt (pressed and released on its own) or presses F10
and the menu opens, with the keyboard able to move through it. With the row hidden this opens
the Menu button's menu; with the row shown it does what it does today for the row.

**Why this priority**: Alt / F10 is the standard way into an application menu on Windows and
Linux; hiding the row must not make the menu mouse-only.

**Independent Test**: With the row hidden, tap Alt: the Menu button's menu opens and the arrow
keys move through File…Help and into a submenu; Escape closes it and focus returns to where it
was.

**Acceptance Scenarios**:

1. **Given** the menu row hidden, **When** the user presses F10, **Then** the Menu button's
   menu opens with its first entry (File) highlighted.
2. **Given** the menu row hidden, **When** the user presses and releases Alt with no other key
   or click in between, **Then** the Menu button's menu opens.
3. **Given** the menu row hidden, **When** the user holds Alt to drag a clip freely (or uses
   any Alt+key / Alt+click combination), **Then** the menu does not open.
4. **Given** the menu open, **When** the user presses Escape (or clicks elsewhere), **Then**
   the menu closes, nothing runs, and keyboard focus is back where it was.
5. **Given** the menu row shown, **When** the user taps Alt or presses F10, **Then** the menu
   row takes keyboard focus as it does today.
6. **Given** the focus in a text field (rename, BPM entry), **When** the user taps Alt or
   presses F10, **Then** the menu opens the same way, and the text being edited is not changed.

---

### User Story 4 - The project name in the title bar on Windows (Priority: P3)

On Windows the caption in Nota's own title bar reads "Nota — My song" ("Nota — Untitled" until
the project is named, or whatever an unsaved new project is called), the same text as the OS
window title shown in the taskbar and Alt+Tab, and it follows Save As, Open and New.

**Why this priority**: With the menu row gone, the title bar is the one place the app and the
project are named in the window; it is a small text change.

**Independent Test**: On Windows, open a project called "My song": the caption reads
"Nota — My song", and Save As "Demo" changes it to "Nota — Demo".

**Acceptance Scenarios**:

1. **Given** a new, never-saved project on Windows, **When** the main window shows, **Then**
   the title-bar caption reads the same text as the OS window title (for example
   "Nota — Untitled").
2. **Given** a project open on Windows, **When** the user saves it under a new name or opens
   another project, **Then** the caption updates to "Nota — <new name>" at the same time as
   the OS window title.
3. **Given** Linux, **When** a project is open, **Then** the OS frame shows "Nota — <name>"
   as it does today (no change needed).
4. **Given** macOS, **When** a project is open, **Then** the caption is unchanged from today
   (the project name alone).

---

### User Story 5 - macOS stays as it is (Priority: P1)

A macOS user sees no change: the menu stays in the system menu bar, there is no Menu button,
no "Show menu bar" setting in Settings → Appearance and no Show menu bar item in View, and the
title bar is as before.

**Why this priority**: The issue explicitly limits the change to Windows and Linux; a macOS
regression would break the platform's convention.

**Independent Test**: On macOS, compare the main window, the system menu and Settings →
Appearance with the previous release: identical.

**Acceptance Scenarios**:

1. **Given** macOS, **When** the main window opens, **Then** the transport bar starts with
   the view switch, no Menu button.
2. **Given** macOS, **When** the user opens View in the system menu bar or Settings →
   Appearance, **Then** there is no Show menu bar item or setting.

---

### Edge Cases

- The menu changes while the Menu button's menu is closed (Open recent gains an entry, a check
  state or enabled state changes, the interface language changes): the next opening shows the
  current state, never a stale copy.
- The interface language is switched: both the Menu button's menu and the menu row show the
  new texts; the button's own label / tooltip follows too.
- Fullscreen on Windows hides Nota's title bar as today; the Menu button is still in the
  transport bar and the menu row follows the setting.
- The interface scale (spec 027) and text size settings apply to the Menu button and its menu
  as to the rest of the transport bar and to context menus.
- A menu item opens a dialog (Settings…, Export…): the menu closes first and the dialog opens
  as it does from the menu row.
- Alt tapped while a menu, popup or modal dialog is already open: no second menu opens; the
  standard behaviour of that popup applies.
- Alt pressed then released after a mouse button press or wheel event in between (e.g.
  Alt+wheel zoom): no menu.
- Alt tapped in the middle of a clip drag (to switch snapping off and on): no menu — the drag
  owns Alt until the button is released.
- Alt or F10 while the command palette is open: the palette keeps the keyboard; no menu.
- The setting file from an older version has no "Show menu bar" value: the row is hidden
  (the new default), the user can turn it on.
- Changing the menu row's visibility changes the height of the work area: the arrangement,
  session and mixer re-lay out without losing scroll position or selection.
- Remote / MCP / shortcut-driven commands are unaffected by whether the menu row is shown.

## Requirements *(mandatory)*

### Functional Requirements

**Menu button (Windows / Linux)**

- **FR-001**: On Windows and Linux the transport bar MUST start with a Menu button, placed
  before the view switch, whenever the menu row is hidden.
- **FR-002**: Clicking the Menu button MUST open a single menu whose top-level entries are the
  menu row's top-level menus (File, Edit, View, Transport, Help) as submenus, in the same order.
- **FR-003**: The Menu button's menu MUST be built from the same menu definition and command
  catalogue as the menu row, so that items, order, separators, nested submenus (Edit → Grid,
  File → Open recent), labels, shortcut hints, check marks and enabled state are the same in
  both, with no second hand-maintained list.
- **FR-004**: Picking an item in the Menu button's menu MUST run the same command as the
  corresponding menu-row item.
- **FR-005**: The Menu button's menu MUST reflect the current state each time it opens (recent
  projects, check marks, enabled state, language).
- **FR-006**: The Menu button MUST NOT be shed when the transport bar is too narrow.
- **FR-007**: The Menu button MUST be an icon-only key (a "menu" ☰ glyph) of the same size as
  the view-switch keys, with the label "Menu" as its tooltip and accessible name; it follows
  the transport bar's design language (DESIGN.md) and the interface scale.

**Menu row and the "Show menu bar" setting (Windows / Linux)**

- **FR-008**: On Windows and Linux the menu row MUST be hidden by default, including for users
  upgrading from a version without the setting.
- **FR-009**: A "Show menu bar" on/off setting MUST be offered in Settings → Appearance and as
  a check item in the View menu (thus also reachable from the Menu button).
- **FR-010**: Changing the setting from either place MUST show or hide the menu row
  immediately, update the other place's state, and be persisted with the user's settings so
  it applies from the first frame on the next start.
- **FR-011**: While the menu row is shown, the Menu button MUST be hidden; when the row is
  hidden, the button MUST be shown.
- **FR-012**: Menu keyboard shortcuts MUST work identically whether the menu row is shown or
  hidden.

**Keyboard access (Windows / Linux)**

- **FR-013**: With the menu row hidden, F10 MUST open the Menu button's menu with keyboard
  navigation (arrows, Enter, Escape) working in it.
- **FR-014**: With the menu row hidden, pressing and releasing Alt with no other key, mouse
  button or wheel input in between MUST open the Menu button's menu; any Alt combination
  (Alt+key, Alt+click, Alt+drag, Alt+wheel) MUST NOT open it, nor Alt pressed or released
  while a mouse button is down (during a drag Alt toggles snapping). Neither F10 nor Alt opens
  it while the command palette is open.
- **FR-015**: Closing the menu without picking an item MUST return keyboard focus to the
  control that had it before.
- **FR-016**: With the menu row shown, Alt and F10 MUST keep their current behaviour for the
  row.
- **FR-017**: The Shortcuts list in Settings MUST mention Alt / F10 as the way to open the menu
  on Windows and Linux.

**Title bar caption**

- **FR-018**: On Windows the caption in Nota's own title bar MUST show the same text as the OS
  window title ("Nota — <project name>"), updated whenever the project name changes.
- **FR-019**: On Linux the window title MUST stay "Nota — <project name>" (shown by the OS
  frame); no change.

**macOS**

- **FR-020**: On macOS there MUST be no Menu button, no "Show menu bar" setting or View item,
  no Alt / F10 menu handling, and no change to the title bar caption or the system menu.

**Localisation**

- **FR-021**: All new user-visible texts (Menu, Show menu bar and its caption in Settings) MUST
  go through the localisation catalogue with English entries; other languages follow the
  project's usual translation flow.

### Key Entities

- **Show menu bar** (user setting): a per-user on/off preference, default off; meaningful on
  Windows and Linux only; stored with the other appearance settings.
- **Application menu definition**: the one description of the menu (top-level menus, items,
  separators, command ids) from which both the menu row and the Menu button's menu are
  produced; each item draws its label, shortcut, check and enabled state from the command
  catalogue.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: On Windows and Linux with default settings, the vertical space above the
  transport bar shrinks by the full height of the former menu row (the work area gains that
  height).
- **SC-002**: 100% of menu-row items (every command, submenu and separator) are present in the
  Menu button's menu, in the same order, with matching check, enabled and shortcut states —
  verified by an automated comparison of the two menus.
- **SC-003**: Every menu command reachable from the menu row is reachable from the Menu button
  in at most three clicks (button → submenu → item; four for items in nested submenus such as
  Edit → Grid).
- **SC-004**: A keyboard-only user can open the menu and run any item without the mouse, with
  the row hidden.
- **SC-005**: Turning "Show menu bar" on or off takes effect within one frame and survives a
  restart in 100% of tries.
- **SC-006**: On macOS, screenshots of the main window, the system menu and Settings →
  Appearance are unchanged from the previous release.

## Assumptions

- **Menu button when the row is shown**: the issue left this to review; decided with the
  contributor on 2026-11-12 — the button hides while the row is shown (FR-011), so there is one
  menu on screen and the transport bar keeps its width.
- **Default**: hidden for everyone on Windows / Linux, upgraders included, as the issue says
  ("hidden by default once the button exists"); there is no migration that keeps the row for
  existing users.
- **Where in Settings**: the setting goes on the Appearance page as it is laid out after #427
  (spec 088), next to the other window-layout choices (track headers side); hidden on macOS.
- **Project name in the transport**: already removed by the new look (#296); nothing to remove
  here, and no transport-fit code depends on it.
- **Linux title**: Linux keeps the native frame (no own title bar), so part (1) needs no change
  there.
- **#41** (hiding the native title bar and putting the Menu button in the title bar) is out of
  scope.
- **Docs**: the user documentation (nota-docs, EN + RU), FEATURES.md, CHANGELOG `[Unreleased]`
  and the Shortcuts screen are updated as part of this change.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI-only change, nothing on the audio thread
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no engine change
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change; UI-level tests cover the menu equivalence
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
