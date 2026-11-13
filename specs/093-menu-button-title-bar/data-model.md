# Data Model: Menu button and a hideable menu bar

## Settings (user settings file)

| Field | Type | Default | Notes |
|---|---|---|---|
| `ShowMenuBar` | bool | `false` | Windows / Linux: the in-window menu row is shown. Ignored on macOS. A settings file without the key reads `false` (the new default for upgraders too). Written by Settings → Appearance and View ▸ Show menu bar; every write fires `Settings.Changed`. |

## Command catalogue

| Id | Title | Category | Gesture | View |
|---|---|---|---|---|
| `view.menuBar` | Show menu bar | View | — | — |

Bound in `MainWindow.Commands.cs` to `ToggleMenuBar()` (flip `ShowMenuBar`, save, status line).

## Menu mirror (in memory, per opening)

A tree built from the `NativeMenu` each time the Menu key's menu opens; nothing is kept.

| Source (`NativeMenu` side) | Mirror (`ContextMenu` side) |
|---|---|
| `NativeMenuItem.Header` | `MenuItem.Header` (same text; `_` access-key escaping kept) |
| `NativeMenuItem.Gesture` | `MenuItem.InputGesture` (shown, not bound — the window dispatches shortcuts, as today) |
| `NativeMenuItem.ToggleType` / `IsChecked` | `MenuItem.ToggleType` / `IsChecked` |
| `NativeMenuItem.IsEnabled` | `MenuItem.IsEnabled` |
| `NativeMenuItem.ToolTip` | `ToolTip.Tip` |
| `NativeMenuItem.Menu` | `MenuItem.Items` (recursive) |
| `NativeMenuItemSeparator` | `Separator` |
| click | source item's `RaiseClicked()` |

## State transitions (Windows / Linux)

```
ShowMenuBar = false  →  menu row detached + hidden, Menu key shown, Alt/F10 → Menu key's menu
ShowMenuBar = true   →  menu row attached + shown, Menu key hidden, Alt/F10 → stock menu-row behaviour
```
Both directions apply immediately on `Settings.Changed` and at start-up.
