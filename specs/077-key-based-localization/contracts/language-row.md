# Contract: Preferences → Appearance → Language row

Extends the 068 row (combo + "Applies after restart" + **Restart**). Studio design: neutral
greys, orange only for the selected / live item, sentence case, no dialogs.

## Combo items

| Item | Shown as | Picking it |
|---|---|---|
| English | `English` | as today |
| Installed (repository) | `Français` · `97%` (muted) | as today (restart note) |
| Installed (local file) | `Français` · `local file` | as today |
| Available, not installed | `Deutsch` · `82%` · download glyph | downloads; row shows `Downloading…` + progress; then the restart note; on failure `Couldn't download Deutsch — <reason>` and the combo returns to the running language |

Order: English, installed by code, a separator, available by code.

## Row actions (beside the combo, only when relevant)

| Condition | Shows |
|---|---|
| Stored language newer in the index | `Update available` + **Update** (downloads, then restart note if it is the running one) |
| Selected item is an installed non-English language | **Remove** (a running one keeps working until restart, then English) |
| Always | **Load file…** (opens a JSON file; problems listed below the row) |
| Index unreachable or unreadable | `Couldn't get the language list — <reason>` (muted); with checks off **Show available languages** stays to retry |
| A download in progress | the list and **Load file…** are disabled until it ends |
| A running language updated this run | the restart note, as for a new choice |
| `CheckForUpdates` off and index not fetched this run | **Show available languages** |
| Stored language missing, checks on | `Downloading Français…`, then `Français is ready` + **Restart** (story 5) |
| Stored language missing, checks off or offline | `Français will be downloaded when Nota is online` / **Download Français** |

The status bar shows the story-5 "ready — Restart" notice once per run, the same way the app
update notice appears (`MainWindow.Updates.cs`).

## Problems list (local file)

Up to 10 lines `key — problem`, then `…and N more (see the log)`.
