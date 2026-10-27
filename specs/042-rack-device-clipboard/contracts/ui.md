# Contract: UI behaviour

## Rack card (Chains tab, device lane)

| Gesture | Result |
|---|---|
| Left-click a device's header | selects it: the track chain's selected-device look (accent outline); clears any other selection on the track |
| Drag a device's header (or its grip) ≥ 5 px | the card dims to 0.55 and an `AccentBright` 3-px drop bar shows at the gap under the pointer, only among the same section's cards |
| Release over a gap | moves there: one undo step; the device stays selected |
| Drag over a chain row in the chain list | the row gets the drop outline (as for Browser drops) if that chain can take the device; else no drop is offered |
| Release on a chain row | moves the device to the end of that chain's section, and chooses that chain |
| Right-click a device's header | menu: Open · Turn on/off · ─ · Move left · Move right · ─ · Copy ⌘C · Cut ⌘X · Paste ⌘V · Duplicate ⌘D · ─ · Delete ⌫ |
| Right-click a chain row | the chain menu gains **Paste** (at the end of the clipboard's family section), disabled when the clipboard is empty or doesn't fit |
| Click the lane's dashed "+" target | its add menu gains **Paste** at the top when the clipboard fits that section |
| Right-click the instrument card | as now (no copy or paste of instruments) |

## Keys (device panel focused)

| Key | Rack device selected | Track device selected | Nothing selected |
|---|---|---|---|
| ⌘C | copy it | copy it (as now) | — |
| ⌘X | cut it | cut it (as now) | — |
| ⌘V | paste after it, same chain and section | paste after it (as now) | paste at the end of the track chain (as now) |
| ⌘D | duplicate after it | duplicate after it (new) | — |
| Delete / ⌫ | delete it | delete it (as now) | — |

A paste that's refused leaves everything as it was and puts the reason on the status line (data-model.md). A pasted keyed effect adds "· sidechain dropped".

## Track chain

- The device menu gains **Duplicate ⌘D** between Paste and the rack group items.
- ⌘V pastes a rack chain copy as it pastes any copy. A rack device (kind 5) pasted onto the track chain is fine (existing behaviour).

## Shortcuts list (Preferences → Shortcuts, "Devices (after a click on a device)")

- Add: `⌘ D` — "Duplicate the device". Existing rows ⌘C / ⌘X / ⌘V / Delete now also say "(track or rack chain)" where the text names the chain.

## Themes

The selection outline, drop bar, dimmed card and row drop outline use the track chain's tokens (`Accent`, `AccentBright`, `Teal` washes). They are checked on a 4-theme render.
