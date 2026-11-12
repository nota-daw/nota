# Data Model: spec 092

## SampleFolder (settings)

Stored in `Settings.SampleFolders` (settings JSON, `Nota.Application/Settings.cs`).

| Field | Type | Rules |
|---|---|---|
| `Path` | string | Absolute, normalised (no trailing separator). `""` only for the migrated "default location" entry, resolved to `~/Music/Nota Samples`. Unique (OS path comparer). |
| `Name` | string? | Display label; null/empty → folder name. Never renames on disk. |
| `IsDefault` | bool | Exactly one `true` in the list. Pack installs and Mosaic `samples:` refs use it. |

Derived at runtime (not stored): `Resolved` (absolute path), `Available` (directory exists
and is readable at the last refresh), `Label` (Name ?? folder name).

**Invariants**

- List has ≥ 1 entry; exactly one default (repaired on load: first entry).
- No entry is equal to, inside, or containing another (checked on add; on load an
  offending later entry is dropped with a log line).
- `Settings.SamplesFolder` is rewritten on every save to the default entry's `Path`
  (downgrade compatibility).

**Transitions**: Add (validate → append → save → rebuild tree + re-watch index) · Remove
(non-default, or default when another exists → confirm → next becomes default) · Rename
(label only) · Set default · Availability refresh (tab shown, Settings opened, start).

## Browse state (settings + session)

| Field | Where | Rules |
|---|---|---|
| `FilesBrowseMode` | Settings (bool) | Tree = false (default). |
| `FilesBrowseFolder` | Settings (string) | Last browsed folder; missing → default sample folder. `""` = Computer level is never persisted (falls back to default folder). |
| Current location | memory | A folder path, or the **Computer** pseudo-location (volume list). |
| Back / Forward stacks | memory | Navigating to a new location pushes current onto Back and clears Forward; Back/Forward move between stacks; capped at 100. |

## Clip source (project)

`AudioClipDto.Source : string?` and `AudioTakeDto.Source : string?` in `project.json`
(arrangement clips, comp takes, session audio slots' `Audio`).

| Rule | |
|---|---|
| Written | when `engine.SampleSource(sampleId)` is non-empty. |
| Not written | recordings, consolidate/bounce/stems/reverse renders, files loaded from a bundle's `samples/`. |
| Read | after the clip's sample is loaded from the bundle: `engine.SetSampleSource(id, Source)`. |
| Format | optional, additive, no version bump; absent = unknown. |

Runtime: `NotaEngine._sampleSources[sampleId] = fullPath` (beside `_sampleNames`).

## Project entry (Projects tab)

`BrowserItem` (Kind = Project) gains:

| Field | Type | Source |
|---|---|---|
| `Modified` | DateTime? | `project.json` last-write time, else bundle dir time; null sorts last. |
| `Summary` | ProjectSummary? | Background read / cache; null until read or when unreadable. |
| `IsOpen` | bool | `Path` equals the open project's path. |

`ProjectSummary`: `Bpm : double`, `Key : string?`, `TrackCount : int`,
`LengthBeats : double` (shown as m:ss at `Bpm`).

Cache file `NOTA_DATA_DIR/cache/project-summaries.json`:
`{ "<bundle path>": { "stamp": "<manifest ticks>:<size>", "bpm": …, "key": …, "tracks": …, "lengthBeats": … } }`.
Entries for vanished bundles are pruned on write.

Settings: `ProjectsSort : "Date" | "Name"` (default `Date`).

## BrowserItem additions (Files)

| Field | Meaning |
|---|---|
| `PlaceIndex : int` | ≥ 0 on a top-level sample-folder node (index into `SampleFolders`), else −1. |
| `Unavailable : bool` | The place's folder is missing/unreadable; row dimmed, children empty. |
| `Located : bool` | The row is the selected clip's source file (highlight). |
