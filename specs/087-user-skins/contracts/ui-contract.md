# Contract: Preferences → Appearance, user themes

Follows `DESIGN.md` and the `nota-design` skill; all strings via `Loc.T`, EN + RU.

## U1 — Theme cards

- User themes appear in the Dark or Light card list by kind, **after** the built-ins, sorted
  by name (culture-aware, case-insensitive).
- A user theme card carries a small "User" tag (caption style, secondary text) under its name.
- A card whose theme has warnings shows a warning glyph and the count; tooltip lists up to 5
  ("Text on Panel: 3.1 of 4.5", "Font 'X' is not installed — using Geist", "+N more").
- The card preview swatch shows the theme's colours **and** its radius (the swatch's own
  corners follow the theme's Card radius) and renders its name in the theme's typeface.
- Selecting a user card behaves exactly as selecting a built-in (single mode or a
  follow-system slot).

## U2 — Card context menu

- Every card (built-in or user): "Duplicate" → writes `<new-id>.json` (id = slug of
  "<name> copy", numbered on clash), reloads, selects the copy.
- User cards also: "Show in folder" (reveals the file).

## U3 — Themes folder row (below the cards)

- "Open themes folder" — creates the folder if missing, opens it in the system file manager.
- "Reload" — re-reads the folder; the page and the worn theme update.
- The page also reloads each time it is opened.

## U4 — Problems list

- Shown only when some file failed to load: a heading "Some theme files could not be loaded"
  and one row per file: `file name — reason` (reason as produced by validation; the first
  three problems, then "+N more").
- Not shown when there are no problems. Never a modal dialog.

## U5 — Fallback

- If the stored theme is missing or invalid after a reload, the page shows the fallback
  selected and a one-line note "'<id>' could not be loaded — showing Studio Dark" under the
  mode row; the stored choice is not overwritten until the user picks.
