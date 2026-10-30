# Bug Verification: Session clip editor — Clip tab, selection follow, playhead (#334)

- **Slug**: session-clip-editor
- **Tested**: 2026-10-30
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

Items 1–4 no longer reproduce on Linux: a click opens the slot in the Clip tab, the Clip
button does the same, the editor follows another clip, and the playhead moves while that
slot plays. Items 5–9 were not changed.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Click a filled slot | App, Session, single click on the slot body | pass | Detail panel shows the piano roll; before, it stayed on Devices |
| Clip button | Click Clip with a filled slot selected | pass | Opens that slot. Before, it stayed on Devices |
| Another slot, same track | Click scene 2 while scene 1's editor is open | pass | Header and notes follow |
| Another track | Click a filled slot on a second instrument | pass | Header changes to that track |
| Playhead, editor already open | Launch the slot (PulseAudio null sink) | pass | Accent line moves across the roll |
| Playhead, open while playing | Launch, then click the slot | pass | Line is at the slot position immediately |
| Empty slot, other track | Click the centre of an empty slot | pass (unchanged) | Centre click is the record/stop target and does not move the selection, so the editor stays. Selecting an empty slot still shows Devices |
| Smoke test | `dotnet run --project tests/Nota.SmokeTest -c Release` | fail (pre-existing) | One render-pool timing guard, same failure on unmodified develop |
| `--perf` alone | `Nota.SmokeTest.dll --perf` | pass | Same engine as develop (no native change). The full-run failure above does not reproduce on its own |

## Output Excerpts

Develop (`scripts/build-linux.sh`) and this branch (smoke test) both end with:

```
[FAIL] overloaded: at most N late blocks within 50 ms (<= 4: no bursts when the pool re-checks the load)
SMOKE TEST FAILED (1)
```

N was 9 on develop and 5 on this branch, both while the machine was busy. No other check failed.
Alone, `Nota.SmokeTest.dll --perf` printed `PERF PASSED`.

## Residual Risks

- macOS and Windows were not run here. CI covers them.
- A Drum Rack Pattern grid was not exercised in the GUI pass; the code keeps Pattern when the
  panel is already on it and the new slot is a Drum Rack clip.
- Items 5–9 remain.

## Recommendation

Do not close #334. The Session navigation and playhead items are fixed; the loop, envelope,
waveform and Texture items are waiting on product decisions.
