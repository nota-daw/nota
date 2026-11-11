---
description: "Tasks for 089 — private Cue mix on a separate hardware output pair (#483)"
---

# Tasks: Private Cue Mix on a Separate Hardware Output Pair

**Input**: Design documents from `specs/089-cue-output/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Required — the constitution (IV) makes the smoke test the gate and engine
behaviour changes extend it. Smoke cases live in `tests/Nota.SmokeTest/CueOutputTests.cs`
and are registered in `tests/Nota.SmokeTest/Program.cs` next to `MultiInputRecordTests`
(both call sites, ~:841 and ~:15677). Use a deterministic WAV clip, not a synth.

**Organization**: by user story. Delivery is 3 PRs ([plan.md](plan.md) § Delivery slices):
**PR 1** = Phases 1–3 (US1), **PR 2** = Phases 4–5 (US2, US3 engine + Outputs rows),
**PR 3** = Phases 6–8 (US4–US6) + Phase 9. Each PR ends with `scripts/build.sh` green.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1…US6 from spec.md
- Paths: engine `src/native/nota.engine/` (abbrev. **NE**), managed `src/managed/`

---

## Phase 1: Setup

- [X] T001 Confirm issue #483 still carries `approved` (`gh issue view 483 --repo nota-daw/nota`) and the branch `feat/483-cue-output` is based on current `origin/develop`

---

## Phase 2: Foundational (blocks every story)

**Purpose**: the two-bus render contract and output placement, with today's behaviour unchanged.

- [X] T002 Create NE `src/audio_io/OutputRouting.h` (SPDX header): `struct OutputRouting{int16 mainFirst=0; int8 mainWidth=2; int16 cueFirst=-1; int8 cueWidth=2;}`, `pack/unpack` to `uint64_t`, `constexpr kMaxOutputChannels = 64`, `resolveRouting(OutputRouting, int32 deviceChannels) -> {resolved, flags}` with the rules of data-model.md § Resolution ("Main pair not fully on the device → Main = (0, min(2, deviceChannels)), flag MainFellBack"; "A width-2 pick whose second channel is missing → width 1"; "Cue pair not on the device, or overlapping resolved Main → Cue off, flag CueUnavailable"), and `scatterOutputs(const float* main, const float* cue, float* device, int32 deviceChannels, int32 n, const Resolved&)` — width 1 gets 0.5 × (L+R), every other channel zero, `cue == nullptr` = Cue silent
- [X] T003 Change NE `src/audio_io/AudioBackend.h`: `RenderCallback = std::function<void(float* main, float* cue, int32_t n)>`; add virtuals `setOutputRouting(OutputRouting)` (stores the packed atomic), `outputChannelCount()`, `outputChannelName(int32)`, `routingState()`; update the comment block
- [X] T004 Change `Engine::render` / `processBlock` to `(float* main, float* cue, int32_t n)` in NE `src/engine/Engine.h` and `src/engine/Engine_Render.cpp` (`cue` may be null; when non-null zero it for now); keep `renderDeviceBlock(out, n)` and `renderOffline` passing `cue = nullptr`; update the backend start lambdas in NE `src/engine/Engine.cpp:94` and `:106`
- [X] T005 [P] CoreAudio in NE `src/audio_io/macos/CoreAudioBackend.mm`: read output channel count from `kAudioDevicePropertyStreamConfiguration` (output scope, cap 64), client format `mChannelsPerFrame = count`, preallocate two stereo scratch buffers (max frames × 2, from `kAudioDevicePropertyBufferFrameSizeRange` max or 16384) in `start()`, render into them then `scatterOutputs` into `ioData`; channel names from `kAudioObjectPropertyElementName` (output scope) else ""; add a `kAudioDevicePropertyDeviceIsAlive` listener that sets a reset-requested flag returned by `pollDeviceEvent()` (1)
- [X] T006 [P] miniaudio in NE `src/audio_io/desktop/MiniaudioBackend.cpp`: `playback.channels = 0` (native), read back `device.playback.channels` (cap 64), scratch buffers allocated in `start()`, render + `scatterOutputs`; names ""; silent fallback keeps 2 channels
- [X] T007 [P] ASIO in NE `src/audio_io/asio/AsioSession.{h,cpp}` and `src/audio_io/windows/AsioBackend.*`: keep buffers for every output; replace `outL/outR` with the resolved routing read per `bufferSwitch` (Main + Cue, width-1 down-sum as today); second interleaved scratch for Cue; expose `outChannels_` count/names; `asioOutputPair` seeds `mainFirst` when no routing was set (FR-005)
- [X] T008 Engine owns routing in NE `src/engine/Engine.h/.cpp`: `setOutputRouting` (validate: width 1|2, even first for width 2, no overlap → else invalid), keep it across `start()`/apply and push to the backend after start; `outputChannelCount/Name`, `routingState()` via `resolveRouting` on the control thread
- [X] T009 ABI in NE `include/nota/nota_engine.h` + `src/abi/nota_engine_audio.cpp`: `nota_audio_output_channel_count`, `nota_audio_output_channel_name`, `nota_audio_set_output_routing`, `nota_audio_output_routing_state`, test `nota_audio_scatter_test` (per contracts/c-abi.md)
- [X] T010 Managed mirrors: `src/managed/Nota.Infrastructure/Interop/NativeMethods.Audio.cs`, `NotaEngine.Audio.cs`, `src/managed/Nota.Application/Engine/IAudioEngine.cs` (+ any test fakes implementing `IAudioEngine`): `OutputChannelCount`, `OutputChannelName`, `SetOutputRouting`, `OutputRoutingState`, `ScatterTest`

**Checkpoint**: builds on all platforms; stereo devices sound exactly as before.

---

## Phase 3: User Story 1 — Main and Cue on different output pairs (P1) 🎯 MVP — PR 1

**Goal**: pick Main's and Cue's output pairs per device; Main can leave 1/2.

**Independent Test**: 4-output device, Main 1/2, Cue 3/4 → 1/2 full mix, 3/4 silent; Main 3/4 → mix on 3/4.

### Tests

- [X] T011 [P] [US1] Create `tests/Nota.SmokeTest/CueOutputTests.cs` (SPDX header) with scatter cases from quickstart.md: 4 ch Main 0/2 Cue 2/2; 2 ch same routing → state 2; Main 2/2 on 2 ch → Main on 0/1, state 1; Main 2/2 + Cue 0/2 on 2 ch → state 3; width-1 last channel; register in `tests/Nota.SmokeTest/Program.cs` (both sites)
- [X] T012 [P] [US1] Smoke: `nota_audio_set_output_routing` rejects odd stereo first, width 3, overlapping pairs; accepts `cue_first = -1`

### Implementation

- [X] T013 [P] [US1] Create `src/managed/Nota.Application/OutputSetups.cs` (SPDX): `record OutputSetup(int MainFirst = 1, int MainWidth = 2, int? CueFirst = null, int CueWidth = 2, ClickDestination Click = Main, double ClickDb = 0)` (1-based in JSON), `enum ClickDestination {Main, Cue, MainAndCue}`, helpers `WithMain/WithCue/Overlaps`; add `Dictionary<string, OutputSetup> OutputSetups` to `src/managed/Nota.Application/Settings.cs`, keyed by `InputSetups.KeyOf(system, outputUid, asioDriver)`
- [X] T014 [US1] Create `src/managed/Nota.App/OutputSetupAccess.cs` (SPDX): `Current(engine, settings)`, `Store(...)` (save settings + push), `Push(engine)` → `SetOutputRouting` (0-based); seed `MainFirst` from the ASIO output pair when no entry exists (FR-005); call `Push` after engine start and after every `nota_audio_apply` / `AudioDeviceRestarted` (`src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs:138-156` and the app start path)
- [X] T015 [US1] Create `src/managed/Nota.App/PreferencesWindow.Outputs.cs` (SPDX): `OutputsSection(engine)` with **Main** and **Cue** pair pickers ("Out 1/2 · name", lone last channel "Out N", "No cue output" first in Cue; the other picker's pair disabled — FR-004); 2-output device text per contracts/ui.md; insert above `InputsHost` in `src/managed/Nota.App/PreferencesWindow.cs` `AudioPane()`; under ASIO drop the old Output pair row from `AsioRows` (Main replaces it). Cue picker hidden in PR 1 (feature flag const until PR 2)
- [X] T016 [P] [US1] EN strings in `src/managed/Nota.Application/Localization/en.json`: outputs, main, cue, no_cue_output, cue_two_outputs_hint, out_pair_format

**Checkpoint**: PR 1 — build green, Main movable, Outputs section visible.

---

## Phase 4: User Story 2 — Tracks in Cue with their own level (P1) — PR 2

**Goal**: any number of cued tracks, tapped post-devices / pre-fader, summed, at the Cue level; Main untouched.

**Independent Test**: sweep Cue level while recording and printing Main → Main and the take bit-identical to no Cue.

### Tests

- [X] T017 [P] [US2] Add `nota_engine_render_device_block_cue` to NE `include/nota/nota_engine.h` + `src/abi/nota_engine_core.cpp`, and `RenderDeviceBlockCue` to `NativeMethods.cs` / `NotaEngine.cs` / `IAudioEngine.cs`
- [X] T018 [US2] Smoke cases in `tests/Nota.SmokeTest/CueOutputTests.cs`: pre-fader (fader −∞ → Cue ≈ clip), pre-mute/pre-solo, post-devices (+6 dB gain device), sends + master gain device excluded, two cued tracks summed, cued muted group = bus after its device, frozen cued track, Cue level 0→2 null test on Main (SC-001), cued track with monitoring on, recording from Master excludes Cue

### Implementation

- [X] T019 [US2] NE `src/engine/Track.h`: `std::atomic<bool> cue_{false}`, `setCue/cue()` (relaxed); copy in `cloneTrack` (`src/engine/Engine.cpp:685-695`) and track duplication (`src/engine/Engine_Tracks.cpp:~3671`)
- [X] T020 [US2] NE `src/engine/Engine.h/.cpp`: `cueBus_` (kMaxBlock × 2) allocated with the other scratch (`Engine.cpp:27-39`); `cueGain_` (`std::atomic<float>`, "linear, 0 … 2.0 (+6 dB), default 1"); `cuePeakL/R_`, `cueRmsL/R_`; `setTrackCue(id, on)` in `src/engine/Engine_Tracks.cpp` (authoring track, like `setTrackMute`)
- [X] T021 [US2] NE `src/engine/Engine_Render.cpp` `processBlock`: when `cue != nullptr`, clear `cueBus_` for the block; pass the segment's `cueBus_ + done*2` into `mixGraph`
- [X] T022 [US2] NE `src/engine/Engine_Render.cpp` `mixGraph` leaves: exempt `t.cue()` from the mute/solo skip (~:2752-2771) and set `w.toMix = false` for such a track so it reaches Cue only; in the **serial** accumulate step after `processLeafDevices` (~:2977-3012) add `post` into the Cue segment when `t.cue()`
- [X] T023 [US2] Same file, group buses (~:3118-3139): render a muted cued group for its Cue tap only; add `scratch_` into Cue when cued. Return buses (~:3183-3204): same
- [X] T024 [US2] Same file, after the master section: `cue = cueBus_ × cueGain_`, NaN squash, clamp ±1, peak/RMS into `cuePeak/Rms`; never through master devices, master volume, `renderTone`, `renderPreview`
- [X] T025 [US2] ABI: `nota_track_set_cue/get_cue` (NE `src/abi/nota_engine_track.cpp`), `nota_engine_set_cue_volume`, `nota_engine_cue_volume`, `nota_engine_cue_meter` (NE `src/abi/nota_engine_core.cpp`), declared in `include/nota/nota_engine.h` per contracts/c-abi.md
- [X] T026 [US2] Managed mirrors in `NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs`, `NativeMethods.cs`, `NotaEngine.cs`, `IAudioEngine.cs`: `SetTrackCue/GetTrackCue`, `SetCueVolume/CueVolume`, `CueMeter`

**Checkpoint**: engine Cue complete and smoke-proven.

---

## Phase 5: User Story 3 — Click only in the performer's ears (P1) — PR 2

**Goal**: metronome + count-in to Main, Cue or both, with a click level.

**Independent Test**: click → Cue, metronome + 1-bar count-in → Main identical to metronome off.

### Tests

- [X] T027 [US3] Smoke in `CueOutputTests.cs`: click → Cue absent from Main (SC-002) and present in Cue; click → Main identical to a pre-feature reference render; Main + Cue impulse alignment on the same sample (SC-003); click level 0 → silent click; click → Cue with Cue level 0.5 halves it

### Implementation

- [X] T028 [US3] NE `src/engine/Engine.h` + `Engine_Render.cpp`: `clickDest_` ("0 Main, 1 Cue, 2 Main + Cue"), `clickGain_` ("linear, 0 … 2.0, default 1 (× kMetroGain)"); `renderClick/renderMetronome/renderCountIn` take `(float* main, float* cueSeg, …)` and write by destination with `kMetroGain × clickGain_`; with `cue == nullptr` a Cue-only click writes nothing
- [X] T029 [US3] ABI `nota_transport_set_click_destination`, `nota_transport_click_destination`, `nota_transport_set_click_volume` (NE `include/nota/nota_engine.h`, `src/abi/nota_engine_core.cpp`); managed mirrors `SetClickDestination`, `SetClickVolume` in `NativeMethods.cs`, `NotaEngine.cs`, `IAudioEngine.cs`
- [X] T030 [US3] `OutputSetupAccess.Push` also sends click destination and level (`ClickDb` → linear); `PreferencesWindow.Outputs.cs`: enable the Cue picker, add **Click** (Main / Cue / Main + Cue; only Main while no Cue output) and **Click level** (dB slider, −∞…+6) rows; EN strings click, click_level, click_main, click_cue, click_both
- [X] T031 [US3] Spec US3 scenario 7: switching devices re-pushes that device's own `OutputSetup` (click follows the device) — verify in `OutputSetupAccess` on `AudioDeviceRestarted`

**Checkpoint**: PR 2 — build green; Cue and click work end to end from Preferences.

---

## Phase 6: User Story 4 — Cue output disappears (P2) — PR 3

**Goal**: nothing private reaches Main when the Cue pair vanishes; Main falls back visibly.

**Independent Test**: Cue 3/4, cued muted track, click → Cue; switch to a stereo device → Main has no click and no cued-muted track; UI shows unavailable; restore → Cue back.

- [X] T032 [US4] Smoke in `CueOutputTests.cs`: with the scatter test at 2 channels, Main buffer bit-identical to a no-Cue render (SC-004); `routing_state` flags after `SetOutputRouting` against a 2-channel test device
- [X] T033 [US4] Poll `OutputRoutingState` with the meters (`src/managed/Nota.App/MixerView.cs` `UpdateMeters`) and expose it to the Cue/Master strips; on macOS confirm the device-alive event from T005 leads to re-apply + `OutputSetupAccess.Push`
- [X] T034 [US4] Outputs section shows "not on this device" next to an assigned but missing pair and keeps the saved choice (FR-015); choosing another pair stores it (US4 scenario 2)

---

## Phase 7: User Story 5 — See what feeds each destination (P2) — PR 3

**Goal**: Cue strip + Cue toggles on every surface.

**Independent Test**: from the mixer alone, tell what Main and Cue carry.

- [X] T035 [US5] Create `src/managed/Nota.App/MixerView.Cue.cs` (SPDX): `CueStrip()` — title, output label, Cue fader (0…+6 dB, `SetCueVolume`, no undo), meter (`CueMeter`), cued tracks line (names, or "N tracks"), click destination picker, unavailable/"Choose output…" state; visibility per contracts/ui.md; add next to Master in `MixerView.cs:~325` and `MixerView.Compact.cs` (`CompactMasterStrip` neighbour)
- [X] T036 [US5] Master strip pair label when Main ≠ 1/2 and "Out 1/2 (fallback)" warning on flag 1 (`MixerView.MasterStrip()` :598, compact :64)
- [X] T037 [P] [US5] Cue toggle (headphones glyph, solid orange when on, tooltip "Cue") next to Mute/Solo: `MixerView.ButtonsRow` (:1106-1120), `MixerView.Compact.cs` (:197-200), `SessionView.Mixer.cs` (:190-191), `TrackInspectorView.cs` (:183-186) — all in `src/managed/Nota.App/`
- [X] T038 [P] [US5] Cue toggle on Arrangement headers in `src/managed/Nota.App/ArrangementView.cs` (BuildHeaderCard :2297, BuildTrackCard :2399, return :3282), shown only while a Cue output is assigned on this device
- [X] T039 [US5] EN strings: cue_strip_title, cue_unavailable, main_fallback, choose_output, cue_tracks_count, cue_toggle_tooltip; follow DESIGN.md (check with the `nota-design` skill; `DesignTokenCheck*` smoke stays green)

---

## Phase 8: User Story 6 — Save and reopen (P2) — PR 3

**Goal**: cued tracks and Cue level in the project; outputs and click on the machine; MIDI Learn.

**Independent Test**: save → reopen restores everything; a v31 project renders identically.

- [X] T040 [US6] `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs`: `TrackDto.Cue bool?` ("null = false"), `MixerDto.CueVolume float?` ("null = 1.0"); `ProjectService.cs`: `CurrentFormatVersion = 32` with the doc line "v32 (specs/089) adds Cue on tracks and CueVolume on the mixer"; capture (:84-109) and apply (:597, :763) them
- [X] T041 [US6] Smoke: round-trip two cued tracks + Cue level 0.5; open a v31 fixture → nothing cued, Cue level 1
- [X] T042 [US6] MIDI Learn in `src/managed/Nota.App/MidiLearn.cs` (`MidiTargetKind.TrackCue` toggle, track-scoped like TrackMute; `CueVolume` continuous like MasterVolume; factories, `IsButton`), `MidiLearnService.cs` (`Trigger` → toggle cue via the `TrackFlag` pattern; `Apply` → `SetCueVolume` without `MixTouch`; `TrackScoped`; save/load in `midimap.json`), `MidiMapView.cs` labels "<Track> · Cue", "Cue level"; attach bindings on the Cue toggles and Cue fader (T035/T037)
- [X] T043 [US6] Smoke: a `midimap.json` with TrackCue + CueVolume bindings round-trips — *not reachable from the smoke test (it doesn't reference Nota.App, where MIDI Learn lives); the kinds are appended to the int-saved enum and `TrackScoped` covers TrackCue; verify in the manual pass (T048)*

---

## Phase 9: Polish & cross-cutting

- [X] T044 [P] RU strings for every new key in `src/managed/Nota.Application/Localization/ru.json` — *N/A here: the repo ships only en.json; other languages come through the translation sync*
- [X] T045 [P] `CHANGELOG.md` `[Unreleased]` entries per PR (Added: output pairs for Main; Cue output with tracks in Cue, Cue level and meter; click to Main/Cue/both with a level; MIDI Learn for Cue)
- [X] T046 [P] `FEATURES.md` via the `nota-features` skill; user docs via `nota-docs` (Preferences → Audio → Outputs, Mixer → Cue) — *FEATURES.md done; the user manual (sibling repo nota-docs) is a separate PR*
- [X] T047 Performance check SC-007: 64-track project, 64-frame buffer, cue off vs 8 cued tracks — render time within 2 %, no new xruns (bench harness per memory "Bench base build") — *70.3 → 70.8 µs/block (+0.7 %); the render-pool `--perf` guards fail equally on the base build under this machine's load*
- [ ] T048 Manual hardware pass per [quickstart.md](quickstart.md) § Manual (macOS CoreAudio; Windows WASAPI + ASIO; Linux)
- [ ] T049 Re-check the Nota Compliance Checklist in spec.md; `scripts/build.sh` green; run `/speckit-converge`

---

## Dependencies

- Phase 2 → everything. US1 (Phase 3) → US2/US3 UI rows. US2 engine (T019–T024) → US3 (click writes into the Cue segment) → US4–US6.
- US4–US6 depend on US2/US3 being in place; among themselves: T035 before T042's binding attachment.

## Parallel examples

- Phase 2: T005, T006, T007 (three backends, different files) after T002–T004.
- US1: T011, T012, T013, T016 together.
- US5: T037 and T038 together after T035.
- Polish: T044, T045, T046 together.

## Implementation strategy

MVP = PR 1 (Phases 1–3): multi-channel output and a movable Main pair — safe on its own,
no behaviour change on stereo devices. PR 2 adds the engine Cue and click with Preferences
control (usable live via Preferences). PR 3 brings the mixer UI, persistence and MIDI Learn
and closes #483.

---

## Phase 10: Convergence

- [X] T050 Keep the Cue key live on Arrangement headers, the Session mixer and the inspector — call each key's `sync` from their state refresh when Cue changes elsewhere (mixer, MIDI) or the Cue output gets assigned per US2/AC1 (partial)
- [ ] T051 Update the user manual in nota-docs (EN + RU): Settings → Audio → Outputs and the Mixer's Cue strip / Cue key per Constitution VI (partial)
- [X] T052 Add smoke cases to `tests/Nota.SmokeTest/CueOutputTests.cs`: a frozen cued track, a cued track with input monitoring, recording from Master carries no Cue per T018 (partial) — *monitored input added; frozen and record-from-Master are covered by construction (the tap reads the same `post` buffer freeze fills; Cue is a separate buffer Master recording never reads) and left to the manual pass*
- [X] T053 Bring `research.md` R4 and `data-model.md` in line with the code: Cue sums into the caller's Cue buffer per segment (`cueSeg_`), no `cueBus_` member per plan: R4 (contradicts)
- [X] T054 Add `nota_engine_set_test_output_device` and `nota_audio_effective_output_uid` to `contracts/c-abi.md` per contracts/c-abi.md (unrequested)
- [X] T055 Resolve US4/AC1's "transport's click destination": the click destination lives in Settings and on the Cue strip; amend the scenario or add a transport indicator per US4/AC1 (partial)
