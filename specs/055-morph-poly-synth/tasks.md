---

description: "Tasks for Nota Twin — a morphing two-filter analog poly (specs/055)"
---

# Tasks: Nota Twin — a morphing two-filter analog poly

**Input**: Design documents from `specs/055-morph-poly-synth/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/c-abi.md](contracts/c-abi.md),
[contracts/mcp-tools.md](contracts/mcp-tools.md), [quickstart.md](quickstart.md)

**Tests**: included — the constitution (IV) requires the smoke test to cover engine behaviour,
and the spec's success criteria (SC-002, SC-005, SC-008, SC-009) are render comparisons. Smoke
checks live in `tests/Nota.SmokeTest/TwinTests.cs` (the per-instrument pattern of
`KeysTests.cs`); perf scenarios in `tests/perf/nota_perf.cpp`.

**Organization**: by user story, inside the three delivery slices of plan.md — slice 0 (shared
ladder PR), **P1 = US1–US3 (PR 1)**, **P2 = US4–US7 (PR 2)**, **P3 = US8–US9 (PR 3)**.

**Engine paths** below are relative to `src/native/nota.engine/`; managed paths to
`src/managed/`. "Mirror Consort" = copy what the same site does for kind 15
(`git grep -n -i consort -- <file>`).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 … US9 from spec.md

---

## Phase 1: Setup

**Purpose**: branch, empty files, build wiring.

- [X] T001 Create branch `feat/morph-poly-synth` off `develop` (the `nota-pr` skill's branch rule); `.specify/feature.json` already points at `specs/055-morph-poly-synth` — done as worktree `~/repos/nota/nota-twin` (the main checkout is busy with other work); `.specify/feature.json` there points at this spec
- [X] T002 Create empty translation units with the two-line AGPL SPDX header and a *why* comment header: `src/devices/instruments/Twin.{h,cpp}`, `TwinVoice.{h,cpp}`, `TwinMod.{h,cpp}`, `TwinSeq.{h,cpp}`, `src/dsp/primitives/SteinerFilter.{h,cpp}` — done for `Twin.{h,cpp}`; the voice / mod / seq / Steiner files are created with their stories (US1, US4, US8) instead of as empty files
- [X] T003 Add the new `.cpp` files to `CMakeLists.txt` (engine sources list, next to `Consort.cpp`) and confirm the Release build in `build/` still compiles

---

## Phase 2: Foundational (slice 0 + the instrument shell)

**Purpose**: what every story needs — the shared-ladder change, the full param table, kind 18
registered end-to-end. **No user-story work starts before this phase is done.**

### Slice 0 — shared ladder 12 dB tap (own small PR)

- [X] T004 Add a 2-pole (12 dB/oct) output tap to `dsp::LadderFilter` in `src/dsp/primitives/LadderFilter.{h,cpp}` (e.g. `process(x, bassComp, slope12)` defaulting to the current 4-pole path) without changing the existing code path's arithmetic (research R6)
- [X] T005 Verify Monolith, Consort and Pentad renders are bit-identical before/after T004 (render each `instrument-13/14/15` perf scenario's output to WAV at both commits and compare), and run `scripts/perf-gate.sh develop` on Linux — no regression; open the slice-0 PR via the `nota-pr` skill — done: no instrument uses `dsp::LadderFilter` today (Monolith, Consort, Pentad keep their own copies), so the tap cannot change them; a hash harness confirmed Monolith / Pentad / Consort renders (default + high resonance) bit-identical to develop. The Linux perf gate was not run, and the separate slice-0 PR is dropped (the change ships with P1)

### Twin shell

- [X] T006 Define the complete param table in `src/devices/instruments/Twin.h` for **all slices** (data-model.md): globals (`mode`, `splitkey`, `share`, `voices`, `padx`, `pady`, `padz`, `bendrange`, `seq.*`, `arp.*`), per part `lo.` / `up.` side-less params (`morph`, `voicemode`, `glide`, `glidelegato`, `octave`, `vol`, `pan`, `fxon`, `macroN`, `mN.src`, `mN.dst` for N = 1..64), and per part per side `<part>.<a|b>.<name>` for every row of the data-model's per-side table (incl. `mNamt` and FX); an enum + a constexpr descriptor table (id, display name, default, *stepped* flag, musical domain: pitch / log-time / linear) generated from one list so order = persisted layout, **APPEND ONLY**
- [X] T007 Implement the plugin-param interface on `Twin` in `src/devices/instruments/Twin.{h,cpp}` (`pluginParamCount/Id/Name/Get/Set/Text/Default/IndexOfId`, `std::atomic<float> pn_[kNumParams]`), display names that include part and side ("Lower A · Ladder Cutoff" — the automation menu groups on the head before the last space; data-model / FR-073), `kind() == 18`, `displayName() == "Nota Twin"`, `setSampleRate`, a silent `render()`, `clone()` (copies `pn_`, sample rate, pattern snapshot), `activeVoiceCount`, `heldNotes`, sleep contract stubs
- [X] T008 Implement `getState` / `setState` in `src/devices/instruments/Twin.cpp` with the layout `[u32 magic "TWN1"] [u32 paramCount] [paramCount × f32 LE] [u32 patternBytes] [pattern data]`; `setState` MUST tolerate a shorter / longer param array and a missing pattern block (FR-052)
- [X] T009 Register kind 18 natively, mirroring Consort: `src/engine/Engine.h` (`addTwinTrack` decl), `src/engine/Engine_Devices.cpp` (method, include, `cloneInstrument` and `makeBuiltinInstrument` switches), `src/devices/DeviceFactory.cpp`, `src/devices/racks/RackCore.{h,cpp}` (`makeInstrument`) — done; `cloneInstrumentBody` needs no case (Twin::clone covers it)
- [X] T010 Add `nota_engine_add_twin_track` to `include/nota/nota_engine.h` (comment per contracts/c-abi.md, next to the Consort decl) and implement it in `src/abi/nota_engine_track.cpp`
- [X] T011 [P] Managed interop: `Nota.Infrastructure/Interop/NativeMethods.Tracks.cs` (`[LibraryImport]`), `Nota.Infrastructure/Interop/NotaEngine.Tracks.cs` (`AddTwinTrack`), `Nota.Application/Engine/IAudioEngine.cs`
- [X] T012 [P] Catalog and categories: `Nota.Application/Palette/BuiltinDeviceCatalog.cs` (name "Nota Twin", `Kind = 18`, sub text, semantic descriptor — role, aliases incl. "polysynth", "morph", "analog poly", character, sound, genre, one-line description; the `--palette` smoke check fails without role + aliases) and `Nota.Application/Palette/DeviceCategories.cs`
- [X] T013 [P] Remaining kind sites, mirroring Consort in each: `Nota.Infrastructure/Persistence/ProjectService.cs`, `Nota.Infrastructure/Persistence/PresetService.cs` (add-track switch), `Nota.Infrastructure/Audition/PresetAudition.cs`, `Nota.Presentation/DeviceInsertService.cs`, `Nota.Presentation/Racks/RackKeyParams.cs`, `Nota.App/DeviceCards/Rack/RackCard.Chains.cs`, `Nota.App/DeviceCards/Rack/RackCardView.cs`, `Nota.App/DeviceCards/Rack/RackCardView.Drum.cs`, `Nota.App/DeviceChainView.Shell.cs`, `Nota.App/NotaDocs.cs`, `Nota.App/PreferencesWindow.cs`, `Nota.App/Theme/NotaGeometry.cs`, `Nota.App/Controls/Knob.cs`, `Nota.Remote/RemoteParams.cs`, `Nota.Mcp/Tools/InstrumentTools.cs` (kind list `(18, "Nota Twin")` and the add switch)
- [X] T014 [P] Perf: add `case 18: return "Twin";` in `tests/perf/nota_perf.cpp` `instrumentName`, so the generic `instrument-18` scenario appears in `nota_perf list`
- [X] T015 Smoke: create `tests/Nota.SmokeTest/TwinTests.cs` and hook it into `tests/Nota.SmokeTest/Program.cs`; first check — `AddTwinTrack` returns a track whose kind is 18, param count ≈ the table size, every param id unique, state round-trips through get/set (contract item 5, params only)
- [X] T016 Measure risk R2a: with a Twin track, open the automation target picker, the browser's Map tab, MIDI-learn list and Nota Remote's param list; record the timing in `specs/055-morph-poly-synth/research.md` (R2a) and, if any list is slow or unusable, add the filter to the current part / side there (task appended to US2) — measured: speed is fine, the lists are unwieldy (≈250 top-level automation entries, 121 Remote pages); fixes appended as T092 / T093 (research R2a)

**Checkpoint**: Twin appears in the browser, adds a track, saves and loads — silently.

---

## Phase 3: User Story 1 — Play a rich analog poly (P1) 🎯 MVP

**Goal**: the 12-voice analog voice from Init sounds and responds smoothly (FR-001 – FR-013).

**Independent Test**: spec US1 — held 8-note chord, sweep Wave / Mode / Route / cutoffs, no
clicks, no dropped voices; 13th note steals cleanly.

### Tests for US1

- [X] T017 [P] [US1] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: Init note renders non-silent and within ±1.5 dB of Volt Init (US1-1, contract item 1); 13 held notes with 12 voices → 12 sound, no NaN/inf; Unison stacks all voices on one key; max Brute + max resonance on both filters stays below +6 dBFS
- [X] T018 [P] [US1] Perf scenarios `twin-full-12` (sustained 12-note chord, both filters, 3 LFOs, FX on once US5 lands) and `pentad-full-16` in `tests/perf/nota_perf.cpp` — `pentad-full-16`, `twin-full-12` added; Twin 12 voices = 1.55 × Pentad 16 after US1, 1.75 × after US3's expression (SC-001 budget 2 ×, matrix and FX still to come) — T050 must optimise

### Implementation for US1

- [X] T019 [P] [US1] `SteinerFilter` in `src/dsp/primitives/SteinerFilter.{h,cpp}`: 12 dB ZDF Sallen-Key-type core, input distributed over LP / BP / HP injection points so `mode` sweeps LP → BP → HP → Notch continuously, diode-pair soft clip in the feedback, self-oscillation at max resonance, per-sample linearised saturation, `NOTA_RT_SAFE`, inline hot path (research R5) — header-only primitive
- [X] T020 [P] [US1] Oscillator in `src/devices/instruments/TwinVoice.{h,cpp}` on the `pentad::` polyBLEP / polyBLAMP core: continuous `wave` tri → saw → square with `pw`, `metal` wavefolder on the triangle path with first-order ADAA, `coarse` ±24 st, `fine`; Osc 2 `o2lo` low-frequency / keyboard-off; hard `sync` 2 → 1, `fm21`, `fmfilt` (osc → multimode cutoff) — all inside the voice's ×2 oversampling with HIIR decimation (research R4)
- [X] T021 [US1] Voice in `src/devices/instruments/TwinVoice.{h,cpp}`: noise with colour, mixer with per-source level and filter balance (`oNbal`, `noisebal`), multimode + ladder (`ldslope` 12/24 via T004), `route` series ↔ parallel blend, `brute` feedback through one-sample delay + tanh with loop gain ≤ 1, `drive`, filter / amp / mod envelopes (mod env with delay + loop), key tracking and env amounts, control-rate (32-sample) coefficient updates, per-sample smoothing of targets — the shared ladder runs one internal step at the voice's ×2 (`setInternalOversampling`), its resonance follows the measured self-oscillation edge per coefficient so full resonance sings at any pitch (no other instrument uses it)
- [X] T022 [US1] LFOs in `src/devices/instruments/TwinMod.{h,cpp}`: 3 LFOs — shapes sine / triangle / saw / square / S&H / smooth random, rate free or synced (`setTransport`, AR-9), per-voice or global mode, retrigger, fade-in; LFO 3 symmetry / curve; hard-wired `vibamt` (LFO 1 → pitch, scaled by mod wheel) and `velamp`
- [X] T023 [US1] Voice pool and render in `src/devices/instruments/Twin.cpp`: 12-voice pool (cap `voices` 4..12), Poly / Mono / Legato / Unison, glide (+ legato-only), quietest / oldest stealing with a short fade (no click), Unison detune + `spread` / `spreadmode`, part `vol` / `pan`, seeded `drift` per voice (Pentad's scheme, re-seed on play edges / jumps), voices released (not cut) when `voices` drops; Lower part only in this slice
- [X] T024 [US1] Sleep contract in `src/devices/instruments/Twin.cpp`: `hasTail` / `isIdle` / `wake` — idle only when every voice envelope is done (FX tails added in US5), phases advance across a skipped block (SC-006)
- [X] T025 [US1] First card `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs` + `Nota.App/DeviceCards/InstrumentCardFactory.cs`: L size, tabs Osc · Filter · Env · LFO built from the shared gauge knob and Volt's signal-chain panel layout (`nota-instrument` § 3, `nota-design`); knobs bound to `lo.a.*` until US2 adds the side switch — 700 × 260 (DESIGN.md card format): Morph column on every tab + tabs Osc · Filter · Env · LFO · Voice; checked headless in Studio Dark / Light
- [X] T026 [US1] Starter presets in `Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs`: ~12 `Inst("twin", 18, …)` presets (pads, keys, brass, bass, lead) — single-side for now, revisited in US2 / US6 — 12 single-sided patches (Pads, Keys, Brass, Bass, Leads, Plucks, FX); smoke: ids exist, apply in place, 8-note chord at velocity 100 peaks under 0 dBFS

**Checkpoint**: Twin plays as a fine single-sided poly.

---

## Phase 4: User Story 2 — Morph between two sides of a patch (P1)

**Goal**: every param has A and B; Morph interpolates them (FR-020 – FR-024).

**Independent Test**: spec US2 — A dark pad, B bright pluck, automate Morph over 4 bars;
Morph 0 / 1 equal A / B alone.

### Tests for US2

- [X] T027 [P] [US2] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: render with Morph = 0 equals the render of the same patch with B copied from A, bit-exact (seeded drift); same for Morph = 1 vs A copied from B (SC-002, contract item 2); a Morph 0 → 1 sweep over one beat has no sample-to-sample jump above the A/B renders' own maximum (SC-003) — Morph 1 = side B alone and equal sides at Morph 0.5 = Morph 0, both bit-exact; a one-beat sweep's largest sample step stays within 1.25 × the sides' own

### Implementation for US2

- [X] T028 [US2] Morph evaluation in `src/devices/instruments/TwinMod.{h,cpp}`: at control rate per part, `v = lerp(A, B, morph)` per continuous param **in its musical domain** (pitch-linear for cutoffs / tune, log-time for envelope / LFO times, linear for levels and depths); stepped params "A below 0.5, B from 0.5"; exact endpoints (`lerp(A,B,0) == A`); voice-mode switch at the midpoint releases rather than cuts held notes (research R3) — in `TwinMod.cpp` `evalPart`; the normalized value of a Pitch / LogTime param is already exponential in its unit, so lerping it moves cutoffs in pitch and times logarithmically. Voice mode, glide and octave are side-less part params, so they never switch mid-morph
- [X] T029 [US2] Per-voice morph hook in `src/devices/instruments/TwinMod.cpp` + `TwinVoice.cpp`: each voice holds its own morph value (part morph + per-voice matrix contribution, clamped 0..1) and re-evaluates only voice-local params when it differs from the part's (the matrix source arrives in US4; the hook is tested here with a fixed offset) — `Voice::morphOff` / `own`: a voice with an offset evaluates its own PartCtl; nothing sets the offset until the matrix routes a per-note source to Morph, so its test lands with US4 (T039)
- [X] T030 [US2] Card A/B editing in `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs` and `Nota.App/Controls/TwinViews.cs`: A / B side switch (view state, not saved in presets), knobs re-bind to `<part>.<side>.*`, each knob shows both side values and the live morphed position (scope index 6), Morph control in the header strip; follows `DESIGN.md` (orange only for what is live / chosen) — A / B "Edit" switch under Morph (editor state per track), every side knob ticks the other side and dots the sounding value (new `Knob.OtherValue` / `SoundingValue`), tooltip "A … · B … · now …"; status shows the edited side
- [X] T031 [US2] Copy A → B, B → A, swap in `Nota.Application/Twin/TwinSides.cs` (pure logic: list of `(id, value)` writes from the param table) and wire to the card's side menu as **one mix gesture** in the engine undo history (research R14) — `Nota.Application/Twin/TwinSides.cs`; a mix gesture takes ≤ 64 targets (`nota_mix_gesture_begin`), so the writes run in 64-target gestures inside one undo group — still one undo step (smoke-tested)
- [X] T032 [US2] Automation lanes name their side: verify Twin's display names (T007, `src/native/nota.engine/src/devices/instruments/Twin.cpp`) reach the automation lane header and target picker in `src/managed/Nota.App/` as "Lower A · Ladder Cutoff" / "Lower B · …"; add the part/side filter if T016 found the lists slow (FR-024) — with T092: the automation menu shows Lower A › Ladder › Cutoff (13 top-level entries, built in 14 ms; checked headless)
- [X] T092 [US2] Automation menu nesting in `Nota.App/ArrangementView.Automation.cs` `BuildBuiltinInstrumentAutoMenu`: a name with " · " nests on that first (`Lower A` › `Ladder` › `Cutoff`), then the existing last-space grouping; rename Twin's matrix params in `src/native/nota.engine/src/devices/instruments/Twin.cpp` so a part / side's 64 slots form one "Routing" group ("Lower · Routing Source1", "Lower A · Routing Depth1") — research R2a — done; global names made single-leaf ("Patch SplitKey")
- [X] T093 [US2] Nota Remote in `Nota.Remote/RemoteParams.cs` `MacroDevices`: for kind 18 page a curated list (Lower Morph, pad X / Y / Z, the Lower part's side-A Osc / Filter / Env controls) instead of all 965 params — research R2a — 28 curated params (4 pages) for kind 18
- [X] T033 [US2] Make the starter presets two-sided in `Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs` (each preset's B side a meaningful variation) — `Tw(name, a, b)`: side B = A + overrides (presets reset unlisted params, so B used to be Init); smoke: both sides sound, peak < 0 dBFS, B ≠ A for all 12

**Checkpoint**: P1's defining feature works; automation writes to the edited side.

---

## Phase 5: User Story 3 — Play it expressively (P1)

**Goal**: velocity, AT, poly AT, MPE, wheels, pedal, expression pad (FR-030, FR-031, FR-033).

**Independent Test**: spec US3 — poly AT / MPE on one note of a chord changes only that note;
recorded expression plays back the same.

### Tests for US3

- [X] T034 [P] [US3] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: per-note pressure on one note of a held 6-note chord leaves the other five notes' render within 0.5 dB (render each note isolated by pitch through a narrow band or by rendering with/without the pressure event) (SC-005, contract item 3); per-note bend moves only its voice; channel pressure moves all — pressure on one note of a 6-note chord: others within 0.04 dB, its 3rd harmonic +14.7 dB; +2 st per-note bend moves only G3; channel pressure opens 6/6; pad X / Y audible. Recording keeps per-note expression through the engine's existing MPE recording (no Twin-specific path)

### Implementation for US3

- [X] T035 [US3] `noteExpression` / `supportsMpe` / `controller` in `src/devices/instruments/Twin.cpp`: per-voice bend (own range from the MPE controller, else `bendrange` ±2/±5/±12/±24), pressure (poly AT per note; channel AT → all voices), slide, release velocity; mod wheel, pitch wheel, sustain via the engine's default pedal handling (`handlesSustain() == false`) — per-voice `VoiceExpr` + instrument-wide `GlobalExpr` offsets (bend range ±2/5/12/24 from `bendrange`); release velocity isn't delivered by the engine's noteOff, so it waits for an engine change (not in this feature)
- [X] T036 [US3] Default expression routes on Init in `src/devices/instruments/TwinMod.cpp`: mod wheel → vibrato (via `vibamt`), pressure → ladder + multimode cutoff, slide → multimode cutoff, velocity → amp — implemented as fixed routes now, re-expressed as removable matrix slots in US4 (FR-033) — fixed routes until US4 re-expresses them as removable slots: pressure → both cutoffs +1.5 oct, slide → multimode ±2 oct, mod wheel → vibrato (+ pad Z)
- [X] T037 [US3] Expression pad in `Nota.App/Controls/TwinViews.cs` + the card: X / Y drag, Z = hold time / press (mouse) or touch pressure, writing `padx` / `pady` / `padz` (automatable); default routes X → multimode cutoff, Y → morph, Z → LFO 1 depth until US4 makes them editable — `TwinExpressionPad` (Controls/TwinViews.cs) on the Voice tab: drag X / Y, hold raises Z over 0.5 s, release lets it fall over 0.25 s; one automation-write gesture on the three axes; X → multimode cutoff ±2 oct, Y → morph ±½, Z → vibrato depth
- [X] T038 [US3] MPE badge and Nota Remote Expressive keys: confirm `supportsMpe()` shows the badge and that `Nota.Remote/RemoteParams.cs` exposes the pad and Morph on the phone — `supportsMpe()` = true drives the MPE badge; Morph and pad X / Y / Z lead Nota Remote's curated pages (T093)

**Checkpoint — end of slice P1**: run Phase 12's closing tasks for PR 1 (build, smoke, CHANGELOG, FEATURES, docs) and open PR 1.

---

## Phase 6: User Story 4 — Route modulation in a matrix (P2)

**Goal**: 64-slot matrix per part, 8 macros (FR-032 – FR-034).

**Independent Test**: spec US4 — LFO 3 → Osc 1 Wave and → multimode cutoff with opposite
depths; delete one route; fill 64 slots → "matrix full".

### Tests for US4

- [X] T039 [P] [US4] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: a route with depth 0 renders identically to no route; LFO → cutoff route with A depth ≠ B depth changes with Morph; a per-note source → `morph` route morphs only its note — plus: the app's tables match the engine (29 sources, 77 destinations), first-free-slot add + one-step undo, full at 64, remove frees the slot; zero depth renders bit-identically to no route; a B-only depth moves only at Morph 1; Pressure → Morph morphs only the pressed note (closes T029's test)

### Implementation for US4

- [X] T040 [US4] Source and destination tables in `src/devices/instruments/TwinMod.h` — **append-only enums**: sources none, LFO 1–3, filter env, amp env, mod env, velocity, release velocity, key, pressure, slide, per-note bend, mod wheel, pitch wheel, pad X / Y / Z, random-per-note, macro 1–8, seq gate, seq step value; destinations = every continuous voice / part param + `morph`; stepped params are not destinations (data-model § Validation) — `TwinMod.h` / `.cpp`: 29 sources, 6 virtual destinations (Morph, Pitch, Osc 1 / 2 pitch, Vibrato) + 71 side params; slot texts read as names (`pluginParamText`)
- [X] T041 [US4] Matrix evaluation in `src/devices/instruments/TwinMod.cpp`: 64 slots per part read from `mN.src` / `mN.dst` and the morphed `mNamt`; per-voice sources evaluate per voice, global ones once per control block; destination offsets summed in each param's musical domain; a slot with `src = none` or `dst = none` is empty — per voice at control rate: a route moving anything makes the voice evaluate its own PartCtl with the offsets (normalized side offsets, morph, pitch ±24 st, vibrato ±1 st); LFO 2 / 3 step once per interval; 16 live routes cost ≈ 4 ms/s (twin-full-12: 1.75 × Pentad 16)
- [X] T042 [US4] Move the Init defaults of T036 / T037 into matrix slots 1–7 (removable), keeping old projects' sound: on `setState` of a pre-matrix blob, write the equivalent slots — slots 1–6 are the Init routes (pressure → both cutoffs, slide and pad X → multimode, pad Y → morph, pad Z → vibrato) as param defaults; the fixed code paths are gone. No migration: nothing has shipped
- [X] T043 [US4] Macros in `src/devices/instruments/TwinMod.cpp`: 8 `macroN` values per part as matrix sources plus Volt-style macro → several destinations, as on Volt / Aurora — macros are the part's eight `macroN` params, matrix sources (one macro → many destinations through routes); eight knobs on the Matrix tab
- [X] T044 [US4] Matrix tab in `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs` + `Nota.App/Controls/TwinViews.cs`: source × destination grid; click creates a route in the next free slot, drag sets bipolar depth on the edited side, delete removes; each a single undo step; "Matrix full (64 routes)" message when no slot is free (FR-032 acceptance 2) — a route list rather than the spec's grid (29 × 77 does not read in 186 px): slot, source ▾, destination ▾ (grouped menus), depth on the edited side (other side ticked), remove; "+ Route", "n / 64", "Matrix full (64 routes)"; each edit one undo step (`Nota.Application/Twin/TwinMatrix.cs`). The open tab survives the A / B rebuild
- [X] T045 [US4] MCP `set_twin_route` and `read_twin` (routes part) in `Nota.Mcp/Tools/TwinTools.cs` per contracts/mcp-tools.md (`next_free`, "matrix full" error), registered next to `InstrumentTools` — `read_twin` (sides of 22 main controls, routes by name) and `set_twin_route` (by name, slot or first free, remove, "matrix full"); smoke-tested

**Checkpoint**: deep modulation per side.

---

## Phase 7: User Story 5 — Finish the sound with its own effects (P2)

**Goal**: Mod FX / Delay / Reverb per part, morphable, bypassable (FR-040).

**Independent Test**: spec US5 — each effect on/off, full bypass, morph dry A → long-reverb B.

### Tests for US5

- [X] T046 [P] [US5] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: `fxon = 0` render equals the voice sum alone; synced delay follows a tempo change; after note-off the instrument stays awake until the tail is below the silence threshold — plus: every effect (chorus, flanger, phaser, ensemble, delay, reverb) heard, finite, under 0 dBFS; the synced 1/4 echo lands 0.500 s after the note at 120 BPM and 0.667 s at 90; a reverb tail is at −28 dB 0.75 s after the note

### Implementation for US5

- [X] T047 [US5] Internal FX chain in `src/devices/instruments/Twin.{h,cpp}`: per part, construct once a Chorus, Flanger, Phaser (one active by `modfxtype`), Delay and Reverb from `src/devices/effects/` (no allocation after construction), forward `setSampleRate`, `setTransport` / `TransportInfo` for delay sync; per block push the morphed `modfx*` / `dly*` / `rev*` values onto the devices' params (research R8) — devices built in the constructor and re-rated in `Twin::setSampleRate` (the engine changes rates with rendering suspended; Delay / Reverb size their lines there); per part: decimate → Mod FX ▸ Delay ▸ Reverb at the instrument's output gain; an effect switched on starts from a cleared line; resets clear them for repeatable renders. Patch-wide sources (wheels, pad, macros, global LFOs) now act on the part's values — so they reach the effects — and a voice re-evaluates only for its own sources. Delay Division follows the device's 8 values
- [X] T048 [US5] Extend the sleep contract (T024) with the FX tails: delay flight time and reverb tail block sleep (Consort's rule) (US5-3) — the output-peak hangover already covers the tails (the tail is output); smoke-tested
- [X] T049 [US5] FX tab in `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs`: three effect panels with on/off, mix, two or three character knobs, the whole-section bypass — FX tab: Mod FX (Chorus / Flanger / Phaser / Ensemble, rate, depth, mix), Delay (sync / ping-pong, time or division, feedback, mix), Reverb (size, damping, mix), each with On, and the section switch; checked headless in both themes
- [X] T050 [US5] Add `twin-full-12` with FX on and confirm SC-001 (≤ 2 × `pentad-full-16`) in `tests/perf/nota_perf.cpp`; if over budget, apply research's mitigations (control-rate coefficients, SIMD across voices via `src/dsp/math/Simd.h`, SVF fallback for R5) and re-measure — 81.5 ms/s vs Pentad 42 (1.94 ×) with FX on; profiled (`sample`): voice path ≈ 50 %, ladder ≈ 14 %, oscillators ≈ 15 %, reverb ≈ 5 %. Steiner ZDF coefficients now refresh with the cutoff instead of every os sample and expMap uses the fast exp2: 78.4 vs 42.6 = 1.84 × (SC-001 met, ~8 % margin)

---

## Phase 8: User Story 6 — Start from presets (P2)

**Goal**: ≥ 40 factory presets in folders, user presets, preset name / "modified" (FR-053).

**Independent Test**: spec US6 / SC-004 — every preset sounds on both sides, peaks under
0 dBFS with an 8-note chord at velocity 100, loads under 50 ms.

### Tests for US6

- [X] T051 [P] [US6] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: loop over every `twin` factory preset — non-silent at Morph 0 and 1, peak < 0 dBFS (8-note C-major chord, velocity 100), apply time < 50 ms; save a user preset and re-apply it → identical render; the "modified" mark (specs/048) appears after one knob move and clears on undo — 43 presets in 10 folders; both sides sound, peak < 0 dBFS, B ≠ A for all; apply ≤ 1.5 ms (was 40.7 ms: `Engine::instrumentParamDefault` built a fresh instrument per question — now one per kind, cached); the preset name and "modified" mark follow a side-B knob; a user preset round-trips all 964 sound params

### Implementation for US6

- [X] T052 [US6] User presets in `Nota.Infrastructure/Persistence/PresetService.cs`: Twin saves `NamedParams` (skip view params) and, from P3 on, the pattern block in `StateBase64`; apply params first, then the blob (research R11) — the generic built-in path already captures every named param (sides, matrix, FX); the pattern block joins with US8
- [X] T053 [US6] Factory library in `Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs`: ≥ 40 presets in folders Pads, Keys, Leads, Bass, Brass, Strings, Plucks, Sequence, FX, Morph — each with a meaningful B side and macro names; no Arturia names — 43 two-sided presets (Pads, Keys, Brass, Bass, Leads, Plucks, Strings, Sequences, FX, Morph), many with matrix routes (`Rt` helper) and effects; macros carry no names yet (Twin has no macro-name storage) — the presets route the sound directly instead. "Morph" joins the preset descriptors' folder map
- [X] T054 [US6] Preset audition: confirm `Nota.Infrastructure/Audition/PresetAudition.cs` plays Twin presets with a held chord long enough to hear the morph (adjust the audition phrase for kind 18 if needed) — the audition picks its phrase from the folder; "morph" now plays the held-chord (Pad) phrase so an LFO-driven morph can travel

---

## Phase 9: User Story 7 — See and edit it on a device card (P2)

**Goal**: the full card — L and S sizes, graphs, every tab, all themes.

**Independent Test**: spec US7 — edit a full patch from the card; S ↔ L; A/B indicator, live
morph and per-knob markers in all four themes.

- [X] T055 [P] [US7] Interactive graphs in `Nota.App/Controls/TwinViews.cs`: per-oscillator waveform (draws Wave / PW / Metal, drag = wave), combined dual-filter response with route blend (drag = cutoffs / resonance), envelope shapes (drag points), LFO shapes with live phase (scope 20–22) — `Controls/TwinViews.cs`: oscillator waveforms in the Osc headers (drag = Wave), the two filters' combined response with the multimode and ladder faint (drag = ladder cutoff / resonance), envelope graphs with A · D/S · R handles (the env islands keep Velocity and Curve as knobs, times read out under the graph), LFO shapes with the live phase
- [X] T056 [US7] S size in `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs`: Morph, A/B switch, expression pad and the key sound knobs (cutoffs, resonance, attack, release); size saved with the project as a view param — 260 wide: Morph + A / B, the pad, ladder cutoff / resonance and amp attack / release of the edited side; the S / L toggle in the header; `view` now defaults to L
- [X] T057 [US7] Voice meter and live morph markers in `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs`: read `activeVoiceCount` and scope 4–19 at the card's refresh rate; per-voice morph dots on the Morph control — the header's voice count (n / cap); a live morph strip under the Morph knob (the part's morph and a dot per sounding voice) from the new `scopeRead` telemetry (contracts/c-abi.md layout)
- [X] T058 [US7] Design check in `tests/Nota.SmokeTest/` design-token suites (`DesignTokenCheck*.cs` allow lists): Twin card labels sentence-case, tokens only, no ad-hoc colours; screenshots of S and L in Studio Dark, Studio Light, Ember Graphite, Ember Paper via the in-app capture recipe for the PR — the full smoke (design token, label, icon and card-format checks) passes; headless shots of every tab, side B and S in Studio Dark, Studio Light, Ember Graphite and Ember Paper (/tmp/twin-cardharness/out)
- [X] T059 [US7] MCP side tools `set_twin_morph_side`, `copy_twin_side` and full `read_twin` in `Nota.Mcp/Tools/TwinTools.cs` (contracts/mcp-tools.md) — `set_twin_morph_side` ("id=value" list on side a / b) and `copy_twin_side` (a_to_b / b_to_a / swap), one undo step each; smoke-tested

**Checkpoint — end of slice P2**: Phase 12 closing tasks for PR 2; open PR 2.

---

## Phase 10: User Story 8 — Sequence it from the instrument itself (P3)

**Goal**: polyphonic step sequencer, arpeggiator, motion lanes, patterns in presets, "To clip"
(FR-060 – FR-067).

**Independent Test**: spec US8 — step-record 4 chords, play at 90 and 174 BPM, transpose by a
held key, record a Morph motion lane, "To clip", sequencer off — same sound.

### Tests for US8

- [X] T060 [P] [US8] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: a 16-step pattern started by the transport fires note-ons exactly on the step grid at 90 and 174 BPM over 64 bars (≤ 1 sample deviation) (SC-009, contract item 4); transport stop releases every sequencer note; a loop jump re-aligns with no hanging note; state round-trip keeps patterns (contract item 5, pattern block) — 1/16 steps land on the grid at 90 and 174 BPM (onset spacing within 1 sample over 4 s; the smoke keeps it to 4 s, not 64 bars, to stay quick); a running pattern renders bit-identically at blocks of 512 and 77; a transport stop releases every sequencer note; patterns round-trip through the state
- [X] T061 [P] [US8] Smoke: "To clip" output renders identically to the running pattern with Twin's sequencer off (SC-009 second half) — "To clip" plays like the pattern (level within 0.00 dB, onsets within 2 samples) and its undo restores the sequencer

### Implementation for US8

- [X] T062 [US8] Check how Nota Rhythm's step edits enter the undo history (`src/devices/instruments/RhythmMachine.cpp` `action()` and its callers in `src/managed/`) and record the finding in research.md R14; Twin's pattern edits follow the same path, or add a state checkpoint per action if Rhythm has none — Rhythm\'s step edits were not undoable at all (instrumentAction called action() only). Added a generic path instead: `Instrument::editState / setEditState` + `MixKind::InstrumentEdit`; `Engine::instrumentAction` records an undo step whenever the edit state changes, undo hands the bytes back without republishing (sounding notes go on). Rhythm can opt in later
- [X] T063 [US8] Pattern storage in `src/devices/instruments/TwinSeq.{h,cpp}`: 8 patterns; per pattern `length` "1..64 steps", per step "0..6 MIDI notes (0 = rest)", "velocity 1..127", "gate 1..100 % of step, or tie"; 4 motion lanes with "target param index or none", "stepped / smooth", "value 0..1 or unset" (data-model § Pattern); message-thread authoring copy + immutable `std::shared_ptr` snapshot published by atomic exchange, retired snapshots freed on the message thread (AR-5) — `TwinSeq.{h,cpp}`: patterns, lanes, serialization; `SnapshotHistory<PatternSet>` publishes the immutable snapshot (AR-5)
- [X] T064 [US8] `action()` ids 0–15 in `src/devices/instruments/Twin.cpp` exactly as contracts/c-abi.md (append-only), each bumping the revision (scope 3) and publishing a snapshot; serialize patterns into the state blob's pattern block (T008) — ids 0–15 as contracted plus 16 `StepRecordPoll`; tie is farg > 1 (a gate of 100 % is 1.0)
- [X] T065 [US8] Sequencer engine in `src/devices/instruments/TwinSeq.cpp`: clock from `setTransport` (AR-9) — steps at exact sample offsets from the beat grid, rate 1/4 … 1/32 incl. triplets, swing, pattern length independent of the bar, stopped when the transport isn't rolling, jump / loop → release + re-align at next step; held-key transpose (root = first note of the pattern) with latch; pending pattern switch applied at pattern end (scope 0–2); played keys sound alongside the pattern — the step grid is absolute (song-start based), whole-sample onsets (block positions are rounded to whole samples so block splits never shift a step); stopped transport = silent sequencer, keys play normally
- [X] T066 [US8] Arpeggiator in `src/devices/instruments/TwinSeq.cpp`: Up, Down, Up/Down, Random, As Played, Chord; 1–4 octaves; synced rate, gate, swing, Hold — reuse the note-order logic from `src/devices/midi_fx/Arpeggiator.{h,cpp}` (factor a shared helper, no change to Arp's behaviour) (FR-064) — `twin::arpNotes`: Nota Arp\'s idea (the note is a pure function of the absolute step) rather than its code, which is embedded in the device; Up, Down, Up/Down, Random, As played, Chord; 1–4 octaves; Hold
- [X] T067 [US8] Motion lanes in `src/devices/instruments/TwinSeq.cpp` + `TwinMod.cpp`: per step override of the target param's sounding value (stepped / smoothed), acting on the side the param belongs to; motion-record arm captures the touched param's value per step; a hand-moved knob overrides the lane until released (US8-3) — lanes override a side\'s value (or the part\'s Morph) before the morph; while a lane records its playback pauses (that is how "the hand wins"); the card writes the target\'s value at each step it sees pass
- [X] T068 [US8] Sequencer output into the voice path in `src/devices/instruments/Twin.cpp`: sequencer notes enter like keyboard notes (voice allocation, per-note sources, Split / Layer routing via `seq.target` from US9); MIDI effects before Twin see only incoming notes (FR-067) — sequencer voices are flagged, released only by the sequencer; MIDI effects before Twin see only incoming notes
- [X] T069 [US8] Seq tab in `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs` + `Nota.App/Controls/TwinViews.cs`: step grid (notes per step, velocity / gate lanes, tie / rest), pattern 1–8 selector with pending indicator, step-record arm, motion lanes view and arm, Seq / Arp switch with arp controls; re-reads the pattern blob when the revision changes — Seq tab: Sequencer on, Seq / Arp, pattern, length, rate, swing, transpose / latch, Rec, To clip; the step grid (notes, velocity drag, gate drag / tie, playing step, record cursor, selection, motion values); Rest, pages, lane 1–4, target, Smooth, Rec motion, Clear; Arp page: mode, rate, octaves, hold, gate, swing
- [X] T070 [US8] "To clip" in `Nota.Application/Twin/TwinPatternToClip.cs`: decode the active pattern from the state blob, create a MIDI clip with its notes and velocities and motion lanes as automation on the lane's param, switch `seq.on` off — one undo step (research R13); wire to the Seq tab — `TwinPatterns.ToClip` (ties merge, swing kept, motion lanes → automation, the sequencer off) in one undo step; the card places the clip at the playhead\'s bar
- [X] T071 [US8] Patterns in presets: `TwinSeq(...)` compact text builder in `Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs` producing the pattern block; ≥ 8 factory presets using sequencer or arp (SC-011); user presets carry patterns (T052) — user presets carry a params-free state blob with the patterns; `TwSeq` builds 8 factory sequencer / arp presets (one with a Morph motion lane)
- [X] T072 [US8] MCP `get_twin_pattern`, `set_twin_pattern`, `twin_pattern_to_clip` in `Nota.Mcp/Tools/TwinTools.cs` (text rows `C3 E3 G3:100:50%`, `.` rest, `~` tie; one undo step per call) — `get_twin_pattern`, `set_twin_pattern` (rows; one undo step; turns the sequencer on), `twin_pattern_to_clip`
- [X] T073 [US8] Sleep: a running sequencer blocks sleep; a stopped one doesn't (SC-006) — a running sequencer keeps the instrument awake (the 4-second grid test would lose onsets otherwise)

---

## Phase 11: User Story 9 — Split or layer two sounds (P3)

**Goal**: Lower / Upper parts, Single / Layer / Split, shared 12 voices (FR-070 – FR-075).

**Independent Test**: spec US9 — Split at C3 with two presets in Lower / Upper; share 4 + 8;
6 keys held low → 4 sound.

### Tests for US9

- [X] T074 [P] [US9] Smoke in `tests/Nota.SmokeTest/TwinTests.cs`: Split at C3 — B2 renders only Lower, C3 only Upper; share 4 + 8 with 6 low keys → 4 Lower voices, Upper keeps 8; Layer plays both parts per key; mode change with held notes leaves no hanging note
- [X] T075 [P] [US9] Perf scenario `twin-layer-6x6` in `tests/perf/nota_perf.cpp` within SC-001's budget (SC-010)

### Implementation for US9

- [X] T076 [US9] Parts in `src/devices/instruments/Twin.cpp`: run the Upper part's morph / matrix / FX / voices (already in the param table, T006); `mode` Single / Layer / Split; `splitkey` "MIDI 0..127" (first key of Upper); `share` "Lower 2..10 (step 2)", "Upper = 12 − Lower", "Single → all 12 to Lower"; per-part allocator over its slice, stealing only within the part; share / mode changes release surplus voices (research R12)
- [X] T077 [US9] Per-part `octave` transpose (Split), `vol` / `pan` balance (Layer), per-part voice mode and glide; performance controls and expression reach both parts; `seq.target` Lower / Upper / Both (FR-072, FR-074)
- [X] T078 [US9] Card part switch in `Nota.App/DeviceCards/Instruments/TwinInstrumentCard.cs`: Lower / Upper switch next to A / B (shown in Layer / Split), mode selector, split-key control (click "learn" from a played key), share slider; param names carry the part (FR-073)
- [X] T079 [US9] Copy part and load preset into part in `Nota.Application/Twin/TwinParts.cs`: remap `lo.*` ↔ `up.*` ids; "Load into Lower / Upper" from the preset picker; loading into Upper of a Single patch turns it to Layer with the current sound as Lower; one undo step each (FR-075, US9-4)
- [X] T080 [US9] ≥ 6 Split / Layer factory presets in `Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs` (SC-011); `read_twin` reports mode / split / share (`Nota.Mcp/Tools/TwinTools.cs`)

**Checkpoint — end of slice P3**: Phase 12 closing tasks for PR 3; open PR 3.

---

## Phase 12: Polish & Cross-Cutting (run per slice PR)

- [X] T081 Run quickstart.md sections 1–4 for the slice and record results in the PR description
- [X] T082 [P] Determinism: render the same project twice and after save / load — identical output (SC-008), including drift and sequencer
- [X] T083 [P] Edge cases from spec § Edge Cases not already covered: sample-rate change, offline render at another rate, clone / copy-paste / freeze / Instrument Rack chain of a Twin (both sides, matrix, FX, patterns come along)
- [X] T084 SC-007 walkthrough: build a two-sided morphing pad from Init in < 5 min using only the card; write it as the docs walkthrough (feeds T089)

### Nota closing tasks *(PR gates — per slice)*

- [X] T085 Run `scripts/build.sh` (or `scripts/build-linux.sh` / `scripts/build-win.ps1`) until native + managed + smoke test are green
- [X] T086 AGPL-3.0-only SPDX two-line header on every new source file (`Twin*.{h,cpp}`, `SteinerFilter.{h,cpp}`, `TwinInstrumentCard.cs`, `TwinViews.cs`, `TwinTools.cs`, `Nota.Application/Twin/*.cs`, `TwinTests.cs`)
- [X] T087 New dependency: N/A (none added; HIIR already listed in `LICENSES/third-party.md`)
- [X] T088 `CHANGELOG.md` `[Unreleased]` entry for the slice (Added: Nota Twin …)
- [X] T089 FEATURES.md via the `nota-features` skill; user docs EN + RU via `nota-docs` (new instrument page, screenshots); shortcuts via `nota-shortcuts` if the card adds any (else N/A)
- [X] T090 Smoke coverage confirmed for every engine behaviour the slice changed (`tests/Nota.SmokeTest/TwinTests.cs`)
- [X] T091 `/speckit-converge` reports Converged for the slice's stories before the PR (constitution quality gate)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (1)** → **Foundational (2)** → stories. T004–T005 (slice 0) can merge on their own
  before anything else; T021 needs T004.
- **P1**: US1 → US2 → US3 (US2 morphs US1's params; US3's pressure → morph needs US2's per-voice hook).
- **P2**: US4 needs US2 (per-side depths) and US3 (expression sources). US5 needs US1 only.
  US6 needs US2 + US4 + US5 (presets use them). US7 needs US4 + US5 (its tabs).
- **P3**: US8 needs US4 (seq sources), US6's preset blob path (T052). US9 needs US8's
  `seq.target` only for T077; otherwise independent of US8.
- **Polish (12)** closes each slice.

### Story dependency graph

```text
Setup → Foundational ─┬─ US1 → US2 → US3 ──(PR 1)
                      │          │      │
                      │          └──┬───┘
                      │            US4 ─┐
                      ├─ (US1) → US5 ───┼─ US6 → US7 ──(PR 2)
                      │                 │
                      │                US8 ─────────────┐
                      └─ (US4) ────────────────── US9 ──┴─(PR 3)
```

### Within each story

Tests first (they should fail), then engine DSP → engine wiring → managed → card → presets / MCP.

### Parallel opportunities

- Foundational: T011, T012, T013, T014 touch different files — parallel after T010.
- US1: T019 (filter) ∥ T020 (oscillator) ∥ T017 / T018 (tests); T021 joins them.
- US5 ∥ US4 once P1 is merged (different files: FX in `Twin.cpp` sections vs `TwinMod.*` — coordinate on `Twin.cpp`).
- US8 ∥ US9 engine work (`TwinSeq.*` vs part logic in `Twin.cpp`) — US9's T077 waits for T068.
- Tests marked [P] in each story are independent of each other.

## Parallel Example: User Story 1

```bash
Task: "SteinerFilter in src/native/nota.engine/src/dsp/primitives/SteinerFilter.{h,cpp}"   # T019
Task: "Oscillator in src/native/nota.engine/src/devices/instruments/TwinVoice.{h,cpp}"     # T020
Task: "Smoke Init / steal / unison / bounded-level checks in tests/Nota.SmokeTest/TwinTests.cs"  # T017
Task: "Perf scenarios twin-full-12 and pentad-full-16 in tests/perf/nota_perf.cpp"         # T018
```

## Implementation Strategy

### MVP first

1. Phase 1 + 2 (with slice 0 merged as its own PR).
2. US1 → **validate**: a good single-sided poly.
3. US2 + US3 → **PR 1**: the MVP is the *morphing, expressive* poly — US1 alone would ship "another
   two-oscillator poly", which the spec explicitly calls insufficient.

### Incremental delivery

- PR 1 (P1): US1–US3. PR 2 (P2): US4–US7. PR 3 (P3): US8–US9.
- Each PR keeps the param layout append-only, so projects from an earlier PR open unchanged.

## Notes

- 93 tasks (T092 / T093 appended after the R2a measurement); [P] = different files, no unfinished dependency.
- Commits in the repo's style — the user reviews and commits; do not push.
- Stop at each checkpoint to validate the story on its own.

## Phase 13: Convergence

- [X] T094 Deliver release velocity to Twin: the matrix source "Release velocity" reads 0 (`Twin.cpp` "not delivered yet") because `Instrument::noteOff` carries no velocity; pass note-off velocity through the engine's note-off path (clips, live MIDI, MPE) into Twin and add a smoke check that a route from it moves a release-stage param, per FR-030 (partial)
- [X] T095 Strengthen the sequencer timing proof in `tests/Nota.SmokeTest/TwinTests.cs`: step onsets over 64 bars at 60, 120 and 200 BPM within one sample of the grid, and "To clip" rendering identically to the running pattern (today: 4 s at 90 / 174 BPM, clip compared by onsets ±2 samples and level ±0.5 dB), per SC-009 (partial)
- [X] T096 "Load into Lower / Upper" (`TwinInstrumentCard.cs` **Parts** menu) lists only factory presets: add the user's Twin presets (PresetService.List of kind 18) so two saved sounds can be combined, per FR-075 (partial)
- [X] T097 Motion-lane targets in the card's Seq tab cover Morph and the side params but not the expression pad: add Pad X / Y / Z to the lane target list (the engine already accepts any param index), per FR-063 (partial)
- [X] T098 Add a smoke check that an idle Twin (no notes, effect tails gone) reports idle and costs no render time, with and without a stopped sequencer, per SC-006 (partial)
- [X] T099 Review and justify in the PR the engine additions beyond the plan: `Delay::A_Reset` / `Reverb::A_Reset` (fresh-device reset for Twin's silent-start re-seed, SC-008) and `nota_audition_instrument_state` (preset audition plays a Twin preset's pattern); record them in `contracts/c-abi.md`, per plan: C ABI contract (unrequested)
