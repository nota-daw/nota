# Contract: Controller lanes (piano roll) and the CC automation target

Follow [`DESIGN.md`](../../../DESIGN.md) (Studio) and the `nota-design` skill:
- sentence-case labels;
- neutral ink for curves and blocks;
- accent only for the point or block under the pointer and for the selection.

## Piano roll lane tabs

`Vel · Bend · Pres · Slide · <Controller>`

- **Fifth tab**: shows the current controller's short name: *Sustain*, *Mod wheel*,
  *Pitch wheel*, *Pressure*, *CC 11*, …
  - Default: *Sustain*, or the first controller with data in the clip.
  - The choice is remembered per clip editor session, like the lane mode.
- **Picker**: a click on the active fifth tab, or a small chevron beside it, opens a menu.
  - Controllers with data in this clip come first, each marked with the dot used by the
    automation picker.
  - Then Sustain (CC64), Mod wheel (CC1), Pitch wheel, Channel pressure, Expression (CC11)
    and Breath (CC2).
  - Then **Other CC ▸**, a submenu of 0–127 in groups of 16, "CC 7 — Volume" style.
    Reserved CCs are not listed.
- **Height**: the lane uses the MPE lane height (`ExprH`).

## Controller lane (clip-wide)

**Time axis**: the same as the notes, covering the whole clip. A pickup before the clip
start is drawn faintly left of the clip edge, like notes.

**Switch controllers (CC64–69)** are drawn as blocks:
- A block is a span where the value is ≥ 0.5. Partial values show as the block's fill
  height, so a half pedal draws a half-height block.
- **Drag on empty lane**: draws a new block (snaps to the grid; ⌘ or Ctrl turns snap off,
  as for notes).
- **Drag either edge**: resizes the block.
- **Drag the body**: moves the block.
- **Double-click a block or press Delete with it selected**: deletes the block.
- **Right-click**: *Clear Sustain* (the controller's name).

**Continuous controllers** are drawn as a curve with points:
- **Range lines**:
  - CC: 0–127, with a midline label.
  - Pitch wheel: centre line plus ±full travel, labelled "centre" and "+/−". Points within
    2% of centre snap to exact 0.
- **Drag across the lane**: draws freehand. The drawn stretch replaces the points under
  it. ⌥ draws a straight line.
- **Points**:
  - Drag a point to move it in time (between its neighbours) and in value.
  - Double-click empty lane adds a point; double-click a point deletes it.
- **Right-click**: *Clear <name>*.

**Hover readout**: the value under the pointer in the controller's units ("64", "+0.42",
"on / off").

**Undo**: every gesture is one undo step (`nota_midi_clip_ctl_set` once on release; a live
preview during the drag uses the same call without a checkpoint if one exists, otherwise
the final set only).

**Selection and copy**: notes are unaffected by controller edits. Copying notes does not
copy controllers; copying the clip does.

## Arrangement automation target picker (instrument tracks)

- **Submenu**: a **MIDI controllers ▸** submenu sits after the instrument's parameters.
  Its list matches the piano-roll picker, minus the clip-data dots.
- **Dots**: the automated-target dot marks ctls that have a target-5 lane.
- **Lane label**: the short name ("Mod wheel", "CC 74").
- **Value range**: wheel −1..1 with a centre line; others 0..1, shown as 0–127.
- **Recording**: the existing behaviour. With Record engaged, moving the hardware
  controller writes the lane if it exists, latched until stop.
- **Clip data**: where a target-5 lane has points, the piano-roll lane for that
  controller shows a one-line note: "Track automation overrides this lane".
