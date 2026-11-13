# UI Contract: Menu button and a hideable menu bar

Applies to Windows and Linux. On macOS none of this exists (FR-020).

## Menu key (transport bar)

- Position: first control of the transport bar, before the view switch.
- Look: one 28-px icon key (`seg view`) in its own well; glyph ☰ (`GlyphKind.Menu`).
- Tooltip and accessible name: "Menu" (`main.menu`).
- Visible: when `ShowMenuBar` is off. Never shed on a narrow window.
- Click / Alt tap / F10: opens a menu below the key with File, Edit, View, Transport, Help as
  submenus — the menu row's contents, current at the moment of opening.

## View ▸ Show menu bar

- Check item, command `view.menuBar`, checked when the row is shown.
- Also in the command palette ("Show menu bar") on Windows / Linux.

## Settings → Appearance ▸ Menu bar

- Segmented picker "Hidden | Shown" (`preferences.menu_bar`, `preferences.menu_bar_hidden`,
  `preferences.menu_bar_shown`) with a caption (`preferences.menu_bar_caption`).
- Follows changes made from the View menu while open.

## Keyboard (row hidden)

| Input | Result |
|---|---|
| F10 | open the Menu key's menu, first entry highlighted |
| Alt pressed and released alone | same |
| Alt + any key / click / wheel / drag | nothing menu-related (existing behaviour) |
| Escape / click outside | close, focus back where it was |
| Menu shortcuts (Ctrl+S…) | unchanged |

With the row shown: stock behaviour of the menu row.

## Title bar (Windows)

Caption = OS window title = `Nota — <project name>` (`main.files.nota`).
