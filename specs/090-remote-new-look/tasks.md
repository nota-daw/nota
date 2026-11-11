---

description: "Tasks for spec 090 — Nota Remote in the new look, with clip previews in Scenes"
---

# Tasks: Nota Remote in the new look, with clip previews in Scenes

**Input**: Design documents from `specs/090-remote-new-look/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/

**Tests**: the smoke test is the project's gate (constitution IV); the spec asks it to cover
the theme geometry and the clips message (Compliance checklist), so test tasks are included.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 look, US2 skins, US3 guard rails, US4 clip previews

## Path Conventions

- Phone page: `src/web/remote/`
- Hub: `src/managed/Nota.Remote/`; host: `src/managed/Nota.App/MainWindow.Remote.cs`
- Tests: `tests/Nota.SmokeTest/`

---

## Phase 1: Setup

- [X] T001 Capture "before" screenshots of every screen and overlay (phone 390×844, phone landscape 844×390, tablet 1024×1366; 4 built-in themes) from the current page via the mock (`src/web/remote/mock/nota-mock.mjs`, `npm run build && npm run mock`) into `/tmp/remote090/before/`, and record hit-area sizes and text contrast per screen (DOM measurement script) to `/tmp/remote090/before/metrics.json`

---

## Phase 2: Foundational (blocking)

- [X] T002 Add the design tokens to `src/web/remote/src/app.css`: `--r-card 9px; --r-group 7px; --r-field 6px; --r-cell 5px; --r-value-bar 4px; --r-switch 3px; --r-fine 2px` on `:root`; `--font-ui: Geist, system-ui, -apple-system, sans-serif`; `--picked-ink: var(--accent-bright)` under `:root, [data-theme='dark']` and `var(--accent-pale)` under `[data-theme='light']`; root `font-family: var(--font-ui)` and `font-variant-numeric: tabular-nums`; utilities `.live` (solid accent + ink-on-accent) and `.picked` (accent-soft + picked-ink)
- [X] T003 Remove Geist Mono: delete `src/web/remote/public/fonts/GeistMono-Medium.woff2`, its `@font-face` and the `.mono` rule in `src/web/remote/src/app.css`; replace every `class="mono"` / `.mono` use in `src/web/remote/src/**` with plain text (tabular figures are global) or a role class
- [X] T004 [P] Create `src/web/remote/src/lib/fmt.ts` (SPDX header): `db(v)` (one decimal, U+2212, `−∞`, `+` only when > 0), `pct(v)` (whole, thin space + `%`), `bpm(v)` (two decimals), `bars(beats)` ([n, unit]), `MINUS`, `THIN`; replace the helpers in `screens/Mixer.svelte`, `screens/Scenes.svelte`, `screens/XY.svelte`, `screens/Macros.svelte`, `components/Header.svelte`, `screens/BigTransport.svelte`
- [X] T005 Update `tests/Nota.SmokeTest/DesignTokenCheck.Remote.cs`: radii must be `0`, `50%` or contain `var(--r-` (replace the 0–4 px list); fail on `Geist Mono`, `monospace` or `class="mono"` in `src/web/remote/src`; require `--picked-ink` defined for both theme kinds in `app.css`; require `GeistMono` absent from `src/web/remote/public/fonts`

**Checkpoint**: tokens exist; the page still renders; build and smoke scan pass.

---

## Phase 3: User Story 1 — the phone looks like the desktop (P1) 🎯 MVP

**Goal**: every surface follows DESIGN.md (spec 038): corner scale, one typeface, orange in three strengths, sentence case, number format.

**Independent Test**: mock at phone / landscape / tablet in Studio Dark; every screen and overlay matches DESIGN.md (quickstart § 2).

- [X] T006 [US1] Restyle shared controls in `src/web/remote/src/app.css`: `.btn` (radius `--r-group`, `.btn.on` = picked: soft tint + `--picked-ink`, no solid), `.seg` (group `--r-group`, cells `--r-cell`, `.on` = picked), `.switch` (track `--r-switch`, knob `--r-fine`, solid accent when on), `.label` (sentence case, ink 3, 400 weight), `.learnable`
- [X] T007 [US1] Restyle `src/web/remote/src/App.svelte`: tabs and rail — current screen = picked (soft tint fill, `--picked-ink` text, rail gets the 2 × 14 orange tick like the desktop's left rail, tabs keep the edge line); docked bars square
- [X] T008 [P] [US1] Restyle `src/web/remote/src/components/Header.svelte`: track picker and transport keys `--r-group`; Play running and Record = live solid (record colour for Record); position readout well `--r-field`, open = picked edge; readout 500 weight; link status text; `fmt.bpm`
- [X] T009 [P] [US1] Restyle `src/web/remote/src/components/Sheets.svelte`: sheet / menu `--r-group` with popover; chosen row = picked (soft tint + orange tick); `who` chips `--r-cell`; field `--r-field` with 1 px accent focus outline; tonic buttons picked; Theme segment picked
- [X] T010 [P] [US1] Restyle `src/web/remote/src/components/Overlays.svelte` and `components/Banner.svelte`: covers docked, checklist `--r-card`, code cells `--r-field` with the typing cell's accent edge, primary action by weight + tone, count-in number live accent, toast `--r-group`, banner docked; text sentence case
- [X] T011 [US1] Restyle `src/web/remote/src/screens/Mixer.svelte`: strips docked; fader cap `--r-value-bar`; pan bar and fader slot grey at rest, while held soft tint + accent line (shell slider rule); dB / pan via `fmt`; M / S / Arm `--r-cell` with Mute / Solo / Record fills and derived ink; meter channels `--r-fine`; group tag chip
- [X] T012 [US1] Restyle `src/web/remote/src/screens/Macros.svelte`: grid frame `--r-card`; device picker `--r-group`; knob arc and active value text in the playing track's colour (`--tc` from `app.track.color`, neutral `--viz-ink` when no track), arc groove grey; values via `fmt.pct`
- [X] T013 [US1] Restyle `src/web/remote/src/screens/XY.svelte`: field `--r-card`; crosshair and point in the track colour (`--tc`), a quiet `--tc` tint layer (opacity, no `color-mix`) behind the point while live; axes panel `--r-group`; Hold / Return picked; Tilt `.btn.on` picked, the "Tilt is moving the point" pill live solid; automation chip `--r-cell`
- [X] T014 [P] [US1] Restyle `src/web/remote/src/screens/Pads.svelte`: pads `--r-field`; under finger = live accent; played by the track = track colour + derived ink; bank segment picked; Full velocity / Repeat picked; rate chip `--r-value-bar`
- [X] T015 [P] [US1] Restyle `src/web/remote/src/screens/Keys.svelte`: keyboard frame `--r-group` bottom, keys `--r-fine` bottom; scale cells and chords `--r-cell`; under finger live accent; track-played band / edge in track colour; mode segment and key button picked; octave readout
- [X] T016 [P] [US1] Restyle `src/web/remote/src/screens/BigTransport.svelte`: keys `--r-card`; Play running live accent, Record live record; Loop / Click on = picked (soft tint + orange icon/text); section buttons `--r-cell`, current = orange tick, looping = live solid; position readout weight 500, record colour while recording; tempo via `fmt.bpm`
- [X] T017 [P] [US1] Restyle `src/web/remote/src/screens/Empty.svelte`: card `--r-card`, sentence case
- [X] T018 [US1] Restyle the Scenes chrome in `src/web/remote/src/screens/Scenes.svelte` (cell layout itself in US4): scene buttons and Stop `--r-cell`, clip cells `--r-field`, empty slots sunken; playing / queued in clip colour with derived ink; recording record colour; selected-looking states never orange
- [X] T019 [US1] Sweep `src/web/remote/src/**` for labels: sentence case everywhere ("Tap tempo", "Full velocity", "Follow Nota"), acronyms kept (MIDI, XY, USB, dB, BPM); no letter-spacing; ranges with en dash

**Checkpoint**: US1 complete — MVP of #556.

---

## Phase 4: User Story 2 — the phone wears the user's skin (P1)

**Goal**: font and radii from a spec 087 skin reach the phone, live, in both modes.

**Independent Test**: wear a v2 skin with `radius.scale: 0` and a font family; the phone repaints square and in that face (or Geist).

- [X] T020 [US2] Extend `src/managed/Nota.Remote/RemoteContracts.cs`: `RemoteTheme(string Id, string Name, bool Dark, IReadOnlyDictionary<string,string> Colors, string? Font = null, IReadOnlyDictionary<string,double>? Radii = null)` — `Radii` "7 keys (`card … fine`), resolved px; null when the design scale"
- [X] T021 [US2] Fill them in `src/managed/Nota.App/MainWindow.Remote.cs` `RemoteThemeOf`: `Font = t.Geometry.FontFamily`; `Radii` = `SkinGeometry.Key(step) → t.Geometry.RadiusOf(step)` for every `RadiusStep` when any differs from `SkinGeometry.Design(step)`, else null; make the cache key include `Geometry` if `NotaColourTheme` equality does not
- [X] T022 [US2] Write `font` / `r` per theme in `ThemeJson` in `src/managed/Nota.Remote/RemoteHub.State.cs` per contracts/theme-message.md G1–G2
- [X] T023 [US2] Phone side: `ThemeMessage` types in `src/web/remote/src/lib/types.ts` (`font?: string; r?: Record<string, number>`); in `src/web/remote/src/lib/palette.ts` `cssVars` also emits `--font-ui` (sanitised family, quoted, then Geist stack) and `--r-*` (from `r`, else the design values, so a theme without them resets — G4); `lib/theme.svelte.ts` applies and stores them (index.html restores them generically from `nota-remote:vars`)
- [X] T024 [P] [US2] Mock: `?skin=<file>` in `src/web/remote/mock/nota-mock.mjs` loads a v2 theme file (font + radius scale / steps, resolved like `SkinGeometry.RadiusOf`) and sends `font` / `r`; add a sample skin `src/web/remote/mock/square-avenir.json`
- [X] T025 [US2] Smoke test in `tests/Nota.SmokeTest/RemoteTests.cs`: a theme with `Font` and `Radii` sends `font` and all seven `r` keys; a built-in sends neither; a geometry-only change re-sends the theme

**Checkpoint**: US1 + US2 done — #556 complete.

---

## Phase 5: User Story 4 — clip previews in Scenes (P2, #557)

**Goal**: each filled cell shows name, length and notes / waveform like the desktop slot, updated on change, growing while recording.

**Independent Test**: quickstart § 3.

- [X] T026 [US4] Add `string ClipColor(int trackId, int scene)` to `IRemoteHost` in `src/managed/Nota.Remote/RemoteContracts.cs` and implement it in `src/managed/Nota.App/MainWindow.Remote.cs` (clip's own palette colour via `TryGetSessionClipProps`, else the track's — as `SessionView.ClipBrush`); update the test host in `tests/Nota.SmokeTest/RemoteTests.cs`
- [X] T027 [US4] Create `src/managed/Nota.Remote/RemoteHub.Clips.cs` (SPDX header): `ClipPreview` per data-model.md ("Notes ≤ 128, 1/64-of-length grid"; "Peaks byte[48] 0–99"); build from `GetSessionNotes` (instrument tracks) or `GetSessionSlotPeaks(…, 48)` (audio), `AudioRecordPeaksOf` / bars-so-far while recording (state 4 / 5); name `GetSessionClipName` else "<track> <scene+1>"; FNV-1a signature; round-robin ≤ 24 slots per tick while any phone is on Scenes; state/length changes and recording slots (≤ 4 Hz) jump the queue
- [X] T028 [US4] Send `clips` in `Push` (`src/managed/Nota.Remote/RemoteHub.State.cs`) per contracts/clips-message.md C1–C6: per-phone `SentClips` signatures on `RemoteClient` (`src/managed/Nota.Remote/RemoteClient.cs` or where the client class lives), cleared when the phone leaves Scenes; full set in batches ≤ 64 slots (first `all: true`); `k: "x"` for slots emptied
- [X] T029 [US4] Phone store: `ClipsMessage` / `Preview` types in `src/web/remote/src/lib/types.ts`; `clips` handler in `src/web/remote/src/lib/app.svelte.ts` keeping `clips: Map<"id:i", Preview>` (cleared on `all: true`, deleted on `k: "x"`)
- [X] T030 [US4] Create `src/web/remote/src/components/ClipPreview.svelte` (SPDX header): DPR-aware canvas; notes as 3 px bars over the clip's own pitch range, x by start/len ÷ 64; peaks as centred bars 62 % of a step wide; ink from a prop (CSS var resolved via computed style), redraw on data / size / ink / theme change only (ResizeObserver)
- [X] T031 [US4] Scenes cell layout in `src/web/remote/src/screens/Scenes.svelte`: top row clip name (ellipsis) + length; preview below in clip colour (stopped), `--tc-ink` (playing / queued), record colours (recording); play mark and progress line on top; use the clip colour from `clips` for `--tc`
- [X] T032 [P] [US4] Mock: `src/web/remote/mock/nota-mock.mjs` sends a `clips` message on Scenes with demo notes and peaks, and a growing recording slot
- [X] T033 [US4] Smoke test in `tests/Nota.SmokeTest/RemoteTests.cs`: a phone on Scenes gets `clips` for filled slots (notes for an instrument clip, 48 peaks for audio), none on another screen, none re-sent while unchanged, a note edit re-sends just that slot, notes capped at 128

**Checkpoint**: #557 complete.

---

## Phase 6: User Story 3 — guard rails (P2)

**Goal**: nothing smaller, nothing less readable.

- [X] T034 [US3] Capture "after" screenshots and metrics (same script and sizes as T001, plus the sample skin) into `/tmp/remote090/after/`; compare hit areas (≥ before, ≥ 36 px short side) and contrast (≥ before) per screen; fix regressions in the affected component

---

## Phase 7: Polish & cross-cutting

- [X] T035 Run `npm run check` and `npm run build` in `src/web/remote`; fix every error and warning
- [ ] T036 Run `scripts/build.sh`; smoke test green
- [X] T037 [P] `CHANGELOG.md` `[Unreleased]`: Changed — Nota Remote in the new look, wears skins' typeface and corners (#556); Added — clip previews in Remote Scenes (#557)
- [ ] T038 [P] FEATURES.md via the `nota-features` skill; user docs (Remote page EN + RU) via the `nota-docs` skill
- [ ] T039 Real-hub check (quickstart § 3) with the desktop side by side; Scenes with previews screenshots for the PR

---

## Dependencies & Execution Order

- Setup (T001) → Foundational (T002–T005) → US1 (T006–T019) → US2 (T020–T025) can start after T002 (independent of US1 screens) → US4 (T026–T033) after T018 → US3 (T034) after US1, US2, US4 → Polish.
- Within US1: T006 before T007–T018 (shared classes); T011–T013, T018 touch one file each.
- Within US4: T026 → T027 → T028; T029 → T030 → T031.

## Parallel Opportunities

- T004 with T002 / T003.
- US1: T008, T009, T010, T014, T015, T016, T017 in parallel after T006.
- US2 hub side (T020–T022) in parallel with US1 page work; T024 any time.
- US4: hub (T026–T028) in parallel with phone (T029–T031); T032 any time.

## Implementation Strategy

1. MVP: Phases 1–3 (the look) — demonstrable in the mock.
2. US2 (skins) — #556 done.
3. US4 (previews) — #557 done, separate commit.
4. Guard rails, polish, evidence, one PR closing both issues.

## Nota Compliance (re-check)

- [ ] SPDX header on `fmt.ts`, `ClipPreview.svelte`, `RemoteHub.Clips.cs`, mock skin file
- [ ] No new dependency; Geist Mono removed
- [ ] No audio-thread or C ABI change
- [ ] Smoke test extended (T005, T025, T033)
- [ ] CHANGELOG / FEATURES / docs (T037, T038)
- [ ] DESIGN.md followed

## Phase 8: Convergence

- [X] T040 Clamp every skinnable radius on small parts so a 2× skin never turns a part into a pill: wrap `var(--r-*)` in `min(var(--r-…), 25%)`-style caps (or a `--r-*-small` set) for buttons, segment cells, M / S / Arm, chips, pads, keys and Session cells in `src/web/remote/src/app.css` and the screen styles; keep `DesignTokenCheck.Remote` passing per spec Edge Cases "Roundness 2×" (partial)
- [ ] T041 Make the smoke test green: rerun `scripts/build.sh` on a quiet machine and compare the VST3 render-pool timing checks against `develop` built the same way; if they fail on `develop` too, record that they are unrelated, else fix per Constitution IV / plan Quality Gates (partial)
- [X] T042 Cover audio-clip previews end to end: a smoke test in `tests/Nota.SmokeTest/RemoteTests.cs` that puts an audio clip in a Session slot (`AddSessionAudioFile` or equivalent) and checks the `clips` slot has `k: "p"` with 48 values 0–99 and a 99 at the loudest bucket per FR-018 / US4/AC2 (partial)
- [ ] T043 Real-hub check with the desktop beside the phone (quickstart § 3): MIDI and audio previews match the desktop Session slot, an edit follows within 1 s, a recording grows; capture the side-by-side screenshots per FR-017 / SC-007 / SC-008 (missing)
- [ ] T044 Update the user manual's Nota Remote page (EN + RU) in `../nota-docs` via the `nota-docs` skill — new look, skin typeface / corners, Scenes clip previews — once that repo is free of the unrelated in-progress branch per Constitution VI (missing)
- [ ] T045 Review the two fixes outside the restyle — the tablet Mixer master strip width (`.tablet .masterstrip`) and full-width rows in the track sheet — and keep or split them with a line in the PR description per Constitution V (unrequested)
