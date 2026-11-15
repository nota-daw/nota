# Bug Fix: Input monitoring clicks when the capture device runs at another rate

- **Slug**: 563-monitor-input-rate
- **Fixed**: 2026-11-15
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The capture device now opens at the engine's rate where it can (macOS: the input device's
nominal rate is set, as the output already did; miniaudio: it is asked for the engine's rate
and converts). Where the rates still differ (Bluetooth, a device that doesn't list the rate),
live monitoring reads the input through a cubic resampler whose step follows the queue, so
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
| `src/native/nota.engine/src/engine/Engine.h`, `Engine.cpp` | modified | resampler state, window sized in the constructor (4104 frames × 64 columns) |
| `src/native/nota.engine/include/nota/nota_engine.h`, `src/abi/nota_engine_track.cpp` | modified | test hook `nota_engine_set_monitor_input_test(engine, rate, block_frames)` |
| `src/managed/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs`, `NotaEngine.Tracks.cs` | modified | `SetMonitorInputForTest` |
| `tests/Nota.SmokeTest/MonitorInputRateTests.cs` | added test | see below |
| `tests/Nota.SmokeTest/Program.cs` | modified | `--monitor-input-rate`, and in the full run |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed |

## Diff Highlights

The resampler (`Engine::resampleMonitorInput`), per output segment:

- `need = ceil(frames · ratio) + 2` input frames, `target = need + one capture block`;
  `queued` = ring + frames held past the read position.
- Primes until `queued ≥ target`; drops the backlog only past `target + 2 blocks + 256` (a
  real stall), as the direct path does.
- Drift control: `fillAvg` follows `queued` over ~0.5 s (the capture blocks' sawtooth
  averages out); `step = ratio · (1 + clamp((fillAvg − target) / (2 · ratio · outRate), ±0.5 %))`.
- Catmull-Rom over a window of input frames (one history frame kept, the rest slid down); an
  empty ring mid-segment re-primes; a block of a newer capture layout restarts the window.

## Tests Added or Updated

- `MonitorInputRateTests` (`--monitor-input-rate`, also in the full run). An audio track
  monitors the "hardware" input; a fake capture device delivers 512-frame blocks of a
  330 Hz sine on its own clock while the engine renders 512-frame blocks on another:
  - 48 kHz input → 44.1 kHz engine: continuous (worst second difference 0.00063, the sine's
    own) at 330.4 Hz;
  - 44.1 kHz → 48 kHz: continuous (0.00053) at 330.3 Hz;
  - capture clock 200 ppm fast, 20 s: no click (0.00063), 330.1 Hz;
  - matching rates: the direct path, unchanged (0.000625, 330.0 Hz).
- Against the old path (the hook's rate set to 0, so the frames are played one for one) the
  first three fail: worst jump 0.0625 / 0.283 / 0.071, pitch 303.3 Hz.

## Local Verification

- `cmake --build src/native/nota.engine/build` (Release) → builds.
- `dotnet Nota.SmokeTest.dll --monitor-input-rate` → MONITOR INPUT RATE PASSED (6/6).
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
