# Bug Assessment: Plugin scan hangs forever on Windows ("Scanning Analytiks…")

- **Slug**: plugin-scan-hang-windows
- **Created**: 2026-10-21
- **Source**: https://github.com/nota-daw/nota/issues/220 (host: github.com — allowlisted, read via `gh issue view`) + follow-up details from the reporter relayed by the maintainer
- **Verdict**: valid (scan-never-finishes mechanism confirmed in code; the specific plugin that wedges the worker is not yet identified)
- **Severity**: high

## Report (verbatim or summarized)

Issue #220, "Plug-in scan doesn't work on Windows 11 (0.56.1)": the plug-in scan does
not pick up installed plug-ins, so none show up in the browser. Windows 11 x64, Intel
Core i5, 24 GB RAM. No log attached.

Follow-up from the reporter (relayed in chat, with two screenshots):

- Preferences scan path: `C:\Users\tuned\AppData\Roaming\Nota\plugins\VST3` (the
  Get Plug-ins store folder).
- App installed at `C:\Program Files\Nota\Nota.App.exe`. No antivirus.
- After downloading a plug-in from Get Plug-ins, the job hangs indefinitely on
  "Scanning Analytiks…" (the progress bar never finishes).

## Symptom

On Windows, after a Get Plug-ins install (and likewise on Rescan) the plug-in scan
never returns: the job stays on "Scanning <name>…" forever, the catalog is never saved
and the browser is never rebuilt, so installed plug-ins never show up. Expected: each
plug-in is probed within a bounded time (a hanging one is killed and skipped after
30 s), the scan finishes and the browser lists what was found.

## Reproduction

1. Windows 11 x64, Nota 0.56.1, installed under `C:\Program Files\Nota`.
2. Preferences → Get Plug-ins → install a plug-in (reported: Analytiks).
3. The job shows "Scanning Analytiks…" and never completes.

The label is misleading: `RescanAfterStoreChange` runs a **full** rescan of every scan
path plus the default VST3 location (`C:\Program Files\Common Files\VST3`), so the
plug-in that wedges the worker may be any not-yet-catalogued one on that machine,
not necessarily Analytiks.

[NEEDS CLARIFICATION: which plug-ins are in `C:\Program Files\Common Files\VST3`; is a
`nota-scanworker.exe` process left running in Task Manager while the job hangs, and
does it carry the path of a specific `.vst3` on its command line?]

## Suspected Code Paths

- `src/native/nota.engine/pluginhost/scanner/OutOfProcessScanner.cpp:scanDescriptions()`
  — the per-plug-in timeout is checked only *between* `proc.readProcessOutput(buf, 8192)`
  calls. That call does not return early:
  - Windows (`juce_core/native/juce_Threads_windows.cpp`, `ActiveProcess::read`): loops
    with `PeekNamedPipe` + `Sleep(1)` until **all 8192 bytes** are read or the child
    stops running.
  - POSIX (`juce_SharedCode_posix.h`, `ActiveProcess::read`): `fread(…, 8192)` on the
    pipe, which likewise blocks until 8192 bytes or EOF.
  The worker's whole output is far below 8 KB, so the first read blocks until the
  worker exits. If the worker never exits, the `deadline` check is never reached and
  `proc.kill()` never runs — the 30 s timeout is dead code on every platform.
- `src/native/nota.engine/pluginhost/scanner/abi_pluginscanner.cpp:nota_pluginhost_scan()`
  — scans plug-ins serially and only calls `catalog.save()` at the end, so one stuck
  worker blocks the whole scan and nothing found earlier is persisted.
- `src/native/nota.engine/pluginhost/scanner/worker/PluginScannerWorker.cpp` — prints the
  sentinel block and returns from `main`; the process then runs plug-in unload / static
  teardown (`ScopedJuceInitialiser_GUI` destructor, `FreeLibrary` of the `.vst3`). A
  plug-in that deadlocks during unload, waits on a hidden dialog (activation, licence,
  first-run), or blocks inside `findAllTypesForFile` keeps the worker alive forever.
  The worker is started with `CREATE_NO_WINDOW` and the parent shows nothing about it.
- `src/managed/Nota.App/PluginScan.cs:RescanAsync()` and
  `src/managed/Nota.App/PreferencesWindow.Store.cs:InstallFromStore()` — `await
  Task.Run(() => catalog.Scan(worker))` with no cancellation or timeout, so the store job
  waits on the native call indefinitely.
- `src/native/nota.engine/pluginhost/scanner/PluginCatalog.cpp:unknownIds()` — a plug-in
  whose probe failed or timed out is not remembered, so it is re-probed (and costs the
  full timeout) on every scan.

## Root Cause Hypothesis

`OutOfProcessScanner::scanDescriptions` relies on `juce::ChildProcess::readProcessOutput`
returning promptly, but both JUCE implementations block until the requested 8 KB are
read or the child exits. Any `nota-scanworker.exe` that does not exit — a plug-in that
hangs during probing, waits on an invisible modal, or deadlocks on unload after the
sentinel was already printed — therefore hangs the whole scan forever instead of being
killed after 30 s. Confidence: **high** that this is why the scan never ends (the timeout
cannot fire); **medium** on which plug-in, and in which phase, keeps the worker alive on
the reporter's machine (Windows VST3 plug-ins hanging in `DllMain`/static teardown on
process exit is a well-known pattern, and the bug is long-standing: the same loop existed
in the former `PluginHost.cpp`).

## Proposed Remediation

**Preferred**: make the per-plug-in timeout real and stop waiting once the answer is in.

1. In `scanDescriptions`, move the pipe draining off the waiting thread: a reader
   (`std::thread` or `juce::Thread`) loops `readProcessOutput` into a buffer guarded by
   a mutex and signals when the buffer contains `<<<NOTA_PLUGINS_END>>>` or the read
   returns 0. The caller waits on that signal with the deadline. On the END sentinel →
   parse the result, `proc.kill()` if still running (the answer is in; a hang at exit no
   longer matters). On deadline → `proc.kill()` (which unblocks the read on both
   platforms: `TerminateProcess` stops `isRunning()`, SIGKILL gives EOF), join the reader,
   return empty.
2. In the worker, after printing and flushing the sentinel block, terminate immediately
   (`std::_Exit(0)` / `TerminateProcess(GetCurrentProcess(), 0)` on Windows) instead of
   returning through plug-in unload and static destructors.
3. Log one line per probe (format, path, ms, outcome: ok / no types / timeout / crash) to
   the engine log, so the next report names the plug-in that misbehaves.

**Alternatives**:
- Read 1 byte at a time in the existing loop — does not help: a 1-byte read still blocks
  while the pipe is empty and the child is alive.
- Windows-only `PeekNamedPipe` before each read via our own process wrapper — fixes
  Windows, but POSIX keeps the same latent hang; the reader-thread approach covers both.
- Remember timed-out / crashed plug-ins in a "failed" list in the catalog (JUCE's
  `KnownPluginList::addToBlacklist` / dead-man's-pedal style) so later scans skip them
  until the file changes, and show them in Preferences — worthwhile follow-up, not
  required to stop the hang.
- Make the managed scan cancellable and report "Scanning <plug-in>…" per plug-in (needs a
  progress callback across the C ABI) — better UX, larger change.

**Files likely to change**:
- `src/native/nota.engine/pluginhost/scanner/OutOfProcessScanner.cpp`
- `src/native/nota.engine/pluginhost/scanner/OutOfProcessScanner.h` (if the timeout
  becomes injectable for tests)
- `src/native/nota.engine/pluginhost/scanner/worker/PluginScannerWorker.cpp`
- `src/native/nota.engine/pluginhost/scanner/abi_pluginscanner.cpp` (per-probe logging)

**Tests to add or update**:
- Native test: point `OutOfProcessScanner` (short timeout, e.g. 500 ms) at a stub
  "worker" that prints nothing and sleeps forever → `scanDescriptions` returns empty
  within ~timeout and the child is gone.
- Native test: stub that prints a valid sentinel block and then sleeps forever →
  returns the parsed descriptions promptly, child killed.
- Native test: stub that prints > 8 KB of noise before the sentinel → still parsed.
- Smoke: existing scan path keeps finding the bundled/test plug-ins.

## Risks & Considerations

- Killing the worker right after the END sentinel is safe: the result is already in the
  pipe buffer and fully read before the kill.
- Reader thread lifetime: always join after kill; on Windows the read loop exits once
  `isRunning()` is false, on POSIX once EOF is seen.
- `std::_Exit` in the worker skips flushing — flush `std::cout` explicitly first (the
  current `std::endl` already does).
- No ABI change; managed side untouched for the preferred fix.
- Performance: one extra thread per probe, negligible next to process spawn.
- This does not explain *why* the worker hangs on the reporter's machine; logging (step 3)
  is what will name the plug-in if the scan still reports a timeout.

## Open Questions

- [NEEDS CLARIFICATION: logs from `%APPDATA%\Nota\logs\` and the list of VST3 plug-ins on
  the reporter's machine — is Analytiks itself the hanging plug-in, or one in
  `C:\Program Files\Common Files\VST3`?]
- [NEEDS CLARIFICATION: is `nota-scanworker.exe` still running in Task Manager during the
  hang, and does ending it let the scan continue? This would confirm the hypothesis on the
  user's machine without a new build.]
