# UI contract: clip editor gestures, keys and controls

The user-facing contract this batch adds or changes. Preferences → Shortcuts (nota-shortcuts)
and the user docs (nota-docs) are updated from this list.

## Playhead

| Where | Gesture | Result |
|---|---|---|
| Audio clip editor ruler (arrangement clip) | click | transport → arrangement position under the pointer, snapped (warped clips, Snap on) |
| same | drag | scrub, snapped |
| same | Alt + click / drag | free (no snap) |
| Audio clip editor, playhead line (ruler, marker strip, wave), ±4 px | drag | scrub like the ruler; cursor ↔ |
| Piano roll ruler | click / drag / Alt | unchanged (#50) |
| Piano roll ruler, playhead line ±4 px | drag | scrub |
| Piano roll note grid, playhead line ±3 px | drag ≥ 3 px | scrub; a click without movement is a normal grid click |
| Either editor, Session-slot clip | ruler / line | no seek; cursor 🚫; tooltip "A Session slot plays on its own loop clock — launch it to play from the start" |

Playhead visibility: audio editor shows it whenever the song position is inside the clip's
played region (stopped or playing); Session slots follow the slot clock as today.

## Rulers

| Ruler | Bars mode | Time mode |
|---|---|---|
| Arrangement | unchanged | `m:ss` … by zoom |
| Piano roll, one arrangement clip | arrangement bars (`Origin + local`) | arrangement time |
| Piano roll, several clips | unchanged (already arrangement) | arrangement time |
| Audio editor, warped | arrangement bars at the warp's position (before Start bracket = before the clip) | arrangement time |
| Audio editor, unwarped | arrangement bars | arrangement time |
| Session-slot clip (any editor) | clip-relative, as today | clip-relative time |

Mode follows a click on the transport position read-out (bars ↔ time), live.

## Keyboard

| Situation | Space / Return | ⌘1–⌘5 (grid) |
|---|---|---|
| After a click on a non-text control in the clip editor | transport; the focused control is **not** activated | act on the clip editor grid; if that grid is inert (unwarped audio, ADSR mode) on the arrangement grid; status line names the target |
| Text field focused | typed into the field | ignored |
| After Enter / Esc / click-away from a field | transport | as above |
| During a warp-marker drag | — | Esc cancels the drag |

## Warp section (audio clip editor)

```
Warp            [● on]   Complex ▾
Tempo   [ 87.00 BPM ]   ÷2  ×2
[Detect tempo] [To transients]
```

- **Tempo**: `DragNumber` — vertical drag (0.5 BPM per pixel), double-click → type,
  Enter / click-away commit, Esc cancel. 20–999 BPM, two decimals.
- **÷2 / ×2**: one step each; disabled when the result would leave the range or no tempo is
  known.
- Tooltip on the label and value: "The tempo the audio was recorded at. Nota stretches it to
  the project tempo. ×2 / ÷2 fix a half- or double-time detection."
- Warped clip: value = tempo of the played region; edits scale all markers + trim.
- Unwarped clip: value = last detected tempo or "—" (dimmed, still editable); an edit turns
  Warp on at that tempo.
- Every edit (typed commit, a whole drag, ÷2, ×2) is one undo step.

## Warp-marker drag

While dragging: marker, waveform and the two neighbouring BPM chips follow the pointer.
Release: one commit, one undo step, the waveform is replaced by the engine's stretch.
Esc / capture lost: everything returns to the pre-drag state, nothing is committed.
