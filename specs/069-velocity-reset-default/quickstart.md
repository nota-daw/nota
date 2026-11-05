# Quickstart: validating the velocity reset

Prerequisites: the repo builds (`scripts/build.sh`). Run the app from the repo root: `dotnet run --project src/managed/Nota.App`.

## 1. Automated gate

`scripts/build.sh` runs the smoke test; the new `VelocityResetTests` must pass:

| Check | Proves |
|---|---|
| Default | the default is 0.8 and the "last touched" start value and the playable keys' value read it |
| One stem | a stem outside the selection targets only that note |
| Selection | a stem inside the selection targets every selected note |
| Locked | locked notes are never targets |
| Already default | a note at 0.8, or within half a MIDI step of it, is not a target; nothing to do |

The localization checks must stay green (French entry for each new text, no raw literal).

## 2. Manual: double-click (User Story 1, 2)

1. Open a MIDI clip in the piano roll, draw two notes, drag the first note's stem to the top.
2. Double-click that stem. Expect the stem at about 80 % height (`vel 102` in the note tooltip) and the sound follow.
3. ⌘Z once. Expect the stem back at the top, not slightly lower.
4. Select both notes, give them different velocities, double-click one of the two stems. Expect both at 0.8; ⌘Z once restores both.
5. Select one note, double-click the other, unselected, note's stem. Expect only that one reset and the selection to move to it.
6. Double-click a stem already at 0.8. Expect no visible change; ⌘Z undoes the step before, not a no-op.
7. Double-click empty lane space. Expect nothing.

## 3. Manual: menu (User Story 3)

1. Right-click a stem not at 0.8. Expect a menu with **Reset velocity**; choose it. Expect 0.8; ⌘Z restores.
2. Right-click a stem of a selected note. Expect the whole selection to reset.
3. Right-click a stem already at 0.8. Expect **Reset velocity** disabled.
4. Right-click empty lane space. Expect no menu.
5. Switch the tab to Pres or Bend. Expect their own right-click "Clear" menus and double-click add / remove point, unchanged.

## 4. Edge cases

- Play a loop while resetting: the next note plays at the new velocity, no glitch.
- Open a clip with a locked reference clip behind it: its stems do not react.
- A Session slot's roll: reset and ⌘Z behave the same (the engine receives the change within a second).
- Language French (`NOTA_UI_LANGUAGE=fr`): the menu item and the Shortcuts rows read in French.

## 5. Docs (FR-008)

Preferences → Shortcuts → Piano roll lists both gestures; `CHANGELOG.md` `[Unreleased]` has an Added entry; `FEATURES.md` and the user manual's piano-roll page (EN + RU) describe them.
