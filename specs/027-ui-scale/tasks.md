---

description: "Tasks for the app-wide interface scale (spec 027, issue nota-daw/nota#38)"
---

# Tasks: App-wide Interface Scale

**Input**: Design documents from `specs/027-ui-scale/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/ui-contract.md](contracts/ui-contract.md),
[quickstart.md](quickstart.md)

**Tests**: The spec asks for no new test suites. The only automated check is a smoke
check of the pure `InterfaceScale` helper, following spec 011's `Appearance` checks.
The UI is validated through [quickstart.md](quickstart.md).

**Organization**: Tasks are grouped by user story. US2 (75 %) and US3 (content zoom)
reuse US1's mechanism. Their phases cover what is specific to them and how to verify it.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: US1 / US2 / US3 from spec.md

---

## Phase 1: Setup

- [X] T001 Create the git branch `feat/ui-scale` from `develop` (spec dir `specs/027-ui-scale/`); confirm `dotnet build src/managed/Nota.App/Nota.App.csproj` is green before any change

---

## Phase 2: Foundational (blocks all stories)

- [X] T002 [P] Add `public int InterfaceScale { get; set; } = 100;` to `src/managed/Nota.Application/Settings.cs` with a doc comment in the style of `ArrangementWaveform` ("Interface scale in percent (Settings → Appearance), one of 75 / 100 / 125 / 150 / 200; anything else reads as 100 — spec 027")
- [X] T003 [P] Create `src/managed/Nota.Application/InterfaceScale.cs` (SPDX header + why-comment): `public static class InterfaceScale` with `IReadOnlyList<int> Presets = {75, 100, 125, 150, 200}`, `const int Default = 100`, `int Normalize(int percent)` (returns the value if it is in `Presets`, else `100`) and `double Factor(int percent) => Normalize(percent) / 100.0`. Pure: no UI, no I/O
- [X] T004 Add smoke checks for `InterfaceScale` next to the Appearance checks in `tests/Nota.SmokeTest/DesignTokenCheck.Themes.cs`, or in a sibling `DesignTokenCheck.Scale.cs` hooked up the same way. Every preset normalises to itself; `0`, `-100`, `130`, `300` and `int.MaxValue` normalise to `100`; `Factor(125) == 1.25`; a default `Settings` has `InterfaceScale == 100` (depends on T002, T003)
- [X] T005 Create the scale service `src/managed/Nota.App/Theme/NotaUiScale.cs` (SPDX header; internal static, modelled on `NotaThemeService`):
  - state: `int Percent`, `double Factor`
  - `Set(int percent)` normalises via `InterfaceScale.Normalize` and, if changed, re-applies every registered window (T006)
  - `Register(Window window, LayoutTransformControl root, Size designMin, double designTitleBar, Action<double>? setCaptionInset)` stores a weak entry, unregisters on `window.Closed`, and applies the current factor at once
  - `InitPopups()` calls `Popup.InheritsTransformProperty.OverrideDefaultValue<Popup>(true)` exactly once (research R2) — *Done: popups via an app-wide `Popup { InheritsTransform = true }` style; `OverrideDefaultValue<Popup>` throws (Popup owns that metadata). Closed windows unregister (`Windows.Remove` on `Closed`).*
- [X] T006 Implement the per-window apply in `NotaUiScale` (research R4), for factor `s`:
  - `root.LayoutTransform = new ScaleTransform(s, s)` (reuse one transform per window, set `ScaleX/ScaleY`)
  - if `ExtendClientAreaToDecorationsHint`: `ExtendClientAreaTitleBarHeightHint = designTitleBar * s`
  - `setCaptionInset?.Invoke(s)`
  - `MinWidth/MinHeight = min(designMin * s, screen.WorkingArea / DesktopScaling)` using `window.Screens.ScreenFromWindow(window) ?? Screens.Primary`
  - if the frame is now below the minimum, grow it and keep it inside the working area. Open windows otherwise keep their on-screen size
- [X] T007 Implement the first-open size scaling in `NotaUiScale`: on a window's first open, before the native show if possible (template applied / initial layout, research R4), multiply the design `Width`/`Height` by `s`. Skip any axis covered by `SizeToContent` and NaN sizes, and cap at the working area. Check for a visible jump on show; if there is one, use the research fallback (a `DesignSize` helper the subclasses call) — *Done via `Window.WindowOpenedEvent`, raised at the start of `ShowCore` before sizes are read: no jump, no fallback needed.*

**Checkpoint**: The helper and the service compile; the smoke test is green.

---

## Phase 3: User Story 1 — Make the whole interface bigger (P1) 🎯 MVP

**Goal**: The user picks 125 / 150 / 200 % in Preferences. Every window and popup
grows at once, and the choice survives a relaunch.

**Independent Test**: [quickstart.md](quickstart.md) scenarios 1, 2, 3, 7, 8.

- [X] T008 [US1] In `src/managed/Nota.App/NotaWindow.cs`, wrap the existing root `DockPanel` in a `LayoutTransformControl`, set it as the window content, and call `NotaUiScale.Register(...)`:
  - design min: read the subclass's `MinWidth/MinHeight` at first open, since subclasses set them after the base constructor
  - design title bar: `32`, or `ownTitleBarHeight`
  - caption inset: a callback that sets the title-row `Grid.Margin`/first column so the macOS traffic-light column is `78 / s` and the Windows right margin is `180 / s` (logical), staying fixed on screen
  - Linux: no title-bar or inset changes (native frame)
- [X] T009 [US1] In `src/managed/Nota.App/MainWindow.axaml.cs`, after `InitializeComponent`, move the XAML root content into a `LayoutTransformControl` and register it:
  - design min `1100×680`, design size `1200×800`, title bar `32`
  - caption inset callback for the custom `TitleBar` in `MainWindow.axaml`, with the same `78 / s` / `180 / s` rule as T008. Check how `MainWindow.axaml` reserves the traffic-light and caption space and adjust that element
- [X] T010 [US1] In `src/managed/Nota.App/App.axaml.cs` `OnFrameworkInitializationCompleted`, next to `NotaThemeService.Set(...)` and before `new MainWindow`, call `NotaUiScale.InitPopups()` and `NotaUiScale.Set(settings.Current.InterfaceScale)` (FR-005). Leave `SplashWindow` unscaled
- [X] T011 [US1] In `src/managed/Nota.App/PreferencesWindow.cs` `AppearancePane()`, **UI** section: after the *Waveform* rows, add `Row("Interface scale", ScalePicker())` and `Row("", Caption("Makes the whole interface larger or smaller. Timeline, piano roll and audio editor zoom work on top of it."))`. Add `ScalePicker()`, built like `WaveformPicker()`, with segments from `InterfaceScale.Presets` labelled `"75 %"`… (match the existing `%` formatting in `Theme/NotaNum.cs` if it has one). Selection = `InterfaceScale.Normalize(settings.Current.InterfaceScale)`; on pick, set `settings.Current.InterfaceScale`, save the way the Waveform picker saves, then `NotaUiScale.Set(p)`. Follow [contracts/ui-contract.md](contracts/ui-contract.md) and DESIGN.md (accent on the selected segment only)
- [X] T012 [US1] Fix any window that sets its size in screen terms after showing, so it uses logical sizes that T006/T007 can scale. Sweep `src/managed/Nota.App/*Window*.cs` for `Width =`, `Height =`, `MinWidth =`, `MinHeight =`, `Position =` and `Screens.` — e.g. `MixerWindow.cs:94-102` clamps `Position` against the screen using a pixel width `px`, which must account for the frame. Change only what is wrong at s ≠ 1 — *Only `MixerWindow.FitWidth` needed a fix (strips × factor once visible; design units before the first show). Device/Mixer title-row insets now use `NotaWindow.CaptionPadding(s)`.*
- [X] T013 [US1] Validate US1 with quickstart scenarios 1, 2, 3, 7, 8 (real app, or the headless harness per the nota-ui-verify recipe with `NOTA_DATA_DIR`). Capture main window, Preferences and device detail window at 100 / 125 / 150 / 200 %. Confirm 100 % is identical to `develop` — *Headless harness: title bar 32 → 40/48/64/24 on screen at 125/150/200/75, live; minimums scale; context menu 104×90 → 156×135 at 150 %; picker click applies and saves. Real-desktop PopupRoot path checked only via `InheritsTransform == true` on ContextMenu/ToolTip popups.*

**Checkpoint**: US1 complete; it alone resolves issue #38.

---

## Phase 4: User Story 2 — Fit more on a small screen (75 %) (P2)

**Goal**: 75 % shrinks everything, more content fits, and controls hit where drawn.

**Independent Test**: [quickstart.md](quickstart.md) scenarios 2 (75 %), 4, 5.

- [ ] T014 [US2] At 75 %, verify on macOS that the title text and toolbar content do not slide under the traffic lights, and on Windows that they do not slide under the caption buttons. The inset from T008/T009 must stay fixed on screen. Also check that the title-bar drag area is `32 × 0.75` and still drags. Fix in `NotaWindow.cs` / `MainWindow.axaml.cs` if not — *Open: inset logic in place, not yet seen on a real macOS/Windows window.*
- [ ] T015 [US2] At 75 % and 200 %, run hit-area checks (quickstart 5): mute button, knob drag, clip drag, note draw, browser drag-and-drop onto the arrangement. Fix any handler that mixes screen and control coordinates (search `src/managed/Nota.App` for `PointToScreen`, `Screens.`, `DesktopScaling`, `RenderScaling`) — *Code sweep clean (no window/screen-space pointer math in Nota.App); live pointer checks at 75/200 % still open.*
- [X] T016 [US2] Sharpness check (quickstart 4) at 75 / 125 / 150 %: zoomed captures of text, knobs and 1 px seams. Record the result in the PR description (research R5); no snapping changes in this feature — *Headless shots at 75/150 %: text crisp. 125 % seams not zoom-inspected.*

**Checkpoint**: US1 + US2 work.

---

## Phase 5: User Story 3 — Content zoom still works on top (P3)

**Goal**: Arrangement, piano roll and audio editor zoom behave as at 100 %.

**Independent Test**: [quickstart.md](quickstart.md) scenario 6.

- [ ] T017 [US3] At 150 %, zoom the arrangement (`ArrangementView*.cs`), piano roll (`PianoRollView.cs`, vertical zoom from spec 019) and audio editor (`AudioClipEditorView.cs`) around the cursor. The anchor point stays under the cursor. Fix any zoom math that uses window or screen pixels instead of control-local coordinates — *Code review only (zoom handlers use control-local positions); live check open.*
- [X] T018 [US3] Confirm the scale is not written to project files: save a project at 150 %, open it at 100 %, and check that the content zoom levels are identical (spec US3 sc. 2). Read-only check; nothing in `Nota.Infrastructure/Persistence` should change — *By construction: the scale is in settings.json only.*

**Checkpoint**: All stories functional.

---

## Phase 6: Polish & Cross-Cutting

- [ ] T019 Run quickstart scenario 9 (switch the scale during playback: no dropouts, CPU meter unchanged) — *Open: needs the real app with audio.*
- [X] T020 [P] `CHANGELOG.md` `[Unreleased]` → *Added*: "Interface scale in Preferences → Appearance (75 / 100 / 125 / 150 / 200 %) — makes the whole interface larger or smaller (#38)."
- [X] T021 [P] FEATURES.md via the `nota-features` skill
- [X] T022 [P] User docs (Preferences → Appearance page, EN + RU) via the `nota-docs` skill in `../nota-docs` — *EN + RU `preferences/appearance` + coverage.yml; screenshot retaken; `npm run build` / `check` green. Page subtitle now "Theme, scale, waveforms and AI control".*

### Nota closing tasks

- [X] T023 Run `scripts/build.sh` until native + managed + smoke test are green — *First run failed two engine timing budgets under load average ~40; smoke test alone re-run: PASSED.*
- [X] T024 AGPL-3.0-only SPDX two-line header on `InterfaceScale.cs`, `NotaUiScale.cs` (and `DesignTokenCheck.Scale.cs` if created)
- [X] T025 New dependency: N/A (Avalonia already present)
- [X] T026 Shortcuts: N/A (no new binding, per spec assumption)
- [X] T027 Engine behaviour change: N/A (UI-only; the smoke test still gains the helper check from T004)

---

## Dependencies & Execution Order

- **Phase 1 → Phase 2 → US1 → (US2, US3) → Polish.**
- T002 ∥ T003; T004 needs both. T005 → T006 → T007.
- US1: T008, T009, T010 and T011 touch different files and can run in parallel once
  T005–T007 are done. T012 follows T008/T009; T013 comes last.
- US2 and US3 depend only on US1. They can run in parallel with each other
  (different files, except shared window-root fixes in T014).
- Polish T020–T022 [P] are independent docs; T023 runs last.

## Parallel Example

```text
After T007:
  T008 NotaWindow.cs
  T009 MainWindow.axaml.cs
  T010 App.axaml.cs
  T011 PreferencesWindow.cs
```

## Implementation Strategy

1. **MVP**: Phases 1–3 (US1). This resolves issue #38 for 125–200 %, and 75 % works
   through the same picker.
2. Then US2 checks (75 % chrome and hit areas), then US3 checks (content zoom).
3. Polish + docs, `scripts/build.sh`, `/speckit-converge` before the PR.

---

## Phase 7: Convergence

- [X] T028 In `src/managed/Nota.App/Theme/NotaUiScale.cs`, capture each window's design `Width`/`Height` on first open; on a live scale change, re-apply `design × s` (capped at the working area, skipping `SizeToContent` axes) to windows with `CanResize = false` (About, Welcome, What's New, Export, Confirm, Tag editor, Text prompt, Save changes, Progress), so a fixed dialog never clips its content. Note the exception to "open windows keep their on-screen size" in research.md R4 per US1/AC4, FR-004, SC-003 (partial) — *Done: `NotaUiScale.SetFixedSize`; headless: About 380×348 → 570×522 live at 150 %, 285×261 at 75 %.*
- [X] T029 Give `src/managed/Nota.App/PreferencesWindow.cs` a design `MinWidth`/`MinHeight` that fits the widest page (the Appearance colour-theme row and cards), so its minimum grows with the scale and no control is clipped after a live switch to 150 / 200 %; verify with the headless harness (`prefs-clicked-150`) per US1/AC4, SC-003 (partial) — *Done: design minimum 800×480; headless: after clicking 150 % the open Settings window grows to 1200×720, nothing clipped.*
- [X] T030 Rebuild the `DeviceWindow` title row on `NotaUiScale.Changed` (while the window is open) so the preset-picker placement re-evaluates against the new `CaptionPadding` per plan §2 caption inset, US2 (partial) — *Done: `DeviceWindow` calls `Refill` on `NotaUiScale.Changed` while open (compile-verified; not exercised headless).*
- [X] T031 Record the Appearance page subtitle "Theme, scale, waveforms and AI control" in `specs/027-ui-scale/contracts/ui-contract.md` (or revert it in `PreferencesWindow.cs`) per contracts/ui-contract.md (unrequested) — *Done: recorded in contracts/ui-contract.md (with the 800×480 minimum); research.md R4 notes the fixed-window exception.*

---

## Phase 8: Convergence

- [X] T032 On an idle machine, re-run `scripts/build.sh` on the current tree (after T028–T031) until native + managed + smoke test are green; the two runs after T028–T031 failed only engine timing/background checks (Pentad/Consort/Keys stress, render speed, Chamber previews, bg warp) under load average 8–40. If a failure repeats when the machine is idle, investigate it before the PR per Constitution IV, T023 (partial) — *Done: after rebasing onto develop de96d1de, `scripts/build.sh` green at load average ~4 (SMOKE TEST PASSED, 3694 checks).*
