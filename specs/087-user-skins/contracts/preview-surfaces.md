# Contract: Preview surfaces

Names are stable: clients may request them one by one. They are rendered at scale 1×,
at the default text size, with the requested accent option. Every surface is drawn in the
previewed theme inside one UI-thread job, and the screen never shows it (research R6).

Sources (a `settings` surface is built for the picture like a sample one, but shows the
user's current choices, so it is not byte-identical across changes to them):
- **Sample** surfaces are real views over the fixed sample scene (research R6), built for
  the picture and thrown away. Each is hosted in its own window, shown far off screen and
  fully transparent, so the layout manager, Loaded handlers and virtualising lists run; the
  skin is worn only in the job that draws them. They are deterministic: the same theme gives
  the same bytes.
- **Live** surfaces are the main window's own parts, so they show the user's open project
  and library. They are bound to the app's one engine and its view models, which a second
  copy cannot share. They are drawn at their size on screen, re-coerced for the skin during
  the job and given the live skin back right after. Their content follows the project, so
  SC-004 (identical images) covers the sample surfaces only.

| Name | Source | Size (px) | Description |
|---|---|---|---|
| `overview` | live | the window's | The main window: top bar, browser, arrangement and the detail panel |
| `top-bar` | live | the row's | Transport keys, position, tempo, view switches, CPU |
| `arrangement` | sample | 1200 × 520 | Track headers, MIDI and audio clips in the track colours, an automation lane (`ArrangementView`) |
| `mixer` | sample | 1200 × 520 | Channel strips with sends, a return and the master (`MixerView`) |
| `device-rack` | sample | 1200 × 300 | An instrument and two audio effects as device cards (`DeviceChainView`) |
| `piano-roll` | sample | 1200 × 480 | Chords on the keyboard grid with the velocity lane (`PianoRollView`) |
| `browser` | live | the panel's | Categories, the list and the search field |
| `preferences` | settings | 900 × 640 | Preferences → Appearance (`PreferencesWindow`): the theme cards, including the user's own, and the user's current accent, scale and text-size choices |
| `menu` | sample | 320 × content | A track context menu with the first row hovered (`ArrangementView.BuildTrackMenu`) |
| `dialog` | sample | 480 × content | A confirmation dialog with primary and secondary buttons (`ConfirmWindow`, never shown) |

A surface that cannot be drawn (no main window yet, a view that throws) is left out of the
result; the others still come back. Adding a surface later is additive. Removing or
renaming one is a breaking change for MCP clients and needs a CHANGELOG note.
