# Implementation Plan: Aurora, both wavetables on one card, and custom wavetables from samples

**Branch**: `feat/aurora-one-card` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/053-aurora-one-card/spec.md` (committed at 41bd30d3,
with the Clarifications session of 2026-10-29)

## Summary

The Osc tab of Nota Aurora drops its "1 / 2" switch. Both oscillators are drawn side by side,
each with its own table window, type selector, warp-mode selector and six knobs. The card
stays 700 × 260 (the open FR-005 question; § Layout at 1050 lists what would change). Each
oscillator can also load a custom wavetable made from an audio file:

- a standard wavetable file keeps its frames, up to 256 × 2048;
- a single cycle becomes one frame;
- a longer recording becomes 16 even, zero-crossing-aligned 2048-sample windows (no pitch
  detection).

The engine holds a custom table as an immutable, band-limited **table set** with mip levels.
It is built off the audio thread and reaches the audio thread in one of two ways:

- **Undoable edits** (import, remove) publish a cloned instrument through the existing
  `republishWithTrack` path, so they get undo for free (AR-5).
- **In-place loads** (`setState` from a preset or a project) swap it through the engine's
  existing `SnapshotHistory` epoch retirement (AR-6).

The audio thread only ever reads a raw pointer. It never allocates, never frees and never
takes a lock.

Persistence:

- **Engine state blob**: the table rides as a trailer section after the 156 parameters, so a
  blob without a custom table stays byte-identical to today's. The trailer keeps racks,
  clones and copy/paste self-contained.
- **Top-level project tracks**: tables are also written as content-addressed
  `samples/<hash>.wav` files, so identical tables are stored once. The project format goes
  from 27 to 28.
- **User presets**: carry the table as an embedded payload.

The two table windows read a cached preview per table, cache their geometry, draw at most
17 frames, and repaint only when the lit frame changes. "Lit" means the played position,
since live follow comes from a new Aurora scope. UI cost is measured per thread with
`pidstat` on the 036 reference machine, plus an opt-in paint-time probe.

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill (DSP class, kind registration across C++ and C#, device-card UI).
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine: `WavetableSynth`, the new table builder, C ABI); C# / .NET 10
(interop, persistence, presets, Avalonia card).

**Primary Dependencies**: Existing only.

- dr_libs (WAV / FLAC / MP3 decoding, `src/media/AudioFile.cpp:29-73`, `LICENSES/third-party.md:19`)
  through `decodeAudioFile`.
- signalsmith-linear `RealFFT`, already used by `dsp/primitives/Convolution.h:14` and
  `SpectrumAnalyzer.h:18`, for band-limiting and periodic resampling.
- Avalonia 12.

There is **no new dependency** and no `LICENSES/third-party.md` change.

**Storage**:

- `.nota` bundle, two parts:
  - `project.json`: `InstrumentDto.Wavetables`, new in format v28.
  - `samples/<hash>.wav`: content-addressed, the `BundleContent` pattern (`BundleContent.cs:4-9`).
- `plugin-states/<hash>.bin` for the instrument state blob (unchanged mechanism, `ProjectService.cs:150-153`).
- User preset JSON: `PresetDocument.AuroraTables`, new.

**Audio-Thread Impact** *(required)*:

- **New per-block reads.** The table set is read once per block through `SnapshotHistory::live()`,
  plus two relaxed atomic source flags. Imported oscillators use a mip-level pick and reads from
  a new size-masked morph reader.
- **Built-in path untouched.** Built-in oscillators keep the exact current code path
  (`oscStack` → `readMorph`, `WavetableSynth.cpp:681-703`), so factory sound stays bit-identical
  (036 null gate).
- **Scope writes.** One scope write per block: a few relaxed atomic floats.
- **Nothing heavy on the audio thread.** No allocation, lock, I/O or free there. Table builds run
  on a background thread; swaps happen on the message thread.
- **Retirement.** Old instruments are retired by the existing snapshot mechanisms
  (`Engine.cpp:671-688`, `SnapshotHistory.h:4-14`).

**Testing**: `tests/Nota.SmokeTest`.

- New `AuroraTableTests.cs` (ABI-level: conditioning, persistence, undo, missing, presets, racks, scope).
- The existing `AuroraNullTests.cs` gate (bit-identical factory renders).
- `tests/perf/nota_perf.cpp`: existing `aurora-supersaw-2x` plus a new `aurora-imported-2x` scenario.
- Manual UI performance runs with `pidstat` and the paint probe (quickstart § 4).

**Target Platform**: macOS, Windows, Linux x86-64. Performance acceptance is on the 036 reference
machine (i7-12700, Linux, `specs/036-aurora-perf/baseline-env.md:6-13`).

**Project Type**: Desktop app (native engine + managed UI).

**Performance Goals**:

- UI thread at most +2 points of one core over today's card, and the card at most 5 % of a core (SC-003).
- Both windows paint in at most 2 ms at p99 (SC-004).
- The card shows within 100 ms of switching tracks, with no UI frame over 50 ms (SC-005).
- `aurora-supersaw-2x` within ±10 % of about 28 ms/s; imported tables cost at most 10 % more than built-in (SC-006).
- A table build takes at most 1 s for a 10 s file (SC-002).

**Constraints**:

- A table is at most 256 frames × 2048 samples (2 MB as float32 at the base level; about 4 MB
  with its mips).
- Reading stops at 60 s of audio.
- The state blob layout is append-only.
- No new automatable parameter (spec Assumptions).
- The card is 260 high and 700 wide by default.

**Scale/Scope**:

- 2 table slots per Aurora; any number of Auroras, including inside Instrument Rack chains and
  Drum Rack pads.
- About 12 C ABI functions.
- One card tab rebuilt.
- Persistence in 3 places: blob, project, preset.

## Constitution Check

*GATE: checked before Phase 0 and again after Phase 1 design. Result: PASS, no violations.*

| Principle | How this plan complies |
|---|---|
| I. Real-time audio safety | The table set is built on a worker thread (`nota_aurora_table_build`, which touches no engine state, like `nota_audio_import`, `nota_engine.h:613-631`). It is handed over either by an instrument clone plus graph republish (AR-5), or through `SnapshotHistory` epoch retirement for in-place `setState` (AR-6). The audio thread reads `live()` once per block. Frees always happen on the message thread. The source flags are relaxed atomics. |
| II. Boundary discipline | All additions are `extern "C"` in `nota_engine.h` with an opaque `NotaAuroraTable*` handle, plain structs, UTF-8 and integer results ([contracts/c-abi.md](contracts/c-abi.md)). No JUCE. |
| III. Licensing hygiene | New files carry the two-line SPDX header. No new dependency (dr_libs and signalsmith-linear are already listed). |
| IV. Verification gate | Smoke-test coverage is listed per story in tasks.md. The 036 Aurora null gate must stay green. |
| V. One logical change per PR | One PR for the feature, built in reviewable commits by phase. If review needs it, US1 (layout only) can ship first as its own PR (tasks.md § Strategy). |
| VI. Docs in sync | `CHANGELOG.md` `[Unreleased]`, FEATURES.md (nota-features), the manual EN + RU (nota-docs), DESIGN.md Aurora note. |
| VII. Spec first | Spec committed (41bd30d3). One clarification is still open (FR-005, card width). The plan takes the 700 default and isolates the layout so a switch to 1050 touches only the card ([research.md](research.md) R10). |
| VIII. Safety rules not weakened | The design never mutates the table in place on the audio thread. It does not use a lock "just for the swap", and it does not decode on the audio thread. |

**Post-design re-check (after Phase 1)**: PASS. The contracts add no C++ types to the ABI. The
blob trailer keeps old blobs byte-identical. The only non-POD state crossing threads is the
immutable table set behind `SnapshotHistory`.

## Design overview

### Engine (`src/native/nota.engine`)

1. **`AuroraTable` (new, `src/devices/instruments/AuroraTable.h/.cpp`).** An immutable
   table: `frames` (1–256), base level 2048 × frames, mip levels of length 2048 >> k
   (k = 0…5, at most 1024 >> k harmonics each), the display name, the origin
   (wavetable file / single cycle / recording) and notes (`truncatedLength`,
   `truncatedFrames`). Also the pure build functions:
   - `buildFromFile(path)`: decode via `decodeAudioFile` → mono mix → rules (a) / (b) / (c)
     → condition;
   - `buildFromFrames(const float*, frames, name)`: the raw path used by project reload,
     with no re-conditioning, so a reload is bit-identical;
   - the conditioning steps: DC removal per frame, a 32-sample loop-seam blend for
     recording windows, per-table peak normalisation to 1.0 (the built-in level,
     `WavetableSynth.cpp:739-741`), refusal below −60 dBFS, and mip generation by FFT
     truncation.
   - A content hash of the base level, used for dedup inside a blob and for UI caching.
   See [data-model.md](data-model.md) § AuroraTable.
2. **`WavetableSynth` changes.**
   - A `SnapshotHistory<const AuroraTableSet>` member holds `{ shared_ptr<const AuroraTable>
     slot[2]; std::string missing[2]; }`, plus `std::atomic<uint8_t> useCustom_[2]`.
   - `render()` opens a `RenderScope` and reads `live()` once per block next to the existing
     block snapshot (`WavetableSynth.cpp:330-345`). For an oscillator with `useCustom` and a
     table present, it calls a new `oscStackCustom` with the mip level picked from the
     voice's highest phase increment (unison detune and a warp-mode bias included). Otherwise
     it calls the unchanged `oscStack` (`:497-500`).
   - `clone()` (`:246-252`) shares the table set and copies the flags.
   - `getState()` / `setState()` (`:227-244`) add the trailer section
     ([contracts/state-blob.md](contracts/state-blob.md)). `setState` builds the set on the
     calling (message) thread and publishes it through the history, so a live preset load or
     a rack restore is safe.
   - `scopeRead()` (the `Instrument.h:155-159` hook, Keys pattern `Keys.h:199-215`) publishes
     the played position of each oscillator, from the most recently started sounding voice
     including modulation, plus the two table serials.
3. **`Engine` (`Engine_Devices.cpp`)**, next to the Rhythm sample path
   (`Engine_Devices.cpp:328-343`):
   - `auroraSetTable(track, osc, table)` and `auroraClearTable(track, osc)`: clone →
     modify → `republishWithTrack` → one undo step (`Engine.cpp:685-688`, undo at
     `:694-714`). Sounding notes stop, as with a Mosaic program change
     (`nota_engine.h:705-709`).
   - `auroraSetSource(track, osc, useCustom)`: in place, an atomic store, notes continue.
   - `auroraSetMissing(track, osc, name)`: message-thread metadata, published through the
     history.
   - Read-only queries for table info, the full frames and a preview.
   - Rack-chain variants mirror `nota_rack_set_chain_sampler_sample` (`nota_engine.h:1801`).
4. **C ABI**: [contracts/c-abi.md](contracts/c-abi.md), implemented in
   `src/abi/nota_engine_track.cpp`, with the builder in `src/abi/nota_engine_import.cpp`
   (next to `nota_file_decode_mono`, `:103`).

### Managed

- **Interop**: `NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs`, `IAudioEngine.cs`, and the
  rack proxy engine used for in-rack cards (`RackCardView.cs:211`).
- **Application service `AuroraTableImporter`** (`src/managed/Nota.Application/Instruments/Aurora/`):
  checks the extension against `SampleIndex.AudioExtensions` (`SampleIndex.cs:20-21`), runs
  the build on a `Task`, marshals the result to the UI thread, applies it with
  `AuroraSetTable` and reports a typed result (ok / truncated / unreadable / silent).
- **Project** (`ProjectService.cs`):
  - Save, for kind 5 with any table: the blob is written **without** tables
    (`nota_track_aurora_state(include_tables = 0)`). Each table is written as
    `samples/<hash>.wav` (float32 mono, 2048 × N, itself a standard wavetable file) and
    listed in `InstrumentDto.Wavetables`.
  - Load: replay the blob, then `AuroraTableFromFrames` per entry. A missing file → a
    warning plus `AuroraSetMissing` (FR-016).
  - `CurrentFormatVersion` 27 → 28 (`ProjectService.cs:40`), with a no-op migration
    appended (`ProjectMigrations.cs:25-50`).
  - Pruning: `PinnedBinaries` / `PruneUnreferenced` learn the new references
    (`ProjectHistory.cs:4-18`).
  - Aurora instances nested in racks stay self-contained in the rack blob (rack Sampler
    precedent, `RackCore.cpp:664-674`, `:801-814`).
- **Presets** (`PresetModel.cs`, `PresetService.cs`): `AuroraTables` (osc, name, origin,
  frames, base64 float32 frames), written at save and applied after the parameters on load.
  This follows the `MosaicProgram` pattern (`PresetService.cs:62`, `:313-324`). A preset
  without the field clears both custom tables (FR-015).

### UI (`src/managed/Nota.App`)

- **`AuroraTableView`** (new, replaces `AuroraStack` on the Osc tab): one per oscillator.
  - Built-in tables use the existing per-process shape cache (`AuroraStack.cs:47-49, 86-115`).
  - Imported tables use a preview read once per table serial (at most 16 representative
    frames plus up to 64 for the lit frame, 96 points each).
  - Geometries are cached per (source, size), so a repaint is at most 17 `DrawGeometry`
    calls (today it rebuilds 16 geometries on every paint, `AuroraStack.cs:138-152`).
  - `Set(source, litPos, dim)` invalidates only when the lit index or the source changes.
  - It acts as the drop target, with a hover highlight (Rhythm pattern,
    `RhythmInstrumentCard.cs:220-235`), and shows the loading, error and missing states.
- **Osc tab layout at 700**: [contracts/ui-osc-tab.md](contracts/ui-osc-tab.md). There are
  two 216 px columns:
  - header row: name, on switch, type drop-down, warp drop-down;
  - a 216 × 60 window;
  - a row of six 34 px knobs.
  The shared sub / unison row spans below. The `osel` pick, `oscPick`, `applyOsc` and the
  shared strip (`AuroraInstrumentCard.cs:423-442, 480-487`) are removed.
- **Live tick**: `Refresh` reads the 5-float Aurora scope on every tick before the
  `ParamsMoved` gate (`AuroraInstrumentCard.cs:147-176`). It updates only the windows when
  only the played position moved.

### Measurement (SC-002 to SC-006)

- **UI CPU**: a smoke-test helper `--aurora-ui-fixture <dir> [--builtin]` writes two fixture
  projects:
  - the scenario: 4 Auroras, both oscillators on generated 256-frame tables, LFO 1 → Osc 1
    position and LFO 2 → Osc 2 position at 2 Hz, a 4-bar chord loop;
  - the baseline: the same with built-in tables and format v27, so today's build can open it.
  `pidstat -t -p <pid> 1 60` gives the UI thread (TID = PID) average. See
  [quickstart.md](quickstart.md) § 4.
- **Paint and card-build time**: an opt-in probe (`NOTA_UI_PROBE=aurora`). Stopwatch ticks go
  into a fixed ring in `AuroraTableView.Render`, and the time from track selection to the
  card's first render is measured in `DeviceChainView`. It logs p50 / p99 every 10 s to
  stderr and costs nothing when off.
- **Audio**: `nota_perf aurora-supersaw-2x` and the new `aurora-imported-2x`; `--aurora-null`
  for bit-identity.
- Results go into `specs/053-aurora-one-card/perf-baseline.md`, in the same table format as
  `036-aurora-perf/baseline-env.md`.

### Layout at 1050 (if FR-005 is answered "Wide")

Only `AuroraInstrumentCard.cs` and `AuroraTableView` sizes change:

- `CardWidth` becomes 1050;
- the centre panel grows to about 788 px, so each column is about 391 px;
- the type and warp selectors go back to inline segment chips (4 + 5 cells, as today);
- each window becomes about 150 × 86, beside its knob grid;
- the knobs grow to 36 with full labels.

The engine, ABI, persistence, measurement and tasks for US2–US4 do not change. T019, T020 and
T024 change their numbers, and DESIGN.md's Aurora note moves from L to Wide.

## Project Structure

### Documentation (this feature)

```text
specs/053-aurora-one-card/
├── spec.md
├── plan.md              # this file
├── research.md          # Phase 0 decisions R1–R12
├── data-model.md        # AuroraTable, table set, blob trailer, DTOs, scope layout
├── quickstart.md        # validation runs, incl. the UI CPU measurement
├── contracts/
│   ├── c-abi.md         # new nota_engine.h functions
│   ├── state-blob.md    # WavetableSynth state trailer v1
│   ├── persistence.md   # project v28 + preset fields
│   └── ui-osc-tab.md    # Osc tab layout and states at 700 (and 1050 delta)
├── checklists/requirements.md
├── perf-baseline.md     # written during implementation (T005, T045)
└── tasks.md
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                       # + Aurora table ABI (contracts/c-abi.md)
├── CMakeLists.txt                                   # + AuroraTable.cpp (list at :171)
└── src/
    ├── devices/instruments/AuroraTable.h/.cpp       # NEW: build, condition, mips, hash
    ├── devices/instruments/WavetableSynth.h/.cpp    # table set, custom reader, state trailer, scope
    ├── engine/Engine.h, Engine_Devices.cpp          # auroraSetTable / Clear / Source / Missing / queries
    ├── devices/racks/RackCore.cpp                   # (no change: child getState carries the trailer)
    └── abi/nota_engine_track.cpp, nota_engine_import.cpp, nota_engine_rack.cpp
src/managed/
├── Nota.Application/Engine/IAudioEngine.cs          # + Aurora table members
├── Nota.Application/Instruments/Aurora/AuroraTableImporter.cs   # NEW
├── Nota.Infrastructure/Interop/NativeMethods.Tracks.cs, NotaEngine.Tracks.cs
├── Nota.Infrastructure/Persistence/ProjectModel.cs, ProjectService.cs, ProjectMigrations.cs,
│                                   PresetModel.cs, PresetService.cs, BundleContent.cs
└── Nota.App/
    ├── DeviceCards/Instruments/AuroraInstrumentCard.cs   # Osc tab rebuilt
    ├── Controls/AuroraTableView.cs                       # NEW (AuroraStack's successor)
    ├── Controls/AuroraStack.cs                           # shape cache kept, view removed
    ├── DeviceCards/Rack/RackCardView.cs                  # proxy engine forwards the new calls
    └── DeviceChainView.Instruments.cs                    # opt-in probe hook
tests/
├── Nota.SmokeTest/AuroraTableTests.cs                    # NEW
├── Nota.SmokeTest/AuroraNullTests.cs                     # unchanged gate, re-run
├── Nota.SmokeTest/Fixtures/                              # generated test tables (written at run time)
└── perf/nota_perf.cpp                                    # + aurora-imported-2x
```

**Structure Decision**: The change goes into the existing engine instrument, the C ABI, the
Infrastructure persistence and the App card. The one new engine file pair isolates table
building (pure, any-thread) from the DSP class. The one new managed service keeps
file / threading logic out of the card.

## Complexity Tracking

No constitution violations. Two choices add weight on purpose:

| Choice | Why needed | Simpler alternative rejected because |
|---|---|---|
| Tables stored twice by shape (self-contained blob trailer **and** bundle `samples/` for top-level tracks) | FR-014 asks for storage once across Auroras and project versions, and racks, clones and copy/paste need self-contained state | Blob only: no dedup across instances (2–4 MB per copy). `samples/` only: rack chains and device copy/paste would lose the table |
| Two hand-over paths (republish for edits, `SnapshotHistory` for in-place `setState`) | Undo comes from graph snapshots (AR-5). `setState` is called in place on the live instrument (`Engine_Devices.cpp:1644-1650`) | Republish only: in-place preset or state loads would race. History only: no undo without a custom undo entry |
