# Data Model: Studio context menus

Only one persisted entity is new; the rest are view-only descriptions of menu parts.

## RecentInstrument (persisted, app settings)

| Field | Type | Notes |
|---|---|---|
| `Kind` | string | `"builtin"` or `"plugin"` |
| `Id` | string | built-in device kind id (e.g. `volt`), or the plug-in's unique id from the scan |
| `Name` | string | display name at the time it was added ("Nota Volt", "Diva") |
| `FactoryPreset` | string? | factory preset id when the add came from a preset item; null otherwise |

Stored as `Settings.RecentInstruments : List<RecentInstrument>`, most recent first.

**Rules** (FR-012a):
- Identity = (`Kind`, `Id`, `FactoryPreset`). Recording an entry already present moves it to the top; no duplicates.
- Capacity 5; recording a sixth drops the oldest.
- Recorded on every instrument add by any route: browser drop / double-click / activate, "Add track ▸ Instrument" (Volt), "Add track ▸ Recent", instrument replace, MCP add tools.
- Read through `Visible(isInstalled)`: plug-in entries whose id is not in the current scan are hidden (not deleted — they come back if the plug-in returns).
- Missing / malformed list in an older settings file → empty list.

## View-only parts (not persisted)

- **MenuHeader**: colour (brush or none), name (truncates), badge text (mono, muted). From track / clip / device / chain / scene / version state at open time.
- **ColourGrid**: 9 × 3 palette indices + Auto (−1); `current` index; `pick(index)`.
- **SegmentRow**: options `(label, value)`; `chosen` value or none; `pick(value, allModifier)`.
- **SwitchItem**: label, icon, `isOn`, `toggle()`.
- **MenuItem roles**: plain · value (current value text) · danger · inline.
