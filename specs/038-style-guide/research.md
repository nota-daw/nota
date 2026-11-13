# Research: Style guide — the new Nota look

## R1 — Fixed-width digits without Geist Mono

- **Finding**: Geist's default digits are proportional (advance widths 384–663 units), but the
  font carries the OpenType `tnum` feature (also `pnum`, `case`, `ss01`–`ss11`). "Geist" is a
  Reserved Font Name under its OFL licence (`LICENSES/third-party.md` note 5), so we can't
  ship a modified copy with tabular digits baked in under the same name.
- **Decision**: keep the unmodified Geist files and turn `tnum` on at render time. Text
  controls get it from one inherited style in `NotaTheme.axaml`; custom-drawn text goes
  through one helper in `NotaText` that builds the text with `tnum` set, so the 98
  `new FormattedText` sites in 36 files stop naming a typeface themselves.
- **Alternatives rejected**: a renamed, re-cut "Nota Sans" with tabular digits (needs a
  font-build step and a new licence row for a derivative); keeping Geist Mono for numbers
  (the guide drops it).

## R2 — Corner scale

- Today `NotaRadius` has ten names on a 0–4 scale. Use counts: Docked 6, Bar 25, Clip 46,
  Badge 179, Control 88, Tile 24, Panel 24, Body 11, Pill 20, Modal 3.
- **Decision**: the guide's scale is 9 / 7 / 6 / 5 / 4 / 3 / 2 (plus 0 for docked panels).
  The names become what they're for:

  | Name | Value | Used for |
  |---|---|---|
  | Docked | 0 | docked panels |
  | Card | 9 | device card, toolbar view group, modal windows |
  | Group | 7 | icon button, chip, segment group, device section, search, menus |
  | Field | 6 | field, dropdown, clip, toggle pill, row highlight |
  | Cell | 5 | segment cell, track button, EQ band cell, tooltip, inner key |
  | ValueBar | 4 | value bar, fader cap, overview strip |
  | Switch | 3 | switch track |
  | Fine | 2 | switch knob, notes, plot bars |

  Old names are migrated site by site (each site picks the name for what it draws), not
  aliased, so the guard test can keep listing exactly one scale. `RadiusAllowList.txt` is
  re-ratcheted to the new values.

## R3 — Theme tokens

- Theme JSON files are flat `"Name": "#RRGGBB"` maps read by `NotaColourTheme`; `NotaPalette`
  exposes each as a static brush and `DesignTokenCheck.Themes` checks every theme has every
  key. Alpha colours already exist in the files (`#AARRGGBB`).
- **Decision**: add the 15 brushes from the guide to all four themes (Ember values from the
  guide's table; the see-through wells copied from Studio), change Studio Dark Panel,
  SurfaceRaised, TextDisabled, VizInk and Studio Light Mute. The guard test's key list grows
  with them.

## R4 — Track colour order without recolouring saved projects

- Projects store the slot index (`Brush.Track1..9`); `NotaPalette` notes that saved
  projects are not migrated. New tracks use `ArrangementView.AutoTrackColorIndex(nth)`,
  which walks the slots in table order.
- **Decision**: leave the slot table alone and give `AutoTrackColorIndex` an order table
  (green 1, blue 4, amber 5, violet 6, rose 7, teal 2, slate 9, olive 8, sky 3). Stored
  indices keep meaning the same hue (SC-005).

## R5 — EQ-8 fold state

- Prism keeps its picked band and tabs in a static dictionary keyed by (track, device index)
  for the session; nothing UI-only is saved in the project.
- **Decision**: the same pattern for the EQ-8 fold, so no format change. The fold redraws the
  card at 776 wide; the device panel already lays out cards by their reported width.

## R6 — Status lines

- Most cards push a summary into `DeviceCardContext.Status`, drawn under the card. The guide
  removes it everywhere and says, per device, where any control that lived there goes.
- **Decision**: the card shell stops drawing the status line; each body drops its `Status`
  calls, and the few controls that lived there (EQ-8 Out readout, Dynamic EQ-8 Dynamic switch
  and Out, Pentad Voice menu, Consort patch-bay count and Open) move into the card per the
  guide.

## R7 — Build in the cloud container

- .NET 10 SDK comes from the Ubuntu 24.04 archive (`dotnet-sdk-10.0`); the dotnet.microsoft
  download host is blocked. The engine's hiir dependency host is blocked too, so the engine
  is configured with `-DCPM_hiir_SOURCE=<old vendored copy>` (from `64250a5f^`). GitHub
  tarballs and SourceForge download. Smoke tests run with `CI=true`.
