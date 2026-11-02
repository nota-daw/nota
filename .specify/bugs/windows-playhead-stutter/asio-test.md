<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# ASIO Verification: Windows playhead

- **Slug**: windows-playhead-stutter
- **Tested**: 2026-11-01
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Earlier verification**: ./test.md (WASAPI)
- **Result**: verified on Yamaha Steinberg USB ASIO at 48 kHz / 64 frames

## Summary

The user selected Yamaha Steinberg USB ASIO from the installed drivers. The actual Nota
window and engine successfully ran that driver at 48,000 Hz with 64-frame callbacks.
The final frame path updated the Arrangement cursor on all 1,184 observed animation
callbacks, approximately 237 times per second. The original timer path updated it about
22 times per second. No run reported an audio dropout or a fallback to WASAPI.

## Driver and Environment

- Windows build: `Microsoft Windows NT 10.0.26300.0`.
- Driver: `Yamaha Steinberg USB ASIO`.
- Driver CLSID: `{CB7F9FFD-A33B-48B2-8BC0-437D94F37142}`.
- Actual active backend: `Asio`, checked throughout each frame or lifecycle callback.
- Negotiated sample rate / buffer: 48 kHz / 64 frames in all three runs.
- Fallback reason: empty in all three runs.

The diagnostic uses scratch Nota settings and the driver's existing buffer setting.
It does not open the driver's control panel or change its buffer setting. This task
changed only diagnostic code/documentation and added evidence; no production code
changes were needed for the ASIO check.

## Checks Performed

| Check | Result | Evidence |
|-------|--------|----------|
| Diagnostic build | pass | `dotnet build .specify/bugs/windows-playhead-stutter/diagnostics/AppFrameProbe/AppFrameProbe.csproj --nologo -v quiet`, exit 0, no warnings/errors |
| Actual ASIO startup | pass | Active backend remained ASIO; exact requested driver ID confirmed |
| Original timer path | pass | `diagnostics/app-asio-yamaha-legacy-results.json` |
| Final frame path | pass | `diagnostics/app-asio-yamaha-frames-results.json` |
| Lifecycle integration | pass | All 12 checks in `diagnostics/app-asio-yamaha-lifecycle-results.json` |
| Whitespace | pass | `git diff --check` |

The three executions exited 0. The lifecycle checks cover play, export suspension,
backend stop/start and resume, minimize/restore, seek, short repeated loops, tempo
changes, stop to the launch anchor, replay, closing, and a late callback after engine
disposal. The lifecycle result also confirms ASIO remained active after restarting
the audio backend.

## Measured Cursor Cadence

| Path | Cursor changes/s | Median gap | 95th percentile | Observed callbacks / cursor changes | Reported dropouts |
|------|-----------------:|-----------:|----------------:|-----------------------------------:|------------------:|
| Original service timer | 22.22 | 47.43 ms | 48.89 ms | 453 / 112 | 0 |
| Final frame path | 236.80 | 4.16 ms | 4.40 ms | 1184 / 1184 | 0 |

These are actual Arrangement display-position changes sampled on animation callbacks
over five seconds on this high-refresh display. They are not physical compositor
presentation measurements. The empty-project comparison uses the same production
window, native engine, driver and negotiated buffer; `legacy` disables only the newly
added frame loop so the existing service timer drives the cursor directly.

## Reproduce

From the repository root after the Windows Release build:

```powershell
dotnet run --project .specify/bugs/windows-playhead-stutter/diagnostics/AppFrameProbe/AppFrameProbe.csproj -- frames .specify/bugs/windows-playhead-stutter/diagnostics/app-asio-yamaha-frames-results.json 0 "Yamaha Steinberg USB ASIO"
```

Use `legacy` or `lifecycle` instead of `frames` with a corresponding result file for
the other checks. The probe writes an explicit failure and exits unsuccessfully if
the requested ASIO driver is unavailable or the engine falls back to WASAPI.

## Coverage Limits

- Covers the selected Yamaha driver at its current 64-frame buffer and 48 kHz rate;
  other driver settings and other installed ASIO drivers were not exercised.
- Measures the reported empty-project playhead behavior, not loaded-project DSP
  performance or an audible-content playback test.
- Existing WASAPI results remain in `test.md`; that historical report was not overwritten.

## Recommendation

The empty-project playhead fix is also verified on the user's selected Yamaha
Steinberg USB ASIO configuration. No additional ASIO-specific fix is indicated by
these checks.
