# Data model: Arrangement group trays

The pure layout that both looks draw from. It lives in `Nota.Presentation`
(`GroupTrayLayout`), takes no Avalonia types, and is what `GroupTrayTests` checks.

## Input: `TrayRow` (one per visible header row, in display order)

| Field | Type | Meaning |
|---|---|---|
| `Id` | int | Track id |
| `ParentId` | int | Enclosing group's id, −1 at top level |
| `IsGroup` | bool | The row is a group |
| `Folded` | bool | Group only: its members are hidden |
| `ColorIndex` | int | Track colour slot |
| `HiddenTracks` | int | Group only: non-group descendants (`GroupSlotCount`); 0 otherwise |

Rows come from `ArrangementView._tracks` after `ApplyHierarchy` (DFS order, folded
descendants already removed). Rule: a row's parent, if any, appears earlier in the list.

## Output: `TraySlice` (one per input row)

| Field | Type | Meaning |
|---|---|---|
| `Depth` | int | Number of enclosing groups |
| `Chain` | `TrayRef[]` | Trays the row is drawn in, outer → inner. A group's own row includes its own tray as the last entry |
| `Heads` | bool | The row is the header row of the last tray in `Chain` (new look) |
| `Closings` | int | Trays that end on this row, innermost first (0..`Chain.Length`) |
| `ContentLeft` | double | x where the row's own content (colour bar / chevron) starts |
| `SpineX` | double | Classic: x of the row's own 3 px spine; new look: x of its own colour bar |
| `FoldedCount` | int? | Folded group: `HiddenTracks`; otherwise null |

`TrayRef` = (`GroupId`, `ColorIndex`, `Level`) where `Level` is the tray's index in the
chain (its edge sits at `Level * Step`).

## Rules

- **Chain**: the ancestors of the row, outer → inner; plus the row itself if it is a group.
- **Closings**: tray *T* closes on row *r* when *r* is the last row (in display order)
  whose chain contains *T*. A folded or empty group's tray closes on its own row. Counted
  innermost first.
- **New look**: `Step = 6` (3 px edge + 3 px gap); `ContentLeft = Chain.Length * Step` for
  a member track; for a group's own row `ContentLeft = (Chain.Length − 1) * Step + 3 + 3`
  (past its own tray's edge and gap). A top-level track: `ContentLeft = 0`.
- **Classic look**: no chain painting; `SpineX = Depth * 10`, `ContentLeft = SpineX + 3`.
  `Closings` is still computed but unused.
- **Invariants** (tested):
  - `ContentLeft(depth 2 track) − ContentLeft(top-level track) ≤ 12` in the new look
    (SC-001).
  - Every tray opened is closed exactly once; total closings = number of visible groups.
  - Closings on a row ≤ `Chain.Length`; closing band (4 px + 2 px per further closing) leaves 20 px on a Compact row
    closing four trays and 18 px (one line of chips) on a slim group row closing three.
  - The layout never outputs anything about the right side: the right content edge is a
    constant of the view (FR-007).

## State transitions

- **Fold / unfold** → `_tracks` changes → slices recomputed → headers rebuilt (existing path).
- **Look change** → `HeaderLook` set → slices recomputed with the other look → headers
  rebuilt, lanes invalidated.
- **Selection** → no slice change; the selected group's full card takes the head slice.
- **Row height drag** → no slice change; the painter uses the row's current height.
