# Data model: Modular device cards and a searchable cable-drop picker

UI-only state. Nothing is added to the engine or the project's `.nota` state. The one
persisted change is the third value in the Modular layout sidecar.

## ModularNodeSize

| Value | Meaning | Sidecar code |
|---|---|---|
| *(unset)* | use the default: S when the device has S (research R2), else L | 0, or no third value |
| `XS` | today's compact node: header, ports, parameter rows | 1 |
| `S` | the device's S card body | 2 |
| `L` | the device's L card body | 3 |

- Applies to: nodes with `Slot ∈ {MidiFx, Instrument, Device}`. Modulator, Input,
  Output and Island nodes have no size (FR-019).
- Validation: `S` on a device without S resolves to `L`. Unknown codes resolve to
  unset.
- Lives in `ModularView` as `Dictionary<string, ModularNodeSize>`, keyed exactly like
  `_savedPos` (`{trackId}/{nodeId}`, `{docIndex}/{nodeId}` on disk).
- Independence: never reads or writes `DeviceView` / `InstrumentView` / MIDI `View`
  params or the Detail plug-in `PluginCardState`. The `CardSizeEngineProxy` presents
  it to the card bodies as if it were the device's own size (R2).

## Layout sidecar `modular-layout.json` (extended)

```json
{ "0/dev0": [120.0, 80.0, 2], "0/inst": [40.0, 80.0], "isl:1": [300.0, 40.0] }
```

- Value array: `[x, y]` (legacy, size unset) or `[x, y, sizeCode]`.
- Islands never carry a size.
- Written when a node is dragged or resized and when the project is saved (existing
  `SaveLayout`). Read in `LoadLayout`.
- An older Nota reading a new file uses `x, y` and ignores the size.

## GNode (changes)

| Field | Change |
|---|---|
| `Size` *(new)* | the resolved `ModularNodeSize` (never unset after build) |
| `HasSmall` *(new)* | whether the device offers S (R2) |
| `Width` *(new)* | `NodeW` at XS, else the card body's measured width plus the frame; replaces the `NodeW` constant in anchors, hit tests and layout |
| `Params` | filled only at XS |

## ParamPickerEntry

| Field | Type | Notes |
|---|---|---|
| `IsHeading` | bool | group heading (device or track name); not pickable |
| `Label` | string | parameter, or modulator name; fallback "Parameter N" when empty, and a "(2)" suffix for a duplicate within a group |
| `Group` | string? | heading text the entry belongs to; searched too in grouped lists |
| `Payload` | opaque | what to link: `(kind, device, param)` for targets, `(ownerTrack, modId)` / `(device, param)` for sources |

## ParamPickerModel

| Member | Behaviour |
|---|---|
| `Entries` | all entries in display order (headings first in each group) |
| `Query` | set → recomputes `Visible` (case-insensitive substring on `Label`, or `Group` for grouped lists); headings with no visible items are dropped |
| `Visible` | the filtered list the ListBox virtualizes |
| `Highlight` | index into `Visible`, always on an item (never a heading); first item after each query change; −1 when empty |
| `MoveHighlight(±n)` | skips headings and clamps |
| `Pick()` | returns the highlighted payload, or null |
| `EmptyText` | "No parameters" (no entries) or "No matching parameters" (query filtered all) |
