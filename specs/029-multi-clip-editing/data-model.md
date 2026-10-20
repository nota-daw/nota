# Data Model: Multi-Clip Editing

Managed-only, in-memory editor state. Nothing new is persisted in the project file; the
engine's per-clip note lists stay the source of truth.

## OpenClip

One MIDI clip shown in a Clip editor. `Nota.Application/Editing/ClipSet.cs`.

| Field | Type | Notes |
|-------|------|-------|
| TrackId | int | arrangement track |
| ClipIndex | int | clip index on that track (as used by `SetClipNotes`) |
| StartBeat | double | arrangement start; refreshed on geometry change (R8) |
| LengthBeats | double | > 0 |
| IsReference | bool | read-only lock (FR-009); never true for the active clip |

Identity: `(TrackId, ClipIndex)`; unique within a set.

## ClipSet

The open clip set of one editor (FR-001, FR-001a).

| Member | Meaning |
|--------|---------|
| Clips | `IReadOnlyList<OpenClip>`, ordered by StartBeat then TrackId; the index is the **slot** stored per note |
| Active | slot of the active clip |
| Origin | min StartBeat — editor beat 0 |
| SpanBeats | max(Start + Length) − Origin |
| IsMulti | Clips.Count > 1 (strip shown, selection-follow disabled) |
| ToEditor(slot, localBeat) / ToLocal(slot, editorBeat) | coordinate conversion |
| Reach(slot) | contiguous run of open, editable clips on the slot's track around it (R4) |
| OwnerAfterMove(slot, editorStart) | FR-006 owner rule (R4) |
| Split(notes, owners) → `ClipNotes[]` | per-clip lists in clip-local beats |
| Merge(`ClipNotes[]`) → (notes, owners) | inverse of Split |

Validation:
- At least one clip; Active is a valid slot and not a reference.
- Setting a clip to reference is refused for Active; making a reference clip Active
  clears its reference flag first (it becomes editable).
- Removing a clip (dead after an arrangement edit) remaps slots; Active moves to the next
  clip; an empty set closes the editor.

## ClipNotes

Commit payload: `(int TrackId, int ClipIndex, IReadOnlyList<NotaNote> Notes)` in
clip-local beats.

## Roll note ownership

`PianoRollView._notes[i]` (editor beats) ↔ `_owner[i]` (slot). Invariant: equal lengths
after every mutation. Notes of reference clips are present (drawn) but never selectable.

## Clipboard note

Spec 007's clipboard entry plus `SourceTrackId`, `SourceClipIndex`, `SourceClipStart`
(R11).

## State transitions

```text
open (double-click in multi-selection / "Edit selected clips")
  └─► Active = clicked clip (or first in time)
click strip bar / click a note of clip X      → Active = X (settles pending placement)
click lock on non-active X                    → X.IsReference toggled (settles placement)
arrangement geometry change on X              → X.Start/Length updated, notes reloaded
X deleted / no longer MIDI                    → X removed from set
re-open                                       → set replaced (old placement settled first)
```
