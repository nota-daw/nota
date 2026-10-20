# Data Model: App-wide Interface Scale

## InterfaceScale (user preference)

| Field | Type | Where | Default | Rule |
|---|---|---|---|---|
| `InterfaceScale` | int, percent | `Nota.Application.Settings` → `settings.json` | `100` | One of `75, 100, 125, 150, 200`; anything else reads as `100` |

- App-wide, not per-project and not per-window. Never written to project files.
- Read once at startup (before the main window is created) and again on every
  Preferences change. Writing goes through the existing settings save path.
- Missing key (settings from older versions) → default 100 (no migration needed).

## Runtime state (app layer, not persisted)

**Scale service** (`Nota.App`, static like `NotaThemeService`):

- `Factor` — `InterfaceScale / 100.0`.
- `Set(int percent)` — normalises the value, then updates every registered window's
  transform, title-bar hint and scaled minimum size.
- `Register(Window)` — called by `NotaWindow` and `MainWindow`. The service holds a
  weak list and drops a window when it closes.

State transitions: `100 → any preset → any preset`, applied live. No intermediate
or animated states.
