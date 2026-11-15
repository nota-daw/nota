# Bug Fix: Input monitoring clicks when the capture device runs at another rate

- **Slug**: 563-monitor-input-rate
- **Fixed**: 2026-11-15
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The capture device now opens at the engine's rate where it can (macOS: the input device's
nominal rate is set, as the output already did; miniaudio: it is asked for the engine's rate
and converts). Where the rates still differ (Bluetooth, a device that doesn't list the rate),
live monitoring reads the input through a windowed-sinc resampler (cut at the lower Nyquist,
so a faster input is low-passed before it is decimated) whose step follows the queue, so
neither the rate nor the drift between the two clocks turns into trims and re-primes (clicks).
At matching rates the monitor path is the old one, unchanged.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/native/nota.engine/src/audio_io/AudioInput.h` | modified | `start(cb, device, preferredRate)` |
| `src/native/nota.engine/src/audio_io/macos/CoreAudioDevice.h` | added | nominal-rate / Bluetooth / supported-rate queries shared by output and input, plus `setDeviceNominalSampleRate` (waits for the change) |
| `src/native/nota.engine/src/audio_io/macos/CoreAudioBackend.mm` | modified | uses the shared helpers (moved, not changed) |
| `src/native/nota.engine/src/audio_io/macos/CoreAudioInput.{h,mm}` | modified | sets the input device to the engine's rate **before** binding the AUHAL to it |
| `src/native/nota.engine/src/audio_io/desktop/MiniaudioInput.{h,cpp}` | modified | `config.sampleRate` = the engine's rate (miniaudio converts) |
| `src/native/nota.engine/src/audio_io/windows/AsioInput.{h,cpp}` | modified | signature only: the ASIO driver already runs at the engine's rate |
| `src/native/nota.engine/src/engine/Engine_Recording.cpp` | modified | `ensureAudioInput` passes the engine's rate and keeps the device's in `monitorInRate_`; `pullMonitorInput` splits into the direct path (as before) and `resampleMonitorInput`; the ring read is factored into `readMonitorRing` |
| `src/native/nota.engine/src/engine/Engine.h`, `Engine.cpp` | modified | resampler state; window (4424 frames × 64 columns) and the Kaiser-windowed sinc table built in the constructor |
| `src/native/nota.engine/include/nota/nota_engine.h`, `src/abi/nota_engine_track.cpp` | modified | test hook `nota_engine_set_monitor_input_test(engine, rate, block_frames)` |
| `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs` | modified | `SetMonitorInputForTest` |
| `tests/Nota.SmokeTest/MonitorInputRateTests.cs` | added test | see below |
| `tests/Nota.SmokeTest/Program.cs` | modified | `--monitor-input-rate`, and in the full run |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed |

## Diff Highlights

The resampler (`Engine::resampleMonitorInput`), per output segment:

- Kernel: sinc under a Kaiser window (β 7, ~70 dB), 20 zero crossings each side, tabulated
  (256 points per crossing, linear between them). Cutoff at the lower Nyquist: `1/ratio` of
  the input's when decimating (the kernel widens to `ceil(20 · ratio)` taps a side), the
  input's own when interpolating. Weights are normalised per output frame (unity at DC).
- `need = ceil(frames · ratio) + half + 1` input frames, `target = need + one capture block`;
  `queued` = ring + frames held past the read position. Primes until `queued ≥ target`;
  drops the backlog only past `need + 4 blocks + 256` (a real stall).
- Drift control: `fillAvg` follows `queued` over ~0.5 s. Within a band one block wide around
  `need + 1.5 blocks` nothing is corrected: the sampled queue swings by up to a block as
  capture blocks land, slowly when the two block rates are close, and chasing it bent the
  pitch. Past the band `step = ratio · (1 + clamp(over / (2 · ratio · outRate), ±0.2 %))`.
- A window of input frames (the kernel's history kept, the rest slid down); an empty ring
  mid-segment re-primes; a block of a newer capture layout restarts the window, and the
  output is cleared in the new layout's columns first (review on #620).

## Tests Added or Updated

- `MonitorInputRateTests` (`--monitor-input-rate`, also in the full run). An audio track
  monitors the "hardware" input; a fake capture device delivers 512-frame blocks of a
  330 Hz sine on its own clock while the engine renders 512-frame blocks on another:
  - 48 kHz input → 44.1 kHz engine: continuous (worst second difference 0.00063, the sine's
    own) at 330.0 Hz;
  - 44.1 kHz → 48 kHz: continuous (0.00053) at 330.0 Hz;
  - capture clock 200 ppm fast, 20 s: no click (0.00063), 330.00 Hz;
  - 96 kHz input → 44.1 kHz: a 30 kHz tone (which would alias to 14.1 kHz) is −84 dB against
    a 15 kHz one, which passes whole (review on #620);
  - matching rates: the direct path, unchanged (0.000625, 330.0 Hz).
  The test sets the hook before monitoring is turned on, and the hook puts the engine in input
  test mode: otherwise turning monitoring on opened the machine's real microphone, whose blocks
  landed in the same ring (stray clicks in the first run of the test, and the microphone's rate
  set to the engine's).
- Against the old path (the hook's rate set to 0, so the frames are played one for one) the
  first three fail: worst jump 0.0625 / 0.283 / 0.071, pitch 303.3 Hz.

## Local Verification

- `cmake --build src/native/nota.engine/build` (Release) → builds.
- `dotnet Nota.SmokeTest.dll --monitor-input-rate` → MONITOR INPUT RATE PASSED (7/7), twice.
- Cost, offline render of 20 s at 44.1 kHz in 512-frame blocks, two input columns, median of
  5 (Apple M4 Max): direct 0.14 ms per second of audio; resampled from 48 kHz 2.83 ms, from
  96 kHz 5.24 ms (the kernel is ~44 / ~88 taps). Only while a hardware input is monitored
  through the resampler; once per capture block whatever the number of monitoring tracks.
- Live, real CoreAudio devices (`/private/tmp/n563harness`, not in the repo): a second engine
  plays a 330 Hz sine into BlackHole 2ch; the engine under test monitors BlackHole and outputs
  to the MacBook speakers (master at 0), and a track records the monitoring track:
  - BlackHole at 48 kHz, engine at 44.1 kHz: before, 3.8 clicks/s at 303 Hz; now the input is
    switched to 44.1 kHz, 0 clicks, 330.1 Hz;
  - BlackHole at 44.1 kHz, engine at 48 kHz: before, 8 click events/s; now 0 clicks, 330.1 Hz.
- `scripts/build.sh` (macOS) → 6519 checks pass, 2 fail: the render-pool timing checks in
  `-- render pool --` (`overloaded: ~200 of 4125 blocks late`, `at most 5 late blocks within
  50 ms`). They fail the same way, with the same numbers, on the engine without this change
  (`--perf`, twice on this branch and once on the pre-fix build), with a load average of 7–8.
  They don't touch input monitoring; they need an idle machine or CI. The script stops at
  that failure, so its last step was run by hand: `--scan-scale` → SCAN SCALE PASSED.

## Deviations from Assessment

- Review on #620 (Greptile): the cubic resampler had no anti-alias filter when decimating, and
  a widened capture layout could replay a previous segment's samples. Both fixed (windowed
  sinc; output cleared in the new layout).
- The device's rate has to be set **before** the AUHAL is bound to it: set after binding, the
  unit kept the old hardware format and capture delivered nothing (found in the live check,
  fixed before it shipped).
- The test hook also takes the capture callback size, since the monitor's cushion is sized
  from it. Changing the existing `push_monitor_frames` hook to record it would have resized
  the cushion of the tests that push 8192 frames at once.
- At matching nominal rates the monitor keeps the direct path, so drift between two separate
  devices at the same nominal rate still trims or re-primes now and then, as before. Running
  every separate-clock input through the resampler would cover it, at the cost of the direct
  path's bit-exactness; left as a follow-up.

## Follow-ups

- The resampler path (a device that can't run at the engine's rate) is covered by the smoke
  test only: no Bluetooth or fixed-rate microphone was available to try it live.
- Windows / Linux: the miniaudio change is not built or tested here (macOS only).
- Same-rate drift between separate devices (see Deviations).
- Noticed while assessing: arming a hardware-input track and a track-input track at once
  records only the hardware take.
- Setting the input device's rate changes it system-wide, as the output side already does.
