---

description: "Task list for articulation maps"
---

# Tasks: Articulation maps

**Input**: Design documents from `/specs/085-articulation-maps/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The owner's request lists the tests to write, and Constitution IV requires the smoke test to cover engine behaviour changes.

**Organization**: Grouped by user story (spec.md). `[x]` = written **and** verified by a passing test on 2026-11-09 (full smoke test: 5300 pass, 0 fail). That code was written before the plan (plan, Constitution VII); T089 reviews it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US6 from spec.md
- Engine `src/native/nota.engine/`, managed `src/managed/`, UI `src/managed/Nota.App/`, tests `tests/Nota.SmokeTest/`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Fixtures, build environment, feature pointer

- [x] T001 Copy the owner's real files into `tests/Nota.SmokeTest/Fixtures/expressionmaps/CSSS Violins 1 A.expressionmap` and `tests/Nota.SmokeTest/Fixtures/keyswitches/` (Violins 1, Lite Ensemble); never modify them — *removed before the PR (2026-11-09, owner's choice): they are third-party works (Babylonwaves Art Conductor) and can't be relicensed under AGPL; the tests that needed them were dropped*
- [x] T002 Configure and build the native engine with MSVC x64 + Ninja in `src/native/nota.engine/build` (dependencies fetched with the owner's permission, research R12)
- [x] T003 [P] Point Spec Kit at the feature in `.specify/feature.json`

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: Note id, track offset, map table, ABI, persistence: everything every story needs

**⚠️ CRITICAL**: No user story can be finished before this phase

### Done and verified

- [x] T004 Add `int32_t articulation = 0` to `Note` in `src/native/nota.engine/src/engine/MidiClip.h`; append `int32_t articulation_id` **last** to `NotaNoteData` in `src/native/nota.engine/include/nota/nota_engine.h`; convert in `noteFromData` / `noteToData` (negative → 0) in `src/native/nota.engine/src/engine/Engine_Tracks.cpp`
- [x] T005 [P] Append `public int ArticulationId` **last** to `NotaNote` in `src/managed/Nota.Application/Engine/EngineContracts.cs` (layout must match `NotaNoteData`)
- [x] T006 Add `std::atomic<double> midiOffsetMs_` with accessors to `src/native/nota.engine/src/engine/Track.h`; copy it in `cloneTrack` (`Engine.cpp`) and the duplicate path (`Engine_Tracks.cpp`); `setTrackMidiOffsetMs` clamps to "±2000 ms" and treats NaN as 0 in `Engine_Recording.cpp`
- [x] T007 Expose `nota_track_set_midi_offset_ms` / `nota_track_get_midi_offset_ms` in `nota_engine.h` and `src/abi/nota_engine_track.cpp`; P/Invoke in `Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`; wrappers in `NotaEngine.Tracks.cs`; `IAudioEngine.SetTrackMidiOffsetMs` / `GetTrackMidiOffsetMs`
- [x] T008 Project format v30: `TrackDto.MidiOffsetMs` (omitted when 0) and `NoteDto.Articulation` (omitted when none) in `Nota.Infrastructure/Persistence/ProjectModel.cs`, capture/apply in `ProjectService.cs`, no-op `Migration(29)` in `ProjectMigrations.cs`
- [x] T009 [P] Managed model per data-model.md in `src/managed/Nota.Application/Articulations/ArticulationMap.cs` (ids "never reused after a delete", channel "null or 1..16", data "0..127")
- [x] T010 Engine round-trip tests in `tests/Nota.SmokeTest/ArticulationEngineTests.cs`: ABI layout, one-note edit isolation, undo/redo, clamping, duplicate track, save/reopen, v29 migration

### To do

- [x] T011 Audit every place that rebuilds a `NotaNote` from its fields (`new NotaNote(`) in `src/managed/` (known: `Nota.Mcp/Tools/MidiTools.cs`, `Nota.Mcp/Tools/SessionTools.cs`, `Nota.Application/Twin/TwinPatterns.cs`, `Nota.Application/Midi/MidiTransforms.cs`, `Nota.Application/Editing/NoteOverwrite.cs`, `Nota.Application/Editing/ClipSet.cs`) and carry `ArticulationId` wherever a note is transformed rather than created (FR-013); list each site and its verdict in research.md R11
- [x] T012 Trace every write to `clip.notes` / `MidiClip` in `src/native/nota.engine/src/engine/*.cpp` (note edits, recording, split, trim, consolidate, duplicate) to the publish step (`republishWithTrack`, `republishWithTrackRaw`, `publishRaw`) and record the choke point for timeline rebuilds in research.md R5
- [x] T013 Create `src/native/nota.engine/src/engine/ArticulationTable.h` / `.cpp` (SPDX header): immutable `ArticulationTable { defaultId, entries sorted by id, actions }`, entry `{ id, offsetMs, leadMs, trigger, actionStart, actionCount }`, `resolve(id)` by binary search where "id 0, a missing id or a disabled one resolves to defaultId"; add the files to `src/native/nota.engine/CMakeLists.txt`
- [x] T014 Add the per-clip articulation timeline builder to `ArticulationTable.cpp`: entries `{ startBeat, articulationId, endBeat }` sorted by start; "latching neighbours with the same id collapse into one entry; momentary entries are one per note"; stored on `MidiClip` as `std::shared_ptr<const ArticulationTimeline>`; rebuilt at the T012 choke point on the message thread only
- [x] T015 Store the map JSON blob and `std::shared_ptr<const ArticulationTable>` on `Track` in `src/native/nota.engine/src/engine/Track.h`; copy both in `cloneTrack` (`Engine.cpp`) and the duplicate path (`Engine_Tracks.cpp`)
- [x] T016 Implement `Engine::setTrackArticulationMap` / `clearTrackArticulationMap` / `trackArticulationMapJson` in `src/native/nota.engine/src/engine/Engine_Tracks.cpp` as one undo step each (`cloneTrack` + `republishWithTrack`), validating per contracts/c-abi.md ("ids ascend strictly; each [action_start, action_start+action_count) lies within actions"; nothing changes on error) and rebuilding the track's clip timelines
- [x] T017 Add `NotaArticulationEntry`, `NotaArticulationAction`, `nota_track_set_articulation_map`, `nota_track_clear_articulation_map`, `nota_track_get_articulation_map_json` to `src/native/nota.engine/include/nota/nota_engine.h` and `src/native/nota.engine/src/abi/nota_engine_track.cpp`
- [x] T018 [P] Add `int32_t eventChannel = 0` and `virtual void programChange(int32_t)` (default ignored) to `src/native/nota.engine/src/devices/Instrument.h`; in `src/native/nota.engine/pluginhost/hosting/PluginInstrument.cpp` send note, CC, all-notes-off on `eventChannel + 1` instead of the hardcoded 1 and map `programChange` to `juce::MidiMessage::programChange`; bank select stays `controller(0|32, v/127)` — *verified in Kontakt + Cinematic Studio Strings by the owner, 2026-11-09: keyswitch notes and CC 58 on channel 1 switch the articulation. Not exercised by a real plug-in yet: explicit channels 2–16, program change, bank select (covered by engine self-test check 20 only)*
- [x] T019 Managed interop for T017: structs in `Nota.Application/Engine/EngineContracts.cs`, P/Invoke in `Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, wrappers in `NotaEngine.Tracks.cs`, `IAudioEngine.SetTrackArticulationMap` / `ClearTrackArticulationMap` / `GetTrackArticulationMapJson`
- [x] T020 [P] Create `src/managed/Nota.Application/Articulations/ArticulationMapCompiler.cs`: managed map → sorted entries + flat actions for T019 (channel null → 0)
- [x] T021 [P] Create `src/managed/Nota.Application/Articulations/ArticulationMapFile.cs`: read/write the Nota format per contracts/nota-articulation-map-file.md ("format must be nota-articulation-map", higher version refused, repairs reported, "larger than 16 MiB … or not valid JSON is refused", offsets clamped "±2000 ms and leads to 0..2000")
- [x] T022 Persist the map per track: `TrackDto.ArticulationMap` in `Nota.Infrastructure/Persistence/ProjectModel.cs`; capture from `GetTrackArticulationMapJson` and apply through T020/T019 in `ProjectService.cs`; a malformed map is a load warning, never a failed load (FR-027); note in the v30 comment and migration that the field is absent in v29
- [x] T023 Tests in `tests/Nota.SmokeTest/ArticulationEngineTests.cs` and new `tests/Nota.SmokeTest/ArticulationFileTests.cs`: file round trip yields an equal map; repairs and refusals; the engine rejects unsorted/duplicate ids and out-of-range action spans; set/clear map undo and redo; duplicate track keeps the map; save/reopen restores it; a project with a broken map still loads

**Checkpoint**: A map can be given to a track through the ABI, saved, undone and duplicated

---

## Phase 3: User Story 1 - Import a library's articulation map (Priority: P1) 🎯 MVP

**Goal**: Import Cubase and Studio One files, preview, keep in the library, assign to a track

**Independent Test**: Import a Cubase map and the Studio One list for the same library (manual, your own files; the smoke test uses inline maps): the same keyswitches from both; a damaged or unrelated file is refused with a message; the source file is unchanged; the map lands on the selected track

### Done and verified

- [x] T024 [P] [US1] Cubase importer per contracts/import-formats.md in `src/managed/Nota.Application/Articulations/ExpressionMapImporter.cs`
- [x] T025 [P] [US1] Studio One keyswitch-list importer in `src/managed/Nota.Application/Articulations/KeySwitchListImporter.cs`
- [x] T026 [US1] Import messages through `Loc` (`articulations.import.*` keys in `src/managed/Nota.Application/Localization/en.json`); saved names language-neutral (`#N`)
- [x] T027 [US1] Importer tests on the real fixtures and hostile input in `tests/Nota.SmokeTest/ArticulationImportTests.cs` — *now on small inline maps in both formats (the real-file checks were dropped with the fixtures)*

### To do

- [x] T028 [US1] Create `src/managed/Nota.Application/Articulations/ArticulationImport.cs`: pick the importer by content (`Sniff`), never by extension; `.notamap` through T021; a `.soundvariation` or unknown file returns an error message saying the format is not supported (FR-005), never a guess
- [x] T029 [P] [US1] Library port `IArticulationLibrary` in `src/managed/Nota.Application/Articulations/IArticulationLibrary.cs` and store `src/managed/Nota.Infrastructure/Articulations/ArticulationLibraryStore.cs` over `NotaPaths.SubDir("articulations")` (`*.notamap`, list / get / save / delete by map id, atomic write)
- [x] T030 [US1] Import preview dialog in `src/managed/Nota.App/ArticulationImportPreview.cs`: articulations (name, short name, colour swatch, actions as text), messages grouped by severity with unverified items labelled (contracts/import-formats.md), Accept / Cancel; DESIGN.md tokens, `Loc` keys — *verified by the owner in the running app, 2026-11-09*
- [x] T031 [US1] Articulations section in `src/managed/Nota.App/TrackInspectorView.cs` for instrument tracks: assigned map name and count, Import…, Assign from library, Remove; edits call `Edited()` — *verified by the owner in the running app, 2026-11-09*
- [x] T032 [US1] File picker for `.expressionmap`, `.keyswitch`, `.notamap` and All files from the inspector section, through T028 → T030 → T029 + T019 — *verified by the owner in the running app, 2026-11-09*
- [x] T033 [US1] Drop from Windows Explorer onto the inspector Articulations section (`BrowserView.ExternalPaths` pattern, `DragDrop.AddDropHandler`): files read at drop, decided by content, same preview as the picker — *verified by the owner in the running app, 2026-11-09*
- [x] T034 [P] [US1] Tests in `tests/Nota.SmokeTest/ArticulationImportTests.cs`: T028 dispatch by content (renamed files, `.soundvariation` refused), T029 store in a scratch `NOTA_DATA_DIR` (save, list, get, delete), import → compile → `SetTrackArticulationMap` → `GetTrackArticulationMapJson` round trip
- [x] T035 [US1] Run quickstart scenario H by hand (drop from Explorer onto the inspector); record the result in quickstart.md

**Checkpoint**: A real map can be imported and assigned without leaving Nota

---

## Phase 4: User Story 2 - Give notes articulations (Priority: P1)

**Goal**: Per-note articulation from the piano roll; playback sends the library its MIDI before each note

**Independent Test**: Chord with Staccato and Legato; the test instrument receives each keyswitch before its note, once; undo restores; save/reopen keeps it

- [x] T036 [US2] Add `ArtEv { off, seq, kind, channel, data1, data2 }` to `src/native/nota.engine/src/engine/EngineTypes.h` and a per-track buffer in `RenderScratch`, pre-sized in `publishRaw` like `blockMidi` (AR-6: "on overflow events are dropped and counted, never allocated") — *done differently: `ArtEv` and the scheduler live in `src/native/nota.engine/src/engine/ArticulationSchedule.{h,cpp}`; the block's events go in a fixed stack array in `renderInstrumentRaw` (articulation MIDI never crosses tracks, so no `RenderScratch` buffer is needed); overflow dropped and counted. Verified by T041.*
- [x] T037 [US2] In `src/native/nota.engine/src/engine/Engine_Render.cpp` emit, from each active clip's timeline, the actions whose trigger time falls in the block, in action order (`seq`), into the `ArtEv` buffer, outside the MIDI-FX chain (research R2) — *verified by T041*
- [x] T038 [US2] Merge `ArtEv` into `playEvents` in `Engine_Render.cpp`: same offset → articulation actions before note-ons; set `eventChannel` per event; keyswitch → `sendNoteOn`/`sendNoteOff`, CC and bank → `deliverExpr` path, program → `programChange`; both the segmented and the timed-events paths — *verified by T041 (check 20: actions ahead of the note at the same offset, on their channel, channel reset); gated by the new `Instrument::takesArticulations()` (built-ins: no; hosted plug-ins and racks holding one: yes)*
- [x] T039 [US2] Keyswitch release in `Engine_Render.cpp`: latching released after a named 20 ms hold constant, momentary at its note's end; bank MSB before LSB before program (FR-022) — *verified by T041 (checks 3, 6, 15, 16)*
- [x] T040 [US2] Chase in `Engine_Render.cpp`: on seek, loop wrap and start (`chaseNotes_`) emit the articulation in force at the block start (timeline binary search) at offset 0 before chased notes; never twice for one change; release held keyswitches on stop and seek (FR-021) — *verified by T041 (checks 11–14); stop/seek release relies on the existing `allNotesOff` flush*
- [x] T041 [US2] Engine self-test `Engine::articulationSelfTest` with a recording `Probe : Instrument` (segmented and timed) in `Engine_Render.cpp`, exposed as `nota_engine_articulation_selftest` (`nota_engine.h`, ABI, `NotaEngine.ArticulationSelfTest()`): order, chord with two articulations, overlapping notes, dedupe of repeated latching notes, seek into a phrase, loop wrap, stop with no held keyswitch, overflow counting — *21 checks, passing; `tests/Nota.SmokeTest/ArticulationEngineTests.cs` calls it*
- [x] T042 [US2] Session slots: same timeline and `ArtEv` path in `renderSessionSlotRaw` (`Engine_Render.cpp`), looping over `it = i0..i0+1` (research R7) — *verified: `scheduleSlotArticulations` (self-test checks 23–25: a switch per pass, one-shot slots, an early switch reaching back across the loop end)*
- [x] T043 [US2] Articulation submenu in the piano-roll note context menu in `src/managed/Nota.App/PianoRollView.cs` (`NoteGrid`; locate its right-click path first): the assigned map's enabled articulations + Default + Clear, applied to the selection with one `Commit`; hidden when the track has no map — *verified by the owner in the running app, 2026-11-09 (after the `NotaNote.Same` fix)*
- [x] T044 [US2] Missing articulation (FR-014): a note whose id is not in the map shows a marker and a tooltip in `PianoRollView.cs`; it plays as the default (engine T013) — *written: a warning notch at the start of a note whose articulation the map no longer holds, and a tooltip; map cached for 500 ms; seen working by the owner in the app, 2026-11-09*
- [x] T045 [P] [US2] Tests in `tests/Nota.SmokeTest/ArticulationEngineTests.cs`: the self-test passes; offline render with a map plays keyswitches on a test track; editing operations keep `ArticulationId` (copy/paste, Alt/Shift duplicate, quantize, transpose, velocity edit, clip duplicate, split, loop) through the managed editing APIs found in T011 — *verified: duplicate, split, trim, copy/paste, consolidate, Session slot and slot→arrangement keep each note's articulation; the roll's multi-clip split/merge and resize too; the clip tools (earlier); `NotaNote.Same` covers every field*
- [x] T046 [US2] Manual quickstart G with Kontakt + CSS on the owner's machine; record the result in `specs/085-articulation-maps/quickstart.md`; not claimed working until done — *done 2026-11-09: CSS in Kontakt switches articulation per phrase*

**Checkpoint**: Notes switch articulations in playback

---

## Phase 5: User Story 3 - Compensate sample-library latency (Priority: P1)

**Goal**: Track offset + articulation offset move the note; lead time moves only the trigger

**Independent Test**: Track −100 ms + Legato −150 ms → note 250 ms early at 60 and 120 BPM; keyswitch 30 ms before it; notes unmoved

- [x] T047 [US3] Apply the track offset to arrangement note-on and note-off in `gatherInstrumentNotes` (`Engine_Render.cpp`), milliseconds × sample rate, notes unmoved (verified: ±100 ms = 4800 samples at 48 kHz)
- [x] T048 [US3] Add the articulation offset in the same place ("effective = nominal + trackOffsetMs + articulationOffsetMs", applied once) and schedule actions at "trigger = effective − leadMs" (FR-018–FR-020) in `Engine_Render.cpp` — *verified: −100 + −150 ms = 250 ms early at 120 and 60 BPM (12000 samples); lead verified by T041 check 5*
- [x] T049 [US3] Track and articulation offsets for Session slots in `renderSessionSlotRaw` (`Engine_Render.cpp`) — *verified: a slot's note moves 4800 samples for ±100 ms; with no offset the slot's note loop is unchanged (beat-based block test kept)*
- [ ] T050 [US3] Negative offsets at a loop start: lookahead across the loop seam in `Engine_Render.cpp`, or, if it can't be done allocation-free, keep the documented limit (research R4) and test that nothing is dropped or doubled — *deferred by the owner, 2026-11-09: the limit stays as documented in research R4 (a note at a loop start with a negative offset sounds late on each repeat)*
- [x] T051 [US3] Make the track offset one undo step (`cloneTrack` + `republishWithTrack`) in `Engine_Recording.cpp`; keep the atomic read on the audio thread — *verified: one undo step, redo restores, the same value adds none*
- [x] T052 [US3] Track MIDI offset field in the inspector (`src/managed/Nota.App/TrackInspectorView.cs`): typed milliseconds via `ValueText`, display culture (U+2212 minus), range "±2000 ms" — *written: "MIDI offset" box in the inspector's Articulations section (types −80, -80 ms, −0.08 s; Enter / leave sets, Esc reverts); seen working by the owner in the app, 2026-11-09*
- [x] T053 [US3] Effective-offset readout per articulation (track + articulation) in `src/managed/Nota.App/TrackInspectorView.cs` and `src/managed/Nota.App/ArticulationMapManager.cs` (FR-018) — *written for the inspector (rule tested: `ArticulationText.EffectiveOffsets`) and the manager: a line under Note offset, "Plays at −250 ms (−100 ms track + −150 ms articulation)", using the selected track's offset when it holds the map, else the one its tracks share, else "adds to each track's offset" (rule tested: `ArticulationMapUsage.TrackOffsetFor`); both seen working by the owner in the app, 2026-11-09*
- [x] T054 [P] [US3] Tests in `tests/Nota.SmokeTest/ArticulationEngineTests.cs`: −100 + −150 = −250 ms at 120 and 60 BPM within one block (SC-003); lead 30 ms before its note (SC-004); offset undo/redo; Session slot offset — *verified: SC-003 at 120 and 60 BPM, lead (self-test 5), offset undo/redo, Session slot offset*

**Checkpoint**: Attacks land on the grid with compensation

---

## Phase 6: User Story 4 - See articulations on the notes (Priority: P2)

**Goal**: Note Color Mode (the Articulation Lane was dropped at the owner's request, FR-016)

**Independent Test**: Articulation Color recolours notes and keeps the selection visible

- [x] T055 [P] [US4] Note Color Mode (Track Color / Articulation Color) in the piano-roll settings (`LaneSettings`) with persistence, in `src/managed/Nota.App/PianoRollView.cs` — *written: a palette chip in the clip editor header beside the speaker (`ClipEditorView.NoteColourControl`), app-wide like audition, saved as `Settings.PianoRollNoteColorByArticulation` (off by default); every open roll redraws on a flip; seen working by the owner in the app, 2026-11-09*
- [x] T056 [US4] Pure colour resolver `src/managed/Nota.Application/Articulations/ArticulationColours.cs`: `ColorArgb`, else a default per articulation from palette tokens (no hex outside `NotaPalette.Ink()`) — *written and tested: `Shown` (own articulation, else the default it plays as; a missing or disabled id keeps the track colour, FR-014) and `PaletteSlot` (source index, else id; stable across reordering); the view reads the slot's track-palette colour at render time, so it follows the theme; seen working by the owner, 2026-11-09*
- [x] T057 [US4] Use T056 in the normal branch of `NoteGrid.Render` (`_o.ColorOf(i)`) in `PianoRollView.cs`; selected, ghost, locked and out-of-scale branches unchanged — *written: `ColorOf` returns the articulation colour when the mode is on, so notes, velocity stems and expression pens agree; the selected / ghost / locked branches and the out-of-scale fade are untouched; seen working by the owner in the app, 2026-11-09*
- ~~T058 [US4] Articulation Lane~~ — *removed at the owner's request (FR-016 dropped)*
- [x] T059 [P] [US4] Tests: T056 resolver (pure) in `tests/Nota.SmokeTest/ArticulationUiTests.cs`; the design and localization checks pass — *verified: 8 checks (own, default, missing, disabled, no default / no map, palette slot, stable across reorder); full smoke 5401 / 0*
- [x] T060 [US4] Manual quickstart I (colours, selection visible, switching back to track colours); record the result in `specs/085-articulation-maps/quickstart.md` — *done 2026-11-09: the owner reports colours, selection and switching back all work*

---

## Phase 7: User Story 5 - Manage maps (Priority: P2)

**Goal**: The Articulation Map Manager

**Independent Test**: Duplicate a map, edit it, assign to one track; a track on the original is unaffected; export → import is equal

- [x] T061 [US5] Manager window `src/managed/Nota.App/ArticulationMapManager.cs`: maps from the library and the project; create, import, rename, duplicate, delete; articulation list add / remove / reorder; edit names, colour, group, trigger mode, channel, actions, offset, lead, enabled, default; export `.notamap`; DESIGN.md, `Loc` — *written: three columns (maps · articulations · details), edits on a working copy, unsaved changes asked about on switching map and closing, Save / Revert / Export… / Assign to track; design, label and localization checks pass; seen working by the owner in the app, 2026-11-09. Groups can be chosen from the map's own groups, not created or renamed*
- [x] T062 [US5] Applying an edit updates every track using that map id in one undo group (`BeginUndoGroup`/`EndUndoGroup`) and the manager says how many (FR-010), in `src/managed/Nota.App/ArticulationMapManager.cs` — *written: Save → `lib.Save` + `ArticulationMapUsage.ApplyEverywhere`, status "Saved · N tracks updated" (the rule is tested, T066); seen working by the owner in the app, 2026-11-09*
- [x] T063 [US5] Drop from Explorer onto the manager in `src/managed/Nota.App/ArticulationMapManager.cs` (same path as T033) — *written: the window takes the first dropped file, decides by content, previews, adds to the library; seen working by the owner in the app, 2026-11-09*
- [ ] T064 [US5] If feasible in `src/managed/Nota.App/ArrangementView.cs`: drop a map file onto an instrument track header to import and assign; otherwise record why in research.md — *deferred by the owner, 2026-11-09: maps are dropped on the inspector section or the manager*
- [x] T065 [US5] Open the manager from the inspector section (`src/managed/Nota.App/TrackInspectorView.cs`) and the app menu (`src/managed/Nota.App/MainWindow.*.cs`); shortcut via `nota-shortcuts` if one is added — *written: "Edit maps…" in the inspector, View → "Articulation maps…" and the command palette (`view.articulationMaps`, bound — palette check passes); no shortcut added, so the Shortcuts pane is unchanged; seen working by the owner in the app, 2026-11-09*
- [x] T066 [P] [US5] Tests in `tests/Nota.SmokeTest/ArticulationEngineTests.cs` and `tests/Nota.SmokeTest/ArticulationFileTests.cs`: duplicate independence, edit reaches all tracks with that map in one undo step, export → import equality, deleting an articulation keeps notes' ids (data-model "the id is not reused") — *verified in `ArticulationManagerTests.cs`: duplicate independence, edit on every track as one undo, removed ids retired across save, notes keep a removed articulation's id after Save, and write → read → write is stable for the Nota, Cubase and Studio One maps (what the manager's unsaved-changes test relies on)*

---

## Phase 8: User Story 6 - Offset presets and measurement (Priority: P3)

**Goal**: Manual, user-saved Library Preset, and Automatic Measurement only if verified

**Independent Test**: Apply a saved preset and override one value; measurement returns a known test delay within tolerance, or the UI states the blocker

- [ ] T067 [P] [US6] Offset presets: user-saved sets of offsets per map in `src/managed/Nota.Application/Articulations/OffsetPreset.cs` and the T029 store; apply with per-articulation overrides; no built-in values (research R10) — *deferred by the owner, 2026-11-09 (US6)*
- [ ] T068 [US6] Measurement spike: offline render a track's instrument with an injected probe note (freeze/offline path) and estimate onset with the `TempoDetect` novelty envelope; record whether a hosted plug-in renders in that path, in research.md R10 — *deferred by the owner, 2026-11-09 (US6)*
- [ ] T069 [US6] Only if T068 verifies it: measurement with confidence, accept / reject, in `src/managed/Nota.App/ArticulationMapManager.cs`; otherwise the Automatic option is disabled with a tooltip naming the blocker (FR-026) — *deferred by the owner, 2026-11-09 (US6): no Automatic option is shown*
- [ ] T070 [P] [US6] Tests in `tests/Nota.SmokeTest/ArticulationEngineTests.cs`: preset apply and override; measurement on a built-in instrument with a known synthetic delay (if T069 implemented) — *deferred by the owner, 2026-11-09 (US6)*

---

## Phase 9: Polish & Cross-Cutting Concerns

- [x] T071 [P] Run `/speckit-converge` on `specs/085-articulation-maps/` over the code written before the plan (Constitution VII deviation): T004–T010, T024–T027, T047 — *done 2026-11-09: 27 FRs, 8 SCs, 21 acceptance scenarios, 10 edge cases, 12 plan decisions and 9 constitution principles checked; 7 findings appended as Phase 10 (T090–T096), none critical or high; one reported item (English literals in ProjectService warnings) dropped as the layer's existing convention*
- [ ] T072 Perf check (SC-007): `scripts/perf-gate.py` / `tests/perf/nota_perf.cpp` with and without a mapped track — *local check 2026-11-09, not the gate: scripts/perf-gate.sh needs Linux + Valgrind + git worktrees, none possible here (Windows; owner rule: no worktrees). Offline render, 16 hosted-probe tracks, a note every 32nd with the articulation changing on every note (worst case), fastest of 7 interleaved rounds: 536 ms without a map, 628 ms with (1.79 % → 2.09 % of real time, ~2 µs per track per 512-sample block); tracks without a map add only a null check per note. Run the gate on Linux before release*
- [x] T073 Run quickstart.md scenarios A–F and record the results — *done 2026-11-09: A, B, C pass; D, E, F pass with one claim each not directly tested — recorded in quickstart.md Results*
- [x] T074 Review every new line on the audio thread in `Engine_Render.cpp` for allocation, locks, I/O, logging (Constitution I) — *done 2026-11-09: no allocation, lock, I/O or logging on the new audio-thread code; two fixes — PluginInstrument's MidiBuffers reserve room for a block's articulation MIDI (kMaxBlockArt × 9 bytes on top of the notes' 8192), and a static_assert that Track::midiOffsetMs_ is lock-free. Seen, not changed: the notes' own worst case (1024 notes + 1024 expression events) already exceeded the 8192 bytes before this feature*

### Nota closing tasks *(the PR gates)*

- [x] T075 Run `pwsh -NoProfile -File .\scripts\build-win.ps1` from a VS x64 developer shell until native + managed + smoke test + plugin scan check are green — *done 2026-11-09: native, scanner self-test, managed, smoke test and plugin scan check green (5671 pass, 0 fail). The first run failed one check, the render-pool timing guard ("render threads do 0 % of the work"), known to fail the same way on unchanged develop; the rerun passed*
- [x] T076 [P] AGPL-3.0-only SPDX two-line header on every new source file (`src/managed/Nota.Application/Articulations/*.cs`, `src/native/nota.engine/src/engine/ArticulationTable.*`, `tests/Nota.SmokeTest/Articulation*.cs`, new `src/managed/Nota.App/*` files) — *done 2026-11-09: all 24 new files carry the two-line AGPL-3.0-only header*
- [x] T077 [P] New dependency: none (XML is the .NET base library) → N/A — *done 2026-11-09: no package, FetchContent or find_package added; CMake gains only the two new source files*
- [x] T078 [P] `CHANGELOG.md` `[Unreleased]` entry for articulation maps and the track MIDI offset — *done 2026-11-09: Added — articulation maps, the map manager, track MIDI offset, notes coloured by articulation*
- [ ] T079 [P] FEATURES.md via `nota-features`; manual pages via `nota-docs` including the cases that cannot be played early (FR-023); shortcuts via `nota-shortcuts` if any — *FEATURES.md done 2026-11-09 (Articulation maps bullet, Track inspector bullet); no shortcut added, so nota-shortcuts has nothing to do. Manual pages blocked: the docs repo (../nota-docs) is not on this machine — the FR-023 limit (a note at a loop start with a negative offset sounds late on each repeat, T050) must go into the manual there*
- [x] T080 Engine behaviour change → smoke test extended in `tests/Nota.SmokeTest/` (T010, T023, T041, T045, T054); confirm all pass — *done 2026-11-09: full smoke test with the rebuilt engine 5401 pass / 0 fail, 153 articulation checks (T010, T023, T041 self-test 25 checks, T045, T054, T059, T066)*

---

## Dependencies & Execution Order

- **Setup (Phase 1)**: done
- **Foundational (Phase 2)**: T011–T023 block every story. Order: T012 → T013 → T014 → T015 → T016 → T017 → T019 → T022; T018, T020, T021 in parallel; T023 last
- **US1 (P1)**: after Phase 2. T028 → T030 → T031 → T032 / T033; T029 parallel
- **US2 (P1)**: after Phase 2. T036 → T037 → T038 → T039 → T040 → T041; T042 after T038; UI T043–T044 parallel to the engine work once T019 exists
- **US3 (P1)**: T048 needs T037 (actions) and T013 (offsets in the table); T049 needs T042
- **US4, US5 (P2)**: after US1 (a map must exist to show); independent of each other
- **US6 (P3)**: after US5 (manager hosts it)
- **Polish**: after the stories in scope

### Parallel opportunities

- Phase 2: T018 (plug-in host), T020 (compiler), T021 (file format) in different files
- US1: T029 (store) alongside T028/T030
- US2: T043/T044 (piano roll) alongside T036–T041 (engine)
- US4: T055 and T056 together

## Parallel Example: Phase 2

```text
Task: "T018 eventChannel + programChange in Instrument.h and PluginInstrument.cpp"
Task: "T020 ArticulationMapCompiler.cs"
Task: "T021 ArticulationMapFile.cs"
```

## Implementation Strategy

### MVP

Phase 2 + US1 + US2 + US3 (all P1): import a real map, articulate notes, hear them switch in time. Stop and validate with quickstart G on Kontakt before P2 work.

### Incremental delivery

1. Phase 2 → a map on a track through the ABI, saved and undoable
2. US1 → import and assign from the UI
3. US2 → per-note articulations play
4. US3 → compensation
5. US4 / US5 → readability and editing
6. US6 → presets; measurement only if verified

## Notes

- Nothing is committed automatically (owner's rule); no branch, no push
- `.soundvariation` stays unsupported until a real file is supplied; then extend contracts/import-formats.md first
- A task is checked only when its test passes, not when it compiles

## Phase 10: Convergence

*Appended by `/speckit-converge` (T071), 2026-11-09. IDs start at T090: the intro's "T089 reviews it" is a stale reference (the review is T071), so T081–T089 are left unused to keep it unambiguous.*

- [ ] T090 Send bank select before program change within each articulation, whatever order its actions are listed in, per FR-022 (partial) — `ArticulationTable::build` keeps list order and `sendActions` sends it as is
- [ ] T091 Say in the Track inspector when the track's instrument takes no articulation MIDI (a built-in, or a rack without a plug-in), so the skipped actions aren't silent, per spec Edge Cases "instrument ignores channels, programs or banks" (missing) — the engine gates on `Instrument::takesArticulations()` but nothing reaches the UI
- [ ] T092 Count articulation events dropped on a full block buffer during playback, per plan "on overflow events are dropped and counted" (partial) — the render call sites don't pass `dropped`
- [x] T093 Make the chase re-send the articulation whose switch fired last in time (the latest trigger before the landing point), not the last by beat, so a seek gives the same state as straight playback when per-articulation offsets or leads reorder the triggers, per FR-021 (partial) — *done with the PR #549 review fixes: the scheduler decides latching switches by trigger time across the whole track, and the chase takes the last switch before the landing point (self-test 11, 25, 27)*
- [ ] T094 Let the manager create, rename and remove articulation groups, or record that groups come only from imported files, per FR-001 / FR-009 (partial)
- [ ] T095 Record the `Instrument::takesArticulations()` gate (built-ins and racks without a plug-in get no articulation MIDI at all) as a decision in plan.md / research R3, per FR-024 (unrequested — undocumented design)
- [ ] T096 Bring `contracts/c-abi.md` up to date (the self-test returns 0 on pass, else the failed check's number; drop "not yet built"), and fix the intro's stale "T089" reference to T071, per plan: contracts (contradicts — stale artifact)

## PR #549 review fixes

*2026-11-09: the CI perf gate and the automated review (Greptile, 7 comments) on PR #549. The user authorized separate fix commits for the PR; verification is recorded in `.specify/bugs/pr549-review/test.md`.*

- [ ] R1 Perf gate: remove the no-map rendering and publication overhead — *revision `96733e0e` passed Linux/Valgrind CI, run `42617643537`. Revision `8abb3ad1` regressed `edit-warp-split` by +4.59% instructions (run `42622338564`): graph clones allocated a fresh Session player with release storage before replacing it. Clones now reuse the existing player during construction; a fresh Linux instruction gate is required. The no-map note specialization and cheap timeline checks remain in place.*
- [x] R2 A chase in a block shorter than the 20 ms hold dropped its keyswitch release, leaving the key held — *the arrangement retains its chase origin; Session retains actual keys and remaining rendered frames so tempo changes cannot skip the release (self-tests 26, 34–37)*
- [x] R3 Session slot, short loop with a large offset: notes and switches more than one pass away were lost — *pass ranges follow shifted starts, note ends, leads and holds with no arbitrary pass cap (self-tests 29, 30, 33; C ABI smoke: +1500 ms spans 3 passes of a 500 ms loop and 48 passes of a 31.25 ms loop)*
- [x] R4 Session slot launch dropped a switch whose lead falls before the launch — *the first block after a launch (also legato) chases what is in force (self-tests 25, 35)*
- [x] R5 Session slot offsets moved notes but not their recorded expression — *notes and expression share the same shifted times and pass enumeration; a timed-instrument probe verifies opening pressure for positive and negative combined track/articulation offsets (self-test 32)*
- [x] R6 Manager: a library copy with the same id hid a different project copy; Save could overwrite restored settings — *the catalog lists the project's copy, flagged "differs from your library"; Save writes it to the library (smoke test)*
- [x] R7 Overlapping clips: one clip's latching switches were thinned without regard to another clip — *timelines keep every latching point; whether it sends is decided at playback across all clips (self-test 27)*
- [x] R8 Overlapping momentary notes sharing a keyswitch: the first to end released the key the other still held — *a key is released only when no other momentary note holds it, including different articulations in separate clips (self-tests 28, 31)*
- [x] R9 Session tempo changes during a latching switch's 20 ms hold could skip the release — *fixed-capacity pending keys count rendered frames for both natural and chased switches; due releases have priority, map removal still drains them, and launch/stop clears flushed keys (self-tests 36, 37)*
- [x] R10 Duplicate readable `.notamap` files with the same ID made the manager's catalog throw — *the lookup keeps the first copy per ID and continues to prefer project maps; manager regressions cover project-backed and library-only catalogs*
- [x] R11 Muted/solo-gated Session tracks retained obsolete queued releases after flushing voices — *discard pending releases on the silent-track path; native check 38 reproduces the old failure and verifies unmuting into a momentary hold of the same key*
