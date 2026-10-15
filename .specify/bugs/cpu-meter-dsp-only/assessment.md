# Bug Assessment: CPU meter shows DSP load only, far below the OS process figure

- **Slug**: cpu-meter-dsp-only
- **Created**: 2026-10-15
- **Source**: https://github.com/nota-daw/nota/issues/54 (host: `github.com`, policy: allowlisted)
- **Verdict**: valid
- **Severity**: low

## Report (summarized)

**#54 — "CPU meter shows 5–10 % while the system monitor shows 35–45 % for Nota"** (Linux x86_64,
version not reported, reported by a user via @ambertape).

The transport CPU indicator reads 5–10 % during playback; the OS monitor (`top`, GNOME System
Monitor) shows the Nota process at 35–45 %. In two other DAWs the internal meter roughly matched
the system monitor. The issue already identifies the cause (the meter shows `Engine.CpuLoad`,
the audio-callback DSP load) and suggests labelling the meter honestly and also showing the
process CPU as a recent-window average. The fact that Nota's process CPU is high in absolute
terms is tracked separately in #55 and is **out of scope** here.

Additional requirement from the maintainer: the fix must behave correctly on **macOS, Linux and
Windows**.

## Symptom

The only CPU readout in the app is labelled plainly "CPU N%" but measures audio-thread render
time ÷ block duration. It excludes the UI thread, Avalonia rendering, timers, plugin editors and
everything else in the process, so it is several times lower than any OS monitor. Expected: the
meter says what it measures, and the process-wide CPU is visible in the app in a form that can be
compared against the platform's own monitor.

## Reproduction

1. Open a project with several tracks/devices and press Play.
2. Read the transport meter ("CPU N%", right of the time signature).
3. Compare with the OS monitor for the Nota process: `top`/`htop`/GNOME System Monitor (Linux),
   Activity Monitor (macOS), Task Manager → Details (Windows).
4. Observed: meter ≈ 5–10 %, OS ≈ 35–45 % (Linux report). Not yet measured on macOS / Windows —
   the gap is expected there too, since the cause is platform-independent.

## Suspected Code Paths

- `src/native/nota.engine/src/Engine_Render.cpp:327-345` — `Engine::render` times `processBlock`
  with `steady_clock` and stores `elapsed / (numFrames / sr)`, smoothed 0.9/0.1, into `cpuLoad_`.
  This is a correct **DSP load** (render is single-threaded on the device callback), not process CPU.
- `src/native/nota.engine/src/nota_engine_core.cpp:107` → `NativeMethods.CpuLoad` →
  `NotaEngine.CpuLoad` (`src/managed/Nota.Infrastructure/Interop/NotaEngine.cs:248`) — plumbing.
- `src/managed/Nota.App/MainWindow.axaml:359-367` — the meter (`CpuMeter`/`CpuFill`/`CpuText`),
  no tooltip, comment says "live DSP load" but the label says "CPU".
- `src/managed/Nota.App/MainWindow.axaml.cs:298-301` — updates the meter on every
  `PlayheadUpdated` tick from `Engine.CpuLoad`; text `"CPU " + N + "%"`.
- `src/managed/Nota.Mcp/Tools/SettingsTools.cs:23-36` — `get_cpu_load` computes `ProcessCpu` as
  `TotalProcessorTime / (now − StartTime)`: a **lifetime average**, which barely moves after a few
  minutes and is useless as a live readout. Also allocates a `Process` per call.
- `src/managed/Nota.App/PreferencesWindow.cs:362-400` — Preferences → Audio "CPU check" also
  measures DSP load only (caption already says "audio-thread load", so it is honest).
- Device-card meta lines also print `CPU x %` from the global DSP load:
  `SamplerInstrumentCard.cs:562-563`, `RhythmInstrumentCard.cs:696`, `MosaicInstrumentCard.cs:1261`,
  `PendulumInstrumentCard.cs:174` — same mislabelling (and it is the whole engine's load, not the
  device's).

## Root Cause Hypothesis

Confidence: **high**. The DSP-load number itself is computed correctly; the defect is
presentation. The UI calls it "CPU" with no qualifier, and the process-wide figure the user is
comparing against is never shown live — the only implementation (MCP) averages since process
start. Note that the two numbers are different in kind: DSP load is "% of the real-time budget
used by the audio callback" (what predicts dropouts), process CPU is "% of CPU time used by all
threads" (what the OS shows). Neither can replace the other.

## Proposed Remediation

**Preferred**:

1. **Shared sampler for process CPU** — add a small, UI-agnostic `ProcessCpuSampler` (e.g. in
   `Nota.Infrastructure`, behind an interface in `Nota.Application` so MCP and App share it).
   It keeps one cached `Process` (`Process.GetCurrentProcess()` once), and on `Sample()` calls
   `Refresh()`, reads `TotalProcessorTime` and a `Stopwatch` timestamp, and returns
   `Δcpu / Δwall` over the window since the previous sample (EMA or a ~2–3 s window). Sample at
   ~1 Hz from a dedicated `DispatcherTimer`, **not** from the 30–60 Hz `PlayheadUpdated` tick
   (`Refresh()` + `/proc/self/stat` parse / `proc_pidinfo` / `GetProcessTimes` per frame is
   wasteful and makes the number jittery). Wrap in try/catch → `null` = "unavailable" rather
   than 0.
   `Process.TotalProcessorTime` is backed by `GetProcessTimes` (Windows), `/proc/self/stat`
   (Linux) and `proc_pidinfo`/`task_info` (macOS), so one managed code path covers all three; no
   new native ABI and no new dependency.

2. **Cross-platform normalization (the key decision)** — OS monitors disagree on scale:
   - Linux `top`/`htop`, GNOME System Monitor (default, "Irix mode"), macOS Activity Monitor and
     `top`: **% of one core** (can exceed 100 %).
   - Windows Task Manager / Resource Monitor: **% of the whole machine** (÷ logical CPUs, ≤ 100 %).
   Preferred: report raw core-% internally and **display in the platform's native convention**
   (`OperatingSystem.IsWindows()` → divide by `Environment.ProcessorCount`), so the number lines up
   with the monitor the user actually opens, and say which in the tooltip ("…of one core" /
   "…of all CPUs"). `Environment.ProcessorCount` honours affinity and cgroup limits on Linux,
   which is also what `top` effectively counts against.

3. **Name the meter for what it is** — transport: change the label to `DSP N%` (or keep the
   compact readout but rename) and add a tooltip that shows both figures, e.g.
   "Audio (DSP) load: 8 % of the buffer time — what causes dropouts · Nota process: 41 % of one
   core (UI, plugins, audio)". The bar colour keeps tracking DSP load (that is what drives
   dropouts). Rename the device-card meta `CPU x %` to `DSP x %`. Optionally add a "Process CPU"
   line to the Preferences → Audio "CPU check" result (average/peak over the same 5 s).

4. **MCP** — make `get_cpu_load` use the same sampler (recent window, not lifetime), and say in the
   description which normalization `ProcessCpu` uses (recommend: always % of one core in MCP, a
   stable machine-facing contract; add `LogicalCpus` so the client can rescale). Keep field names
   for compatibility.

**Alternatives**:
- *Always show % of the whole machine on every OS* — one consistent number, but on Linux/macOS it
  will be 8–16× lower than `top`/Activity Monitor and the issue will be re-reported.
- *Native implementation in the engine (C ABI `nota_engine_process_cpu`)* using
  `getrusage`/`GetProcessTimes` — no benefit over `Process.TotalProcessorTime`, adds ABI surface
  and three platform branches in C++. Not recommended.
- *Only relabel, no process figure* — cheapest, satisfies "says clearly what it measures", but
  leaves users without the number they compare against.

**Files likely to change**:
- `src/managed/Nota.Application/…` — `IProcessCpuSampler` (or similar) interface
- `src/managed/Nota.Infrastructure/…/ProcessCpuSampler.cs` — new
- `src/managed/Nota.App/MainWindow.axaml`, `MainWindow.axaml.cs` — label, tooltip, 1 Hz timer
- `src/managed/Nota.App/PreferencesWindow.cs` — optional process figure in "CPU check"
- `src/managed/Nota.App/DeviceCards/Instruments/{Sampler,Rhythm,Mosaic,Pendulum}InstrumentCard.cs` — `CPU` → `DSP`
- `src/managed/Nota.Mcp/Tools/SettingsTools.cs` — use the sampler, document units
- `CHANGELOG.md` [Unreleased] Fixed; docs (`nota-docs` transport/Preferences pages, EN + RU) via
  the `nota-docs` skill; `FEATURES.md` if the readout counts as a capability

**Tests to add or update**:
- Unit/smoke check for the sampler: busy-spin one thread for ~1 s between two samples → reading
  ≈ 100 % of one core (tolerance ±25 %); idle → near 0; never negative/NaN; first sample (no
  previous point) returns null/0 rather than a lifetime average. Run in `tests/Nota.SmokeTest`
  so it executes on all three CI platforms.
- Normalization test: core-% → display-% conversion for Windows vs. others (pure function, OS
  passed in, so it is testable on any host).
- Manual verification on each OS: play a project, compare tooltip process figure with
  `top`/GNOME System Monitor, Activity Monitor and Task Manager → Details; expect agreement within
  a few percent of the monitor's own convention.

## Risks & Considerations

- **macOS / Apple Silicon accuracy**: older .NET versions reported `TotalProcessorTime` on arm64
  macOS in Mach absolute-time ticks instead of ns (values off by ~41×). Believed fixed in current
  .NET, but this must be verified on an M-series Mac with .NET 10 against Activity Monitor before
  shipping; if wrong, convert via `mach_timebase_info` or fall back to `getrusage(RUSAGE_SELF)`.
- **Windows**: `Process.Refresh()`/`TotalProcessorTime` is cheap via `GetProcessTimes`, but
  `Process.StartTime`-style calls can be slow/throw for other processes; we only touch our own, so
  fine. Task Manager's "CPU" column on Windows 11 may use "CPU utility" (frequency-scaled) rather
  than "CPU time" — expect small differences; note in docs rather than chase.
- **Linux sandboxed builds (Flatpak/AppImage)**: `/proc/self/stat` is always available to the
  process itself; `ProcessorCount` reflects cgroup quotas — the readout is consistent with what the
  sandbox grants.
- **Child processes** (`nota-scanworker` plugin scanner) are not included in our process time —
  correct, OS monitors list them separately too.
- **Thread-safety / RT rules**: the sampler runs on the UI thread; nothing touches the audio thread.
  No allocations per tick if the `Process` instance is cached.
- **MCP contract**: changing `ProcessCpu` from lifetime to recent-window changes the value's
  meaning; keep the name, update the description, consider adding a `WindowSeconds` field.
- **Expectation management**: after the fix, users will see a large process figure (35–45 %) — the
  real cost is addressed by #55; the tooltip should not imply the high number is a problem with
  audio safety (DSP load is the dropout predictor).

## Open Questions

- [NEEDS CLARIFICATION: Should the compact transport readout show DSP load (dropout predictor,
  current behaviour) or process CPU (comparable to OS monitor)? Assessment assumes DSP stays in
  the bar, renamed "DSP", with process CPU in the tooltip.]
- [NEEDS CLARIFICATION: Platform-native normalization (Windows ÷ cores, macOS/Linux per-core) vs.
  one convention everywhere? Assessment recommends platform-native.]
- [NEEDS CLARIFICATION: Nota version for the report — not stated; does not change the analysis.]
