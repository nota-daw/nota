# Contract: C ABI, managed engine API and MCP tools

## C ABI (`src/native/nota.engine/include/nota/nota_engine.h`, next to `nota_engine_set_sections`)

```c
/* Section edits that carry the arrangement with them (specs/073). Indices are into the
 * start-sorted section list. Each is one undo step covering clips (audio + MIDI, cut at the
 * edges), automation (every track, the master track, master volume), take lanes and the
 * sections; the playhead does not move. Errors (NOTA_ERR_INVALID_ARG, nothing changed):
 * index out of range, a section that overlaps another, a target inside an overlap, or a
 * no-op. Message thread only. */

/* Move section `index` to boundary `to_boundary` (0..count): before section k, or after the
 * last when == count. The sections in between shift by its length; song length unchanged. */
NOTA_API NotaResult nota_section_move(NotaEngine* engine, int32_t index, int32_t to_boundary);

/* Insert a copy of section `index` and everything in its span right after it; the rest of
 * the song moves later by its length. The copy keeps the name (automatic stays automatic). */
NOTA_API NotaResult nota_section_duplicate_with_content(NotaEngine* engine, int32_t index);

/* Remove section `index` and everything in its span; the rest moves earlier by its length. */
NOTA_API NotaResult nota_section_delete_with_content(NotaEngine* engine, int32_t index);

/* 1 when section `index` overlaps another (with-content edits unavailable), else 0. */
NOTA_API int32_t    nota_section_overlaps(const NotaEngine* engine, int32_t index);
```

## Managed (`Nota.Application/Engine/IAudioEngine.cs`, `Nota.Infrastructure/Interop`)

```csharp
/// <summary>Move section <paramref name="index"/> with its content to boundary
/// <paramref name="toBoundary"/> (0..Sections.Count). False when rejected / no-op.</summary>
bool MoveSection(int index, int toBoundary);
bool DuplicateSectionWithContent(int index);
bool DeleteSectionWithContent(int index);
bool SectionOverlaps(int index);
```

`NativeMethods.Tracks.cs` (or the sections partial) gets the four `LibraryImport`s.

## MCP (`Nota.Mcp/Tools/ProjectTools.cs`)

| Tool | Params | Returns | Notes |
|---|---|---|---|
| `move_section` | `index`, `toBoundary` | the section list (as `list_sections`) | error text when rejected (overlap, out of range, no-op) |
| `duplicate_section_with_content` | `index` | section list | |
| `delete_section_with_content` | `index` | section list | |

Descriptions spell out: indices are `list_sections` order; everything in the span moves;
one undo step.

## Command palette (`Nota.Application/Palette/CommandCatalog.cs`)

| Id | Title | Acts on |
|---|---|---|
| `edit.moveSectionEarlier` | Move section earlier | section at the playhead |
| `edit.moveSectionLater` | Move section later | section at the playhead |
| `edit.duplicateSectionWithContent` | Duplicate section with content | section at the playhead |
| `edit.deleteSectionWithContent` | Delete section with content | section at the playhead |

No section at the playhead / disabled case → status text, nothing changes (as
`edit.resetSectionName`).
