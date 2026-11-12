# Quickstart: validating spec 092

## Prerequisites

- Release engine build in `build/` (see BUILD.md; never a Debug engine there).
- A scratch data dir so real settings aren't touched: `export NOTA_DATA_DIR=/tmp/nota092`.
- Two sample folders, one nested folder and one "external" folder, e.g.
  `/tmp/s092/A/Drums/kick.wav`, `/tmp/s092/B/loop.wav`, `/tmp/s092/A/Drums` (nested),
  `/tmp/s092/Other/far.wav` (never added).

## Gate

```bash
scripts/build.sh
```

Builds native + managed and runs the smoke test; the new smoke checks (research R9) must
pass.

## Manual / harness checks

1. **Migration** — start with an old settings file holding only `SamplesFolder=/tmp/s092/A`.
   Settings → Library lists A as default; the Files tab looks as before; the saved settings
   still contain `SamplesFolder` = A.
2. **Add folders** — add B via Settings, drag `/tmp/s092/Other` onto the Files tab; both
   show as roots; search "loop" finds B's file; tags appear after the scan. Add
   `/tmp/s092/A/Drums` → refused ("Inside A"). Rename B → label changes, disk unchanged.
3. **Unavailable** — rename `/tmp/s092/B` on disk, reopen the tab → B dimmed "Unavailable";
   restore → back.
4. **Default** — set B as default, install a pack from Downloads → lands in `B/Downloaded`.
5. **Rail icon** — headless screenshot of the rail in Studio Dark/Light, Ember Graphite/Paper,
   classic look; the folder sits in the same padding as Doc/Bookmark.
6. **Browse mode** — toggle Browse: opens at default folder; Up to `/`, then Computer;
   Back/Forward; click a segment; paste `/tmp/s092/Other/far.wav` + Enter → folder opened,
   file selected; Copy path; drag `far.wav` into the arrangement; restart → Browse mode and
   last folder restored.
7. **Locate** — drag `A/Drums/kick.wav` into the arrangement; collapse `Drums` and select
   the clip → no highlight; expand → row highlighted; right-click → Locate → In browser
   (tree reveals + selects, no audition); Locate → In Finder reveals it. Save, close,
   reopen → Locate still works. Record a clip → In browser disabled, In Finder reveals
   `samples/<hash>.wav`. Repeat in Session.
8. **Projects** — save three projects a minute apart with different tempos/keys; the tab
   lists newest first with `MM/dd/yyyy HH:mm · BPM · key · m:ss · N tracks`; the open one
   is marked; switch to Name and restart → kept; corrupt one `project.json` → that row
   shows name + date only.
9. **RU** — switch language to Russian; every new label is translated.

UI evidence for the PR: before/after shots of the rail, Files tab (tree with two roots,
browse mode), Settings → Library, a Locate highlight, Projects tab.
