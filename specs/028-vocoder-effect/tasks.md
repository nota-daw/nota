---

description: "Task list for Nota Vocoder audio effect"
---

# Tasks: Nota Vocoder audio effect

**Input**: Design documents from `/specs/028-vocoder-effect/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included — the constitution (IV) requires engine behaviour changes to extend
`tests/Nota.SmokeTest`; the contract in [contracts/device-params.md](contracts/device-params.md)
lists the guarantees to check. Tests live in a new `tests/Nota.SmokeTest/VocoderTests.cs`,
wired from `Program.cs` like `KeysTests` (`foreach (var (ok, label) in VocoderTests.Run()) Check(ok, label);`).

**Organization**: Tasks are grouped by user story to enable independent implementation and testing of each story.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)
- Every new source file starts with the two-line AGPL-3.0-only SPDX header and a comment
  header saying why the file exists.

## Path Conventions

- **Engine (C++)**: `src/native/nota.engine/` — DSP in `src/Vocoder.h`, registration in `src/Engine_Devices.cpp`
- **Managed (C#)**: `src/managed/Nota.App/` (card, graph), `src/managed/Nota.Application/Palette/`
  (catalogue), `src/managed/Nota.Infrastructure/Persistence/` (presets), `src/managed/Nota.Mcp/`
- **Tests**: `tests/Nota.SmokeTest/VocoderTests.cs`
- **Design reference**: `design/vocoder/index.html`

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: The device exists, is registered and can be added — it passes audio through.

- [X] T001 Create `src/native/nota.engine/src/Vocoder.h`: `class Vocoder final : public Device`, `builtinKind()` = 26, `name()` = "Nota Vocoder", 15 params as `std::atomic<float>` normalized 0..1 with the names, order and defaults from contracts/device-params.md (append-only — "Index order is the persisted layout"), `paramCount/paramName/paramMin(0)/paramMax(1)/getParam/setParam` and the per-index default used by `nota_builtin_device_param_default`; `setSampleRate`; `process(buf, frames)` passes audio through for now. Mirror `AutoFilter.h` / `Vintage.h` for the param interface.
- [X] T002 Add sidechain plumbing to `src/native/nota.engine/src/Vocoder.h` exactly like `Compressor.h` (lines ~279–282): `acceptsSidechain()` → true, atomic source track id, `setSidechain(const float*, int32_t)` storing the previous-block buffer pointer; the constructor calls `setSidechainTapPre(1)` ("a new instance sets `sidechainTapPre = 1`").
- [X] T003 Register kind 26 in `src/native/nota.engine/src/Engine_Devices.cpp`: `#include "Vocoder.h"` and `case 26` in `addTrackBuiltinDevice`, `cloneDevice` and `makeBuiltinDevice` (all three switches).
- [X] T004 [P] Add `new("Nota Vocoder", A, 26, "vocoder", D(...))` to `src/managed/Nota.Application/Palette/BuiltinDeviceCatalog.cs` with a semantic descriptor (role vocoder; aliases robot voice, talk box, choir, channel vocoder; one-line description "Shapes this track's sound with another track's voice."), and add "Nota Vocoder" to the **Modulation** category in `src/managed/Nota.Application/Palette/DeviceCategories.cs`.
- [X] T005 Update the doc comment of `nota_device_accepts_sidechain` in `src/native/nota.engine/include/nota/nota_engine.h` to list the built-in Nota Vocoder (comment only; no ABI change).

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The band engine every story needs, plus the smoke-test identity checks.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T006 Implement the band layout in `src/native/nota.engine/src/Vocoder.h` (research R-4): `kMaxBands = 32`; centres `f_i = Low · (High/Low)^((i+0.5)/N)`; synthesis centres × `2^(Formant/12)`, bands above `0.45 · fs` muted; Q → bandwidth; "Low is clamped so that `High / Low ≥ 2` (one octave) when either moves"; discrete params via `round(v · (steps − 1))`. Unit mappings exactly as the contract table (exp maps for Low, High, Attack, Release).
- [X] T007 Implement the filter banks in `src/native/nota.engine/src/Vocoder.h`: each band = two cascaded TPT SVF band-pass sections; three fixed `std::array` banks (analysis mono, synthesis L, synthesis R); coefficients recomputed every 32 samples only when Bands / Low / High / Q / Formant changed, with smoothing so moves are click-free. No allocation after construction (AR-6).
- [X] T008 Implement envelope followers in `src/native/nota.engine/src/Vocoder.h` (R-5): full-wave rectify, one-pole attack / release from the Attack / Release params; envelopes clamped to 0..4 after Mod gain (R-12).
- [X] T009 Implement the scope feed in `src/native/nota.engine/src/Vocoder.h`: override the device `scope()` used by `nota_device_scope` with the 100-float layout from contracts/device-params.md (key peak, out L/R peak, N, envelopes[32], carrier levels[32], centres[32]), written once per block into a member array.
- [X] T010 Create `tests/Nota.SmokeTest/VocoderTests.cs` and wire it in `tests/Nota.SmokeTest/Program.cs` (both run sites, next to `KeysTests`): add an audio track + `AddBuiltinDevice(track, 26)`; assert `DeviceName` = "Nota Vocoder", param count 15, every name and default per the contract, set/get round-trip of all 15, duplicate the track and compare all params plus sidechain source / tap, `DeviceAcceptsSidechain` = true, `DeviceSidechainTapPre` = true on a new instance.

**Checkpoint**: The device loads, persists and clones; the band engine exists.

---

## Phase 3: User Story 1 - Make a pad "talk" with a vocal (Priority: P1) 🎯 MVP

**Goal**: Carrier = the track's audio, modulator = another track; the output is the carrier shaped by the modulator, visible on the card.

**Independent Test**: Chord clip on track 1 with the vocoder, speech clip on track 2 as modulator → the chord speaks; modulator silent → wet silent (quickstart §2).

### Tests for User Story 1

- [X] T011 [P] [US1] In `tests/Nota.SmokeTest/VocoderTests.cs`: render (offline) a deterministic WAV chord on the carrier track and a deterministic speech-like WAV (e.g. amplitude-modulated noise bursts) on a second track routed as sidechain (pre); assert output RMS > 0 and finite; with source −1 and Mix 100 % assert output RMS ≈ 0; with the modulator clip muted assert wet RMS ≈ 0; with Mix 0 % assert output ≈ input.

### Implementation for User Story 1

- [X] T012 [US1] Implement `process()` for Carrier = Input in `src/native/nota.engine/src/Vocoder.h`: modulator = sidechain buffer summed to mono × (Mod gain dB + `sidechainGainDb()`); analysis bank → followers → gain on the synthesis bands of each carrier channel; wet sum normalised by `sqrt(16 / N) · g(Q)` (R-8); equal-power Mix; output Gain; null sidechain → envelopes decay to 0. Latency 0.
- [X] T013 [P] [US1] Create `src/managed/Nota.App/Controls/VocoderGraph.cs`: a `NotaGraph` well drawing N band bars in `VizInk` from the scope envelopes, at the log-frequency x of each centre (20 Hz – 20 kHz), corner axis labels "20 Hz" / "20 k", title "Envelope"; reads a `float[]` snapshot passed in; no animation easing (meters / spectra are never animated).
- [X] T014 [US1] Create `src/managed/Nota.App/DeviceCards/Bodies/VocoderDeviceBody.cs` (`IDeviceBody`, `Width = 700`), layout per `design/vocoder/index.html` with `DeviceCardKit.Section`: left column **Modulator** — source picker (track colour via `ArrangementView.TrackBrush`, name via `TrackNames.Of`, "None" first; the list hides the device's own track **and every group it is nested in** (R-12); a **Pre / Post** `Segments` row at the menu foot writing `SetDeviceSidechainTapPre`), Mod gain `SliderRow`, key `MeterBar`; centre **Bands** with `VocoderGraph`; right **Output** — Mix and Gain `SliderRow`s and the output meter. Params via `DeviceGetParam` / `DeviceSetParam` with `BeginAutomationWrite` / `EndAutomationWrite`; `ctx.AddDeviceRefresher` reads `DeviceScope` at 60 Hz; source changes call `ctx.NotifyChanged()`.
- [X] T015 [US1] Register `[26] = new VocoderDeviceBody()` in `src/managed/Nota.App/DeviceCards/DeviceCardFactory.cs`.

**Checkpoint**: The P1 flow works end to end in the app and in the smoke test.

---

## Phase 4: User Story 2 - Vocode without a carrier track (Priority: P2)

**Goal**: Internal Saw (at Pitch) and Noise carriers, with Tilt.

**Independent Test**: Empty track, Carrier = Saw + speech modulator → robot voice at C3; Noise → whisper (quickstart §3).

### Tests for User Story 2

- [X] T016 [P] [US2] In `tests/Nota.SmokeTest/VocoderTests.cs`: on a track with no clips, Carrier = Saw (norm 0.5) and Noise (norm 1.0) with the speech modulator → output RMS > 0 and finite; Carrier = Input on the same empty track → RMS ≈ 0; switching Carrier mid-render produces no sample-to-sample jump above a threshold (click check).

### Implementation for User Story 2

- [X] T017 [US2] In `src/native/nota.engine/src/Vocoder.h`: polyBLEP saw at the Pitch note (`round(v · 84)` semitones above C0, C3 = 130.81 Hz, 10 ms glide), xorshift32 white noise per channel, Tilt as a first-order shelf pair pivoting at 1 kHz applied to Input and Saw only, and a 10 ms crossfade on Carrier changes (R-6).
- [X] T018 [US2] In `src/managed/Nota.App/DeviceCards/Bodies/VocoderDeviceBody.cs`: **Carrier** section under Modulator — `Segments` Input / Saw / Noise, Tilt and Pitch `SliderRow`s (Pitch readout as a note name via `NoteName`); Tilt inactive when Carrier = Noise, Pitch inactive unless Carrier = Saw (`Inactive.Set`); live-follow so automation moves them.

**Checkpoint**: US1 and US2 work independently.

---

## Phase 5: User Story 3 - Shape the character (Priority: P2)

**Goal**: Bands, Low / High (knobs and graph edges), Q, Formant, Attack, Release, Unvoiced, Hold.

**Independent Test**: With a fixed carrier and modulator, each control changes sound and graph as described (quickstart §4).

### Tests for User Story 3

- [X] T019 [P] [US3] In `tests/Nota.SmokeTest/VocoderTests.cs`: Bands 8 vs 32 both finite and audible, scope N reports 8 / 32; Formant −12 / +12 finite and differ from 0; Hold on → scope envelopes unchanged across two later blocks while the modulator moves; Unvoiced 100 % on a noise-burst modulator raises energy above 4 kHz vs 0 %; sweeping Bands, Low, High, Q, Formant every block for 2 s produces finite output with no jump above the click threshold (SC-003); Low set above High clamps to `High / Low ≥ 2`.

### Implementation for User Story 3

- [X] T020 [US3] In `src/native/nota.engine/src/Vocoder.h`: band-count change = stage the layout, duck output to 0 over 5 ms, reset band states, switch, ramp back over 5 ms (no allocation); Hold skips follower updates; Unvoiced per R-7 (HF follower at 4 kHz, ratio gate, high-passed noise × Unvoiced, added before Mix).
- [X] T021 [US3] In `src/managed/Nota.App/DeviceCards/Bodies/VocoderDeviceBody.cs`: the knob row under the graph — `KnobCell` × 7 (Bands, Low, High, Q, Formant, Attack, Release) with mono readouts (`NotaNum`: Hz to 999 then "x.x k", ms, "st" signed with U+2212); Unvoiced `SliderRow` and Hold `DeviceCardKit.Switch` in Output; band knobs and Mod gain inactive while the source is None; the Bands section title shows "N · Low – High".
- [X] T022 [US3] In `src/managed/Nota.App/Controls/VocoderGraph.cs`: teal carrier polyline through the scope's per-band carrier levels; `Rule` lines at Low / High; hover / drag on an edge turns it accent with `ew-resize`, dragging writes Low / High params (bracketed by `BeginAutomationWrite` / `EndAutomationWrite`) exactly as the knobs do; empty state line "Pick a modulator track to shape the carrier" (Ink 5) when the source is None.

**Checkpoint**: All shaping controls work; the card matches the design reference.

---

## Phase 6: User Story 4 - Recall, automate and reuse (Priority: P3)

**Goal**: Persistence, undo, automation / MIDI learn, factory presets, MCP.

**Independent Test**: Save / reopen identical; automate Formant; load each preset; route a modulator over MCP (quickstart §5, §7).

### Tests for User Story 4

- [X] T023 [P] [US4] In `tests/Nota.SmokeTest/VocoderTests.cs`: save / load a project with the vocoder and a modulator → params, source and tap restored; two offline renders sample-identical (FR-017); delete the modulator track → source −1, undo → restored; every `vocoder` factory preset loads and its render on the reference pair peaks < 0 dBFS (SC-006); MCP `set_device_sidechain` / `get_device_sidechain` on the vocoder (use `McpTestSupport`). — *done: save/load, determinism, modulator deletion, presets. Undo restore and the MCP round-trip are not covered by a smoke check (they use the generic sidechain paths the compressor already tests).*

### Implementation for User Story 4

- [X] T024 [P] [US4] Add six presets in `src/managed/Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs` as `Fx("vocoder", 26, "<Name>", ("<Param name>", v), …)` with normalized values: Init, Robot choir, Talk box, Whisper (Carrier Noise), Lo-fi robot (8 bands, Saw), Strings choir (32 bands, slow Release). Add the "vocoder" folder to `src/managed/Nota.Application/Palette/PresetDescriptors.cs`' folder map if it is not vocabulary. — *done: 7 presets (Voice: Init, Robot choir, Talk box, Whisper, Lo-fi robot; Pads: Strings choir, Deep giant).*
- [X] T025 [P] [US4] In `src/managed/Nota.Mcp/Tools/DeviceTools.cs`: add "Nota Vocoder (kind 26) — the modulator" to the `get_device_sidechain` / `set_device_sidechain` descriptions and note that `gainDb` adds to the Mod gain param (contracts/mcp.md).
- [X] T026 [US4] Verify the automation menu and MIDI Learn list all 15 vocoder params by name for a built-in effect (generic path); fix only if a param is missing or a name is not sentence case.

**Checkpoint**: All user stories independently functional.

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T027 Performance: in `tests/Nota.SmokeTest/VocoderTests.cs` time 32 bands at 48 kHz against Nota Chamber on the same input (`TimingBudget`); assert ≤ Chamber (SC-004). Profile and optimise `Vocoder.h` if not. — *done: ≈ 23 ms vs Chamber ≈ 59 ms per 3 s.*
- [X] T028 Visual check of the card per the `nota-ui-verify` recipe in Studio Dark, Studio Light, Ember Graphite, Ember Paper; compare with `design/vocoder/index.html`; trim leftover margins; nothing overflows 700 × 260. — *done headless (/tmp/vocharness), all four themes, live and no-modulator states.*
- [X] T029 Run the design guard checks (`RunStudio`, `RunThemes`, `RunLabels`, `RunIcons`, `--palette`) and fix any finding in the new files without adding to the allow lists.
- [ ] T030 Run the full quickstart.md validation (sections 2–7) in the app. — *partly: the smoke test and headless renders cover §1 and §3–§5 offline; a live in-app run (§2 playback, §4 at a 128 buffer) and the §7 MCP call are still to do.*
- [X] T031 Ask the user whether `design/vocoder/` ships with the PR or is removed before merge (plan, principle V). — *2026-10-20: not shipped; the folder is removed, the spec keeps its links for history.*

### Follow-up from user testing (2026-10-20)

- [X] T038 [US1] Normalise band weights by the modulator's L2 energy with a slow AGC and floor; recalibrate make-up so the vocoded level follows the carrier (FR-019, research R-14) in `src/native/nota.engine/src/Vocoder.h`.
- [X] T039 [US1] With no modulator routed, pass the track through unchanged with a 10 ms fade (FR-018) in `src/native/nota.engine/src/Vocoder.h`; give no route slot to a deleted source track in `Engine::recomputeRouting` (`src/native/nota.engine/src/Engine_Devices.cpp`).
- [X] T041 [US1] Render a muted or solo-gated sidechain source for its taps (pre live, post silent while muted) in `src/native/nota.engine/src/Engine_Render.cpp` (FR-020); smoke check with a muted voice track.
- [X] T040 [US1] Smoke checks in `tests/Nota.SmokeTest/VocoderTests.cs`: level independent of modulator level, near the carrier's level, no-modulator and deleted-modulator equal the bypassed track; card texts (empty state, Mod gain tooltip) and docs pages updated.

### Follow-up 2: the track is the voice (2026-10-20, research R-15)

- [X] T042 [US1] Make the input the modulator and Carrier = Saw (default) · Noise · Track (sidechain) in `src/native/nota.engine/src/Vocoder.h`; Saw / Noise follow the voice's level, Track keeps the AGC; pass-through only for Track with no source.
- [X] T043 [US1] Card: Modulator = "This track" (Gain, key meter); Carrier segments Saw · Noise · Track with the carrier-track picker and Pre / Post in `src/managed/Nota.App/DeviceCards/Bodies/VocoderDeviceBody.cs`; graph empty state in `src/managed/Nota.App/Controls/VocoderGraph.cs`.
- [X] T044 [US2] Presets (Saw default; Talk box and Strings choir use Track) in `FactoryPresetCatalog.cs`; MCP descriptions in `DeviceTools.cs`.
- [X] T045 Smoke tests rewritten for the model in `tests/Nota.SmokeTest/VocoderTests.cs`; spec, contracts, data model, quickstart, docs (EN + RU), FEATURES and CHANGELOG updated.

### Nota closing tasks *(PR gates)*

- [X] T032 Run `scripts/build.sh` until native + managed + smoke test are green (Release engine build, see BUILD.md).
- [X] T033 AGPL-3.0-only SPDX two-line header on `Vocoder.h`, `VocoderDeviceBody.cs`, `VocoderGraph.cs`, `VocoderTests.cs`.
- [X] T034 New dependency: N/A (none added).
- [X] T035 `CHANGELOG.md` `[Unreleased]` → Added: Nota Vocoder (issue #113).
- [X] T036 FEATURES.md via the `nota-features` skill; the effect's EN + RU page in `../nota-docs` via `nota-docs` (with a card screenshot); shortcuts: N/A.
- [X] T037 Smoke test extended: covered by T010, T011, T016, T019, T023, T027 — confirm all run in both `Program.cs` sites.

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none. T001 → T002 → T003 (same file / registration needs the class); T004, T005 parallel to them.
- **Foundational (Phase 2)**: after T003. T006 → T007 → T008 → T009 (same file); T010 after T003, parallel to T006–T009.
- **US1 (Phase 3)**: after Phase 2. T012 (engine) and T013 (graph) parallel; T014 after T013; T015 after T014; T011 after T012.
- **US2 (Phase 4)**: after Phase 2; engine task T017 touches `Vocoder.h`, so run after T012 to avoid edit conflicts; T018 after T014.
- **US3 (Phase 5)**: after Phase 2; T020 after T012 / T017 (same file); T021 after T014; T022 after T013.
- **US4 (Phase 6)**: after US1 (presets need the sound to calibrate); T024, T025 parallel.
- **Polish (Phase 7)**: after the stories wanted for the release.

### User Story Dependencies

- **US1 (P1)**: only Foundational.
- **US2 (P2)**: Foundational; shares `Vocoder.h` and the card with US1 but is testable alone (empty track + Saw).
- **US3 (P2)**: Foundational; testable with any carrier.
- **US4 (P3)**: US1 for meaningful presets and render checks.

### Parallel Opportunities

- T004, T005 alongside T001–T003.
- T010 alongside T006–T009.
- T013 (managed graph) alongside T012 (engine DSP); likewise T018 / T021 / T022 alongside T017 / T020.
- T024, T025 in parallel.
- Test tasks marked [P] are separate methods in `VocoderTests.cs`; if two agents work at once, give each its own partial-class file to avoid conflicts.

---

## Parallel Example: User Story 1

```bash
# Engine and managed sides of US1 at the same time:
Task: "T012 Implement process() for Carrier = Input in src/native/nota.engine/src/Vocoder.h"
Task: "T013 Create src/managed/Nota.App/Controls/VocoderGraph.cs"
```

---

## Implementation Strategy

### MVP First (User Story 1 Only)

1. Phase 1 + Phase 2 → the device loads and the band engine exists.
2. Phase 3 → a pad talks with a vocal; card with picker, graph, Mix / Gain.
3. **STOP and VALIDATE** with quickstart §2 and the smoke test.

### Incremental Delivery

1. MVP (US1).
2. US2 → internal carriers (usable without a synth track).
3. US3 → full shaping, the knob row and graph edges — the card now matches the mockup.
4. US4 → presets, MCP description, persistence checks.
5. Polish + PR gates.

---

## Notes

- [P] tasks = different files, no dependencies
- The user makes commits in Nota repos — leave changes uncommitted for review.
- Stop at any checkpoint to validate a story independently.
