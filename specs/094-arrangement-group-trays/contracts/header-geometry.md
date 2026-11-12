# Contract: Arrangement header geometry for groups

The visual contract the implementation and the screenshots are checked against. Colours
are tokens / track tints; radii are `NotaRadius` steps (the look and skin pick the values).

## New look — trays

```text
x: 0   6   12
   ▐ ▾ Group                         M S ∞    ▮   ← tray head: 16 % wash, top-right rounded
   ▐  ▐ ▾ DRUMS                      M S ∞    ▮   ← inner head: 16 % of DRUMS over Group's 8 %
   ▐  ▐  ┃Nota Drum Rack  MIDI      M S ●    ▮   ← card on Brush.Panel, own 3 px bar only
   ▐  ▐  ┃Audio 4         Audio     M S ● ◠  ▮
   ▐  ▐▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁╯   ← DRUMS closing strip (4 px, from the row)
   ▐  ▐ › Instruments · 4            M S ∞    ▮   ← folded: count, closes its own tray
   ▐▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁▁╯   ← Group closing strip
     ┃Nota Consort     MIDI          M S ●    ▮   ← top level: full-width card, no tray
```

| Item | Value |
|---|---|
| Step per level | 6 px (3 px edge + 3 px gap) |
| Tray edge | 3 px, group colour (full) |
| Wash, tray head row | `TrackTint(color, 0.16)` |
| Wash, rest of tray | `TrackTint(color, 0.08)` |
| Member card | `Brush.Panel` (selected: `TrackHeaderSelected`), inset `TrayInsetR` = 4 px right |
| Own colour bar | 3 px, inset 6 top / bottom (as today) |
| Closing strip | 4 px for the innermost tray closing on a row, 2 px for each further one; bottom of the last row, inner above outer |
| Tray corners | top-right on the head row, bottom-right on the closing strip; `NotaRadius` field step |
| Slim group row | 26 px (unchanged): chevron, name, ` · N` when folded, M / S / (Cue) / Link, vertical meter |
| "Group" tag | not shown in the new look (the tray carries it) |
| Meter | vertical, same column and margins as a track's; inside the card above any closing strip |
| Right content edge | the same constant for every row (top-level or member) |
| Group lane | `GroupLaneBg` + `TrackTint(color, 0.16)` (unchanged); member lanes unwashed |

## Classic look — 0.59.2 staircase

| Item | Value |
|---|---|
| Spine | 3 px, own colour, at `depth * 10`; full row height; accent when selected |
| Track card | `Brush.SurfaceCard` |
| Group row | `Brush.SurfaceRaised` |
| Tints | none on headers; none on the group lane (keeps `GroupLaneBg`) |
| Slim group row | chevron, name 11 / SemiBold (+ ` · N` folded), M / S / (Cue) / Link, "Group" tag; horizontal meter on its own line under the title (dock bottom, 3 px from the edge) |
| Track meter | vertical, right edge, inside the card on every row height |

## Both looks

- No header row changes height; header rows and lanes stay aligned.
- No meter pixel outside its card; no meter under a name or chip.
- The name trims (ellipsis) before any control moves or clips.
