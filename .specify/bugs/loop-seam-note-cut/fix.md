# Bug Fix: Held notes and release tails cut at the loop seam; notes recorded across it shrink

- **Slug**: loop-seam-note-cut
- **Fixed**: 2026-10-16
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The loop wrap no longer hard-flushes every Arrangement instrument (`allNotesOff` + MIDI FX `reset`).
In the first segment after the wrap, the engine sends a note-off for each clip note that was still
sounding at the loop end, so live-held keys keep sounding and instruments play their release. MIDI FX
with transport-beat state (Arp, Note Length) shift that state by −loop length, so they carry on
across the seam. While recording into the Arrangement, a note held across the seam (live or routed)
is split into "start → loop end" and "loop start → release".

## Decisions (user, 2026-10-16 — resolving the assessment's Open Questions)

- A note held across the seam while recording is **split into two** notes.
- The arpeggiator **continues its pattern from the same position** across the seam (no restart, held keys kept).
- Stop and seek **keep the hard flush**. Tails on stop are out of scope.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/Engine_Render.cpp` | modified | Wrap: no `flushArrangementVoices()`. It records the pre-wrap playhead (`seamEndSamples_`), calls `onLoopWrap()` and sets `seamOffs_` for the next segment. `gatherInstrumentNotes` emits offset-0 note-offs for clip notes with `onS < seamEnd ≤ offS`. New `Engine::onLoopWrap()` handles MIDI FX `timeJump` and the recording split. |
| `src/native/nota.engine/src/Engine.h` | modified | `seamOffs_`, `seamEndSamples_`, `onLoopWrap()` declaration. |
| `src/native/nota.engine/src/MidiDevice.h` | modified | New `virtual void timeJump(double deltaBeats)` (default no-op). The `reset()` comment no longer lists the loop wrap. |
| `src/native/nota.engine/src/Arpeggiator.h` | modified | `timeJump`: shifts `arpBeat_`, `phaseRef_`, and the pending on/off beats. |
| `src/native/nota.engine/src/MidiNoteLength.h` | modified | `timeJump`: shifts `intBeat_`, the pending offs, the sounding and held note beats. |
| `tests/Nota.SmokeTest/LoopSeamTests.cs` | added test | Six seam cases (see below). |
| `tests/Nota.SmokeTest/Program.cs` | modified | Runs `LoopSeamTests` in the full run and under `--live-record`. |
| `CHANGELOG.md` | modified | `[Unreleased] → Fixed` entry. |

## Diff Highlights

```cpp
// processBlock, at the wrap
seamEndSamples_ = transport_.playheadSamples();
transport_.wrapToLoopStart();
if (spb > 0.0) onLoopWrap(g, transport_.loopStartSamples() / spb, seamEndSamples_ / spb);
seamOffs_ = true;
chase = true;

// gatherInstrumentNotes: release what was still sounding at the loop end (offs sort before chase ons)
if (seamOffs_ && onS < seamEndSamples_ && offS >= seamEndSamples_ && n < 1024)
    evs[n++] = {0, false, note.pitch, 0.0f};

// onLoopWrap: split a note held across the seam while recording
const double head = loopEndBeat - start;
if (head >= kMinHead) recorded_.push(RecordedNote{pitch, start, head, vel});
start = loopStartBeat;
```

## Tests Added or Updated

`tests/Nota.SmokeTest/LoopSeamTests.cs` (1-bar loop 0–4 at 120 BPM, offline render):
- `HeldKey`: a live key held across the wrap keeps its level after the wrap.
- `ReleaseTail`: a clip note ending at 3.9 on a long-release Synth is still audible right after the wrap.
- `ClipNoteAcrossEnd`: a clip note from 3.0 to 6.0 releases at the seam, and the next pass is silent (no hanging note). This is the regression guard for removing the flush.
- `Arpeggiator`: an Arp on the track keeps playing a key held across the wrap.
- `RecordAcrossSeam`: a key held from 3.5 to 0.5 of the next pass is stored as `3.5+0.5` and `0.0+0.5`.
- `RoutedAcrossSeam`: the same for MIDI routed in from another track.

## Local Verification

- `cmake --build src/native/nota.engine/build` and `dotnet build tests/Nota.SmokeTest -c Release` → OK.
- **Without the fix** (engine sources reverted, rebuilt): five of the six seam tests FAIL, while `ClipNoteAcrossEnd` passes. The failures: held key `0.0288 → 0.0000`, tail `0.0000`, arp `0.0222 → 0.0000`, and a recorded note of `3.51+0.10` (the stub from the report).
- **With the fix**: `dotnet run --project tests/Nota.SmokeTest -c Release` → `SMOKE TEST PASSED`, 3483 PASS / 0 FAIL, all six seam tests pass, including the existing `loop seam declicked` check.
- The full `scripts/build.sh` (universal arm64+x86_64 build) was not run. Only the existing native build directory and the smoke test were.
- Not checked by ear in the app, and not with hosted VST/AU instruments.

## Deviations from Assessment

- **Scope expansion: MIDI FX `timeJump`.** The assessment proposed dropping `md->reset()` on the wrap. While implementing, two problems turned up. The Arpeggiator and Note Length keep pattern phase and pending offs in absolute transport beats. Without a shift, pending events would wait a whole pass, and a Note-retriggered arp would go silent after the wrap (`k < 0`). So I added `MidiDevice::timeJump(deltaBeats)`, implemented in `Arpeggiator.h` and `MidiNoteLength.h`. Chord, Scale, Random and Velocity run on their own sample clock or are stateless, so they only needed to stop being reset.
- **Short heads are dropped.** A key pressed less than 1/64 beat before the seam (the block-quantisation sliver, or a player anticipating the downbeat) is not split. The whole note moves to the loop start instead.
- **No change to the 0.1-beat fallback** in `collectRecorded`. With the split in place, Arrangement-loop notes no longer reach it with a negative length, so the extra clamp from the assessment wasn't needed.

## Follow-ups

- Spot-check with a hosted VST/AU instrument. Plugins now get individual note-offs at the seam instead of CC123.
- Drum one-shots (Rhythm, Drum Rack) are no longer cut at the seam, so their tails ring into the next pass. This is intended, but worth a listen.
- The Overdub/Replace loop-record toggle from #29 is a separate feature (`/speckit-specify`; proposed as a task card).
- User docs: no page describes the old cut behaviour, so I left nota-docs alone. Re-check during `/release` audit.
