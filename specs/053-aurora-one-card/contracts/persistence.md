# Contract: project format v28 and preset fields

## Project (`project.json`, `ProjectService.CurrentFormatVersion` 27 → 28)

- **Migration**: append `new Migration(27, _ => { })` (`ProjectMigrations.cs:25-50`
  pattern). A v27 document has no `wavetables`, and the DTO defaults to null.
- **Save** of a top-level Aurora (kind 5, `ProjectService.cs:150-153` branch):
  1. `instrument.state` comes from `nota_track_aurora_state(include_tables = 0)`, through
     `RegisterBlob`.
  2. For each oscillator with a table: read the frames (`nota_track_aurora_table_read`) and
     write them through a WAV writer as float32 mono with a `clm ` chunk (frame size 2048).
     Name the file by content hash (`BundleContent.SampleRel`), skip it if it is already on
     disk, and add a `WavetableDto` ([../data-model.md](../data-model.md)).
  3. A missing slot is saved as `{ Missing: true, Sample: "", Name }`.
- **Load**:
  1. Create the track, then `SetPluginState(blob)`.
  2. For each `WavetableDto`: if `Missing`, call `nota_track_aurora_set_missing` and add the
     warning. Otherwise decode the WAV (`nota_file_decode_mono`, exact for float32) and call
     `nota_aurora_table_from_frames`, then `nota_track_aurora_set_table(checkpoint = 0)` and
     `nota_track_aurora_set_source(UseCustom)`.
  3. On a failure (file absent, unreadable, or frame count mismatch), add the warning
     `"Track {n}: Aurora Osc {o} table \"{Name}\" is missing — it plays its built-in table."`,
     then `set_missing(Name)` and `set_source(0)`.
- **History and pruning**: `samples/<hash>.wav` files referenced by `wavetables` are pinned
  like other sample references (`ProjectHistory.cs:4-18`, `BundleContent.PruneUnreferenced`).
- **Older Nota**: a v28 file is refused by the existing "higher version" check
  (`ProjectService.cs:34-40` doc comment). That is expected behaviour and needs no change.

## Presets (`PresetDocument`)

- New: `[JsonIgnore(Condition = WhenWritingNull)] List<PresetAuroraTable>? AuroraTables`,
  placed beside `MosaicProgram` (`PresetModel.cs:47-49`). `FormatVersion` stays 1, because
  unknown fields round-trip (`PresetModel.cs` forward-compat note).
- **Save** (`PresetService.cs:62` neighbourhood): when kind 5 has any table, write one entry
  per slot with `Data = base64(float32 LE frames)`.
- **Apply**: first the named parameters, then:
  - if `AuroraTables` is null: `clear_table` for both slots and `set_source(0)`;
  - otherwise `from_frames`, then `set_table`, then `set_source`.
  The whole apply is one undo group with the parameter change.
- Factory presets carry no `AuroraTables`, so they always play the built-in tables.
