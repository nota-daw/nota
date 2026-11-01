<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Audio level-meter update cadence

- **Tested**: 2026-11-01, Windows x64, current Release binaries
- **Scope**: measurement only; no production code or settings changes
- **Result**: all five observed level meters update at approximately 30 Hz

## Results

Actual App, normal main window and docked mixer, one audio track with a generated
quiet stereo amplitude-modulated signal; 1 second warmup, 10 second sample per backend.
Scratch settings/audio data are confined to diagnostics/MeterProbe/probe-data/.

| Measurement | WASAPI, 48 kHz / 480 frames | Yamaha Steinberg USB ASIO, 48 kHz / 64 frames |
|-------------|---------------------------:|------------------------------------------:|
| Animation callbacks per second | 238.00 | 237.49 |
| Existing service ticks per second | 29.93 | 29.75 |
| Transport master meter level changes/s | 29.92 | 29.75 |
| Arrangement track meter level changes/s | 29.92 | 29.75 |
| Arrangement master meter level changes/s | 29.92 | 29.75 |
| Mixer track meter level changes/s | 29.92 | 29.75 |
| Mixer master meter level changes/s | 29.92 | 29.75 |
| Meter drawing updates/s (all five) | 29.92 | 29.75 |
| Median level-change interval | 33.34 ms | 33.34 ms |
| 95th-percentile level-change interval | 33.77 ms | 37.52 ms |
| Reported audio dropouts during sample | 0 | 0 |

The backend comparison is two short sequential samples, not evidence that one backend
has worse scheduling. Every meter remained visible and no meter controls were rebuilt
during either measured interval. Input was disabled in the temporary probe window to
isolate automatic drawing from real hover, scrolling and editing.

## Why meters differ from the fixed playheads

MainWindowViewModel creates a 33 ms service clock in its constructor. OnTick raises
PlayheadUpdated, whose MainWindow.axaml.cs handler feeds the transport master meter,
ArrangementView.UpdateMeters and MixerView.UpdateMeters. The previous playhead fixes
moved cursor presentation to RequestAnimationFrame while deliberately retaining meter
polling on this service cadence. MeterBar.Push takes readings immediately and only
invalidates when the level or held peak changes; there is no separate meter animation.

Thus the current primary level meters have approximately 30 fresh readings/drawing
updates per second, even when display-frame callbacks arrive around 238 times/second.
Repeatedly repainting an unchanged meter would not provide additional audio readings.
No implementation change was requested or made by this test.

## Method and checks

- Probe builds against the existing Release assemblies, using no additional dependencies.
- Starts the real App, imports a generated 20-second stereo PCM WAV into one audio track,
  and opens the app's existing docked MixerView through its normal routing.
- Samples MeterBar's left/right level fields on actual window animation callbacks;
  counts changes, rather than mistaking steady-level suppression for missed updates.
- Separately counts composition draw-list replacements to observe drawing updates.
- Records the existing service event cadence without calling UpdateMeters itself.
- Follows current control instances, since startup can rebuild arrangement headers.
  An initial diagnostic failed because it retained a detached startup meter; the probe
  was corrected before either final measurement. No app code changed in response.
- Requires visible controls, existing composition draw lists and varying nonzero levels;
  verifies ASIO remains the active backend, without silently accepting WASAPI fallback.
- Diagnostic build: passed, zero warnings/errors. Both final runs: exit 0.
- git diff --check: passed. Full smoke suite was not repeated for this measurement-only
  task; the prior piano-roll build/smoke/scanner validation remains unchanged.

## Reproduce

From the repository root after a Windows Release build:

```powershell
dotnet build .specify/bugs/windows-playhead-stutter/diagnostics/MeterProbe/MeterProbe.csproj
dotnet .specify/bugs/windows-playhead-stutter/diagnostics/MeterProbe/bin/Debug/net10.0/MeterProbe.dll .specify/bugs/windows-playhead-stutter/diagnostics/MeterProbe/wasapi-results.json
dotnet .specify/bugs/windows-playhead-stutter/diagnostics/MeterProbe/bin/Debug/net10.0/MeterProbe.dll .specify/bugs/windows-playhead-stutter/diagnostics/MeterProbe/yamaha-results.json "Yamaha Steinberg USB ASIO"
```

Raw results: [WASAPI](diagnostics/MeterProbe/wasapi-results.json),
[Yamaha ASIO](diagnostics/MeterProbe/yamaha-results.json).
Diagnostic source: [Program.cs](diagnostics/MeterProbe/Program.cs).

## Limits

Animation callbacks and composition draw-list changes are UI-side measurements, not
physical display/compositor presentation. This test covers the transport, arrangement
and docked mixer MeterBar controls on this machine; it does not measure device-card
meters, Session meters, floating-mixer/minimization behavior or heavy sessions. The
generated signal keeps levels varying specifically to measure the UI update ceiling.
