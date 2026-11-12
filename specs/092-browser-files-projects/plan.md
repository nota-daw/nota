# Implementation Plan: Browser — your own sample folders, a file-system browse mode, Locate, and a richer Projects tab

**Branch**: `feat/browser-files-projects` | **Date**: 2026-11-12 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/092-browser-files-projects/spec.md`
(issues #268, #346, #579, #580, #581, #582 — one pull request, milestone 0.60.1).

## Summary

The single Samples folder becomes a list of sample folders kept in settings (the old field
stays as a mirror of the default, so pack installs, Nota Mosaic refs and older versions keep
working); the sample index scans all of them; the Files tab shows each as a root with
Rename / Remove / Reveal, accepts dropped folders, and gains a Tree / Browse toggle whose
Browse view walks the whole file system with Back / Forward / Up, a clickable and editable
path bar and Copy path. Audio clips remember the file they came from (a managed-side map
plus an optional `Source` field in `project.json`), which drives a highlight of the selected
clip's file in the browser and a Locate → In browser / In Finder submenu. The Projects tab
sorts newest first and shows date, tempo, key, length and track count read in the
background from each project's manifest and cached. The rail's folder icon is redrawn on
the common 24-unit grid. Details: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill (tokens, row
  states, the orange line/tint/solid strengths, sentence-case labels).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).
- New keyboard shortcuts (browse Back/Forward/Up, path field) → `nota-shortcuts` skill.
- User-visible → CHANGELOG `[Unreleased]`, `nota-features`, `nota-docs` (EN + RU).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only). No C++ change.

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: settings JSON (`NOTA_DATA_DIR`), `project.json` in `.nota` bundles (one
optional field), a disposable cache `NOTA_DATA_DIR/cache/project-summaries.json`.

**Audio-Thread Impact**: None. All work is UI-thread or background-thread managed code;
the new sample-source map lives beside the existing sample-name map in the managed engine
wrapper and is never touched by the audio thread.

**Testing**: `tests/Nota.SmokeTest` (the only harness; run by `scripts/build.sh`) plus the
headless-app screenshot harness for UI.

**Target Platform**: macOS (Apple Silicon/Intel), Windows x64/ARM, Linux x86_64/ARM —
volume listing and the "Show in …" label differ per OS.

**Project Type**: desktop app.

**Performance Goals**: no UI stall when adding or browsing large/slow folders (enumeration
off the UI thread; first level ≤ 1 s, SC-005); Projects tab names + dates ≤ 1 s, details
≤ 10 s for 200 projects first time, instant from cache (SC-008).

**Constraints**: downgrade-safe settings and project files; Mosaic `samples:` refs keep
resolving against the default folder; the per-folder file cap (`MaxSamples`) and the index
cap apply per root (#358 lesson: never walk a huge tree synchronously).

**Scale/Scope**: 5 user stories, 31 FRs; touches Settings, SettingsService,
SampleLibraryIndex, BrowserViewModel, BrowserView, PreferencesWindow (Library), Glyph,
NotaEngine (managed), ProjectService/ProjectModel, ArrangementView lane menu, SessionView
grid menu, MainWindow wiring, Welcome date helper, localisation files.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-checked after Phase 1 design: still passes.*

| Principle | Status |
|---|---|
| 0. Approved issue first | ✅ #268, #346, #579–#582 carry `approved`, milestone 0.60.1. |
| I. Real-time audio safety | ✅ No audio-thread code touched. |
| II. Boundary discipline | ✅ No C ABI change; `SampleSource` is managed-only (like `SampleName`). |
| III. Licensing hygiene | ✅ No dependency; new files get the two-line SPDX header. |
| IV. Verification gate | ✅ Smoke test extended (settings migration, multi-root index, clip source round-trip, project summary, browse state). |
| V. One logical change per PR | ⚠️ Six issues in one PR — explicitly requested by the maintainer; they share one surface (browser Files/Projects) and one stored folder list. Tracked below. |
| VI. Documentation in sync | ✅ Planned: CHANGELOG, FEATURES.md, nota-docs (EN+RU), nota-shortcuts. |
| VII. Spec-first | ✅ This spec/plan/tasks. |
| VIII. Rules not weakened | ✅ |

## Project Structure

### Documentation (this feature)

```text
specs/092-browser-files-projects/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R9 decisions
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── persisted-formats.md   # settings JSON, project.json Source, IAudioEngine additions
│   └── ui.md                  # rail, Files tab, Settings → Library, clip menus, Projects rows
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   ├── Settings.cs                         # SampleFolders, FilesBrowse*, ProjectsSort; ISettingsService additions
│   ├── Ports/ISampleIndex.cs               # Watch(IReadOnlyList<string> roots)
│   ├── Engine/IAudioEngine.cs              # SampleSource / SetSampleSource
│   └── Browser/ (new)                      # SampleFolderRules (overlap/dup checks), FileBrowseState, PathInput, ProjectSummary
├── Nota.Infrastructure/
│   ├── Settings/SettingsService.cs         # migration, mirror, add/remove/rename/default, availability
│   ├── SampleStore/SampleLibraryIndex.cs   # multi-root scan, per-root hints and sweep
│   ├── SampleStore/SampleStore.cs          # installs into the default folder (unchanged call)
│   ├── Interop/NotaEngine.Tracks.cs / .Session.cs   # record sources on audio-clip loads
│   ├── Persistence/ProjectModel.cs         # AudioClipDto.Source, AudioTakeDto.Source
│   ├── Persistence/ProjectService.cs       # write/restore Source
│   └── Persistence/ProjectSummaryReader.cs (new) + cache
├── Nota.Presentation/ViewModels/BrowserViewModel.cs   # multi-root tree (background), browse list, project rows/sort, located path
└── Nota.App/
    ├── Controls/Glyph.cs                   # Folder → 24-grid path
    ├── BrowserView.cs (+ new BrowserView.Browse.cs, BrowserView.Places.cs)  # toggle, nav bar, path field, root menus, folder drop, located marker
    ├── PreferencesWindow.cs / PreferencesWindow.Samples.cs   # Sample folders list
    ├── ArrangementView.LaneControl.cs      # Locate submenu
    ├── SessionView.Grid.cs                 # Locate submenu
    ├── MainWindow.Browser.cs / .Detail.cs / .Welcome.cs  # selection → located path, Locate handlers, shared date helper, open-project marker
    (strings) Nota.Application/Localization/en.json; RU in ../nota-translations/languages/ru.json (validated after the app PR reaches develop)

tests/Nota.SmokeTest/
├── SampleFoldersTests.cs (new)    # migration, mirror, overlap/dup, remove/default rules, multi-root tree, index hint root
├── ClipSourceTests.cs (new)       # Source round-trip, split shares, bundle-path exclusion, record has none
├── ProjectSummaryTests.cs (new)   # tempo/key/tracks/length, warped/unwarped audio, unreadable, cache stamp
└── FileBrowseTests.cs (new)       # back/forward/up, computer level, path parsing (~, file path, invalid)
```

**Structure Decision**: Managed-only change following the existing layering — pure rules
and state in `Nota.Application` (testable from the smoke test without UI), file-system and
persistence in `Nota.Infrastructure`, list building in `Nota.Presentation`, Avalonia views in
`Nota.App`. `BrowserView.cs` is already ~1700 lines, so Browse-mode and Places UI go into
partial-class files.

## Delivery order (one PR, independently testable steps)

1. Rail icon (#579) — isolated.
2. Settings model + migration + rules (#346 core) → index multi-root → Files tab roots +
   menus + folder drop (#268) → Settings → Library list (#346 UI).
3. Projects tab (#582) — independent of 2.
4. Clip source capture + persistence → highlight + Locate (#581).
5. Browse mode (#580) — then Locate's "outside every folder" path switches to it.
6. Docs: CHANGELOG, FEATURES.md, nota-docs EN+RU, shortcuts pane; screenshots.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| Six issues in one PR (principle V) | Maintainer asked for a single PR; #268/#346 share one stored list, #580/#581 depend on it, all touch the same browser files | Six PRs would conflict on `BrowserView.cs` / `BrowserViewModel.cs` and ship the folder list half-done (#268 without #346 or vice versa) |
