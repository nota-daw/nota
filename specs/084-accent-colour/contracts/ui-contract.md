# UI contract: the Accent row and the colour picker

Visual reference: [`../mockup/accent.html`](../mockup/accent.html).

## Accent row (Settings → Appearance → UI)

- Position: directly under the colour theme picker and its caption; label **Accent**
  (`preferences.accent`), caption under it (`preferences.accent_caption`): "The colour of
  what is live or chosen — the playhead, the loop, selection, toggles that are on. Tuned to
  each theme so it stays readable."
- Content, left to right: 7 preset swatches · 6 px gap · **Custom** button · readout.
- Swatch: 22 × 22 hit area, 2 px ring slot (transparent; `Rule` on hover; `TextPrimary`
  when chosen), 2 px gap, 14 × 14 fill radius `Radius.Clip` (2) in the preset's accent *as
  worn in the active theme*. Tooltip: the preset name.
- Custom button: default button (raised, seam, bevel), content = 14 × 14 chip of the
  custom colour (or `SurfaceHover` with no custom colour yet), label "Custom"
  (`preferences.accent_custom`), `GlyphKind.Edit` 10 px. Chosen → the same `TextPrimary`
  ring. Click opens the picker.
- Readout: `TextMuted`, 12 px — the preset name, or "Custom · #RRGGBB" (hex in Geist Mono).
- Theme cards above preview each theme with the chosen accent.
- A pick applies at once (re-tint + repaint every window) and saves.

## Colour picker (Flyout below the Custom button)

- Body 240 wide, padding 10, `SurfaceRaised`, seam ring (popups carry no blur).
- Rows, gap 8:
  1. Saturation/value area 216 × 132, radius 3, `Rule` frame; handle 12 px circle,
     `HandleSel` 2 px ring + `Seam` 1 px outer ring.
  2. Hue strip 216 × 10, radius 3, `Rule` frame; handle 4 × 14 `HandleSel` with `Seam` edge.
  3. Before/after chip (two 18 × 22 halves, `Rule` frame) · hex field (`TextBox.field`, Geist
     Mono 12, accepts `#RGB`, `#RRGGBB`, with or without `#`).
  4. "In themes" (`preferences.accent_in_themes`, `CellLabel`) and four chips (theme names
     abbreviated in a tooltip) showing the derived `Accent` per theme; a 4 px `TextPrimary`
     dot marks a theme where lightness was adjusted; a refused theme shows `BgSunken`.
  5. Note (11 px, `TextMuted`; `Danger` when refused), two lines reserved so the flyout never
     changes height:
     - `preferences.accent_note_as_is` — "Worn as is in every theme."
     - `preferences.accent_note_adjusted` — "Lightness adjusted in {0} to stay readable."
       (`{0}` = the theme names, or `preferences.accent_note_adjusted_all` when all four)
     - `preferences.accent_note_grey` — "Pick a colour, not a grey — the accent has to read
       as a colour."
     - `preferences.accent_note_clash` — "Too close to {0} in {1} — pick another colour. The
       accent stays as it was." (`{0}`: `preferences.accent_clash_record` "the record red",
       `…_mute` "mute", `…_solo` "solo", `…_meter` "the meter green")
     - `preferences.accent_note_unreadable` — "No lightness keeps this readable in {0} — pick
       another colour. The accent stays as it was."
- Interaction: drag in the area / strip (left button) or type a valid hex → applied live
  (not saved) when accepted; refused → shown in the chip and the note only. Opening
  preselects the custom accent, or the last custom colour while a preset is worn, or — when
  none was ever chosen — the worn preset's colour.
  Closing (outside click, Return in the hex field) saves the last accepted colour as Custom;
  Esc restores the accent worn before opening and closes.

## Preset names (localization keys)

`accent.orange` Orange · `accent.lime` Lime · `accent.aqua` Aqua · `accent.sky` Sky ·
`accent.violet` Violet · `accent.magenta` Magenta · `accent.pink` Pink.

## Nota Remote

No UI. The theme message carries the accented theme(s); the phone repaints on change.
