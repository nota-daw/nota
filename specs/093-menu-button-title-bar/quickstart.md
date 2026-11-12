# Quickstart: validating the Menu button and the hideable menu bar

## Prerequisites

- A Release build: `scripts/build.sh` (macOS) / `scripts/build-linux.sh` / `scripts/build-win.ps1`.
- A fresh data dir for default-settings checks: `NOTA_DATA_DIR=$(mktemp -d)`.

## Automated

1. `scripts/build.sh` — must pass; the smoke run includes:
   - **MenuMirror**: the mirrored menu equals its `NativeMenu` source (items, order, separators,
     submenus, headers, gestures, check marks, enabled, tooltips) and reflects changes made
     between two openings.
   - **Palette registry drift**: `view.menuBar` is in the catalogue, bound, and named in the menu.

## Windows / Linux (manual or harness)

1. Start with a fresh data dir → no menu row; ☰ leads the transport bar (US1-1).
2. Click ☰ → File, Edit, View, Transport, Help; walk each submenu against the contract (US1-2/3).
3. Toggle Edit ▸ Grid ▸ Snap via ☰; reopen → check mark follows (US1-5).
4. Open a project, then ☰ → File → Open recent lists it (US1-6).
5. Ctrl+S / Ctrl+Z work with the row hidden (US1-7).
6. Narrow the window to its minimum → ☰ still there (US1-8).
7. ☰ → View → Show menu bar → row appears, ☰ disappears (US2-1, US2-4); Settings → Appearance
   shows "Shown"; switch to "Hidden" → row goes, ☰ returns, View item unchecked (US2-2).
8. Restart with the row shown → shown from the first frame (US2-3).
9. Row hidden: F10 → menu opens with File highlighted; arrows/Enter work; Escape → closes, focus
   back (US3-1, US3-4). Tap Alt → opens (US3-2). Alt+drag a clip, Alt+wheel → no menu (US3-3).
10. Windows: caption reads "Nota — Untitled"; Save As "Demo" → "Nota — Demo" (US4).

## macOS

- Main window, system menu (View has no Show menu bar), Settings → Appearance (no Menu bar row),
  Settings → Shortcuts (no Alt / F10 row): identical to `develop` (US5, SC-006).
