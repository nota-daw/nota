# Contract: UI and MCP surface

## Arrangement — master automation row

- The master footer row's label opens the same target picker as a track lane header:
  **Volume**, **Tempo**, **Pan**, then one submenu per master device listing its parameters
  (plug-ins: their automatable parameters), in master-chain order.
- The row edits exactly one target at a time, with the shared automation editor: click to
  add, drag, curve handle, double-click/right-click delete, marquee select, copy/paste,
  Delete, undo/redo. No tempo-only gesture exists.
- Tempo target: axis labels and hover/drag readout in BPM (two decimals while dragging);
  two small editable fields at the lane's top/bottom edge show the range max/min
  (FR-003a). Points outside the range draw clamped to the edge.
- No separate tempo row exists between the ruler and the tracks. The Ctrl-hover time-scale
  cursor and Ctrl-drag bar mapping no longer exist.

## Transport

- Tempo field: when `nota_engine_tempo_automated` = 1 — read-only, shows the tempo under the
  playhead, tooltip `transport.tempo_automated` ("Tempo follows the master Tempo
  automation"). Otherwise unchanged.

## Mixer

- Master strip: a pan knob like a track strip's (PR 2).

## Notice

- After opening a migrated project with `tempoClamped`: one info toast
  `project.tempo_clamped` ("This project's tempo went outside 20–300 BPM and was limited to
  that range"). Shown once.

## Shortcuts (Preferences → Shortcuts)

- The "Tempo track" group from #568 is removed.

## MCP

- `get_overview`: adds `masterTrackId`.
- `list_automation` / `get_automation_points` / `set_automation_points` /
  `add_automation_lane` / `remove_automation_lane`: accept `masterTrackId`; target names add
  `tempo` (master only; BPM values) — master `pan` and `device_param` targets work there too;
  `volume` on the master returns -1 (its volume is the graph-level lane).
- `add_plugin_param_automation` (new): a hosted plug-in effect's parameter lane by stable id, on
  any track or the master.
- `set_master_pan` (new, next to `set_master_volume`): −1..1.
- `set_tempo`: error "Tempo is automated on the master track; edit its Tempo lane" while
  automated.
