# Data Model: Aurora one card and custom wavetables

Phase 1 of [plan.md](plan.md). Entities follow the spec's Key Entities; the field names here are the
planned ones.

## AuroraTable (engine, immutable)

An imported wavetable after conditioning. Built on any thread, never mutated after
construction, shared by `shared_ptr<const AuroraTable>`.

| Field | Type | Rule |
|---|---|---|
| `frames` | int | 1–256 |
| `base` | float[frames × 2048] | DC-free per frame, table peak = 1.0 (R3) |
| `mip[k]`, k = 1…5 | float[frames × (2048 >> k)] | FFT-truncated to ≤ 1024 >> k harmonics (R4); `mip[0]` is `base` |
| `name` | UTF-8, ≤ 255 bytes | the source file name without its folder; the default display name |
| `origin` | enum | 0 wavetable file · 1 single cycle · 2 recording |
| `truncatedLength` | bool | more than 60 s of audio was given |
| `truncatedFrames` | bool | more than 256 frames were given |
| `hash` | 16 bytes | 128-bit content hash of `base` (R3 step 5) |
| `serial` | u32 | process-unique, for UI cache invalidation |

**Validation**: a build fails with a typed error if:

- the file cannot be decoded (`NOTA_AURORA_ERR_DECODE`);
- the format is not WAV / FLAC / MP3 (`ERR_FORMAT`);
- the peak before normalising is below −60 dBFS (`ERR_SILENT`);
- the file is empty (`ERR_EMPTY`).

The `frames` rules: (a) a wavetable file has its count clamped to 256; (b) a single cycle
gives 1; (c) a recording always gives 16 windows, which overlap when the file is shorter than
16 × 2048 samples.

## AuroraTableSet (engine, immutable, behind SnapshotHistory)

| Field | Type |
|---|---|
| `slot[2]` | `shared_ptr<const AuroraTable>` (null = none) |
| `missing[2]` | UTF-8 name of a table that could not be restored ("" = not missing) |

Owned by `WavetableSynth::tables_` (`SnapshotHistory<const AuroraTableSet>`). The audio thread
reads `live()` once per block.

## Per-oscillator source state (engine)

| Field | Type | Rule |
|---|---|---|
| `useCustom_[osc]` | `std::atomic<uint8_t>` | 1 plays `slot[osc]` when present; otherwise the built-in `table` / `osc2table` parameter decides. Relaxed. |

**State transitions** (per oscillator):

```text
Built-in ──import ok──▶ Imported(active) ──pick built-in──▶ Imported(kept, inactive)
   ▲                         │  ▲                                   │
   │                      remove │ pick imported ◀──────────────────┘
   └────────────remove───────────┘
Any ──project load, file missing──▶ Missing(name)  (plays built-in; import or pick built-in clears)
Any ──import failed──▶ unchanged (card shows the reason)
```

Import and remove are undoable (a graph republish). A pick is in place. Missing is set by the
load path.

## Scope telemetry (engine → UI)

`WavetableSynth::scopeRead`, through `nota_track_instrument_scope`:

| Index | Meaning |
|---|---|
| 0 | Osc 1 played position 0..1 (most recent sounding voice, modulation included), −1 = silent |
| 1 | Osc 2 played position, as above |
| 2 | Osc 1 table serial (0 = built-in) |
| 3 | Osc 2 table serial |
| 4 | active voices |

## State blob trailer

See [contracts/state-blob.md](contracts/state-blob.md).

## Project (managed, format v28)

`InstrumentDto.Wavetables : List<WavetableDto>?`. It is null when the Aurora has no custom
table, so an Aurora without one saves exactly as in v27.

| WavetableDto field | Type | Rule |
|---|---|---|
| `Osc` | int | 0 or 1 |
| `Sample` | string | bundle-relative `samples/<hash>.wav` ("" when `Missing`) |
| `Name` | string | display name |
| `Origin` | int | as `AuroraTable.origin` |
| `Frames` | int | 1–256, cross-checked on load |
| `UseCustom` | bool | the source flag |
| `Missing` | bool | saved while the table was missing, so the warning and the name persist |

The stored WAV is float32 mono, 2048 × `Frames`, with a `clm ` frame-size chunk. Identical
tables share one file (content-addressed, `BundleContent.cs:4-9`).

## Preset (managed)

`PresetDocument.AuroraTables : List<PresetAuroraTable>?` (null = no custom tables; loading
such a preset clears them).

| PresetAuroraTable field | Type |
|---|---|
| `Osc` | int |
| `Name` | string |
| `Origin` | int |
| `Frames` | int |
| `UseCustom` | bool |
| `Data` | string: base64 of little-endian float32, 2048 × `Frames` |

## UI view state (managed, not persisted)

| Entity | Fields |
|---|---|
| `AuroraTableView` | source key (built-in bank, or table serial), lit index (0..63 of the lit grid), dim, load state (idle / loading / error(text) / missing(name)), cached geometries per bounds |
| `AuroraTablePreview` cache | key (track, osc, serial) → up to 16 representative frames + 64 lit-grid frames × 96 points |
