<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Logic Grey — implementation and validation

**Date**: 2026-11-01
**Branch**: `dev/windows-fixes`
**Status**: implemented; Windows build and validation passed.

## Result

Choose **Settings → Appearance → Dark → Logic Grey**. The existing picker applies and
persists it immediately; Follow system can retain it as the dark choice. Studio Dark
remains the default. Existing track/note/status hues, layout and editing behavior retain
their established roles. No audio, MIDI, plugin-host or native-engine changes were added
by this theme work. The previous Windows playhead work remains in the workspace.

The grey hierarchy starts with `#303033` ground, `#414145` panels, `#4A4A4E` raised fills
and `#535358` hover surfaces. The orange accent is `#E6A14B`; cursor is `#F1C45D`.
Secondary/disabled inks were lightened from the suggested starting palette to pass the
existing contrast bar. Raised controls share a very slight `#505054` → `#4A4A4E` gradient.
Original themes use equal stops and the exact original solid brush.

The third dark preset initially exceeded the Appearance row's viewport. That existing
ListBox now allows horizontal scrolling and fills its available width; selected cards
scroll into view. No DAW workspace panels, dimensions or controls were rearranged.

## Checks and evidence

| Check | Result | Evidence |
|-------|--------|----------|
| Windows x64 build, smoke suite and scanner checks | pass | `pwsh scripts/build-win.ps1` in the existing VS x64 developer environment; exit 0; `SMOKE TEST PASSED`, `SCAN SCALE PASSED`; local `validation/build-win.log` |
| All five theme files and readability pairings | pass | Full smoke run; 109 semantic tokens per theme; T3.1–T3.9 including the lighter raised top |
| Musical/status colour preservation | pass | Existing smoke guard compares the new palette to Studio Dark, including tracks, returns, note shading, meters and status fills |
| Original palette values | pass | `validation/palette-preservation.json`: all 428 pre-existing values across four themes unchanged, compared to `original-palettes.json` captured before edits |
| Real Appearance selection and reverse switch | pass | `validation/logic-grey-results.json`: actual picker updates resources/settings; Studio Dark returns to its solid fill |
| Restart persistence | pass | `validation/restart-results.json`, read by a separate process using the real SettingsService; a subsequent real app startup also verified its saved palette |
| Follow system | pass | Appearance smoke checks retain Logic Grey for dark and the existing light choice; no resolver/storage modifications |
| Track/note and serialized project data | pass | Actual-window probe compares track indices, MIDI notes and full ProjectService-captured serialized content before/after live switching |
| Arrangement, piano roll, mixer and Settings captures | pass | 26 PNGs: both themes, three views × four render scales, plus Appearance; inspected default and grey views, including 200% piano roll |
| High-refresh playback | pass on sampled machine | Studio Dark 238.58 vs Logic Grey 238.01 observed cursor changes/s; both zero reported dropouts, 48 kHz / 480-frame WASAPI |
| Generated design reference | pass | Installed Python 3.14 executable runs `scripts/design-html.py`; generated page includes Logic Grey, raised-fill shading and cursor token |
| Whitespace | pass | `git diff --check` |

The frame benchmark samples the actual Arrangement cursor property on animation callbacks
over five seconds. It is not a physical display/compositor measurement or a universal
performance guarantee. Both runs changed the cursor on every observed frame callback;
the roughly 0.24% difference is not evidence of a systematic rendering regression.
Wall-clock thresholds are deliberately absent from CI.

## Screenshots

- [Logic Grey Arrangement](validation/screenshots/logic-grey-arrangement-100.png)
- [Logic Grey piano roll](validation/screenshots/logic-grey-piano-roll-100.png)
- [Logic Grey mixer](validation/screenshots/logic-grey-mixer-100.png)
- [Logic Grey Appearance picker](validation/screenshots/logic-grey-appearance.png)
- Original-theme comparisons use the same names with `studio-dark-` instead of `logic-grey-`.
- Each view also has `-125.png`, `-150.png` and `-200.png` captures.

Screenshots are real Avalonia UI renders, not mockups. They use 1×/1.25×/1.5×/2× render DPI;
physical Windows per-monitor display-scaling settings were not changed. Full-screen
interaction at every physical monitor/DPI combination remains a manual check. Screenshot
files and generated application data/logs are local ignored artifacts; the validation
sources and small JSON results are retained for reproduction.

## Reproduce

After a Windows Release build, from the repository root:

```powershell
dotnet build specs/069-logic-grey-theme/validation/ThemeProbe/ThemeProbe.csproj
dotnet specs/069-logic-grey-theme/validation/ThemeProbe/bin/Debug/net10.0/ThemeProbe.dll capture logic-grey specs/069-logic-grey-theme/validation
dotnet specs/069-logic-grey-theme/validation/ThemeProbe/bin/Debug/net10.0/ThemeProbe.dll restart logic-grey specs/069-logic-grey-theme/validation
```

Use `capture studio-dark` for original-theme captures. The probe starts the real App,
opens its normal main window and actual Settings, piano-roll and mixer controls with
generated three-track content, applies the preset through the real picker, then closes.
Scratch settings live under `validation/probe-data/appearance`; welcome/update dialogs
are disabled. One early diagnostic attempted to replace Avalonia's lifetime after setup;
the corrected probe uses the normal lifetime from initialization. Its separate restart
reader avoids opening a splash with no message loop.

For playback measurements, build `validation/FrameProbe/FrameProbe.csproj`, then:

```powershell
dotnet specs/069-logic-grey-theme/validation/FrameProbe/bin/Debug/net10.0/FrameProbe.dll frames specs/069-logic-grey-theme/validation/frames-studio-dark.json 0 - studio-dark
dotnet specs/069-logic-grey-theme/validation/FrameProbe/bin/Debug/net10.0/FrameProbe.dll frames specs/069-logic-grey-theme/validation/frames-logic-grey.json 0 - logic-grey
```

Use `C:/Users/Dexter/AppData/Local/Programs/Python/Python314/python.exe scripts/design-html.py`
to regenerate the design reference on this host; the `python` launcher alone has no
registered runtime, although that installed interpreter works.

## Complete feature file list

Application and tests:

- `src/managed/Nota.App/Theme/Themes/logic-grey.json` — new complete preset.
- `src/managed/Nota.App/Theme/Themes/studio-dark.json`
- `src/managed/Nota.App/Theme/Themes/studio-light.json`
- `src/managed/Nota.App/Theme/Themes/ember-graphite.json`
- `src/managed/Nota.App/Theme/Themes/ember-paper.json` — original palettes add compatible cursor/raised-top values only.
- `src/managed/Nota.App/Theme/NotaColourTheme.cs` — register the preset.
- `src/managed/Nota.App/Theme/NotaPalette.cs` — live cursor/top slots and shared computed raised fill.
- `src/managed/Nota.App/Theme/NotaThemeService.cs` — publish computed raised fill at theme changes.
- `src/managed/Nota.App/Theme/NotaTheme.axaml` — shared raised bindings and theme-picker horizontal scrolling.
- `src/managed/Nota.App/ArrangementView.cs` — cursor brush; automation hovers retain the primary accent.
- `src/managed/Nota.App/PianoRollView.cs` — cursor brush only.
- `src/managed/Nota.App/AudioClipEditorView.cs` — cursor brush only.
- `src/managed/Nota.App/PreferencesWindow.cs` — preview cursor colour and constrained, scrollable preset row.
- `tests/Nota.SmokeTest/DesignTokenCheck.Themes.cs` — raised-top contrast, data preservation, compatible legacy defaults and preference roundtrip checks.

Documentation/workflow:

- `CHANGELOG.md`, `FEATURES.md`, `DESIGN.md`, `DESIGN.html`, `scripts/design-html.py`.
- `.specify/feature.json` — local active-feature pointer.
- `specs/069-logic-grey-theme/spec.md`, `checklists/requirements.md`, `plan.md`, `research.md`,
  `data-model.md`, `contracts/theme.md`, `quickstart.md`, `tasks.md`, `.gitignore`,
  `manual-handoff.md`, `validation.md`.
- `specs/069-logic-grey-theme/validation/ThemeProbe/{ThemeProbe.csproj,Program.cs}`.
- `specs/069-logic-grey-theme/validation/FrameProbe/{FrameProbe.csproj,Program.cs}`.
- Small JSON evidence in `validation/`: `original-palettes.json`, `palette-preservation.json`,
  `logic-grey-results.json`, `studio-dark-results.json`, `restart-results.json`,
  `frames-studio-dark.json`, `frames-logic-grey.json`.

Earlier playhead/ASIO source changes visible in the working tree are prior work, not new
theme changes. No commit or push was performed for this request.

## Coverage limits and documentation handoff

The selected Windows/WASAPI configuration was benchmarked; no additional ASIO, macOS,
Linux, third-party plugin-GUI or heavy-session performance claim is made for this theme.
Third-party GUI styling is intentionally outside scope. Physical Windows monitor-DPI
changes remain a manual check as described above.

The sibling `C:/Dev/nota-docs` repository is absent. The nota-docs workflow was inspected;
`manual-handoff.md` records the English/Russian Appearance page update to apply there.
All available in-repository design/feature/release documentation is updated.
