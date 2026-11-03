# Research: Rearrange the song by moving sections with their content

All spec decisions are settled (spec § Clarifications). This file records the technical
choices the plan rests on.

## R1 — Where the move happens: one engine edit, not a managed choreography

**Decision**: the engine does each with-content operation (move, duplicate, delete) as **one**
authoring edit: build a new `Graph` with every affected track cloned, sections rewritten,
then `pushUndo()` + `publishRaw()` once — the pattern `Engine::insertTime` already uses
(`Engine_Tracks.cpp`).

**Rationale**: FR-007 needs content and sections in a single undo step. Sections already live
in the graph snapshot (`Graph::sections`, specs/016), so a graph-level edit carries them for
free. Composing existing calls from C# (`CutRange` + `InsertTime` + `SetSections`) would make
three undo steps, use the user's clipboard, and publish three snapshots during playback.

**Alternatives considered**: managed composition with an undo "transaction" wrapper — no such
mechanism exists in the engine and adding one is a larger change than the edit itself.

## R2 — The primitive: move a time range, as a piecewise shift

**Decision**: a new `Engine::moveTimeRange(a, b, d)` — move `[a,b)` so it is inserted at
original-timeline position `d` (`d <= a` or `d >= b`). With `L = b − a` the timeline maps as:

| original span | `d <= a` (move earlier) | `d >= b` (move later) |
|---|---|---|
| `[a,b)` (moved) | `− (a − d)` | `+ (d − b)` |
| between `[d,a)` / `[b,d)` | `+ L` | `− L` |
| everything else | unchanged | unchanged |

Clips are cut at the three cut points (`a`, `b`, `d`) with the existing `midiSlice` /
`audioSlice` helpers, and each piece is shifted by its region's offset. Everything outside
`[min(a,d), max(b,d))` is untouched (FR-002, SC-005); total length never changes.

**Rationale**: the same cut rule as Insert silence / Duplicate time (spec Assumptions); one
mapping function drives clips, automation, takes and sections alike, which keeps them
consistent by construction.

**Alternatives considered**: "cut range with ripple, then insert at d'" — equivalent result
but needs the intermediate coordinate shift explained twice and two passes over every lane.

## R3 — Delete with content: `removeTimeRange(a, b)`

**Decision**: a new `Engine::removeTimeRange(a, b)`: clips cut at `a` and `b`, the inside
dropped, everything at or after `b` shifted by `−L`; sections inside `[a,b)` dropped and later
ones shifted. **Duplicate with content** reuses `insertTime(b, L, duplicate=true)`'s logic,
extended to copy and shift sections (R5).

## R4 — Automation continuity at cuts and joins (FR-005)

**Decision**: rebuild each lane over the affected window from `laneSlice` pieces (which
already carry the lane's exact value at both edges and keep a partial segment's curvature),
re-based to their new positions; points outside the window are kept as they are. At each
join the incoming piece starts with an explicit point at the edge value, so the curve inside
every piece plays exactly as before. A join between two pieces with different edge values
becomes a step at the join — the same behaviour Duplicate time has, and the only faithful
choice (the music on each side changes there too).

Applies to every track lane, the master track's lanes and `Graph::masterVolume`. Empty lanes
stay empty.

## R5 — Sections in the edit; overlap rule (FR-006, FR-009)

**Decision**: the engine takes **section indices**, not beats, for the user-facing ops:
`moveSection(index, boundary)`, `duplicateSectionWithContent(index)`,
`deleteSectionWithContent(index)`. It validates (index range, no-op, overlap) and applies the
R2 mapping to every section's start. A section overlaps if its span intersects any other
section's span by more than an epsilon. A section that overlaps another, or a drop boundary
that would land inside an overlap, is rejected (`NOTA_ERR_INVALID_ARG`) — the UI checks the
same predicate first to disable menu items and fall back to a marker-only drag.

Boundary `k` (0..n over the start-sorted list): `k < n` → `sections[k].start`;
`k == n` → end of the last section. `k == i` or `k == i + 1` is a no-op (no undo step).
With a gap between X and Y, boundary "before Y" is Y's start — the gap stays after X
(spec Edge Cases).

**Rationale**: indices keep the C ABI small and make the overlap rule the engine's to
enforce, so MCP gets the same validation as the UI (FR-014).

## R6 — Takes (specs/067) and frozen tracks

**Decision**: take lanes move by the mapping of their `originBeat` (a whole take travels with
the region its start is in). Comp clips are ordinary `AudioClip`s and are cut like any clip.
Frozen tracks: their clips move like any track's, but the frozen buffer is not re-rendered —
exactly what Insert silence does on a frozen track today. Recorded as a known limitation,
unchanged by this feature.

**Note**: `insertTime` does not move takes or sections today. Fixing that for Insert silence /
Duplicate time is out of scope (separate issue), but the duplicate-with-content path shares the
new code and does move both.

## R7 — Real-time safety (AR-4/5/6)

All work happens on the message thread on authoring copies; the audio thread sees one
atomic snapshot swap (`publishRaw`). No new audio-thread code. The playhead (transport
position, AR-9) is not touched (FR-011).

## R8 — UI drag model

**Decision**: `SectionsControl` gains a `Reorder` mode (plain body drag) next to the existing
`Move` (now Option/Alt only, checked at press time and live while dragging). In `Reorder`:

- the target boundary is chosen by comparing the dragged section's middle (the ghost's centre)
  with each other section's midpoint (US2 scenario 1) — not the pointer, so the grab point
  doesn't shift where it lands;
- render: the dragged span as a ghost under the pointer, a 2 px accent insertion bar at the
  target boundary, and the other sections drawn at their preview positions (same R2 mapping,
  computed in C#);
- edge auto-scroll reuses the lane's `EnsureAutoScroll` pattern
  (`ArrangementView.LaneControl.cs`);
- Escape cancels through a `CancelGesture()` like the lane's and is routed from
  `MainWindow.Input.cs`;
- release on a no-op boundary or before the 3 px drag threshold: nothing happens (a click
  still seeks, as today).

No new engine call happens until release, so the drag itself has no audio effect.

## R9 — Surfaces

- Section menu: **Move earlier**, **Move later**, **Duplicate with content**, **Delete with
  content** (disabled at the ends / for overlapping sections).
- Command palette (`CommandCatalog.cs` + `MainWindow.Commands.cs`), section at the playhead:
  `edit.moveSectionEarlier`, `edit.moveSectionLater`, `edit.duplicateSectionWithContent`,
  `edit.deleteSectionWithContent`; each reports in the status bar like
  `edit.resetSectionName`.
- MCP (`Nota.Mcp/Tools/ProjectTools.cs`): `move_section`, `duplicate_section_with_content`,
  `delete_section_with_content`.
- Preferences → Shortcuts (`PreferencesWindow.cs`): "Drag section" → reorder with content,
  "⌥-drag section" → move the marker only.
- Strings via `Loc.T` with entries in `Nota.App/Languages/*.json`.
