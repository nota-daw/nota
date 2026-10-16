# UI contract: piano roll vertical zoom

The user-facing surface this feature adds. ⌘ = Ctrl and ⌥ = Alt on Windows/Linux.

## Gestures

| Where | Input | Effect | Anchor |
|-------|-------|--------|--------|
| Note grid, keys column | ⌘⌥ + wheel / two-finger scroll | Rows taller (scroll up) / shorter (scroll down), factor 1.2 per notch | Pitch under the pointer |
| Keys column | Press + drag vertically | Down = taller, up = shorter, ~2.7× per 100 px; X ignored; no note created or played | Key pressed, held at the press height |
| Note grid (macOS trackpad) | ⌥ + pinch | Pinch out = taller, in = shorter | Pitch under the pointer |
| Note grid (focused) | ⇧F | Fit to notes | — |

Unchanged: wheel = vertical scroll, ⌘ + wheel = horizontal zoom, ⇧ + wheel / horizontal
swipe = horizontal scroll, pinch without ⌥ = no effect (as today).

Zoom input is ignored while a note move, resize or box selection is in progress.

## Limits

Row height 6–36 px, default 12 px. Zooming past a limit is a no-op.

## Command

| Id | Title | Category | Gesture | Aliases | Available when |
|----|-------|----------|---------|---------|----------------|
| `roll.fitNotes` | Fit to notes | Piano roll | ⇧F | zoom vertical rows height | A MIDI clip is open ("Open a MIDI clip first") |

Behaviour: all clip notes visible with one row of margin above and below, row height
clamped to the limits and the range centred; an empty clip returns to 12 px around C5.

## Header control

A neutral "Fit" chip (same style as the header's "Tools" chip) in the clip editor
header's right group, before the snap chip; tooltip "Fit to notes (⇧F)"; runs Fit to notes.

## Preferences → Shortcuts ("Piano roll" section)

| Keys | Description |
|------|-------------|
| ⌘⌥ + wheel | Zoom rows (vertical zoom) |
| Drag on the keys | Zoom rows · down taller, up shorter |
| ⌥ + pinch | Zoom rows (trackpad) |
| ⇧F | Fit to notes |
