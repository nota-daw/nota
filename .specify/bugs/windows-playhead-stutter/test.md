<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Verification: Windows playhead stutters even in an empty project

- **Slug**: windows-playhead-stutter (from the current bug-fix context)
- **Tested**: 2026-11-01
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified on the tested Windows/WASAPI configuration

## Summary

The actual Nota-window integration harness reproduces the empty-project cursor's original
roughly 21 Hz service-timer stepping. With the final fix, the default-device run changes the
cursor on every observed animation callback, about 239 times per second on this machine,
with no reported audio dropouts. The complete Windows build and smoke suite pass.

## Checks Performed

| Check | Command / Action | Result | Notes |
|-------|------------------|--------|-------|
| Original symptom, automated equivalent | `AppFrameProbe legacy .../app-legacy-results.json` | pass | Real App, MainWindow, engine and Arrangement; only the new frame loop is disabled to exercise the original timer path |
| Final fix, default WASAPI device | `AppFrameProbe frames .../app-frames-final-results.json` | pass | Fresh scratch settings; empty project; 48 kHz, 480 hardware frames; 1,194 observed frame callbacks and 1,194 cursor changes in the sample |
| Small WASAPI client buffer | `AppFrameProbe frames .../app-frames-64-final-results.json 64` | pass | 64 requested, 480 hardware frames negotiated; 1,189 callbacks and 1,189 cursor changes |
| Larger WASAPI buffer | `AppFrameProbe frames .../app-frames-2048-final-results.json 2048` | pass | 2048 hardware frames; median cursor gap remains 4.16 ms; occasional endpoint holds are bounded rather than extrapolating beyond rendered audio |
| Window and transport lifecycle | `AppFrameProbe lifecycle .../app-lifecycle-final-results.json` | pass | All 12 checks, including export suspension, device restart, minimize/restore, seek, loops, tempo, stop and a callback after engine disposal |
| Deterministic display and native ABI regressions | `dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --transport-display` | pass | All 21 transport-display/snapshot assertions; also includes design/architecture checks before the focused branch |
| Full Windows build and regression suite | `pwsh scripts/build-win.ps1` in the Visual Studio x64 developer environment | pass | Exit 0; native and managed build, scanner self-tests, smoke tests, render-pool guards and large-folder scanner check |
| Whitespace | `git diff --check` | pass | No errors |
| ASIO and other OSes | Not run | not-run | No visual-verification claim for those configurations |

Builds used the installed Visual Studio toolchain and .NET 10; Avalonia is the project's
existing 12.1.4 dependency. The GUI harness uses ignored scratch configuration and disables
welcome dialogs and update checks. It does not modify production sources. Its commands
and measurement definitions are in `diagnostics/README.md`.

## Measured Cursor Cadence

| Path | Cursor changes/s | Median gap | 95th percentile | Reported dropouts |
|------|-----------------:|-----------:|----------------:|------------------:|
| Original timer path | 21.47 | 47.75 ms | 48.79 ms | 0 |
| Final default-device path | 238.80 | 4.16 ms | 4.42 ms | 0 |
| Final 64-client-frame request | 237.79 | 4.16 ms | 4.36 ms | 0 |
| Final 2048-frame request | 213.38 | 4.16 ms | 4.60 ms | 0 |

These are changes to the actual Arrangement display-position property sampled on animation
callbacks over five seconds. They are not direct physical screen-refresh or compositor
presentation measurements. The original and final default-device paths both negotiated
480 frames at 48 kHz. Raw timestamps are retained in the four JSON files above.

## Output Excerpts

```text
[PASS] transport display: stalled audio or a delayed UI frame holds the exact rendered endpoint
[PASS] transport snapshot: fractional-sample loop lengths do not accumulate display drift
[PASS] transport snapshot: concurrent reads never mix two published blocks
SMOKE TEST PASSED
SCAN SCALE PASSED
```

Local build/check logs are `diagnostics/build-win-final.log` and
`diagnostics/transport-tests-final.log`; generated logs and scratch application data are
ignored so they do not inflate the change. Initial test setup failures and the WASAPI
batching refinement are documented in `fix.md`.

## Residual Risks

- Verification covers Windows build 26300 and this machine's WASAPI device. A dedicated
  60 Hz display, ASIO, macOS and Linux were not tested.
- The integration harness creates the real window after App setup, rather than launching
  the packaged splash/startup sequence. It exercises the production frame lifecycle and
  native backend but does not measure the physical display or DAC clock.
- The audio clip editor shares the frame position, but its popped-out configuration was
  not instrumented. The piano-roll cursor intentionally retains its prior cadence to
  avoid moving full-grid repaints to display rate.
- Content-heavy Follow scrolling still repaints waveform content. That performance path
  is separate from the reported empty-project stutter and was not benchmarked here.

## Recommendation

The empty-project Arrangement cursor fix is verified on Windows/WASAPI and ready for
review. Keep the platform and editor coverage limits above in the review notes; no
additional implementation is required for the tested empty-project symptom.
