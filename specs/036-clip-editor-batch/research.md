# Research: Clip editor batch

All findings come from reading the code on `develop` @ 92a0608. Paths are under
`src/managed/Nota.App/` unless noted.

## R1 — Waveform position ↔ arrangement position (#277, #278, #275)

**Facts.** The audio clip editor draws everything on one normalised content axis
(`FracToX` / `XToFrac`, mirrored when reversed). Three domains map onto it:

- warped clip — warp beats `[0, TotalBeats]` (the full warp, markers' last beat); the
  played window is `[_trimStart, _trimEnd]` (`warp_play_start / _end`);
- unwarped clip — source frames `[0, _srcTotal]`; the played region is
  `[_srcOff, _srcOff + _srcLen]`;
- the playhead `_frac` runs over the **played** window, `0` = the clip's first audible
  moment, and is flipped (`1 − frac`) when reversed (`Render`, the `_frac` block).

`OnPlayhead` computes `frac = (beats − _startBeat) / _lengthBeats`, gated on `playing`. Audio
clips have no clip loop (`NotaAudioClipInfo` has no loop fields), so the played window maps
linearly onto `[start, start + length]` in the arrangement.

**Decision.** Introduce one pure mapping, `ClipTimeMap` (data-model.md), built from the clip
geometry on every `Reload` and handed to the waveform view:

- `PlayFracAt(contentFrac)` — inverse of the existing playhead draw: content frac → played
  frac (clamped to `[0,1]` only for the playhead, not for seeking), reversed-aware;
- `ArrangementBeatAt(playFrac) = start + playFrac × length`;
- and the forward direction for ruler labels: `ArrangementBeatAtContent(contentFrac)`.

Seeking left of a trimmed Start (played frac < 0) gives a beat before the clip start;
clamp at 0, as the spec's edge case says. The same map labels the ruler (R3) so labels
and seeks can't disagree.

**#277 fix.** Drop the `playing &&` gate for arrangement clips; keep the session branch.

**Alternatives.** Computing the inverse ad hoc in the pointer handler — rejected: the
ruler labels need the same transform, and the smoke test can exercise a pure map.

## R2 — Seeking from the clip editors (#278)

**Facts.** The piano roll already seeks: `RulerLane` press / drag → `SnapBeat(XToBeat(x))`
→ `Seek(local)`; the host (`MainWindow.Detail.cs`, `BuildClipEditor`) maps
`roll.Origin + local` → `Engine.Seek` + `Timeline.SetPlayhead`. The session-slot roll
(`MainWindow.Detail.cs` ~L772) sets no `Seek`, so seeking is already off there, with a
default cursor. The audio editor has no `Seek` hook.

**Decision.**

- Audio editor gets `Action<double>? Seek` (arrangement beat), set by `BuildAudioClipEditor`
  for arrangement clips only (`SessionClip.SceneOf(clipIndex) < 0`), same body as the roll's
  (`Engine.Seek`, `Timeline.SetPlayhead`).
- Ruler press / drag in `WaveCanvas` → snap → `Seek`. Snap in the **warp-beat** domain with
  `SnapWarp` (the grid already drawn there); Alt = free (`_free`, as for markers). Unwarped
  clips have no beat grid (#269: "Snap needs Warp"), so seeks there are free — this matches
  what Snap does elsewhere in that editor.
- Playhead-line grab: a hit zone of ±4 px around the drawn playhead x, active in the ruler,
  the marker strip and the wave area of the audio editor and in the ruler + note grid of
  the piano roll. The zone wins over the content gesture under it (FR-005). Cursor
  `SizeWestEast` over the zone.
  - Piano roll: the grid's press handler checks the zone first (`NoteGrid.OnPointerPressed`);
    while dragging it reuses `RulerLane.SeekAt` logic, so snap / Alt match the ruler.
  - A note under the playhead line stays editable from outside the ±4 px zone; with the
    transport stopped the line sits where play starts, which is exactly where users click to
    add a note at the song position — so in the note grid the grab needs **no modifier but a
    narrower zone (±3 px) and only when the line is visible**, and a click without movement
    there falls through to the grid's normal click (adds / selects a note). Drag ≥ 3 px
    turns it into a scrub.
- Session-slot clips (both editors): no `Seek`; ruler and line show `StandardCursorType.No`
  and a tooltip "A Session slot plays on its own loop clock — launch it to play from the
  start". (Avalonia `ToolTip.Tip` on the ruler control, set when `Seek is null` and the clip
  is a slot.)

**Alternatives.** Seek within a playing slot — rejected by the user (clarification Q1).
Modifier-gated line grab in the note grid — rejected: undiscoverable; the narrow zone plus
the click-falls-through rule keeps note entry safe.

## R3 — Ruler labels in arrangement position and in time (#275)

**Facts.**

- Piano roll `RulerLane.Render` labels `barNo = floor(b/bpb) + 1 + RulerBarOffset`, where
  `RulerBarOffset` is non-zero only for multi-clip sets (their origin sits on a bar line).
  `Origin` is the clip's start for a single clip.
- Audio editor `DrawRuler`: warped → bar.beat labels over warp beats from 0; unwarped →
  seconds over the source from 0 s.
- Arrangement `RulerControl` (`ArrangementView.Controls.cs`): bars only.
- The transport read-out flips on `TransportViewModel.ShowTimeDisplay`; time =
  `beats × 60 / Bpm` (one global tempo; scene tempos change `Bpm` itself — there is no
  tempo map).

**Decision.**

- One shared formatter, `RulerLabels` (pure): given an arrangement beat, a step and the mode
  (bars / time), returns the label — bars `N` / `N.b` / `N.b.s`, time `m:ss`, `m:ss.f` …
  with as many decimals as the step needs (reuses the shape of the existing
  `SecondsLabel`). Time steps for tick placement come from the existing seconds step table.
- Mode source: a static `RulerDisplay` (observable `ShowTime` + `Changed` event) in
  `Nota.App`, set from `TransportViewModel.ShowTimeDisplay`'s property-changed in
  `MainWindow`; the three rulers subscribe and `InvalidateVisual`.
- Piano roll single clip: labels use `Origin + b` (arrangement beat), not `b`. Grid lines
  and their spacing stay clip-local (FR-011); for a bar-aligned clip (the usual case) the
  labels read 9, 10 … exactly. A clip starting off a bar labels its ticks with their true
  position (e.g. 2.3). Session-slot rolls keep `Origin = 0` → unchanged.
- Audio editor: labels come from `ClipTimeMap.ArrangementBeatAtContent` for warped and
  unwarped clips alike. For an unwarped clip in bars mode the ticks are placed at
  arrangement beat steps mapped back to content (the inverse of the same map), so the
  ruler reads bars too; in time mode, seconds.
- Time mode places ticks at time steps (arrangement seconds) in all three rulers; the
  grid itself (lines in the content area) does not change.

**Alternatives.** Re-basing the piano roll grid onto arrangement bars for off-bar clips —
rejected: changes snapping (FR-011) and the multi-clip `ClipSet` origin logic.

## R4 — Keyboard focus after a click in the clip editor (#114)

**Facts.**

- Space / Return: tunnel handler on the main window (`OnGlobalTransportKey`) — runs before
  any focused control, skipped only when `e.Source` is a `TextBox` / `NumericUpDown`. The
  floating detail window forwards via `HandleTransportKeyTunnel`.
- Grid ⌘1–⌘5: native menu items → `StepGrid` → `GridTarget()`. After a press inside the
  detail area `_detailWasLastFocused` is true, so the grid commands act on the **clip
  editor's** grid, by design (status line says "Clip editor grid: …"). On an unwarped
  audio clip or with the ADSR shown, the clip editor's grid has no visible effect
  ("Snap needs Warp").
- `StepGrid` ignores the commands while a `TextBox` has focus.
- Avalonia `Button` / `ToggleButton` activate on **Space KeyUp**; the tunnel handler only
  marks the KeyDown handled. The clip editor's props rail is made of focusable buttons
  (`ClipEditorKit.Button`, `SwitchRow`, dropdown cells): after clicking one, Space starts
  playback *and* re-clicks that button on release.
- Hidden text fields already hand focus back (`HideBpm`, `ValueEntry.Close`, #141).

**Decision.** Reproduce first in the running app (macOS, the reporter's setup: an audio
clip with the ADSR shown, then a MIDI clip), using the env-guarded UI-verify hook.
Expected fixes, by the hypotheses above:

1. Space KeyUp: extend the tunnel handler to also swallow the matching `KeyUp` of a Space
   it handled (track `_spaceDown`), so a focused button in the clip editor (or anywhere)
   is not activated.
2. Clip editor buttons and switches: `Focusable = false` for pointer-only controls in the
   props rail (`ClipEditorKit` factories), so a click leaves no focused control behind; keep
   focus for the text fields.
3. Grid feedback: when the clip editor is the grid target but its grid is inert (unwarped
   audio / ADSR mode), apply the command to the arrangement grid instead, and say so on the
   status line. This makes "grid shortcuts work as after a click in the arrangement"
   (FR-013) true in exactly the case the reporter hit, without changing the design for MIDI
   and warped audio clips.

If the reproduction shows another cause, record it here and fix that instead; the
acceptance scenarios in the spec stay the test.

**Reproduction (2026-10-22, T019).** Headless Avalonia run of the real `MainWindow` with
input through the headless platform (hit testing, focus, tunnel / bubble): an audio clip at
bar 3, clicks on the arrangement, the props rail, the **ADSR** tab, the ADSR **Reset**
button and the wave, each followed by Space down / up twice and `grid.narrower`.

- Space played and stopped after every click; the focused element stayed `(none)` after
  each one — clip editor buttons and segments take no focus on click, so no control was
  re-activated by Space's KeyUp. Hypotheses 1 and 2 are **not** reproduced (T020, T021 N/A).
- After any click in the detail area, `grid.narrower` went to "Clip editor grid: Auto ·
  1/32" — in ADSR mode and on an unwarped clip, a grid nothing on screen follows. The
  reporter's screenshot (attachment on #114) shows exactly this state: an unwarped clip on
  the ADSR tab, status line "Clip editor grid: Auto · 1/32". Hypothesis 3 is the cause of the
  "grid shortcuts stop responding" half; fixed by `AudioClipEditorView.GridInert` +
  `GridTarget()` (T022). After the fix the same run reports "Arrangement grid: …".
- The Space half could not be reproduced (headless is not the macOS native host). The
  tunnel handler already runs before any focused control; nothing in the clip editor
  marks Space handled. Left as is; the quickstart's in-app check (§ 4) covers it on macOS.

## R5 — Clip warp tempo control (#248)

**Facts.** Per-segment BPM = `beatSpan × srcSR × 60 / srcSpan` (`ApplySegmentBpm`, the chip
drawing). Warp edits go through `WarpEdit` (deferred background stretch). The engine keeps
the trim window across marker edits (`clampWarpPlay`). `nota_clip_auto_warp_bpm(bpm)`
enables warp with a given tempo and snaps the length (no detection). The managed engine has
`BeginUndoGroup` / `EndUndoGroup`.

**Decision.**

- Clip tempo = played region's warp-beat span ÷ its source span, in BPM:
  `bpm = (pe − ps) × srcSR × 60 / (src(pe) − src(ps))`, `src(·)` interpolated through the
  markers (data-model `ClipTempo`). For a single segment it equals the segment chip.
- Set tempo `t` on a warped clip: factor `k = t / bpm`; every marker beat × `k`, trim
  window × `k` (`play_end = 0` stays 0); commit markers then trim inside one undo group via
  `WarpEdit`. Relative segment timing is preserved (FR-019).
- Unwarped clip: no detection on open (it is heavy). Show the last detected value (`ai`
  has none; the editor's `_detectedBpm` comes from Detect tempo / import) or "—". An edit
  calls the existing `Engine.AutoWarpClipAtBpm(t)` (wrapper over
  `nota_clip_auto_warp_bpm`) inside `WarpEdit` — enables warp and conforms the clip in one
  engine call, one undo step.
- Control: a `DragNumber` (existing control: drag, double-click → text, Enter / Esc) in a
  "Tempo" row directly under the Warp switch row, with `÷2` and `×2` icon-less text buttons
  (`ClipEditorKit.Button` style, compact). Range 20–999 BPM (the chips' range); ×2 / ÷2 that
  would leave it do nothing. Tooltip: "The tempo the audio was recorded at. Nota stretches
  it to the project tempo. ×2 / ÷2 fix a half- or double-time detection."
- A drag edits live without committing (scale markers locally, redraw) and commits once on
  release — same pattern as the BPM chip scrub.

**Alternatives.** Re-running auto-warp with the new tempo on warped clips — rejected: it
would throw away hand-placed markers.

## R6 — Live waveform while dragging a warp marker (#140)

**Facts.** The waveform of a warped clip is engine-rendered peaks over the full warp in the
beat domain (`GetClipWarpFullPeaks`, zoomed slices via `GetClipWarpPeaksRange`), i.e. laid
out by the markers **as committed**. During a marker drag only `_mk` changes; the peaks stay
until `MarkersCommitted` → engine → `Reload`.

**Decision.** Display-only re-timing: at drag start snapshot the committed markers
(`_mkCommitted`). While dragging, draw each pixel column at new beat `b` from the peak at
old beat `b' = beatOld(srcNew(b))`, both piecewise-linear through the marker lists
(data-model `WarpRemap`). Overview and zoomed detail peaks are both beat-domain, so the
same remap applies. Cost: two binary searches per column — negligible at ~2 000 columns.
BPM chips already derive from `_mk`, so they update live for free.

Release → `Commit()` (unchanged: one engine call, one undo step) → `Reload` swaps in the
real stretch. Esc during the drag: `MainWindow.HandleKeyDown` asks the visible audio editor
to `CancelGesture()` before the arrangement's `EscapePressed`; the canvas restores
`_mk = _mkCommitted` and releases capture. Capture lost → same restore.

**Alternatives.** Asking the engine to re-stretch during the drag — rejected: seconds of
work per move and AR concerns; the display remap is exact for the peaks' envelope at the
resolution drawn.

## R7 — Principle V (one logical change per PR)

Six issues in one PR is a deliberate batch on one area (the clip editor), like #272 / #273,
at the user's request. Recorded under Complexity Tracking in plan.md.
