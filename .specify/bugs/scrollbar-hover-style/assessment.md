# Bug Assessment: Scrollbars expand to wide unstyled Fluent appearance on hover

- **Slug**: scrollbar-hover-style
- **Created**: 2026-11-26
- **Source**: https://github.com/nota-daw/nota/issues/1113 (host: github.com, allowlisted; created and approved in this session)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

When hovering over a scrollbar in Nota, it becomes unstyled and wide (16 px) instead of keeping Nota's thin muted scrollbar styling. Issue: https://github.com/nota-daw/nota/issues/1113 (`bug`, `area: ui`, `approved`).

## Symptom

At rest, scrollbars appear thin because Avalonia `FluentTheme` scales the `Thumb` down with `VerticalSmallScrollThumbScaleTransform` / `HorizontalSmallScrollThumbScaleTransform`. On hover (and once `ShowDelay` triggers `IsExpanded = true`, or immediately on `Thumb:pointerover`), the scrollbar expands to 16 px wide/tall and reveals `FluentTheme`'s unstyled track background, arrow `RepeatButton`s, translucent white/black thumb fills, and `ScrollViewer`'s corner separator.

## Reproduction

1. Open any scrollable panel in Nota (e.g., Browser, Settings, Mixer, Device Chain, or a zoomed Piano Roll / Audio Clip Editor).
2. Move the pointer onto the scrollbar and hover past `ShowDelay` (500 ms), or hover over the thumb.
3. Observe the scrollbar expanding to 16 px with Fluent's `#1f1f1f` / `#e6e6e6` track fill, arrow buttons, and `#66ffffff` / `#66000000` thumb color.

## Suspected Code Paths

- `src/managed/Nota.App/Theme/NotaTheme.axaml:1411-1437` — applies `<Style Selector="ScrollBar ...">` rules on top of `FluentTheme`'s `ScrollBar` `ControlTheme` instead of providing a Nota `ControlTheme`.
- `src/managed/Nota.App/PreferencesWindow.Downloads.cs:90-91` — assumes vertical `ScrollBar` width is `9` (`ScrollBarWidth = 9`), which was overridden by `FluentTheme`'s `MinWidth = ScrollBarSize` (`16`).
- `src/managed/Nota.App/BrowserView.cs:1052` — reserves `Gutter = 9` for the vertical scrollbar.
- `src/managed/Nota.App/AudioClipEditorView.cs:201` and `src/managed/Nota.App/PianoRollView.cs:499` — create standalone horizontal `ScrollBar`s with `Height = 10`, which were clamped to `16` by `MinHeight = ScrollBarSize`.

## Root Cause Hypothesis

`NotaTheme.axaml` loads after `FluentTheme` in `App.axaml` and only adds `<Style>` rules for `ScrollBar`, `ScrollBar /template/ Thumb`, and `ScrollBar /template/ RepeatButton`. Because `FluentTheme`'s `ScrollBar` `ControlTheme` defines `MinWidth`/`MinHeight = {DynamicResource ScrollBarSize}` (16), uses `FluentScrollBarThumb` (whose `^:pointerover /template/ Border` overrides `Thumb.Background`), and defines `^[IsExpanded=True]` styles that scale `Thumb` back to 1.0 and set `Opacity = 1` on `Rectangle#TrackRect` and arrow `RepeatButton`s (plus `ScrollViewer`'s `PART_ScrollBarsSeparator`), hovering/expanding overrides Nota's styles and widens the bar to 16 px. Confidence: high (verified directly in headless Avalonia 12.1.4 inspection).

## Proposed Remediation

**Preferred**: Define dedicated `<ControlTheme>` resources for `ScrollBar`, its thumb (`NotaScrollBarThumb`), and its transparent page buttons (`NotaScrollBarPageButton`) inside `src/managed/Nota.App/Theme/NotaTheme.axaml` (matching how `MenuItem` is themed in `NotaTheme.axaml`), and hide `ScrollViewer /template/ Panel#PART_ScrollBarsSeparator`. Keep the 9 px scrollbar footprint (`MinWidth`/`MinHeight` = 0, `:vertical` `Width="9"`, `:horizontal` `Height="9"`), 5 px rounded pill thumb via `Margin="2"`, transparent track without arrows, and 120 ms ease-out color-only state transitions (`Brush.Rule` → `Brush.TextFaint` → `Brush.TextMuted`).

**Alternatives**:
- Overriding `FluentTheme`'s `^[IsExpanded=True]`, `ScrollBarSize`, and `FluentScrollBarThumb` resources/selectors piecemeal — brittle across Avalonia updates and still leaves hidden 16 px arrow buttons occupying track ends.

**Files likely to change**:
- `src/managed/Nota.App/Theme/NotaTheme.axaml`
- `tests/Nota.SmokeTest/DesignTokenCheck.cs`
- `CHANGELOG.md`

**Tests to add or update**:
- Structural check in `DesignTokenCheck` verifying `ScrollBar` defines its own Nota `ControlTheme` in `NotaTheme.axaml` (no Fluent `ScrollBar` hover expansion) and `PART_ScrollBarsSeparator` stays unstyled-fill-free.
- Headless Avalonia verification exercising `ScrollViewer` (both scrollbars, rest, `:pointerover`, `IsExpanded = true`, `AllowAutoHide = false`, `Thumb:pressed`, track page click) and standalone `ScrollBar` (`Height = 10`) across built-in dark/light themes.

## Risks & Considerations

- `Track` on vertical scrollbars must keep `IsDirectionReversed="True"` so `Value = 0` stays at the top.
- `DesignTokenCheck` enforces no colour literals, no `Opacity` used as a state in `.cs`/`.axaml`, valid `Radius.*` and `Brush.*` keys, and `NotaSkin` corner radius compatibility.
- Standalone `Height = 10` horizontal `ScrollBar`s in `AudioClipEditorView` and `PianoRollView` must still take their explicit height (`10`) rather than being clamped.

## Open Questions

- None.
