# Tasks: Piano roll Fold

**Input**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/)

Format: `[ID] [P?] [Story] Description`. [P] = can run in parallel (different files).

## Phase 1: Foundation (blocks every story)

- [ ] T001 [P] Add `PianoRowMap` (rows, `RowOf`, `PitchAt`, `Step`, labels, marks, `IsChromatic`,
  `Build`) in `src/managed/Nota.Presentation/PianoRowMap.cs`.
- [ ] T002 [P] Move the pad scan out of `DrumPatternView.ScanPads` into
  `src/managed/Nota.Presentation/DrumPads.cs` (joins pads on one note), and call it from
  `DrumPatternView`.
- [ ] T003 Route every vertical mapping in `PianoRollView.cs` through the map: `RangeH`, the keys
  column, the grid rows, hover, held keys, audition, `EnsurePitchVisible`, the initial scroll and
  `PitchAt`/`YToPitch`. Unfolded output must be pixel-identical to develop.
- [ ] T004 Smoke: `FoldTests` (`--fold`) checks that the chromatic map matches the old formula
  for every pitch and every y; register it in `Program.cs` with the full run.

## Phase 2: User Story 1, scale fold (P1)

- [ ] T005 [US1] `PianoRowMap.Build` scale case: in-scale pitches plus used pitches, marked
  `OutOfScale`; the no-scale case uses used pitches; an empty set is chromatic with the hint.
- [ ] T006 [US1] Drag (`DragUpdate` Move) and `MoveSelection` step by rows when folded (research
  R2); ⇧↑/⇧↓ stay ±12. Freeze the map during a gesture (R3).
- [ ] T007 [US1] New-note drawing and paste land on row pitches; the selection rectangle selects
  by rows.
- [ ] T008 [US1] Fold switch in the `ClipEditorView` toolbar beside Scale, with tooltip; keep
  the view centred on the same notes when toggling or when the scale changes (SC-004).
- [ ] T009 [US1] Smoke: A-minor map rows, E→F→G drag steps, a chord keeps its shape in degrees,
  out-of-scale used pitch keeps a marked row, toggle restores chromatic.

## Phase 3: User Story 2, Drum Rack fold (P1)

- [ ] T010 [US2] Drum Rack case in `Build` (pads plus used pitches, `NoPad` marks, joined names);
  find the window's rack-changed signal and rebuild the map on it (FR-006).
- [ ] T011 [US2] Keys column draws pad names when folded on a Drum Rack; a key click auditions the
  row pitch.
- [ ] T012 [US2] Smoke: a 16-pad kit gives 16 named rows, a note with no pad keeps an unnamed row,
  and renaming a pad changes its label.

## Phase 4: User Story 3, memory (P2)

- [ ] T013 [P] [US3] Engine `pianoFold` beside `colorIndex` in `Track.h`, set/get in the engine,
  copied by every track clone path (duplicate, copy/paste, freeze-linked clones).
- [ ] T014 [P] [US3] ABI `nota_track_set/get_piano_fold` in `nota_engine.h` and
  `nota_engine_track.cpp`; managed `NativeMethods`, `NotaEngine`, `IAudioEngine`.
- [ ] T015 [US3] `ProjectModel.PianoFold` (`bool?`, written only when true), Capture and Apply in
  `ProjectService`.
- [ ] T016 [US3] The piano roll reads the track's flag on clip open and writes it on toggle.
- [ ] T017 [P] [US3] MCP `set_piano_fold` / `get_piano_fold`.
- [ ] T018 [US3] Smoke: the flag survives save and load, an old project opens unfolded, duplicate
  copies it, and the MCP round trip works.

## Phase 5: Polish

- [ ] T019 Shortcut ⌥⇧F through the `nota-shortcuts` skill (shortcuts pane + docs).
- [ ] T020 SC-005 timing check: a 2,000-note fold builds in under 16 ms (smoke, CI only budget).
- [ ] T021 Docs EN+RU: piano roll page (Fold section with both modes), FEATURES.md, CHANGELOG.
- [ ] T022 macOS hands-on check with before/after screenshots (Volt in A minor, Kompakt kit) into
  `pr-assets/pr-<N>/`.

## Dependencies

T001–T004 block everything. US1 and US2 can run in parallel after T004. US3 (T013–T018) is
independent of US1 and US2 apart from T016. Polish comes last.
