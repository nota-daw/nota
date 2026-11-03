# Quickstart: validate section rearranging

## Prerequisites

- A Release engine build (`build/` stays Release; a `-O0` engine hangs and xruns).
- `scripts/build.sh` green before and after — it runs `tests/Nota.SmokeTest`.

## 1. Engine, through the C ABI (smoke test — the PR gate)

The smoke test gains a `Section rearrange (specs/073)` block next to the Insert silence tests
(`tests/Nota.SmokeTest/Program.cs`). It builds three back-to-back sections A [0,8) · B [8,16) ·
C [16,24) over two instrument tracks and one audio track, with a MIDI clip crossing A|B, a
volume ramp on one track and master volume automation, then checks:

| Case | Expect |
|---|---|
| `MoveSection(2, 1)` | order A · C · B; C's content at 8, B's at 16; the crossing clip cut at 8 and its B part at 16; automation values at 0, 8⁻, 8, 16⁻, 16, 24 match the corresponding original positions; nothing ≥ 24 moved |
| `MoveSection(0, 3)` | order B · C · A; A's content at 16 |
| `MoveSection(1, 1)` / `(1, 2)` | false; no undo step |
| Undo after a move | clips, lanes, sections identical to before; Redo repeats |
| `DuplicateSectionWithContent(1)` | B · B′ with B′ at 16, C at 24, B′ automatic name stays automatic |
| `DeleteSectionWithContent(1)` | A · C, C at 8, B's content gone |
| overlapping B and C | `SectionOverlaps` true; all three ops false, nothing changed |
| render compare (SC-002/003) | offline render after move + undo equals the render before; moved section's span renders the same at its new place (deterministic WAV clip, not synth notes — synth renders drift) |

## 2. MCP

With the app running and the MCP server enabled: `list_sections` → `move_section {index: 2,
toBoundary: 1}` → `list_sections` shows the new order; `move_section` on an overlapping
section returns the error text.

## 3. UI (headless app shots, then by hand)

1. Open a project with 4 sections and content in each.
2. Drag section 3 left: the ghost follows, the insertion bar jumps boundary to boundary at
   each section's midpoint, the others preview their new places.
3. Drop before section 2 → content follows, one ⌘Z restores.
4. Start a drag, press Esc → nothing changed.
5. ⌥-drag a section by two bars → only the marker moved.
6. Right-click: Move earlier disabled on the first section, Move later on the last; Duplicate /
   Delete with content work and undo.
7. Make two sections overlap → their with-content entries are disabled, a plain drag moves
   the marker and the status bar explains.
8. Repeat 3 during playback: no dropout (DropoutCounter stays 0), playhead keeps its time.
9. Preferences → Shortcuts lists both section drags.
