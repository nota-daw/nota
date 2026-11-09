# Implementation Plan: Articulation maps

**Branch**: none (checked-out `develop`, local only) | **Date**: 2026-11-09 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/085-articulation-maps/spec.md`

## Summary

Add per-track articulation maps and per-note articulations. A map is engine state on the
instrument track (JSON blob for save and undo, compiled immutable table for the audio thread),
published with the graph snapshot (AR-5). The scheduler shifts note times by track offset plus
articulation offset, and emits articulation MIDI (keyswitch, controller, program, bank) from a
separate sorted event list, so MIDI effects cannot disturb it. A per-clip articulation timeline,
built when the clip is published, retains every latching point; playback resolves deduplication
across the track by trigger time. Arrangement scheduling uses immutable clips, the table, and
the block's last chase origin. Session latching releases retain emitted channel/key pairs and
remaining rendered frames in fixed audio-thread-owned storage, so tempo changes cannot skip a
20 ms release. The queue never grows and still drains when the map is removed.
Importers read the two real file formats seen (Cubase expression map,
Studio One keyswitch list). UI: map manager, inspector section, note menu, note colours,
drag and drop. Decisions and evidence: [research.md](research.md).

## Nota: Read Before Planning

Consulted: `ARCHITECTURE.md` AR rules (AR-4 queue, AR-5 snapshots, AR-6 audio-thread limits, AR-7
pure C ABI), `DESIGN.md` for the UI, and `CONTRIBUTING.md` for licensing. The `nota-features`,
`nota-docs` and `nota-shortcuts` skills apply at the end (FEATURES.md, manual, shortcuts).

## Technical Context

**Language/Version**: C++20 (engine, plug-in host), C# / .NET 10 (managed app, Avalonia 12).

**Primary Dependencies**: None new. XML parsing is `System.Xml` from the base library; JSON is
`System.Text.Json`. JUCE stays in `pluginhost/` (the `PluginInstrument` change).

**Storage**: Project file (`TrackDto.ArticulationMap`, `TrackDto.MidiOffsetMs`,
`NoteDto.Articulation`, format version 30) and a user library folder
`NotaPaths.SubDir("articulations")`.

**Audio-Thread Impact**: Yes, in the render path. The audio thread reads an immutable table and
per-clip timeline through the graph snapshot; the track offset is a relaxed atomic. No allocation,
lock or I/O: the per-block event list lives in the pre-sized scratch buffer (`RenderScratch`,
sized in `publishRaw`), capped like the existing 1024-event buffers; on overflow events are dropped
and counted, never allocated. The new timeline and table are built on the message thread.

**Testing**: `tests/Nota.SmokeTest`: pure managed tests (importers, file format, model), engine
tests through a recording `Probe : Instrument` exposed as `nota_engine_articulation_selftest`, and
project round trips. Real Kontakt playback is a manual owner check.

**Target Platform**: Windows x64 first (the build available); no platform-specific code, so Linux
and macOS are not expected to differ.

**Project Type**: Desktop app (native engine + Avalonia UI).

**Performance Goals**: No new dropouts on the existing perf test; scheduling cost O(notes in block
+ log articulations) per track per block.

**Constraints**: Offsets in milliseconds independent of tempo; ±2000 ms; no fake data (no invented
library offsets); the native engine cannot be built here until its dependencies are available
offline (research R12).

**Scale/Scope**: Maps of ~50 articulations per track; clips with thousands of notes.

## Constitution Check

*GATE: pass before research; re-checked after design.*

| Principle | Status |
|---|---|
| 0. Approved issue first | Pass: #547, labelled `approved`. (The work started locally before the issue was linked; that waiver is now closed.) |
| I. Real-time safety | Pass by design (table and timeline immutable and published with the snapshot; scratch and Session release queue fixed-size; pending keys and chase origins audio-thread-owned). Native scheduler/Session probes and audio-thread review cover the fixes. Revision `96733e0e` passed Linux instruction-count CI; follow-up commits require a new CI run. |
| II. Boundary discipline | Pass: only POD structs and `nota_*` C functions cross; no JUCE outside `pluginhost/`. |
| III. Licensing | Pass: no dependency. Every new file needs the SPDX header (done for the files written so far). |
| IV. Verification gate | Pending: smoke tests for importers exist; engine tests and the full `build-win.ps1` run need the native build (blocked, R12). |
| V. One logical change | The feature is large; delivery is split into the phases below, each independently buildable and testable. |
| VI. Docs in sync | Planned in the final phase: CHANGELOG, FEATURES.md, manual, shortcuts. |
| VII. Spec-first | **Deviation to correct**: code (note id, track offset, scheduler shift, model, importers, importer tests) was written before this plan. It is provisional; `/speckit-tasks` lists it as tasks to review and `/speckit-converge` must pass over it. |
| VIII. Rules not weakened | Pass. |

Post-design re-check: unchanged. The one new risk is the timeline going stale if a note edit skips the
publish step (research R5); Phase B's first task closes it.

## Project Structure

### Documentation (this feature)

```text
specs/085-articulation-maps/
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── c-abi.md
│   ├── nota-articulation-map-file.md
│   └── import-formats.md
├── checklists/requirements.md
└── tasks.md            # created by /speckit-tasks, not by this command
```

### Source Code (what this feature touches)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h         # note field, offset, map functions, self-test
├── src/abi/nota_engine_track.cpp      # ABI implementations
├── src/engine/MidiClip.h              # Note.articulation, clip timeline
├── src/engine/Track.h                 # offset, map blob, compiled table
├── src/engine/ArticulationTable.h/.cpp  # NEW: table, timeline builder, lookup
├── src/engine/Engine*.cpp             # set/clear map, cloneTrack, noteFrom/ToData, scheduler, self-test
├── src/devices/Instrument.h           # eventChannel, programChange
└── pluginhost/hosting/PluginInstrument.cpp   # channel and program change to real MIDI

src/managed/
├── Nota.Application/Articulations/    # model, importers (written), export, library port
├── Nota.Infrastructure/               # P/Invoke, project DTOs, migrations, library store
└── Nota.App/                          # manager, inspector section, note menu/colours, drop

tests/Nota.SmokeTest/                  # ArticulationImportTests (inline maps; no vendor files), engine tests
```

**Structure Decision**: Extend existing layers; no new project. The engine type lives in its own
file pair so `Engine_Render.cpp` (RT-hot) only gains the scheduling calls.

## Delivery phases

Each phase builds, passes its tests, and is usable alone. Tasks are generated from these.

- **Phase A: Foundations (partly written).** Note id and track offset through engine, ABI, C# and
  project; format 30. Importers and model; importer tests. Close R5's choke-point question.
  *Blocked on the native build for the engine half.*
- **Phase B: Scheduling.** Table and timeline; `ArtEv` stream; keyswitch hold; chase on seek, loop and
  start; `eventChannel` and `programChange` through `PluginInstrument`; engine self-test.
- **Phase C: Edge handling.** Negative offset near the loop seam (lookahead), Session slots, deleted or
  disabled articulations, overflow behaviour.
- **Phase D: Manager and assignment.** Library store, manager window, inspector section, offsets UI
  with effective-offset readout, export/import of Nota files, undo.
- **Phase E: Notes in the piano roll.** Context menu, note colours and Note Color Mode (no
  Articulation Lane: dropped at the owner's request); check every C# place that rebuilds notes by field.
- **Phase F: Drag and drop, presets, measurement.** Explorer drop onto manager and inspector (header
  if feasible); user-saved offset presets; measurement feasibility, implemented only if verified.
- **Phase G: Docs and verification.** CHANGELOG, FEATURES.md, manual, shortcuts, full build and smoke
  test, the owner's Kontakt checks, `/speckit-converge`.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Constitution VII (code before plan) | Early slices were written before the Spec Kit process was applied | Deleting working code adds risk; tasks mark it for review and converge covers it |
| Separate `ArtEv` stream in `playEvents` | Keyswitches must bypass MIDI FX and keep action order | Putting them in `MidiEv` lets the arpeggiator touch them and `std::sort` reorder them |
