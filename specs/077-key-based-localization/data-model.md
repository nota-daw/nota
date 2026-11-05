# Data Model: Key-Based Interface Texts and Downloadable Languages

File shapes are in [contracts/](contracts/); this page is the entities and their states.

## Key

- `string`, regex `^[a-z0-9_]+(\.[a-z0-9_]+)+$`, ≤ 80 chars ([research R2](research.md)).
- Shape: `text` or `plural`; placeholder set `{0}…`.
- Identity is the string; it never changes when the English text changes. Changing shape or
  placeholders ⇒ a new key (the old one is removed).

## English reference (`en.json`, embedded)

- All keys with English text / plural object.
- Derived: **key-set hash** (contract language-file § Key-set hash).
- Published per Nota release into `nota-translations/reference/<version>.json` by pull.

## TranslationTable (in memory)

- From any language file: `Code`, `Name`, `PluralRule`, `Notes`, `Entries: key → Translation(Other, One?)`,
  `Problems: list<(key, problem)>` (invalid entries are dropped, not fatal).
- Load errors (whole file rejected): bad JSON, `formatVersion ≠ 2`, bad `code`, unknown
  `plural`, duplicate key.

## Installed language (`<data>/languages/`)

| File | Content |
|---|---|
| `<code>.json` | The downloaded language file (repository source only) |
| `<code>.meta.json` | `{ "source": "repository" \| "local", "path"?: "<abs path>", "version"?: 12, "commit"?: "…", "sha256"?: "…", "installed": "<ISO time>" }` |
| `.staging-<code>.json` | Transient during download; removed on start if left over |

State per code:

```
not installed ──download ok──▶ installed(repository, v)
installed(repository, v) ──update ok──▶ installed(repository, v')
installed(*) ──remove──▶ not installed          (a running one stays loaded until restart)
not installed / installed ──load file──▶ installed(local, path)
installed(local) ──file missing/invalid at start──▶ English this run, problem shown
download / update failure ──▶ previous state unchanged
```

## Catalog entry (from `index.json`)

`Code`, `Name`, `Version`, `Commit`, `Path`, `Size`, `Sha256`, `Coverage: hash → %`.
Combined with installed state into the row's items ([contracts/language-row.md](contracts/language-row.md)).

## Settings

Unchanged: `Settings.Language` (code), `Settings.CheckForUpdates` (drives R10). No new setting.

## Pending start-up download (story 5)

Not persisted. At start: stored code ≠ `en`, no valid installed file, `CheckForUpdates` on →
background fetch of index + file after the main window is shown; on success the row and the
status bar offer **Restart**.
