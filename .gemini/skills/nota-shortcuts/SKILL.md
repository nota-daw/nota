---
name: nota-shortcuts
description: Keep the Preferences → Shortcuts screen in sync with the app's real keyboard/mouse bindings. Run this WHENEVER you add, remove, or change a keyboard shortcut or mouse gesture (transport keys, editing commands, piano-roll keys, the computer-keyboard note map, device drag, etc.) — since specs/096 every key is a binding in the key-action catalog, so a new key must be declared there (not compared by hand in a handler) and a new gesture listed in ShortcutLayout. Handles: declaring the action, asking for it in the handler, the fixed rows, tests and visual verification.
user-invocable: true
---

# Keeping the Shortcuts screen honest

Since specs/096 (#61) every keyboard shortcut is **rebindable** in Settings → Shortcuts.
A key is no longer compared by hand in a handler and then described in a list: it is a
**key action** with an id, a scope and a default, and the handler asks the user's bindings
whether a press is that action. The Shortcuts page is built from the catalog, so an action
can't be missing from it — but a key compared by hand, or a gesture nobody listed, still can.

**Trigger:** you touched anything that changes what a key or mouse gesture does — a new
hotkey, a re-mapped default, a removed command, the computer-keyboard note layout, a new
drag gesture.

## 1. Where things live

| What | File |
|---|---|
| Palette / menu commands and their default keys | `src/managed/Nota.Application/Palette/CommandCatalog.cs` (the gesture is the *default*) |
| Every other key action (note map, nudges, transposes, browser, Modular, Session…), scopes, shared editing actions | `src/managed/Nota.Application/Input/KeyActionCatalog.cs` |
| The user's keys, conflict rule, reset, persistence | `src/managed/Nota.Application/Input/KeyBindings.cs` |
| What the page lists, in which group; the **fixed rows** (mouse, trackpad, gamepad, list and typing keys) | `src/managed/Nota.Application/Input/ShortcutLayout.cs` |
| Asking in a handler: `Keys.Is(e, "roll.transposeUp")`, `Keys.Which(e, …)` | `src/managed/Nota.App/Input/Keys.cs` |
| The editor (capture, conflicts, reset) | `src/managed/Nota.App/PreferencesWindow.Shortcuts.cs` |

Scopes (research R3 in `specs/096-custom-shortcuts`): Global, Arrangement and Notes are
window-wide and conflict with each other; Session, PianoRoll, AudioEditor, Devices, Browser and
Modular are focus scopes that win while they have the keyboard. A menu chord reaches every
scope. Copy / Cut / Paste / Duplicate / Delete / Select all / Cancel (`edit.*`) are **shared**
— one binding read by every view, never a per-view action.

## 2. Procedure

1. **New key**: add a `KeyAction` (an existing command: give `CommandCatalog` its gesture; a
   view key: `Add(...)` in `KeyActionCatalog` with a literal `Loc.T("keys.…")` title and the
   right scope). Paired keys are one action per direction.
2. In the handler, ask `Keys.Is(e, id)` / `Keys.Which(e, ids)` — never `e.Key == Key.X`. Keep
   the handler's focus gates and order. List-navigation and typing keys (arrows in a list,
   Return / Esc / Tab in a text box) stay fixed and are compared directly.
3. **New gesture or fixed key**: add an `F(key, text)` row to its group in `ShortcutLayout`.
4. Default keys must not collide (rule R3) — `KeyBindingTests` lists the pairs allowed on
   purpose; a new one fails until it is added there with its reason.
5. Build and run `dotnet run --project tests/Nota.SmokeTest -c Release -- --shortcuts` and
   `-- --palette` (every action is laid out once), and `-- --localization` (new strings).
6. Visually verify with the headless harness (memory: nota-ui-verify) —
   `PreferencesWindow.ShowPage("Shortcuts")`, a filter, and `StartCapture` / `Propose` by
   reflection for the editor states.

## 3. Don't forget

- User-visible → `CHANGELOG.md` `[Unreleased]`, and the user docs: `npm run shortcuts` in
  nota-docs regenerates `reference/shortcuts` from the layout.
- Tooltips naming a key: `Keys.SetTip(control, text, id)` (the default glyph in the text is
  swapped for the user's key). Context-menu hints: `MenuKit.Bound(id)`.
