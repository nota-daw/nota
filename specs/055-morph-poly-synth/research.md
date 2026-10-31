# Research: Nota Twin (specs/055)

Phase 0 of the plan. Each entry: **Decision** · **Rationale** · **Alternatives considered**.

## R1 — Instrument kind and registration

- **Decision**: built-in instrument **kind 18**, display name "Nota Twin", registered at every
  site Nota Consort (kind 15) touches (engine factory, clone, `RackCore::makeInstrument`,
  `nota_engine_add_twin_track`, managed interop, `BuiltinDeviceCatalog`, `DeviceCategories`,
  `InstrumentCardFactory`, `PresetService` / `FactoryPresetCatalog`, `ProjectService`,
  `PresetAudition`, MCP `InstrumentTools`, `RemoteParams`, `RackKeyParams`, `NotaDocs`,
  `tests/perf/nota_perf.cpp`, smoke test).
- **Rationale**: 17 (Mosaic) is the highest kind in use; Consort is the newest full-stack
  instrument with a sequencer, ladders and a delay, so its footprint is the most complete
  checklist (`git grep -il consort` lists 38 files).
- **Alternatives**: extending Pentad or Consort — rejected: the A/B-side and part model changes
  their persisted layout and their identity as faithful single-architecture instruments.

## R2 — Parameter model for sides and parts

- **Decision**: every sound parameter exists as **one plugin-param per part per side**, with ids
  `<part>.<side>.<name>` (`lo.a.cut1`, `up.b.o2wave`). Side-less per-part params (`lo.morph`,
  matrix source / destination, voice mode, voice share) use `<part>.<name>`; global params
  (mode, split key, expression pad, sequencer settings) use a bare `<name>`. Param order =
  persisted layout, append-only, as on every built-in.
- **Rationale**: FR-024 wants automation on the *edited side*: a lane on `lo.b.cut1` **is** "Lower ·
  Cutoff · B", so automation, MIDI-learn, macros, CV links, presets ("modified" mark), clone
  and undo all work with no new mechanism. The card writes to whichever side is being edited.
- **Alternatives**: (a) one param per sound control + a hidden B blob — automation could only
  drive one side, contradicting FR-024; (b) exposing only the morphed value — not editable per
  side. Both rejected.
- **Size**: ≈ 120 sound params × 2 sides × 2 parts + 64 routes × 4 fields × 2 parts + globals ≈
  **1 050 params** (Volt 143, Aurora 156). Open risk **R2a**: the automation target picker,
  the Map tab and Nota Remote's param lists enumerate all params; they must filter Twin's to
  the current part / side or group them. Measured in US1's slice before US2 lands (task in
  tasks.md); if a list is slow, it gets a filter, not a different param model.
- **R2a measured (2026-10-30, T016)**: speed is not the problem — reading 965 ids and names
  over the C ABI takes well under a millisecond and Avalonia builds menus lazily. Usability is:
  the automation menu groups one level deep on the text before the last space, so Twin's
  names make ~250 top-level entries (one per island per part × side, plus a group per
  matrix slot), and Nota Remote's Macros screen pages every param by eight — 121 pages. The
  browser's Map tab lists mappings, not params, so it is unaffected. Fixes, both in US2
  (T092, T093): the automation menu nests on " · " first (Lower A › Ladder › Cutoff), the
  matrix slot params are renamed so they group as one "Routing" section per part / side, and
  Remote pages a curated set for Twin (Morph, the pad, the Lower part's side A sound controls)
  instead of all 965.

## R3 — Morph evaluation and per-voice morph

- **Decision**: at control rate (every 32 samples, as Volt's coefficient updates) each part
  computes `v = lerp(A, B, m)` per continuous param **in its musical domain** (linear in
  pitch for cutoff/tune, in log-time for envelope / LFO times, linear for levels and depths);
  stepped params take A for `m < 0.5`, B otherwise. Per-voice morph (matrix → Morph from a
  per-note source) re-evaluates only the ≈ 70 *voice-local* params for that voice; global ones
  (effects, LFOs in global mode) use the part's morph.
- **Rationale**: cheap (12 voices × 70 lerps / 32 samples), click-free (per-sample smoothing of
  the resulting target values already exists in every voice), and exact at the endpoints
  (SC-002: `lerp(A,B,0) == A` bit-exact).
- **As built (US2)**: the lerp runs on normalized values — a Pitch / LogTime param's normalized
  value is already exponential in its unit (cutoff = 20 Hz · 1000^v), so no per-domain math is
  needed. Voice mode, glide and octave are part params, not side params, so no morph position
  can switch the voice mode under held notes.
- **Alternatives**: per-sample morph of all params — 30× the cost for no audible gain;
  morph on normalized 0..1 values — makes cutoff and times sweep unevenly.

## R4 — Oscillators and Metal

- **Decision**: reuse the `pentad::` 4-point polyBLEP / polyBLAMP core (as Consort does). Wave
  = a continuous blend tri → saw → square; Metal = a wavefolder on the triangle path with
  first-order antiderivative anti-aliasing (ADAA) inside the voice's ×2 oversampling. Sync and
  cross-FM run inside the oversampled voice.
- **Rationale**: proven, aliasing-safe core; ADAA keeps folding clean without ×4.
- **Alternatives**: wavetable-based morph (Aurora) — rejected, loses the analog sync / FM
  behaviour; naive fold at ×4 — 2× more expensive.

## R5 — The multimode (Steiner-Parker-style) filter

- **Decision**: a new shared primitive `dsp/primitives/SteinerFilter.{h,cpp}`: a 12 dB/oct
  Sallen-Key-type ZDF core whose input is *distributed* between its LP / BP / HP injection
  points (as the original circuit does), so Mode sweeps continuously LP → BP → HP → Notch
  (Notch = LP + HP injection); diode-pair soft clipping in the feedback path gives the
  dirty resonance and self-oscillation; per-sample linearised saturation, like
  `LadderFilter`.
- **Rationale**: the injection topology is what makes the original's mode morph and its
  character; a ZDF form stays stable under audio-rate FM (FR-003).
- **Alternatives**: the shared `SvfFilter` with mode crossfading — clean, but lacks the
  character the spec asks for; kept as the fallback if the new model misses the CPU budget.

## R6 — The ladder filter

- **Decision**: reuse `dsp::LadderFilter` (4-pole ZDF, self-oscillating, bass compensation
  flag); the 12 dB slope is `process12()`, the same ladder with its output after the second pole.
- **Rationale**: the shared primitive is already tuned. *Found while implementing:* no instrument
  calls it today — Monolith, Consort and Pentad keep their own copies — so the tap cannot change
  their sound (confirmed by a render-hash comparison), and the separate slice-0 PR is dropped.

## R7 — Brute feedback, drive, drift

- **Decision**: Brute = the filter-section output fed back into the filter inputs through a
  one-sample delay and a tanh stage (bounded gain ≤ 1 at max, so it never runs away — Edge Cases).
  Drive = the shared soft-clip. Drift = Pentad's seeded per-voice spread scheme (re-seeded on
  play edges / jumps), so freezes and renders are identical (FR-012, SC-008).

## R8 — Built-in effects

- **Decision**: Twin owns, per part, an internal chain of existing effect devices — **Chorus /
  Flanger / Phaser** (one active, by the Mod-FX type param), **Delay**, **Reverb** — built at
  construction (allocation-free afterwards), like Nota Rhythm's voice FX. Twin's own A/B FX
  params are morphed (R3) and pushed onto the embedded devices' params at block rate.
- **Rationale**: reuse instead of new DSP; tempo sync comes from the Delay device's own sync
  through `setTransport` / `TransportInfo`.
- **Alternatives**: Aurora-style in-house FX — more code; exposing the devices as a user chain —
  their params couldn't morph.

## R9 — Modulation matrix

- **Decision**: **64 slots per part**, each four params `<part>.mN.src`, `<part>.mN.dst`
  (side-less, stepped, indices into append-only source / destination tables), `<part>.a.mN.amt`,
  `<part>.b.mN.amt` (bipolar, morphed). Per-voice sources (velocity, key, pressure, slide,
  bend, per-voice LFOs, envelopes, random-per-note) evaluate per voice; global sources once.
  Hard-wired panel routes (LFO 1 → pitch, filter env → cutoffs, velocity → amp) stay as knobs.
  A full matrix is reported to the card by an empty-slot search (FR-032 acceptance 2).
- **Rationale**: Consort's patch-bay already stores cables as (src, dst, depth) params, so the
  slot model persists, clones and automates like any param. A grid (Volt's 7 × 6) cannot
  reach 64 routes across ~20 sources × ~60 destinations.
- **Cost check**: 64 routes × 12 voices at control rate — negligible; the cost is in voice DSP.

## R10 — Sequencer, arpeggiator and motion data

- **Decision**: sequencer patterns are **structural state**, not params (as Nota Rhythm's
  steps): 8 patterns × up to 64 steps × up to 6 notes, plus 4 motion lanes × 64 values. The
  message thread edits an authoring copy through `nota_track_instrument_action` (existing ABI)
  and publishes an immutable pattern snapshot (`std::shared_ptr`, older ones retired the
  RackCore way) that the audio thread reads — AR-5. A revision counter in `scopeRead` tells the
  card to re-read after an MCP edit. Sequencer *settings* (rate, swing, length, arp mode,
  octaves, gate, on/off, target part) are ordinary global params, so they automate.
- **Clock**: `setTransport` (beat start, samples per beat, playing) — AR-9; steps fire at exact
  sample offsets computed from the beat grid (SC-009); a jump or loop re-aligns at the next
  step and releases sounding sequencer notes. Not rolling → stopped (FR-061; unlike Consort,
  which free-runs).
- **Arpeggiator**: reuses the note-order logic of `midi_fx/Arpeggiator` (factored into a small
  shared helper if it isn't already one) rather than a second implementation.
- **Motion lanes**: a lane targets a param index + is applied as an override of that param's
  sounding value per step (stepped or smoothed), so it acts on the side the param belongs to
  (FR-063 / FR-024).
- **Alternatives**: patterns as params (Consort's 16 steps) — 8 × 64 × 6 notes would add
  ~10 000 params; patterns as a MIDI clip — loses per-patch patterns and transpose.

## R11 — Presets that carry patterns

- **Decision**: Twin presets keep `NamedParams` (so the "modified" mark, specs/048, and
  cross-version loading keep working) and add the **pattern blob** in `StateBase64`;
  `PresetService` applies params first, then the blob. Factory presets with patterns build the
  blob in `FactoryPresetCatalog` from a compact text form (like `ConsortSeq(...)`).
- **"Load into Lower / Upper"**: managed-side remap of the preset's `lo.*` ids to the target
  part's prefix, applied in one undo gesture; a Single preset fills the chosen part and turns
  the patch to Layer (US9 acceptance 4).

## R12 — Voice allocation, Split / Layer

- **Decision**: one 12-voice pool; each part has a contiguous share (`share` param, steps of 2)
  and its own `PolyVoiceManager`-style allocator over its slice, so stealing never crosses
  parts (FR-071). Split routes by key; Layer sends each note to both parts. A share or mode
  change releases surplus voices (never cuts).
- **CPU**: SC-001 / SC-010 budget is the whole instance, so Layer at 6 + 6 costs what Single
  at 12 does.

## R13 — "To clip"

- **Decision**: managed-side: read the active pattern from the state blob, write notes into a
  new MIDI clip on the track through the existing clip / note APIs and motion lanes into
  automation on the lane's param, in one undo step; then switch `seq.on` off. No new ABI.

## R14 — Undo for multi-param edits

- **Decision**: Copy A → B / B → A / swap, copy part, load-into-part and matrix-route create /
  delete are param batches inside **one mix gesture** in the engine's undo history (specs/009
  / 016 mechanism). Pattern edits checkpoint the instrument's state blob into the same history;
  how Nota Rhythm's step edits undo today is checked first in tasks — Twin follows it, or the
  checkpoint is added if Rhythm has none.

## R15 — Verification

- **Decision**: smoke test (`tests/Nota.SmokeTest`) gains a Twin section: add track, render,
  morph endpoints equal side-only renders (SC-002), per-note pressure isolation (SC-005),
  sequencer onset grid at two tempos (SC-009), state round-trip (SC-008). The perf bench
  (`tests/perf/nota_perf.cpp`, `scripts/perf-gate.sh`) gains kind 18 at 12 voices for SC-001 /
  SC-010 against Pentad at 16.

## Open risks carried into tasks

| Risk | Mitigation |
|------|------------|
| R2a — ~1 050 params slow down param lists / pickers | measure in slice P1; group by part / side |
| R5 — Steiner model misses CPU budget | fall back to SVF mode-crossfade, same params |
| SC-001 — 12 voices × ×2 oversampling × 2 filters | control-rate coefficients, SIMD across voices if needed (`dsp/math/Simd.h`) |
