# Data Model: Articulation maps

Three representations of the same data, each for one job:

1. **Managed model** (`Nota.Application.Articulations`): editing, import, export, UI.
2. **Map file / project JSON**: the managed model serialised (see
   `contracts/nota-articulation-map-file.md`).
3. **Compiled engine table**: flat, immutable, read by the audio thread (see
   `contracts/c-abi.md`).

## Entities

### ArticulationMap
| Field | Type | Rule |
|---|---|---|
| Id | string (32 hex) | Stable across rename and export; a duplicate gets a new one |
| Name | string | Required, non-empty |
| InstrumentName | string | Library or instrument it is for; may be empty |
| Description | string? | Optional |
| Version | int | Format version of the serialised map (now 1) |
| DefaultArticulationId | int? | Must name an existing articulation or be null |
| Groups | list of ArticulationGroup | Ids unique, > 0 |
| Articulations | ordered list of Articulation | Order is display order |
| Metadata | string map | Provenance (`source.format`, `source.file`, `source.comments`); never interpreted |

### Articulation
| Field | Type | Rule |
|---|---|---|
| Id | int > 0 | Unique within the map, **never reused** after a delete (`NextArticulationId` = max + 1) |
| Name, ShortName | string | Name required; short name defaults to a prefix of the name |
| ColorArgb | uint? | `0xAARRGGBB`; null = Nota's default colour for this slot |
| SourceColorIndex | int? | Cubase palette index kept from the file; not RGB |
| GroupId | int | 0 = none, else an existing group id |
| Trigger | Latching or Momentary | Default Latching |
| Actions | ordered list of MidiAction | Order is send order; empty is allowed (sends nothing) |
| NoteOffsetMs | double | Added to the track offset; clamp ±2000 |
| TriggerLeadMs | double ≥ 0 | Clamp 0..2000 |
| Enabled | bool | Disabled articulations are skipped (note plays as default) |
| Metadata | string map | Descriptions, original ids, unsupported constructs, raw activation strings |

### MidiAction
| Field | Type | Rule |
|---|---|---|
| Kind | Keyswitch, ControlChange, ProgramChange, BankSelectMsb, BankSelectLsb | |
| Channel | int? (1..16) | null = the note's own channel |
| Data1 | int | Keyswitch pitch / CC number / program; 0..127 |
| Data2 | int | Keyswitch velocity / CC value / bank value; 0..127 |

Validation (`MidiAction.IsValid`): channel null or 1..16; all data 0..127. An invalid action is
dropped with an import warning, never stored.

### Note (existing, extended)
| Field | Where | Rule |
|---|---|---|
| `articulation` (C++) / `articulation_id` (ABI) / `ArticulationId` (C#) / `Articulation` (JSON) | per note | 0 = none; otherwise an id in the track's map; copying a note copies the id |

### Track timing
`MidiOffsetMs` (double, clamp ±2000, default 0) per instrument track. Persisted only when non-zero.

### Derived: articulation timeline (engine only, never saved)
Per clip, built at publish time from notes + table: sorted entries
`{ startBeat, articulationId, endBeat }`. Latching neighbours with the same id collapse into one
entry; momentary entries are one per note. Rebuilt whenever the clip's notes or the track's
table change. Used for: trigger events and "articulation in force at beat X".

### Derived: compiled table (engine)
`ArticulationTable { defaultId, entries[] sorted by id, actions[] }` where an entry holds
`{ id, offsetMs, leadMs, trigger, actionStart, actionCount }` (times stay in milliseconds and are
converted with the sample rate at schedule time). Lookup by id is a binary search. Id 0 resolves to `defaultId`. An id that is missing or disabled resolves to
`defaultId` (FR-014).

## State transitions

- **Assign map to track**: engine track gets the JSON blob and compiled table (undoable).
- **Edit map**: managed edits a copy; applying replaces blob and table on every track using that
  map (one undo step per track, grouped by `beginUndoGroup`).
- **Delete an articulation**: the id is removed from the map; notes keep the id; resolution falls
  back to default and the UI shows the id as missing. The id is not reused.
- **Remove map from track**: notes keep their ids; with no table nothing is sent (the ids are kept
  so re-assigning the map restores them).
- **Duplicate map**: new map id; articulation ids unchanged, so notes written for the original
  still resolve against the copy.
