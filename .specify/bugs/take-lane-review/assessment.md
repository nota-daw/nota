# Bug Assessment: Take-lane review follow-ups (#187)

- **Slug**: take-lane-review
- **Created**: 2026-11-04
- **Source**: review comments on https://github.com/nota-daw/nota/pull/403 (host: github.com — allowlisted), the three left after #187 was merged. Issue #187 stays closed; this follow-up references it.
- **Verdict**: valid
- **Severity**: high (the two P1 playback bugs); medium for the highlight

## Report (verbatim or summarized)

Greptile comments on PR #403, created 2026-10-31T11:31:05Z, after #187 closed. Earlier comments on that PR (flatten sound, tempo re-place, Escape swipe, inactive highlight, saved tempo on load) were already fixed before merge and are out of scope.

1. **Recording adds a volume dip** (`Engine_Recording.cpp`, P1). When loop recording stops partway through the last pass, `materialiseLoopTakes` creates adjacent clips for that pass and the earlier pass behind it. Playback fades both clips to zero at their shared edge. The old recording used one clip, so it had no fade there. This adds a roughly 6 ms volume dip before the user makes any comp edit. Preserve the old playback gain across these automatic joins, and compare rendered audio against the old single-buffer result.

2. **Comp cuts use old tempo** (`Engine_Takes.cpp`, P1). `compTake` uses the take's new tempo mapping, but `overwriteAudioClipsInRange` reads the transport's old tempo until the next audio block. Calling `SetBpm(60)` and then comping a range before that block can cut existing sections at the wrong source frames. Once the tempo command lands, the remaining clips play the wrong audio and can extend beyond the selected range. Use the same requested tempo for both the new section and the cuts.

3. **Moved clips highlight different audio** (`ArrangementView.Takes.cs`, P2). Moving a comp section keeps its `takeId`, but `DrawTakeLanes` highlights the section's new timeline range on the original take. Moving audio from beat 1 to beat 2 therefore highlights beat 2's waveform even though the clip still plays beat 1's source frames. The highlight points users at different audio from what they hear, so swiping it can select the wrong section. Only highlight sections that still match the take's position, or map the highlight through their source offset.

## Symptom

A loop recording that ends mid-pass plays a short fade to silence where the last pass meets the pass under it. Comping immediately after a tempo change carves neighbouring clips with the tempo that has not reached the transport yet, so those clips keep the wrong source frames. A comp clip that has been moved still lights up the take lane over its new timeline range, which is a different part of the take than the clip plays.

## Reproduction

1. Record three loop passes on an audio track and stop during the third pass. Render across the frame where the newest partial pass meets the previous pass (the default comp is two clips). The samples at that join fall to silence for about 3 ms on each side. The first sample of the comp still fades in, as a single clip always did.
2. Record the same three passes at 120 BPM, call `SetBpm(60)`, and `CompTake` a range of the first take before any render block. The new section's own frames follow the new mapping, but the neighbours are cut with 120 BPM's samples-per-beat, so their source offsets and lengths are wrong.
3. Move a comp section off the beat where its take placed it and open the take lanes. The lane paints the in-use highlight over the clip's new timeline range. Expected: no highlight there, because that range is not the audio the clip plays. Delete and flatten behaviour stay as they are (the clip keeps its `takeId`).

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Track.h` `audioClipEdgeGain` — ~3 ms linear fade at both ends of every unwarped clip.
- `src/native/nota.engine/src/engine/Engine_Render.cpp` `mixAudioClip` — applies that fade on the unwarped path. Two adjacent clips therefore dip at the join.
- `src/native/nota.engine/src/engine/Engine_Recording.cpp` `materialiseLoopTakes` — builds the default comp as newest-first coverage clips that meet at a frame boundary. It does not settle fades itself; the following republish is the right place.
- `src/native/nota.engine/src/engine/Engine_Takes.cpp` `flattenTakes` — bakes `audioClipEdgeGain` at every inner edge so flatten matches multi-clip playback. A fade change has to bake the same edges or the existing render comparison breaks.
- `src/native/nota.engine/src/engine/Engine_Tracks.cpp` `overwriteAudioClipsInRange` — calls `audioOverwriteRange` with `transport_.samplesPerBeat()`.
- `src/native/nota.engine/src/engine/Engine.cpp` `setBpm` — updates `authoringBpm_` immediately and says `transport_.samplesPerBeat()` is stale until the audio thread applies `SetBpm`. `republishWithTrackRaw` publishes the clone. `placeTakesForTempo` publishes through `publishRaw` and must not be settled by mutating a track still shared with the previous snapshot.
- `src/native/nota.engine/src/engine/Engine_Takes.cpp` `atTakePosition` — a section is still where its take put it when `startBeat` equals `originBeat + sourceOffsetFrames * beatsPerFrame`.
- `src/managed/Nota.App/ArrangementView.Takes.cs` `DrawTakeLanes` — highlights every active clip with a matching `TakeId` over `c.StartBeat`. `TakeVM` has no origin or beats-per-frame. `ClipVM` has `TakeId` but not `SourceOffsetFrames`. `ArrangementView.cs` `Refresh` already calls `TryGetAudioClipInfo`.

## Root Cause Hypothesis

Confidence: high. Each report matches the code. The dip is the ordinary per-clip edge fade applied to two clips that replaced one buffer. The tempo cut is `overwriteAudioClipsInRange` reading the transport clock while `compTake` and `placeTakesForTempo` already use `authoringBpm_`. The highlight treats `takeId` as "this timeline range is that take's audio", but a moved clip keeps the id and the source frames while its start beat changes.

## Proposed Remediation

**Preferred**:

1. Add `fadeHead` and `fadeTail` on `AudioClip`, default true, so ordinary clips and older in-memory snapshots keep today's fades. `audioClipEdgeGain` and the unwarped `mixAudioClip` path honour them. On the message thread, `settleTakeJoins` runs on the new track clone at the start of `republishWithTrackRaw`, and on each clone inside `placeTakesForTempo` before `publishRaw`. It sets both fades on, then turns off the shared edge of unwarped take clips in the same group whose beat ranges meet. Outer edges, gaps, other groups, and non-take clips keep both fades. Do not persist the flags and do not settle inside `publishRaw` (that graph can still share track pointers). `flattenTakes` bakes a fade only when the clip's flag is on and the edge is not the outer edge of the merged range.
2. `overwriteAudioClipsInRange` passes authoring samples-per-beat (`deviceRate * 60 / authoringBpm_`, falling back to the transport when that is unavailable) into `audioOverwriteRange`. No other `samplesPerBeat()` call changes.
3. `TakeVM` stores `OriginBeat` and `BeatsPerFrame` (`LengthBeats / LengthFrames`). `ClipVM` stores `SourceOffsetFrames` from `TryGetAudioClipInfo`. `DrawTakeLanes` skips the in-use paint unless `startBeat` is still `origin + sourceOffset * beatsPerFrame`.

**Alternatives**:

- Drop the edge fade for every butt-joined clip. That also changes ordinary splits, which are supposed to fade.
- Map the highlight through the source offset onto the take waveform. Correct visually, but the comment's first option (only sections still at the take position) matches `atTakePosition` and does not paint a moved clip as if it were still a comp section.

**Files likely to change**:

- `src/native/nota.engine/src/engine/Track.h`
- `src/native/nota.engine/src/engine/Engine.h`
- `src/native/nota.engine/src/engine/Engine.cpp`
- `src/native/nota.engine/src/engine/Engine_Takes.cpp`
- `src/native/nota.engine/src/engine/Engine_Tracks.cpp`
- `src/native/nota.engine/src/engine/Engine_Render.cpp`
- `src/managed/Nota.App/ArrangementView.Takes.cs`
- `src/managed/Nota.App/ArrangementView.Vms.cs`
- `src/managed/Nota.App/ArrangementView.cs`
- `tests/Nota.SmokeTest/TakeLaneTests.cs`
- `CHANGELOG.md`

**Tests to add or update**:

- After the three-pass default comp, render the loop and require the join frame (30000) and the frame before it to match the raw timeline, while frame 0 stays near silence.
- `SetBpm(60)` then `CompTake` on the first take over beats 0.5–1.0 before any render block. The new section is source frames 24000–48000; the left neighbour keeps frames 0–24000; the right neighbour starts at source frame 48000. Update the flatten comment that says every inner join keeps a fade.

The highlight is view-only. The smoke project does not reference `Nota.App`, so it is not pinned by `TakeLaneTests`. A before/after screenshot of a moved clip is the check.

## Risks & Considerations

- Settling a track that is still shared with the previous snapshot would change audio already playing. Settle only the clone about to be published.
- Suppressing fades for every butt join would change split clips. Limit it to `takeId != 0`, the same take group, and unwarped clips.
- Flags are derived at publish time, so project format stays v29. A snapshot published before this change defaults the new fields to true, and undoing back to it can dip again. Acceptable.
- `overwriteAudioClipsInRange` is also the recording-stop carve. Using the authoring tempo there matches a pending tempo change; other edit paths keep reading the transport.
- `placeTakesForTempo` must settle after it updates `beatsPerFrame`, so the meet test uses the mapping the clips were just moved onto.
- No new dependency. No audio-thread allocation: the flags are plain fields read while mixing, and settling runs on the message thread before publish.

## Open Questions

- None. The maintainer asked for these three comments in one follow-up, referencing #187 without reopening it.
