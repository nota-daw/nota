# Research: Arrangement group trays

**Feature**: [spec.md](spec.md) · **Plan**: [plan.md](plan.md) · **Issue**: nota-daw/nota#584

What the code does today, and the decisions this feature takes. Line numbers are as of
`develop` @ eec6a4ff and only a guide.

## Today

- **Header column.** `ArrangementView.RebuildHeaders` (`ArrangementView.cs` ~2265) places
  one card per visible row on the `_headers` canvas at `RowTop(i)`, `HeaderW` wide and
  `RowH(i)` tall, so cards line up with the lanes by construction. `_tracks` is in
  hierarchy order with `Depth` set and folded descendants dropped
  (`ArrangementView.Groups.cs` `ApplyHierarchy`).
- **Slim group row.** An unselected group is `BuildGroupBar` (26 px, `GroupRowHeight`):
  chevron, name, M / S / (Cue) / Link, the "Group" tag, and a `MeterBar(horizontal)`
  3 px tall with `VerticalAlignment.Bottom` in a `Panel` over the whole row — that is why
  it runs under the name and the chips. A selected group is a full `BuildTrackCard`.
- **Nesting.** `HeaderBars` draws one 4 px bar per enclosing group at `k * GroupIndent`
  (14), then the row's own bar (3 px, inset 6 top/bottom for tracks; 4 px full height for a
  group's own row). Content starts at `x + NameGap` (12).
- **Tints.** Group card: `TrackTint(color, 0.16)`; a member card: `TrackTint(groupColor,
  0.06)` of its direct group. Group lane: `GroupLaneBg` plus `TrackTint(color, 0.16)`
  (`ArrangementView.LaneControl.cs` ~1158).
- **Track meter overflow — root cause.** `MeterBar`'s vertical constructor sets
  `MinHeight = 40` (`Controls/MeterBar.cs`). The header meter has `Margin(6, 6, 4, 6)` and
  `VerticalAlignment.Stretch`; on a Compact row (30 px) only 18 px are left, on the
  smallest Standard row (50 px) 38 px. Avalonia honours `MinHeight`, so the meter is laid
  out 40 px tall from its top margin and runs into (and is clipped at) the card's bottom
  edge — the "pokes out" in the issue's screenshot.
- **Folded count.** `ApplyHierarchy` already walks the full child map (folded groups
  included) and sets `GroupSlotCount` = the number of non-group descendant tracks. That is
  exactly the clarified count. (The Mixer's `HiddenIds` includes nested groups; not reused.)
- **Look.** `NotaThemeService.Look` (`Nota.App/Theme`), enum `Look` in
  `Nota.Application/Looks.cs`. A look change calls `RepaintEverything` (invalidate only)
  and raises `NotaThemeService.Changed`; it does not rebuild the header cards. The clip
  style follows the look through `ArrangementView.ClipStyle`, set by `MainWindow` from
  settings.
- **0.59.2 reference** (`git show v0.59.2:src/managed/Nota.App/ArrangementView.cs`):
  `SpineGrid(depth, spine, body)` = columns `[depth*10 px][3 px spine][*]`;
  `BuildGroupBar` on `Brush.SurfaceRaised`, name 11 / SemiBold, chips 3 apart, a
  horizontal meter 4 tall docked **bottom** of a `DockPanel` (margin 0,0,0,3) so it takes
  its own line, body margin 8,0,8,0; track cards on `Brush.SurfaceCard`, vertical meter
  8 wide, margin `(0,4,3,4)` compact / `(0,6,3,6)` otherwise; a selected row's spine in
  `NotaPalette.Accent`. No group lane tint.
- **Tests.** `tests/Nota.SmokeTest` references `Nota.Presentation`, not `Nota.App`;
  pure layout code there is testable (precedent: `PianoRowMap` + `FoldTests`).
  `DesignTokenCheck*` scans `ArrangementView*.cs` source for raw radii / numbers with
  allow-lists.

## Decisions

### R1 — Tray geometry (new look)

- **Decision**: one fixed step per nesting level, **6 px = a 3 px tray edge + a 3 px gap**.
  For a row whose tray chain (enclosing groups, outer → inner; a group's own header row
  includes itself) has length *k*, tray *i* has its edge at `x = i * 6`; the row's card (a
  member) or its header content (a group) starts at `x = k * 6`. Every tray and every card
  share one right edge; the trays' right corners are rounded where a tray starts (top
  right, on its header row) and where it closes (bottom right). The member card is inset
  `TrayInsetR` (4 px) from the right so the tray shows around it; top-level tracks are not
  inset, but every row lays out its controls against the same right content edge (R3).
- **Rationale**: SC-001 asks a depth-2 track's name to start ≤ 12 px further in than a
  top-level track's: two levels × 6 = 12 ✓. The issue's "about 4 px" inset meant the visible
  gap; 3 px plus the 3 px edge keeps the step at 6. A shared right edge is what keeps the
  controls' columns fixed at every depth (FR-007).
- **Alternatives**: 4 px gap + 3 px edge (7 px/level → 14 px at depth 2, fails SC-001);
  nested `Border`s per group (cannot work: header rows are separate canvas children per
  row, aligned to lanes); insetting nested trays on the right too (moves the controls per
  depth, fails FR-007).

### R2 — Tray painting

- **Decision**: a pure layout in `Nota.Presentation` (`GroupTrayLayout`) turns the visible
  rows (id, parent id, is-group, folded, colour) into per-row **slices**: the chain of
  trays the row sits in, which tray (if any) the row heads, how many trays close on the
  row (innermost first), and the content left. `ArrangementView` paints each card's
  background from its slice (a small custom-drawn layer under the existing card content),
  so cards stay per-row and aligned to their lanes.
- **Rationale**: rows are already per-row canvas children and rebuilt per change; a slice
  per row needs no cross-row visual tree. The layout is testable headlessly.
- **Alternatives**: one big backdrop control behind all headers (must track every row's
  `RowTop`, fold and live row-height drags — more coupling for no gain).

### R3 — Wash, closings and row heights

- **Decision**: wash = 16 % of the group colour on its header row (today's group tint), 8 %
  on the rest of its tray (`TrackTint(color, 0.08)`); a member card sits on
  `Brush.Panel` (no 6 % tint). The innermost tray closing on a row takes a **4 px** strip at the bottom of that row,
  each further tray closing there a **2 px** one (outer below inner), carved out of the row's
  height (found in implementation: four 4 px strips left a Compact row 14 px, short of its
  20 px name row); the card / content is laid out in the remaining height. A folded group's row
  closes its own tray. Controls and the meter lay out against one right content edge for
  every row: `HeaderW − TrayInsetR − HeaderRightPad`-style constant, the same for a
  top-level track as for a member.
- **Rationale**: FR-009 (no row grows), FR-004 (no stacked tints on a card). Two closings
  on a Compact row leave 22 px — the 20 px name row fits.
- **Alternatives**: a separate spacer row after each group (breaks the row ↔ lane mapping
  and every `RowTop` caller).

### R4 — Group row meter (new look)

- **Decision**: the slim group row gets the same vertical `MeterBar` as track cards,
  docked right in the same column; the horizontal rail goes. All header meters (both
  looks) get `MinHeight = 0` and margins that leave equal inner padding top and bottom.
- **Rationale**: FR-005, FR-008; the `MinHeight` is the overflow's root cause.
- **Alternatives**: shrinking the margins only (still overflows on Compact).

### R5 — Classic look staircase

- **Decision**: a second header-geometry path that reproduces 0.59.2's `SpineGrid` and
  slim `BuildGroupBar` (surfaces `SurfaceRaised` / `SurfaceCard`, 11 px name, horizontal
  meter docked below the title, accent spine when selected), with today's controls inside
  (Cue key when shown, Link) and the folded count. No tray, no group lane wash. Tokens, not
  0.59.2's raw numbers, wherever a token exists (the classic look already maps radii and
  colours).
- **Rationale**: FR-010–FR-012, SC-004.
- **Alternatives**: restoring 0.59.2's whole header builder (would drop features added
  since: sends, Cue, Link marks, takes toggle, player tag).

### R6 — Live look switch

- **Decision**: `ArrangementView` gets a `HeaderLook` property (like `ClipStyle`). The main
  window sets it at start-up and from `NotaThemeService.Changed` **only when the look
  differs**; the setter rebuilds the headers and invalidates the lanes (group lane wash).
- **Rationale**: FR-013 / SC-005; a theme or accent change must not rebuild headers.
- **Alternatives**: reading `NotaThemeService.Look` inside the builders without a
  rebuild trigger (headers would keep the old look until the next refresh).

### R7 — Folded count text

- **Decision**: after the name, " · N" in the type-tag style (Label 11, ink 3); N =
  `GroupSlotCount`. Shown only when folded, in both looks. Localized as a number only (no
  word), so no new string.
- **Rationale**: FR-006 + clarification; no new localization key.

### R8 — Out of scope, confirmed

Mixer group bands, Nota Remote, sticky headers, the group lane's mini-clip preview, the
takes rows and the footer rows (returns, master) are untouched.
