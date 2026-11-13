# Data model: Modular tab in the browser, arrangement track headers in Modular

No project-file or engine data changes. UI-side shapes only.

## ModularNode (existing, `Nota.Application.Palette.ModularCatalog`)

| Field | Meaning |
|---|---|
| `Name` | "LFO", "Envelope Follower", … |
| `Kind` | engine modulator kind (0 LFO … 6 Scope) |
| `Sub` | category caption (modulator, converter, control, CV utility, monitor) |
| `Descriptor.Aliases` | search words ("wobble", "sidechain", …) |
| `Descriptor.Description` | one-line description |

The catalogue stays the single list for the palette and the browser tab (FR-002, FR-003).

## BrowserItem (extended, `Nota.Presentation`)

- New kind `BrowserItemKind.ModularNode`.
- A Modular row: `Name = node.Name`, `BuiltinKind = node.Kind`, `Sub = node.Sub`,
  `Tip = description`, `Keywords = aliases joined by spaces` (new init-only property,
  default "").
- Rules: a ModularNode row is never auditioned, favourited, tagged or revealed; it is
  draggable and activatable.

## ModularBrowserItems (new, pure)

- `Build(IReadOnlyList<ModularNode>) → IReadOnlyList<BrowserItem>` — one row per node, catalogue
  order.
- `Matches(BrowserItem, string query) → bool` — empty query matches; otherwise every
  whitespace-separated word must occur (case-insensitive) in name, category or keywords.

## BrowserTabMemory (new, pure)

State: `InModular` (bool), `OutsideTab` (int, initially the browser's start tab),
`ModularTab` (int?, null until a tab is picked in Modular).

| Event | Effect | Tab to show |
|---|---|---|
| `Picked(tab)` | stores `tab` for the current context | — |
| `Enter()` (view → Modular) | `InModular = true` | `ModularTab ?? modularTabIndex` |
| `Leave()` (view → Arrangement / Session) | `InModular = false` | `OutsideTab` |

The Modular tab index is never stored as `OutsideTab` (it isn't on the rail there).

## ArrangementView headers-only mode (new state)

| State | Meaning |
|---|---|
| `HeadersOnly` | true while Modular is the view: the timeline bands are hidden, the view is as wide as its header column, docked on the header side |
| `ColumnHint` | brass divider while a CV cable is dragged in Modular |
| `ModularTrackId` | a callback asked when a menu opens: the track Modular shows (for disabling Show in Modular on it); a callback, not a copy, so it can't go stale when a header click switches the graph |

`ArrangementView.HeaderColumnWidth` + `HeaderColumnWidthChanged` let the host inset the canvas.
