---

description: "Tasks: Rearrange the song by moving sections with their content"
---

# Tasks: Rearrange the song by moving sections with their content

**Input**: Design documents from `specs/073-section-rearrange/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/abi.md](contracts/abi.md), [quickstart.md](quickstart.md)

**Tests**: included — the constitution (IV) requires engine behaviour changes to extend
`tests/Nota.SmokeTest`. Engine tests live in a new `tests/Nota.SmokeTest/SectionRearrangeTests.cs`
(`internal static class SectionRearrangeTests { public static IEnumerable<(bool ok, string label)> Run() }`,
registered in `tests/Nota.SmokeTest/Program.cs` next to `ArrangementMovesTests.Run()`).

**Organization**: grouped by user story (spec.md US1–US5).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on unfinished tasks)
- **[Story]**: US1 … US5 from spec.md

---

## Phase 1: Setup

- [X] T001 Create branch `feat/section-rearrange` off `develop` (user commits; do not commit or push)
- [X] T002 Create `tests/Nota.SmokeTest/SectionRearrangeTests.cs` (SPDX two-line header, empty `Run()` yielding nothing) and register `foreach (var (ok, label) in SectionRearrangeTests.Run()) Check(ok, label);` in `tests/Nota.SmokeTest/Program.cs` beside `ArrangementMovesTests`

---

## Phase 2: Foundational (engine time-mapping primitives)

**Purpose**: the shared engine machinery every with-content operation uses (research R2–R5).

**⚠️ CRITICAL**: no user story work before this phase is done.

- [X] T003 In `src/native/nota.engine/src/engine/Engine_Tracks.cpp` (anonymous namespace next to `laneSlice` / `laneInsertGap`) add a `TimeMap` helper: given `(a, b, d)` with `L = b − a > 0`, `d ∉ (a, b)`, return the offset for a position by region — `d <= a`: `[a,b) → −(a−d)`, `[d,a) → +L`; `d >= b`: `[a,b) → +(d−b)`, `[b,d) → −L`; else 0 — plus the sorted cut points `{a, b, d}` and the affected window `[min(a,d), max(b,d))`
- [X] T004 In `Engine_Tracks.cpp` add `laneRemap(AutomationLane&, const TimeMap&)`: keep points outside the window untouched; rebuild the window from `laneSlice` pieces of each region, re-based to the region's new start, each piece starting with an explicit point at its edge value (FR-005); return false when the lane is empty or unchanged
- [X] T005 In `Engine_Tracks.cpp` add `remapTrack(const Track&, const TimeMap&)` → cloned track or null: MIDI clips and audio clips cut at the cut points with `midiSlice` / `audioSlice` (pieces keep audio and notes in time) and shifted by their region's offset; every `automation` lane through `laneRemap`; `takes` shifted by the offset of their `originBeat` (research R6); clips wholly outside the window kept as-is
- [X] T006 Add `bool Engine::moveTimeRange(double a, double b, double d)` (private, declared in `src/native/nota.engine/src/engine/Engine.h`) in `Engine_Tracks.cpp`: new `Graph`, `inheritGraphLevel`, `remapTrack` over every track and `masterTrack`, `laneRemap` on `g->masterVolume`, every section's start shifted by the same map (sections re-sorted), then one `pushUndo()` + `publishRaw()`; false (and no undo step) when nothing changed
- [X] T007 Add `bool Engine::removeTimeRange(double a, double b)` (private) in `Engine_Tracks.cpp`: clips cut at `a`/`b`, the inside dropped, everything `>= b` shifted by `−L` (clips, lanes via slices, takes, master volume); sections inside `[a,b)` removed, later ones shifted; one undo step
- [X] T008 Add `bool Engine::sectionOverlaps(int index) const` in `Engine_Tracks.cpp` / `Engine.h`: true when section `index`'s span intersects another section's by more than `1e-6` beats (data-model § Section); plus a private `boundaryBeat(int k)` — `k < n` → `sections[k].startBeat`, `k == n` → end of the last section

**Checkpoint**: primitives compile; no public surface yet.

---

## Phase 3: User Story 1 — Drag a section to reorder the song (P1) 🎯 MVP

**Goal**: a plain drag of a section's body moves it with all its content to a section boundary.

**Independent Test**: four sections with a distinct MIDI clip, audio clip and automation ramp in
each; drag the third before the second; playback and positions match; one ⌘Z restores.

### Tests for User Story 1

- [X] T009 [P] [US1] In `tests/Nota.SmokeTest/SectionRearrangeTests.cs` add the engine block from quickstart § 1: sections A [0,8) · B [8,16) · C [16,24), two instrument tracks (one MIDI clip crossing 8), one audio track, a volume ramp, master volume automation; assert `MoveSection(2, 1)` → A · C · B with C's content at 8 and B's at 16, the crossing clip cut at 8, automation values at 0, 8⁻, 8, 16⁻, 16, 24 equal the original values at the mapped positions, nothing ≥ 24 moved; `MoveSection(0, 3)` → B · C · A; `MoveSection(1, 1)` and `(1, 2)` false with no undo step; Undo restores clips, lanes and sections exactly, Redo repeats; names and automatic mode preserved
- [X] T010 [P] [US1] In `SectionRearrangeTests.cs` add the render checks (SC-002/003) with a deterministic WAV clip (not synth notes): offline render after move + undo equals the render before; the moved section's span renders the same at its new place
- [X] T011 [P] [US1] In `SectionRearrangeTests.cs` add: a take (specs/067) whose origin is inside B moves with B; the playhead position is unchanged by a move during playback (FR-011)

### Implementation for User Story 1

- [X] T012 [US1] Add public `bool Engine::moveSection(int index, int toBoundary)` in `Engine_Tracks.cpp` / `Engine.h`: reject out-of-range, `toBoundary ∈ {index, index+1}` (no-op), `sectionOverlaps(index)`, or a boundary beat strictly inside an overlap; else `moveTimeRange(start, start+len, boundaryBeat(toBoundary))`
- [X] T013 [US1] Declare `nota_section_move` and `nota_section_overlaps` in `src/native/nota.engine/include/nota/nota_engine.h` after `nota_engine_set_sections`, with the comment block from contracts/abi.md; implement them in `src/native/nota.engine/src/abi/nota_engine_core.cpp` beside `nota_engine_set_sections` (`NOTA_OK` / `NOTA_ERR_INVALID_ARG`)
- [X] T014 [US1] Add `[LibraryImport]`s `SectionMove` / `SectionOverlaps` in `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs` beside `SetSections`; add `bool MoveSection(int index, int toBoundary)` and `bool SectionOverlaps(int index)` to `src/managed/Nota.Application/Engine/IAudioEngine.cs` (with doc comments) and implement them in `src/managed/Nota.Infrastructure/Interop/NotaEngine.cs` near `SetSections`
- [X] T015 [US1] In `src/managed/Nota.App/ArrangementView.Sections.cs` add `Mode.Reorder`: on press over a body without Option/Alt, and when the section does not overlap (`_engine.SectionOverlaps(index)`), enter Reorder once past the 3 px threshold (Option/Alt or an overlapping section → existing `Mode.Move`; for an overlap set the status text explaining why, FR-009)
- [X] T016 [US1] In `ArrangementView.Sections.cs` compute the target boundary while dragging: compare the pointer beat with each other section's midpoint (research R8); keep `_reorderFrom` (start-sorted index) and `_reorderTo` (0..n); skip boundaries inside an overlap
- [X] T017 [US1] In `ArrangementView.Sections.cs` on release in Reorder: if `_reorderTo` is not a no-op call `_engine.MoveSection(from, to)`, then `ClearTimeSelection()`, `Refresh()`, `ReloadSections()`, raise `SectionsChanged`; a no-op or failed call changes nothing (no seek either once the drag threshold was passed)
- [X] T018 [US1] Run the US1 smoke tests (T009–T011) via `scripts/build.sh` and fix until green

**Checkpoint**: dragging reorders with content; MVP usable without a preview.

---

## Phase 4: User Story 2 — See where the section will land (P1)

**Goal**: preview while dragging, auto-scroll, Esc cancels, a no-op drop records nothing.

**Independent Test**: drag across the lane — the insertion bar jumps at midpoints; Esc mid-drag
changes nothing; dropping on the original place adds no undo step.

- [X] T019 [US2] In `ArrangementView.Sections.cs` `Render`: while in Reorder draw the other sections at their preview positions (the R2 mapping computed in C# for `(from, to)`), the dragged section as a ghost under the pointer (section fill at reduced opacity), and a 2 px insertion bar in the accent brush at the target boundary — tokens per `DESIGN.md` / `nota-design`, no hard-coded colours
- [X] T020 [US2] In `ArrangementView.Sections.cs` add edge auto-scroll during Reorder (and Move), reusing the `EnsureAutoScroll` / `AutoScrollTick` pattern from `src/managed/Nota.App/ArrangementView.LaneControl.cs`
- [X] T021 [US2] In `ArrangementView.Sections.cs` add `internal bool CancelGesture()` on `SectionsControl` (restore the working copy via `ReloadSections()` after clearing the mode, release capture) and expose it from `ArrangementView`; route Esc to it in `src/managed/Nota.App/MainWindow.Input.cs` next to the existing `CancelGesture` calls; lost window focus cancels too, as the lane does
- [X] T022 [US2] Headless check (done in the throwaway UI harness /tmp/sections073harness, not in the smoke test — the Sections lane is a private Avalonia control) in `SectionRearrangeTests.cs` (or the existing headless UI harness): a Reorder drag dropped on its own boundary makes no engine call and adds no undo step; `CancelGesture()` mid-drag leaves sections and clips unchanged

**Checkpoint**: US1 + US2 = the full P1 experience.

---

## Phase 5: User Story 3 — Move the section marker alone (P2)

**Goal**: Option/Alt-drag keeps today's marker-only move; edge resizing never moves content.

**Independent Test**: ⌥-drag a section two bars — only the section moved; Undo restores it.

- [X] T023 [US3] In `ArrangementView.Sections.cs` make sure `Mode.Move` is reached only with Option/Alt held at press (or for an overlapping section) and behaves exactly as before (`SnapBar`, `SectionsEdited()` → one undo step); `EdgeLeft` / `EdgeRight` unchanged
- [X] T024 [P] [US3] In `src/managed/Nota.App/PreferencesWindow.cs` replace the "Drag section · its edge" row with "Drag section" → "Reorder the song: the section and everything in it move to the marked boundary", "⌥-drag section" → "Move only the section marker, snapped to bars", "Drag section edge" → "Resize it, snapped to bars" (via the `nota-shortcuts` skill)
- [X] T025 [US3] Headless check (UI harness, as T022; the engine side is covered by the smoke test) in `SectionRearrangeTests.cs`: a marker-only move changes `Sections` but no clip or automation position

---

## Phase 6: User Story 5 — Duplicate or delete a section with its content (P2)

**Goal**: menu + palette commands that ripple the rest of the song.

**Independent Test**: A · B · C → Duplicate with content on B gives A · B · B′ · C, C later by
B's length; Delete with content on B gives A · C; Undo restores each.

### Tests for User Story 5

- [X] T026 [P] [US5] In `SectionRearrangeTests.cs`: `DuplicateSectionWithContent(1)` → B′ at 16 with B's content and name (automatic stays automatic), C at 24, automation continuous at 16 and 24; `DeleteSectionWithContent(1)` → A · C, C at 8, B's content gone, a clip crossing 8 or 16 cut there; both one undo step; both false on an overlapping section

### Implementation for User Story 5

- [X] T027 [US5] Add `bool Engine::duplicateSectionWithContent(int index)` in `Engine_Tracks.cpp` / `Engine.h`: reject out-of-range / overlapping; reuse the `insertTime(b, L, duplicate=true)` track/lane logic (factor its body into a helper that takes the target `Graph`) and in the same graph copy every section inside `[a,b)` to `+L` and shift sections `>= b` by `+L`; one undo step
- [X] T028 [US5] Add `bool Engine::deleteSectionWithContent(int index)` in `Engine_Tracks.cpp` / `Engine.h`: reject out-of-range / overlapping; `removeTimeRange(start, start+len)`
- [X] T029 [US5] Declare/implement `nota_section_duplicate_with_content` and `nota_section_delete_with_content` in `nota_engine.h` / `nota_engine_core.cpp`; imports in `NativeMethods.cs`; `DuplicateSectionWithContent` / `DeleteSectionWithContent` in `IAudioEngine.cs` and `NotaEngine.cs`
- [X] T030 [US5] In `ArrangementView.Sections.cs` `ShowMenu`: add **Duplicate with content** (GlyphKind.Duplicate) next to Duplicate and **Delete with content** (`MenuKit.Danger`, GlyphKind.Trash) next to Delete; disabled when `SectionOverlaps`; after success `ClearTimeSelection()`, `Refresh()`, `ReloadSections()`, `SectionsChanged`
- [X] T031 [US5] Add `internal string DuplicateSectionWithContentAtPlayhead()` / `DeleteSectionWithContentAtPlayhead()` in `ArrangementView.Sections.cs` (status text like `ResetSectionNameAtPlayhead`, incl. "No section at the playhead" and the overlap reason); register `edit.duplicateSectionWithContent` / `edit.deleteSectionWithContent` in `src/managed/Nota.Application/Palette/CommandCatalog.cs` (category Edit, view "Arrangement") and bind them in `src/managed/Nota.App/MainWindow.Commands.cs`

---

## Phase 7: User Story 4 — Move a section without dragging (P3)

**Goal**: Move earlier / later from the menu and the palette.

**Independent Test**: A · B · C → Move later on A gives B · A · C; Move earlier disabled on the first.

- [X] T032 [US4] In `ArrangementView.Sections.cs` `ShowMenu` add **Move earlier** / **Move later** (a new group above Duplicate) calling `_engine.MoveSection(i, i-1)` / `MoveSection(i, i+2)`; disabled for the first / last section and for an overlapping one
- [X] T033 [US4] Add `MoveSectionAtPlayhead(bool later)` returning status text in `ArrangementView.Sections.cs`; register `edit.moveSectionEarlier` / `edit.moveSectionLater` in `CommandCatalog.cs` and bind them in `MainWindow.Commands.cs`
- [X] T034 [P] [US4] In `SectionRearrangeTests.cs`: `MoveSection(0, 2)` on A · B · C gives B · A · C (the "Move later" call shape); palette catalog contains the four new ids (extend `PaletteTests` if it enumerates ids)

---

## Phase 8: MCP (FR-014, serves US1/US4/US5)

- [X] T035 [P] In `src/managed/Nota.Mcp/Tools/ProjectTools.cs` add `move_section(index, toBoundary)`, `duplicate_section_with_content(index)`, `delete_section_with_content(index)` per contracts/abi.md — `Mutate(...)` returning `Listed()`, an error message when the engine call returns false (out of range, overlap, no-op); descriptions say indices are `list_sections` order and everything in the span moves in one undo step
- [X] T036 [P] Extend the MCP tests in `tests/Nota.SmokeTest` (where `list_sections` / `set_sections` are covered) for the three tools

---

## Phase 9: Polish & Cross-Cutting Concerns

- [X] T037 [P] Add every new `Loc.T` string (menu entries, palette titles / keywords, status texts, Shortcuts rows) to `src/managed/Nota.App/Languages/en.json` and `fr.json`; run the localization design-token check in the smoke test
- [X] T038 Performance check (SC-004): 50 tracks / 500 clips, move a section and time release → redraw ≤ 200 ms; during playback `DropoutCounter` stays 0
  - Measured headless (/tmp/sections073harness `perf`): the engine edit takes 0.2 ms; release → redraw is ~400 ms, almost all of it the existing full `ArrangementView.Refresh()` — Insert silence on the same project costs the same (210–370 ms). Not met at first; met by T047. Dropout during playback not measured on a device.
- [X] T039 Run quickstart.md §§ 2–3 (MCP and UI); capture before/after headless screenshots of the drag preview for the PR (`nota-pr` skill)
- [X] T040 Post a scope note on issue #192 (Duplicate / Delete with content added in clarification) — ask the user before posting
  - Posted: https://github.com/nota-daw/nota/issues/192#issuecomment-6602859981

### Nota closing tasks *(PR gates)*

- [X] T041 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T042 AGPL-3.0-only SPDX two-line header on every new source file (`SectionRearrangeTests.cs`)
- [X] T043 New dependency: N/A
- [X] T044 `CHANGELOG.md` `[Unreleased]` → Added: reorder sections with content (drag, Move earlier / later), Duplicate / Delete with content, MCP tools; Changed: plain section drag now moves content, ⌥-drag moves the marker only
- [X] T045 FEATURES.md via `nota-features` (Sections bullet); user docs EN + RU via `nota-docs`; shortcuts via `nota-shortcuts` (T024)
- [X] T046 Engine behaviour change → smoke test extended (T009–T011, T026, T034, T036) ✔ confirm all run in `scripts/build.sh`

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2** → all stories.
- **US1 (Phase 3)** needs Phase 2. **US2** builds on US1's Reorder mode (same file). **US3**
  touches the same drag code — do after US1. **US5** needs Phase 2 only (engine) plus the menu
  in `ArrangementView.Sections.cs`. **US4** needs `MoveSection` from US1.
- **MCP (Phase 8)** needs T012/T014 (move) and T027–T029 (duplicate/delete).
- **Polish** last.

Within the engine, T003 → T004 → T005 → T006/T007 → T012/T027/T028. ABI (T013, T029) before
managed wrappers (T014, T029) before UI.

### Parallel Opportunities

- T009, T010, T011 (tests, one file but independent blocks — write together before T012).
- T024 (PreferencesWindow), T035/T036 (MCP), T037 (strings) touch separate files and can run
  alongside UI work once the engine API exists.
- US5 engine work (T027–T029) can proceed in parallel with US1/US2 UI work (T015–T021): different
  files after Phase 2.

## Parallel Example: after Phase 2

```text
Task: "T012–T014 moveSection engine + ABI + managed wrapper"
Task: "T027–T029 duplicate/delete with content engine + ABI + managed wrappers"
Task: "T009–T011 SectionRearrangeTests engine blocks"
```

## Implementation Strategy

1. **MVP** = Phases 1–3 (drag reorders with content, one undo step). Validate with T018.
2. Add US2 (preview, Esc, auto-scroll) — together with US1 the full P1 experience.
3. US3 (marker-only ⌥-drag + Shortcuts), then US5 (duplicate / delete with content), then US4
   (menu / palette moves), then MCP.
4. Polish and gates; one PR closing #192.

## Notes

- Same-file tasks (`ArrangementView.Sections.cs`, `Engine_Tracks.cpp`) are sequential.
- Don't commit or push — the user reviews and commits.

## Phase 10: Convergence

- [X] T047 Bring release → redraw after a section move/duplicate/delete within 200 ms on 50 tracks / 500 clips (refresh only what the edit touched instead of the full `ArrangementView.Refresh()` in `src/managed/Nota.App/ArrangementView.Sections.cs` `SectionContentEdited`), and measure dropouts while moving during playback, per SC-004 / FR-011 (partial)
  - `SectionContentEdit` refreshes with `RefreshMovedClips()` (no header rebuild; audio peaks kept by source, not index) unless a take lane came or went. Headless, 50 tracks × 10 clips: move → refresh → frame 6–14 ms (was ~400). Reused peaks match a full refresh, cut clips included (/tmp/sections073harness `peaks`). Dropouts while moving during playback not measured on a device (the edit is one snapshot swap, AR-5).
- [X] T048 Pick the drop boundary by the pointer's position against each other section's midpoint (not the ghost's centre) in `SectionsControl.TargetBoundary` (`src/managed/Nota.App/ArrangementView.Sections.cs`), or record the ghost-centre rule in the artifacts, and keep the docs' "until its middle passes" wording in step, per US2/AC1 and plan R8 (contradicts)
  - Kept the ghost-centre rule (the grab point doesn't change where it lands); recorded in spec US2/AC1 and research R8. The docs already describe it.
- [X] T049 With only one section, make a plain body drag a marker-only move (`Mode.Move`) instead of a Reorder that can never land, in `SectionsControl.OnPointerPressed` (`src/managed/Nota.App/ArrangementView.Sections.cs`), per spec Edge Cases "Only one section" (contradicts)
  - `_sections.Count < 2` → `Mode.Move`.
- [X] T050 Handle take lanes in the ripple edits: a take whose first played frame is inside a deleted section's span is removed (or documented as kept), and Duplicate with content either copies takes in the span or the spec states it doesn't, in `Engine::deleteSectionWithContent` / `duplicateSectionWithContent` (`src/native/nota.engine/src/engine/Engine_Tracks.cpp`), per FR-015 / FR-016 (partial)
  - Duplicate copies a take that starts in the section (new id and number) and points the copied comp clips at it; delete drops such a take unless a comp clip elsewhere still plays from it. Smoke-tested.
- [X] T051 Add a save → reopen check that a moved arrangement (clips, automation, sections) round-trips through the project file in `tests/Nota.SmokeTest/SectionRearrangeTests.cs`, per FR-013 (partial)
  - Save → `ProjectService.Load` / `Apply` into a fresh engine: sections, notes and automation as moved. Smoke-tested.
