# Bug Assessment: Input monitoring clicks when the capture device runs at another rate

- **Slug**: 563-monitor-input-rate
- **Created**: 2026-11-15
- **Source**: https://github.com/nota-daw/nota/issues/563 (host: github.com — allowlisted), reopened
  2026-11-15 after #567; plus the reporter's description in chat
- **Verdict**: valid (reproduced)
- **Severity**: high

## Report (verbatim or summarized)

> **Input monitoring on an audio track clicks (hardware input or another track)** (#563, `bug`,
> `approved`, reopened)
>
> Monitoring an audio track whose input is a hardware input (a microphone) clicks. Tried two
> interfaces: the laptop's built-in microphone and an external audio interface — both click.
> Monitoring an audio track whose input is another track clicks the same way. It does not depend
> on the block size or the sample rate, and it is not Bluetooth-related. Recording the same input
> is clean. Nota 0.60.1 (with #567), macOS Apple Silicon.

The first round of #563 (`.specify/bugs/563-monitor-crackle/`, PR #567) fixed a different
mechanism: the track-to-track monitor reading stale frames when segment lengths vary. That fix
is in 0.60.1 and is not what the reporter hears now.

## Symptom

An audio track monitoring a hardware input plays it with periodic clicks (several per second) and
at the wrong pitch. Recording the same input is clean. Expected: the monitored input is
continuous and at the right pitch, at a fixed small latency.

## Reproduction

Reproduced on this machine (MacBook Pro, macOS, Apple Silicon) with a scratch harness
(`/private/tmp/n563harness`, not part of the repo). It drives the real CoreAudio backends through
`NotaEngine`. Track M monitors the hardware input, and track R records M (a take of a track's
post-fader output is sample-exact, so the take shows exactly what M played):

1. Default devices: MacBook Pro Microphone (the default input) and MacBook Pro Speakers; the
   engine runs at 44.1 kHz. A hardware take records at **48 000 Hz**, the microphone's own rate.
2. A clean sine (330 Hz) through the BlackHole 2ch loopback, played by a second engine, as the
   input of an engine with a monitoring track M and a track R recording M:

   | Input device rate | Engine / output | Monitored result (from R's take) |
   |---|---|---|
   | 48 kHz (BlackHole) | 48 kHz, same device | clean: 0 clicks, 329.9 Hz |
   | 44.1 kHz (BlackHole) | 44.1 kHz, same device | clean: 0 clicks, 330.1 Hz |
   | 48 kHz (BlackHole) | 44.1 kHz (speakers) | **3.8 clicks/s**, every ~11 500 frames; pitch **303 Hz** (−1.5 semitones) |
   | 44.1 kHz (BlackHole) | 48 kHz (speakers) | **8 click events/s**: a 1024-frame gap every ~11 800 frames; the pitch runs high |

3. Offline (`RenderOffline`, test-fed input via `PushMonitorFramesForTest`, 512-frame blocks,
   transport stopped / playing / armed): clean. Live, track-to-track (an audio clip's track
   monitored, real output device, 512 frames): clean. So the engine's monitor path is sound when
   the input arrives at the engine's rate.

[NEEDS CLARIFICATION: the "another track" case did not reproduce. A track monitoring a playing
clip, live, is clean. Most likely the reporter's source track was itself monitoring the
microphone (an audio track on the mic, routed into another), so the clicks came with it. To
confirm with the reporter.]

## Suspected Code Paths

- `src/native/nota.engine/src/audio_io/macos/CoreAudioInput.mm:96-117` — the capture AUHAL is
  opened at the device's **hardware** rate (`sampleRate_ = hw.mSampleRate`), and the client
  format keeps that rate. Nothing sets the input device's nominal rate to the engine's, and the
  stream is not converted.
- `src/native/nota.engine/src/audio_io/desktop/MiniaudioInput.cpp:42-57` — same on Windows /
  Linux: `ma_device_config` leaves `sampleRate` at 0 (the device's native rate), and
  `sampleRate_` reports it.
- `src/native/nota.engine/src/engine/Engine_Recording.cpp:216` `onInputBlock()` — pushes the
  capture blocks, at the input's rate, into `monitorRing_`.
- `src/native/nota.engine/src/engine/Engine_Recording.cpp:265` `pullMonitorInput()` — pops
  `frames` input frames per output segment, one for one, as if both ran at the engine's rate.
  With a faster input the backlog grows until `avail > keep + cushion + 256` and the excess is
  skipped (a jump = click). With a slower input the ring runs dry, `monitorPrimed_` drops and the
  segment goes silent until a cushion is rebuilt (a gap = two clicks). It handles only small
  clock drift, and even that by dropping or repeating, not by smoothing.
- `src/native/nota.engine/src/engine/Engine_Render.cpp:2539` `renderMonitorInput()` — copies
  `monitorHwBuf_` straight into the track; no rate handling.
- Recording, for comparison: `Engine_Recording.cpp:1139` stamps the take with the input's rate
  (`audioRecordSampleRate_ = input_->sampleRate()`), and the clip is resampled on playback. That
  is why the take is clean.

## Root Cause Hypothesis

Live monitoring assumes the capture device delivers frames at the engine's sample rate, but the
capture device is opened at its own rate and never converted. On a Mac the built-in microphone
runs at 48 kHz while Nota defaults to 44.1 kHz. The monitor then plays 48 000 input frames a second
into a 44 100-frame stream: the audio is pitched down by 1.5 semitones, and about four times a
second `pullMonitorInput` throws away the backlog — a click. The other way round (input slower
than the engine) the ring underruns and the monitor drops out for a block every quarter second.
Recording stores the input at its own rate, so takes are clean. Confidence: **high**. The
mechanism is in the code and reproduced with a clean sine at both rate pairs, and it disappears
when the rates match.

Why the reporter sees it "regardless of sample rate": the input rate is fixed by the device
(Nota sets only the output device's rate), so most engine-rate choices still mismatch the mic.
With an external interface used as the input but not as the output, Nota never sets the
interface's rate either. [NEEDS CLARIFICATION: which output device was in use with the external
interface, and at which rates. If input and output were the same interface at the same rate, a
second mechanism exists: separate clocks drifting, which the drop/re-prime logic also turns into
clicks, only far less often.]

## Proposed Remediation

**Preferred**: convert the capture stream to the engine's rate before it reaches the monitor
ring, and absorb clock drift smoothly instead of dropping or re-priming.

1. **Match the rate at the device where possible.** On macOS, when the input device is not
   Bluetooth and supports the engine's rate, set its nominal rate to the engine's (as
   `CoreAudioBackend` already does for the output). On miniaudio, ask for
   `config.sampleRate = engine rate` (miniaudio converts itself). This alone fixes the common
   case (built-in mic at 48 kHz, engine at 44.1 kHz) with no added DSP.
2. **Otherwise convert in the monitor path.** When the device can't run at the engine's rate,
   or input and output are separate devices with separate clocks: in `pullMonitorInput`, read
   the ring through a small streaming resampler (cubic / windowed-sinc, fixed state per input
   column, RT-safe, no allocation). Its ratio is `inputRate / engineRate`, nudged by a slow
   controller that holds the ring's fill at the cushion target (adaptive resampling, as
   aggregate devices do). Underrun and backlog then only happen on a real stall, not every
   quarter second.
3. Recording keeps capturing at the input's rate, unchanged (the take is stamped with that
   rate). If (1) changes the device rate, the take simply gets the engine's rate.

**Alternatives**:
- Only (1): the smallest change. It doesn't cover devices that can't run at the engine's rate
  (some USB mics are fixed at 48 kHz, Bluetooth inputs), and drift between two devices still
  clicks now and then.
- Only (2): covers everything without touching device settings. More DSP code, and a resampler
  adds a little latency and CPU even when the rates could have matched.
- Refuse or warn: show "Input runs at 48 kHz — monitoring disabled" in Settings. Honest but not
  a fix.

**Files likely to change**:
- `src/native/nota.engine/src/audio_io/macos/CoreAudioInput.mm` (set the nominal rate; report
  the actual rate)
- `src/native/nota.engine/src/audio_io/desktop/MiniaudioInput.cpp` (request the engine's rate)
- `src/native/nota.engine/src/audio_io/AudioInput.h` (`start` takes a wanted rate)
- `src/native/nota.engine/src/engine/Engine_Recording.cpp` (`ensureAudioInput`,
  `pullMonitorInput` with the resampler and the fill controller)
- `src/native/nota.engine/src/engine/Engine.h` (resampler state)
- possibly a new `src/native/nota.engine/src/dsp/primitives/StreamResampler.h`
- `tests/Nota.SmokeTest/` — new test

**Tests to add or update**:
- Smoke test (offline, deterministic): feed the monitor ring a 330 Hz sine "captured" at 48 kHz
  (a test hook that sets the input rate, next to `PushMonitorFramesForTest`) while the engine
  renders at 44.1 kHz in 512-frame blocks, with input pushes timed to match. Assert that the
  monitored output has no jump (second difference below the sine's own) and the right pitch
  (330 Hz by zero crossings). Same for 44.1 kHz input → 48 kHz engine. Both fail today (3.8 and
  8 events/s).
- The same with the input rate drifting by ±100 ppm against the output: no click over 30 s.
- Regression: matched rates stay bit-exact through the monitor, with the same latency as today
  (the existing `monitorSharedClockSelfTest` and #352 meter tests keep passing).
- Live check by hand: MacBook mic at 48 kHz, engine at 44.1 kHz, monitoring a voice: no clicks,
  right pitch.

## Risks & Considerations

- Changing the input device's nominal rate affects other apps using that device (a system-wide
  setting on macOS). The output side already does this for the output device. Never do it for
  Bluetooth (see `isBluetoothDevice`: forcing a rate there breaks the A2DP/HFP link).
- Real-time safety: resampler state sized when the capture layout changes (message thread), not
  on the audio thread.
- Latency: a sinc resampler adds a few frames; the fill controller must keep the cushion small
  (about one capture block, as now).
- The take's sample rate and the latency compensation (`audioRecordLatencyFrames_`) read the
  input's rate; if (1) changes it, they must read the rate after the change.
- ASIO (`monitorSharedClock_`): one device, one clock, already at the engine's rate; stays on
  the direct path.
- Same bug on Windows / Linux (miniaudio opens the native rate): the fix should cover both.

## Open Questions

- [NEEDS CLARIFICATION: in the "from another track" case, was the source track itself monitoring
  the microphone? Live track-to-track monitoring of a clip is clean here.]
- [NEEDS CLARIFICATION: with the external interface, which device was the output, and at which
  rates were input and output? The interface's rate decides whether it's the rate mismatch or
  only clock drift.]
- Out of scope, noticed in passing: arming a hardware-input track and a track-input track at the
  same time records only the hardware take (the internal take doesn't appear). Worth its own
  issue if it isn't intended.
