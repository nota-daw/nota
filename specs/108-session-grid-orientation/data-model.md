# Data model: Session view — turn the grid (spec 108)

No project-file or engine data changes. Three small pieces of state:

## SessionGrid (desktop app setting)

| Field | Where | Values | Default | Notes |
|---|---|---|---|---|
| `SessionGrid` | `Settings` (`src/managed/Nota.Application/Settings.cs`), app settings JSON | `"down"`, `"across"` | `"down"` | Unknown / missing value reads as `"down"`. Not undoable, not in the project. |

Derived: `SessionAcross = SessionGrid == "across"` — read by `SessionView.Refresh()` and by the
Remote host (`IRemoteHost.SessionAcross`).

## SessionStep (desktop, transient)

`PrevScene | NextScene | PrevTrack | NextTrack` — a selection move by grid meaning.

| Input | Scenes down | Scenes across |
|---|---|---|
| ↑ | PrevScene | PrevTrack |
| ↓ | NextScene | NextTrack |
| ← | PrevTrack | PrevScene |
| → | NextTrack | NextScene |
| `session.selectUp` / `Down` | PrevScene / NextScene | PrevScene / NextScene |
| `session.selectLeft` / `Right` | PrevTrack / NextTrack | PrevTrack / NextTrack |

Rules (unchanged from today, now by meaning): PrevTrack on the first track → the scene;
NextTrack on the scene → the first track; NextTrack past the last track → the first Return /
Master; on a Return / Master, Prev/NextTrack walk them (PrevTrack off the first → last track) and
Prev/NextScene do nothing.

## Remote session-grid choice (per device)

| Client | Field | Values | Default | Migration |
|---|---|---|---|---|
| Nota Remote (localStorage `prefs`) | `sessionGrid` | `'nota'`, `'down'`, `'across'` | `'nota'` | old `sessionAxis: 'across'` → `'across'`; else `'nota'`; `sessionAxis` dropped |
| Nota NX (config) | `prefs.sessionGrid` | `0` As in Nota, `1` down, `2` across | `0` | absent → `0` |

Effective axis = the device's choice, or, for "As in Nota", the `ax` field of the last `ses`
message (absent → down).
