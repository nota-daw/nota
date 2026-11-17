# Data Model: Tempo as master-track automation

## Engine (snapshot data, `Graph`)

### AutomationTarget (enum, engine + managed mirror)

| Value | Name | Valid on | Notes |
|---|---|---|---|
| 0 | Volume | tracks | master Volume stays the graph-level `masterVolume` lane |
| 1 | Pan | tracks, **master (new)** | master: writes the master pan stage |
| 2 | DeviceParam | tracks, **master (new playback)** | |
| 3 | PluginParam | tracks, **master (new playback)** | |
| 4 | MidiDeviceParam | tracks | master has no MIDI effects |
| 5 | Send | tracks | not played (specs/069) |
| **6** | **Tempo** | **master only** | value = BPM; `addAutomationLane` rejects it on any other track |

### Master Tempo lane (`masterTrack->automation`, target Tempo)

- At most one per project (add reuses it, as for every target).
- Points: `beat ≥ 0`, `value` BPM clamped to [20, 300] on set, `curve ∈ [-1, 1]`; sorted;
  a same-beat pair is a jump; more than two points on one beat keep the first and last.
- Empty / removed lane ⇒ no tempo map; project tempo = transport BPM.

### TempoMap (derived, immutable)

- `source`: the lane points it was compiled from (equality check for re-derivation).
- `points`: `{beat, bpm, incomingBpm, curve}`; first at beat 0; strictly increasing beats.
- `nodes`: compiled `{beat, seconds}` (unchanged algorithm).
- Derived in `deriveTempoMap(Graph&)` on every publish of a new edit (not undo/redo) where
  `source` ≠ lane points. Section / insert-time edits leave the Tempo lane untouched.
  Side effect in the same graph: unwarped clips and takes keep their seconds position.

### Master pan

- `masterTrack` pan, −1..1, default 0; applied after master devices, before master fader,
  with `Graph::panLaw`.

## Project file (format v34)

| Field | Where | Change |
|---|---|---|
| `master.automation` | `MasterDto` | **new**: `AutomationLaneDto[]` — Tempo, Pan, device-param lanes |
| `master.pan` | `MasterDto` | **new**: float, absent = 0 |
| `masterVolumeAutomation` | root | unchanged |
| `tempoPoints` | root | **read only by migration v33→v34**, then removed |
| `bpm` | root | unchanged — the project tempo when no Tempo lane; with a lane, the lane value at beat 0 |
| `tempoLaneMin/Max` | `track-view.json` sidecar | **new**: doubles, absent = default (FR-003a) |
| `tempoClamped` | root | **new, transient**: set by migration when values were clamped; the app shows the notice once and drops it on next save |

### Migration v33 → v34

1. If `tempoPoints` is absent or empty: drop it; done.
2. Convert to lane points (research R3); clamp to 20–300; set `tempoClamped` if any clamp.
3. Append `{target: "tempo", points}` to `master.automation`; set `bpm` to the first value.
4. Remove `tempoPoints`.

## Managed view model

- `MasterLaneVM`: `Target` (Volume | Tempo | Pan | DeviceParam/PluginParam with
  device/param address), `Height`, `Visible`; Tempo adds `RangeMin`, `RangeMax`.
- `TransportViewModel`: `IsTempoAutomated` (bool), `Bpm` shows `TempoAt(playhead)` when true;
  `BpmEditable = !IsTempoAutomated`.
