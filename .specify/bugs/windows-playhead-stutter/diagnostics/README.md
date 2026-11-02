<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Windows playhead diagnostics

Run from the repository root after a Windows Release build. Both diagnostic projects
reference the existing application binaries; they add no application dependencies.

The original windowless timer measurement:

```powershell
dotnet run --project .specify/bugs/windows-playhead-stutter/diagnostics/TimerProbe.csproj -- src/managed/Nota.App/bin/Release/net10.0/nota_engine.dll
```

The real-window comparison (each run opens and closes a Nota window):

```powershell
dotnet run --project .specify/bugs/windows-playhead-stutter/diagnostics/AppFrameProbe/AppFrameProbe.csproj -- legacy .specify/bugs/windows-playhead-stutter/diagnostics/app-legacy-results.json
dotnet run --project .specify/bugs/windows-playhead-stutter/diagnostics/AppFrameProbe/AppFrameProbe.csproj -- frames .specify/bugs/windows-playhead-stutter/diagnostics/app-frames-final-results.json
```

`legacy` disables only the new frame loop through reflection. The real window's existing
service tick then sets the cursor directly, reproducing its old scheduling behavior.
`frames` uses the actual frame callback implementation. The probe starts the real engine,
removes the default template's tracks, plays an empty project, and samples changes in the
Arrangement's display position on frame callbacks for five seconds. The counters measure
UI property changes observed on those callbacks, not compositor presentation or physical
screen refresh. This is an integration harness, not a packaged application launch.

Append a buffer-size request, e.g. `64` or `2048`, after the result path. Inspect the
reported **negotiated** buffer size: this Windows shared-mode device negotiates a 480-frame
hardware period when 64 client frames are requested.

To test an installed ASIO driver, pass `0` for the buffer request and its exact name
as a fourth argument. ASIO uses the driver's existing buffer setting:

```powershell
dotnet run --project .specify/bugs/windows-playhead-stutter/diagnostics/AppFrameProbe/AppFrameProbe.csproj -- frames .specify/bugs/windows-playhead-stutter/diagnostics/app-asio-yamaha-frames-results.json 0 "Yamaha Steinberg USB ASIO"
```

The probe requires the actual running backend to stay ASIO throughout the check.
If driver startup fails or the device falls back to WASAPI, it records the failure
and exits unsuccessfully; the result cannot be mistaken for ASIO verification.
Driver identity, negotiated rate/buffer and fallback reason are included in the JSON.
The same fourth argument works with `legacy` and `lifecycle` modes.

`lifecycle` instead checks play/stop, export suspension, backend restart, minimize/restore,
seek, short loops, tempo changes and closing with a pending frame callback:

```powershell
dotnet run --project .specify/bugs/windows-playhead-stutter/diagnostics/AppFrameProbe/AppFrameProbe.csproj -- lifecycle .specify/bugs/windows-playhead-stutter/diagnostics/app-lifecycle-final-results.json
```

Settings, logs and generated presets go under `probe-data/` beside the requested results;
this ignored directory isolates the probe from the user's Nota configuration. Welcome
dialogs and update checks are disabled. The harness changes no production source.

Deterministic display math and native snapshot ABI checks are part of the smoke suite,
and can also run independently:

```powershell
dotnet run --project tests/Nota.SmokeTest -c Release -- --transport-display
```

Timing results depend on the display, audio backend, foreground/occluded window state,
and system load. Keep wall-clock cadence measurements out of CI pass/fail assertions.
