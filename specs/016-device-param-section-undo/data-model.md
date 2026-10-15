# Data Model: Undo for Device Parameters and Arrangement Sections

## Value target (`MixTarget`, engine + managed)

| Field | Type | Meaning |
|---|---|---|
| kind | MixKind | TrackVolume 0 · TrackPan 1 · MasterVolume 2 (specs/009) · **DeviceParam 3** · **MidiDeviceParam 4** · **PluginParam 5** |
| trackId | int32 | track (the master chain's devices use the master track id); ignored for MasterVolume |
| deviceIndex | int32 | audio-effect chain position (3), MIDI-effect chain position (4), effect position or −1 = the instrument (5); −1 otherwise |
| paramIndex | int32 | parameter index (3, 4: the device's own range; 5: normalized 0..1); −1 otherwise |

Two targets are the same when all four fields match.

## Value change (`MixChange`)

A target plus `before` / `after` (float, exact). A step holds any number of changes, at most
one per target. Merge rule (group / recording merge): per target the older `before`, the
newer `after`; a target whose merged `before == after` is dropped.

## Open gesture

`handle`, the targets with their `before`, `pushCountAtBegin`, `stackSizeAtBegin`
(specs/009, unchanged). Touch gestures additionally remember their automation-write
identity `(track, target, device, param, paramId)` so the matching
`endAutomationWrite` closes them; one open touch per identity.

## Section (`Graph::sections`)

| Field | Type | Rule |
|---|---|---|
| startBeat | double | ≥ 0 |
| lengthBeats | double | > 0 (entries with ≤ 0 are dropped) |
| name | UTF-8 string | free text |

Kept sorted by `startBeat` (stable). Replaced as a whole; equality is field-wise, so
re-setting an identical list makes no step.

## Project document

`ProjectDocument.Sections`: `List<SectionDto>?` — `{ "start": double, "length": double,
"name": string }`. `null` (member absent) = older file → read the legacy `sections.json`
sidecar once.
