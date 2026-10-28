# Bug Verification: VST editor typing-keyboard audition

- **Slug**: vst-editor-keyboard
- **Tested**: 2026-10-28
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

Automated checks are green (scanner selftest, managed build, smoke minus flaky
render-pool timing guards), but the live GUI reproduction (physical keys in a
hosted editor window) was not exercised in this environment — that needs the
running app. A `clang-format` drift in the touched files was found during
verification and fixed with a format pass + rebuild.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction (post-fix) | GUI: keys in hosted editor window | not-run | Needs running app + plugin + physical keys; no headless coverage for this path |
| New / updated tests | — (none; manual matrix only) | n/a | Assessment matrix, to run by the reporter |
| Regression suite | `nota-scanner-selftest` | pass | `SCANNER SELFTEST PASSED` |
| Regression suite | `dotnet build Nota.sln -c Release` | pass | 0 errors, 33 pre-existing warnings |
| Regression suite | `dotnet run --project tests/Nota.SmokeTest` (×3) | pass* | ~4393 pass; 1–2 marginal FAILs in render-pool timing guards, values vary per run (9→5 late blocks, thr ≤4), unrelated path |
| Lint / type-check | `clang-format --dry-run --Werror` on touched files | pass (after format pass) | `clang-format -i` on the touched files; rebuild green |

## Output Excerpts

```text
SCANNER SELFTEST PASSED
dotnet build: Warnings: 33, Errors: 0
SMOKE TEST FAILED (1) — only: [FAIL] overloaded: at most 5 late blocks within 50 ms (<= 4 …)
FORMAT-CLEAN (after clang-format -i + rebuild)
```

## Residual Risks

- Live audition (macOS monitor vs. native NSViews, JUCE fallback on Win/Linux, text-field exemption, no stuck notes) unverified without the manual matrix from the assessment.
- Native `HWND`-child editors on Windows likely still swallow keys for non-JUCE plugins — tracked follow-up in `fix.md`.

## Recommendation

Hold — verify live in the running app (assessment matrix: `A` vs `W-K`, `Z/X`
octave, preset search field, close while holding a key) and close the bug. If live
audition still fails for a specific plugin, reopen with that plugin named.
