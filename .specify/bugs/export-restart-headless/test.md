# Bug Verification: Export and freeze restart audio that was never running

- **Slug**: export-restart-headless
- **Tested**: 2026-10-14
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The original reproduction — the smoke test on GitHub's Linux and Windows runners, which have
no audio output device — now runs to the end: export, stem export, bounce and freeze leave a
never-started engine stopped and nothing throws. No regressions on any of the three
platforms.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | CI run [38556208783](https://github.com/nota-daw/nota/actions/runs/38556208783), `scripts/build-linux.sh` / `build-win.ps1` on `ubuntu-latest` / `windows-2022`, commit `a6374a7` | pass | the same headless runners that crashed in run 38546277984 |
| New tests | 4 smoke checks "… leaves a backend that was never started stopped" (export, stems, bounce, freeze) | pass | on Linux, Windows and macOS in the run above |
| Regression suite | full smoke test, all three CI jobs | pass | `SMOKE TEST PASSED` ×3 |
| Regression suite (local) | `scripts/build.sh` on macOS after the merge | pass | `SMOKE TEST PASSED` |
| Lint / type-check | design-token lint inside the smoke test; `dotnet build` in CI | pass | build has 0 errors; warning count not compared |

## Output Excerpts

```
Linux    [PASS] export leaves a backend that was never started stopped
Linux    [PASS] stem export leaves a backend that was never started stopped
Linux    [PASS] bounce leaves a backend that was never started stopped
Linux    [PASS] freeze leaves a backend that was never started stopped
Linux    SMOKE TEST PASSED
Windows  [PASS] export leaves a backend that was never started stopped   (… same four)
Windows  SMOKE TEST PASSED
macOS    SMOKE TEST PASSED
```

Before the fix, the same Linux job logged `ALSA lib pcm.c:2721:(snd_pcm_open_noupdate)
Unknown PCM default` and died with `NotaEngineException: AudioDevice` from
`AudioExporter.Restore`. After it, no ALSA open attempt appears in the log: the engine no
longer tries to start a device it never had.

## Residual Risks

- The other half of the behaviour — an engine that *was* running restarts after an export —
  has no automated check (CI has no device; `--audiocheck` was not extended, see fix.md
  Deviations). The code path for it is unchanged from before the fix.
- A device that disappears while running and is gone when the export ends still throws from
  `Restore`; that is intended (a real device error) and surfaced by the app's export UI.
- `bg warp: plays identically to the synchronous rebuild` failed once locally during the fix
  (unrelated, intermittent); it passed on all three CI jobs here.

## Recommendation

Close the bug — verified end-to-end on the environments where it reproduced. Track the
intermittent `bg warp` check separately.
