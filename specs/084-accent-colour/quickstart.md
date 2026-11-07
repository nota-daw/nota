# Quickstart: verifying the accent colour

## Prerequisites

`scripts/build.sh` builds the engine and the app and runs the smoke test.

## 1. Data checks (smoke test)

```bash
scripts/build.sh
```

Expect `RunAccent` lines: every preset × theme passes T3.4/T3.5/T3.8/A1/A2/A3; Orange is the
identity; `#8C8C8C` refused as grey; `#3FA7FF` refused in Studio Light (solo); `#4C6FFF`
accepted with an adjustment; resolution fallbacks. `RunThemes`, `RunBans`, `RunLabels`,
`RunLocalization` stay green.

## 2. In the app

1. Launch with a scratch data dir (`NOTA_DATA_DIR=/tmp/nota-accent`), open Settings →
   Appearance: the Accent row shows Orange chosen; nothing else looks different.
2. Click Violet: the play key, loop, selection, toggles, menu hover turn violet at once in
   every window; the theme cards show violet.
3. Switch theme to Studio Light, Ember Graphite, Ember Paper: violet, tuned per theme.
4. Custom → type `#00B5D8`: applied, note "Worn as is in every theme." Type `#8C8C8C`: the
   note refuses, the accent stays cyan. Press Esc: the accent returns to violet.
5. Custom → `#4C6FFF`, click outside: saved; restart Nota: the first frame is that blue.
6. Pick Orange: identical to a build before this change (screenshot diff).

## 3. Nota Remote

Connect the phone, change the accent: the phone's play key and selection repaint within 1 s.

## 4. Mockup

`python3 specs/084-accent-colour/mockup/build.py` regenerates `accent.html`; its values must
match the smoke test's.
