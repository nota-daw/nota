# Bug Fix: Scrollbars expand to wide unstyled Fluent appearance on hover

- **Slug**: scrollbar-hover-style
- **Fixed**: 2026-11-26
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

Replaced the partial `ScrollBar` style overrides in `NotaTheme.axaml` with a dedicated Nota `ControlTheme` for `ScrollBar` (`NotaScrollBarThumb` and `NotaScrollBarPageButton`), and hid `ScrollViewer`'s `PART_ScrollBarsSeparator`. Scrollbars now keep a fixed 9 px track (or explicit dimensions like `Height = 10`), a 5 px rounded pill thumb, a transparent track with no arrow buttons, and 120 ms ease-out colour-only transitions (`Brush.Rule` → `Brush.TextFaint` → `Brush.TextMuted`) across rest, hover, expansion, and press.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/Theme/NotaTheme.axaml` | modified | Added `NotaScrollBarPageButton`, `NotaScrollBarThumb`, and `ScrollBar` `ControlTheme`; replaced partial `ScrollBar` styles; hid `ScrollViewer /template/ Panel#PART_ScrollBarsSeparator`. |
| `tests/Nota.SmokeTest/DesignTokenCheck.cs` | modified | Added `ScrollBar` `ControlTheme` and `PART_ScrollBarsSeparator` regression check in `RunControls()`. |
| `CHANGELOG.md` | modified | Added `[Unreleased]` → `Fixed` entry referencing `#1113`. |

## Diff Highlights

```xml
<ControlTheme x:Key="{x:Type ScrollBar}" TargetType="ScrollBar">
  <Setter Property="Background" Value="Transparent"/>
  <Setter Property="MinWidth" Value="0"/>
  <Setter Property="MinHeight" Value="0"/>
  <Style Selector="^:vertical">
    <Setter Property="Width" Value="9"/>
    ...
```

## Tests Added or Updated

- `DesignTokenCheck.RunControls()` — verifies `NotaTheme.axaml` defines its own `ScrollBar` `ControlTheme` (`MinWidth="0"`, `MinHeight="0"`, 9 px vertical/horizontal size, `NotaScrollBarThumb`, no `TrackRect`/`PART_LineUpButton`) and hides `ScrollViewer /template/ Panel#PART_ScrollBarsSeparator`.

## Local Verification

- Commands run: pending full `scripts/build.sh` and headless Avalonia state checks (tracked in `./test.md`).

## Deviations from Assessment

- None.

## Follow-ups

- Verify end-to-end build and headless UI behaviour in `./test.md`.
