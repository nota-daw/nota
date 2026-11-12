# Research: Browser — sample folders, browse mode, Locate, Projects tab (spec 092)

Every decision below was taken from reading the current code on `develop` (c613167d).

## R1. Where the list of sample folders lives, and what "the Samples folder" means afterwards

- **Decision**: Add `Settings.SampleFolders` (a list of `{ Path, Name?, IsDefault }`) and keep
  `Settings.SamplesFolder` as a **mirror of the default entry's path** (written on every save).
  `ISettingsService.ResolvedSamplesFolder()` keeps its meaning — *the default folder* — and a
  new `ResolvedSampleFolders()` returns every entry with availability.
- **Rationale**: Six call sites use `ResolvedSamplesFolder()` for a single folder and all of
  them mean "the default": pack installs (`SampleStore`), Nota Mosaic's `samples:` path root
  and refs (`MosaicLibrary.ToRef/Resolve`, `NotaEngine.SetPathRoot`), the import dialog's
  start folder, Preferences. Keeping it means Mosaic presets saved as `samples:/…` keep
  resolving, and an older Nota reading the same settings file still finds its folder
  (downgrade-safe).
- **Migration**: On load, if `SampleFolders` is null/empty → one entry
  `{ Path = SamplesFolder (or "" = default location), IsDefault = true }`. Exactly one
  default is enforced on load (first entry wins if none/several).
- **Alternatives**: Replace `SamplesFolder` outright (breaks downgrade and needs every call
  site touched); keep the default outside the list (two concepts for one thing in the UI).

## R2. Indexing several folders

- **Decision**: `ISampleIndex.Watch(string root)` becomes `Watch(IReadOnlyList<string> roots)`;
  one scan thread walks the roots in list order (priority folders first, as today), and
  name hints (`SampleNameHints.Parse(path, root)`) use the root that contains the path.
  The stale-entry sweep (`IsUnder(p, root)`) runs per root; files under an *unavailable*
  root are kept (not swept), so an unplugged drive doesn't lose its analyses.
- **Rationale**: `SampleLibraryIndex` already scans in the background with a cap and a
  progress counter; the only single-root assumptions are `_root`, the hint parse and the
  sweep. Nested roots are refused (FR-008), so no file belongs to two roots.
- **Alternatives**: One index instance per folder (duplicates the similar-search space and
  the progress line).

## R3. Files tab tree with several roots

- **Decision**: `BrowserViewModel.RebuildSamples()` builds one top-level `Folder` node per
  entry (default first, then list order) and places files under it with
  `GetOrCreateFolder(root, …)` per root. The default root's `Downloaded` pack folder keeps
  its icon and first place *inside* that root. A root node carries a new
  `BrowserItem.RootIndex` (or a `IsPlace` flag) so the view can give it the Rename / Remove /
  Reveal menu, and an `Unavailable` flag that dims it. The per-folder `MaxSamples` cap
  applies per root.
- **Enumeration off the UI thread**: today `RebuildSamples` enumerates synchronously. With
  several (possibly slow, external) roots it moves to a background task that builds the tree
  and swaps it in on the UI thread; first level of each root appears first (SC-005).
- **Rationale**: Reuses the existing flattened-tree machinery (`_sampleTree`,
  `RebuildSampleVisible`, filter chips, "similar").

## R4. Browse mode

- **Decision**: A second presenter in the Files tab, toggled by a two-state control next to
  the search field: **Tree** (today) / **Browse**. Browse is a flat `BrowserItem` list of one
  folder (folders first, then audio/MIDI files; hidden entries skipped) bound to the same
  list control, so preview, double-click, drag-and-drop and the sample player work
  unchanged. A `FileBrowseState` in `Nota.Presentation` holds the current folder and
  back/forward stacks; enumeration runs off the UI thread.
- **Above the root**: "Computer" level = drives/volumes:
  - Windows: `DriveInfo.GetDrives()` that are ready.
  - macOS: `/` (labelled with the system volume name) + each directory in `/Volumes` except
    the one that is a symlink to `/`.
  - Linux: `/`, `$HOME`, and mount points under `/media/$USER`, `/run/media/$USER`, `/mnt`.
- **Path bar**: segments as buttons; clicking the bar's empty area (or ⌘L / Ctrl+L while the
  Files tab has focus) turns it into a text field; Enter navigates, Esc cancels; a file path
  opens its folder with the file selected; `~` expands to home.
- **Persistence**: `Settings.FilesBrowseMode` (bool) and `Settings.FilesBrowseFolder`
  (string).
- **Alternatives**: a "Computer" root inside the tree (rejected in clarification); the OS
  file dialog (not a browser — no preview/drag).

## R5. Remembering a clip's original file (Locate after reopen)

- **Finding**: The engine keeps only audio; the managed `NotaEngine` remembers a sample's
  *file name* per sample id (`_sampleNames`, `RememberSampleName`) for Sampler/Rack/Rhythm
  loads only — **audio clip loads (`AddAudioClip`, `AddAudioClipEx`, `AddSessionAudioClip`,
  `AddSessionAudioFile`) remember nothing**. On save, `RegisterSample` content-hashes the
  buffer into `samples/<hash>.wav`; `AudioClipDto` has `Sample` (bundle path) and `Name`
  only.
- **Decision**:
  - `NotaEngine` gains `_sampleSources: Dictionary<long sampleId, string fullPath>` with
    `SampleSource(id)` / `SetSampleSource(id, path)` on `IAudioEngine`, recorded by every
    audio-clip and session-audio load **when the path is not inside a `.nota` bundle's
    `samples/` folder** (those are project copies, not sources).
  - `AudioClipDto.Source` (`string?`, absolute path) is written by `ProjectService` from
    `engine.SampleSource(ac.SampleId)` for arrangement clips, comp takes and session audio
    slots; on load, after the clip is restored, `SetSampleSource(newSampleId, dto.Source)`.
  - Split / duplicate / copy share the sample id, so the source follows for free;
    consolidate, record, bounce, stems and reverse-render make new buffers with no source
    (correct per spec).
- **Format**: optional, additive; no `CurrentFormatVersion` bump and no migration step
  (absent = unknown). Older Nota versions keep it via `[JsonExtensionData] Extra` on
  round-trip.
- **Sessions-named saves** (`doc.SessionNames`, autosave/recovery) write the same field.
- **Alternatives**: search by file name (clarification rejected); session-only memory.

## R6. Highlight of the selected clip's file

- **Decision**: `MainWindow` already hears `Timeline.ClipSelectionChanged`; Session's slot
  selection gets the equivalent hook. On change it resolves the selected audio clip's
  source (`engine.SampleSource(sampleId)`) and calls `Browser.SetLocatedPath(path | null)`.
  The browser marks the matching row if it is in the visible (flattened) list — tree or
  browse — with a left accent bar + soft tint distinct from selection (nota-design: "orange
  soft tint for picked" is the selection; the located marker uses the orange *line*
  treatment). No expand/scroll/tab switch (FR-019).
- **Locate → In browser**: `BrowserView.RevealFile(path)`: if under a root → switch to Tree,
  expand ancestors, scroll into view, select (no audition); else → Browse at its folder
  with it selected. Expands a collapsed browser first.
- **Locate → In Finder**: `MainWindow.RevealInFileManager(path)` (exists); for a clip with
  no known/existing source, reveal the bundle copy: the saved project's
  `samples/<hash>.wav` when the project is saved, otherwise the item is disabled.

## R7. Projects tab summary

- **Decision**: `ProjectSummaryReader` (Infrastructure) reads `project.json` with
  `System.Text.Json` `JsonDocument` (no full `ProjectDocument` bind, no engine): tempo =
  `Transport.Bpm`; key = `Transport.Key`; tracks = `Tracks.Length` (excluding returns/master
  — they live in `Mixer`/`Master`); length = max clip end in beats over MIDI clips
  (`StartBeat + LengthBeats`), audio clips (warped: `StartBeat + (WarpPlayEnd > 0 ?
  WarpPlayEnd - WarpPlayStart : WarpBeats)`; unwarped: `LengthFrames` or the WAV header's
  frame count of `Sample`, ÷ the WAV's sample rate × bpm/60), and session slots don't
  count; shown as `m:ss` at the project tempo.
- **Cache**: `NOTA_DATA_DIR/cache/project-summaries.json`, keyed by bundle path + the
  manifest's last-write time + size; read on a background task; rows update in place.
- **Date**: the **manifest's** (`project.json`) last-write time, falling back to the bundle
  directory's. Welcome uses the directory's time, but that changes only when entries are
  added/removed, while every save rewrites `project.json`. Welcome switches to the same
  helper so both show the same date.
  Format reuses Welcome's `"MM/dd/yyyy HH:mm"` through one shared helper.
- **Sort**: `Settings.ProjectsSort` = `Date` (default) | `Name`.
- **Open marker**: `BrowserViewModel.OpenProjectPath` set by MainWindow on open/save/new.

## R8. Rail icon size

- **Finding**: `GlyphKind.Folder` is drawn procedurally at ±0.42 of the box; the other rail
  icons are 24-unit SVG paths in `Glyph`'s path table with ~3–6 units of padding (e.g. Doc
  spans x 6–18.4, y 3.5–20.5).
- **Decision**: Move `Folder` to the path table as a 24-grid outline spanning x 3.5–20.5,
  y 5–19 (tab at top-left), same stroke as its neighbours. Check the other users of the
  glyph (group-tracks menu items, Rhythm card at 8 px, Reveal items).

## R9. Tests

- Smoke test project (`tests/Nota.SmokeTest`) is the only test harness; it already has
  `SampleStoreTests`, `SmartSampleTests`, `BrowserCategoryTests`, `BrowserTestSupport`.
- New smoke checks: settings migration (single folder → list; mirror kept), nested/duplicate
  refusal, multi-root tree build, index hint root, `AudioClipDto.Source` save/load
  round-trip (incl. split + bundle-path exclusion), project summary reader (tempo, key,
  tracks, length incl. warped/unwarped audio, unreadable manifest), browse-state
  back/forward/up semantics, path parsing.
- UI checked with the headless-app screenshot harness (rail icon, Files tab both modes,
  Settings → Library list, Projects rows, Locate highlight), all themes + classic look.
