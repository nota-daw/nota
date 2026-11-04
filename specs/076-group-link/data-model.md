# Data model: Linked editing on group tracks

## Track (engine `Track`, managed `NotaTrackInfo` / `TrackVM` / `TrackDto`)

| Field | Type | Where | Rule |
|-------|------|-------|------|
| `editLink` | bool, default false | engine `Track.h` | Meaningful on group tracks only; `setTrackEditLink` refuses other kinds. Copied by `cloneTrack`. Changing it is an undo step. |
| `EditLink` | `bool?` | `TrackDto` (project JSON) | Written `true` or omitted. Applied on load after the hierarchy (`GroupId`) is set. |
| `EditLink` | bool | `TrackVM` (Arrangement) | Read from the engine on `Refresh`. |

Existing fields used: `Id`, `Type` (`IsGroup`, `IsReturn`, `IsInstrument`), `GroupId`
(parent group id or -1), `Armed`.

## Link set (derived, never stored)

- For a non-group, non-return track **T**: walk `GroupId` up to the root; the **outermost**
  ancestor group with `editLink` is T's *link root*. None → T is unlinked.
- The set of a link root **G**: every non-group, non-return track whose link root is G.
- Membership changes as soon as the hierarchy or a flag changes (moving a track in / out,
  ungrouping, toggling, undo) — it is recomputed from the current tracks on each use.

## Match (derived)

- Clip **c'** on member **T'** matches source clip **c** on **T** (T ≠ T', same set) when
  `|c'.start − c.start| ≤ 1/256` and `|c'.end − c.end| ≤ 1/256` (beats).
- Kind does not matter (audio and MIDI clips can match).
- A clip may have at most one match per member track in practice (clips on one track don't
  overlap); if several qualify, all are taken.

## Selection state (Arrangement view)

| State | Meaning |
|-------|---------|
| `_sel` | every selected clip (unchanged meaning) |
| `_selLinked` ⊆ `_sel` | the clips that are selected **because** they match a clip the user chose; used by vertical moves and by trim / ADSR to find the grabbed clip's partners |

Transitions:

- plain click / drag start on unselected clip → `_sel = {c} ∪ matches(c)`, `_selLinked = matches(c)`.
- ⌥-click → `_sel = {c}`, `_selLinked = ∅`.
- ⌘-click on unselected c → add c and matches; on selected c → remove c and its matches
  that are in `_selLinked`.
- `SetSelection(list)` (marquee, ⌘A, reselect after an edit) → `_sel = list`; `_selLinked`
  = clips in the list that match another clip of the list on a different member of the same
  set and were not the user's direct target (for reselect, the source clips' new positions are
  known, so the rest are linked).
- Any clear → both empty.
