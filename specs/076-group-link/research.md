# Research: Linked editing on group tracks

Phase 0 of [plan.md](plan.md). Every open point of the Technical Context is resolved here.

## R1. Where the Link flag lives

- **Decision**: an authoring flag on the engine's `Track` (`editLink`), set through a new
  C ABI pair, persisted by `ProjectService` as `TrackDto.EditLink`.
- **Rationale**: track metadata already lives there (`name`, `colorIndex`, `pianoFold`);
  `cloneTrack` copies it, so duplicate / copy-paste of a group keeps it; setting it via
  `republishWithTrack` makes the toggle an undo step for free (spec FR-003); undo of a
  group delete restores it. The audio thread never reads it.
- **Alternatives**: a managed-only sidecar like the fold state (`GroupFold`, specs/070) —
  rejected: fold is view state with no undo, while Link changes what edits do and must
  undo, copy and travel with the track; a flag on the managed `Track` domain object —
  rejected: the engine is the source of truth for tracks at runtime.

## R2. How linked edits are carried

- **Decision**: through the selection. Selecting a clip adds its matches to `_sel`
  (tracked in `_selLinked`); the block edits already act on the whole selection.
- **Rationale**: move block, nudge, duplicate, copy, cut and delete are all
  selection-driven (`SelectionBlock`, `SelectedClipStarts`), and `MoveClipBlock` /
  `DuplicateClipBlock` / `CutClipBlock` are already single undo steps. The user also sees
  what an edit will touch before making it (spec Story 2).
- **Alternatives**: expand at each edit call site — rejected: ~10 call sites, each a place
  to forget, and the selection would not show what moves.

## R3. Matching tolerance

- **Decision**: |Δstart| ≤ 1/256 beat and |Δend| ≤ 1/256 beat.
- **Rationale**: simultaneous takes start and end on the same beat values exactly; a beat
  tolerance needs no tempo (the spec's first draft mixed ms and notes, which needs the
  tempo map); 1/256 beat ≈ 2 ms at 120 BPM — far below any grid step the user can snap to,
  so two clips placed on different grid lines never match.
- **Alternatives**: exact equality — rejected: a clip moved by key and back could pick up
  float error; overlap — rejected by the user in clarification.

## R4. The single-track bypass gesture

- **Decision**: ⌥-click (Alt-click) on a clip selects it alone; ⌥-click on an arm button
  arms that track alone. ⌥-drag stays "move without snap" and stays linked.
- **Rationale**: ⌃-click is the macOS context-menu click (`IsPrimaryDown` comment);
  ⌘ / Ctrl toggles the selection, ⇧ extends it / starts a range; a ⌥ *click* (press and
  release without crossing the drag threshold) currently does the same as a plain click,
  so giving it a meaning breaks nothing. Because linked edits follow the selection,
  selecting one clip alone is enough to make the following edits single-track.
- **Alternatives**: a new held key (e.g. a letter) — rejected: letters type notes on the
  computer keyboard; ⌘⌥-click — workable but harder to discover; turning Link off and on —
  rejected by the user.
- **Note**: some Linux window managers grab Alt-click for moving windows; there the user
  can still turn Link off. Recorded in the docs.

## R5. Vertical moves

- **Decision**: the matches added by the link (`_selLinked`) take the time shift of a drag
  but keep their own track; ⌥↑ / ⌥↓ leave them out (their time shift is 0).
- **Rationale**: the user's clarification; the partners would otherwise need target
  tracks of their own.

## R6. Split at the playhead and ranges

- **Decision**: split adds, per touched link set, every member clip spanning the playhead
  (spec FR-010); ranges widen `TimeSelectionTrackIds` to whole link sets and the drawn row
  span to the set's rows.
- **Rationale**: these are time-based edits, so they act on the members' material at that
  time, matched or not — the dry / wet tracks are cut at the same place even after a
  partner was trimmed. A group's subtree is contiguous in `HierarchyOrder`, so the set's rows
  form one span (when the group is folded its rows are hidden and the engine-level track
  list still includes them — `TimeSelectionTrackIds` takes ids from the set, not rows).

## R7. One undo step per linked edit

- **Decision**: wrap the loops that call per-clip engine edits (split, delete, trim,
  ADSR) in `BeginUndoGroup` / `EndUndoGroup`; block edits already are one step.
- **Rationale**: the engine's undo group API nests and is already used by the app
  (device replace, recent instruments).

## R8. Arming

- **Decision**: extend `Nota.Presentation.TrackArm.Set` / `FollowSelection` to the link set;
  all arm buttons (Arrangement, Mixer, compact Mixer, Session mixer, inspector) call it via
  `TrackArming.Set`, so one change covers every view. MCP `set_track_armed` stays per track.
- **Rationale**: single choke point; the logic is already tested against the real engine in
  `SessionInputTests`.
- Non-recordable members (none today: every non-group, non-return track can arm) are
  skipped.

## R9. Persistence and compatibility

- **Decision**: `TrackDto.EditLink` (`bool?`), written only when true; no
  `CurrentFormatVersion` bump.
- **Rationale**: same as `PianoFold` (specs/045): old projects read as unlinked (SC-005);
  older builds ignore the unknown field (`JsonExtensionData` keeps it round-tripping).
