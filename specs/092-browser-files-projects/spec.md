# Feature Specification: Browser — your own sample folders, a file-system browse mode, Locate, and a richer Projects tab

**Feature Branch**: `feat/browser-files-projects`

**Created**: 2026-11-12

**Status**: Draft

**Input**: One feature covering six approved issues in nota-daw/nota, milestone 0.60.1,
shipped in one pull request:
[#268](https://github.com/nota-daw/nota/issues/268) (Files tab: add your own folders),
[#346](https://github.com/nota-daw/nota/issues/346) (Settings → Library: a list of sample folders),
[#579](https://github.com/nota-daw/nota/issues/579) (the Files rail icon is drawn larger than the others),
[#580](https://github.com/nota-daw/nota/issues/580) (Files tab: a full file-system browse mode),
[#581](https://github.com/nota-daw/nota/issues/581) (locate a selected clip's file in the browser / in Finder),
[#582](https://github.com/nota-daw/nota/issues/582) (Projects tab: newest first, with dates).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Where things are today** (for orientation, not a design):

- One Samples folder setting (Settings → Library; empty = `~/Music/Nota Samples`). The
  Files tab shows its tree; sample packs from Downloads install into its `Downloaded`
  subfolder, which is listed first with its own icon. The sample index (tempo, key,
  similar sounds) watches that one folder.
- The Projects tab lists the `.nota` bundles of the projects folder, sorted by name, each
  row reading just "project".
- The Welcome window's recent-projects list shows each project's last-modified date and
  time.
- The rail's Files icon is a hand-drawn folder that fills more of its box than the other
  rail icons, which are drawn on a common 24-unit grid with padding.

## Clarifications

### Session 2026-11-12

- Q: How does the file-system browse mode (#580) sit in the Files tab? → A: A Tree / Browse toggle inside the Files tab; the tree of added folders stays as it is, browse is a separate flat view of one folder with the path bar and Back / Forward / Up.
- Q: What happens when a folder being added is inside, or contains, a folder already in the list? → A: Refuse it, with a note naming the overlapping folder.
- Q: What does a Projects tab row show besides the name (#582)? → A: Last-modified date and time, tempo, length, number of tracks, and the project key when one is set; read from each project in the background and cached.
- Q: Saved projects keep only the bundle copy of a clip's audio, not where it came from — how does Locate (#581) find the file? → A: Projects save each audio clip's original source path (optional, additive field). Locate uses it; without one (older projects, recordings, a deleted source) **In browser** is disabled and **In Finder** reveals the bundle copy.

### Session 2026-11-12 (review of the browse view)

- Q: How do I get back to where browsing started? → A: A **Home** button next to Back / Forward / Up opens the default sample folder.
- Q: How are folders and files told apart in the browse view? → A: Each row has an icon — folder, drive, audio file, MIDI file, other file.
- Q: What does a file row show, and which files are listed? → A: Every file of the folder is listed (hidden ones excepted); each shows its extension and size in their own columns; files Nota can't add (anything but audio and MIDI it can load) are drawn half-transparent and can't be dragged, opened or previewed.
- Q: Can audio be auditioned in the browse view? → A: Yes — selecting an audio file there drives the sample player exactly as in the tree.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Keep samples in several folders and see them all in the browser (Priority: P1) — #346, #268

My samples live in several places — my own recordings, a commercial library on an
external drive, packs from another DAW. I add each folder once, either in Settings →
Library or straight from the Files tab (button or by dragging a folder in from
Finder/Explorer), and every one of them appears in the Files tab as its own top-level
folder, searchable and with tempo/key tags like the Samples folder has today.

**Why this priority**: It's the most requested gap (a reviewer looked for a file browser
and dragged a sample in from Finder instead), and the browse mode and Locate stories build
on the list of folders.

**Independent Test**: Add two folders (one via Settings, one by dragging onto the Files
tab), confirm both show as top-level nodes with their files, are searched, get tags, and
survive a restart; remove one and confirm the files on disk are untouched.

**Acceptance Scenarios**:

1. **Given** an existing install with a custom Samples folder, **When** the new version
   starts, **Then** Settings → Library shows a list with that folder as its first entry,
   marked as the default, and the Files tab looks as before.
2. **Given** Settings → Library, **When** I click **Add folder…** and pick a folder,
   **Then** it is appended to the list and appears as a new top-level node in the Files
   tab, labelled with the folder's name.
3. **Given** the Files tab, **When** I click its **Add folder…** control or drag a folder
   from Finder/Explorer onto the tab, **Then** the folder is added to the same list and is
   visible in Settings → Library as well.
4. **Given** a top-level folder node, **When** I right-click it, **Then** I get **Rename**
   (changes only the label Nota shows), **Remove from browser** (forgets the folder; files
   on disk are never touched), and **Show in Finder / Explorer / file manager**.
5. **Given** several folders, **When** I type in the Files search box or use the sample
   filter chips (loops, one-shots, BPM, key, "similar"), **Then** results come from all
   folders.
6. **Given** a folder on an external drive that is unplugged, **When** I open the browser
   or Settings, **Then** the folder stays in the list, shown as unavailable (dimmed, with a
   note), and its contents come back when the drive is reconnected — at the latest on the
   next refresh or restart.
7. **Given** a non-default folder, **When** I mark it **Default** in Settings → Library,
   **Then** sample packs downloaded afterwards install into it (into its `Downloaded`
   subfolder), and the default folder is the one listed first in the Files tab.

---

### User Story 2 - The Files rail icon is the same size as its neighbours (Priority: P1) — #579

The rail of browser tabs reads as one row of equal icons; the Files icon no longer looks
bigger than Instruments, FX, MIDI, Presets, Projects, Map, History and Track.

**Why this priority**: A small visual defect, fixed in minutes, visible to everyone.

**Independent Test**: Screenshot the rail in each colour theme and the classic look;
the folder's drawn extent matches the other icons' (same padding, same stroke weight).

**Acceptance Scenarios**:

1. **Given** the browser rail, **When** I compare the Files icon with the Projects icon,
   **Then** their drawn heights and widths differ by no more than the shapes themselves
   require (a folder is wider than tall), and both sit inside the same padding.
2. **Given** the other places that draw the folder icon (group-tracks menu items, the
   Rhythm card, "Show in Finder" menu items), **When** I open them, **Then** the icon still
   reads clearly at their sizes.

---

### User Story 3 - Browse anywhere on the computer from the Files tab (Priority: P2) — #580

Sometimes the sample I need is outside every folder I've added — a bounce folder, Downloads,
another drive. I switch the Files tab into a **browse** view that works like a small file
manager: it opens at the default Samples folder, I can go up past it to the drive roots,
step back and forward, see where I am in a path bar, click a path segment to jump there,
copy the path, or paste a path and go straight to it.

**Why this priority**: Saves a trip to Finder/Explorer, but the folder list (P1) already
covers the common case.

**Independent Test**: Enter browse mode, go up to the volume root, into another drive,
back and forward, paste a path, copy a path; preview a file and drag it into the
arrangement.

**Acceptance Scenarios**:

1. **Given** the Files tab, **When** I switch to browse mode, **Then** it shows the
   contents of the default Samples folder (or of the folder last selected in the tree, if
   one is), with a path bar on top and **Back**, **Forward**, **Up** and **Home** buttons.
2. **Given** browse mode at any folder, **When** I press **Up** repeatedly, **Then** I
   reach the drive/volume root and, above that, a list of drives/volumes (Windows drive
   letters; macOS `/Volumes` entries and the system disk; Linux `/` and mounted media).
3. **Given** I have navigated through several folders, **When** I press **Back** and
   **Forward**, **Then** I move through the visited folders like in a web browser; opening
   a new folder after going back clears the forward history.
4. **Given** the path bar, **When** I click a segment, **Then** browse mode jumps to that
   folder.
5. **Given** the path bar, **When** I choose **Copy path**, **Then** the current folder's
   full path is on the clipboard; a selected file's context menu also has **Copy path**.
6. **Given** the path bar, **When** I click into it, paste or type a path and press Enter,
   **Then** browse mode opens that folder (or the folder containing it, with the file
   selected, if it is a file); an invalid or unreadable path leaves me where I was and
   says so.
7. **Given** browse mode, **When** I preview, double-click or drag an audio or MIDI file,
   **Then** it behaves exactly as in the tree view (audition, drop into the arrangement /
   Session / device).
8. **Given** browse mode at a folder that isn't in the list yet, **When** I choose **Add
   this folder to the browser**, **Then** it is added to the list from Story 1.
9. **Given** browse mode, **When** I switch back to the tree view, **Then** the tree is as
   I left it, and switching to browse mode again returns to the last browsed folder.
10. **Given** browse mode anywhere, **When** I press **Home**, **Then** it opens the default
    sample folder (a step Back returns where I was).
11. **Given** a folder holding subfolders, audio, MIDI and other files, **When** browse mode
    shows it, **Then** every row has an icon for its kind (folder, drive, audio, MIDI, other
    file), each file shows its extension and size in separate columns, and files Nota can't
    add are half-transparent and can't be dragged, opened or previewed.
12. **Given** browse mode, **When** I select an audio file (click or arrow keys), **Then** the
    sample player auditions it as in the tree view (Auto, Space, Loop, waveform).

---

### User Story 4 - Find a selected clip's file (Priority: P2) — #581

I select an audio clip in the arrangement or in Session and want to see where its file
lives — to find a similar sample next to it or reuse it. If its folder is open in the Files
tab, the file's row is highlighted; from the clip's context menu I can **Locate → In
browser** to open the Files tab right at the file, or **Locate → In Finder** (Explorer,
file manager) to reveal it on disk.

**Why this priority**: Valuable navigation, but depends on the folder list for "in
browser" to find most files.

**Independent Test**: Drag a sample from a nested folder into the arrangement, collapse
the tree, select the clip → no highlight; expand the folder → the row is highlighted;
Locate → In browser expands, scrolls to and selects it; Locate → In Finder reveals it.

**Acceptance Scenarios**:

1. **Given** an audio clip whose file is inside one of the browser's folders, and the
   Files tab shows the folder containing it expanded, **When** I select the clip in the
   arrangement or Session, **Then** the file's row is highlighted (a marker distinct from
   the normal selection), and the highlight moves or clears as the clip selection changes.
2. **Given** the same clip but the containing folder collapsed (or another tab showing),
   **When** I select the clip, **Then** nothing in the browser changes — no automatic
   expanding, scrolling or tab switch.
3. **Given** browse mode showing the folder that contains the clip's file, **When** I
   select the clip, **Then** its row is highlighted there too.
4. **Given** an audio clip, **When** I right-click it and choose **Locate → In browser**,
   **Then** the browser opens the Files tab, expands the folders down to the file, scrolls
   it into view and selects it (without auditioning it).
5. **Given** an audio clip whose file is outside every browser folder, **When** I choose
   **Locate → In browser**, **Then** browse mode opens at the file's folder with the file
   selected.
6. **Given** a project saved and reopened, **When** I right-click an audio clip that was
   dragged in from a sample folder before saving, **Then** Locate still leads to that
   original file (the project remembers where each clip's audio came from).
7. **Given** a clip with no known source (recorded in Nota, from a project saved by an
   older version, or created by consolidate / export), **When** I open its context menu,
   **Then** **In browser** is disabled and **In Finder** reveals the clip's audio inside
   the project bundle.
8. **Given** an audio clip, **When** I choose **Locate → In Finder** (macOS) / **In
   Explorer** (Windows) / **In file manager** (Linux), **Then** the OS file manager opens
   with the file selected (or its folder opened, where the OS can't select).
9. **Given** a clip whose source file no longer exists on disk, **When** I open its context
   menu, **Then** **In browser** is disabled and **In Finder** reveals the bundle copy.
10. **Given** a MIDI clip (no source file), **When** I open its context menu, **Then** there
   is no Locate submenu.

---

### User Story 5 - See my newest projects first, with dates (Priority: P2) — #582

The Projects tab lists the most recently changed project first and shows when each was
last changed, the way the Welcome window's recent list does, so I can pick up yesterday's
work without remembering its name.

**Why this priority**: Self-contained, used every session.

**Independent Test**: Save three projects at different times and tempos; the Projects tab
lists them newest first, each with its date and time, tempo, length, track count and key
(when set); switch the sort to name and back.

**Acceptance Scenarios**:

1. **Given** the projects folder with several projects, **When** I open the Projects tab,
   **Then** they are listed by last-modified time, newest first.
2. **Given** a project row, **When** I look at it, **Then** it shows the project name and,
   on a muted second line, its last-modified date and time in the same format as the
   Welcome window's recent list, its tempo, its length, its number of tracks, and its
   key when the project has one set.
3. **Given** the Projects tab, **When** I switch the sort to **Name**, **Then** projects are
   ordered A–Z; the choice is remembered across restarts.
4. **Given** I save the open project, **When** I look at the Projects tab, **Then** that
   project moves to the top with its new date (on the next refresh at the latest — the tab
   refreshes when shown).
5. **Given** the search box, **When** I type, **Then** filtering works as today and keeps
   the chosen order.
6. **Given** the project that is currently open, **When** I look at the list, **Then** its
   row is marked as open.
7. **Given** a projects folder with many projects, **When** I open the Projects tab,
   **Then** names and dates appear at once and tempo / length / tracks / key fill in as
   they are read in the background; a project that hasn't changed since is not re-read.
8. **Given** a project that can't be read (damaged or from a newer version), **When** its
   row is shown, **Then** it shows the name and date only.

### Edge Cases

- Adding a folder that is already in the list → nothing is added; the existing node is
  selected and a short note says it's already there.
- Adding a folder that sits inside, or contains, a folder already in the list → refused
  with a note naming the overlapping folder (so no file is listed or indexed twice). The
  same check applies to **Add this folder to the browser** in browse mode.
- Removing the default folder → refused while it's the only folder; otherwise the next
  folder becomes the default, after a confirmation that names it.
- The list may not be emptied: the last folder can be changed but not removed.
- Two folders with the same name → both shown; the label is the folder name, and the
  tooltip shows the full path; Rename lets the user tell them apart.
- A folder becomes unavailable while browsing it → its node shows unavailable; preview of
  its files fails quietly with the normal "file not found" status.
- Very large folders (500 GB+, see #358) → adding one must not freeze the app; listing and
  indexing stay in the background with the existing per-folder file cap and progress line.
- Browse mode in folders the OS denies access to (macOS privacy-protected folders,
  Windows system folders) → an "unreadable folder" note in place of the list; the app
  never prompts in a loop.
- Browse mode lists every folder and file except hidden ones; files Nota can't add are shown
  half-transparent and are inert.
- Browse mode does not add folders to the sample index; tags appear only for files that
  are also inside an added folder (or after the user adds the folder).
- Locate on a clip whose file is inside a `Downloaded` pack folder → works like any other
  file in the default folder.
- Locate when the browser is collapsed → **In browser** expands it first.
- Projects folder contains a bundle with an unreadable date → it sorts last, with no date.
- Settings from older versions with an empty Samples folder (meaning the default
  location) → migrate to a list whose one entry is the default location.

## Requirements *(mandatory)*

### Functional Requirements

**Sample folders (#346, #268)**

- **FR-001**: The app MUST store a list of sample folders (one or more) in place of the
  single Samples folder setting, each with a path, an optional display name, and exactly
  one marked as default.
- **FR-002**: On first start after upgrade, the existing Samples folder (or the default
  location when unset) MUST become the first, default entry; nothing else changes for a
  user who never touches the new controls.
- **FR-003**: Settings → Library MUST show the list with **Add folder…**, **Remove**,
  **Set as default** and **Show in Finder/Explorer** per entry, accept folders dropped from
  the OS file manager, and show unavailable folders as such.
- **FR-004**: The Files tab MUST show each folder as a top-level node (default first, then
  in list order), and offer **Add folder…** and folder drag-and-drop that write to the same
  list.
- **FR-005**: A top-level folder node's context menu MUST offer **Rename**, **Remove from
  browser**, and **Show in Finder/Explorer/file manager**; Remove and Rename never touch
  files on disk.
- **FR-006**: Every folder MUST be indexed like the Samples folder is today (tempo, key,
  loop/one-shot, similar sounds), in the background, and take part in search, filter
  chips and "similar".
- **FR-007**: Downloaded sample packs MUST install into the default folder's `Downloaded`
  subfolder; packs already installed in the old default stay where they are and remain
  visible.
- **FR-008**: Duplicate and overlapping (nested) folders MUST be refused with a note, as
  described in Edge Cases.
- **FR-009**: Everything else that reads "the Samples folder" today (pack installs, kit
  folders, similar-sample search, MCP sample tools) MUST keep working, using the default
  folder where a single folder is needed and all folders where a search is meant.

**Rail icon (#579)**

- **FR-010**: The folder icon MUST be drawn with the same padding and stroke weight as the
  other rail icons, so that at the rail's size it reads as the same size as its
  neighbours, in every theme and in the classic look.

**Browse mode (#580)**

- **FR-011**: The Files tab MUST offer a toggle between the folder tree and a browse view.
- **FR-012**: The browse view MUST show one folder's subfolders and files, folders first,
  each group sorted by name; hidden entries are not shown. Files Nota can't add (anything
  but the audio and MIDI it loads) MUST be drawn half-transparent and MUST NOT be dragged,
  opened or previewed.
- **FR-012a**: Each browse row MUST carry an icon for its kind (folder, drive, audio file,
  MIDI file, other file); each file row MUST show its extension and its size in separate
  columns.
- **FR-013**: The browse view MUST provide **Back**, **Forward**, **Up**, **Home** (the
  default sample folder), a path bar with
  clickable segments, **Copy path**, and an editable path field that accepts a typed or
  pasted folder or file path.
- **FR-014**: **Up** MUST continue past the samples folder to the volume root and then to a
  list of drives/volumes appropriate to the OS.
- **FR-015**: Files in the browse view MUST support the same preview, double-click and
  drag-and-drop as files in the tree, plus **Copy path** and **Show in
  Finder/Explorer/file manager** in their context menu.
- **FR-016**: A folder shown in the browse view MUST offer **Add this folder to the
  browser**, which goes through the same checks as FR-008.
- **FR-017**: Search in the browse view MUST filter the current folder's entries only.
- **FR-018**: The tree/browse choice and the last browsed folder MUST be remembered across
  restarts; a remembered folder that no longer exists falls back to the default folder.

**Locate (#581)**

- **FR-019**: While an audio clip is selected in the arrangement or Session, the Files tab
  MUST highlight that clip's file row if the row is currently visible in the list (its
  folder is expanded in the tree, or its folder is the one shown in browse mode). It MUST
  NOT expand, scroll or switch tabs on its own.
- **FR-019a**: Saving a project MUST record, for each audio clip whose audio came from a
  file on disk, that file's original path; loading MUST restore it. The field is optional
  and additive: projects without it load as before, and older Nota versions ignore it.
- **FR-020**: The context menu of an audio clip (arrangement and Session) MUST offer
  **Locate → In browser** and **Locate → In Finder** (Explorer on Windows, File manager on
  Linux).
- **FR-021**: **In browser** MUST bring the Files tab up, reveal the file (expand its
  folders, scroll, select) in the tree when it is inside an added folder, or open browse
  mode at its folder with it selected otherwise; it MUST NOT start a preview.
- **FR-022**: **In Finder/Explorer/file manager** MUST reveal the clip's original file in
  the OS file manager, or the clip's audio inside the project bundle when the original
  path is unknown or the file is gone.
- **FR-023**: **In browser** MUST be disabled when the clip has no known original path or
  the file at it is missing; MIDI clips show no Locate submenu.

**Projects tab (#582)**

- **FR-024**: The Projects tab MUST sort projects by last-modified time, newest first, by
  default, with a choice of **Date** / **Name** that is remembered.
- **FR-025**: Each project row MUST show the last-modified date and time in the format the
  Welcome window's recent list uses, plus tempo, length, number of tracks, and key when
  set.
- **FR-025a**: Tempo, length, track count and key MUST be read in the background without
  blocking the list, cached per project, and re-read only when the project has changed
  since; an unreadable project shows name and date only.
- **FR-026**: The currently open project's row MUST be marked.
- **FR-027**: The list MUST refresh when the tab is shown and after the open project is
  saved.

**Common**

- **FR-028**: All new labels MUST be localised in English (in-app) and Russian (nota-translations).
- **FR-029**: All new UI MUST follow the Studio design language (DESIGN.md) and work in all
  four colour themes and in the classic look.

### Key Entities

- **Sample folder**: a folder on disk the user added to the browser — path, optional
  display name, default flag, availability (present / unavailable).
- **Browse location**: the folder the browse view is showing, plus back/forward history
  (session-only) and the last location (persisted).
- **Project entry**: a project bundle in the projects folder — name, path, last-modified
  time, whether it is the open project, and a cached summary (tempo, length, track count,
  key when set).
- **Clip source**: the original file an audio clip's audio was taken from — a path that
  may be unknown or point to a file that no longer exists.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A user can add a second sample folder and find a file in it by search in
  under 30 seconds, without leaving Nota.
- **SC-002**: Upgrading users see the same Files tab contents as before the upgrade, with
  no manual step (100% of existing Samples folder settings migrate).
- **SC-003**: From a selected audio clip, the user reaches its file in the browser or in
  Finder/Explorer in 2 clicks (right-click, choose Locate item).
- **SC-004**: From the default Samples folder, the user can reach any readable folder on
  any mounted drive using only Up, Back/Forward, path segments or a pasted path.
- **SC-005**: Adding a folder with tens of thousands of files never freezes the UI; the
  first level of the folder appears within 1 second and the rest fills in in the
  background.
- **SC-006**: The most recently saved project is the first row of the Projects tab in
  100% of cases (date sort), with its date visible without hovering.
- **SC-008**: With 200 projects in the folder, the Projects tab shows all names and dates
  within 1 second and the remaining details within 10 seconds on first open, instantly on
  later opens.
- **SC-007**: In a side-by-side screenshot of the rail, the Files icon's drawn extent is
  within 1 pixel of the other rail icons' padding.

## Assumptions

- Default suggestions (Desktop, Downloads, Music) from #268 are **not** added
  automatically; the user adds what they want. Browse mode covers one-off access.
- Rename changes only the label Nota shows, never the folder on disk.
- Locate applies to audio clips; MIDI clips imported from `.mid` files don't keep a link
  to the file and are out of scope.
- Clips already in projects saved before this change have no recorded source; they get
  one only when re-added from a file.
- A thumbnail or waveform preview of a project is out of scope.
- Folder availability is re-checked when the browser refreshes (tab shown, Settings
  opened, app start) — no live file-system watching of drive mounts is required.
- The Linux label for the OS file manager is "Show in file manager", matching the
  existing Reveal wording on each platform.
- No engine change: this is managed app and UI work only; the smoke test is unaffected
  except where it reads settings.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — UI/managed only, no audio-thread code touched
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no ABI change
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — no engine change; project format gains an optional clip-source field: check save/load round-trip and settings migration don't break it
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
