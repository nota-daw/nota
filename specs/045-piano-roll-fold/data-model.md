# Data model: Piano roll Fold

## Fold state (persisted)

| Where | Field | Type | Default | Notes |
|---|---|---|---|---|
| Engine track UI metadata | `pianoFold` | bool | false | Control thread only, like `colorIndex` |
| Project track (`ProjectModel`) | `PianoFold` | `bool?` | null = off | Written only when true; old projects load unfolded |

It is copied by track duplicate and track copy/paste. Undo snapshots carry it with the track, but
toggling Fold is not itself an undo step (FR-009).

## Row map (derived, never stored)

`PianoRowMap`:

| Member | Meaning |
|---|---|
| `Rows` | Visible pitches, top row first (highest pitch first) |
| `Count` | Number of rows |
| `RowOf(pitch)` | Row index, or −1 if the pitch is hidden |
| `PitchAt(row)` | Pitch of a row (clamped to the ends) |
| `Step(pitch, rows)` | The pitch `rows` visible rows above (+) or below (−); a hidden start pitch steps from the nearest row |
| `Label(row)` | Note name, or the pad name(s) on a Drum Rack |
| `Mark(row)` | `InSet`, `OutOfScale`, `NoPad` |
| `IsChromatic` | True when unfolded, or when folding found no rows (the hint is shown) |

Built by `PianoRowMap.Build(fold, kind, scaleMask?, root, pads?, usedPitches)`:

1. Not folded → chromatic A0..C8.
2. Drum Rack → the pads' notes ∪ used pitches; marked `NoPad` where there is no pad.
3. Melodic with a scale → in-scale pitches in range ∪ used pitches; marked `OutOfScale` where out
   of scale.
4. Melodic, no scale → used pitches.
5. Empty set → chromatic, `IsChromatic = true`.

## State transitions

`Unfolded ⇄ Folded` on the toolbar button, ⌥⇧F or MCP `set_piano_fold`. While folded, the map
is rebuilt on these events, never during a gesture (research R3): scale on/off/root/mode,
rack pads changed, notes committed, clip switched.
