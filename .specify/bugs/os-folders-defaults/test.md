# Bug Verification: OS Documents/Music locations for default folders

- **Slug**: os-folders-defaults
- **Tested**: 2026-11-05
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified

## Summary

The original symptom no longer reproduces through the production path, the 13 new checks pass, and the neighbouring Linux-desktop suite shows no regression.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix, XDG hit) | harness printing `UserDirs` with `HOME=/tmp/opencode/fakehome`, `user-dirs.dirs` → `Dokumenty`/`Muzyka` | pass | `DOC=/tmp/opencode/fakehome/Dokumenty` (old code gave `…/Documents`) |
| Reproduction (post-fix, fallback) | same harness with empty home, no `user-dirs.dirs` | pass | `DOC=…/Documents`, `MUS=…/Music` as designed |
| New / updated tests | `dotnet run --project tests/Nota.SmokeTest -- --user-dirs` | pass | 13/13, `USER DIRS PASSED` |
| Regression suite | `dotnet run --project tests/Nota.SmokeTest -- --linux-desktop` | pass | `LINUX DESKTOP PASSED`, engine starts |
| Lint / type-check | `dotnet build` of touched projects (via the runs above) | pass | only pre-existing nullable warning in `LanguageCatalog.cs` |
| Hardcode sweep | grep `"Documents"` / `"Music", "Nota` in source | pass | only fallback names in `UserDirs.cs` + tests remain |
| Full gate | `scripts/build-linux.sh` | fail (2, unrelated) | only `RenderPoolTests` overload timing checks fail (see below); everything else green |

## Output Excerpts

```
HOME=/tmp/opencode/fakehome
DOC=/tmp/opencode/fakehome/Dokumenty
MUS=/tmp/opencode/fakehome/Muzyka
...
HOME=/tmp/opencode/emptyhome
DOC=/tmp/opencode/emptyhome/Documents
MUS=/tmp/opencode/emptyhome/Music
USER DIRS PASSED
LINUX DESKTOP PASSED
```

## Residual Risks

- Full `scripts/build-linux.sh` run 2026-11-05: `SMOKE TEST FAILED (2)` — both are
  `RenderPoolTests` overload timing checks (`100 of 4125 blocks late after warm-up`,
  `at most 6 late blocks within 50 ms`). They fail consistently on this machine
  independent of this change (engine render-pool timing, no settings/UI path involved;
  same failures documented before in `.specify/bugs/render-pool-review-232/test.md` and
  `.specify/bugs/dsp-perf-regressions/test.md`). Owner's call: ignored for this PR, to be
  fixed separately.
- Windows/macOS Known-Folder branch covered by injected-path unit checks only, not by a real redirected-folder machine.

## Recommendation

Close the bug — verified end-to-end. Open the PR against `develop` with `Closes #522` after the full build passes.
