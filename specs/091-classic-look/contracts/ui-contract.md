# Contract: Preferences → Appearance, Look

Follows `DESIGN.md` and the `nota-design` skill; all strings via `Loc.T`, EN + RU.

## L1 — The Look row

- A row **Look** at the top of Preferences → Appearance, above the mode row (Dark / Light /
  Follow system) and the theme cards: a two-cell segment control **New | Classic**.
- A caption row under it, like the other Appearance rows: "Classic brings back the colours,
  corners and monospaced values of Nota 0.59, over any theme." (RU via the translation sync).
- Picking a cell applies at once (no Apply button), stores `Settings.Look`, and repaints
  every window, this one included.
- Keyboard: the segment control is reachable with Tab; arrows move between the cells.

## L2 — Theme cards under Classic

- Built-in theme cards show their classic colours and the classic corners while Look is
  Classic; user theme cards show their own colours with the radii they would wear.
- "Duplicate" on a built-in card copies the colours of the look being worn (FR-013).

## L3 — Skins

- A skin whose `font.values` names a family that is not installed carries a warning like a
  missing typeface ("values font 'X' is not installed — the look's own is used instead"),
  shown in the card's warning tooltip; values fall back to the look's own face (Geist under
  New, Geist Mono under Classic).

## L5 — Clips row

- A row **Clips** under Look: a two-cell segment control **Solid | Tinted** showing the style
  in effect (the look's default until the user picks), with a caption: "Solid draws clips as
  blocks of the track colour, as in Nota 0.59. With no pick, it follows the look."
- A pick applies at once to the open Arrangement and is stored.

## L4 — No other UI changes

- No restart prompt, no dialog. The Look row is not part of Nota Remote.
