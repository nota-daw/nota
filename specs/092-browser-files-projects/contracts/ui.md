# Contract: UI surfaces (spec 092)

All strings via `Loc.T` with EN + RU entries; all colours/radii/fonts from tokens
(nota-design); every surface checked in the four themes and the classic look.

## Browser rail

- Files tab icon: `GlyphKind.Folder` as a 24-grid path (x 3.5–20.5, y 5–19), same stroke as
  neighbours. No other rail change.

## Files tab — header

```
[ Search…                          123 ]
[ Tree | Browse ]                 [ + ]      ← segmented toggle · Add folder… (Tree mode)
```

Browse mode adds, under the toggle:

```
[ ‹ ] [ › ] [ ↑ ] [ ⌂ ]  Macintosh HD › Users › me › Music ›  Nota Samples   [⧉]
```

- `‹` Back, `›` Forward, `↑` Up (disabled at Computer level); tooltips with shortcuts:
  Back ⌘[, Forward ⌘], Up ⌘↑ (Ctrl elsewhere, where Alt+Left / Right / Up also work; Files
  tab focused).
- `⌂` Home: the default sample folder (disabled while there).
- Rows: kind icon (folder / drive / audio / MIDI / other); files: extension and size columns
  (size fixed width, right-aligned); a file Nota can't add in the disabled ink, inert.
- Segments are buttons; long paths elide from the left (first segment kept).
- Click empty bar area or ⌘L / Ctrl+L → editable text field (full path, selected); Enter
  go, Esc cancel; error → field shakes/edge turns red token + status "Can't open <path>".
- `⧉` Copy path (current folder) → status "Path copied".

## Files tab — rows

| Row | Tree | Browse | Context menu (additions) |
|---|---|---|---|
| Sample-folder root (only with 2+ folders; one folder keeps today's flat tree) | top-level, label = Name ?? folder; dimmed when missing | — | Rename · Remove from browser · Show in Finder/Explorer/file manager · Set as default |
| Folder | as today | navigates on click/double-click/Enter | Copy path · Show in … · Add this folder to the browser (browse only) |
| Audio / MIDI file | as today | same behaviour as tree (preview, drag, double-click) | Copy path · Show in … |
| Located (selected clip's source) | orange left edge line (2 px) on top of normal styling | same | — |

- Dropping a folder (from the OS) on the Files tab → AddSampleFolder; files dropped behave
  as today.
- Notes for refused adds: "Already in the browser" / "Inside <Label> — already in the
  browser" / "Contains <Label> — remove it first".
- Browse mode empty/unreadable states: "This folder has no audio or MIDI files" /
  "Nota can't read this folder".
- Computer level rows: volume icon + name; double-click enters.

## Settings → Library — Sample folders

```
Sample folders
  Nota Samples   ~/Music/Nota Samples                default
  Splice         /Volumes/Lib/Splice                 unavailable
[ Add folder… ] [ Set as default ] [ Rename… ] [ Show ] [ Remove ]
```

- A well of selectable rows (like Plug-ins → scan folders) over a row of buttons acting on
  the selected folder; folders can be dropped on the well. Remove of the default asks:
  "<Next> becomes the default folder…". Remove is disabled for the only entry.
- Sits above the Content folders section, which keeps the Projects folder picker.

## Clip context menus (arrangement lane control + Session grid slot)

Audio clips only, single-clip menu (not in multi-selection header):

```
Locate ▸  In browser
          In Finder            (macOS)  ·  In Explorer (Windows)  ·  In file manager (Linux)
```

- In browser disabled when no known source or source missing.
- In Finder enabled when source exists **or** the project is saved (reveals the bundle copy).

## Projects tab rows

```
My Song                                    ● (open)
11/12/2026 14:20 · 124 BPM · Am · 3:42 · 12 tracks
```

- Second line muted (TextTertiary); details absent until read; unreadable → date only.
- Header right of search: sort toggle `Date | Name`.
