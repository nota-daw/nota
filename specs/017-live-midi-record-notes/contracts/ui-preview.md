# Contract: UI preview of held recorded notes

What each view draws, when, and how it animates. Follows `DESIGN.md` (Studio) and the
`nota-design` skill.

## Data flow

1. `MainWindowViewModel.OnTick` (33 ms) calls `Engine.Poll()`, then raises
   `RecordingTick` while `Engine.IsRecording` (unchanged).
2. On `RecordingTick`, `RecordPreview.Update()` reads the snapshot once
   (`RecordHeldNotes`), and the set of notes in the take's clip/slot, and updates the
   appear-animation tracker. Then the existing `Timeline.Refresh(rebuildHeaders: false)`
   and `ReloadEditorNotes()` run, and the Session grid refresh as today.
3. Views draw from `RecordPreview` during their normal render; they never call the engine
   for the preview themselves.
4. When recording stops, `RecordPreview` clears; the final refresh draws stored notes only.

## Arrangement take (`ArrangementView.LaneControl`)

- On the take's track row only, if visible (`rLo..rHi`).
- Each held note: x from `BeatToX(startBeat)` to `BeatToX(max(startBeat, nowBeat))`,
  min 2 px; y from the same pitch mapping the take's clip uses (its pitch range extended
  to include held pitches).
- Held notes before the clip's start (count-in) draw left of the clip, over a faint
  extension of the take box from the earliest held start to the clip start, so they are
  not floating on the empty lane.
- Colour: the track's note colour, as stored notes are drawn; animated as below.

## Session slot (`SessionView.Grid`)

- The recording cell draws the slot's notes stored so far (it showed none while recording
  before), so the held notes sit among the take rather than on an empty cell; an open-ended
  take scales to the whole bars recorded so far.
- In the slot being recorded (`session_scene ≥ 0`), slot-local: x by `startBeat / slotLen`;
  a note crossing the loop end draws as two pieces (tail at the end, wrap from 0 to
  `nowBeat`).

## Piano roll (`PianoRollView`)

- Only when it shows the take's clip (`_editorTrackId == trackId` and the clip index is
  the record clip; arrangement takes). Clip-relative x: `startBeat − clipStart`.
- Held notes are an overlay, not editable, not selectable, not part of `SetNotes`.
- Notes left of the clip start (count-in) draw in the area before beat 0 if the roll shows
  it, else are clipped at the left edge.

## Appear animation

- Duration 150 ms (within 120–200 ms, FR-008), ease-out.
- Progress `p = clamp((now − firstSeen) / 150 ms, 0, 1)`; opacity `0.35 + 0.65·e(p)`;
  fill = blend(accent, note colour, e(p)).
- Starts on the first tick a note is seen held, or newly present in the take's stored
  notes if it was never seen held (staccato between ticks). Growth, release and the clip
  left move never restart it (key = pitch + absolute start beat).

## Repaint rules

- No preview work and no extra invalidation while not recording (FR-010, SC-004).
- While recording, the preview rides the existing `RecordingTick` repaint; no new timer.
  If the quickstart shows the animation is choppy at 30 Hz, a 60 Hz timer may run only
  while an animation entry is younger than 150 ms.
