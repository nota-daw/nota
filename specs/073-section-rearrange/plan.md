# Implementation Plan: Rearrange the song by moving sections with their content

**Branch**: `feat/section-rearrange` | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/073-section-rearrange/spec.md` (issue
[#192](https://github.com/nota-daw/nota/issues/192))

## Summary

Dragging a section in the Sections lane reorders the song: the section's span — every clip,
automation point, take and the section itself — is lifted out and inserted at a section
boundary, with the material in between shifting by its length (Option/Alt-drag keeps today's
marker-only move). The section menu, command palette and MCP gain Move earlier / later,
Duplicate with content and Delete with content. Technically: three new engine edits built on
one piecewise time-mapping primitive (`moveTimeRange`) plus `removeTimeRange`, each producing
a single graph snapshot (one undo step, AR-5), exposed through four C ABI calls; the managed
side adds a reorder drag mode with preview, menu / palette / MCP entries.

## Nota: Read Before Planning

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): AR-5 (one immutable snapshot
  per edit), AR-6 (nothing new on the audio thread), AR-7 (C ABI only), AR-9 (the playhead is
  the transport's — not moved).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: the insertion bar
  is the orange accent (it marks what is live/chosen); the ghost uses the section fill at
  reduced opacity.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C++20 (engine), C# / .NET 10 (managed app, Avalonia)

**Primary Dependencies**: none new

**Storage**: project file unchanged (positions only); no migration

**Audio-Thread Impact**: None new. Each edit is built on the message thread from authoring
copies and published as one immutable graph snapshot (`pushUndo` + `publishRaw`), like
`insertTime`. No allocation, lock or I/O added to the audio path.

**Testing**: `tests/Nota.SmokeTest` (engine through the C ABI, render comparisons, headless
UI checks as in `ArrangementMovesTests.cs`); headless app screenshots for the drag preview

**Target Platform**: macOS, Windows, Linux desktop

**Project Type**: desktop app (native engine + managed UI)

**Performance Goals**: release-to-redraw ≤ 200 ms on 50 tracks / 500 clips (SC-004); drag
preview at display rate (C#-only computation, no engine calls while dragging)

**Constraints**: one undo step per operation; zero change outside the affected window;
no dropout when edited during playback

**Scale/Scope**: ~4 engine functions (+ helpers), 4 ABI calls, 1 view file reworked
(`ArrangementView.Sections.cs`), menu / palette / MCP / shortcuts / docs entries

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #192 `approved`. Duplicate / Delete with content were added in clarification — **post a scope note on #192 before the PR** |
| I. Real-time safety | ✅ message-thread snapshot edits only (research R7) |
| II. Boundary discipline | ✅ four new pure-C functions in `nota_engine.h`; no JUCE |
| III. Licensing | ✅ no new files without SPDX header; no dependencies |
| IV. Verification gate | ✅ smoke test extended (quickstart § 1) |
| V. One logical change | ✅ one feature: section-level edits with content |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs`, `nota-shortcuts` |
| VII. Spec-first | ✅ this run |
| VIII. Rules not weakened | ✅ |

Post-design re-check: unchanged — all gates pass.

## Project Structure

### Documentation (this feature)

```text
specs/073-section-rearrange/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R9 decisions
├── data-model.md        # boundaries, time mapping, drag states
├── quickstart.md        # validation scenarios
├── contracts/abi.md     # C ABI, managed API, MCP, palette
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h            # + nota_section_move / _duplicate_with_content /
│                                         #   _delete_with_content / _overlaps
├── src/engine/Engine.h                   # + moveSection, duplicateSectionWithContent,
│                                         #   deleteSectionWithContent, sectionOverlaps
├── src/engine/Engine_Tracks.cpp          # + moveTimeRange / removeTimeRange helpers next to
│                                         #   insertTime (reuse midiSlice, audioSlice, laneSlice)
└── src/abi/nota_engine_track.cpp         # ABI wrappers

src/managed/
├── Nota.Application/Engine/IAudioEngine.cs          # + 4 members
├── Nota.Application/Palette/CommandCatalog.cs       # + 4 commands
├── Nota.Infrastructure/Interop/NativeMethods*.cs    # + 4 imports
├── Nota.Infrastructure/Interop/NotaEngine*.cs       # + 4 wrappers
├── Nota.App/ArrangementView.Sections.cs             # Reorder mode, preview, auto-scroll,
│                                                    #   CancelGesture, menu entries, commands
├── Nota.App/MainWindow.Commands.cs                  # palette bindings
├── Nota.App/MainWindow.Input.cs                     # Esc → sections CancelGesture
├── Nota.App/PreferencesWindow.cs                    # Shortcuts rows
├── Nota.App/Languages/*.json                        # new strings
└── Nota.Mcp/Tools/ProjectTools.cs                   # 3 MCP tools

tests/Nota.SmokeTest/Program.cs (+ a SectionRearrangeTests.cs if the block grows)
CHANGELOG.md, FEATURES.md, ../nota-docs (EN + RU)
```

**Structure Decision**: engine-first. The engine owns the edit and its validation so the
drag, menu, palette and MCP share one implementation; `ArrangementView.Sections.cs` keeps all
section UI as today.

## Complexity Tracking

No constitution violations.

## Known limitations (documented, not addressed)

- Frozen tracks keep their old frozen render until re-frozen (same as Insert silence today).
- Insert silence / Duplicate time still don't move sections or takes; a follow-up issue can
  route them through the new helpers.
