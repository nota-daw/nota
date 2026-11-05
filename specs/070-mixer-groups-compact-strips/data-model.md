# Data Model: Mixer Groups and Compact Strips

All state is view state. Nothing reaches the engine, the C ABI or undo history.

## GroupFold (in memory, shared)

One instance per main window, shared by the Arrangement and the Mixer (R2).

| Field | Type | Notes |
|---|---|---|
| folded | set of group track ids | a group id in the set hides its whole subtree in both views |

- `Toggle(id)`, `Set(ids)`, `Clear()` raise `Changed` once.
- Ids that no longer name a group (deleted, ungrouped) are ignored on read and dropped on
  save; no clean-up pass is needed.
- Lifecycle: cleared on New project and before Open; filled from the sidecar after Open.

## track-view.json (per project, sidecar in the `.nota` bundle)

Existing file (`ArrangementView.Sizing.cs`, `version: 1`). One additive field:

```json
{
  "version": 1,
  "headerWidth": 220,
  "trackHeights": [ { "index": 3, "height": 96 } ],
  "masterHeight": 40,
  "collapsedGroups": [ 0, 7 ]
}
```

| Field | Type | Rule |
|---|---|---|
| `collapsedGroups` | int[] | engine track **indices** of folded groups (ids last a session only); omitted when empty; an index that is out of range or not a group is skipped on load |

Older builds ignore the field; a file with only `collapsedGroups` is still written (the
"nothing to save → delete the file" check includes it).

## Settings (per user, `settings.json`)

| Field | Type | Default | Notes |
|---|---|---|---|
| `MixerCompact` | bool | `false` | strip layout of the Mixer, docked and floating (FR-013) |

## Mixer strip row (derived on Refresh, not stored)

| Item | Derived from |
|---|---|
| order | `ArrangementView.HierarchyOrder(engine)` — DFS, group then members; returns, then master last |
| depth / parent group | same walk |
| visible | no ancestor group in `GroupFold` |
| band per group | first strip = group strip, last = last visible descendant; colour = group's effective colour; name = group name; row = depth |
| folded-group indicators | hidden descendants: count, any soloed, any armed, any peak ≥ 0 dBFS since unfold |
| selected outline | the selected track's strip, or its outermost folded ancestor's strip |

## StripMetrics (per layout, constant)

Heights and widths the height fitting uses (R6). Regular keeps today's numbers.

| Metric | Regular | Compact (starting point, set by the mockup) |
|---|---|---|
| width | 154 | 72 |
| header | 24 | 24 |
| I/O | 34 | 18 |
| sends | 42 (A and B rows) | 24 (A and B bars, as the regular strip shows them) |
| pan | 38 | 20 |
| fader / meter min · comfort · max | 72 · 120 · 190 | 72 · 120 · 220 |
| buttons | 28 | 3 × 18 + 6 |
| band bar row | 14 per nesting level, both layouts |
