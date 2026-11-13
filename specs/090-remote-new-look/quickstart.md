# Quickstart: validating spec 090

## Prerequisites

- Node from the repo's usual location (node is not on PATH in some shells — see BUILD.md),
  `npm ci` in `src/web/remote`.
- A Release engine build (`scripts/build.sh`).

## 1. Static checks

```bash
cd src/web/remote && npm run check && npm run build
```

Expected: svelte-check 0 errors / 0 warnings; the bundle builds into
`src/managed/Nota.Remote/wwwroot` with no `GeistMono` file.

```bash
scripts/build.sh
```

Expected: green, including `DesignTokenCheck.Remote` (radii on the scale variables, no
monospace, `--picked-ink` defined) and `RemoteTests` (theme `font` / `r`; `clips` sent on
Scenes only, on change only, capped).

## 2. The look, against the mock (no desktop needed)

```bash
cd src/web/remote && npm run mock
```

Open `http://127.0.0.1:7790/#p=1234` at 390 × 844 (phone), 844 × 390 (phone landscape) and
1024 × 1366 (tablet). For each of `?theme=studio-dark|studio-light|ember-graphite|ember-paper`
and `?skin=square-avenir` (a v2 skin with `radius.scale: 0` and a font family):

- every screen and overlay: picked = soft tint + orange text, never solid; live = solid;
  Mixer grey at rest; Macros / XY values in the track colour;
- numbers: tabular, `−` and thin-space units; labels sentence case;
- the skin: no rounded corners; text in the family if installed, Geist otherwise.

## 3. Clip previews, against the real hub

Use the real-server harness (RemoteServer + hub over an engine project with MIDI and audio
Session clips) and the desktop side by side:

1. Open Scenes on the phone: each filled cell shows name, length and its notes / waveform,
   matching the desktop slot.
2. Edit a MIDI clip's notes on the desktop → the phone cell follows within 1 s.
3. Record into a Session slot (MIDI and audio) → the phone cell grows in the record colour.
4. Leave Scenes → no `clips` frames arrive (browser devtools WS view); return → one full set.

## 4. Evidence for the PR

Before/after screenshots per screen × {phone, phone landscape, tablet} × 4 themes + the
skin; Scenes with previews beside the desktop Session view. Use the `nota-pr` skill.
