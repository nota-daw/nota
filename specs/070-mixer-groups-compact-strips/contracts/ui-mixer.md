# UI Contract: Mixer Groups and Compact Strips

What the user can see and do, control by control. Agreed against the mockup in
[`../mockups/`](../mockups/) (2026-11-02; `shoot.sh` re-renders it).

## Strip order

Arrangement rows → Mixer strips, left to right: each group strip followed by its members
(DFS, nested groups the same way), then return strips, then Master. Same in the docked
panel, the pop-out window and the Stereo Map's strip column.

## Group bands (both layouts)

| Element | Look | Behaviour |
|---|---|---|
| Bar row | 14 px per nesting level above the strips, outermost on top; group colour at ≈ 32 % over the ground, name 9 px semibold, ellipsised; folded: name + mono `+N` | spans the group strip through its last visible member; tooltip = full name; no click action |
| Tint | group colour ≈ 6–8 % behind the same strips | strips keep their own 3 px colour bar on top |
| Ungrouped / return / master | no bar, no tint | — |

## Fold control (group strips only)

| Element | Look | Behaviour |
|---|---|---|
| Triangle in the header, left of the name | the Arrangement's disclosure glyph: ▾ unfolded, ▸ folded | click toggles fold for the group in the Mixer **and** the Arrangement; one click, no undo step (view state) |
| `+N` in the group's bar (folded only) | mono, secondary ink, after the name | N = hidden strips, nested groups included; `+0` for an empty group |
| Hidden-state dots (folded only) | 6 px dots at the right of the group header: solo (`Solo`), arm (`Record`), clip (`Danger`) — shown only when true | solo / arm: any hidden member; clip: any hidden member peaked ≥ 0 dBFS since the unfold; clicking the clip dot clears it |
| Accessible name | "Fold group {name}" / "Unfold group {name}" | reachable with Tab, toggled with Space / Enter |

Folding never changes mute, solo, arm, routing, automation or the audio.

## Selection follow

| Trigger | Result |
|---|---|
| Track selected in the Arrangement or in Modular (`TrackSelected` / `TrackActivated`) | its strip gets a 1 px accent outline and scrolls into view |
| Selected track hidden by folding | the outermost folded ancestor's strip gets the outline; nothing unfolds |
| Selection cleared | no outline |

## Footer

`Show  [Sends] [I/O] [Compact]  |  Pan law …` — `Compact` is a two-state key painted like
`Sends` / `I/O` (accent when on). Remembered per user (`MixerCompact`), default off. Shared
by the docked panel and the pop-out window.

## Compact strip (72 px)

| Section | Control | Behaviour = regular strip's |
|---|---|---|
| Header | name 9 px, ellipsised, tooltip full name; group: triangle (+ hidden-state dots when folded) | — |
| I/O | one line, tooltip with full routing; instrument: MIDI-in combo | same routing actions |
| Sends | A and B as thin full-width `MiniFader` bars with their letter; returns and master keep an empty slot so every meter starts at the same height | drag, typed value (`Send A`), automation, sync |
| Pan | the existing `PanBar` (as in Arrangement track headers): full-width, fills from the centre; BAL tag beside on groups; master keeps an empty slot | drag (relative), fine modifier, double-click → centre, typed value, MIDI learn `TrackPan`, automation write, one undo step per gesture |
| Level | `MeterFader`: 14 px dB scale · two 18 px channels 3 px apart · 11 px gutter with a 9 × 10 px left-pointing triangle at the volume's dB on the meter scale, and a hairline across the channels at the same height; `VizInk` at rest, accent while hovered or dragged | drag anywhere on it (relative, fine modifier), typed value, double-click → 0 dB, MIDI learn `TrackVolume`, automation write, one undo step per gesture; click without drag clears the peak hold |
| dB | the same drag / type number | same |
| Buttons | M, S, Arm stacked, full width, 18 px; Arm hidden on returns | same toggles, colours, MIDI learn `TrackMute` / `TrackSolo`, ⌘/Ctrl-click additive arm |

Height fitting order is the regular one: I/O hides first, then sends, then the meter
shortens to its minimum; header, pan, meter, dB and buttons always stay.

## Strings (new, localised in `en.json` / `fr.json`)

`Compact`, `Fold group {0}`, `Unfold group {0}`, `{0} hidden strips`, `A hidden track is soloed`,
`A hidden track is armed`, `A hidden track clipped · click to clear`,
`Show compact strips: more tracks on screen`.
