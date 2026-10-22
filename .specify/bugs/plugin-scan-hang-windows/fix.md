# Bug Fix: Plugin scan hangs forever on Windows ("Scanning Analytiks…")

- **Slug**: plugin-scan-hang-windows
- **Fixed**: 2026-10-22
- **Assessment**: ./assessment.md
- **Status**: applied (Windows run not verified locally — covered by CI's `build-win.ps1`)

## Summary

The per-plugin scan timeout now actually fires, and a worker that has already answered is
not waited for. After the reporter said they have **about 1000 plugins**, the scope grew
(agreed with the maintainer) to make a large first scan finish in reasonable time and
visibly. The scan now:

- runs four probes in parallel, and retries the timeouts on its own afterwards;
- saves as it goes;
- remembers failed plugins;
- reports progress;
- after a store install, scans only the store folder;
- writes one log line per probe to `logs/plugin-scan.log`.

While load-testing the parallel scan we found a JUCE POSIX bug (an fd closed twice).
Because of it, the macOS/Linux scanner now spawns workers itself instead of through
`juce::ChildProcess`.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/pluginhost/scanner/OutOfProcessScanner.cpp` | rewritten | **POSIX**: `posix_spawn`; pipe is `FD_CLOEXEC` (plus `POSIX_SPAWN_CLOEXEC_DEFAULT` on macOS); stdin/stderr go to `/dev/null`; SIGPIPE/SIGCHLD reset to default; `poll()` loop with the deadline; stops at the end sentinel; `waitpid` only on its own pid; SIGKILL only while unreaped; always reaped (no zombies); exit code or signal goes to the log. **Windows**: `juce::ChildProcess`; the pipe is drained 1 byte at a time on a reader thread; the caller waits with the deadline, then kills the worker; spawns are serialised |
| `src/native/nota.engine/pluginhost/scanner/OutOfProcessScanner.h` | modified | `Outcome` enum (`Ok` / `Timeout` / `NoAnswer` / `StartFailed`); optional `outcome` / `detail` out-params |
| `src/native/nota.engine/pluginhost/scanner/worker/PluginScannerWorker.cpp` | modified | `exitNow(0)` right after printing: `TerminateProcess` on Windows (skips DLL detach), `_Exit` elsewhere |
| `src/native/nota.engine/pluginhost/scanner/abi_pluginscanner.cpp` | rewritten scan | <ul><li>Discovery builds a single probe list; up to 4 workers (`hw/2`, at least 1) pull from it.</li><li>Timeouts and missing answers in the parallel pass are retried one at a time with the full timeout before they count as failures.</li><li>Catalog and failed list are saved every 5 s and at the end.</li><li>Progress counters and the current name are kept in atomics behind a scan mutex (one scan at a time).</li><li>`only_dir` scans only VST3 in that folder.</li><li>`NOTA_SCAN_RETRY_FAILED` clears the failed list.</li><li>`ScanLog` is thread-safe.</li></ul> |
| `src/native/nota.engine/pluginhost/scanner/PluginCatalog.{h,cpp}` | modified | `scan-failed.txt`: id → file mtime. `unknownIds` skips a failed id while its mtime is unchanged. `markFailed` / `forgetFailed` / `clearFailed` / `failedCount`. Entries for deleted bundles are pruned. `scanLogPath()` |
| `src/native/nota.engine/include/nota/nota_engine.h` | modified | New `nota_pluginhost_scan_ex(worker, only_dir, flags)`, `nota_pluginhost_scan_progress(&done, &total)`, `nota_pluginhost_scan_current()`, `nota_pluginhost_scan_failed_count()`, `NOTA_SCAN_RETRY_FAILED`. `nota_pluginhost_scan` is unchanged and wraps `_ex` |
| `src/managed/Nota.Infrastructure/Interop/{NativeMethods,NotaEngine}.PluginHost.cs`, `PluginCatalog.cs` | modified | P/Invoke for the new calls; `ScanPlugins(worker, onlyDir, retryFailed)`, `ScanProgress`, `ScanFailedCount` |
| `src/managed/Nota.Application/Ports/IPluginCatalog.cs` | modified | New members are default-implemented, so the test fakes are unchanged; `PluginScanProgress` record |
| `src/managed/Nota.App/PluginScan.cs` | modified | `RescanAsync(main, progress, onlyDir, retryFailed)` polls progress every 250 ms; `Describe` / `Fraction` |
| `src/managed/Nota.App/PreferencesWindow.cs` | modified | **Rescan plugins** retries failed plugins and shows "Scanning plugins · 312 of 1,000 · Name". Final line: "Found N plugins. K couldn't be loaded — see plugin-scan.log…" |
| `src/managed/Nota.App/PreferencesWindow.Store.cs` | modified | Install/remove scans only `PluginsDir`, with a determinate fraction in the Downloads job |
| `src/managed/Nota.App/MainWindow.MissingPlugins.cs` | modified | Same scoped scan and progress |
| `src/native/nota.engine/pluginhost/scanner/tests/ScannerSelfTest.cpp`, `CMakeLists.txt` | added test | `nota-scanner-selftest` |
| `tests/Nota.SmokeTest/PluginStoreTests.cs`, `Program.cs` | added test | `--scan-scale <worker>` (needs a scratch `NOTA_DATA_DIR`) |
| `scripts/build.sh`, `build-linux.sh`, `build-win.ps1` | modified | Run the self-test after the native build, and `--scan-scale` in a scratch data dir after the smoke test, on all three CI OSes |
| `CHANGELOG.md` | modified | `[Unreleased] → Fixed` entry, kept beside #245's entry (conflict resolved) |

## Tests Added or Updated

**`nota-scanner-selftest`** (1.5 s timeout, the binary acts as its own stub worker):
- `ok`, `hang` (→ Timeout), `answer-then-hang` (→ Ok, fast), `noisy` (20 KB before the answer), `die` (→ NoAnswer, fast), missing worker.
- **8 probes in parallel** with mixed modes: each gets the right outcome, all within timeout + slack.
- **No unreaped children** (POSIX `waitpid(-1)` → ECHILD).

**`Nota.SmokeTest --scan-scale`**, against the real worker and 40 empty `.vst3` bundles:
- A folder-only scan probes exactly those 40 (no AU).
- The probes run in parallel.
- Progress counts up while the scan runs and clears afterwards.
- All 40 are remembered as failed.
- The next scan probes 0.
- A touched bundle is probed again.
- Retry probes all 40.
- Deleted bundles are forgotten.

## Local Verification (macOS, Release)

- `nota-scanner-selftest` → PASSED, 6 runs in a row; the parallel case takes about 1.56 s.
- `--scan-scale` → SCAN SCALE PASSED.
- Real full scan of this Mac (57 AU + 14 VST3), scratch data dir:
  - all 72 plugins found;
  - **about 10 s**, versus 49 s for the serial scan before.
- Headless Avalonia harness (`/tmp/scanharness`): Preferences → Plug-ins → **Rescan plugins**, with 300 fake bundles added to the real plugins (371 candidates).
  - Progress line: "Scanning plugins · 111 of 371 · Synth 124".
  - Final line: "Found 72 plugins. 300 couldn't be loaded — see plugin-scan.log in Nota's logs folder."
  - About 9 s per run.
  - Screenshots: `/tmp/scanharness/shots/rescan-{progress,done}.png`.
- **Harness runs before the POSIX spawn rewrite**:
  - 5–16 of 371 probes per run came back "no answer" (0 bytes, pipe closed while the worker was alive; once even exit code 0 with 0 bytes);
  - in one run Kontakt AU (`NiK8`) timed out.
- **After the rewrite**: 4 runs in a row, 0 retries, 0 failures among real plugins.
- `dotnet build Nota.sln -c Release` → succeeded.
- **Full smoke test**: everything passes except two `RenderPoolTests` "overloaded" timing checks (new in #245, pulled during this work).
  - A/B with `--perf`, alternating the base `develop` engine and this branch's engine under the same load (load average about 7: Rider, a Debug Nota.App and GitKraken running):
    - base: pass, pass, **fail** (32 late blocks);
    - new: pass, **fail** (43), pass.
  - Same flakiness on both, so not caused by this change. Re-check on an idle machine or in CI.
- **Scan timing, develop vs this branch** (371 candidates: 57 AU + 14 VST3 + 300 empty bundles; details in `pr-assets/issue-220/summary.md`):
  - Kontakt loading fast: 46.4 s → 13.3 s; both find 72.
  - Kontakt slow (~32 s per load), 3 interleaved runs: about 103 s vs 100 s; develop finds 70 (loses both Kontakt 8), the branch finds 72.
- `scripts/build.sh` on the final tree:
  - scanner self-test PASSED;
  - smoke: 3839 passed, 2 failed (the RenderPoolTests "overloaded" flake above; load average up to 12.8);
  - the script stops there, so `--scan-scale` was run by hand → PASSED.
- **Not verified locally**: the Windows code paths (`TerminateProcess` in the worker, the reader-thread probe, the build). CI's `build-win.ps1` now builds them and runs the self-test and `--scan-scale`.
- **Not verified on Linux**: the worker under a headless CI runner (JUCE GUI init without a display) for `--scan-scale`. If it fails there, that step needs `xvfb-run` or a Linux skip.

## Deviations from Assessment

- **Scope extended** after the "1000 plugins" follow-up (maintainer's decision): parallel probes, incremental save, a remembered-failures list, a progress ABI with UI, and store-folder-only scans. Some of these were listed in the assessment as alternatives or follow-ups. This adds new C ABI functions; the existing ones are unchanged.
- **POSIX no longer uses `juce::ChildProcess`.** JUCE 8.0.14's POSIX `ActiveProcess::~ActiveProcess` calls `fclose(readHandle)` and then `close(pipeHandle)` on the same fd. Under concurrency that second close hits another thread's newly opened fd, which corrupts sibling probes. It could also, in principle, close an unrelated file or socket of the app; that risk existed at lower odds with the old serial scan too. Not found in the assessment.
- **The final attempt gets 2 minutes.** Kontakt 8 took 32 s alone on a bad day (3 s otherwise); with 30 s it would have been remembered as failed. The parallel pass keeps 30 s; the one-at-a-time retry, and a single-plugin scan such as a store install, get 120 s.
- **Progress shows no name for an AU.** Its id (`Synths/aumu,Ni$D,-NI-`) means nothing to a user before it loads; a VST3 shows its bundle name.
- **Serial retry** of timeouts and missing answers. Without it, parallel load made heavy plugins (NI Kontakt AU) miss the 30 s deadline, and the failed list would have hidden them for good.
- **Fallback parse of output without the sentinel dropped.** The worker always prints the sentinel.
- **Log location**: `<data dir>/logs/plugin-scan.log`, written by the engine itself, rather than the app's daily log. This avoids a logging callback across the ABI.
- **A native test executable** instead of a test framework; the project has no native unit-test harness.

## Follow-ups

- Docs: `../nota-docs` pages `plugins/scanning.mdx`, `preferences/plugins.mdx` and `reference/troubleshooting.mdx` (EN + RU) should describe the progress line, the skipped-failures behaviour, Rescan retrying them, and where `plugin-scan.log` lives (nota-docs skill).
- Ask the reporter (issue #220) to try a build with this fix and attach `%APPDATA%\Nota\logs\plugin-scan.log`.
- Consider a Preferences list of the plugins that failed, with a per-plugin retry.
- Consider reporting the JUCE double close upstream.
- Drop `stash@{0}` ("Auto stash before merge of develop and origin/develop", GitKraken's backup from 09:19) once the branch is reviewed. Its content is an older snapshot of this same work.
