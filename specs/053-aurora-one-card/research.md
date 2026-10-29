# Research: Aurora one card and custom wavetables

Phase 0 of [plan.md](plan.md). Each entry gives the decision, the reason and the
alternatives. Code references are to `ui/style-guide` at cc1531b6 plus the spec commit 41bd30d3.

## R1. How a custom table reaches the audio thread

**Decision**: An immutable `AuroraTableSet`, holding both slots plus their "missing" names,
is owned by `SnapshotHistory<const AuroraTableSet>` inside `WavetableSynth`. It is changed in
one of two ways:

- **Undoable edits** (import, remove) clone the instrument, publish the new set on the
  clone, and republish the track through `Engine::republishWithTrack`
  (`Engine.cpp:685-688`). Undo swaps the graph back (`Engine.cpp:694-714`), and the old
  instrument still holds the old set.
- **In-place changes**: `setState` from a live preset load, a rack restore or a project
  replay, and "missing" metadata. These build a new set on the message thread and
  `publish()` it into the history.

`SnapshotHistory` retires the old pointer with the render epoch and frees it only after
two later renders, on the message thread (`SnapshotHistory.h:4-14, 33-43`). `render()`
holds a `RenderScope` and reads `live()` once per block.

**Rationale**: AR-5 / AR-6. This is the same pattern Rhythm uses for voice samples
(`Engine_Devices.cpp:328-343`) and racks use for chains. `Engine::setPluginState` calls
`instrument->setState` in place on the live instrument (`Engine_Devices.cpp:1644-1650`), so a
plain `shared_ptr` member swap there would race the audio thread. The history removes that
race without a lock.

**Alternatives**:

- `std::atomic<std::shared_ptr>`: may lock on some standard libraries, and the last
  reference could drop on the audio thread.
- A lock-free command-queue message carrying a pointer (AR-4): still needs a retirement
  scheme, which the history already provides.
- Republish for every change: would make preset loads stop notes, and `setState` callers
  do not republish.

## R2. Undo granularity and held notes

**Decision**:

- Import and remove: one undo step each, via republish. Sounding notes stop, as with Mosaic's
  checkpointed program change (`nota_engine.h:705-709`).
- Switching an oscillator between its imported table and a built-in (FR-010): an in-place
  relaxed `std::atomic<uint8_t>` store, read per block. Notes keep sounding, and it is not an
  undo step.

**Rationale**: The spec allows notes to stop on import (Edge Cases). The spec does not
require undo for the source switch, and that switch is a performance gesture.

**Alternatives**:

- A hidden appended plugin parameter for the source: it would become automatable (no
  non-automatable flag exists in `Instrument.h`), which contradicts the spec Assumption.
- In-place import keeping notes: no graph undo entry, so FR-013 would need a bespoke undo
  type in `Engine`.

## R3. Table building and conditioning

**Decision**: These are pure functions in `AuroraTable.cpp`, safe on any thread. They touch
no engine state, like `nota_audio_import` (`nota_engine.h:613-631`).

1. Decode with `decodeAudioFile` (`AudioFile.cpp:73`). Mix to mono by channel average, as
   `nota_file_decode_mono` does (`nota_engine.h:718-721`). Keep only the first 60 s × the file
   rate, and set `truncatedLength`.
2. Classify the file:
   - **(a) Standard wavetable file.** A WAV `clm ` chunk gives the frame size; read it if
     present. Otherwise the length is a multiple of 2048 and at least 2 × 2048. If the
     declared frame size is not 2048, each frame is resampled to 2048 periodically by FFT.
     Keep at most 256 frames and set `truncatedFrames`.
   - **(b) Single cycle.** Length ≤ 4096 and not (a). Resample one period to 2048 by FFT
     (zero-pad or truncate the spectrum): exact for a periodic signal, no pitch detection.
   - **(c) Recording.** Otherwise, 16 windows of 2048 samples. Window *i* is centred at
     `i × (L − 2048) / 15`, then snapped to the nearest upward zero crossing within ±1024
     samples (or kept where it is if there is none). There is no resampling and no pitch
     detection (Clarification 1).
3. For each frame:
   - subtract the mean (DC);
   - for (c), blend the last 32 samples into the first 32 with an equal-power crossfade so
     the frame loops without a click. For (a) and (b) the stored cycle is kept as is,
     because those are already periodic.
4. Normalise per table to a peak of 1.0, the built-in tables' level
   (`WavetableSynth.cpp:739-741` normalises each built-in frame to 1.0). Relative frame
   levels are kept. Refuse the file if its peak before normalising is below −60 dBFS.
5. Build mips (R4) and compute the content hash (128-bit, over the base-level float bytes,
   matching `BundleContent`'s 128-bit SHA-256 prefix, `BundleContent.cs:4-9`).

**Rationale**: These rules follow the spec rules in FR-009 and the clarification. The
FFT already exists (signalsmith-linear). A file declaring frames with a `clm ` marker is the
common wavetable-file convention.

**Alternatives**: pitch-detected cycles were rejected by Clarification 1; linear-interpolation
resampling adds roll-off and aliasing.

## R4. Band-limiting (mip levels)

**Decision**: Six levels, k = 0…5. Level k has length `2048 >> k` and keeps harmonics
≤ `1024 >> k`, made by FFT truncation of each base frame. Memory is about 2 × base, so at
most about 4 MB per 256-frame table.

Per voice per block, pick the smallest k with
`(1024 >> k) × f_max ≤ 0.45 × sr`, where `f_max = f0 × max unison detune factor × warpBias`:

- warpBias is 1 for Off, PWM and Fold;
- for Sync it is `1 + 7 × warp`;
- for Bend it is `1 + 3 × warp`.

Clamp k to 5. `readMorphSized(t0, t1, ff, phase, len)` masks with `len − 1`.

Built-in tables are untouched: they keep at most 64 harmonics with no mips, read by
`readMorph` (`WavetableSynth.h:281-290`, `.cpp:717-743`), so FR-006 holds bit for bit.

**Rationale**: FR-011 asks for "no more aliasing than a built-in of similar brightness". An
imported frame may hold up to 1024 harmonics, so it must be filtered by pitch. Halving the
length per level keeps memory bounded and the reader simple.

**Alternatives**:

- Full-length 2048 tables at every level: 8–11 × memory (about 22 MB per table).
- Oversampling the oscillator: CPU cost on every voice, which would break SC-006.
- A crossfade between levels: doubles the reads. It is deferred, and a level change only
  happens when pitch changes (risk logged in plan, R12).

## R5. State blob layout

**Decision**: The parameters stay first (156 floats). An optional trailer, `"AWT1"`
section + `u32 size` + `"AWT1"`, goes at the very end
([contracts/state-blob.md](contracts/state-blob.md)). It holds the source flags, the missing
names and each slot's base frames (mips are rebuilt on load), and a "same as slot 0" flag
when both slots hold the same hash.

`setState` finds the trailer from the end, so future appended parameters still parse.
With no table, no source flag and no missing name, the blob is byte-identical to today's
(`WavetableSynth.cpp:227-234`).

**Rationale**:

- The existing reader takes `min(kNumParams, size/4)` floats (`:236-244`), so a trailer never
  disturbs the parameters.
- Racks embed child state blobs (`RackCore.cpp:661-663`), so tables survive racks with no
  rack change.
- Clones and device copy/paste go through state or `clone()`.

**Alternatives**: a separate side-channel ABI for racks, as the Sampler payload v3
(`RackCore.cpp:664-674`) does, means more rack format churn.

## R6. Project persistence and dedup

**Decision**:

- **Top-level Aurora tracks.** `ProjectService` saves the blob via
  `nota_track_aurora_state(include_tables = 0)`. Each present table is written as
  `samples/<hash>.wav`: float32 mono, 2048 × N, with a `clm ` marker, so it is itself a
  standard wavetable file. It is listed in `InstrumentDto.Wavetables` as
  `{ Osc, Sample, Name, Origin, Frames, UseCustom, Missing }`. Save registers it like other
  samples (`ProjectService.cs:1067-1080` `RegisterSample` analogue), which gives dedup
  across Auroras and across history versions (`ProjectHistory.cs:4-18`).
- **Load.** `SetPluginState` the blob, then decode each WAV raw (no re-conditioning:
  `nota_aurora_table_from_frames`), then `AuroraSetTable` without an undo checkpoint (the
  load path is not undoable). A missing or unreadable file becomes a warning in the existing
  style ("Couldn't reload…", `ProjectService.cs:582, 662`) plus `AuroraSetMissing`.
- **Format.** `CurrentFormatVersion` 27 → 28 (`ProjectService.cs:40`), with a no-op
  migration (an absent `Wavetables` means none; `ProjectMigrations.cs:25-50`).
- **Racks.** Auroras inside racks rely on the blob trailer: no cross-instance dedup there,
  but the rack blob itself is content-addressed.

**Rationale**: This meets FR-014 for the common case. Reload is bit-identical (SC-007)
because the stored frames are the conditioned frames and the reload path skips
conditioning. Float32 WAV round-trips exactly through dr_wav.

**Alternatives**:

- Store the source audio and re-import on load: not bit-stable across builder changes, and
  larger.
- Blob only: no dedup.

## R7. Presets

**Decision**: `PresetDocument.AuroraTables` holds a list of
`{ Osc, Name, Origin, Frames, UseCustom, Data }`, where `Data` is base64 of
little-endian float32 base frames. It is written by `PresetService` when an Aurora has a
table, and applied after the parameters on load (the `MosaicProgram` pattern,
`PresetService.cs:62, 313-324`; `PresetModel.cs:47-49`).

Loading a preset without the field clears both custom tables and sets the source to
built-in (FR-015). That is one combined undoable step, together with the preset's parameter
change.

**Rationale**: Presets are single JSON files that must travel between machines. A 16-frame
table is about 175 KB of base64; a 256-frame one is about 2.8 MB, which is acceptable for a
user preset.

**Alternatives**: a sidecar file next to the preset would break the one-file preset contract
and the preset library index.

## R8. Live follow telemetry (US4)

**Decision**: `WavetableSynth::scopeRead` returns 5 floats:

- `[0]`, `[1]`: the played position (0..1) of Osc 1 / Osc 2 for the most recently started
  sounding voice, including matrix, macro and MPE modulation, or −1 when no voice sounds;
- `[2]`, `[3]`: table serials (a change means the UI should refetch the preview);
- `[4]`: active voice count.

These are relaxed atomic floats written once per block (the Keys pattern, `Keys.h:199-215`,
`Keys.cpp:60`). They are read by the existing `nota_track_instrument_scope`
(`nota_engine.h:1081-1083`).

**Rationale**: No new channel is needed, and the cost is 5 stores per block.

**Alternatives**: a per-voice position array is more data for no visible gain, since one lit
frame is drawn.

## R9. UI drawing cost

**Decision**:

- `AuroraTableView` keeps the per-process built-in shape cache (`AuroraStack.cs:47-49, 86-115`).
- For imported tables it fetches a **preview** once per table serial: up to 16 representative
  frames plus a 64-frame lit grid, 96 points each, through `nota_track_aurora_table_preview`,
  cached per (track, osc, serial).
- It caches the `StreamGeometry` of each drawn frame per (source, bounds). Today they are
  rebuilt on every paint (`AuroraStack.cs:138-152`).
- A repaint is at most 17 `DrawGeometry` calls plus the window chrome.
- `Set` invalidates only when the quantised lit index (of the 64-grid), the source, the dim
  state or the bounds change.
- The live tick reads the scope (one P/Invoke of 5 floats) before the `ParamsMoved` gate
  (`AuroraInstrumentCard.cs:151-166`). Card readouts update only when parameters moved.

**Rationale**: FR-018 and FR-019. The cost is independent of frame count. In the worst case
(both positions moving every tick) that is 2 × 30 repaints/s of 17 cached geometries, well
under the 2 ms p99 budget.

**Alternatives**: rendering the stack to a bitmap per lit frame trades memory for little
gain, and bitmaps need re-rendering on theme and scale changes.

## R10. Osc tab layout at 700 (FR-005 still open)

**Decision**: The centre body is 438 × 166 px (`AuroraInstrumentCard.cs:51`, `DeviceGap` 5 at
`NotaGeometry.cs:70`). It is split into two 216 px columns with a 6 px gap. Each column has:

- a header row of 20: name, on switch, a type drop-down of about 96 px, a warp drop-down of
  about 64 px (drop-down 22 high, `DESIGN.md:521-537`);
- a 3 px gap;
- the window, 216 × 60;
- a 3 px gap;
- a knob row of 54: six 34 px knobs (`KnobSecondary`, `DESIGN.md:530`) in 36 px cells.

The shared sub / unison row (22) spans both columns. That gives 20 + 3 + 60 + 3 + 54 + 22 =
162 ≤ 166. The window caption uses the graph title row (`NotaGraph.PlotTop` 22,
`DESIGN.md:813-818`) on one line: "Formant · 5/16 · lfo 1 → pos".

Under 1050 (if chosen): columns of about 391 px, inline segment chips, windows of about
150 × 86 beside the knob grid ([plan.md](plan.md) § Layout at 1050).

**Rationale**: The default is the current card size, with no change to the device panel
height or width rules (`DESIGN.md:983-993`, 038 FR-025). The layout code is isolated so the
1050 answer is a sizing change.

**Alternatives**: hiding the rail on the Osc tab (the rail holds Global and the meter, which
the card always shows); a vertical stack of the two oscillators (the window would be under
40 px tall).

## R11. UI CPU measurement method

**Decision**:

- **Machine**: the 036 reference machine (i7-12700, Linux, performance governor,
  `036-aurora-perf/baseline-env.md:6-13`).
- **Fixtures**: generated by a smoke-test helper (`--aurora-ui-fixture`): a scenario fixture
  (v28) and a baseline fixture (v27, built-in tables, the same LFO routing) that today's
  build opens.
- **UI thread CPU**: `pidstat -t -p <pid> 1 60`, averaged for TID = PID (the Avalonia UI
  thread), which is the linux-idle-cpu method (`.specify/bugs/linux-idle-cpu/assessment.md:27`).
- **Paint and card-build times**: the opt-in `NOTA_UI_PROBE=aurora` probe (p50 / p99 over
  10 s windows).
- Each run happens twice; runs must agree within ±10 %, as in 036 SC-004.

**Rationale**: It is repeatable, uses the project's existing methods, and compares like
with like (the same project shape on old and new cards).

**Alternatives**: `dotnet-counters` (process-wide only, so the audio threads blur it).

## R12. Risks

| Risk | Mitigation |
|---|---|
| Held notes stop on import (republish) | Spec accepts it. The importer applies at once, and the card says "loaded" |
| Undo stack and retired graphs keep old tables alive (up to about 4 MB each) | Bounded by undo depth. Noted in the CHANGELOG. Re-check memory with 10 imports in quickstart § 3 |
| Mip pick under Sync / Bend warp still aliases at extreme settings | A warp bias in level selection. A quickstart listening check plus a smoke spectral check (energy above Nyquist folding ≤ −60 dB at C7 for a bright table, Warp Off) |
| A level jump is audible during pitch bends | Pick on the block's highest increment; add an adjacent-level crossfade only if listening shows it |
| 700 layout too tight (knob row exactly 216 px, drop-downs instead of chips) | FR-005 is still open. The 1050 delta is planned |
| Rack-hosted Aurora import goes through the proxy engine | Dedicated task, with rack-chain ABI variants |
| Preset JSON of about 2.8 MB for 256-frame tables | Acceptable. Tested for load time in quickstart § 3 |
| Built-in sound regression | The built-in branch stays untouched. The `--aurora-null` gate runs in the smoke test |

## R13. Changes made during implementation

Places where the plan proved wrong or too big, and the smaller choice taken instead.

- **Mip levels (R4)**. Halving the length at each level leaves level k with 2 samples per
  cycle of its top harmonic. The linear read then rolls the top octave off and folds part of
  it back (a C3 check measured −39 dB, against a −40 dB gate). The plan's clamp at k = 5
  also leaves 32 harmonics at C7, which alias. Now there are **ten levels**: level k keeps
  `1024 >> k` harmonics, its length is `clamp(4096 >> k, 256, 2048)` with level 0 at 2048,
  and that gives at least 4 samples per cycle. C3 measures −51 dB and the C7 alias check
  −73 dB. Memory is **3.5 × the base, about 7 MB per 256-frame table** (the plan said 4 MB).
  The CHANGELOG states it.
- **Mip pick per read, not per block**. Unison voices and Sync / Bend each step the
  phase at their own rate, so the level is picked per read, from that read's increment (with
  the warp bias). That costs one comparison loop, and it removes the R12 pitch-bend jump
  across a block. The `aurora-imported-2x` scenario costs +8 % over built-in tables,
  within SC-006 (perf-baseline.md).
- **Preview (R9)**. A plain resample of the base level drew bright frames as flat lines.
  The preview is now a **min / max outline** of the base level: `points / 2` slices, each
  emitting its min and max in the order they occur. Its shape matches what is heard at low
  pitch.
- **Drawing (R9)**. 17 cached geometries per paint still cost 6.4 / 10.6 ms (p50 / p99) in
  the container: SC-004 failed, so T046 applied. The view now renders the window chrome and
  the depth stack once into a `RenderTargetBitmap`, keyed by table serial, bank, size,
  inset, dim, render scale and ink colour, so a theme or scale change rebuilds it. Each
  paint draws only the lit frame on top. Built-in tables use the same path. Result: 0.43 /
  1.65 ms. This reverses the "Alternatives" note in R9.
- **Rack chains (T040, T041) deferred**. An Aurora inside a rack keeps its tables: they
  travel in the instrument state blob, and the smoke test checks a group / ungroup round
  trip. But the rack card cannot import or remove a table yet, so the card hides **Import…**
  inside a rack. The chain ABI variants and the proxy forwarding are left for a follow-up.
- **ABI**. One extra call, `nota_aurora_table_frames`, copies a table's base frames out, so
  the app can write them to the project bundle without decoding the file again.
- **Handles**. Managed code holds table handles as `nint`, with `try / finally` around
  `AuroraTableFree`, not a `SafeHandle`. Each handle lives for one call chain.
- **Hash**. The table hash is a 128-bit pair of FNV-style mixes over the base frames. It
  needs no new dependency, and it is only used to dedupe bundle files, not for security.
- **`clm ` chunk**. A declared frame size is accepted only as a power of two from 256 to
  4096. Any other value falls back to the 2048-multiple rule.
- **Measurement (R11)**. The runs were done in a 4-core container with a headless
  Avalonia harness, not with `pidstat` on the 036 machine. Each run was compared against
  the base card on the same container. SC-005 (100 ms) is missed **by the unchanged card
  too** there (about 204 ms). The feature is no slower (184 to 191 ms). The
  reference-machine run is still owed (perf-baseline.md).
- **UI fixture**. The baseline project is saved by this branch, then stamped
  `formatVersion: 27`, because nothing in it needs 28. That lets the base build open it.
- **Presets**. A preset saved with no tables (including Init) clears any imported table
  when applied in place, so applying a preset always gives the sound it was saved with.
