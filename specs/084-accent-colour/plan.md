# Implementation Plan: Choose the accent colour

**Branch**: `feat/accent-colour` | **Date**: 2026-11-05 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/084-accent-colour/spec.md` · issue
[nota-daw/nota#311](https://github.com/nota-daw/nota/issues/311) · design: [mockup/accent.html](mockup/accent.html)
(built by `mockup/build.py` from the theme files; approved 2026-11-05).

## Summary

Settings → Appearance gains an **Accent** row under the colour theme: seven presets (Orange —
the default — Lime, Aqua, Sky, Violet, Magenta, Pink) and a **Custom** button that opens a
colour picker popover (saturation/brightness area, hue strip, hex field, a per-theme preview
and a one-line note). The accent is one app preference worn in whichever theme is active.

It works as an **overlay on a theme, not a new theme**: a pure function in
`Nota.Application` takes a theme's colours and an accent choice and returns the theme's
accent tokens re-coloured — each token keeps its OKLCH lightness relation to the theme's own
orange, takes the new hue and a proportional chroma, and (only if the readability bar would
fail) the accent's lightness moves by the smallest step that passes. Orange returns the
theme file verbatim. `NotaThemeService` wears the *accented* theme, so the palette slots,
XAML brushes, Fluent's accent ramp, the theme cards and Nota Remote all follow with the
machinery spec 011 already has.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill: accent =
  live or chosen only; no literals in views; readability bar (§ Readability bar).
- [`specs/011-colour-themes`](../011-colour-themes/) — themes, `NotaThemeService`, the bar.
- [`specs/014-remote-studio`](../014-remote-studio/) — the phone wears the host's theme tokens.
- [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency — the picker is drawn
with Nota's own controls and a `WriteableBitmap`.

**Storage**: `settings.json` (`Settings.Accent`, `Settings.CustomAccent`).

**Audio-Thread Impact**: None — UI and settings only.

**Testing**: `tests/Nota.SmokeTest` — new `RunAccent` (derivation + resolution, all presets ×
all themes against the bar, identity for Orange, custom refusal cases), existing `RunThemes`,
`RunBans`, `RunLabels`, `RunLocalization`; headless-Avalonia screenshots for the PR.

**Target Platform**: macOS, Windows, Linux (desktop), Nota Remote (web page, unchanged).

**Project Type**: desktop app.

**Performance Goals**: a pick re-tints and repaints in < 100 ms (same path as a theme
switch); dragging in the picker stays smooth — the derivation is ~20 colour conversions ×
up to ~50 lightness steps, microseconds; the spectrum bitmap (200 × 120) is rebuilt only
when the hue changes.

**Constraints**: readability bar rows T3.4, T3.5, T3.8 plus the meter-green and chromatic
distance rules (research R3) for every accent × theme; with Orange, pixel-identical to today.

**Scale/Scope**: ~6 files touched, ~4 new; 1 new settings field pair; ~12 new localization keys.

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue first | #311 carries `approved` (added by core contributor ambertape, 2026-11-05). |
| I. Real-time audio safety | Not touched. |
| II. Boundary discipline | No engine/ABI change; derivation is pure in `Nota.Application`, Avalonia only in `Nota.App`. |
| III. Licensing hygiene | New files carry SPDX headers; no dependency. OKLCH/OKLab conversion is public maths (Björn Ottosson's published matrices), written from the formulas. |
| IV. Verification gate | `scripts/build.sh` + smoke test; new `RunAccent`. |
| V. One logical change | Accent choice only (track controls on the right are #72, separate). |
| VI. Docs in sync | CHANGELOG, FEATURES.md, DESIGN.md (accent section + accepted exception: the picker spectrum), user docs (nota-docs) EN + RU. |
| VII. Spec-first | This spec/plan/tasks. |
| VIII. Safety rules never weakened | The readability bar is enforced on every derived accent; a custom colour that cannot pass is refused, not worn. |

Gate: **PASS** (re-checked after Phase 1: PASS — no new violations).

## Project Structure

### Documentation (this feature)

```text
specs/084-accent-colour/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R8 decisions
├── data-model.md        # AccentChoice, presets, derivation, settings
├── quickstart.md        # how to verify end-to-end
├── contracts/
│   └── ui-contract.md   # the Accent row and the picker, states, keys
├── mockup/              # build.py → accent.html (approved design)
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   ├── Accent.cs                    # NEW: AccentChoice, presets, Resolve/Store, the derivation (pure)
│   ├── Settings.cs                  # + Accent, CustomAccent
│   └── Localization/en.json         # + preferences.accent*, accent.<preset> names
├── Nota.App/
│   ├── Theme/NotaAccent.cs          # NEW: Avalonia bridge — NotaColourTheme × AccentChoice → accented theme (cached)
│   ├── Theme/NotaThemeService.cs    # wears the accented theme; SetAccent; Worn(theme) for previews/Remote
│   ├── App.axaml.cs                 # accent resolved before the first paint
│   ├── PreferencesWindow.cs         # Accent row under the theme picker; cards preview Worn(theme)
│   ├── PreferencesWindow.Accent.cs  # NEW: swatch row, Custom button, picker flyout
│   ├── Controls/ColourPicker.cs     # NEW: SV area + hue strip (WriteableBitmap), hex field
│   └── MainWindow.Remote.cs         # Remote theme cache keyed by accent
tests/Nota.SmokeTest/
├── DesignTokenCheck.Accent.cs       # NEW: RunAccent
└── Program.cs                       # run it
DESIGN.md, DESIGN.html (scripts/design-html.py), CHANGELOG.md, FEATURES.md
```

**Structure Decision**: The derivation lives in `Nota.Application` (pure, hex/uint in and
out) so the smoke test can run every preset × theme without Avalonia, exactly like
`Appearance.Resolve`. The app layer only converts to `NotaColourTheme` and caches.

## Complexity Tracking

None.
