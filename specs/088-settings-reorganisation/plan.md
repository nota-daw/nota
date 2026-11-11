# Implementation Plan: Settings reorganisation — every setting where you'd look for it

**Branch**: `feat/settings-reorganisation` | **Date**: 2026-11-10 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/088-settings-reorganisation/spec.md`

## Summary

The Settings window gets five new pages (Outputs, Inputs, AI control (MCP), Language, Editing)
and a new sidebar group (Remote control); Appearance and Audio shrink to what their names say.
Each existing section is moved, not rewritten: the MCP block, the language picker, the
piano-roll / split-fade / detail-panel switches and the Inputs list keep their code and their
settings keys. The window stops selecting pages by position — every page is found by its stable
`Id`, so the hard-coded indices (`RemoteIndex = 3`, `DownloadsIndex = 5`, `UpdatesIndex = 9`,
the `0`/`7` cases in `BuildPane`, `_selected == 0` for Audio) go, and every caller asks by id.
Entry points that pointed at the old place follow (palette commands, the record-input menu's
"Audio I/O setup", the "no audio output" status text). English strings in `en.json`; Russian in
the sibling `nota-translations` repo; docs in `nota-docs`.

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: pages reuse the
  existing `Section` / `Row` / `Caption` / `SwitchRow` builders, so no new visual language.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: Unchanged — the same `ISettingsService` fields (`McpEnabled`, `McpPort`,
`PianoRollLabelAllNotes`, `OpenDetailOnSelect`, `SplitFades`, language, input setup per device).

**Audio-Thread Impact**: None: UI-only change. The panes call the same `IAudioEngine` setters
as today from the UI thread.

**Testing**: `scripts/build.sh` (smoke test) — the palette test already checks every catalog
command is bound; a new smoke check asserts every page id the app asks for exists in the
sidebar and every `prefs.*` command names a real page. Visual check with the headless-app
screenshot harness (memory: Nota UI verify recipe), EN and RU.

**Target Platform**: macOS, Windows (ASIO / WASAPI rows), Linux.

**Project Type**: desktop app.

**Performance Goals**: N/A (pages build on selection as today).

**Constraints**: Russian titles/subtitles must fit the 188 px sidebar and the header row.

**Scale/Scope**: ~5 files in `Nota.App`, `CommandCatalog.cs`, `MainWindow.Commands.cs`,
`MainWindow.AudioFormat.cs`, `ArrangementView.cs`, `en.json`, `Glyph.cs` (one new icon);
`nota-translations/languages/ru.json`; ~10 nota-docs pages × 2 languages.

## Constitution Check

| Principle | Status |
|---|---|
| 0. Approved issue first | ✅ #427 `approved`; widened scope written into the issue 2026-11-10 |
| I. Real-time audio safety | ✅ no audio-thread code touched |
| II. Boundary discipline | ✅ no ABI change |
| III. Licensing hygiene | ✅ new `.cs` files carry the SPDX header; no dependency |
| IV. Verification gate | ✅ `scripts/build.sh` + smoke check for page ids |
| V. One logical change per PR | ✅ one PR: the Settings move (RU strings and docs are their own repos) |
| VI. Documentation stays in sync | ✅ CHANGELOG, FEATURES.md, nota-docs EN+RU, screenshots |
| VII. Spec-first | ✅ this spec |
| VIII. Safety rules not weakened | ✅ |

Post-design re-check: unchanged — all green.

## Project Structure

### Documentation (this feature)

```text
specs/088-settings-reorganisation/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/settings-pages.md
└── tasks.md            # /speckit-tasks
```

### Source Code

```text
src/managed/Nota.App/
├── PreferencesWindow.cs           # Nav table (groups, pages, ids); Select/ShowPage by id; BuildPane by id;
│                                  #   AudioPane → format only; AppearancePane → look only
├── PreferencesWindow.AudioIo.cs   # + OutputsPane, InputsPane (device picker, ASIO pair, note, test tone / input list)
├── PreferencesWindow.Mcp.cs       # NEW — McpPane (moved verbatim from AppearancePane)
├── PreferencesWindow.Editing.cs   # NEW — EditingPane (piano roll, arrangement, detail panel)
├── PreferencesWindow.Language.cs  # + LanguagePane (wraps LanguagePicker + caption)
├── PreferencesWindow.Remote.cs    # ShowRemote → Select("Remote")
├── PreferencesWindow.Downloads.cs # DownloadsIndex → id
├── PreferencesWindow.Updates.cs   # UpdatesIndex → id
├── MainWindow.Commands.cs         # prefs.* → page ids (5 new)
├── MainWindow.AudioFormat.cs      # ShowAudioPreferences(page) / ShowInputPreferences
├── ArrangementView.cs             # record-input menu "Audio I/O setup" → Inputs
└── Controls/Glyph.cs              # + Globe (Language page icon)
src/managed/Nota.Application/
├── Palette/CommandCatalog.cs      # 5 new prefs.* commands, keywords moved
└── Localization/en.json           # page titles/subtitles, group, ASIO note, commands; status text → Outputs
tests/Nota.SmokeTest/              # page-id check
../nota-translations/languages/ru.json   # RU strings (separate repo / PR)
../nota-docs/src/content/docs/{,ru/}preferences/*.mdx (+ pages linking to Settings) # separate repo / PR
```

**Structure Decision**: Stay in the existing partial-class layout of `PreferencesWindow`; one
partial file per page group, matching `Remote`, `Updates`, `Downloads`, `Language`.

## Complexity Tracking

None.
