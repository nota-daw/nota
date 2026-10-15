# Bug Fix: CPU meter shows DSP load only, far below the OS process figure

- **Slug**: cpu-meter-dsp-only
- **Fixed**: 2026-10-15
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The transport meter is now labelled **DSP** (the bar still tracks DSP load). Its tooltip adds
the whole process's CPU over a recent window. Windows shows it as a percent of all CPUs, like
Task Manager; macOS and Linux show it as a percent of one core, like Activity Monitor,
`top` and GNOME System Monitor. The transport and MCP `get_cpu_load` share one sampler,
so MCP no longer reports a lifetime average.

Maintainer decisions applied (2026-10-15): platform-native normalization; the bar stays DSP.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.Application/Ports/IProcessCpu.cs` | added | `IProcessCpu` port (`CorePercent()`, `LogicalCpus`) + `ProcessCpuScale` (platform display scale, unit text) |
| `src/managed/Nota.Infrastructure/Logging/ProcessCpuSampler.cs` | added | Δ`Environment.CpuUsage.TotalTime` ÷ Δwall, 0.5 s step, 2 s time-based EMA; injectable clocks; null on failure |
| `src/managed/Nota.App/App.axaml.cs` | modified | registers `IProcessCpu` singleton |
| `src/managed/Nota.App/MainWindow.axaml` | modified | `CPU 0%` → `DSP 0%`; transparent background so the tooltip hit-tests the whole meter |
| `src/managed/Nota.App/MainWindow.axaml.cs` | modified | label `DSP N%`; `SetUpCpuTip` builds the two-line tooltip, refreshed from the transport tick at ≤ 1 Hz |
| `src/managed/Nota.App/DeviceCards/Instruments/{Sampler,Rhythm,Mosaic,Pendulum}InstrumentCard.cs` | modified | meta line `CPU x %` → `DSP x %` (these show the global engine load) |
| `src/managed/Nota.Mcp/Tools/SettingsTools.cs` | modified | `get_cpu_load` uses the shared sampler: `ProcessCpu` = recent window, % of one core on every OS, `double?` (null while measuring), new `LogicalCpus` |
| `src/managed/Nota.App/Mcp/McpAppServices.cs` | modified | shares `IProcessCpu` with the MCP container |
| `tests/Nota.SmokeTest/ProcessCpuTests.cs` | added test | 12 checks, see below |
| `tests/Nota.SmokeTest/Program.cs` | modified | runs `ProcessCpuTests` in the full run and alone via `--cpu` |
| `CHANGELOG.md` | modified | [Unreleased] → Fixed entry |

## Diff Highlights

```csharp
// ProcessCpuSampler.CorePercent()
double instant = Math.Max(0, (cpu - _lastCpu).TotalSeconds) / dWall.TotalSeconds * 100.0;
double alpha = 1.0 - Math.Exp(-dWall.TotalSeconds / Smoothing.TotalSeconds);
_value = _value is { } prev ? prev + (instant - prev) * alpha : instant;

// ProcessCpuScale
public static bool WholeMachine => OperatingSystem.IsWindows();
public static double ToDisplay(double corePercent, int logicalCpus, bool wholeMachine) =>
    wholeMachine ? corePercent / Math.Max(1, logicalCpus) : corePercent;
```

Tooltip text (macOS, as captured):

```
DSP load: 3 % of the audio buffer time — dropouts start near 100 %
Nota process CPU: 32 % of one core (audio, interface, plugins)
```

## Tests Added or Updated

`tests/Nota.SmokeTest/ProcessCpuTests.cs` (`-- process cpu --`, `--cpu`):
- Injected clocks: null before the first window; 0.5 s CPU / 1 s wall = 50 %; reads within the step
  return the cached value; a jump to 200 % is smoothed; after 18 s idle the reading is near 0
  (recent window, not a lifetime average); a backwards counter never reads negative; a throwing OS
  reading gives null and no exception.
- Scale: Windows 400 % on 8 CPUs → 50 %; macOS/Linux 400 % → 400 %; zero CPU count is guarded;
  `WholeMachine` is true only on Windows.
- Real clocks: one thread busy for 1.5 s reads 60 %…(100 × CPUs + 50) % of one core. This runs on
  every CI platform and would catch wrong units from the OS counter.

## Local Verification

- `dotnet run --project tests/Nota.SmokeTest -- --cpu` → CPU PASSED (12/12; busy loop 105.8 % on macOS arm64).
- `scripts/build.sh` (twice, the second after the tooltip refactor) → exit 0, SMOKE TEST PASSED, 3414 checks;
  busy loop 114–115 % of one core in the full run.
- Headless harness `/tmp/cpumeterharness` (real MainWindow + VM ticks driven by reflection, 12 s):
  label `DSP 3%`, the tooltip refreshed 11× in 12 s (about 1 Hz), and the tooltip figure was 32 % vs. 31 % from an independent
  `Process.TotalProcessorTime` measurement over the last 3 s. The transport capture shows "DSP 3%" in
  the same slot with no layout shift.
- **Not verified**: Linux and Windows runs (only macOS arm64 here), and a tooltip render (popups do not
  render headless; the text was read from `ToolTip.GetTip`).

## Deviations from Assessment

- **API**: `Environment.CpuUsage` (.NET 9+) replaces `Process.TotalProcessorTime`. On Windows it
  calls `GetProcessTimes`, on Linux and macOS `getrusage(RUSAGE_SELF)`. It avoids the per-read
  `Process` allocation and the Apple Silicon Mach-tick units risk the assessment flagged. The real-clock
  test pins the units on each platform.
- **Refresh source**: the assessment proposed a dedicated 1 Hz `DispatcherTimer`. The tooltip now
  refreshes from the existing transport tick (`PlayheadUpdated`), throttled to 1 Hz with a
  `Stopwatch`. This is one fewer timer, and it means the tooltip is refreshed exactly when the meter is.
  The harness showed the separate timer never firing headless, which hid the refresh path from testing.
- **MCP `ProcessCpu`** became `double?` (null while the first window is measuring) and gained
  `LogicalCpus`. The field names are kept.
- **Optional items not done**: no process figure was added to Preferences → Audio "CPU check". Its caption
  already says "audio-thread load".

## Merge with #66 (Less CPU while idle)

On top of #66 the meter keeps #66's redraw-only-on-change (`pct != cpuShown`), with the
label changed to DSP. The tooltip is re-set only when its text changes, and it is skipped
with the rest of the tick while the window is minimized. After the merge: `scripts/build.sh` →
SMOKE TEST PASSED (3420 checks). The harness shows the tooltip at 22 % vs. an independent 20 %.

## Follow-ups

- Run `scripts/build-linux.sh` / Windows sibling (or CI) so the real-clock test runs on Linux x86_64
  and Windows. Manually compare the tooltip with GNOME System Monitor / Task Manager → Details.
- 18 effect/instrument cards (Consort, Pentad, Compressor, Reverb, …) still say `CPU x %` for their
  **own** per-device audio-thread share. That figure is honest, but the same kind of number is
  now called "DSP" on the transport. Decide on one term across cards.
- Docs: transport/meter description in `nota-docs` (EN + RU) via the `nota-docs` skill, and the MCP
  tool reference if it lists `get_cpu_load` fields.
- Actual process CPU cost is tracked in #55.
