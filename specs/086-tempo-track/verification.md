<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Verification: Tempo track and time scale

Date: 2026-11-11
Platform: Windows x64, .NET 10, MSVC 19.44

## Automated verification

- `scripts/build-win.ps1`: passed. Native engine, scanner self-test, managed build,
  complete smoke suite, localization/design checks, render-pool coverage and scan-scale
  tests all completed successfully.
- `dotnet run --project tests/Nota.SmokeTest -c Release --no-build -- --tempo`: passed.
  Covers step, linear and Bezier timing; forward/inverse conversion; validation;
  multi-delete history; multi-section mapping; marker omission; playback; loop and seek;
  metronome start; 6/8 count-in; MIDI timing; reference audio; and persistence.
- `dotnet run --project tests/Nota.SmokeTest -c Release -- --cue`: passed after rebasing
  spec 086 persistence onto the develop branch's format 32; the combined format is 33.
- `dotnet run --project tests/Nota.TempoUiTest -c Release --no-build`: passed 24 interaction
  checks for layout, Ctrl hover, marquee selection, deletion history, point and curve drags,
  time-scale mapping, Ctrl+Alt, cancellation, snapping, zoom/scroll coordinates, resize and
  collapse. Screenshot: `out/tempo-ui.png`.
- `git diff --check`: passed.

## Manual/documentation evidence

- Tempo lane screenshots were generated in Studio Dark and Studio Light for `nota-docs`.
- English and Russian transport/Arrangement documentation and generated shortcuts were
  updated; the documentation check completed with no errors.
- No push, merge or pull request was performed.

## Remaining issues

None within the accepted scope of approved issue #535. Creating time-signature markers and
performing new transient detection remain explicitly outside the specification; the tool uses
the current signature and already available warp/transient markers.
