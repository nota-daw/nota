# Contract: UI — Mixer window views and the Stereo Map

Source of truth: `nota-design/Nota Mixer v2.html` (title bar, map panel, footer). Ember tokens
(`DESIGN.md`, `nota-design` skill), both themes. Only the MVP parts below.

## Mixer window title bar (`MixerWindow.cs`)

36 px own title bar (macOS/Windows; Linux: a 32 px row at the top of the body):
"Mixer" caption left (as other windows), the shell segmented control **Strips | Stereo
Map** centred, nothing else. Selecting a segment sets `MixerView.View` and
`Settings.MixerView`.

## Layout in the Stereo Map view (`MixerView.cs`)

`Grid` columns `*, Auto`: the map panel (min 520 px) | the strips scroller (max 440 px,
horizontal scroll). Gap 8 px, outer padding 8 px. The footer (Show, PAN LAW, crossfader)
stays under both. In the Strips view the map panel is removed and the strips take the width.

## Map panel

| Part | Spec |
|---|---|
| Panel | `SurfaceCard`, 1 px `BorderDefault`, radius 6 |
| Header row (36 px, bottom hairline) | "STEREO MAP" — 10 px bold, letter-spacing 1.4, `TextPrimary` (MVP: no Y switch, filters or tools) |
| Plane well | `BgSunken`, 1 px `BorderDefault`, radius 4, inner padding 14 / 18 / 22 / 40 (top / right / bottom / left) |
| Vertical grid | at L100, L50, C, R50, R100; C line `BorderDefault`, others `GridBeat`; labels under the plane, Mono 8 px, `TextTertiary` |
| Horizontal grid | at the fader positions of +3.5 (top), 0, −6, −12, −24 dB; 0 dB line `BorderDefault`; labels left of the plane, right-aligned, Mono 8 px, `TextTertiary` ("+3.5", "0 dB", "−6", "−12", "−24") |
| Cloud | ellipse at the handle's Y, height 22 px; X from `balance − width` to `balance + width` on the pan scale (min width 1.6 % of the plane); fill = track colour at opacity `0.10 + 0.32 × clamp((level + 60) / 54, 0, 1)` (× 0.35 when dimmed by solo); 1 px outline in the track colour, `Danger` when correlation < −0.05; none when silent or muted |
| Handle | 16 px circle, track colour fill, 2 px ring `BgSunken` (`Danger` when correlation < −0.05); muted or solo-dimmed: fill `SurfaceHover`; cursor grab (master: pointer) |
| Selected | 22 px ring, 2 px `Accent`, around the handle; drawn on top |
| Label | 10 px medium, right of the handle (+13, −7), max 84 px, ellipsis; `TextSecondary`, selected `TextPrimary`, dimmed `TextDisabled`; hidden if it would overlap a placed label |
| Tooltip | 188 px, `TooltipBg`, 1 px `BorderStrong`, radius 4, padding 8 / 10; name row (8 px colour dot + 11 px semibold `TextPrimary`); rows PAN, VOLUME, BALANCE, WIDTH, CORRELATION, LEVEL — key 7 px bold `TextTertiary` 62 px wide, value Mono 9 px (correlation < −0.05 in `Danger`); notes separated by a hairline: "Image is off the pan" (title `Accent`), "Muted", "Silent" (title `TextSecondary`), body 9 px `TextTertiary`; placed right/below the handle, flipped left/up near the edges |
| Bottom row (32 px, top hairline) | "MASTER CORR" 8 px bold `TextTertiary`; "−1"; a 180 × 6 well with a centre tick and a bar from centre to the value (`Success` ≥ −0.05, `Danger` < −0.05); "+1"; the value Mono 10 px (`Danger` when < −0.05); right-aligned hint 10 px `TextTertiary`: "Drag: pan + volume · Shift locks axis · double-click centres pan · ⌥ double-click 0 dB" |

## Texts (exact)

- Tooltip notes: **Image is off the pan** — "The pan is at {pan}, but the signal measures
  {balance}. The source itself is unbalanced."; **Muted** — "No cloud while the track is
  muted."; **Silent** — "Below −60 dBFS, so the cloud is hidden."
- LEVEL reads "below −60 dBFS" when silent; pan reads "C" / "L40" / "R25"; volume "−3.0 dB",
  "−∞".

## Not in the MVP

The mock's Y switch (Volume / Frequency), the All/Tracks/Returns/Selected filter, Conflicts,
Hold, Mono, Export PNG, the REMOTE indicator, marquee / multi-select, the WRITE badge and
the phone panel.
