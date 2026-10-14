---

description: "Task list for 008 Stereo Map (MVP)"
---

# Tasks: Stereo Map (MVP)

**Input**: Design documents from `/specs/008-stereo-map/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md);
specs 006 (Stereo pan law) and 009 (volume/pan undo) implemented.

**Tests**: Requested — FR-021 lists the smoke-test coverage of the measurements, and the
constitution (IV) requires it for an engine change. UI behaviour is verified with the
headless harness (quickstart § 2).

**Organization**: US1 see the real picture (P1), US2 drag to place (P1), US3 inspect and
select (P2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 / US2 / US3

---

## Phase 1: Setup

- [X] T001 Run `scripts/build.sh` on the current tree and confirm it is green (baseline); if red, stop and report

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the engine measures, the measurements reach C#, and the Mixer window can show a
(still empty) map view.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete

- [X] T002 In `src/native/nota.engine/src/Track.h` add `std::atomic<float> stereoL_{0.0f}, stereoR_{0.0f}, stereoLR_{0.0f};` with `void setStereo(float l, float r, float lr)` and getters (comment: running averages of L², R², L·R of the post-fader post-pan signal, τ = 0.3 s, specs/008); in `src/native/nota.engine/src/Engine.h` add the master counterparts `masterStereoL_`, `masterStereoR_`, `masterStereoLR_` (audio thread writes, message thread reads)
- [X] T003 In `src/native/nota.engine/src/Engine_Render.cpp` (research R1): in the track stage loop (`3) Channel fader + …pan`) and the return stage loop add `double sLR = 0.0; … sLR += l * (double)r;` next to `sqL`/`sqR`; after each loop update the track's averages with `a = 1 − exp(−frames / (0.3 · sr))` (`sr` = the block's sample rate; one `exp` per segment) from the block means `sqL/frames`, `sqR/frames`, `sLR/frames` — a `!toMix` track (muted / solo-gated) feeds zeros; in the master loop (after master volume, ~`masterPeakL_.store`) do the same into the master atomics. Groups untouched. No allocation, no lock
- [X] T004 Add the C ABI from [contracts/c-abi-stereo-measures.md](contracts/c-abi-stereo-measures.md) verbatim to `src/native/nota.engine/include/nota/nota_engine.h` and implement `nota_engine_stereo_measures` in `src/native/nota.engine/src/nota_engine_core.cpp`: rows for the authoring graph's non-group tracks in track order, then the master (`kMasterTrackId`); derive per research R2 — `level_db = 10·log10((eL+eR)/2)` (−200 when ≤ 1e-20), `balance = (4/π)·atan2(√eR, √eL) − 1` (0 when silent), `correlation = eLR/√(eL·eR)` (0 when either < 1e-12), `width = clamp(1 − correlation, 0, 1)`; return the total row count, fill up to `cap`
- [X] T005 [P] In `src/managed/Nota.Application/Engine/EngineContracts.cs` add `[StructLayout(LayoutKind.Sequential)] public struct StereoMeasure { public int TrackId; public float LevelDb, Balance, Width, Correlation; public bool Silent => LevelDb < -60f; }`; in `IAudioEngine.cs` add `int GetStereoMeasures(StereoMeasure[] buffer)` (fills, returns the total); P/Invoke in `src/managed/Nota.Infrastructure/Interop/NativeMethods.cs` and the wrapper in `NotaEngine.cs`
- [X] T006 [P] In `src/managed/Nota.Application/Settings.cs` add `public string MixerView { get; set; } = "strips";` with a comment ("strips" | "map", specs/008, remembered per user)
- [X] T007 Create `tests/Nota.SmokeTest/StereoMeasureTests.cs` (SPDX; comment "Stereo measurements for the Stereo Map (specs/008-stereo-map); also `--stereomap`") with WAV helpers (reuse the pattern of `PanLawTests`: 48 kHz Float32 WAVs, `RenderOffline(buf, frames, 48000)`), wired in `tests/Nota.SmokeTest/Program.cs` (full run after `MixUndoTests`, plus a `--stereomap` branch)
- [X] T008 In `src/managed/Nota.App/MixerWindow.cs` switch to an own 36 px title bar (`base(36)`, as `DeviceWindow`): "Mixer" caption left, the shell segmented control **Strips | Stereo Map** (`ClipEditorKit.Segments`) centred; Linux: the same row at the top of the body. Expose an event/callback the hosting `MainWindow` wires to `MixerView.View`
- [X] T009 In `src/managed/Nota.App/MixerView.cs` add `public enum MixerViewMode { Strips, StereoMap }` and a `View` property that rebuilds the content: Strips → the strips scroller alone; StereoMap → a `Grid` (`*, Auto`, gap 8, padding 8) with a `StereoMapView` placeholder (min 520 px) and the strips scroller capped at 440 px; the footer stays. In `src/managed/Nota.App/MainWindow.Mixer.cs` / `MainWindow.axaml.cs`: initialise `View` from `Settings.MixerView`, persist changes, and keep the title-bar segments in sync
- [X] T010 Create `src/managed/Nota.App/StereoMapView.cs` (SPDX; file comment) as a custom `Control` with the panel frame, header "STEREO MAP", the plane well and the grid per [contracts/ui-stereo-map.md](contracts/ui-stereo-map.md) (vertical lines L100 / L50 / C / R50 / R100; horizontal lines at the `VFader` positions of the fader top, 0, −6, −12, −24 dB on the linear-gain 0…1.5 scale — reuse the fader's value↔y mapping in one shared helper), the bottom row (empty correlation bar, hint text), and a `RequestAnimationFrame` loop that runs only while attached and visible

**Checkpoint**: build green; the Mixer switches between Strips and an empty Stereo Map; the engine reports measurements.

---

## Phase 3: User Story 1 — See where every track really sits (Priority: P1) 🎯 MVP

**Goal**: handles and measured clouds for every track, return and the master; mute / solo /
silence states; master correlation.

**Independent Test**: quickstart § 2 steps 1, 2 and 4.

### Tests for User Story 1

- [X] T011 [US1] In `tests/Nota.SmokeTest/StereoMeasureTests.cs` add every Guarantees row of [contracts/c-abi-stereo-measures.md](contracts/c-abi-stereo-measures.md) (write before T013 and confirm they fail): mono 440 Hz at L50 under Stereo → balance −0.50 ± 0.05, width < 0.05, correlation > 0.95; L 440 / R 1 kHz at C → balance 0 ± 0.05, width > 0.95, |correlation| < 0.05; L = −R → correlation < −0.95, width = 1; silence → level < −60; that pair as the only track → master row within ± 0.05 of the track row; muting a track → its level < −60 within 1.5 s of rendering. Render ≥ 1.5 s before reading
- [X] T012 [US1] In `tests/Nota.SmokeTest/StereoMeasureTests.cs` add the cost check (SC-004): render 3 s with 64 audio tracks of the unrelated pair three times and take the best; compare with the best of three renders of the same project before T003's averages — if a clean A/B is not possible without a switch, assert the time is within the run-to-run spread of two plain renders and print both numbers

### Implementation for User Story 1

- [X] T013 [US1] Make T011 pass in `src/native/nota.engine/src/Engine_Render.cpp` / `nota_engine_core.cpp` (fix the averages / derivation as the tests show)
- [X] T014 [US1] In `src/managed/Nota.App/StereoMapView.cs` draw per frame (contract): for every non-group track and return a handle at (pan, fader position) in its colour (`ArrangementView.TrackBrush(EffectiveColorIndex)`), the master handle at (C, master volume); clouds from `GetStereoMeasures` (ellipse 22 px tall at the handle's Y, `balance ± width` on the pan scale, min 1.6 % wide, fill opacity `0.10 + 0.32 × clamp((level + 60) / 54, 0, 1)`, outline `Danger` when correlation < 0); no cloud when silent or muted; muted handle fill `SurfaceHover`; with any solo, non-soloed tracks (not returns or master) dimmed (cloud opacity × 0.35, handle `SurfaceHover`); draw order by level, master last
- [X] T015 [US1] In `StereoMapView.cs` draw labels (10 px medium, right of the handle, ≤ 84 px with ellipsis, colours per contract) with the greedy no-overlap placement of research R7, and the MASTER CORR bar (180 × 6 well, centre tick, bar from centre to the value, `Success` / `Danger`, value text)
- [X] T016 [US1] Headless harness (`/tmp/stereomapharness`, `NOTA_DATA_DIR`): new project + a mono WAV track panned L50 + a wide stereo WAV track, play, switch the Mixer to Stereo Map; assert handle positions, that the L50 cloud is centred within 2 % of the plane width of its handle and narrow, that the wide cloud spans > 90 % of the width, mute → no cloud and dimmed handle, solo → others dimmed, 20 tracks at C/0 dB → no overlapping labels; screenshots in both themes; record the frame time with 128 tracks (< 16 ms)

**Checkpoint**: US1 tests pass; the map shows the real picture.

---

## Phase 4: User Story 2 — Place tracks by dragging them (Priority: P1)

**Goal**: drag handles to change pan + volume as one undo step; resets; Shift lock.

**Independent Test**: quickstart § 2 step 3.

- [X] T017 [US2] In `src/managed/Nota.App/StereoMapView.cs` handle input: hit-test handles (16 px + 4 px slop, topmost first); pointer down on a non-master handle → `new MixGesture(engine, MixTarget.Volume(id), MixTarget.Pan(id)).Begin()` then `BeginAutomationWrite` for Volume and Pan; move → pan += dx · 2 / plotWidth, volume from the y delta via the shared fader mapping, clamped (pan −1…1, volume 0…1.5); Shift locks to the axis with the larger total movement so far; release → `EndAutomationWrite` ×2 then the gesture `End()`; the strips follow live (FR-014, FR-016, FR-017)
- [X] T018 [US2] In `StereoMapView.cs`: double-click a handle → pan 0 as a one-shot (`MixEdit.Step`), ⌥ + double-click → volume 1.0 one-shot; the master handle ignores drag and resets (FR-015)
- [X] T019 [US2] Headless harness: real mouse drag on a handle → pan and volume changed together, the Mixer strip shows them, one `Undo` restores both exactly; Shift-drag changes one axis only; double-click → C; ⌥ double-click → 0 dB, each one step; the master handle does not move

**Checkpoint**: dragging works and undoes as one step.

---

## Phase 5: User Story 3 — Inspect a track (Priority: P2)

**Goal**: hover tooltip with the numbers and notes; click selects app-wide; selection ring.

**Independent Test**: quickstart § 2 step 5; tooltip values.

- [X] T020 [P] [US3] In `src/managed/Nota.App/ArrangementView.cs` add `public void SelectTrackFromOutside(int trackId)` — `Select(trackId, -1)` then `TrackSelected?.Invoke(trackId)` (comment: used by the Stereo Map, specs/008)
- [X] T021 [US3] In `src/managed/Nota.App/StereoMapView.cs` add `Func<int> SelectedTrackId` and `Action<int> SelectTrack` callbacks; a click without movement on a handle (master included) → `SelectTrack(id)`; draw the 22 px `Accent` ring on the selected handle and draw it on top; wire both in `src/managed/Nota.App/MainWindow.axaml.cs` / `MainWindow.Mixer.cs` (`() => Timeline.SelectedTrackId`, `id => Timeline.SelectTrackFromOutside(id)`)
- [X] T022 [US3] In `StereoMapView.cs` the tooltip per [contracts/ui-stereo-map.md](contracts/ui-stereo-map.md): after 300 ms hover and not while dragging; rows PAN, VOLUME, BALANCE, WIDTH, CORRELATION, LEVEL (formats: "C" / "L40", "−3.0 dB" / "−∞", "below −60 dBFS"); notes "Image is off the pan" when `|balance − pan| > 0.15` (exact texts in the contract), "Muted", "Silent"; flipped near the edges
- [X] T023 [US3] Headless harness: click a handle → `Timeline.SelectedTrackId` is that track; select another track in the arrangement → the ring moves; hover a handle with a known measurement → the tooltip rows and the off-pan note show the expected values

**Checkpoint**: all three stories work.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T024 Compare screenshots of the Stereo Map view (both themes) with `nota-design/Nota Mixer v2.html` and fix spacing / token mismatches in `StereoMapView.cs` / `MixerView.cs` / `MixerWindow.cs`

### Nota closing tasks

- [X] T025 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T026 AGPL-3.0-only SPDX header on `StereoMapView.cs` and `StereoMeasureTests.cs`
- [X] T027 New dependency: N/A
- [X] T028 `CHANGELOG.md` `[Unreleased]` Added: "**Stereo Map:** the Mixer window's new **Stereo Map** view (switch in its title bar) shows every track, return and the master as a dot at its pan and fader, with a cloud for where its sound really is and how wide it is, plus the master correlation. Drag a dot to set pan and volume together (one undo step), double-click to centre, ⌥ double-click for 0 dB; hover for the numbers; click to select."
- [X] T029 FEATURES.md via `nota-features` (Tracks and mixer: Stereo Map); user docs EN + RU via `nota-docs` (Mixer page: "Stereo Map" section; the `mixer` shots scene gains a `view: map` arg; new shots in both themes); shortcuts: N/A (no new key bindings — mouse gestures documented on the page)
- [X] T030 Engine behaviour change → smoke test extended: confirm all `StereoMeasureTests` labels pass in the full run and with `--stereomap`

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T010)** → stories → **Polish (T024–T030)**
- T002 → T003 → T004 → T005; T006 ∥ T005; T007 after T005; T008 → T009 → T010
- **US1 (T011–T016)** needs Foundational; T011 before T013; T014 → T015 (same file); T016 last
- **US2 (T017–T019)** needs US1's drawing (handles) — T014
- **US3 (T020–T023)**: T020 ∥ US1/US2 work (different file); T021–T022 after T017 (same file)
- `StereoMapView.cs` tasks are sequential: T010 → T014 → T015 → T017 → T018 → T021 → T022

### Parallel Opportunities

- T005 ∥ T006 (Application files) while the engine work lands
- T020 (ArrangementView) alongside any `StereoMapView.cs` task
- Engine (T002–T004) and the Mixer window/layout (T008–T009) are independent until T010

## Parallel Example: Foundational

```bash
Task: "T002–T004 engine measurements + C ABI"
Task: "T008–T009 Mixer window title bar + view switch"
Task: "T006 Settings.MixerView"
```

## Implementation Strategy

### MVP

US1 alone already delivers the feature's main value (seeing the real stereo picture); US2
makes the map a mixing tool; US3 adds the numbers and selection. Ship all three in one PR
— the spec's MVP is the three together; V2/V3 follow as their own specs.

### Incremental order

1. T001–T010 → measurements + empty map view
2. US1 → clouds, handles, labels, correlation
3. US2 → dragging with undo
4. US3 → tooltip and selection
5. Polish → design pass, docs, final build

## Notes

- Do not commit or push — the user reviews and commits.
- `.specify/feature.json` points at `specs/007-midi-note-editing`; run spec-kit scripts for
  this feature with `SPECIFY_FEATURE_DIRECTORY=specs/008-stereo-map SPECIFY_FEATURE_NO_PERSIST=1`.

## Phase 7: Convergence

- [X] T031 Reconcile the red-outline threshold: the spec says "negative correlation" / "red below 0" (FR-009, FR-012, US1 scenario 5) while `src/managed/Nota.App/StereoMapView.cs` uses `PhaseAlarm = -0.05` so unrelated material near 0 doesn't flicker red — either record the tolerance in the spec via `/speckit-clarify` or set the threshold back to 0, per FR-009 / FR-012 (contradicts)
- [X] T032 Extend the headless Stereo Map harness: hover a handle with a known measurement and assert the tooltip rows (PAN, VOLUME, BALANCE, WIDTH, CORRELATION, LEVEL); a mono source whose measured balance differs from its pan by more than 0.15 shows "Image is off the pan"; a muted track shows "Muted", a silent one "Silent" — reading the drawn content through a test hook in `StereoMapView.cs` (e.g. the last tooltip's rows/notes kept like `_placedLabels`), per FR-019 / US3 scenarios 1–3 (partial)
- [X] T033 Extend the harness: with automation recording on and the transport playing, a map drag creates (or writes) the track's volume and pan lanes like a strip-control drag, and one ⌘Z removes the recorded lanes and restores pan and volume together, per US2 scenario 4 / FR-016 (partial)
- [X] T034 Record the SC-004 measurement in `specs/008-stereo-map/research.md` (A/B of the engine with and without the Stereo Map sums, 64 tracks: 169–170 vs 171–172 µs per 512-frame block, i.e. within noise) with how to repeat it, or add a budget check for 64 tracks to `tests/Nota.SmokeTest/StereoMeasureTests.cs`, per T012 / plan: Performance Goals (partial)
