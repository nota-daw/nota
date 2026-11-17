# Contract: the key-action catalog (096)

Every editable row on Preferences → Shortcuts. Ids are stable (they key the user's settings).
Command-catalog commands keep their ids and gestures as defaults; only the ones handled by
view code or given a new scope are listed here, plus every new non-command action.
"Alt." = default alternative key. ⌘ = Cmd on macOS, Ctrl elsewhere.

## Shared editing actions (Global; read by every view that supports them — FR-010)

| Id | Default | Alt. | Menu key |
|----|---------|------|----------|
| edit.copy | ⌘C | | no |
| edit.cut | ⌘X | | no |
| edit.paste | ⌘V | | no |
| edit.duplicate | ⌘D | | yes |
| edit.delete | ⌫ | ⌦ | no |
| edit.selectAll | ⌘A | | yes |
| edit.cancel *(new)* | Esc | | no |

Consumers: arrangement clips / time range / automation, track headers, device chain + rack
chains, piano roll notes, controller lanes (delete / cancel), Session grid, Modular (duplicate,
delete, cancel = show fewer parameters), audio clip editor (cancel = drop warp drag), rack
cards (cancel = end macro mapping).

## Window layer

| Id | Scope | Default | Notes |
|----|-------|---------|-------|
| transport.* (catalog) | Global | as today | Space, ⇧Space, Return, ⌘R, ⌘., ⌘L, ⌘M, ⌘⇧R |
| view.cycleDetailTab *(new)* | Global | Tab | detail panel last used |
| view.openMenu *(new)* | Global | F10 | Windows / Linux only; the ⌥ tap stays fixed |
| arr.nudgeLeft / arr.nudgeRight *(new)* | Arrangement | ← / → | |
| arr.nudgeFineLeft / arr.nudgeFineRight *(new)* | Arrangement | ⌥← / ⌥→ | |
| arr.moveToTrackUp / arr.moveToTrackDown *(new)* | Arrangement | ⌥↑ / ⌥↓ | |
| edit.toggleClipActive (catalog) | Arrangement | 0 | keypad 0 matches too |
| view.showPlayhead (catalog) | Arrangement | P | |
| notes.c, notes.cs, notes.d, notes.ds, notes.e, notes.f, notes.fs, notes.g, notes.gs, notes.a, notes.as, notes.b, notes.c2 *(new)* | Notes | A W S E D F T G Y H U J K | |
| notes.octaveDown / notes.octaveUp *(new)* | Notes | Z / X | |
| notes.velocityDown / notes.velocityUp *(new)* | Notes | C / V | |

## Focus layer

| Id | Scope | Default |
|----|-------|---------|
| devices.group / devices.ungroup *(new)* | Devices | ⌘G / ⌘⇧G |
| browser.back / browser.forward / browser.up *(new)* | Browser | ⌘[ / ⌘] / ⌘↑ (alt. off macOS: Alt+← / Alt+→ / Alt+↑) |
| browser.editPath *(new)* | Browser | ⌘L |
| modular.fit / modular.bypass *(new)* | Modular | F / B |
| session.launchSelection *(new)* | Session | Return |
| session.rename *(new)* | Session | F2 |
| session.insertScene (catalog) | Session | ⌘I |
| roll.moveLeft / roll.moveRight *(new)* | PianoRoll | ← / → |
| roll.transposeUp / roll.transposeDown *(new)* | PianoRoll | ↑ / ↓ |
| roll.octaveUp / roll.octaveDown (catalog) | PianoRoll | ⇧↑ / ⇧↓ |
| roll.shorten / roll.lengthen *(new)* | PianoRoll | ⇧← / ⇧→ |
| roll.velocityUp / roll.velocityDown *(new)* | PianoRoll | ⌥↑ / ⌥↓ |
| roll.velocityUpFine / roll.velocityDownFine *(new)* | PianoRoll | ⌥⇧↑ / ⌥⇧↓ |
| roll.fitNotes / roll.fold (catalog) | PianoRoll | ⇧F / ⌥⇧F |

## Fixed (read-only rows)

Mouse / trackpad gestures; gamepad; the command palette's list keys (↑ ↓ PgUp PgDn Return
⌘Return ⌥Return ⇧Return Tab ⇧Tab Esc, the `> @ + # ~` prefixes); Session selection arrows;
the browser's Return (open folder) and preview ↑ ↓; Return / Esc / Tab while typing or
renaming; the ⌥ tap that opens the menu; macOS ⌘Q.

## Settings JSON

```json
"KeyBindings": {
  "edit.split": { "Primary": "⌘T", "Alternative": null },
  "notes.c": { "Primary": "Q" },
  "transport.metronome": { "Primary": "" }
}
```
