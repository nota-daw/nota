# UI contract (Devices panel)

The track's device chain in the detail panel (`DeviceChainView`). Rack cards' own chain
selection (specs/042) is unchanged and single.

## Gestures

| Gesture | On | Result |
|---------|----|--------|
| Click | a card's header or empty body | selects only that card; anchor = it |
| Shift-click | a card | selects anchor..card in chain order (MIDI → instrument → audio) |
| ⌘-click (Ctrl-click on Windows / Linux) | a card | toggles it; anchor = it |
| Click | empty chain space | clears the selection |
| Esc | panel focused, nothing in text entry | clears the selection |
| Drag a selected card's header | — | moves every selected card of that section to the drop gap |
| Right-click | an unselected card | selects only it, then shows its menu |
| Right-click | a selected card | menu for the whole selection |

Every selected card shows the existing selection border (DESIGN.md accent); nothing marks the
anchor.

## Keys (panel focused, not typing)

| Key | Action | Notes |
|-----|--------|-------|
| ⌘C / ⌘X / ⌘V | copy / cut / paste the selection | paste lands after each section's last selected device, or at its end |
| ⌘D | duplicate each selected run after itself | the instrument is skipped |
| Delete / Backspace | delete the selection | with the instrument: confirmation (below) |
| ⌘G | Group into rack | status-line reason when refused |
| ⌘⇧G | Ungroup rack | the selection must be one rack card |

## Menu (selected card)

- Header: the device name and badge for one device; **"N devices"** for several.
- Groups: Save preset (one device only) · Copy, Cut, Paste, Duplicate · Move to track ▸ ·
  **Group into rack** (enabled when the shape is valid) / **Ungroup rack** (on a one-chain
  rack; disabled on two or more chains) · Delete (or **Delete track** when the selection
  includes the instrument).
- Group / Ungroup appear on MIDI-effect, instrument and audio-effect cards alike.

## Confirmation

The selection holds the instrument **and** another device, and the user deletes, cuts or
moves it: a dialog "Delete track ‹name›? The track and its N selected devices will be
removed." with Cancel / Delete, every time. The instrument alone keeps specs/043 (asks only
when the track has clips).

## Status line

| Situation | Message |
|-----------|---------|
| Grouped | "Put N devices in an Audio Effect Rack" / "… an Instrument Rack" / "… a MIDI Effect Rack" |
| Ungrouped | "Took N devices out of the rack" (+ " — its macros and chain settings were dropped" when bit set) |
| Not contiguous | "Select devices next to each other to group them" |
| MIDI + audio without instrument | "MIDI and audio effects go in one rack only with the instrument between them" |
| Rack in the run | "‹Rack› can't go inside another rack" |
| Plug-in in a MIDI Effect Rack | "‹Plug-in› can't go in a MIDI Effect Rack" |
| Instrument Rack as the instrument | "An Instrument Rack can't go inside another Instrument Rack" |
| Two or more chains | "Only a rack with one chain can be ungrouped" |
| Paste skipped | "Pasted N devices, skipped M this track can't take" |

## Selection lifetime

Cleared on track switch, on a rack-chain device selection, and when its cards are removed;
after Group the new rack is selected, after Ungroup the restored devices are.
