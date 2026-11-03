# Feature Specification: Per-release splash art

**Feature Branch**: `074-release-splash-art`

**Created**: 2026-11-03

**Status**: Draft

**Input**: User description: "Per-release splash screen art. Each Nota release shows its own abstract image on the launch splash screen (SplashWindow), generated procedurally in the app with the release version as the seed — no image assets, no manual step at release time. The same version always yields the same image; a different version yields a visibly different one. The art must sit inside Nota's "Studio" design language (neutral greys, dark 1px seams, the single orange accent used sparingly, the colour themes) while being genuinely creative — not a generic gradient. The existing splash content (logo, name, version, indeterminate progress) stays readable on top of it. No GitHub issue: the core maintainer waived it for this feature."

**Issue**: none — waived by the core maintainer for this feature (constitution § 0).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

The splash is `src/managed/Nota.App/SplashWindow.cs`, shown by
`App.OnFrameworkInitializationCompleted` for at least 650 ms while the engine starts.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Each release greets you with its own picture (Priority: P1)

A musician updates Nota and launches it. The splash shows an abstract composition they
have not seen before. Every launch of that version shows the same composition, so the
picture becomes the face of that release. When they next update, the picture changes.

**Why this priority**: This is the feature. Without it nothing else matters.

**Independent Test**: Launch builds with two different versions and the same version
twice; capture the splash each time. The same-version captures are identical; the
different-version captures are clearly different.

**Acceptance Scenarios**:

1. **Given** Nota version A, **When** it is launched twice (any machine, any day), **Then** the splash art is identical both times.
2. **Given** versions A and B that differ in any part of MAJOR.MINOR.PATCH (including a patch bump), **When** each is launched, **Then** the two compositions are visibly different at a glance, not just shifted or re-tinted.
3. **Given** a release is being cut, **When** the maintainer follows the release process, **Then** no step concerns the splash art. The new version number alone produces the new picture.

---

### User Story 2 - The art looks like Nota, and it looks deliberate (Priority: P1)

The art is built from Studio's vocabulary (tone planes, seams and rules, crisp geometry)
and from motifs taken from the work of making music: a piano roll, Euclidean rhythm rings,
stacked waveform strata, a partition into panels, a Lissajous figure, and fader rails. The
splash is the one place that also allows soft gradients and heavier strokes. Each version
arranges these into one composed image. It does not read as a stock gradient, random noise
or a screensaver.

**Why this priority**: The maintainer asked for it explicitly. Art that breaks the design
language or looks generic is worse than the current plain splash.

**Independent Test**: Render the art for a set of versions (e.g. the 20 most recent
release numbers plus 20 future ones) as a contact sheet. Review it against DESIGN.md and
for variety.

**Acceptance Scenarios**:

1. **Given** any version, **When** the art is rendered, **Then** every colour comes from the active theme's tokens, and it contains no glow, blur, raster textures or photographic imagery. Gradients and strokes heavier than 1px are allowed (FR-004).
2. **Given** any version, **When** the art is rendered, **Then** its colours are the theme's neutral tones plus that version's track-colour palette, with at most one "live" accent element and no semantic colours (FR-006).
3. **Given** a contact sheet of 40 versions, **When** reviewed, **Then** the compositions show real variety (different motif families, structures and densities), not one template with different random numbers.

---

### User Story 3 - The splash still does its job (Priority: P2)

The logo, the name "Nota", the version line and the indeterminate progress bar stay
legible on top of the art in every theme. The splash appears just as quickly as it does now.

**Why this priority**: The splash exists to show that the app is starting. Art must not
cost readability or launch time.

**Independent Test**: Capture the splash for several versions in each of the four
built-in themes. Check text contrast and measure the time from launch to the first
painted splash against the current build.

**Acceptance Scenarios**:

1. **Given** any version and any built-in theme, **When** the splash shows, **Then** the name and version text meet the theme's readability bar for text on its ground.
2. **Given** the app launches, **When** the splash first paints, **Then** the art is already drawn (no blank frame first, no art popping in later) and launch-to-first-paint is no slower than today by any noticeable amount.
3. **Given** the user's active theme is Studio Light (or any other built-in theme), **When** the splash shows, **Then** the art is drawn in that theme's values. A light theme gets a light splash.

---

### Edge Cases

- **Version string with a suffix or build metadata** (`0.71.0-beta.1`, `+commit`): the art depends only on the release number shown on the splash, so builds of the same release look alike. A pre-release counts as its own version.
- **Version unreadable** (falls back to `0.0.0`): the splash still shows a valid composition for that fallback seed and never fails or shows a blank card.
- **Art generation fails for any reason**: the splash falls back to today's plain card. A launch is never blocked by the art.
- **High-DPI or scaled displays**: lines stay crisp at 1 device-independent pixel; nothing blurs from bitmap scaling.
- **Theme change while the splash is up**: not supported, because the splash closes before Preferences can be opened.
- **Consecutive patch releases** (0.70.1 → 0.70.2): must still look different (Scenario 1.2). Seeding is not allowed to cluster nearby numbers.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The splash MUST show an abstract composition derived only from the app's release version. The same version MUST give the same composition on every machine, OS, run and date.
- **FR-002**: Two different versions MUST give visibly different compositions, including versions one patch apart.
- **FR-003**: The art MUST be produced at launch from code. No per-release image files, and no step added to the release process.
- **FR-004**: The art MUST take every colour from the active colour theme's tokens, with no hard-coded colours. As a documented splash-only exception to DESIGN.md rule 6:
  - the art MAY use linear and radial gradients between those token colours, including fades to transparent (washes behind a motif, note velocity fades, level fills, gradient strokes);
  - the art MAY use strokes heavier than 1px, and each version picks its stroke weight (about 1.5–3px).
  The art MUST NOT use glow, blur, drop shadows, raster textures or photographic imagery. Seams between the art and the text area stay 1px.
- **FR-005**: The art MUST choose, per version, one primary motif family from: piano roll, rhythm rings, waveform strata, panels, Lissajous figure, fader rails. It MAY add a secondary, quieter family on top. Arrangement-style clip lanes are excluded. The version MUST choose the structure, not only parameters, so releases differ in character as well as in detail.
- **FR-006**: Colour in the art follows Studio's "the work carries the colour":
  - The art MAY colour its "work" elements (clips, notes, waveforms, rhythm hits, level fills, panels) with the active theme's nine track colours, drawn the way the app draws them. A clip, for example, gets a track-colour body and a title band 30% toward black.
  - Each version MUST pick a small palette from those track colours: one colour, an adjacent pair or trio, a complementary pair, or a triad. The share of coloured elements MUST vary, so palette and colour amount differ between releases along with structure.
  - The orange accent MAY appear at most once in the art, and only as something "live" in Nota's sense: a playhead, the current step, the fader being touched (DESIGN.md rule 1).
  - Semantic colours (mute, solo, record, danger, success, warning) MUST NOT be used.
  - The neutral tones carry the chrome of the art: grounds, seams, rules and grids.
- **FR-007**: The existing splash content (logo, "Nota", version line, indeterminate progress) MUST stay, in the same order. The name and version MUST meet the active theme's readability bar against what lies behind them.
- **FR-008**: The art MUST be fully drawn on the splash's first paint and MUST NOT measurably delay launch-to-first-paint.
- **FR-009**: The art MUST be static: no animation. The indeterminate progress bar is the only moving element.
- **FR-010**: If the art cannot be produced, the splash MUST fall back to the current plain card without delaying or failing the launch.
- **FR-011**: The art MUST render crisply at any display scale.
- **FR-012**: Developers MUST be able to render the art for any given version without building or launching that version, so it can be reviewed and tested.

### Key Entities

- **Release seed**: the MAJOR.MINOR.PATCH(-pre-release) version shown on the splash. It is the only input besides the theme.
- **Composition**: what one seed produces — a chosen structure, a set of motifs and their placement, rendered in the theme's tokens.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: For the same version, 100% of renders are pixel-identical in a given theme at a given scale.
- **SC-002**: Across 200 consecutive patch versions (e.g. 0.60.0 to 0.79.9), no two compositions are judged the same or near-same by an automated visual-difference check, and none of them is empty.
- **SC-003**: In a contact sheet of 40 versions, a reviewer can find at least 5 clearly different structural families.
- **SC-004**: The name and version text pass the theme readability bar in all four built-in themes for every version in the SC-002 range.
- **SC-005**: Launch-to-first-painted-splash grows by no more than 50 ms over the current build on the reference Mac.
- **SC-006**: Releasing a new version takes no extra action for the splash. The maintainer's release checklist is unchanged.

## Assumptions

- "Release version" means the app version on the splash (`AppInfo.Version`, from the `VERSION` file). Local dev builds of an unreleased version show the art that version will ship with, which serves as a preview.
- The splash keeps its current size (420 × 300) and its 4px modal corner. The art fills the card behind the content, which may be moved within the card (for example to one side or onto a calm area of the art) as long as FR-007 holds.
- DESIGN.md applies to the splash except where FR-004 relaxes rule 6 (gradients, heavier strokes). The exception is written into DESIGN.md, next to the existing plug-in preview wash exception, so it stays the only one.

## Clarifications

### Session 2026-11-03

- Q: How should the art use the orange accent, given DESIGN.md rule 1? → A: First answer: not at all, strictly neutral. Revised the same day after the maintainer reviewed the neutral prototype and asked for more colour (see next question).
- Q: May the art use gradients and strokes heavier than 1px, given DESIGN.md rule 6? → A: Yes, as a splash-only exception documented in DESIGN.md (FR-004).
- Q: Which motif families stay after reviewing the prototype? → A: Piano roll, rhythm rings (with heavier lines), waveform strata, panels and Lissajous are approved. Arrangement lanes are dropped. Fader rails stay too (confirmed by the maintainer) (FR-005).
- Q: Which colours may the art use? → A: The theme's track colours as "the work", using a per-version palette, plus at most one accent element that reads as live (a playhead, the current step). No semantic colours (FR-006).
- No new third-party dependency is needed. The art is drawn with the UI toolkit already in use.
- The About window and other surfaces are out of scope. The art may be reused there later.
- The seed must not depend on the runtime's per-process string hashing, which differs between runs.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI thread only, before the engine starts
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no crossing
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change; determinism and token use are worth a smoke check (FR-001, FR-004)
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
