# Research: Piano roll Fold

## R1. One vertical mapping instead of `(MaxPitch - pitch) * RowH`

- **Decision**: A pure `PianoRowMap` (Nota.Presentation) owns the vertical layout: the ordered
  visible pitches (top row first), `RowOf(pitch)` (−1 when hidden), `PitchAt(row)`, `Count`, and
  `Step(pitch, rows)`. Unfolded it is the identity over A0..C8 (88 rows). Every site in
  `PianoRollView` that computes `(MaxPitch - p) * RowH` or its inverse (12 sites: keys column, grid,
  hover, held keys, audition, `EnsurePitchVisible`, initial scroll, `RangeH`) goes through it.
- **Rationale**: Folding is purely a layout change, so one mapping keeps folded and unfolded code
  identical. A pure type can be smoke-tested without Avalonia.
- **Alternatives**: Separate "folded" drawing paths, rejected as they double the code and drift. A
  hidden-row height of 0 in the existing layout was also rejected: hit-testing and scrolling
  still see 88 rows, and arrow keys would need special cases.

## R2. Drag and transpose step by rows, not semitones

- **Decision**: With Fold on, `MoveSelection(dPitch)` and the drag compute a **row delta**. Each
  note moves to `Step(pitch, dRows)`, so a group keeps its shape in scale degrees (a third stays
  a third in key). The shared delta is clamped so no note leaves the map. Octave moves
  (⇧↑/⇧↓) stay ±12 semitones: in a scale that's the same degree, and on drums it may land on a
  pitch without a pad, which then gets a marked row (FR-003).
- **Rationale**: This matches Ableton's folded editing, and it's what FR-004 asks: moves go
  between visible rows only.
- **Alternatives**: Semitone delta then snap to the nearest visible row, rejected because notes in
  a chord collapse onto the same row.

## R3. The map is frozen during a gesture

- **Decision**: The map is rebuilt only when a gesture isn't running: on pointer-up, on commit,
  and on scale, pad or notes-changed events. A note dragged off an out-of-scale row keeps that
  row until the drag ends.
- **Rationale**: Rebuilding mid-drag would remove the row under the pointer and make the
  layout jump.

## R4. Drum Rack detection and pad names

- **Decision**: Reuse `DrumPatternView.ScanPads()`, moved into a shared helper (`DrumPads.Scan`)
  that both views call. It returns each chain's trigger note and its name (the chain's name, or its
  instrument's name). Several pads on one note are joined: "Kick / Kick 2". A track is a
  Drum Rack track when `TrackInstrumentKind(trackId) == 4`, the same check as the Clip tab's
  **Pattern** button (`MainWindow.IsDrumRackTrack`).
- **Live updates**: The map is rebuilt from whatever change signal the window raises when a rack's
  chains change, renamed or reloaded (FR-006). Task T010 finds that signal. If there is none, the
  piano roll re-scans the pads when it repaints after a chain edit.

## R5. Where Fold is stored

- **Decision**: A per-track `pianoFold` flag in the engine's track UI metadata, next to
  `colorIndex`, exposed as `nota_track_set_piano_fold` / `nota_track_get_piano_fold`. It is
  saved as an optional `PianoFold` field on the track in the project (absent = off, so no
  format bump), copied by track duplicate and copy, and read by MCP `set_piano_fold` /
  `get_piano_fold`.
- **Rationale**: Track colour already lives there for the same reasons: it survives duplicate,
  undo snapshots and save, and MCP can reach it. It is control-thread-only data, never read on the
  audio thread (Principle I). Setting it publishes without an undo checkpoint (FR-009). An undo
  of an earlier edit restores that snapshot's flag, and the editor reads the flag again the
  next time it opens a clip on the track.
- **Alternatives**: A sidecar like `modular-layout.json` was rejected, because MCP can't reach it
  and track duplicate wouldn't copy it. A static per-session dictionary in the view was rejected
  because it isn't saved.

## R6. No scale and no notes

- **Decision**: On a melodic track, Fold uses the scale's pitches when a scale is on, and
  otherwise the clip's used pitches. When that set is empty (an empty clip and no scale, or a
  Drum Rack with no pads), the map stays chromatic and the grid shows the hint "Set a scale or
  add notes to fold". Drawing on a folded keyboard with no scale is limited to existing rows.

## R7. Shortcut

- **Decision**: ⌥⇧F (Alt+Shift+F on Windows and Linux) toggles Fold while the piano roll has
  focus. Plain letters are QWERTY note keys (F plays a note), ⇧F is already "fit to notes", and
  Alt+F opens the File menu on Windows. The shortcut is added through the `nota-shortcuts` skill.

## R8. Performance

- **Decision**: Building the map is O(88 + notes). It runs only on the events in R3, never per
  frame. The draw loops already walk visible rows. There's no engine or audio-thread change, so
  the perf gate isn't affected and no benchmark is needed (constitution: benchmarks only for
  perf-affecting work). SC-005 is checked by timing a 2,000-note fold in the smoke test.
