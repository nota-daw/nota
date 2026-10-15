# Bug Verification: Linux — Nota's process CPU is well above other DAWs, also when idle

- **Slug**: linux-idle-cpu
- **Tested**: 2026-10-15
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

## Summary

On macOS, a headless run of the real `MainWindow` (12 tracks, stopped) shows the fix cuts the
UI-thread cost of the main tick plus a rendered frame by about 3× (≈2.0 → ≈0.6 ms per tick). The
cost also drops when minimized (≈1.4 → ≈0.45 ms). The full build and smoke test pass, including
the new pump-pacing checks. The original symptom (process CPU on Linux vs. other DAWs) was **not**
reproduced or re-measured: no Linux machine was available. That is why the result is `partial`,
not `verified`.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Reproduction on Linux (post-fix) | process CPU via `top`/`perf` on Linux x86_64 | not-run | no Linux machine in this session |
| Idle UI cost, before vs. after (macOS, headless proxy) | `/tmp/idlecpuharness` (fix) vs. the same harness on a `git worktree` of `HEAD` 097af5d (baseline), 3 runs each | pass | see excerpts; same native dylib on both sides (the fix has no native changes) |
| New tests (pump pacer) | `scripts/build.sh` → smoke test `-- plugin host pump --` | pass | 6 new checks + the existing pump no-op check |
| Regression suite | `scripts/build.sh` (native + managed + full smoke test, macOS arm64) | pass | `SMOKE TEST PASSED`, exit 0 (run during `/speckit-bug-fix`; sources unchanged since) |
| Build / type-check | `dotnet build Nota.sln` | pass | no new warnings in the changed files |
| Linux JUCE pump in practice (plugin editor opens/responds) | Linux, hosted VST3 with editor | not-run | needs Linux; pacing logic covered by unit checks only |
| Visual regressions (meters fall on stop, peak-hold click, loop toggle on ruler, theme switch) | manual in the app | not-run | GUI launch not performed; harness measures cost, not visuals |

## Output Excerpts

Harness: the real `MainWindow` + `MainWindowViewModel` from `App.Services`, Avalonia.Headless +
Skia (real drawing), 1600×1000, 4 template tracks + 8 instrument tracks with MIDI clips, transport
stopped. The VM's own 33 ms timer is stopped. Each step = reflected `OnTick()` + `RunJobs()` +
`CaptureRenderedFrame()`. After a 60-step warm-up, 600 steps are timed per state. UI-thread
wall time per step:

| Run | Baseline shown | Fix shown | Baseline minimized | Fix minimized |
|-----|----------------|-----------|--------------------|---------------|
| 1 | 2.156 ms | 0.718 ms | 1.494 ms | 0.530 ms |
| 2 | 1.958 ms | 0.667 ms | 1.341 ms | 0.433 ms |
| 3 | 1.915 ms | 0.516 ms | 1.320 ms | 0.421 ms |

```
-- plugin host pump --
  [PASS] plugin host pump: exported, non-blocking no-op with no plugin loaded
  [PASS] pump pacer starts at the fast rate
  [PASS] a short run of empty pumps stays fast
  [PASS] a long run of empty pumps backs off to the slow rate
  [PASS] an idle pump stays slow
SMOKE TEST PASSED
```

## Residual Risks

- **Not measured on Linux.** The ~3× drop is a macOS headless proxy for UI-thread work. It
  doesn't show how much of the reported 35–45 % process CPU goes away. If the Linux cost is
  dominated by the render backend (software Skia) or by DSP, the gain may be smaller.
- The headless renderer doesn't model the real compositor or vsync; on a real window, the
  savings from fewer invalidations may be larger or smaller.
- Visual correctness of the early returns (meters, ruler/overlay after loop/theme/resize
  changes) was reasoned through in code but not checked on screen.
- Linux pump back-off is checked only as logic. Plugin editor responsiveness on Linux (the
  `linux-plugin-black-window` regression) is unverified.
- The playing case was not measured. When playing, the playhead moves every tick, so savings
  come only from meters and the CPU read-out.

## Recommendation

Hold the issue open. The fix measurably reduces idle UI work and nothing regressed in the smoke
test, so it is fine to merge. But #55 is about Linux process CPU, and that wasn't measured. Before
closing: on Linux x86_64, compare process CPU (stopped / playing / minimized) on `develop` vs. this
change, and note the Avalonia render backend in use. Also open a hosted plugin's editor to confirm
the paced JUCE pump still responds at once. Quickly check the meters and loop ruler on screen.
