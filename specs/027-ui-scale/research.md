# Research: App-wide Interface Scale

**Feature**: [spec.md](spec.md) · **Date**: 2026-10-19

All facts below were checked against Avalonia **12.1.4** (the version in
`Nota.App.csproj`) and the Nota sources on `develop` (bf354060).

## R1 — How to scale the whole UI

**Decision**: Wrap each window's root content in a `LayoutTransformControl` whose
`LayoutTransform` is a `ScaleTransform(s, s)`, with `s` coming from one app-wide
service. A layout transform (not a render transform) makes children measure and
arrange in *logical* space (window size ÷ s), so layouts reflow instead of being
stretched or clipped.

**Rationale**:
- Avalonia 12 has **no app-wide scale API**. `TopLevel.RenderScaling` and
  `WindowBase.DesktopScaling` are read-only and come from the platform
  (`ITopLevelImpl`, a private API we must not implement). `AVALONIA_GLOBAL_SCALE_FACTOR`
  exists only on X11 and needs a restart.
- `LayoutTransformControl` is a built-in control. Avalonia uses it for this exact
  purpose: the Fluent `PopupRoot` template wraps its whole content in one (see R2).
- Drawing stays vector: Skia gets the full transform, so text and paths are
  rasterised at the final size and stay sharp (FR-007). Nota has no
  `RenderTargetBitmap` caches. The one `WriteableBitmap` (Lens spectrogram) is a data
  image that is stretched at every scale anyway.
- Hit-testing and `TranslatePoint` go through the transform, so pointer hits land
  where things are drawn (FR-004). Drag and drop, the piano roll and the arrangement
  all use control-local coordinates.

**Alternatives considered**:
- *Multiply every size/font by a factor* — hundreds of hard-coded pixel values in
  custom-drawn views (`ArrangementView`, `PianoRollView`, device bodies). Rejected:
  huge and would never be complete.
- *Override platform render scaling* (custom `IWindowImpl` wrapper) — private,
  unstable API, per-platform. Rejected.
- *A `RenderTransform` on the root* — content would not reflow; at 200 % it would
  draw off-window. Rejected.

## R2 — Menus, tooltips, flyouts, dropdowns

**Decision**: At startup, call
`Popup.InheritsTransformProperty.OverrideDefaultValue<Popup>(true)`.

**Rationale**: Every popup in Avalonia — `ToolTip`, `ContextMenu`, `PopupFlyoutBase`
(`MenuFlyout`, `Flyout`) and the `ComboBox` template's `PART_Popup` — is a `Popup` that
opens a separate `PopupRoot` window (Nota does not use overlay popups). In 12.1.4,
`Popup.UpdateHostSizing` computes the placement target's scale against its top level
when `InheritsTransform` is true. It then sets `PopupRoot.Transform`, and the Fluent
`PopupRoot` template binds that to its own `LayoutTransformControl`. Explicit popup
`Width`/`MinWidth` are scaled too. ToolTip, ContextMenu and the flyouts create their
`Popup` in code, so a style setter would not reach them reliably; overriding the
property's default does. Popups follow the scale of whatever opened them. No
per-popup code is needed, and a later scale change applies on the next open.

**Alternatives considered**: a global `Style Selector="Popup"` (misses popups created
in code before they join a styled tree); wrapping every flyout's content by hand (88
files use tooltips, 47 use flyouts). Rejected.

## R3 — Where the window transform lives

**Decision**: In code, in the two window roots: `NotaWindow` (14 subclasses:
Preferences, Detail, Device, Mixer, Confirm, Export, …) and `MainWindow`. Each wraps
its existing root panel in a `LayoutTransformControl` and registers with the scale
service. The service updates the transform of every live window when the scale
changes. `SplashWindow` is left alone: it shows before settings load (spec edge case).

**Rationale**: Nota builds windows in code; the two roots already own the body host
and the custom title bar. Keeping the change there means no `Window` control-theme
override, so Fluent updates keep working. The window's `VisualLayerManager`
(adorners, overlay layer) stays outside the transform. Nota uses neither adorners nor
overlay popups, and Avalonia's `AdornerLayer` follows its adorned element's
transform.

## R4 — Window frame, title bar and minimum size

**Decisions**:
1. **Frame size is in screen units.** A live scale change keeps every open window's
   on-screen size; contents reflow (logical size = frame ÷ s).
2. **Minimums scale.** A window's design minimum (for example `MainWindow`
   `MinWidth=1100`/`MinHeight=680`, `DetailWindow` 520×400) is logical. The applied
   minimum is `design × s`, capped at the current screen's working area. If the frame
   is then smaller than the new minimum, it grows, staying on screen.
3. **Default sizes of new windows scale.** A window's design `Width`/`Height` (for
   example `MainWindow` 1200×800) is multiplied by `s` the first time it opens, and
   capped at the screen. `SizeToContent` windows need nothing: the transform reports
   the scaled desired size. Nota stores no window sizes in settings, so nothing is
   scaled twice.
   **Exception (convergence T028):** a window the user cannot resize (`CanResize = false`
   — About, Welcome, What's New, Export, Confirm, …) would clip its content on a live
   scale-up, and the user could not fix it. Its frame follows the scale instead:
   `design size × s`, capped at the screen, except for axes sized to content.
4. **Title bar.** `ExtendClientAreaTitleBarHeightHint` (32, or the device window's
   own height) becomes `design × s`, so the drag area matches the scaled bar. The
   reserved inset for the native caption buttons stays fixed on screen, because the
   OS draws them at system size: macOS traffic lights 78 px → `78 ÷ s` logical,
   Windows caption buttons 180 px → `180 ÷ s` logical. Linux keeps the native frame;
   nothing to adjust.

**Rationale**: SC-003 and the "window bigger than screen" edge case. Capping at the
working area is why 200 % on a small screen still fits (spec edge case).

**Risk**: setting design sizes before the first show. `NotaWindow` subclasses set
`Width`/`MinWidth` in their own constructors, *after* the base constructor. Apply the
scaling in `OnOpened`-adjacent code that runs before the native window is visible
(`Window.ShowCore` runs the initial layout pass, and with it
`OnApplyTemplate`/`TemplateApplied`, before `PlatformImpl.Show`). If a visible jump
shows up, fall back to wrapping the constructor values through a small
`DesignSize(...)` helper the subclasses call.

## R5 — Hairlines and pixel snapping

**Finding**: `UseLayoutRounding` rounds to device pixels using the *top level's*
render scaling. It does not see a `LayoutTransformControl`, so inside a 125 % transform
a 1 px Studio seam is 1.25 device px and can look slightly soft or uneven. That is
inherent to transform scaling. Nota's custom-drawn views do their own snapping
(~99 `+ 0.5`/snap sites) in logical units.

**Decision**: Accept for this feature and check visually at 75 / 125 / 150 %
(quickstart step 4). 100 % must be pixel-identical to today (s = 1 → identity
transform). If seams at 125 % are clearly worse, a follow-up can make the shared
snapping helpers scale-aware. That is not in this scope.

## R6 — Storage and validation

**Decision**: A new `int InterfaceScale { get; set; } = 100` in
`Nota.Application.Settings` (percent; `settings.json` key `InterfaceScale`). A pure
helper `Nota.Application.InterfaceScale` holds the preset list `{75, 100, 125, 150,
200}` and `Normalize(int)` → the stored value if it is a preset, else 100 (FR-008).
It follows the spec 011 `Appearance` pattern: no UI, no I/O, covered by the smoke test.

**Rationale**: Percent integers are exact (no float drift in JSON) and read naturally
in the file. Presets only, per spec assumption.

## R7 — Applying before the first frame

**Decision**: `App.OnFrameworkInitializationCompleted` already resolves the colour
theme from settings before creating `MainWindow` (`NotaThemeService.Set(...)`). Set
the scale service from settings at the same point, so the main window opens at the
stored scale (FR-005).

## R8 — Audio thread

None. This is a UI-only change; the engine, the C ABI and the smoke-test engine paths
are untouched (FR-009, SC-005).
