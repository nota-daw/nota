# Implementation Plan: App-wide Interface Scale

**Branch**: `027-ui-scale` | **Date**: 2026-10-19 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/027-ui-scale/spec.md` (issue nota-daw/nota#38)

## Summary

Add an **Interface scale** preference (75 / 100 / 125 / 150 / 200 %, default 100) to
Preferences → Appearance → UI. Avalonia has no app-wide scale, so each window's root
content is wrapped in a `LayoutTransformControl` with a uniform `ScaleTransform`,
driven by one static scale service. All popups (menus, tooltips, flyouts, dropdowns)
inherit the scale through Avalonia's own `Popup.InheritsTransform`, switched on
globally. Window minimums, default sizes and title-bar drag height are scaled and
capped at the screen. The native caption-button inset stays fixed on screen. UI-only;
no engine change. See [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill —
  the new picker reuses the *Waveform* segmented picker.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia 12.1.4 (existing) — `LayoutTransformControl`,
`ScaleTransform`, `Popup.InheritsTransform`. No new dependency.

**Storage**: `settings.json` via `Nota.Application.Settings` — new int `InterfaceScale`.

**Audio-Thread Impact**: None: UI-only change. No engine, C ABI or command-queue
changes.

**Testing**: Smoke test (`tests/Nota.SmokeTest`) gains a check of
`InterfaceScale.Normalize` / presets, next to the spec 011 Appearance checks in
`DesignTokenCheck.Themes.cs`. UI is validated by the [quickstart](quickstart.md)
(the real app or the headless Avalonia harness, with screenshots).

**Target Platform**: macOS, Windows, Linux desktop.

**Project Type**: desktop app.

**Performance Goals**: A scale switch redraws in < 1 s (SC-002). At 100 % the
transform is the identity, with no measurable cost.

**Constraints**: 100 % must render exactly as today. Text stays vector-sharp. Hit
areas follow the drawing.

**Scale/Scope**: 2 window roots (`MainWindow`, `NotaWindow` → 14 subclasses), every
popup via one global default, 1 Preferences row, 1 setting.

## Constitution Check

| Principle | Status |
|---|---|
| I. Real-time audio safety | ✅ No audio-thread code touched |
| II. Boundary discipline | ✅ No C ABI change; no JUCE |
| III. Licensing hygiene | ✅ No dependency; new files get the SPDX header |
| IV. Verification gate | ✅ `scripts/build.sh` + smoke check for the pure helper |
| V. One logical change | ✅ One feature, one PR |
| VI. Docs in sync | ✅ CHANGELOG `[Unreleased]` *Added*; `nota-features`, `nota-docs` (Preferences → Appearance page EN+RU) |
| VII. Spec-first | ✅ This spec/plan |
| VIII. No rule weakening | ✅ n/a |

Post-design re-check: unchanged — all pass.

## Design

### 1. Pure setting — `Nota.Application`

- `Settings.InterfaceScale` (int, default 100), doc comment in the style of
  `ArrangementWaveform`.
- New `InterfaceScale.cs`: `Presets = {75, 100, 125, 150, 200}`,
  `Normalize(int) → int` (non-preset → 100), `Factor(int) → double`.

### 2. Scale service — `Nota.App/Theme/NotaUiScale.cs` (new)

Static, mirroring `NotaThemeService`:
- `double Factor`, `void Set(int percent)`, `void Register(Window w, WindowScaleInfo)`.
- Per registered window it holds the window's `LayoutTransformControl`, its design
  minimum size, design title-bar height and the caption-inset element. On `Set`, it
  updates each window's `ScaleTransform`, sets `ExtendClientAreaTitleBarHeightHint =
  design × s` and `MinWidth/MinHeight = min(design × s, screen working area)`, grows
  the frame if it is now below the minimum (staying on screen), and sets the caption
  inset to `78 ÷ s` (macOS) or `180 ÷ s` (Windows).
- On first open it multiplies the design `Width`/`Height` by `s` (capped at the
  screen). It skips `SizeToContent` axes.
- Weak references; unregisters on `Closed`.
- At startup: `Popup.InheritsTransformProperty.OverrideDefaultValue<Popup>(true)`
  (research R2).

### 3. Window roots

- `NotaWindow`: wrap the existing root `DockPanel` in a `LayoutTransformControl`,
  register with the service, and hand it the caption-inset `Grid` margin and title-bar
  height.
- `MainWindow`: same for its root from `MainWindow.axaml` (wrap in code after
  `InitializeComponent`), with title bar 32, design size 1200×800 and minimum
  1100×680.
- `SplashWindow`: untouched.

### 4. Startup

`App.OnFrameworkInitializationCompleted`: next to `NotaThemeService.Set(...)`, call
`NotaUiScale.Set(settings.Current.InterfaceScale)` before `new MainWindow`.

### 5. Preferences

`PreferencesWindow.AppearancePane()`, **UI** section: add the
`Row("Interface scale", ScalePicker())` and caption rows after *Waveform*, following
[contracts/ui-contract.md](contracts/ui-contract.md). `ScalePicker` is built like
`WaveformPicker`. On change: save the setting, then `NotaUiScale.Set`.

### 6. Docs

`CHANGELOG.md` `[Unreleased]` → Added: "Interface scale in Preferences → Appearance
(75–200 %) — makes the whole UI larger or smaller (#38)". `FEATURES.md` via
`nota-features`. The user manual Appearance page (EN+RU) via `nota-docs`.

## Project Structure

### Documentation (this feature)

```text
specs/027-ui-scale/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/ui-contract.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Application/
│   ├── Settings.cs                 # + InterfaceScale
│   └── InterfaceScale.cs           # NEW — presets + Normalize (pure)
└── Nota.App/
    ├── App.axaml.cs                # apply scale at startup, popup default
    ├── NotaWindow.cs               # wrap root, register
    ├── MainWindow.axaml.cs         # wrap root, register
    ├── PreferencesWindow.cs        # Interface scale row
    └── Theme/NotaUiScale.cs        # NEW — scale service
tests/Nota.SmokeTest/
└── DesignTokenCheck.Themes.cs      # + InterfaceScale checks (or a sibling file)
```

**Structure Decision**: The UI in this repo lives in `Nota.App` (not
`Nota.Presentation`), next to `Theme/NotaThemeService.cs`. The pure setting logic
goes in `Nota.Application`, like `Appearance.cs`.

## Risks

- **Seam softness at fractional scales** (R5) — accepted; checked in quickstart step 4.
- **Size jump on first show** (R4) — fallback described in research.
- **Layouts tighter than designed** at large scale on small screens — accepted by
  the spec edge case; window minimums capped at the screen.

## Complexity Tracking

None — no constitution violations.
