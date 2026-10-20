# Contract: user-facing behaviour

## Opening

| Action | Result |
|--------|--------|
| Double-click a MIDI clip that is in a selection of ≥2 MIDI clips | All selected MIDI clips open; clicked clip active |
| Double-click a clip outside the selection / single selection | Single-clip editor (unchanged) |
| Command palette / arrangement clip context menu: **Edit selected clips** | All selected MIDI clips open; first in time active. Disabled with <2 MIDI clips selected |
| Change the arrangement selection while the Clip editor is on screen | Editor follows: ≥2 MIDI clips together, 1 clip alone, no MIDI clip → unchanged |

No new keyboard shortcut is bound by default (the command is available to the shortcut
builder). If one is added, run `nota-shortcuts`.

## Clip strip (multi-clip editor only)

Row between the ruler and the note grid, on the roll's beat axis.

| Gesture | Result |
|---------|--------|
| Click a clip bar | That clip becomes active |
| Click the lock on a non-active bar | Toggle reference (dimmed, read-only) |
| Lock on the active bar | Not shown (active is always editable) |

Mouse gestures are listed in Preferences → Shortcuts (nota-shortcuts).

## Header

`<active clip name> · +N clips` with the active clip's track colour cell.

## Visual states (DESIGN.md, via nota-design)

| Notes of | Look |
|----------|------|
| Active clip | full track colour (as today) |
| Other editable clip | track colour, subdued |
| Reference clip | dimmed further, no hover / no edge cursors |
| Outside every open clip | existing outside-clip wash |
