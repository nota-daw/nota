---
name: nota-docs
description: Keep the user documentation (sibling repo ../nota-docs, Astro Starlight, EN + RU) in sync with the app. Run this WHENEVER you add, change or remove a user-visible feature — a new device or parameter, a new view/panel/window, a changed workflow, a moved menu item, a new or re-mapped shortcut, a new Preferences option — i.e. whenever you write a CHANGELOG [Unreleased] entry. Also run it from /release to catch anything that slipped. Handles: finding the affected pages, editing the EN page and its RU twin, re-shooting screenshots (nota-design mockups or the headless app), regenerating the shortcuts page, and building/checking the site.
user-invocable: true
---

# Keeping Nota Docs honest

The user manual lives in **`../nota-docs`** (relative to this repo; GitHub `nota-daw/nota-docs`),
built with Astro Starlight in **English (root) and Russian (`/ru/`)**. It is written by hand,
so every user-visible change in this repo must be mirrored there or the manual lies. The
overall structure and conventions are in `../nota-docs/PLAN.md` — read it once if you have
not.

**Trigger:** you wrote (or are about to write) a `CHANGELOG.md` entry under `[Unreleased]`.
`Added` / `Changed` / `Removed` almost always need a docs edit; `Fixed` only when the fix
changes what the docs describe. Pure refactors, perf and internal changes need nothing.

**Argument** (`/nota-docs <arg>`, optional): a feature name, a changelog line, or `audit`
(reconcile *all* `[Unreleased]` entries — used by `/release`). No argument → the change you
just made in this session.

If the page that should cover the change is still a stub (`status: todo`) and you are not
writing it now, append `{page, change, version}` to the `pending:` list at the bottom
of `../nota-docs/coverage.yml` so the stage that writes that page picks it up, tell the user,
and stop.

## 1. Work out what changed for the user

From the diff and the CHANGELOG entry, list concretely: new/renamed UI labels, new
parameters with ranges and defaults, new menu items and where they sit, new shortcuts,
changed behaviour, removed things. Take names and numbers **from the code** (the device's
DSP class and card under `src/managed/Nota.App/DeviceCards/…`, menus in `MainWindow.axaml`,
`PreferencesWindow.cs`), never from memory or the mockup — the changelog itself can be
contradicted by a later entry.

## 2. Find the pages

1. `../nota-docs/coverage.yml` maps features and source paths → page slugs. Look up every
   file you touched and every feature you named.
2. Grep page frontmatter `sources:` in `../nota-docs/src/content/docs/**/*.mdx` for the
   touched paths, and grep page bodies for the old UI label / old shortcut.
3. Nothing found → it is a new feature. Decide its place in the sidebar (see `PLAN.md`),
   copy `templates/page.mdx` (or `templates/device.mdx`) into both
   `src/content/docs/<slug>.mdx` and `src/content/docs/ru/<slug>.mdx`, and add it to
   `coverage.yml`. The sidebar autogenerates per directory (order via `sidebar.order`); only a
   brand-new top-level section needs an entry in `astro.config.mjs`.

Typical fan-out — check all that apply:
- the feature's own page (device, view, panel, preference pane);
- the overview that lists it (Devices index, Views index, "Main window tour");
- `reference/shortcuts` — regenerated, see step 4;
- the glossary, if you introduced a term;
- tutorials that walk through the changed flow (the old steps may no longer work);
- a new built-in device or a renamed device page → the slug maps in
  `src/managed/Nota.App/NotaDocs.cs` (`Instruments` / `AudioEffects` / `MidiEffects`, indexed by
  engine kind) so the device header's Documentation item opens the right page.

## 3. Edit EN, then RU

- English page under `src/content/docs/<slug>.mdx`, Russian twin at
  `src/content/docs/ru/<slug>.mdx` — **same path, same structure, same screenshots**. Never
  leave one language behind; `scripts/check.mjs` fails on a missing or stale twin.
- A stub page (`status: todo`) you fill in becomes `status: draft` (or `done` once its
  screenshots are in); drop the "Being written" aside.
- Bump `updated:` in both frontmatters to the version in `VERSION` (plus `since:` on new
  pages); add any new source paths to `sources:`.
- Style: short, second person, like the CHANGELOG. UI names exactly as on screen in
  **bold**, keys via `<Kbd>`. In Russian, UI names stay in English (the app is English); the
  explanation is native Russian, not a word-for-word translation.
- Removed feature → delete the text and its screenshots; removed page → delete both
  languages, the sidebar entry and the `coverage.yml` entry, and fix links into it.

## 4. Screenshots and generated pages

`../nota-docs/shots/manifest.yml` lists every image (its header documents the scenes and
args). `npm run shots -- <id-prefix> …` retakes them into `src/assets/shots/{dark,light}/`.

- **app** shots render the real UI headlessly (`shots/app`, both themes, 2×, user/device names
  scrubbed). They build against this repo's `src/managed/Nota.App`, so build the app first if
  you changed it. **design** shots come from `../nota-design` (dark only).
- UI that changed visibly → retake its ids (e.g. `npm run shots -- devices/audio-effects/shutter`).
- New device → add `devices/<group>/<slug>/card` (`scene: device` with its kind id) plus one
  shot per tab you document (`args: {tab: "<tab text>"}`, a chain like `"L>Env"` when a size
  switch comes first, `preset` for an interesting state). Write the page from
  `templates/device.mdx`; facts come from the engine header's top comment and `paramName`,
  the card file's header comment and labels, and `FactoryPresetCatalog` — give a range only
  when the code states it.
- New window / view / Settings page → a new scene in `shots/app/Scenes.cs` if none fits; keep
  it deterministic (demo song, no real user data).
- Mockup no longer matches the code → switch that id to `source: app`; never document the mockup.
- A scene breaks after an app refactor (renamed private method, control name) → fix the
  harness; it reflects into `MainWindow` / `PreferencesWindow` privates on purpose.
- Shortcut changed → `npm run shortcuts` regenerates `reference/shortcuts` (EN + RU) from
  `PreferencesWindow.ShortcutGroups`; run the `nota-shortcuts` skill first so that list is
  right.

## 5. Verify and commit

```bash
cd ../nota-docs
npm run build
npm run check
```

Node may not be on PATH (Homebrew's node isn't linked on this Mac) — use
`/opt/homebrew/Cellar/node/*/bin`. The dev server is the `nota-docs` entry in this repo's
`.claude/launch.json` (preview_start).

Both must pass (broken links, missing RU twin, image id not in manifest, `updated:` older
than a touched source). Look at the changed pages in `npm run preview` via the built-in
browser when screenshots or layout changed.

Commit in **`../nota-docs`**, separately from the app repo: `(docs) <what changed>`, no
Claude attribution trailer. Don't push unless the user asked.

## 6. Report

List the pages changed (EN + RU), screenshots re-shot, anything you could not document
(e.g. a scene the harness can't render) and the gaps you left in `coverage.yml`.

## /release audit mode

Go through every `Added` / `Changed` / `Removed` entry of `[Unreleased]`, check each against
the docs as above, fix what's missing, then `npm run build && npm run check`. Report
the coverage to the user before the tag is created; missing docs do not block a release, but
say so plainly.

## Don't
- Don't document plans, mockup-only features or anything not in the code.
- Don't update only one language.
- Don't hand-edit the generated shortcuts page.
- Don't copy CHANGELOG prose verbatim — the changelog says what changed, the docs say how
  to use it now.
