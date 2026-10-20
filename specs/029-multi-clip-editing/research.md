# Research: Multi-Clip Editing in the Clip Editor

Phase 0 of [plan.md](plan.md). Each entry: decision, rationale, alternatives.

## R1 — One undo step across several clips (FR-011)

**Decision**: Wrap every multi-clip commit in the engine's existing undo group
(`nota_engine_undo_group_begin/end`, managed `IAudioEngine.BeginUndoGroup/EndUndoGroup`)
and push each affected clip with the existing `SetClipNotes`. Live drag frames keep using
`SetClipNotesLive` (no checkpoint), unchanged.

**Rationale**: The group API exists and is already used by the MCP tools for multi-edit
operations, so no C ABI or engine change is needed (constitution II, IV untouched). The
single-clip drag already seeds its undo entry by committing the pre-drag notes once and
then streaming live; the multi-clip version seeds all editable open clips inside one group.

**Alternatives**: a new `set_clips_notes` batch call in the C ABI — rejected, duplicates
what groups already provide and would need smoke coverage for no gain; managed-side
undo stack — rejected, the engine owns undo for notes.

## R2 — Where the "which clip owns this note" lives

**Decision**: `PianoRollView` keeps its flat `_notes` list (now in *editor beats*: offset
from the earliest open clip's start) plus a parallel `_owner` list of clip slots. A small
set of private helpers (`AddNote`, `RemoveNoteAt`, `ReplaceAll`) keeps the two in sync;
the ~13 structural mutations in `PianoRollView.cs` are routed through them. Owner rules,
coordinate conversion, bounds and per-clip split/merge live in a pure class
`Nota.Application/Editing/ClipSet.cs` so they are unit-testable in the smoke test.

**Rationale**: `NotaNote` is blittable and mirrors the native struct, so no owner field
can be added to it. Reworking every `_notes[i].X` read (≈100 sites) into a wrapper type is a
large, risky diff; a parallel list touches only the mutation sites.

**Alternatives**: one `PianoRollView` per clip stacked as layers — rejected, selection,
marquee, drag and keyboard edits must span clips; wrapper struct `RollNote` — rejected
for diff size (could be a later refactor).

## R3 — Single-clip parity (FR-002, SC-004)

**Decision**: The single-clip editor is a `ClipSet` of one clip whose origin is that
clip's start, so editor beats equal clip-local beats and every existing code path runs
unchanged. The clip strip is hidden for one clip. The session-slot editor keeps the plain
`Commit` callback (no `ClipSet` commit wiring).

**Rationale**: One code path; parity is provable by the existing note-editing tests
running unchanged.

## R4 — Move bounds and moving between clips (FR-006)

**Decision**: For a group move, each note's allowed start range is its **reach**: the run
of open, editable clips on its track that are contiguous with its current clip (next
start ≤ previous end + ε). The shared delta is clamped so every note's start stays in its
reach (shape kept, as today). After the move each moved note's owner is recomputed:
keep the current clip if it contains the start, else the active clip if it is in the reach
and contains the start, else the later-starting clip containing it. Tail overhang past the
containing clip's end follows today's single-clip limits (start ≤ end − min length).
Resize-left/right never change the owner.

**Rationale**: Matches clarification 2; reach makes a gap or a reference clip a wall, which
is exactly "stops at the edge as today".

## R5 — Overwrite rule per clip (FR-010)

**Decision**: Add owner-aware overloads of `NoteOverwrite.Settle` / `Fate` (an optional
`IReadOnlyList<int>? owners`); same-pitch overlap only counts when owners match. With
`owners == null` behaviour is byte-identical to today.

## R6 — Clip tools and whole-clip commands (FR-005, Story 4)

**Decision**: `MidiToolsView` runs `MidiToolRunner.Apply` **once per clip** in clip-local
beats (`MidiToolContext.LengthBeats` = that clip's length): on clips that have selected
notes, or — with no selection — on the active clip only. Results are merged back with
their owner through a new `ShowPreview(IReadOnlyList<ClipNotes>)` overload. Quantize,
Transpose, Remove-out-of-scale apply to all editable clips.

**Rationale**: Generators add notes whose owner could not otherwise be known; per-clip runs
also keep each clip's own length as the tool's boundary.

## R7 — Arrangement selection vs. fixed clip set (FR-001a)

**Finding**: Today `OnTrackSelected` resets the editor on a track switch and
`OnDetailClip` re-points the Clip tab at the newly selected clip.

**Decision** (revised after review, bug06): the editor follows the selection. The
arrangement raises `ClipSelectionChanged` once per change (posted, deduplicated, from
`UpdateHeaderSelection`, which every selection path calls); while the Clip editor is on
screen the host opens the selected MIDI clips together, or the single clip when the
selection narrows to one. The first click of a double-click still narrows the selection;
`RestoreNarrowed` puts it back on the second click.

## R8 — Following arrangement changes (FR-013)

**Decision**: Every host hook that touches the single editor today is generalised to the
set: `OnClipGeometryChanged` updates the matching `OpenClip` (start/length) and reloads;
`ReloadEditorNotes` (undo/redo, recording) reloads all clips; each commit validates every
clip with `TryGetClipInfo(...).IsMidi` and drops dead clips from the set (active falls to
the next clip; empty set → `InvalidateClipEditor`). Index shifts after a lower-index
delete are handled exactly as the single editor handles them today (known limitation,
not widened).

## R9 — Envelopes, inspector, header, playhead, seek, record preview

**Decision**: These follow the active clip. `ClipEditorView` listens to a new
`PianoRollView.ActiveClipChanged` and rebinds the `EnvOverlay` (track/clip/length and an
editor-beat offset), `ClipPropsView` start/length/loop and the header name
("Name · +N clips"). Playhead and seek convert with the set origin
(editor beat = arrangement beat − origin). The record-take preview is shown when the take
clip is in the set, with `clipStart = origin`.

## R10 — Clip strip UI (FR-008, FR-009)

**Decision**: A new `ClipStrip` control (row between `RulerLane` and the note grid, ~18px,
only when the set has ≥2 clips) draws one bar per clip on the roll's beat axis, stacked in
rows when clips of different tracks overlap; track colour fill, clip name, chosen marker
on the active bar (accent per DESIGN.md: accent reserved for what is chosen), lock glyph
on non-active bars. Click bar → active; click lock → toggle reference. Visuals via the
`nota-design` skill.

## R11 — Clipboard routing (FR-012)

**Decision**: Clipboard entries carry their source `(trackId, clipIndex)` and the source
clip's arrangement start. Paste: if the source clip is open, the copy goes to that clip at
its original position; otherwise to the active clip at its clip-relative beat (spec 007
rule). Single-clip behaviour unchanged.

## R12 — Testing

**Decision**: Pure logic (`ClipSet`, owner-aware `NoteOverwrite`, paste routing) gets
`tests/Nota.SmokeTest/MultiClipTests.cs` (runnable alone: `--multiclip`), plus one
engine-driven check that a grouped `SetClipNotes` on two clips is undone by one
`Undo`. UI is verified with the headless Avalonia recipe (memory: nota-ui-verify) and a
manual pass from [quickstart.md](quickstart.md).
