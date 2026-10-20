# UI & Settings Contract: Interface Scale

## Preferences → Appearance → UI

The page's subtitle in the Settings sidebar becomes "Theme, scale, waveforms and AI
control", so the new setting is findable from the navigation.

The Settings window gets a design minimum of 800 × 480 (multiplied by the scale like every
window's minimum), so switching to a larger scale grows it instead of clipping the page.

The row order in the **UI** section becomes:

1. Colour theme *(unchanged)*
2. Waveform *(unchanged)*
3. **Interface scale**: a segmented picker in the same style as *Waveform* (DESIGN.md
   Studio; the selected segment wears the accent). Segments, in order:
   `75 %` · `100 %` · `125 %` · `150 %` · `200 %`.
   - Caption below (muted, like the others): "Makes the whole interface larger or
     smaller. Timeline, piano roll and audio editor zoom work on top of it."
   - Picking a segment applies at once (FR-002) and saves the setting. No Apply
     button, no restart prompt.
   - The Preferences window itself rescales live as well, so the picker stays under
     the pointer's segment as far as layout allows.

Labels are sentence case and use a thin non-breaking space before `%` if the Studio
type rules ask for it (follow the existing `%` formatting in `NotaNum`).

## settings.json

```json
{ "InterfaceScale": 125 }
```

- Integer percent. Valid: `75 | 100 | 125 | 150 | 200`. Anything else, or a missing
  key, means `100`. The app never rewrites an invalid value on its own; it saves only
  when the user picks a preset.

## Not affected

The engine C ABI (`nota_engine.h`), project files, MCP tools, Nota Remote, exports and
third-party plugin editor windows.
