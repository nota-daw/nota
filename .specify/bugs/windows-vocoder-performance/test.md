# Bug Verification — Windows vocoder performance

- **Slug** — `windows-vocoder-performance`
- **Tested** — 2026-11-26
- **Assessment** — [assessment.md](assessment.md)
- **Fix** — [fix.md](fix.md)
- **Result** — Partial

## Summary

Nota Vocoder passed all 41 focused checks on macOS. Its 32-band render took 32.0 ms versus Chamber's 68.2 ms. The standalone device benchmark improved by 11.5% on ARM64 and 10.0% on x86-64 via Rosetta. The Windows GitHub Actions result for the patch is still pending.

## Checks Performed

| Check | Command or action | Result | Notes |
| --- | --- | --- | --- |
| Windows reproduction | `pwsh scripts/build-win.ps1` on Windows x64 | Not run | Local host is macOS. The issue records two failing Windows runs of the baseline. |
| Final vocoder tests | `dotnet run --project tests/Nota.SmokeTest -c Release -- --vocoder` | Pass | Rebuilt with the final test fixture. Includes the 32-band timing gate, the retuning assertion, and all existing vocoder checks. |
| Before and after comparison | Clang Release standalone device benchmark and binary comparison | Pass | 11 three-second runs per build and architecture. Audio and telemetry matched bit for bit through automation, carrier transitions, and sample-rate resets. |
| macOS regression build | `CI=true scripts/build.sh` | Pass | Complete smoke and plug-in scanner checks. The new retuning check used the same assertion in the shared shaping fixture during this run. The final focused run checked it in isolation. |
| Default macOS regression build | `scripts/build.sh` | Fail | The only failure was the known RenderPool late-block burst check using Reverb, with 11 late blocks against a limit of 4. [Prior verification](../dsp-perf-regressions/test.md) |
| Patch formatting | `git diff --check` | Pass | No whitespace errors. |

## Output Excerpts

```text
[PASS] retuning the 32-band bank updates its centres (1010,9 … 1978,5 Hz)
[PASS] 32-band vocoder costs ≤ Nota Chamber (32,0 ms vs 68,2 ms per 3 s)
VOCODER PASSED
```

## Residual Risks

The x86-64 benchmark uses the macOS toolchain and Rosetta. Windows x64 with the job's MSVC build may produce a different timing margin.

## Recommendation

The Windows GitHub Actions smoke run is the acceptance check for issue #1117.
