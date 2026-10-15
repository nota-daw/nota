# Bug Verification: CPU meter shows DSP load only, far below the OS process figure

- **Slug**: cpu-meter-dsp-only
- **Tested**: 2026-10-15
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

On macOS arm64 the fix holds. The meter now reads "DSP", and the tooltip and MCP report the
process CPU. Those figures agree with an independent measurement and roughly with `top`. Before
the fix, the only figure was the 3–4 % DSP load labelled "CPU". The full smoke suite passes. The
result is **partial** because the bug was reported on Linux and nothing was run on Linux or Windows.
The tooltip popup itself was also not seen rendered: its text was read programmatically.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| New tests | `dotnet run --project tests/Nota.SmokeTest -- --cpu` | pass | 12/12, CPU PASSED; busy loop 106.7 % of one core (macOS Arm64, 14 CPUs) |
| Reproduction (post-fix), app UI | headless harness `/tmp/cpumeterharness`: real `MainWindow`, VM `OnTick` driven for 12 s | pass | label `DSP 4%`; tooltip refreshed 13× in 12 s; tooltip process CPU 32 % vs. independent `Process.TotalProcessorTime` over the last 3 s: 32 % |
| Reproduction (post-fix), vs. OS monitor | same harness runs `top -l 2 -s 2 -pid <self>` while ticking | pass (approx.) | `top` 25.9 %, sampler 33 %. The windows differ (top: 2 s sample; sampler: 2 s EMA that also covers the moment before top's window). Same order of magnitude, unlike the 4 % DSP figure |
| MCP path | harness calls `SettingsTools.GetCpuLoad()` with the shared sampler | pass | `DspLoad=0.035 ProcessCpu=32.4 LogicalCpus=14`: a recent window, not a lifetime average |
| Transport layout | harness capture of the full window | pass | "DSP 3%" in the same slot, no shift |
| Regression suite | `scripts/build.sh` (native + managed build + full smoke test) | pass | exit 0, SMOKE TEST PASSED, 3414 checks. Last run at 17:05 on the final fix code; no fix file changed since |
| Linux x86_64 (reporter's platform) | — | not-run | no Linux host in this session |
| Windows (whole-machine scale) | — | not-run | no Windows host; the scale is covered only by the pure-function test |
| Tooltip visual | — | not-run | popups don't render headless; text read via `ToolTip.GetTip` |

## Output Excerpts

```
-- process cpu --
  [PASS] a one-core busy loop reads about 100 % of one core on macOS Arm64 (got 106,7 %, 14 CPUs)
CPU PASSED
```

```
independent, last 3 s: 32 % of one core
label: DSP 4%
tip:   DSP load: 4 % of the audio buffer time — dropouts start near 100 %
       Nota process CPU: 32 % of one core (audio, interface, plugins)
mcp get_cpu_load: DspLoad=0.035 ProcessCpu=32.4 LogicalCpus=14
top (2nd sample): 66785  25.9
```

```
SMOKE TEST PASSED
==> Done.
```

## Residual Risks

- **Linux**: the reporter's platform. `Environment.CpuUsage` reads `getrusage` there, the same call as on
  macOS, so the risk is low, but this is unverified. The real-clock test will run on the Linux CI job.
- **Windows**: the percent-of-all-CPUs scale and `GetProcessTimes` are unverified on a real host.
  Compare with Task Manager → Details. Note that Windows 11 Task Manager may show "CPU utility",
  which is frequency-scaled, so small differences are expected.
- **Headless timing**: harness ticks are driven manually; the real app's tick cadence is not exercised here.
- **Unrelated working-tree changes**: `src/managed/Nota.Remote/wwwroot/index.html` and its hashed
  `assets/*` were rebuilt at 17:10, after the last build. This was not done by this fix, most likely
  by the docs screenshot harness. Review or revert them before committing so they don't ride along.

## Recommendation

Merge after review. Close #54 once the Linux CI run is green (the real-clock test passes there).
Ideally also do a quick manual comparison with GNOME System Monitor and with Task Manager →
Details on Windows. On macOS the fix is verified end-to-end.
