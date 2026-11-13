# Data model: Settings reorganisation

No stored data changes. The only "model" is the sidebar table in `PreferencesWindow`.

## Page

| Field | Meaning | Rule |
|---|---|---|
| `Id` | stable English name callers ask for | unique; never translated; never reused for other content |
| `Title` | shown in sidebar and header | localised |
| `Subtitle` | shown beside the header title | localised; describes exactly what the page holds |
| `Icon` | `GlyphKind` | — |

## Group

| Field | Meaning |
|---|---|
| `Group` | localised heading |
| `Pages` | ordered pages |

## The table (order is the sidebar order)

| Group | Ids |
|---|---|
| Devices | `Audio`, `Outputs`, `Inputs`, `MIDI`, `Gamepads` |
| Remote control | `Remote`, `MCP` |
| Plug-ins | `Plug-ins`, `Downloads` |
| General | `Appearance`, `Language`, `Editing`, `Library`, `Shortcuts`, `Updates` |

## Settings each moved control writes (unchanged)

| Control | Page | Setting |
|---|---|---|
| MCP switch / port | MCP | `McpEnabled`, `McpPort` |
| Language picker | Language | UI language (spec 068) |
| Label every note | Editing | `PianoRollLabelAllNotes` |
| Fade audio clips when splitting | Editing | `SplitFades` (+ `Engine.SplitFades`) |
| Open the detail panel on select | Editing | `OpenDetailOnSelect` |
| Output device / ASIO output pair | Outputs | engine audio config |
| Input device / ASIO input pair / Inputs list | Inputs | engine audio config; input setup per device (spec 086) |
