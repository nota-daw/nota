---

description: "Task list for per-release splash art"
---

# Tasks: Per-release splash art

**Input**: Design documents from `/specs/074-release-splash-art/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/splash-art.md](contracts/splash-art.md), [quickstart.md](quickstart.md)

**Reference**: [prototype.html](prototype.html) is the reviewed generator, written in JS. Port its family functions (`F.roll`, `F.rings`, `F.strata`, `F.panels`, `F.lissa`, `F.faders`), plus `palette`, `art` and the helpers, to C#. Follow the decisions in research R3–R5 where they differ from the prototype, for example the family step.

**Tests**: Included. The spec's success criteria (SC-001–SC-005) and quickstart §1 are smoke-test checks.

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story the task belongs to (US1, US2, US3)

## Path Conventions

- App: `src/managed/Nota.App/` (new code in `Theme/SplashArt/`)
- Tests: `tests/Nota.SmokeTest/`

---

## Phase 1: Setup

**Purpose**: Branch and folder.

- [X] T001 Create branch `074-release-splash-art` from `develop`. The uncommitted 073 work in the current `feat/section-rearrange` checkout stays there: use a separate worktree, or ask the user before stashing. Carry over the untracked `specs/074-release-splash-art/` folder.
- [X] T002 Create the folder `src/managed/Nota.App/Theme/SplashArt/`

---

## Phase 2: Foundational (blocking prerequisites)

**Purpose**: The scene model, seed and PRNG, and the test wiring that every story builds on.

**⚠️ CRITICAL**: No user story work can begin until this phase is complete.

- [X] T003 [P] Create `src/managed/Nota.App/Theme/SplashArt/SplashScene.cs`. It holds Avalonia-free records and enums from [data-model.md](data-model.md):
  - `SplashScene` with `Version`, `Layout`, `ArtRect`, `TextRect`, `Family`, `Overlay`, `PaletteMode`, `Palette`, `StrokeWeight`, `Wash`, `Items`;
  - `SplashLayout { TextLeft, TextRight, TextBottom }`;
  - `SplashFamily { Roll, Rings, Strata, Panels, Lissajous, Faders }`;
  - `PaletteMode { Mono, Analog, Pair, Triad }`;
  - `WashKind { None, Radial, Linear }`;
  - the `Prim` kinds `Rect`, `Line`, `Polyline`, `Polygon`, `Circle`;
  - `Paint` as `Solid`, `Linear` or `Radial`, with stops and a `Bounds`/`Art` space;
  - `ColorRef` with `Role`, `TrackIndex` (0–8), `TowardBlack` (0..1), `TowardGround` (0..1) and `Alpha` (0..1);
  - `Role { Ground, App, Panel, Raised, Hover, Seam, Rule, Grid, Faint, Tertiary, Ink, Accent, Track }`. Semantic roles must not exist.

  Use a plain rect struct rather than `Avalonia.Rect`. Use value equality, so scenes compare structurally. Store point lists as arrays wrapped for sequence equality.
- [X] T004 [P] Create `src/managed/Nota.App/Theme/SplashArt/SplashArtComposer.cs` with the seed and PRNG only (research R3):
  - FNV-1a 32-bit over the UTF-16 code units, then the finaliser (`h^=h>>>15; h*=2246822507; h^=h>>>13`);
  - `ReleaseSeed` parsing `Full`, `Line` (hash of `"major.minor"`) and `Patch`. A non-semver string gives `Line = Full` and `Patch = 0`, and parsing never throws;
  - a `Mulberry32` struct with `NextDouble()` and `Pick<T>(T[])`;
  - a helper that rounds coordinates to 0.1.

  Port the arithmetic so that unchecked `uint` math matches the prototype's `Math.imul` and `>>>`.
- [X] T005 Link `SplashScene.cs` and `SplashArtComposer.cs` into the smoke test with `<Compile Include="..\..\src\managed\Nota.App\Theme\SplashArt\SplashScene.cs" Link=…/>`. Do the same for the composer, in `tests/Nota.SmokeTest/Nota.SmokeTest.csproj` (depends on T003, T004).
- [X] T006 Create `tests/Nota.SmokeTest/SplashArtTests.cs` with an empty `Run()` that yields `(bool, string)`, and print it from `tests/Nota.SmokeTest/Program.cs` under `-- design: splash art --` next to the other design blocks (depends on T005).
- [X] T007 [P] In `SplashArtTests.cs`, add a seed check: FNV and mulberry32 outputs for `"0.58.1"` equal the values the prototype's `seedOf`/`rng` produce. Record the first three doubles as constants, taken by running `prototype.html`'s functions in a browser console.

**Checkpoint**: The scene model compiles in both projects and the seed matches the prototype.

---

## Phase 3: User Story 1 — Each release greets you with its own picture (Priority: P1) 🎯 MVP

**Goal**: The splash shows a deterministic, version-specific composition from the six motif families. In this story it is drawn in neutral roles only, with solid fills.

**Independent Test**: Launch, or render headless, with two versions and with the same version twice. Same version gives identical output, different versions give visibly different compositions. The smoke checks below pass.

### Tests for User Story 1

- [X] T008 [P] [US1] In `tests/Nota.SmokeTest/SplashArtTests.cs`, add determinism: for 50 sampled versions, `Compose(v) == Compose(v)` (SC-001).
- [X] T009 [P] [US1] In `tests/Nota.SmokeTest/SplashArtTests.cs`, add variety: over 0.60.0–0.79.9 (200 versions),
  - consecutive patches have different `Family`;
  - all 200 scenes are pairwise distinct;
  - every `Items` is non-empty (SC-002).
- [X] T010 [P] [US1] In `tests/Nota.SmokeTest/SplashArtTests.cs`, add edge versions: `""`, `"0.0.0"`, `"abc"`, `"0.71.0-beta.1"` and `"1.2.3+deadbeef"` each give a valid non-empty scene, with no exception.
  - `"0.71.0-beta.1"` has the same `Family` as `"0.71.0"`.
  - Every coordinate is finite and within one card size of the 420×300 card. Motifs may
    overrun the art region on purpose, and the renderer clips them.

### Implementation for User Story 1

- [X] T011 [US1] In `SplashArtComposer.cs`, implement `Compose(string version)`. It must be pure, total and deterministic.
  - Layout uses the prototype's pick over `[TextLeft, TextRight, TextBottom, TextLeft, TextBottom]`, with rects from data-model.md: `TextLeft` gives text 0,0,150,300 and art 150,0,270,300; `TextRight` is mirrored; `TextBottom` gives art 0,0,420,196 and text 0,196,420,104.
  - `Family = (Line + 5·Patch) mod 6` (research R4).
  - Optional overlay at 35% from the allowed pairs Panels→{Rings, Lissajous}, Strata→Rings, Faders→Strata, Roll→Rings. The overlay is drawn in neutral roles with a thinner stroke.

  Wrap the body so any exception still returns a minimal valid scene, for example `Panels` in neutral roles.
- [X] T012 [US1] In `SplashArtComposer.cs`, port the `Roll` and `Rings` families from `prototype.html` as primitive emitters. Use neutral roles in place of the palette for now. Roll is a step grid with a bar line every 16 steps, plus 1–3 voices of note bars (rx 2) as a random walk. Rings are Euclidean rings with hits and rests.
- [X] T013 [US1] In `SplashArtComposer.cs`, port the `Strata` family: 9–18 stacked lines, each a sum of three sines under a Gaussian envelope. Each layer occludes the one below by filling to the bottom with `Ground`.
- [X] T014 [US1] In `SplashArtComposer.cs`, port the `Panels` family (recursive split, depth ≤ 4, with hatch, dot grid or circle fills and seam outlines) and the `Lissajous` family (20px grid, a:b from {1,2,3,3,4,5}×{2,3,4,5,7}, and a flattened echo curve).
- [X] T015 [US1] In `SplashArtComposer.cs`, port the `Faders` family: 8–17 rails with ticks, a level fill under each cap, and caps on a sine.
- [X] T016 [US1] Create `src/managed/Nota.App/Theme/SplashArt/SplashArtView.cs`, a `Control` sized 420×300. Its `Render` does the following:
  - clips to the card with the same corner radius `SplashWindow` uses today (`NotaRadius.Body`);
  - fills `Ground`;
  - pushes a clip to `scene.ArtRect` and draws `Items` in order;
  - draws a 1px `Seam` line on the art/text boundary and the 1px card border.

  Resolve each `ColorRef` from the live `NotaPalette` slots (`.Color`) inside `Render`, applying `TowardBlack`/`TowardGround` mixes and `Alpha`. Never cache colours in fields. Polylines and polygons use `StreamGeometry`, lines use round caps and polylines round joins. Only `Solid` paints are needed in this story.
- [X] T017 [US1] Edit `src/managed/Nota.App/SplashWindow.cs`:
  - compose from `AppInfo.Version`;
  - put a `Panel` in the card with a `SplashArtView` underneath;
  - place the existing logo, name, version and progress `StackPanel` in `scene.TextRect`. For `TextBottom`, lay it out as a row: logo, then a column with the name, version and bar.

  Keep the existing strings (`Loc.T("Nota")`, `Loc.F("Version {0}", …)`) and sizes. Update the file's header comment to say the card carries the release's art.

**Checkpoint**: The splash shows a per-version neutral composition. The T008–T010 checks pass.

---

## Phase 4: User Story 2 — The art looks like Nota, and it looks deliberate (Priority: P1)

**Goal**: Track-colour palettes, one live accent, gradients and per-version stroke weights, under a documented DESIGN.md exception enforced by test. A contact sheet supports review.

**Independent Test**: Run `--splash-sheet` for 40 versions in Studio Dark and Studio Light. Expected: at least 5 distinct structures, track colours plus greys, at most one orange element per card, and gradients present. The geometry and bans checks pass.

### Tests for User Story 2

- [X] T018 [P] [US2] In `tests/Nota.SmokeTest/SplashArtTests.cs`, add colour invariants for all 200 versions:
  - at most one live mark paints with `Role.Accent` (≤ 2 items: a playhead's line and head);
  - overlay items use only `Ground`, `App`, `Panel`, `Raised`, `Hover`, `Seam`, `Rule`, `Grid`, `Faint`, `Tertiary` or `Ink`;
  - `Palette` has 1–3 indices in 0–8, matching `PaletteMode`: Mono 1, Analog and Triad 3, Pair 2;
  - `StrokeWeight ∈ {1.5, 2, 2.5, 3}`.
- [X] T019 [P] [US2] In `tests/Nota.SmokeTest/DesignTokenCheck.cs` (`RunGeometry`), add the check "gradients in Theme/ only in NotaPalette.cs and Theme/SplashArt/". It scans `Theme/**/*.cs` for `(Linear|Radial|Conic)GradientBrush`. The existing "no gradients in views" check stays as it is.

### Implementation for User Story 2

- [X] T020 [US2] In `SplashArtComposer.cs`, add the palette (research R5):
  - base `i` in 0–8;
  - mode from `[Analog, Analog, Pair, Triad, Mono]`, with indices Mono `[i]`, Analog `[i, i+1, i+2]`, Pair `[i, i+4]`, Triad `[i, i+3, i+6]`, all mod 9;
  - colour share 0.35–0.85;
  - stroke weight from `[1.5, 2, 2, 2.5, 3]`;
  - wash from `[None, Radial, Linear, Radial]` with a focus point.

  Thread these through every family as `C.pal`, `C.amt` and `C.w`, the way the second prototype version does: Roll voices take palette colours, Rings colour per ring, Strata colour hero lines, Panels colour about `amt·0.5` of the cells, Lissajous colours its curves, and Faders colour the level fills.
- [X] T021 [US2] In `SplashArtComposer.cs`, add exactly one live accent per primary family: the playhead line plus a triangle head (Roll, Strata), the current step's spoke plus a ring (Rings), the touched fader's cap line (Faders), a point on the curve (Lissajous), and at most one accent circle (Panels). Overlays never emit `Accent`.
- [X] T022 [US2] In `SplashArtComposer.cs`, emit gradient paints:
  - Roll note velocity fade, horizontal from colour to alpha 0.25–0.65;
  - Rings centre disc, radial from palette first at 0.35 to transparent;
  - Strata hero-layer underglow, vertical from alpha 0.45 to 0 over 35%;
  - Strata hero stroke, horizontal across the palette in `Art` space;
  - Panels coloured cells, from colour to `TowardGround` 0.7, vertical, horizontal or diagonal;
  - Lissajous main curve across the palette in `Art` space;
  - Faders level fill, from colour to alpha 0.15;
  - the wash rect behind the art, from palette first at ≤ 0.28 to the last palette colour at 0.
- [X] T023 [US2] In `src/managed/Nota.App/Theme/SplashArt/SplashArtView.cs`, render `Linear`/`Radial` paints as `LinearGradientBrush`/`RadialGradientBrush`, built inside `Render` from the resolved colours. Map `Bounds` space to relative units and `Art` space to absolute card coordinates. Stroke widths come from the primitives. Brushes are never stored in static fields, because they would snapshot theme colours.
- [X] T024 [US2] Add the review-sheet writer to `tests/Nota.SmokeTest/SplashArtTests.cs` (contract §3):
  - `WriteSheet(outPath, themeId, fromVersion, count)` resolves roles from `src/managed/Nota.App/Theme/Themes/<themeId>.json`, using the same role→token map as `SplashArtView`;
  - it writes one standalone HTML page with an inline SVG per version (420×300, gradients as `<linearGradient>`/`<radialGradient>`, ids unique per card) and a caption `version · family[+overlay] · mode`;
  - the version list is the next 4 patches, then successive minors `.0`, with 40 versions by default.

  Wire `--splash-sheet <out.html> [theme-id] [from] [count]` into `tests/Nota.SmokeTest/Program.cs` next to `--palette-dump`. It exits 1 on an unknown theme or an unwritable path.
- [X] T025 [US2] In `DESIGN.md` rule 6, add a second exception after the plug-in wash paragraph. It says the launch splash's art (`Theme/SplashArt/`, specs/074) may use gradients between theme tokens, including fades to transparent, and strokes heavier than 1px. It is still drawn only from tokens, and has no glow, blur or shadow. In § Enforced by tests, name the new gradient-location check.

**Checkpoint**: The sheet shows coloured, gradient-bearing, varied compositions in both Studio themes. The T018–T019 checks pass.

---

## Phase 5: User Story 3 — The splash still does its job (Priority: P2)

**Goal**: The text stays readable, the art never blocks or slows launch, and the art renders crisply at any scale.

**Independent Test**: Run a headless capture in four themes at 1× and 2× (quickstart §3), a forced-failure launch (§4) and the timing check (§1, §5).

### Tests for User Story 3

- [X] T026 [P] [US3] In `tests/Nota.SmokeTest/SplashArtTests.cs`, add timing: the median of 20 `Compose` calls over different versions is under 10 ms (research R8, SC-005 budget).

### Implementation for User Story 3

- [X] T027 [US3] In `src/managed/Nota.App/SplashWindow.cs`, wrap compose and view creation in `try/catch`. On failure, build today's plain card exactly as before, so launch is never blocked (FR-010).
- [X] T028 [US3] Verify readability and crispness with a throwaway headless harness outside the repository (Avalonia.Headless + Skia, `NotaThemeService.Set` via reflection). Capture `new SplashWindow()` for `studio-dark`, `studio-light`, `ember-graphite` and `ember-paper`, at scale 1 and 2, for 3 versions. Confirm:
  - the text sits on the plain ground;
  - the art is in its region behind a 1px seam;
  - the light themes give a light splash;
  - nothing is blurred.

  Save the captures for the PR's screenshots.
- [X] T029 [US3] Measure launch-to-first-splash-paint against `develop` (quickstart §5, temporary timestamps, not committed). Record the result in the PR. The increase must be ≤ 50 ms.
  - Measured headless on 2026-11-03, on a loaded machine: `new SplashWindow()` → first rendered frame.
  - Art card: cold 237 ms, warm median 4.0 ms. Plain card (the `develop` card, via a forced fallback): cold 201–207 ms, warm median 3.2–3.4 ms.
  - Increase: about 30–36 ms cold and under 1 ms warm, within 50 ms.
  - Not yet measured on a real GUI launch on the reference Mac.

**Checkpoint**: All three stories work. The splash is readable, fast, and safe on failure.

---

## Phase 6: Polish & Cross-Cutting Concerns

- [X] T030 [P] Generate review sheets for Studio Dark and Studio Light (`--splash-sheet`, 40 versions from the current `VERSION`). Check SC-003 (≥ 5 distinct structures) and attach the sheets or their screenshots to the PR.
- [ ] T031 Run every quickstart.md check, §1–§5.

### Nota closing tasks *(PR gates)*

- [ ] T032 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T033 Add the AGPL-3.0-only SPDX two-line header to `SplashScene.cs`, `SplashArtComposer.cs`, `SplashArtView.cs` and `SplashArtTests.cs` (CONTRIBUTING.md)
- [X] T034 New dependency: N/A. Avalonia drawing APIs are already in use.
- [X] T035 Add a `CHANGELOG.md` `[Unreleased]` *Added* entry: every release now opens with its own splash art, generated from its version number.
- [X] T036 Update FEATURES.md via the `nota-features` skill. Run the `nota-docs` skill to check whether the user manual shows the splash, and re-shoot it if so. Shortcuts: N/A.
- [X] T037 Engine behaviour change: N/A. The smoke test is extended for the managed composer (T007–T010, T018–T019, T026).

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)** comes first.
- **Foundational (Phase 2)** follows Setup and blocks every story.
- **US1 (Phase 3)** follows Foundational. It is the MVP.
- **US2 (Phase 4)** follows US1: it extends the composer and view that US1 creates.
- **US3 (Phase 5)**: T026 can start after Foundational. T027–T029 need US1's `SplashWindow` change, and T028's captures are most useful after US2.
- **Polish (Phase 6)** comes after all stories.

### Within stories

- The test tasks (T008–T010, T018–T019, T026) are written first and fail until their implementation lands.
- T011 → T012 → T013 → T014 → T015 all edit `SplashArtComposer.cs`, so they run sequentially.
- T016 (view) can run in parallel with T012–T015 once T011 has defined the scene's shape. T017 needs T016.
- T020 → T021 → T022 run sequentially (same file). T023 needs T022's paint shapes. T024 needs T022. T025 is independent.

### Parallel Opportunities

- T003 ∥ T004
- T008 ∥ T009 ∥ T010 (same file but independent methods; merge carefully)
- T016 ∥ T012–T015
- T018 ∥ T019 ∥ T025
- T030 ∥ T033 ∥ T035

## Parallel Example: User Story 2

```bash
Task: "T019 gradient-location check in tests/Nota.SmokeTest/DesignTokenCheck.cs"
Task: "T025 DESIGN.md rule 6 splash exception"
Task: "T018 colour invariants in tests/Nota.SmokeTest/SplashArtTests.cs"
```

## Implementation Strategy

### MVP first (US1)

Phases 1–3 give a per-release neutral composition with determinism and variety checks.
Stop and validate it on the real splash.

### Incremental delivery

1. US1: the neutral per-release art, already shippable.
2. US2: colour, accent, gradients, the DESIGN.md exception and the review sheet. This
   brings the art to the reviewed look.
3. US3: hardening (fallback, timing) and four-theme verification for the PR.

## Notes

- Tests here are smoke-test checks that print PASS/FAIL. There is no separate test framework.
- Keep the composer free of Avalonia types. The smoke test links it.
- Don't commit temporary timing or verification code (T029). Harnesses live outside the
  repository.

---

## Phase 7: Convergence

- [X] T038 Make the version's colour share (`Amt`, 0.35–0.85) decide how much of each family is coloured, not only Panels, in `src/managed/Nota.App/Theme/SplashArt/SplashArtComposer.cs`. Roll: the share of notes in palette colours, the rest in neutral `Raised`/`Hover`. Rings: coloured hits on that share of rings. Faders: coloured level fills on that share of faders. Strata already scales with palette size. Add a smoke check that the coloured fraction of items differs across versions. Per FR-006 (partial).
- [X] T039 Give `SplashArtView` and the review sheet one role→token map: resolve roles in `src/managed/Nota.App/Theme/SplashArt/SplashArtView.cs` through `SplashTokens.Name`, for example by matching `NotaPalette.AllKeys` on the token name without a raw `"Brush."` literal, and drop the separate `Slot` switch. Or add a smoke check that the two agree. Per plan R2 / T024 (partial).
- [ ] T040 Measure launch → first splash paint on a real GUI launch on the reference Mac, against `develop`, and record it in the PR. The headless figure (+30–36 ms cold) stands in until then. Per SC-005 (partial).
