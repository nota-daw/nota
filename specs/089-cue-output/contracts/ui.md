# Contract: UI, settings and MIDI Learn

Follows [`DESIGN.md`](../../../DESIGN.md) and the `nota-design` skill: sentence-case labels,
solid orange for live state (a lit Cue button), the grey shell.

## Settings → Outputs (new section, above Inputs)

- Rows: **Main** — pair picker; **Cue** — pair picker with "No cue output" first;
  **Click** — Main / Cue / Both (only Main while Cue is "No cue output");
  **Click level** — off, −18, −12, −9, −6, −3, 0, +3, +6 dB.
- Pair pickers list the device's outputs as "Out 1/2 · <name>" (pairs odd-aligned, a lone
  last channel as "Out N"). The Main pair is disabled in the Cue picker and vice versa
  (FR-004).
- Under ASIO the old **Output pair** row is replaced by **Main** (FR-005).
- Saved to `Settings.OutputSetups[key]` and pushed to the engine on every edit.
- A device with 2 outputs shows Main 1/2 (fixed), Cue "No cue output — this device has
  2 outputs".

## Mixer

- **Cue strip** next to Master (full and compact mixer): title "Cue", output label
  ("Out 3/4 · Phones"), level fader + meter, a line with the cued tracks' names (or
  "3 tracks"), and the click destination as a small picker. Hidden when no Cue output is
  assigned on this device and no track is cued; with cued tracks but no Cue output it shows
  "No cue output".
- Unavailable state (routing_state flag 2): the strip shows "Out 3/4 — not on this device"
  in the warning style; the fader stays usable; the tooltip says how to fix it (Settings → Outputs).
- Master strip shows its pair label when Main is not on 1/2, and "Out 1/2 (fallback)" with
  a warning when flag 1 is set.
- Track strips: a **Cue** toggle (headphones glyph + "Cue" tooltip) in the button row next to
  Mute/Solo — full mixer, compact mixer, Session mixer, track inspector.

## Arrangement track headers

- The same Cue toggle on audio, instrument and group headers (returns: in the mixer), shown only while a
  Cue output is assigned on this device (keeps headers clean on stereo laptops).

## MIDI Learn

- In learn mode the Cue toggles and the Cue fader light up like Mute and Master volume.
- **Map** tab labels: "<Track> · Cue", "Cue level".

## Strings

New keys in `Nota.Application/Localization/en.json`: `preferences.outputs*` and `mixer.cue*`.
Other languages come through the usual translation sync.

## Out of scope (spec Assumptions)

Nota Remote buttons for Cue; MCP tools for Cue; per-track Cue levels; Main/Cue on two devices.
