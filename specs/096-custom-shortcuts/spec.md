# Feature Specification: Custom keyboard shortcuts

**Feature Branch**: `feat/custom-shortcuts`

**Created**: 2026-11-13

**Status**: Draft

**Input**: Issue [#61](https://github.com/nota-daw/nota/issues/61) (approved for 0.60.1):
"Custom keyboard shortcuts: edit, detect conflicts, mark changed, reset to default". The
maintainer's scope comment widens it: **every keyboard shortcut in Nota is rebindable** in
Preferences → Shortcuts, not only the command-palette commands; no read-only keyboard rows
remain. Mouse gestures stay read-only. Export / import of shortcut sets and presets
("Ableton-like") are out of scope.

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

**Where things stand today**: about a hundred menu / palette commands carry a default key
in the command catalog, and the menu reads its keys from there. Everything else — piano-roll
and audio-clip editing keys, Session keys, track / device / browser / Modular / rack keys,
grid keys, the computer-keyboard note map, transport keys handled directly in the windows'
key handlers — is hard-coded where it is handled and only *described* in a hand-written
list in Preferences → Shortcuts, mixed with mouse gestures. That list is read-only.

## Clarifications

### Session 2026-11-13

- Q: How many keys per action? → A: A primary plus one optional alternative (FR-009).
- Q: Shared editing actions (Copy, Cut, Paste, Duplicate, Delete, Select all) — one row or
  one per context? → A: One binding each, live in every context that supports it (FR-010).
- Q: List-navigation and text-entry keys — rebindable? → A: No, fixed and shown read-only
  (FR-011).
- Planning refinement (research R3): a context with keyboard focus deterministically wins
  over the window-wide keys, so reuse across those layers is allowed and shown as a note;
  window-wide scopes overlap each other; a key that a menu item carries reaches every scope
  (on macOS the menu fires first). Pairs that ship in the defaults are never reported.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Rebind a shortcut (Priority: P1)

A producer coming from another DAW wants "Split" on ⌘T instead of ⌘E. They open
Preferences → Shortcuts, find "Split at playhead", click its key, press ⌘T, confirm. From
then on ⌘T splits, ⌘E does nothing, the Edit menu shows ⌘T, and the setting survives a
restart.

**Why this priority**: it is the feature; everything else protects or explains it.

**Independent Test**: rebind one command and one non-catalog key (for example piano-roll
"transpose up"), restart the app, press both new keys in their views — the actions fire;
the old keys do not.

**Acceptance Scenarios**:

1. **Given** the Shortcuts page, **When** the user clicks a shortcut's key, **Then** the
   row enters a visible capture mode ("Press a shortcut…") and no app shortcut fires while
   it waits.
2. **Given** capture mode, **When** the user presses ⌘T, **Then** the proposed combination
   is shown in the row with Confirm / Cancel / Clear controls, and nothing is saved yet.
3. **Given** a proposed combination, **When** the user confirms, **Then** the action
   answers the new key everywhere it applies, the old key is free, and the binding is kept
   across restarts.
4. **Given** capture mode, **When** the user cancels, **Then** the previous binding stays
   exactly as it was.
5. **Given** a shortcut, **When** the user chooses Clear, **Then** the action has no key
   (it stays reachable from menus / the palette) and the row shows it as unassigned.
6. **Given** the user rebinds a menu command on macOS, **Then** the menu bar shows the new
   key equivalent immediately, without a restart.
7. **Given** a rebound key, **Then** tooltips that mention the key and the command
   palette's key hints show the new key.

---

### User Story 2 - Conflicts are caught before saving (Priority: P1)

The user presses ⌘D for an arrangement action, but ⌘D is Duplicate. Before anything is
saved the row says "⌘D is used by Duplicate" and offers *Replace* (Duplicate loses ⌘D) or
*Cancel*. A piano-roll key may reuse an arrangement key — the piano roll wins while it has
the keyboard, so that is not a conflict.

**Why this priority**: without it, rebinding silently breaks other actions or makes one
key fire two things.

**Independent Test**: assign a key already used in an overlapping scope → conflict shown,
Replace moves it and leaves the other action unassigned; assign a key used only in a
non-overlapping scope → accepted with at most an informational note.

**Acceptance Scenarios**:

1. **Given** a proposed combination used by another action whose scope overlaps (FR-012),
   **When** it is proposed, **Then** the row names every affected action and its scope and
   offers Replace / Cancel; Confirm alone is not available.
2. **Given** Replace, **Then** each conflicting action loses that key (becomes unassigned
   and marked edited) and the new binding is saved — as one change.
3. **Given** a combination used only by actions in scopes that don't overlap with this one,
   **Then** it is accepted; when the other use is in a different layer the row notes where
   else the key is used and which one wins.
4. **Given** an OS-reserved combination (⌘Q, ⌘H, ⌘Tab, ⌘Space on macOS; Alt+F4, Alt+Tab,
   Win-key combinations on Windows; equivalents on Linux), **Then** it is refused with a
   short reason, and nothing changes.
5. **Given** a bare modifier (⇧ alone, ⌘ alone), **Then** capture keeps waiting for a
   complete combination.

---

### User Story 3 - See what differs from default and get back (Priority: P2)

After a few weeks of tweaking, the user wants to see what they changed and undo one or all
of it.

**Why this priority**: personalisation without a way back is risky, but the feature is
usable without it.

**Independent Test**: edit three shortcuts, turn on "Edited only" — exactly those three
show, each with an edited mark and its default; reset one; Reset all → confirm → none
remain edited.

**Acceptance Scenarios**:

1. **Given** an edited shortcut (changed or cleared), **Then** its row carries an accent
   mark and shows the default key next to the current one.
2. **Given** the filter, **When** "Edited only" is on, **Then** only edited rows show; the
   text filter matches action names and current keys (typing "⌘T" or "cmd t" finds the
   action bound to ⌘T).
3. **Given** an edited row, **When** the user presses its Reset button, **Then** the default
   comes back; if the default key is now used by another action in an overlapping scope,
   the same conflict prompt as in Story 2 appears first.
4. **Given** Reset all to defaults, **Then** a confirmation explains that every custom key
   will be replaced; on confirm all bindings return to defaults and no row is marked.
5. **Given** a row whose binding was changed back by hand to its default, **Then** it is no
   longer marked edited.

---

### User Story 4 - Play notes on a non-US keyboard (Priority: P3)

A user on an AZERTY or Cyrillic layout rebinds the computer-keyboard note map (white keys,
black keys, octave down / up, velocity down / up) to keys that sit where they want them.

**Why this priority**: valuable for a specific group; the note map is a set of many keys
that otherwise behaves like any other binding.

**Independent Test**: rebind the C note and octave-up, arm an instrument track, press the
new keys — the right notes sound; the old keys don't.

**Acceptance Scenarios**:

1. **Given** the note-map rows, **Then** each note and each octave / velocity key is its
   own rebindable row.
2. **Given** a note key bound to a letter, **Then** it plays only where computer-keyboard
   notes already play today, and the conflict check treats it like any key in that scope.

---

### Edge Cases

- Settings hold a custom key for an action that no longer exists after an update: it is
  ignored (no crash) and kept out of the list; nothing else is lost.
- A new version adds an action whose default key a user has already assigned to another
  action: the user's binding wins; the new action's default is inactive and the Shortcuts
  page lists it under "Needs a decision".
- A new version changes the default of an action the user never touched: the user gets the
  new default (only edited bindings are stored).
- The settings file is damaged or hand-edited with an unparsable key: that entry is dropped
  with a log line; every other binding loads.
- Capture is started and the Preferences window loses focus: capture cancels.
- Esc during capture cancels it; Esc can still be *assigned* through the explicit
  "Assign Esc" option offered in capture mode.
- Keyboard layouts: a binding is recorded as the key Nota receives when it is pressed, so it
  always matches that same key later on the same layout; it is shown with platform modifier
  names (⌘⌥⇧ on macOS, Ctrl / Alt / Shift on Windows and Linux).
- While a text field has focus, typing goes to the field as today; no binding hijacks it.
- Two actions in overlapping scopes end up with the same key through an old settings file:
  on load the user's later one keeps it and the other is listed under "Needs a decision".

## Requirements *(mandatory)*

### Functional Requirements

**One catalog of bindings**

- **FR-001**: Every keyboard binding in Nota MUST come from one binding catalog: each
  action has a stable id, a name, a group (for the list), a scope (where it works) and a
  default combination — or none. The code that handles a key MUST ask the catalog which
  action a combination means instead of comparing keys itself.
- **FR-002**: The catalog MUST cover, at minimum: the menu / palette commands; transport
  keys; window and view keys; arrangement editing (nudge, move to track, delete, …); grid
  keys; track, device, browser-files, rack, Modular and Session keys; piano-roll and
  audio-clip-editor keys; the computer-keyboard note map (each note, octave down / up,
  velocity down / up); the command-palette toggle. Paired keys ("← →", "⌘Z ⌘⇧Z") become one
  action per direction.
- **FR-003**: Scopes MUST express where a key is live: window-wide scopes (Global,
  Arrangement, computer-keyboard Notes) and focus scopes that handle their own keys while
  they have the keyboard (Session grid, Piano roll, Audio clip editor, Devices, Browser,
  Modular). Which scopes overlap MUST be defined once and used by the conflict check and the
  key dispatch alike.
- **FR-004**: Mouse gestures (drag, click, modifier + click, wheel, pinch) and the fixed
  navigation / text keys of FR-011 MUST stay in the list as read-only rows, visually
  distinct from editable rows.

**Editing**

- **FR-005**: Clicking a keyboard row's key MUST enter capture mode for that row only;
  while capturing, no app shortcut, menu key equivalent or note-map key fires.
- **FR-006**: Capture MUST show the proposed combination before it is saved, with
  Confirm, Cancel and Clear (unassign); Esc cancels, and an explicit option assigns Esc.
- **FR-007**: Capture MUST accept letters, digits, punctuation, function keys, arrows,
  navigation keys, Space, Return, Tab, Delete / Backspace, with any combination of the
  platform's modifiers; a bare modifier MUST NOT complete capture.
- **FR-008**: OS-reserved combinations MUST be refused with a short reason. The reserved
  list is per platform (research R5).
- **FR-009**: Each action MUST have a primary combination and MAY have one alternative
  combination; both are edited, conflict-checked, marked and reset the same way. Today's
  pairs (Delete and Backspace) become primary + alternative of one action.
- **FR-010**: Shared editing actions — Copy, Cut, Paste, Duplicate, Delete, Select all, and
  Esc's cancel / deselect — MUST each be one action with one binding that applies in every
  context that supports it (tracks, devices, arrangement clips, notes, Session, Modular).
  Their scope is Global for the conflict check.
- **FR-011**: List-navigation and text-entry keys MUST stay fixed: arrows / PgUp / PgDn /
  Return / Tab / ⇧Tab / Esc inside the command palette and lists (Session selection arrows,
  the browser's Return), and Return / Esc / Tab while typing a value or renaming. They are
  listed read-only next to the mouse gestures. Context keys that act on the song (nudge
  clips with ← →, transpose notes with ↑ ↓, Esc to cancel a gesture or clear a selection,
  Return to launch a Session slot) are not navigation keys and stay rebindable.

**Conflicts**

- **FR-012**: Before saving, the editor MUST find every action that would answer the
  proposed combination at the same time as the edited action — same scope, both
  window-wide, or either carried by a menu item — and name each with its scope. Pairs where
  both bindings are the shipped defaults are not reported.
- **FR-013**: On conflict the user MUST choose Replace (the other actions lose that key) or
  Cancel; a conflicting binding is never saved silently.
- **FR-014**: Combinations used only in non-overlapping scopes MUST be accepted; a reuse
  across layers (focus vs window-wide) is shown as a note saying which wins.
- **FR-015**: Replace, a single reset and Reset all MUST each apply as one change: either
  every affected binding changes, or none does.

**Visibility and reset**

- **FR-016**: A binding that differs from its default (including cleared) MUST be marked
  in its row, with the default shown; the mark MUST disappear when the binding equals its
  default again.
- **FR-017**: The Shortcuts filter MUST offer "Edited only" and MUST match action names,
  group names and current key combinations (both symbol and spelled forms, e.g. "⌘T" and
  "cmd t").
- **FR-018**: Each edited row MUST have a Reset control; resetting MUST run the conflict
  check against the default key first.
- **FR-019**: "Reset all to defaults" MUST ask for confirmation, then return every binding
  to its default.

**Persistence and propagation**

- **FR-020**: Only bindings that differ from the default MUST be stored, keyed by action
  id, in the app settings; unchanged actions follow the defaults of the running version.
- **FR-021**: Stored bindings for unknown action ids or unparsable keys MUST be skipped
  without losing the rest; collisions introduced by new defaults MUST surface on the
  Shortcuts page ("Needs a decision") rather than being resolved silently.
- **FR-022**: A changed binding MUST take effect at once: key handling, the macOS menu key
  equivalents, menu shortcut text on Windows / Linux, tooltips that mention keys, and the
  command palette's key hints.
- **FR-023**: Default behaviour MUST NOT change: with no custom bindings every key does
  exactly what it does today.

### Key Entities

- **Action**: something a key can trigger. Id (stable across versions), name, group,
  scope, default primary and alternative combinations (optional).
- **Scope**: where an action's key is live; window-wide or focus layer.
- **Key combination**: a key plus modifiers, shown in platform notation.
- **Custom binding**: a stored override for one action slot — a combination or "unassigned".
- **Conflict**: a proposed combination plus the set of actions in overlapping scopes that
  already answer it.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of the keyboard rows in Preferences → Shortcuts are editable except the
  fixed list-navigation / text-entry keys (FR-011); the other read-only rows are mouse /
  trackpad gestures and gamepad buttons.
- **SC-002**: A user can find an action and rebind it in under 20 seconds.
- **SC-003**: No combination ever triggers two actions at once, under any sequence of
  edits, replaces and resets.
- **SC-004**: With no custom bindings, every key in every view behaves exactly as before
  (existing smoke and UI checks pass unchanged).
- **SC-005**: Custom bindings survive restart and an app update; a binding for a removed
  action never prevents the app from starting.
- **SC-006**: After a rebind, the menu, tooltips and palette show the new key with no
  restart.

## Assumptions

- Gamepad buttons are not keyboard shortcuts; they stay read-only (they are mapped through
  MIDI learn).
- Export / import of shortcut sets and presets are out of scope (issue: "optional later").
- Bindings are app-wide (one set per user), not per project.
- Undo of shortcut edits is the per-row Reset and Reset all, not the project's undo
  history (project undo is about the song).
- Layout-independent "position" bindings and multi-key chords (press A, then B) are out of
  scope.
- Keypad digits act as the digit keys (as today, ⌘1 and keypad ⌘1 both narrow the grid).
- The Russian translation of new strings follows the usual reference-sync step.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency
- [x] Audio-thread impact considered: managed-only UI / input change; nothing reaches the audio thread
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7) — no C ABI change
- [ ] Smoke-test impact assessed: no engine change; the palette smoke test (every shortcut listed in Settings → Shortcuts) is extended to the catalog
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry, FEATURES.md, user docs (nota-docs) and the Shortcuts list (nota-shortcuts)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
