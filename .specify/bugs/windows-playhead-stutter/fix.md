<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Bug Fix: Windows playhead stutters even in an empty project

- **Slug**: windows-playhead-stutter (explicit user request)
- **Fixed**: 2026-11-01
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The Arrangement cursor now updates on the visible window's animation frames, interpolating
an authoritative, timestamped engine transport snapshot. Engine polling, meters, recording
materialization and other service work retain their existing timer cadence. The audio clip
editor's existing playhead overlay also uses the frame position; the piano roll retains its
existing service cadence because moving it to every frame would repaint its full grid.

Display interpolation is bounded by audio already rendered. It stops at the rendered
endpoint if callbacks stall, handles loop wraps, and resets its history on seek, start/stop,
tempo, loop configuration and device sample-rate setup. Small client callbacks batched by
WASAPI are represented by a rendered window up to the negotiated hardware period, avoiding
new pauses between those callbacks. Fractional-sample loop boundaries are re-anchored to
the real transport each block so rounding errors cannot accumulate.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/include/nota/nota_engine.h` | additive API | `NotaTransportSnapshot` and `nota_transport_snapshot`, a 64-byte POD contract |
| `src/native/nota.engine/src/engine/Transport.{h,cpp}` | snapshot publication | Lock-free atomic payload and bounded coherent reads; native monotonic timestamp; rendered-window history |
| `src/native/nota.engine/src/engine/Engine_Render.cpp` | block publication | Records the playing portion, including a leading count-in, and publishes once per completed block |
| `src/native/nota.engine/src/engine/Engine.h`, `src/native/nota.engine/src/abi/nota_engine_core.cpp` | ABI bridge | Exposes the snapshot without allocation or waiting |
| `src/managed/Nota.Application/Engine/NotaTransportSnapshot.cs`, `IAudioEngine.cs` | contract | Blittable snapshot and engine-port query |
| `src/managed/Nota.Infrastructure/Interop/{NativeMethods,NotaEngine}.cs` | interop | One native call fetches a coherent snapshot |
| `src/managed/Nota.Presentation/ViewModels/TransportDisplayPosition.cs` | display math | Stateless interpolation, endpoint hold and loop mapping |
| `src/managed/Nota.App/MainWindow.Playhead.cs`, `MainWindow.axaml.cs` | frame lifecycle | Separate frame loop, export/minimize/close guards, visible detail-window fallback; existing service subscriptions stay on their timer |
| `src/managed/Nota.App/ArrangementView*.cs` | display separation | Ruler, overlays, overview and Follow use the display position; split/paste/section commands continue to use the authoritative position |
| `tests/Nota.SmokeTest/TransportDisplayTests.cs`, `Program.cs` | regression tests | Deterministic display math, native ABI, concurrent publication and fractional-loop drift; focused `--transport-display` option |
| `CHANGELOG.md` | release note | Records the Windows playback-cursor fix |
| `diagnostics/` in this bug directory | integration evidence | Windowless timer probe, actual Nota-window comparison and lifecycle checks with scratch settings |

The audio publisher performs no allocation, locking, I/O or reader wait. Its payload
atomics are compile-time checked for lock freedom. The reader makes at most three
attempts and skips an overlapping publication instead of waiting on the audio writer.
The pure C ABI and Transport's ownership of musical time are preserved (AR-6/7/9).

## Tests Added or Updated

`TransportDisplayTests.Run()` covers interpolation between callbacks; stopped, stalled,
late and zero-duration snapshots; seek/tempo changes; leading count-in; loop seams,
pre-loop playback and invalid ranges; the ABI layout and endpoint values; a block ending
its count-in; 2,000 blocks across fractional-sample loop boundaries; and concurrent readers
over 4,000 offline blocks. It runs in the standard smoke suite.

The diagnostic uses the real `App`, `MainWindow`, engine and Arrangement control. Its
`legacy` mode disables only the new frame loop, exercising the original service-tick
cursor path. `lifecycle` checks actual window minimize/restore, export suspension, device
restart, seek, loops, tempo, stop and closing with a queued callback.

## Local Verification

- **Windows build**: `pwsh scripts/build-win.ps1` under the installed Visual Studio x64
  developer environment — exit 0, `SMOKE TEST PASSED`, `SCAN SCALE PASSED`; scanner
  self-tests, design/architecture checks and render-pool performance guards passed.
  Log: `diagnostics/build-win-final.log` (ignored local build output).
- **Focused tests**: `dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --transport-display`
  passed before the final full run; the final full run also includes the added
  fractional-loop drift guard.
- **Real-window measurements**, Windows build 26300, WASAPI, 48 kHz:

  | Path | Negotiated hardware frames | Cursor updates/s | Median gap | 95th percentile | Dropouts |
  |------|---------------------------:|-----------------:|-----------:|----------------:|---------:|
  | Original service-tick path | 480 | 21.47 | 47.75 ms | 48.79 ms | 0 |
  | Final frame path, device default | 480 | 238.80 | 4.16 ms | 4.42 ms | 0 |
  | Final frame path, 64 client frames requested | 480 | 237.79 | 4.16 ms | 4.36 ms | 0 |
  | Final frame path, 2048 requested | 2048 | 213.38 | 4.16 ms | 4.60 ms | 0 |

  These count cursor property changes observed on animation callbacks over five seconds,
  not physical compositor presentations. Final default and small-buffer runs changed the
  cursor on every observed frame callback. Raw results and reproduction commands are
  under `diagnostics/`; timing assertions are deliberately excluded from CI.
- **Lifecycle integration**: all twelve checks passed, including invoking a late callback
  after the window closed and disposed the engine. See `diagnostics/app-lifecycle-final-results.json`.
- `git diff --check` — clean.

## Deviations from Assessment

- The additive native snapshot was needed to interpolate from a coherent audio-owned
  timestamp rather than inventing an independent UI musical clock. This was explicitly
  allowed by the assessment; its implementation adds native/managed ABI files to scope.
- Real-device testing exposed batching when 64 client frames were requested: the actual
  Windows device period was 480 frames. The snapshot therefore spans a bounded rendered
  history up to that period, rather than just the most recent small callback.
- The existing audio-editor overlay follows frames as well. The piano roll's full-grid
  rendering remains on its prior cadence to avoid multiplying its rendering cost.
- The assessment's missing full-application evidence was addressed with the actual-window
  integration harness. Its original text was left untouched.

An initial smoke run failed two new count-in assertions because the test requested Play
before applying the queued Stop. The test now drains Stop/Seek before requesting count-in,
matching the existing count-in tests. The final full Windows run passed.

## Follow-ups

- ASIO, macOS and Linux were not exercised for this change. Validate those configurations
  during review; no claim of cross-platform visual verification is made.
- A dedicated 60 Hz display, popped-out audio clip editor and content-heavy Follow scrolling
  were not instrumented here. Existing piano-roll and content-rendering performance issues
  are outside the empty-project cursor fix.
- Next workflow step: `/speckit-bug-test slug=windows-playhead-stutter` (verification results
  can be recorded from the checks above).
