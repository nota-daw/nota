# Quickstart: validate per-release splash art

## Prerequisites

- The repository builds with `scripts/build.sh`. The engine is needed only for the full
  smoke run.
- The feature branch is checked out.

## 1. Smoke checks (determinism, variety, bans)

```bash
scripts/build.sh
```

The `-- design: splash art --` block must show PASS for:
- same version → equal scene, for each of 50 sampled versions (SC-001);
- consecutive patches over 0.60.0–0.79.9 → different family, and all 200 scenes distinct
  and non-empty (SC-002);
- every scene → at most one accent item, overlay neutral, coordinates within the card;
- composing one scene takes under 10 ms (median of 20) (SC-005 budget);
- non-semver, empty and pre-release versions → valid scenes, no exception (edge cases).

The `-- design: geometry --` block must still show "no gradients in views", plus the new
"gradients in Theme/ only in NotaPalette.cs and Theme/SplashArt/".

## 2. Review sheet (FR-012, SC-003)

```bash
dotnet run --project tests/Nota.SmokeTest -- --splash-sheet /tmp/splash-dark.html studio-dark 0.58.1 40
```

```bash
dotnet run --project tests/Nota.SmokeTest -- --splash-sheet /tmp/splash-light.html studio-light 0.58.1 40
```

Open both pages. Expected:
- the 40 cards fall into at least 5 visibly different structures;
- no card is empty;
- colours are track colours plus neutral greys, with at most one orange element;
- the families read like the reviewed prototype. Exact compositions differ, because the
  family is stepped by patch (research R4).

## 3. Real splash in all four themes (FR-007, FR-011, SC-004)

Use the headless harness recipe (Avalonia.Headless + Skia, `NotaThemeService.Set` by
reflection):
- construct `new SplashWindow()`, `Show()`, then `CaptureRenderedFrame()` for each theme:
  `studio-dark`, `studio-light`, `ember-graphite`, `ember-paper`;
- repeat at scale 2.

Expected:
- logo, "Nota", version and progress sit on the plain ground in the text region;
- the art is in its region, behind a 1px seam;
- the light themes give a light splash;
- lines are crisp at 2×.

## 4. Fallback (FR-010)

Temporarily make `Compose` throw (a local edit, not committed). Launch: the plain card
appears and the app starts normally.

## 5. Launch time (SC-005)

Compare launch → first splash paint against `develop` on the reference Mac (log a
timestamp in `App` before `splash.Show()` and in the splash's first `Render`). The
increase must be ≤ 50 ms.
