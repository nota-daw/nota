---

description: "Tasks for spec 092 — browser sample folders, browse mode, Locate, Projects tab, rail icon"
---

# Tasks: Browser — your own sample folders, a file-system browse mode, Locate, and a richer Projects tab

**Input**: Design documents from `specs/092-browser-files-projects/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included — constitution IV requires the smoke test to cover new behaviour; plan R9
lists the checks. All tests live in `tests/Nota.SmokeTest/` as `Run()` iterators of
`(bool Ok, string Label)` (pattern: `SampleStoreTests.cs`), wired into `Program.cs` both in
the full run and behind a standalone flag.

**Organization**: Tasks are grouped by user story (spec.md US1–US5).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies on incomplete tasks)
- **[Story]**: US1 sample folders (#346/#268) · US2 rail icon (#579) · US3 browse mode (#580) ·
  US4 Locate (#581) · US5 Projects tab (#582)

## Path Conventions

- Managed: `src/managed/<Project>/…` · Strings: `src/managed/Nota.Application/Localization/en.json`
  (RU later in `../nota-translations/languages/ru.json`) · Tests: `tests/Nota.SmokeTest/`
- Every new source file starts with the two-line SPDX header
  (`// SPDX-License-Identifier: AGPL-3.0-only` / `// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.`)
  followed by a short *why this file exists* comment.

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: Test scaffolding the stories add to.

- [X] T001 Create empty test classes `SampleFoldersTests`, `FileBrowseTests`, `ClipSourceTests`, `ProjectSummaryTests` (each `internal static class … { public static IEnumerable<(bool Ok, string Label)> Run() { yield break; } }`, SPDX header) in `tests/Nota.SmokeTest/SampleFoldersTests.cs`, `FileBrowseTests.cs`, `ClipSourceTests.cs`, `ProjectSummaryTests.cs`
- [X] T002 Wire the four classes into `tests/Nota.SmokeTest/Program.cs`: a standalone flag `--browser-files` (runs all four, prints `BROWSER FILES PASSED/FAILED`, next to the existing `--browser` block ~line 198) and a `-- browser files (spec 092) --` section in the full run right after the `BrowserCategoryTests` section (~line 15634)

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: The settings shape every story reads.

- [X] T003 Add settings fields in `src/managed/Nota.Application/Settings.cs`: `public List<SampleFolderEntry>? SampleFolders { get; set; }` with `SampleFolderEntry { string Path = ""; string? Name; bool IsDefault }` (Path: "Absolute, normalised (no trailing separator). `""` only for the migrated default-location entry"; Name: "Display label; null/empty → folder name. Never renames on disk"; IsDefault: "Exactly one `true` in the list"); `public bool FilesBrowseMode { get; set; }` (default false = Tree); `public string FilesBrowseFolder { get; set; } = ""`; `public string ProjectsSort { get; set; } = "Date"` ("Date" | "Name"). Keep `SamplesFolder` and update its doc comment: "mirror of the default SampleFolders entry, kept for older versions and Mosaic refs"
- [X] T004 Add `ResolvedSampleFolder` record `(int Index, string Path, string Label, bool IsDefault, bool Available)`, `SampleFolderResult` (`Added`, `AlreadyPresent`, `Overlaps`, `NotADirectory`, with the conflicting index) and the `ISettingsService` members from contracts/persisted-formats.md (`ResolvedSampleFolders()`, `AddSampleFolder`, `RemoveSampleFolder`, `RenameSampleFolder`, `SetDefaultSampleFolder`, `event Action? SampleFoldersChanged`) in `src/managed/Nota.Application/Settings.cs`
- [X] T005 Create pure rules `SampleFolderRules` in new `src/managed/Nota.Application/Browser/SampleFolderRules.cs`: `Normalize(path)`, `IsSameOrInside(a, b)` (OS path comparer: case-insensitive on Windows/macOS, sensitive on Linux; separator-aware so `/A/Drum` is not inside `/A/Drums`), `Check(existing, candidate) → SampleFolderResult`, `Repair(list, legacySamplesFolder)` (migration: null/empty → `[{Path = legacy, IsDefault = true}]`; zero/several defaults → first is default; drop entries equal to/nested in/containing an earlier one, returning dropped for logging)
- [X] T006 Implement in `src/managed/Nota.Infrastructure/Settings/SettingsService.cs`: run `SampleFolderRules.Repair` on load; on save write `SamplesFolder = default.Path`; `ResolvedSamplesFolder()` returns the default entry resolved (`""` → `~/Music/Nota Samples`, created if missing — unchanged behaviour); `ResolvedSampleFolders()` (never creates non-default folders; `Available = Directory.Exists`); Add (normalise → rules → append → save → raise), Remove (false for the last entry; removing the default promotes the next), Rename (null/blank clears), SetDefault; all mutators save and raise `SampleFoldersChanged`
- [X] T007 Update the test double in `tests/Nota.SmokeTest/SampleStoreTests.cs` (~line 178, the class implementing `ResolvedSamplesFolder()`) and any other `ISettingsService` implementations (`grep -rn ": ISettingsService" src tests`) so the solution builds

**Checkpoint**: builds; old settings files load into a one-entry list; nothing visible changed.

---

## Phase 3: User Story 1 — Several sample folders in the browser (P1) 🎯 MVP

**Goal**: Add/remove/rename/default folders in Settings → Library and from the Files tab; each is a root in the Files tab, indexed, searched, filtered.

**Independent Test**: quickstart.md steps 1–4.

### Tests for User Story 1

- [X] T008 [P] [US1] In `tests/Nota.SmokeTest/SampleFoldersTests.cs` check `SampleFolderRules`: legacy `SamplesFolder="/x/A"` → one default entry; empty legacy → `Path=""` default; two defaults repaired to first; duplicate (incl. trailing slash / case on macOS-Windows) → `AlreadyPresent`; `/A/Drums` vs `/A` → `Overlaps` both directions; `/A/Drum` vs `/A/Drums` → not overlapping; nested entry in a loaded list is dropped
- [X] T009 [P] [US1] In `tests/Nota.SmokeTest/SampleFoldersTests.cs` check `SettingsService` against a temp `NOTA_DATA_DIR`: add/rename/set-default/remove round-trip through save+reload; `SamplesFolder` mirrors the default after each save; remove of the only entry returns false; removing the default promotes the next; `ResolvedSampleFolders()` marks a deleted folder `Available=false` and keeps it
- [X] T010 [P] [US1] In `tests/Nota.SmokeTest/SampleFoldersTests.cs` check `BrowserViewModel.RebuildSamples` with two temp roots (use `BrowserTestSupport`): two top-level folder nodes, default first, labels = Name ?? folder name, files under the right root, the default root's `Downloaded` subfolder first within it, an unavailable root present with `Unavailable=true` and no children, search finds a file in the second root
- [X] T011 [P] [US1] In `tests/Nota.SmokeTest/SmartSampleTests.cs` (index section) check `SampleLibraryIndex.Watch(new[]{rootA, rootB})` analyses files from both roots, name hints use the containing root, and files under a root that disappears are not swept

### Implementation for User Story 1

- [X] T012 [US1] Change `ISampleIndex.Watch(string root)` to `Watch(IReadOnlyList<string> roots)` in `src/managed/Nota.Application/Ports/ISampleIndex.cs` and implement in `src/managed/Nota.Infrastructure/SampleStore/SampleLibraryIndex.cs`: `_roots` list replaces `_root` (clear `Info` when the set changes), `Scan` enumerates roots in order honouring `_priority`, `Classify` passes the containing root to `SampleNameHints.Parse`, the stale sweep runs per *available* root only, `Prioritize` re-watches `_roots`; update every caller (`grep -rn "\.Watch(" src/managed`)
- [X] T013 [US1] Rework `RebuildSamples()` in `src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs`: iterate `_settings.ResolvedSampleFolders()`; one top-level `BrowserItem` (Kind Folder, Depth 0, `PlaceIndex` = entry index, `Unavailable`, Name = Label, Path = root) per entry; `GetOrCreateFolder` keyed under each root; `MaxSamples` cap per root; `AppendKitFolders()` unchanged; `_index?.Watch(available roots)`; enumeration moves to a background `Task` that builds the tree then swaps it in on the UI thread (cancel a running build when a new one starts; show first-level folders before files fill in). Also fix the second `ResolvedSamplesFolder()` use (~line 704) to search all roots where it means "the library"
- [X] T014 [US1] Add `PlaceIndex` (int, default −1), `Unavailable` (bool) and `Located` (bool, used by US4) to `BrowserItem` (`grep -rn "class BrowserItem" src/managed`)
- [X] T015 [US1] Subscribe `BrowserViewModel` to `ISettingsService.SampleFoldersChanged` → `RebuildSamples()` and refresh availability when the Files tab is shown, in `src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs` and `src/managed/Nota.App/BrowserView.cs` (tab activation ~line 715)
- [X] T016 [US1] New partial `src/managed/Nota.App/BrowserView.Places.cs`: root-row context menu (Rename → inline prompt reusing the existing rename prompt helper; Remove from browser → confirm when it is the default, naming the next default; Show in Finder/Explorer/file manager via `MenuKit.RevealLabel` + `RevealRequested`; Set as default); dimmed style + "Unavailable" sub-label for `Unavailable` rows (tokens only, nota-design)
- [X] T017 [US1] Files-tab header **Add folder…** button (`GlyphKind.Plus`, tooltip) opening the OS folder picker (`StorageProvider.OpenFolderPickerAsync`, start at default folder) and folder drop: in `src/managed/Nota.App/BrowserView.cs` drop handler (`ExternalFiles`, ~line 103–140) route directories to `AddSampleFolder`, files stay as today; show refusal notes from contracts/ui.md through the browser status line
- [X] T018 [US1] Replace the single Samples-folder picker (`src/managed/Nota.App/PreferencesWindow.cs` ~line 971) with a **Sample folders** list in `src/managed/Nota.App/PreferencesWindow.Samples.cs`: rows (label, path, Default badge, Unavailable note, Show, ⋯ menu Rename / Set as default / Remove), **Add folder…**, drop target; rebuild on `SampleFoldersChanged`; Remove disabled for the only entry
- [X] T019 [US1] Make the default folder drive pack installs and Mosaic roots on change: `src/managed/Nota.Infrastructure/SampleStore/SampleStore.cs` keeps `settings.ResolvedSamplesFolder` (verify it re-reads per install, not cached), `src/managed/Nota.Infrastructure/Mosaic/MosaicLibrary.cs` re-calls `NotaEngine.SetPathRoot(MosaicPaths.SamplesRoot, …)` on `SampleFoldersChanged`
- [X] T020 [US1] Add EN strings to `src/managed/Nota.Application/Localization/en.json`: `browser.add_folder`, `browser.remove_from_browser`, `browser.set_as_default`, `browser.unavailable`, `browser.folder_already_added`, `browser.folder_inside` ("Inside {0} — already in the browser"), `browser.folder_contains` ("Contains {0} — remove it first"), `browser.remove_default_confirm` ("{0} becomes the default folder for downloaded packs."), `preferences.sample_folders`, `preferences.default`, plus any new labels used in T016–T018

**Checkpoint**: US1 works alone (quickstart 1–4); `--browser-files` and `--smart-samples` pass.

---

## Phase 4: User Story 2 — Files rail icon size (P1)

**Goal**: The folder glyph matches the other rail icons.

**Independent Test**: quickstart step 5.

- [X] T021 [P] [US2] In `src/managed/Nota.App/Controls/Glyph.cs` remove the procedural `case GlyphKind.Folder` (~line 341) and add a 24-grid entry to the path table (~line 468, next to `[GlyphKind.Doc]`): outline spanning x 3.5–20.5, y 5–19 with the tab at top-left, e.g. `"M3.5 6.5 a1.5 1.5 0 0 1 1.5 -1.5 h4.6 l2 2.2 h7.4 a1.5 1.5 0 0 1 1.5 1.5 v8.8 a1.5 1.5 0 0 1 -1.5 1.5 h-14 a1.5 1.5 0 0 1 -1.5 -1.5 z"`; tune by screenshot so its extent matches Doc/Bookmark
- [X] T022 [US2] Screenshot the rail (headless harness, memory: nota-ui-verify) in Studio Dark/Light, Ember Graphite/Paper and the classic look; also check `GlyphKind.Folder` users: `ArrangementView.Tracks.cs:210`, `ArrangementView.cs:2857`, `DeviceChainView.Clipboard.cs:558`, `PreferencesWindow.Skins.cs:71`, `DeviceCards/Instruments/RhythmInstrumentCard.cs:322` (8 px)

**Checkpoint**: icon shots before/after saved for the PR.

---

## Phase 5: User Story 3 — Browse mode (P2)

**Goal**: Tree / Browse toggle; Browse walks the file system with Back/Forward/Up, path bar, Copy path, typed/pasted paths.

**Independent Test**: quickstart step 6.

### Tests for User Story 3

- [X] T023 [P] [US3] In `tests/Nota.SmokeTest/FileBrowseTests.cs` check `FileBrowseState`: Go pushes Back and clears Forward; Back/Forward move between stacks; stacks capped at 100; Up from a folder → parent; Up from a volume root → Computer; Up at Computer disabled; `Volumes()` contains `/` (macOS/Linux) or a ready drive (Windows)
- [X] T024 [P] [US3] In `tests/Nota.SmokeTest/FileBrowseTests.cs` check `PathInput.Parse`: `~` and `~/x` expand to home; a file path → (its folder, the file selected); a trailing separator is ignored; a missing path → error; quoted pasted paths (`"…"`, `'…'`) are unquoted; `file://` URLs are decoded
- [X] T025 [P] [US3] In `tests/Nota.SmokeTest/FileBrowseTests.cs` check `BrowserViewModel` browse listing of a temp folder: folders first then audio/MIDI files, each sorted by name; hidden entries (`.x`, Windows hidden attribute) and non-audio files skipped; search filters the current folder only

### Implementation for User Story 3

- [X] T026 [P] [US3] New `src/managed/Nota.Application/Browser/FileBrowseState.cs`: `Location` (path or `Computer`), `Back`/`Forward` stacks (cap 100), `Go`, `GoBack`, `GoForward`, `Up`, `CanUp`, `Segments` (for the path bar: volume root first), and static `Volumes()` per OS (Windows `DriveInfo.GetDrives()` ready; macOS `/` labelled with the system volume name + `/Volumes/*` except the symlink to `/`; Linux `/`, `$HOME`, `/media/$USER/*`, `/run/media/$USER/*`, `/mnt/*`)
- [X] T027 [P] [US3] New `src/managed/Nota.Application/Browser/PathInput.cs`: `Parse(string text) → (string Folder, string? SelectFile) | error` per T024
- [X] T028 [US3] In `src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs` add browse mode: `BrowseMode` (persisted to `Settings.FilesBrowseMode`), `Browse` (`FileBrowseState`, starting at `Settings.FilesBrowseFolder` if it exists else the default folder, or the folder of the row selected in the tree when switching), `BrowseItems` collection built off the UI thread (Kind Folder / Sample / MidiFile, `Sample` tags from `_index.Get` when indexed), `Unreadable` / `Empty` states, search over the current folder; persist `FilesBrowseFolder` on navigation (never the Computer level)
- [X] T029 [US3] New partial `src/managed/Nota.App/BrowserView.Browse.cs`: Tree | Browse segmented toggle under the search box (Files tab only); nav bar with Back/Forward/Up buttons (`GlyphKind` arrows), path segments as buttons eliding from the left, empty-area click or ⌘L/Ctrl+L → editable `TextBox` (Enter go via `PathInput`, Esc cancel, error → red edge token + status "Can't open {0}"), Copy path button (clipboard + status "Path copied"); Back ⌘[ / Alt+Left, Forward ⌘] / Alt+Right, Up ⌘↑ / Alt+Up when the Files tab has focus; bind the list to `BrowseItems` in Browse mode so preview, double-click, drag, sample player reuse the existing paths; folder rows navigate on click/double-click/Enter; empty/unreadable placeholders
- [X] T030 [US3] Context menu additions in `src/managed/Nota.App/BrowserView.cs` (~line 1667 folder/file menu): **Copy path** on folders and files (tree and browse), **Show in …** on browse rows, **Add this folder to the browser** on browse folders (and on the current folder from the path bar) via `AddSampleFolder` with the same refusal notes
- [X] T031 [US3] EN strings in `src/managed/Nota.Application/Localization/en.json`: `browser.tree`, `browser.browse`, `browser.back`, `browser.forward`, `browser.up`, `browser.computer`, `browser.copy_path`, `browser.path_copied`, `browser.cant_open` ("Can't open {0}"), `browser.add_this_folder`, `browser.folder_empty` ("This folder has no audio or MIDI files"), `browser.folder_unreadable` ("Nota can't read this folder")

**Checkpoint**: US3 works alone (quickstart 6); `--browser-files` passes.

---

## Phase 6: User Story 4 — Locate a clip's file (P2)

**Goal**: Clips remember their source file across save/reopen; the selected clip's file is highlighted when visible; Locate → In browser / In Finder.

**Independent Test**: quickstart step 7.

### Tests for User Story 4

- [X] T032 [P] [US4] In `tests/Nota.SmokeTest/ClipSourceTests.cs` (real engine, like other project tests in Program.cs): add an audio clip from a temp WAV → `SampleSource(id)` = full path; split the clip → both halves report it; save to a temp bundle, reopen → `AudioClipDto.Source` present in `project.json` and `SampleSource` restored on the reloaded clip; a session audio slot round-trips the same way; loading from `<bundle>.nota/samples/x.wav` records no source; a project without `Source` loads with `SampleSource` = ""

### Implementation for User Story 4

- [X] T033 [US4] Add `string SampleSource(long sampleId)` and `void SetSampleSource(long sampleId, string path)` to `src/managed/Nota.Application/Engine/IAudioEngine.cs` (doc: "full path the sample was loaded from, or \"\"; managed-only, like SampleName") and implement in `src/managed/Nota.Infrastructure/Interop/NotaEngine.Tracks.cs` with `_sampleSources` beside `_sampleNames` (~line 914); record in `RememberSampleName`'s callers and in `AddAudioClip` (~line 20), `AddAudioClipEx` (~line 996) and `src/managed/Nota.Infrastructure/Interop/NotaEngine.Session.cs` `AddSessionAudioClip`/`AddSessionAudioFile` (~line 131–135) by reading the new clip's sample id; skip paths containing a `.nota` directory segment followed by `samples`; implement in any other `IAudioEngine` (`grep -rn ": IAudioEngine" src tests`)
- [X] T034 [US4] Add `public string? Source { get; set; }` to `AudioClipDto` and `AudioTakeDto` in `src/managed/Nota.Infrastructure/Persistence/ProjectModel.cs` (comment: "original file the audio came from (spec 092); optional, absent = unknown"); in `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs` set it from `engine.SampleSource(sampleId)` (null when "") for arrangement clips (~line 259), takes (~line 272) and session audio slots (~line 303); on load, after each clip/take/slot sample is restored, call `engine.SetSampleSource(newSampleId, dto.Source)` when non-empty; no `CurrentFormatVersion` change
- [X] T035 [US4] In `src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs` add `SetLocatedPath(string? path)`: clears the previous `Located` row and sets it on the matching row in the visible Files list (tree `Samples` or `BrowseItems`); never expands, scrolls or switches tabs; re-applied after rebuilds
- [X] T036 [US4] In `src/managed/Nota.App/BrowserView.cs` render `Located` rows with the orange line treatment (2 px left edge, `Brush.Accent`), distinct from selection; add `RevealFile(string path)`: expand the browser if collapsed, open the Files tab, if `path` is under an available root → Tree mode, expand ancestors, scroll into view, select without auditioning; else → Browse mode at its folder with it selected
- [X] T037 [US4] Wire selection in `src/managed/Nota.App/MainWindow.Browser.cs`: on `Timeline.ClipSelectionChanged` (hooked in `MainWindow.axaml.cs:215`) and on Session slot selection changes (`src/managed/Nota.App/SessionView.Grid.cs` — add a `SlotSelectionChanged` event if none), resolve a single selected audio clip's sample id → `engine.SampleSource` → `Browser.SetLocatedPath`, else null
- [X] T038 [US4] Add the **Locate ▸** submenu (single audio clip only, not in the multi-selection menu) in `src/managed/Nota.App/ArrangementView.LaneControl.cs` (clip menu ~line 1030–1090) and `src/managed/Nota.App/SessionView.Grid.cs` (slot menu ~line 898): **In browser** (enabled when source known and `File.Exists`) → `LocateInBrowserRequested(path)`; **In Finder** / **In Explorer** / **In file manager** by OS → reveal source if it exists, else the bundle copy `<open project>/samples/<hash>.wav` (resolve via `BundleContent` for the clip's sample id) when the project is saved, else disabled; handle both events in `src/managed/Nota.App/MainWindow.Browser.cs` with `Browser.RevealFile` / `RevealInFileManager`
- [X] T039 [US4] EN strings in `src/managed/Nota.Application/Localization/en.json`: `clip.locate`, `clip.locate_in_browser`, `clip.locate_in_finder`, `clip.locate_in_explorer`, `clip.locate_in_file_manager`

**Checkpoint**: US4 works (quickstart 7) incl. after save/reopen; `--browser-files` passes.

---

## Phase 7: User Story 5 — Projects newest first with details (P2)

**Goal**: Date sort by default, date/tempo/key/length/tracks per row, open marker, remembered sort.

**Independent Test**: quickstart step 8.

### Tests for User Story 5

- [X] T040 [P] [US5] In `tests/Nota.SmokeTest/ProjectSummaryTests.cs` write temp bundles with hand-made `project.json` and check `ProjectSummaryReader`: bpm and key from `Transport`; tracks = `Tracks` count; length = max end over MIDI clips (`StartBeat + LengthBeats`), warped audio (`WarpPlayEnd - WarpPlayStart`, else `WarpBeats`), unwarped audio (`LengthFrames` or the WAV header frames ÷ WAV rate × bpm/60); session slots ignored; corrupt JSON → null summary; the cache returns the stored summary when stamp (manifest ticks + size) matches and re-reads when it changes
- [X] T041 [P] [US5] In `tests/Nota.SmokeTest/ProjectSummaryTests.cs` check `BrowserViewModel.RebuildProjects` ordering: Date (manifest write time, newest first; unreadable date last) and Name (A–Z); `IsOpen` follows `OpenProjectPath`

### Implementation for User Story 5

- [X] T042 [P] [US5] New `src/managed/Nota.Application/Browser/ProjectSummary.cs` (`record ProjectSummary(double Bpm, string? Key, int TrackCount, double LengthBeats)`, `FormatLength(bpm, beats) → "m:ss"`) and a shared `ProjectDates.Modified(string bundlePath) → DateTime?` (manifest `project.json` write time, else bundle dir time) + `ProjectDates.Format(DateTime)` = `"MM/dd/yyyy HH:mm"` with `NotaNum.Culture`
- [X] T043 [US5] New `src/managed/Nota.Infrastructure/Persistence/ProjectSummaryReader.cs` + cache: read `project.json` via `JsonDocument` (no `ProjectDocument` bind, no engine) per T040; cache at `NOTA_DATA_DIR/cache/project-summaries.json` keyed by bundle path with stamp `"<manifest ticks>:<size>"`, prune vanished bundles on write, corrupt cache → rebuilt
- [X] T044 [US5] Rework `RebuildProjects()` in `src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs` (~line 902): `Modified` from `ProjectDates`, sort by `Settings.ProjectsSort`, `Sub` = formatted date; then a background pass fills `Summary` (cache first) and updates rows in place; add `OpenProjectPath` (marks `IsOpen`) and `ProjectsSort` setter (persists, re-sorts); keep the view-side search filter working with the order
- [X] T045 [US5] In `src/managed/Nota.App/BrowserView.cs` Projects rows: name + open marker (accent dot) on line 1; muted second line `date · 124 BPM · Am · 3:42 · 12 tracks` (parts absent until known; date only when unreadable); `Date | Name` sort toggle beside the search when the Projects tab is active; refresh the list when the tab is shown
- [X] T046 [US5] In `src/managed/Nota.App/MainWindow.Welcome.cs` (~line 87) use `ProjectDates.Modified/Format`; in MainWindow open/save/new paths set `Browser` VM `OpenProjectPath` and call `RebuildProjects()` after save (`grep -n "SaveProject\|OpenProject" src/managed/Nota.App/MainWindow*.cs`)
- [X] T047 [US5] EN strings in `src/managed/Nota.Application/Localization/en.json`: `browser.sort_date`, `browser.sort_name`, `browser.project_open`, `browser.project_tracks` (plural via `Loc.Plural`), `browser.bpm`

**Checkpoint**: US5 works alone (quickstart 8).

---

## Phase 8: Polish & Cross-Cutting Concerns

- [X] T048 [P] Shortcuts pane: run the `nota-shortcuts` skill for browse Back/Forward/Up and ⌘L/Ctrl+L (Files tab)
- [X] T049 [P] `CHANGELOG.md` `[Unreleased]` entries (Added: sample folders, browse mode, Locate, Projects details; Fixed: Files icon size) referencing #268 #346 #579 #580 #581 #582
- [X] T050 [P] `FEATURES.md` via the `nota-features` skill
- [X] T051 User docs via the `nota-docs` skill (EN + RU pages for the browser Files/Projects tabs, Settings → Library, clip context menu; re-shoot screenshots)
- [ ] T052 RU strings for every key added in T020, T031, T039, T047 in `../nota-translations/languages/ru.json` (validated once the app PR reaches develop)
- [ ] T053 Run `/speckit-converge`, then `scripts/build.sh` (must pass incl. smoke test); walk quickstart.md 1–9 with the headless harness and collect before/after screenshots for the PR (rail, Files tree with two roots, Browse mode, Settings → Library, Locate highlight, Projects tab) in all four themes + classic look where the surface is themed
- [X] T054 Re-check the Nota Compliance Checklist in spec.md (SPDX headers on new files, no dependency, no audio-thread code, no ABI change, CHANGELOG/FEATURES/docs done, DESIGN.md followed) and tick it

---

## Dependencies & Execution Order

### Phase Dependencies

- Setup (T001–T002) → Foundational (T003–T007) → stories.
- **US1** (T008–T020) needs Foundational.
- **US2** (T021–T022) needs nothing — can be done first or in parallel with anything.
- **US3** (T023–T031) needs Foundational (settings fields) and T014 (`BrowserItem` fields) and T017's `AddSampleFolder` UI path for T030.
- **US4** (T032–T039) needs T014 (`Located`); T036's "outside every folder → Browse" branch needs US3 (T028/T029) — without it, that branch falls back to In Finder.
- **US5** (T040–T047) needs Foundational (`ProjectsSort`) only.
- Polish after all stories.

### Within stories

Tests first (they fail until implemented) → Application-layer rules/state → Infrastructure → ViewModel → View → strings.

### Parallel Opportunities

- T008–T011 in parallel; T021 any time; T023–T025 and T026–T027 in parallel; T032 alongside T033; T040–T042 in parallel; T048–T050 in parallel.
- US2 and US5 can proceed in parallel with US1 (different files except `BrowserView.cs` / `BrowserViewModel.cs` — serialise edits to those two files).

## Parallel Example: User Story 3

```text
T023 FileBrowseState tests   ─┐
T024 PathInput tests         ─┼─ together
T026 FileBrowseState.cs      ─┤
T027 PathInput.cs            ─┘
then T028 (VM) → T029 (view) → T030 (menus) → T031 (strings)
```

## Implementation Strategy

1. **MVP**: Setup + Foundational + US1 + US2 (both P1) → shippable on their own (#268, #346, #579).
2. Add US5 (independent) → Projects tab.
3. Add US3 → browse mode.
4. Add US4 last so "In browser" can use browse mode for files outside every folder.
5. Polish, converge, build, screenshots → one PR closing all six issues.

## Phase 9: Convergence

- [X] T055 Show each sample folder's first-level subfolders as soon as they are listed and fill files in when the walk ends, in `src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs` (`RebuildSamplesAsync`) per SC-005 (partial)
- [X] T056 Open the selected folder with Enter in the Files browse view (and keep Enter on a file a no-op beyond today's behaviour) in `src/managed/Nota.App/BrowserView.Files.cs` per contracts/ui.md browse rows (partial)

## Phase 10: Browse view review (2026-11-12, spec § Clarifications)

- [X] T057 [US3] Add a Home button (new `GlyphKind.Home`, 24-grid path) after Up in the nav bar of `src/managed/Nota.App/BrowserView.Files.cs`, opening the default sample folder through `FileBrowseState.Go` (history kept); tooltip with no shortcut; EN string `browser.home_tip` per FR-013
- [X] T058 [US3] List every non-hidden file in `BrowserViewModel.ListBrowse` (`src/managed/Nota.Presentation/ViewModels/BrowserViewModel.cs`): unsupported files as a new `BrowserItemKind.OtherFile`; set `BrowserItem.Size` (bytes) on file rows per FR-012
- [X] T059 [US3] Browse rows in `src/managed/Nota.App/BrowserView.cs`: kind icon (Folder / new `GlyphKind.Drive` / new `GlyphKind.AudioFile` / `Midi` / `Doc`), extension and size as two right-hand columns (size column fixed width, `Loc` unit strings), `OtherFile` rows at reduced opacity and excluded from drag, double-click and preview per FR-012a
- [X] T060 [US3] Verify the sample player auditions an audio file selected in the browse view (click and arrow keys) and fix whatever stops it, in `src/managed/Nota.App/BrowserView.cs` / `PreviewPlayer.cs` per US3 AC12
- [X] T061 [US3] Extend `tests/Nota.SmokeTest/FileBrowseTests.cs`: listing includes other files as `OtherFile` with sizes, order folders → files by name; FileBrowseState Home; re-shoot browse view in the harness
