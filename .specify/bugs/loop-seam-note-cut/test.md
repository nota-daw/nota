# Bug Verification: Held notes and release tails cut at the loop seam; notes recorded across it shrink

- **Slug**: loop-seam-note-cut
- **Tested**: 2026-10-16
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The assessment's reproduction steps 3–5 were run through the engine (offline render, the same
`processBlock` path the audio callback uses). Without the fix the symptoms reproduce. With it they
are gone on Synth, Volt, Keys and Operator, across one and several wraps, while recording live and
routed MIDI, and with the Arp (every retrigger mode) and Note Length on the track. The full build and
smoke suite pass with no regressions. Nobody listened in the running app, and no hosted VST/AU
instrument was tested.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (pre-fix) | Engine sources reverted, rebuilt, smoke run | fail (as expected) | 5 of 6 seam tests FAIL: held key `0.0288 → 0.0000`, tail `0.0000`, arp `0.0222 → 0.0000`, recorded `3.51+0.10` (the stub from the report). Run during `/speckit-bug-fix`. |
| Reproduction (post-fix), steps 3–5 | `tests/Nota.SmokeTest/LoopSeamTests.cs` (Synth) | pass | See excerpts. |
| Reproduction (post-fix), on other instruments | Throwaway harness `/tmp/seam-verify` (outside the repo): Volt, Keys, Operator | pass | Held key across the seam on all three. Volt release tail across the seam matches a no-loop baseline exactly. |
| Edge cases | Same harness | pass | A key held across 3 wraps keeps sounding, and Stop still silences it (hard flush kept). Arp with Retrig Off / Note / Beat carries on across the seam with nothing hanging after release. Note Length FX: nothing hangs. A key recorded across one seam gives 2 notes. |
| Full build + regression suite | `scripts/build.sh` | pass | Universal `x86_64 arm64` dylib, managed build, `SMOKE TEST PASSED`, 3483 PASS / 0 FAIL, including the existing `loop seam declicked` check. |
| Lint / type-check | — | not-run | The project has no separate lint step. The C++ and C# compiles in `build.sh` passed. |
| In-app listening (Windows, the reporter's platform) | — | not-run | Needs a person at the keyboard. Engine-level checks cover the same code path. |
| Hosted VST/AU instrument | — | not-run | No plugin available headless. Plugins now get per-note offs at the seam instead of CC123. |

## Output Excerpts

`scripts/build.sh`:
```
Architectures in the fat file: src/native/nota.engine/build/libnota_engine.dylib are: x86_64 arm64
-- loop seam --
  [PASS] loop seam: a held key keeps sounding after the wrap (rms 0,0288 → 0,0288, pos 0,27)
  [PASS] loop seam: a release tail rings on after the wrap (rms 0,0247, pos 0,01)
  [PASS] loop seam: a clip note crossing the loop end releases, no hanging note (rms 0,0229 → 0,000000)
  [PASS] loop seam: the arpeggiator carries on with a key held across the wrap (rms 0,0222 → 0,0220)
  [PASS] loop seam: a note recorded across the wrap is split at the loop end (0,00+0,51, 3,51+0,49)
  [PASS] loop seam: a routed note recorded across the wrap is split at the loop end (0,00+0,51, 3,51+0,49)
SMOKE TEST PASSED
```

Harness (`/tmp/seam-verify`):
```
PASS held key across seam [Volt]: rms 0,0628 -> 0,0377, pos 0,22
PASS held key across seam [Keys]: rms 0,1609 -> 0,1367, pos 0,22
PASS held key across seam [Operator]: rms 0,0799 -> 0,0768, pos 0,22
PASS release tail across seam [Volt]: rms at end 0,0544, just after wrap 0,04866, pos 0,04
PASS held key across 3 wraps, stop flushes [Volt]: rms after 3 wraps 0,0587, after stop 0,000000
PASS arp retrig=0 across seam: rms 0,0567 -> 0,0654, after release 0,000000
PASS arp retrig=1 across seam: rms 0,0665 -> 0,0654, after release 0,000000
PASS arp retrig=2 across seam: rms 0,0567 -> 0,0654, after release 0,000000
PASS note length FX across seam: rms 0,0492 -> 0,0000, after release+4.5 beats 0,000000
PASS record across seam [Volt]: 64: [0,00+0,51, 3,51+0,49]  67: [0,00+4,00, 0,00+2,52, 1,51+2,49]
INFO volt tail loop=False release=0,8: rms 45-100 ms after note-off 0,04866
INFO volt tail loop=True  release=0,8: rms 45-100 ms after note-off 0,04866
```

Notes on reading the harness:
- The first tail run measured 0 because Volt's default amp release is under ~45 ms, and the no-loop baseline also measures 0. With the release raised (param 39), the looped and unlooped tails are identical.
- In the record case, key 67 was held from 1.5 for 9 beats, so it crossed two seams. It came out as 3 notes, two of them stacked at 0.0. That is overdub, which is out of scope here (the Overdub/Replace feature).
- Note Length's "after" is 0 because its default length ends the note before the seam. The check is that nothing hangs.

## Residual Risks

- Not listened to in the app, and not on Windows (the reporter's platform). The engine path is platform-independent.
- Hosted VST/AU instruments were not exercised. They now get individual note-offs at the seam instead of CC123.
- Drum one-shots and long tails now ring into the next pass rather than being cut. This is intended, but it changes the sound of existing loops.
- A key and a clip note on the same pitch: the clip note's seam off also releases the live voice. This matches a normal clip note-off.

## Recommendation

Close the engine side of nota-daw/nota#29 (the cut notes and tails, and the shrunken recorded
note) once someone has listened in the app with a long pad and an Arp on a 1-bar loop. The
issue's other half, the Overdub/Replace toggle, stays open as a separate feature.
