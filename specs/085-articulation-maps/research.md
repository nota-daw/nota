# Research: Articulation maps

Phase 0 of `/speckit-plan`. Each decision names what was verified in the repository and what
was not. "Unverified" means no code or file in this checkout proves it.

## R1. Where articulation data lives in the engine

- **Decision**: The map is engine state on the **instrument track**, as two things published
  together with the graph snapshot: (a) an opaque JSON blob (what the project saves and undo
  restores) and (b) a compiled, immutable `ArticulationTable` the audio thread reads. A note
  carries only an integer articulation id.
- **Rationale**: The engine owns undo (`pushUndo`, graph snapshots through `republishWithTrack`).
  If the map lived only in managed code, undo would restore notes but not the map. Putting it in
  the track makes assign, edit, undo, redo, duplicate-track and save all follow the existing path
  (AR-5: immutable snapshot published, the audio thread never sees a half-built table).
- **Alternatives**: Managed-only catalog with the engine holding a copy (rejected: two sources of
  truth, undo drift). Per-note inline actions (rejected: bloats notes, loses "edit the map once").
- **Verified**: `Track` fields are copied in `cloneTrack` (`Engine.cpp` ~675) and the duplicate
  path (`Engine_Tracks.cpp` ~3672); clip controllers are already shared immutable `shared_ptr`s
  (`MidiClip.h`), the same pattern.

## R2. How articulation MIDI reaches the instrument

- **Decision**: Articulation actions are **not** put in the note stream. The scheduler builds a
  separate, sorted `ArtEv` list per block and `playEvents` merges it with notes and expression.
  `ArtEv` = `{off, seq, kind, channel, data1, data2}`; equal offsets keep `seq` (action order).
- **Rationale**: MIDI effects (`applyMidiEffects`, e.g. the arpeggiator) transform the note
  stream after `gatherInstrumentNotes`. A keyswitch placed in it would be arpeggiated or
  filtered. Keyswitches must bypass MIDI FX. `std::sort(midiEvLess)` is not stable, so actions
  that must stay in file order need their own sequence key.
- **Alternatives**: Prepend keyswitch notes to the note list (rejected: MIDI FX, sort order).
  Send from the message thread (rejected: breaks sample accuracy and AR-4/AR-6).
- **Verified**: `computeInstrumentMidi` sorts, runs the DrumRack swing and MIDI FX, then
  `playEvents` delivers; `playEvents` already merges `MidiEv` and `ExprEv` by offset and has a
  timed-events path for hosted plug-ins.

## R3. Channel, program change and bank select on the `Instrument` interface

- **Recipient gate (implemented)**: `Instrument::takesArticulations()` defaults to false;
  hosted plug-ins return true, as do racks containing one. Built-in instruments therefore
  receive no articulation actions. This avoids treating a library's keyswitch as a musical
  note on a built-in synth. Note timing offsets still apply. The user guide documents the gate.
- **Decision**: Add `int32_t eventChannel = 0` beside `eventOffset` (0 = the note's own / channel
  1) and `virtual void programChange(int32_t program)` (default: ignore). Bank select goes
  through the existing `controller()` path with CC 0 and CC 32. `PluginInstrument` uses
  `eventChannel + 1` instead of its hardcoded `1`, and maps `programChange` to
  `juce::MidiMessage::programChange`. Built-ins ignore both.
- **Rationale**: `eventOffset` already shows how to hand per-event data to an instrument without
  changing every `noteOn` signature across built-in instruments.
- **Verified**: `PluginInstrument.cpp` hardcodes channel 1 in `noteOn`, `noteOff`, `controller`,
  `allNotesOff`; `controller()` accepts ctl 0..127 as a CC with the value quantised through
  `u7(v) = lround(v*127)`, so a CC value N is sent as `N/127.0f` and round-trips exactly.
- **Unverified**: that Kontakt reacts to program change or bank select. CSS needs neither (its
  files use keyswitch notes and CC 58, seen in the owner's files).
- **Implemented (T018)**: `PluginInstrument` sends every engine-driven event on `chan()` (the
  articulation's channel, else 1) and remembers which channels it used, so `allNotesOff`
  flushes channel 1 plus only those (a plug-in never driven off channel 1 still gets exactly one
  message). Its `MidiBuffer` is pre-sized to 8 KiB, so the extra messages don't allocate.
  `RackInstrument` forwards the channel with controllers and program changes to every chain.
- **Limit (racks)**: a keyswitch *note* sent to an Instrument Rack goes through the rack's note
  fan-out like any note: chain key/velocity zones and chain MIDI effects apply, and it reaches
  the chain on channel 1. A plug-in hosted directly on the track has neither limit. Documented
  for the user manual (T079).

## R4. Offsets and the lead time (the scheduling rule)

- **Decision**: `effective = nominal + trackOffsetMs + articulationOffsetMs`;
  `trigger = effective - leadMs`. Milliseconds convert to samples with the device sample rate
  (`ms * sr / 1000`), not through samples-per-beat. A note-on is emitted in the block that contains
  its **effective** time; the actions in the block that contains the **trigger** time. Note-off
  uses the same shift as note-on (length preserved).
- **Rationale**: The engine decides what plays by comparing sample positions with the block window
  (`gatherInstrumentNotes`). Shifting the sample position is the smallest change that preserves
  every existing rule (clip active, clip end, seams, chase).
- **Consequences (documented limits, spec FR-023)**:
  - Playback that starts inside `[effective, nominal)` of a note: its note-on is already past.
    The existing chase rule re-triggers it late only if the note is still held (`offS > blockStart`);
    a shorter note is not played. This is the "cannot be scheduled early" case.
  - A positive offset can push a note past a loop end; the transport wraps first, so it is not played
    in that pass.
  - A negative offset on a note at the loop start cannot sound before the wrap in the first
    version. Lookahead across the loop seam is a later task (see Plan, Phase C).
- **Verified**: the first slice (track offset applied to `onS`/`offS` in `gatherInstrumentNotes`)
  is already written; it is not yet built or tested.

## R5. Keyswitch state without runtime state

- **Decision**: The scheduler is stateless per block. At publish time (message thread) each clip
  with a map gets a derived, immutable **articulation timeline**: notes sorted by start, with
  consecutive latching notes of the same articulation collapsed to one change point; momentary
  articulations stay one entry per note. At run time a block emits the change points whose trigger
  time falls in the block; on a seek, loop wrap or start (`chaseNotes_`) it also emits the
  articulation in force at the block start, found by binary search, at offset 0.
- **Rationale**: Dedupe ("no duplicate triggers") and "restore state after a seek" both need to
  know the previous articulation. Scanning all notes per block for it is O(n²) in the worst case;
  keeping state on the audio thread breaks on seeks. A precomputed timeline makes both exact and
  allocation-free.
- **Choke point (T012, traced 2026-11-09)**: `Engine::publishRaw` (`Engine.cpp`). Every graph the
  audio thread can see passes through it: `publish` → `publishRaw`; `republishWithTrack` →
  `republishWithTrackRaw` → `publishRaw` (84 + 23 callers); 30 direct callers including undo/redo
  (`restored = true`). The only other `graphHist_.publish` is the constructor's empty graph.
  All 37 writes to `notes` / `midiClips` in `src/engine/*.cpp` (note set, recording incl. session
  takes and pickup shifts, split, trim, slice, consolidate, move between tracks, duplicate, take
  retry) are made on a fresh `cloneTrack` copy or on a new local clip pushed into one, before that
  copy is published. No code edits notes on a published track in place.
- **Where the timeline is rebuilt**: in `publishRaw`, beside the existing `settleTakeJoins` pass,
  which already does the same thing for derived take joins: for each track of the new graph that
  is **not** shared with the current `authoring_` graph (a fresh clone the audio thread has never
  seen), rebuild the timeline of every MIDI clip and Session slot clip from its notes and the
  track's table; with no table, clear them (a clone copies its source's timelines). Tracks shared
  with the current graph are untouched. Restored tracks (undo, redo) were published before with a
  timeline that matches their notes and table, so they need nothing. A track without a map costs
  nothing.
- **Cost**: O(n log n) per clip of an edited track that has a map, on the message thread; a
  live piano-roll drag (`setClipNotesLive`) pays it per frame. To be measured in T072.
- **Alternatives**: Re-send the keyswitch before every note (rejected: breaks legato scripts and
  FR-021 "no duplicate triggers"). Hold "last sent" in the instrument (rejected: wrong after a seek).

## R6. Latching and momentary keyswitch release

- **Decision**: A latching keyswitch note is released after a fixed 20 ms hold (a named constant);
  a momentary one is released when its note ends. Both are scheduled by time, statelessly, like
  note-offs. Controller, program and bank actions have no release.
- **Rationale**: Cubase sends a keyswitch note-on and a note-off right away for its "direct"
  articulations. 20 ms is long enough for any script to see it and short enough to avoid
  overlapping the next trigger.
- **Unverified**: the exact hold some Kontakt scripts want. Exposed as a constant; if a library
  needs more, make it a per-articulation field.

## R7. Session clips

- **Decision**: Session slots get the same offset and articulation path, implemented after the
  Arrangement one by sharing one helper. `renderSessionSlotRaw` already scans the current and next
  loop iteration, so a shifted note near the slot end naturally lands in the next iteration.
- **Verified**: `renderSessionSlotRaw` iterates `it = i0..i0+1` and tests `onAbs` against `[p, p+db)`.

## R8. Persistence

- **Decision**: `TrackDto.ArticulationMap` (the map as Nota's file format, spec in
  `contracts/nota-articulation-map-file.md`), `TrackDto.MidiOffsetMs` and `NoteDto.Articulation`
  (the last two already written), format version 30 with a no-op migration. A user library folder
  `NotaPaths.SubDir("articulations")` holds the same JSON for reuse across projects.
- **Verified**: `NotaPaths.SubDir`, the `PresetStore` pattern and `[JsonExtensionData]` exist.
  Unknown properties survive a load/save cycle, so older builds that read a v30 file lose nothing
  they did not understand (they refuse files with a higher version, as today).

## R9. Importers (what is and is not known)

| Format | Evidence | Parsed | Not stored in the file |
|---|---|---|---|
| Cubase `.expressionmap` | Real file: Art Conductor "CSSS Violins 1 A" (45 slots) | names, short names, descriptions, order, output events `{status,data1,data2}` (144 note, 176 CC), palette colour index, group number | switch mode, RGB colour, group names |
| Studio One `.keyswitch` | Real file: "CSSS Violins 1 Attribute" (+ Lite Ensemble); entries cross-checked against the Cubase twin | names, ids, keyswitch notes (`noteP.V`), original activation string | colours, groups, channels, the CC that CSS uses (`unknown`) |
| Studio One `.soundvariation` | **No file seen** | **nothing: not implemented** | n/a |

- Cross-check result (from the real files): all 44 non-empty entries have the same keyswitch notes
  in both formats; `noteP.V` is velocity V and `noteP` is velocity 127; `unknown` is the CC 58
  action the keyswitch format cannot hold.
- Not verified because no file shows it: program change and bank select in Cubase maps, non-zero
  groups, the slot-level channel field when it is not -1, momentary vs latching.
- The importers use `System.Xml` (base class library), DTD processing prohibited, size bounded.

## R10. Library presets and automatic measurement

- **Library Preset mode**: Nota will **not** ship recommended offsets it cannot source. Presets are
  user-saved sets of offsets that can be applied and overridden. Values for a named library are
  added only when the owner supplies them or measurement produces them.
- **Automatic Measurement**: plausible, **unverified**. The engine can bounce a track's audio offline
  (`freeze` begin/end) and has offline rendering. A measurement would need an ABI to inject a
  probe keyswitch and note into the track's instrument during an offline render, then an
  onset detector (`TempoDetect` has an onset-novelty envelope to reuse). Whether a hosted
  Kontakt instance renders correctly in that offline path is unknown and needs the owner's
  machine. Scheduled last (Phase F); the report must say "not verified" unless it is.

## R11. UI integration points (located; behaviour not yet read)

- Piano-roll note colour: `NoteGrid.Render` in `PianoRollView.cs` uses `_o.ColorOf(i)` for the
  normal branch; selected, ghost and locked branches are separate. Hook point for Articulation Color.
- Note context menu: a right-click path on `NoteGrid` was **not located**; the first UI task finds it.
  `MenuFlyout` + `MenuKit.Item` is the pattern (`ShowResetMenu`, controller picker).
- Inspector: new "Articulations" section in `TrackInspectorView.cs`, gated by `IsInstrument`.
- Drop: `BrowserView.ExternalPaths` and the Mosaic SFZ drop (`HookDrop`) are the patterns.
- **Note rebuilds (T011, audited 2026-11-09).** Searched `src/managed` for `new NotaNote(`,
  target-typed `new(x.Pitch…)`, initializers and `default(NotaNote)`, and `src/native` for `Note`
  built from fields:

  | Site | What it does | Verdict |
  |---|---|---|
  | `Nota.Mcp/Tools/MidiTools.cs` `ToEngine`/`FromEngine` | MCP `get_clip_notes` → agent edits → `set_clip_notes` | **Was lossy; fixed**: optional `Articulation` on the MCP `Note` record, mapped both ways |
  | `Nota.Mcp/Tools/SessionTools.cs` `ToEngine`/`FromEngine` | same for Session slots | **Was lossy; fixed** the same way |
  | `Nota.App/PianoRollView.cs:2409` | a click draws a new note | new note: none is correct |
  | `Nota.App/DrumPatternView.cs:181` | step sequencer creates notes | new note: none is correct |
  | `Nota.App/MainWindow.Convert.cs` (4 sites) | audio → MIDI conversion | new notes: none is correct |
  | `Nota.Application/Midi/MidiFileReader.cs:102` | MIDI file import | new notes: none is correct |
  | `Nota.Application/Ports/IClipAi.cs:38` | AI-generated notes | new notes: none is correct |
  | `Nota.Application/Twin/TwinPatterns.cs:307` | pattern generator | new notes: none is correct |
  | `Engine_Render.cpp` 479/535 (`collectRecorded`) | recorded notes into a take | new notes: none is correct |
  | `Engine_Tracks.cpp` `noteFromData` | ABI → `Note` | carries the id (T004) |

  `MidiTransforms`, `NoteOverwrite` and `ClipSet` (named earlier as suspects) copy notes as values
  (`var c = n; c.StartBeat = …`) and never rebuild them; the struct copy keeps `ArticulationId`.
  Native split, trim, consolidate and duplicate copy `Note` by value. Behavioural proof that edits
  keep the id is T045.

## R12. Build and test environment

- **Resolved 2026-11-09**: the native CMake configure downloads CPM and its pinned dependencies
  (nlohmann_json, JUCE, miniaudio, signalsmith and others). No offline cache existed, so the owner
  allowed the download. They now live in `src/native/nota.engine/build/_deps` (git-ignored). The
  native engine builds with MSVC x64 + Ninja (324 steps, 0 errors), the scanner self-test passes,
  and the full smoke test passes.
- Managed code builds today without the engine (`Nota.Application` compiled). Engine-dependent
  smoke tests need the native library.
- The importer tests are pure managed code and can run as soon as the smoke-test project builds.
- Git Bash exists (`C:\Program Files\Git\bin\bash.exe`), so `.specify/scripts/bash/*` can run
  if needed; no script here needs a branch.
