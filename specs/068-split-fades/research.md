# Research: Fade audio clips when they are split

## Decision: visible ADSR edges, not a hidden declick

Unwarped clips already fade about 3 ms at each edge inside the render (`audioClipEdgeGain`). That fade is not drawn and cannot be dragged. Warped clips do not have it. The maintainer's answer to "fixed, then drag" means the fade has to be the attack and release the arrangement and the clip editor already draw. The hidden 3 ms fade stays as a safety net on the unwarped path.

## Decision: 4 ms, converted with the authoring tempo

`0.004 * authoringBpm_ / 60`. At the default 120 BPM that is 0.008 beats. `authoringBpm_` is already updated by `setBpm` on the message thread; `transport_.bpm()` can still be stale until the audio thread applies the command, so the fade does not read it. No new tempo mirror.

## Decision: do not fade inside the carve

`audioCarveLeft` / `audioCarveRight` / `audioSlice` also serve delete, duplicate, and insert time. Those stay hard cuts. Only `Engine::splitClip`, `Engine::splitClipsAtRange`, and `Engine::fadeTouchingSplits` call `raiseSplitEdge`.

## Decision: raise, never shorten

A cut edge that the carve zeroed is shorter than 4 ms, so it is raised. An attack kept on the left piece, or a release kept on the right, is not the cut edge and is not passed as the edge to raise. A fade the user dragged longer than 4 ms fails the "shorter than" test and stays. If attack and decay already fill the piece, the cap is 0 and the release stays 0.

## Decision: no overlap

`placeAudioNonOverlap` forbids two clips on one track from overlapping, so a constant-power crossfade would need a new clip model. The maintainer asked for a draggable fade. A fade-out plus a fade-in is that fade. A longer drag dips the level at the cut; the 4 ms default only stops the click. The PR says this in plain language.

## Decision: no shortcut

Shift+X already shifts the typing-keyboard octave up. The command is palette and context menu only, with an empty gesture so the shortcut list does not gain a row.

## Alternatives rejected

- A hidden non-editable declick. The maintainer's drag answer rules it out.
- A new overlap crossfade. Out of scope for the clip model, and not what was asked.
- Putting the fade in the render edge fade and making that editable. The handles already exist on ADSR; a second fade graphic would duplicate them.
