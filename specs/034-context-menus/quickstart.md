# Quickstart: validating Studio context menus

## Prerequisites

- macOS dev machine with the repo built once (`scripts/build.sh`).
- Scratch data dir so settings (the recent-instruments list) don't touch the real ones:
  `export NOTA_DATA_DIR=/tmp/menus-data`.

## 1. Gate

```bash
scripts/build.sh
```

Expect green, including `DesignTokenCheck` (`RunMenus`, `RunIcons`, `RunLabels`,
`RunThemes`) and `RecentInstrumentsTests`.

## 2. Visual check (headless)

Menus are popups and do not render headless; build each menu's **content** into a
`Window` instead (see the project's UI-verify recipe: Avalonia.Headless +
`CaptureRenderedFrame`). Shoot, in Studio Dark / Studio Light / Ember Graphite /
Ember Paper:

- the track menu of an audio track (header, colour grid with the current colour ringed,
  Height segments with Standard chosen, Record input value, Live freeze switch off,
  clipboard group, Delete in danger ink), with one item forced to `:pointerover`;
- the same track live-frozen (switch on, Edit source, Flatten in danger ink);
- the multi-track menu (header "3 tracks");
- the clip, device, Session slot, scene, browser item and history version menus;
- "Add track" submenu with a populated "Recent" group.

Compare against [contracts/menu-kit.md](contracts/menu-kit.md) and `DESIGN.md` § Menus.

## 3. Behaviour (in the app)

| Step | Expected |
|---|---|
| Right-click a track, click a swatch | colour changes, menu closes, ⌘Z restores it (one step) |
| Click "Large" | track grows; Option-click "Compact" → every track compact |
| Select 3 tracks, right-click one, click a swatch | all three recoloured, one undo step |
| Live freeze switch on an instrument track | live-freezes as before; reopen → switch on; click → unfreezes |
| Add Volt, then a plug-in instrument from the browser; quit; relaunch | "Add track ▸ Recent" lists the plug-in, then Nota Volt |
| Click a Recent entry | new instrument track with it, after the clicked track |
| Arrow keys in the colour grid, Enter | moves the ring, applies, closes |
| Any menu on macOS | shortcuts read `⌘C`, never `⌘+C` |

## 4. Docs

`DESIGN.md` § Menus updated, `python3 scripts/design-html.py` regenerated, CHANGELOG
`[Unreleased]` entry, FEATURES.md (nota-features), user docs (nota-docs), Shortcuts pane
(nota-shortcuts) for Option-click on height keys.
