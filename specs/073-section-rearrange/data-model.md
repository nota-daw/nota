# Data Model: Rearrange the song by moving sections with their content

No new persisted data: the project file shape (`SectionDto`, track clips, automation lanes)
is unchanged — a move only rewrites positions. This file describes the transient model the
operations work on.

## Section (existing — `Graph::sections`, `SectionInfo`, `ArrangementSection`)

| Field | Type | Rule |
|---|---|---|
| `startBeat` | double | ≥ 0 |
| `lengthBeats` | double | > 0 |
| `name` | string | `""` = automatic ("Section N" by position, specs/058) |

Kept sorted by start. **Overlapping** (derived): its span intersects another section's span
by more than 1e-6 beats. Overlapping sections take part in no with-content operation
(FR-009).

## Section boundary (new, transient)

An index `k` in `0..n` over the start-sorted sections.

| k | beat |
|---|---|
| `0..n-1` | `sections[k].startBeat` |
| `n` | `sections[n-1].startBeat + sections[n-1].lengthBeats` |

Moving section `i` to `k ∈ {i, i+1}` is a no-op. A boundary is invalid when its beat lies
strictly inside an overlap.

## Time-range move (new, transient — research R2)

`(a, b, d)` with `L = b − a > 0`, `d ∉ (a, b)`. Mapping `f(t)` for a timeline position:

```text
d <= a:  t in [a,b) → t − (a − d);  t in [d,a) → t + L;  else t
d >= b:  t in [a,b) → t + (d − b);  t in [b,d) → t − L;  else t
```

Applied to: MIDI and audio clips (pieces after cutting at `a`, `b`, `d`), automation lanes
(rebuilt from slices), take origins, section starts. A section move is
`(start_i, start_i + len_i, boundary(k))`.

## Time-range removal (new, transient — research R3)

`(a, b)`: content in `[a,b)` removed (clips cut at both edges), `t >= b → t − L`, sections
inside `[a,b)` removed.

## Range duplicate with sections (extends Duplicate time)

`(a, b)`: a copy of `[a,b)` is inserted at `b`, `t >= b → t + L`; sections inside `[a,b)` are
copied with the same name (automatic stays automatic) and their copies placed `+L`.

## State transitions (UI drag, `SectionsControl`)

```text
None ──press on body──▶ Pending ──moved > 3 px──▶ Reorder   (no modifier)
                                     └────────────▶ Move     (Option/Alt, or overlapping section)
Reorder ──release on new boundary──▶ engine MoveSection ─▶ None (one undo step)
Reorder ──release on no-op boundary / Esc──▶ None (nothing changed)
Pending ──release──▶ seek to section start (existing click)
```
