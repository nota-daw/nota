---
name: nota-shortcuts
description: Keep the Preferences → Shortcuts screen in sync with the app's real keyboard/mouse bindings. Run this WHENEVER you add, remove, or change a keyboard shortcut or mouse gesture (transport keys, editing commands, piano-roll keys, the computer-keyboard note map, device drag, etc.) — the Shortcuts pane is a hand-maintained list and silently drifts from the handlers otherwise. Handles: finding every place bindings are defined, updating the on-screen list, and visually verifying it.
user-invocable: true
---

# Keeping the Shortcuts screen honest

Nota shows its keyboard/mouse shortcuts in **Preferences → Shortcuts**. That list is
**hand-written**, not generated from the handlers — so every time the input handling
changes, the screen must be updated by hand or it lies to the user. This skill is the
checklist for doing that.

**Trigger:** you touched anything that changes what a key or mouse gesture does — a new
hotkey, a re-mapped key, a removed command, a changed computer-keyboard note layout, a new
drag gesture. If your change added/removed/moved a binding, you are not done until the
Shortcuts pane matches.

## 1. The on-screen list (what to edit)

`src/managed/Nota.App/PreferencesWindow.cs` → the static **`ShortcutGroups`** array (feeds
`ShortcutsPane`). It's grouped: `TRANSPORT`, `ARRANGEMENT & EDITING`, `PIANO ROLL`,
`PLAY NOTES (COMPUTER KEYBOARD)`, `MOUSE`. Each row is `(Key, Action)`; the `Key` string is
mono-rendered as a key-cap, the `Action` is a short sentence. Use `⌘` for Meta/Ctrl, `⇧`
for Shift, `⌥` for Alt, and the arrow glyphs `← → ↑ ↓`. Keep actions imperative and terse
(design language: sentence case, `·`/` / ` as separators).

## 2. The source of truth (where bindings actually live)

Read these and reconcile the list against them — do **not** trust the existing pane text:

| Bindings | File |
|---|---|
| Transport (Space/Return), global editing (A, R, M, ⌘M, ⌘G/⌘⇧G, ⌘C/X/V, Delete, Tab, Esc) and the **computer-keyboard note map** (`KeyToPitch`) | `src/managed/Nota.App/MainWindow.Input.cs` |
| Piano-roll note editing (arrows, ⇧ octave, ⌘A/⌘D, ⌘C/X/V, Delete, Esc) | `src/managed/Nota.App/PianoRollView.cs` (its `OnKeyDown`) |
| Arrangement lane / clip gestures (double-click, drag) | `src/managed/Nota.App/ArrangementView*.cs` |
| Device reorder drag | `src/managed/Nota.App/DeviceChainView.cs`, `DeviceCards/…` |

`KeyToPitch` is the note layout: bare letters play notes, and `A` is **deliberately not** a
note (it toggles automation mode) — so the "Play notes" rows must never list `A`. When that
map changes, update the `PLAY NOTES` group (white vs. black keys, and the starting octave).

## 3. Procedure

1. List, from your own diff, every binding you added/changed/removed.
2. Open the source-of-truth file(s) above and confirm the *current* behaviour of each key in
   the affected group — including ones you didn't touch, since the list may already be stale.
3. Edit `ShortcutGroups` so every row matches. Add a row for a new binding, delete a row for a
   removed one, fix the key-cap or wording for a changed one. Put it in the right group.
4. Build: `dotnet build src/managed/Nota.App -c Debug`.
5. Visually verify (see [nota-ui-verify] memory): the pane can't be reached by a click in a
   headless run, so temporarily open it via an env-guarded hook in
   `MainWindow.OnDataContextChanged` (`new PreferencesWindow(new SettingsViewModel(settings),
   _vm).Show(this)`), and select the Shortcuts section (index 5) — e.g. a temporary env read
   in front of the constructor's `Select(0)`. Screenshot with `screencapture -x -o -l<winid>`
   (get the id from the `winlist` swift helper), check the rows read correctly, then **revert
   the temp hooks** and rebuild.

## 4. Don't forget

- If the change is user-visible, it also wants a `CHANGELOG.md` entry under `## [Unreleased]`.
- Keep the pane concise — it's a reference, not a manual. Group related keys onto one cap
  (`⌘C  ⌘X  ⌘V`) rather than three rows when the action is one idea.
