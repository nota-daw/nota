# Contract: device page files

Two files with the same shape (FR-018, FR-018a, research R8):

- **User global** — `<DataDir>/controllers/pages.json`: the user's edited pages, applied in every
  project.
- **Project copy** — `devicepages.json` in the project bundle (next to `midimap.json`): the edited
  pages for the device types that project contains, written on save. While the project is open,
  its copy wins over the global file; opening a project never rewrites the global file.

```jsonc
{
  "format": 1,
  "types": {
    "builtin:inst:volt": [
      { "name": "Main",   "params": ["Cutoff", "Resonance", "Env Amount", "Drive", "Attack", "Decay", "Sustain", "Release"] },
      { "name": "Osc",    "params": ["Osc 1 Wave", "Osc 2 Wave", "Detune", null, null, null, null, "Glide"] }
    ],
    "plugin:VST3:ABCDEF0123456789": [
      { "params": ["id:1024", "id:1025", "id:7", null, null, null, null, null] }
    ],
    "rack:inst": [
      { "params": ["macro:1", "macro:2", "macro:3", "macro:4", "macro:5", "macro:6", "macro:7", "macro:8"] }
    ]
  }
}
```

| Element | Meaning |
|---------|---------|
| type key | `builtin:inst:<kind>`, `builtin:fx:<kind>`, `builtin:midi:<kind>`, `rack:audio`, `rack:inst`, `rack:midi`, `plugin:<pluginId>` (the id `TrackDevicePluginId` / `TrackInstrumentPluginId` return) |
| page | `name` optional; `params` up to 8 entries |
| param ref | built-ins: parameter name as the engine reports it; plug-ins: `id:<PluginParamId>`; racks: `macro:<n>`; `null` = empty knob |

Resolution order for a focused device: project copy → user global → built-in catalog
(`DevicePageCatalog`) → plug-in default (all parameters in order, 8 per page). A ref that no longer
resolves (renamed / removed parameter) leaves that knob empty; the editor shows it struck through
so the user can fix it.

"Reset pages" removes the type key from the global file and from the open project's copy.
