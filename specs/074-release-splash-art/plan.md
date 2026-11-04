# Implementation Plan: Per-release splash art

**Branch**: `074-release-splash-art` (off `develop`) | **Date**: 2026-11-03 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/074-release-splash-art/spec.md`

**Note**: This template is filled in by the `/speckit-plan` command; its definition describes the execution workflow.

## Summary

The launch splash shows an abstract composition generated from the app version. Each
version picks its own layout, one of six motif families (piano roll, rhythm rings,
waveform strata, panels, Lissajous, fader rails), a palette of track colours, a stroke
weight and a gradient wash. Exactly one orange element is "live".

The technical approach has five parts:
1. A pure, theme-free composer turns the version into a scene of primitives with colour
   roles.
2. An Avalonia `Control` resolves those roles against the live palette and draws the scene.
3. Both live in `Theme/SplashArt/`, the one place where gradients are allowed, next to the
   existing plug-in wash. DESIGN.md records the exception and a smoke check pins it.
4. The smoke test checks determinism and variety on the scene, and gains a `--splash-sheet`
   mode that writes an HTML contact sheet for review.
5. The splash text stays on today's plain ground, in a region of its own.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill. Rule 6
  gets a documented splash exception (gradients, heavier strokes). Rules 1 (accent only for
  live elements) and 3 (no semantic colours) hold unchanged.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md). The new files carry the SPDX
  header, and there is no new dependency.
- Engine, instruments and build are not touched.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia 12.1.4 (already used): `Control.Render`,
`DrawingContext`, `StreamGeometry`, `Linear/RadialGradientBrush`. No new dependency.

**Storage**: N/A. The art is composed in memory at launch.

**Audio-Thread Impact**: None. The splash runs on the UI thread before the engine is
constructed.

**Testing**: `tests/Nota.SmokeTest`:
- a new `SplashArtTests` block on the linked composer;
- the extended `DesignTokenCheck` gradient check;
- the `--splash-sheet` review mode;
- a headless Avalonia capture for the four-theme check (quickstart §3).

**Target Platform**: macOS, Windows, Linux. The app uses no platform-specific drawing.

**Project Type**: desktop app (Avalonia)

**Performance Goals**: compose ≤ 10 ms median. First splash paint ≤ 50 ms later than today
(SC-005).

**Constraints**:
- deterministic per version (FR-001);
- consecutive patches change family (FR-002, research R4);
- colours only from theme tokens, with gradients only in `Theme/SplashArt/` (FR-004);
- at most one accent item (FR-006);
- static, with no animation besides the progress bar (FR-009);
- never blocks launch (FR-010).

**Scale/Scope**: about 4 new source files (~700 lines including six motif ports), 2 edited
app files, 2 test files plus a csproj link, DESIGN.md, CHANGELOG and FEATURES.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| 0. Approved issue first | Waived | The core maintainer waived the issue for this feature in chat on 2026-11-03. Recorded in spec.md. |
| I. Real-time audio safety | Pass | UI thread only, before the engine exists. |
| II. Boundary discipline | Pass | No C ABI or JUCE involvement. |
| III. Licensing hygiene | Pass | New files carry the two-line SPDX header. No dependency. |
| IV. Verification gate | Pass | Smoke test extended (`SplashArtTests`, gradient-location check). `scripts/build.sh` must pass. |
| V. One logical change per PR | Pass | Splash art only, with its DESIGN.md exception and tests. |
| VI. Docs in sync | Pass | CHANGELOG *Added*, FEATURES.md via `nota-features`, `nota-docs` check, DESIGN.md exception. |
| VII. Spec-first | Pass | spec → plan → tasks. |
| VIII. Safety rules not weakened | Pass | Rule 6 is a design rule, not one of I–III. The exception is narrow, documented in DESIGN.md first, and enforced by a stricter check than today's. |

Post-design re-check: unchanged, all pass.

## Project Structure

### Documentation (this feature)

```text
specs/074-release-splash-art/
├── plan.md              # This file
├── research.md          # Phase 0: R1–R9 decisions
├── data-model.md        # Phase 1: seed, scene, primitives, paints, colour refs
├── quickstart.md        # Phase 1: validation guide
├── prototype.html       # Reviewed browser prototype (reference for the port)
├── contracts/
│   └── splash-art.md    # Composer, renderer and review-sheet contracts
└── tasks.md             # Phase 2 (/speckit-tasks)
```

### Source Code (repository root)

```text
src/managed/Nota.App/
├── SplashWindow.cs                       # EDIT: compose, host SplashArtView, put the content in TextRect, fallback
└── Theme/
    └── SplashArt/                        # NEW: the only gradient code outside NotaPalette
        ├── SplashScene.cs                # records: SplashScene, Prim, Paint, ColorRef, enums (no Avalonia)
        ├── SplashArtComposer.cs          # seed, PRNG, layout, palette, six families + overlays (no Avalonia)
        └── SplashArtView.cs              # Control: resolve ColorRefs from NotaPalette in Render, draw

tests/Nota.SmokeTest/
├── Nota.SmokeTest.csproj                 # EDIT: link SplashScene.cs and SplashArtComposer.cs
├── SplashArtTests.cs                     # NEW: determinism, variety, invariants, timing, edge versions; --splash-sheet writer
├── DesignTokenCheck.cs                   # EDIT: gradients inside Theme/ only in NotaPalette.cs + Theme/SplashArt/
└── Program.cs                            # EDIT: "-- design: splash art --" block, --splash-sheet mode

DESIGN.md                                 # EDIT: rule 6 second exception; Enforced by tests
CHANGELOG.md                              # EDIT: [Unreleased] Added
FEATURES.md                               # EDIT via nota-features
```

**Structure Decision**:
- All new code stays in `Nota.App`, the only project that knows Avalonia drawing and the
  palette.
- The composer is kept Avalonia-free so the smoke test can link it, as it already links
  `Changelog.cs` and `ClipEditor/*.cs`.
- `Theme/SplashArt/` follows DESIGN.md: gradient-bearing code lives with the tokens, and
  views draw none.

## Complexity Tracking

No constitution violations. The one design-rule exception (gradients and heavier strokes
on the splash) is the feature itself. It is documented in DESIGN.md and enforced by test,
not tracked as a violation.
