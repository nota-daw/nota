# Research: ASIO audio on Windows

Phase 0 for [plan.md](plan.md). Each item: decision, rationale, alternatives.

## R1 — License of the ASIO SDK

**Decision**: Use the Steinberg ASIO SDK **2.3.4** under its **GPLv3** option, fetched by
CPM from the pinned URL
`https://download.steinberg.net/sdk_downloads/ASIO-SDK_2.3.4_2025-10-15.zip` (≈ 8.9 MB)
with a `URL_HASH SHA256=…` computed when the task lands. Record it in
`LICENSES/third-party.md` as a new row with a note, plus `THIRD-PARTY-NOTICES.md`.

**Rationale**: Verified 2026-10-28 at the source:
- The SDK's `LICENSE.txt` says it is licensed "under the terms of the Steinberg ASIO
  License, or alternatively under the terms of the General Public License (GPL) Version 3";
  `changes.txt` for 2.3.4: "new DUAL licensing: Proprietary and GPVv3".
- steinberg.net/developers/asiosdk-open: "Steinberg ASIO technology is available in open
  source form under a GNU General Public License, version GPLv3." The name and logo stay
  trademarks; using them is optional and governed by Steinberg's usage guidelines, not the
  GPL.
- GPLv3 §13 and AGPLv3 §13 explicitly allow combining a GPLv3 work with an AGPLv3 work;
  each part keeps its own license and the combined binary is conveyed under AGPLv3 terms
  for Nota's part. Nota is already AGPL-3.0-only and ships full source, so the GPLv3
  source-availability duty is met by the existing release process.
- CONTRIBUTING.md rejects copyleft dependencies "not already accounted for"; this plan
  accounts for it explicitly (register row + note + PR rationale), which is the
  deliberate decision the policy asks for.

**Trademark handling**: UI text says "ASIO" only as the name of the driver type (a
permitted product-related context). No ASIO logo is shipped. "Nota" names nothing
"ASIO…".

**Fetch rather than vendor**: consistent with every other dependency (spec 032 removed
vendored trees). Only `common/asio.h`, `common/iasiodrv.h`, `common/asiosys.h` are
compiled against; no SDK `.cpp` is built (see R3).

**Alternatives**: proprietary license (incompatible with AGPL distribution — rejected);
the unofficial GitHub mirror `audiosdk/asio` (not Steinberg's; rejected in favour of the
official URL); opt-in build flag (rejected by the user in spec § Clarifications).

## R2 — Where the ASIO code lives and how it plugs into the engine

**Decision**: A new `src/native/nota.engine/src/audio_io/windows/` folder with one
**`AsioSession`** object that owns the single open driver, and two thin facades over it:
`AsioBackend : AudioBackend` (output) and `AsioInput : AudioInput` (capture).
`AudioBackendFactory` picks the miniaudio or ASIO pair from `AudioConfig::audioSystem`.

**Rationale**: `AudioBackend` and `AudioInput` are separate objects today, opened by
`Engine::start()` and `Engine::ensureAudioInput()` respectively. ASIO is one duplex
driver with one callback. Keeping both interfaces and backing them with one session
means `Engine`, `Engine_Recording.cpp` (input rings, monitor cushion, record tap) and the
C ABI device flow keep their shape. In `bufferSwitch` the session converts the chosen
input pair to interleaved stereo float and hands it to the registered `InputCallback`
**before** calling the `RenderCallback`, then converts the rendered stereo to the chosen
output pair.

**Alternatives**: a new duplex `AudioDevice` interface replacing both (cleaner long-term,
but touches macOS/Linux paths for no user benefit — rejected for this feature); JUCE's
`AudioIODeviceType::createAudioIODeviceType_ASIO` (violates AR "JUCE only in
pluginhost/" — rejected); RtAudio/PortAudio (a second device library beside miniaudio for
a thin wrapper — rejected).

## R3 — Talking to drivers: SDK host code vs. the COM interface

**Decision**: Compile only the SDK **headers**. Enumerate drivers ourselves from the
registry (`HKEY_LOCAL_MACHINE\SOFTWARE\ASIO\<name>` → `CLSID`, `Description`), create the
driver with `CoCreateInstance(clsid, …, IID = clsid)` and call it through `IASIO`
(`iasiodrv.h`). Do not build `host/asiodrivers.cpp`, `host/pc/asiolist.cpp` or
`common/asio.cpp`.

**Rationale**: the SDK host files keep global singletons (`theAsioDriver`,
`asioDrivers`), use ANSI APIs, and leak on some paths; the COM interface is ~20 methods
and lets `AsioSession` hold the driver pointer itself. Registry enumeration is ~60
lines. 32-bit drivers live under `WOW6432Node` and are skipped by the 64-bit build by
construction (spec edge case). On ARM64 only native ARM64 drivers register in the ARM64
view.

**Alternatives**: SDK host helpers (global state, ANSI) — rejected.

## R4 — Threads

**Decision**:
- **Driver thread** (`AsioSession` control thread): a dedicated thread initialised with
  `CoInitializeEx(COINIT_APARTMENTTHREADED)` that creates, inits, starts, stops, disposes
  the driver and opens `controlPanel()`. Engine calls are marshalled to it and waited on
  with a timeout (SC-005: no launch hangs > 5 s; a timed-out driver is abandoned and the
  WASAPI fallback taken).
- **Buffer thread**: the driver's own thread calling `bufferSwitch` /
  `bufferSwitchTimeInfo`. It is the engine's audio thread: convert, call the input
  callback, render, convert. No allocation, no locks, no I/O (AR-6). On first entry it
  joins MMCSS "Pro Audio" only if the driver did not already (ASIO SDK 2.3 §MMCSS).
- **asioMessage** may arrive on any thread: it only sets atomics (`resetRequested_`,
  `latencyChanged_`, `resync_`) and returns.

**Rationale**: many drivers require that all non-callback calls come from the thread that
created them, and that it pumps messages for the control panel. ASIO callbacks carry no
user-data pointer, so a single static `AsioSession* g_active` routes them — acceptable
because ASIO allows one driver per process anyway.

## R5 — Reset requests, sample-rate changes and the app

**Decision**: `kAsioResetRequest`, `kAsioBufferSizeChange`, `kAsioLatenciesChanged` and
`sampleRateDidChange` set `resetRequested_`. The engine exposes it through the C ABI
(`nota_audio_poll_device_event`); `MainWindowViewModel`'s existing UI tick polls it and
runs the normal apply path, then resumes playback if it was playing. The driver is never
restarted from inside a driver callback.

**Rationale**: re-uses `ApplyAudioSettings()` (stops the playhead clock, restarts the
backend, re-prepares nodes, re-computes PDC) — the one tested restart path. Polled at UI
tick rate (≈ 16–33 ms), so the 2-second budget (FR-009) is dominated by the driver's own
re-init.

**Alternatives**: restart from the driver control thread directly (races the UI thread's
engine calls — rejected).

## R6 — Sample formats

**Decision**: Support `ASIOSTInt16LSB`, `ASIOSTInt24LSB` (packed 3-byte),
`ASIOSTInt32LSB`, `ASIOSTInt32LSB16/18/20/24` (left-justified variants), `ASIOSTFloat32LSB`,
`ASIOSTFloat64LSB`. Refuse every MSB and DSD type with a named error (spec edge case).
Integer → float scales by `1/2^(bits-1)`; float → integer clamps to [-1, 1) and rounds,
no dither (the master is already float and dither is an export concern).

**Rationale**: these cover every Windows driver in practice; Int32LSB and Float32LSB are
the common ones. Conversions are pure functions in `AsioSampleFormat.{h,cpp}`,
platform-independent, so the smoke test exercises them on macOS and Linux too.

## R7 — Latency reporting and recording compensation

**Finding**: the engine has **no hardware-latency compensation for recording today** on
any platform (plugin delay compensation exists; device latency does not).

**Decision**: add `virtual int32_t inputLatencyFrames() const { return 0; }` and
`outputLatencyFrames()` to `AudioBackend`; `AsioBackend` returns `ASIOGetLatencies`
values. When a hardware-input take is materialised, the engine shifts the clip start
earlier by `input + output` latency frames. Backends returning 0 (CoreAudio, miniaudio)
behave exactly as today. The Latency line shows output latency (and round trip when an
input is in use).

**Rationale**: FR-010 / SC-006 need it; making it backend-generic keeps one code path and
leaves room for CoreAudio to report its latencies later (out of scope here).

## R8 — Monitoring cushion in duplex mode

**Decision**: when input and output share a clock (ASIO), `pullMonitorInput` uses no
cushion: input for block *n* is pushed in the same callback before block *n* renders, so
the queue always holds exactly the current block. A backend flag
`sharesInputClock()` (default false) selects it.

**Rationale**: the current one-block cushion exists because WASAPI/CoreAudio capture and
playback run on separate clocks. Keeping it under ASIO would add a full buffer of
monitoring latency for nothing.

## R9 — Channel pair selection

**Decision** (spec Q1 → A): `AudioConfig` gains `asioOutputPair` and `asioInputPair` —
the index of the first channel of the pair (0, 2, 4, …). The UI lists pairs by the
driver's channel names ("Out 1/2", "Analog 3/4"). A pair whose second channel doesn't
exist is mono (spec edge case: down-sum on output, duplicate on input). All other
outputs get zeros every buffer (FR-006): output buffers are cleared once at
`createBuffers` and the session writes only the chosen pair, plus re-zeroes all others
each switch (drivers may not preserve buffer contents).

## R10 — Persistence and migration

**Decision**: `audio.json` (existing, nlohmann/json, atomic write) gains
`"audioSystem": "wasapi" | "asio"`, `"asioDriver": "<CLSID string>"`,
`"asioDriverName": "<name>"`, `"asioOutputPair": 0`, `"asioInputPair": 0`. Missing keys
mean today's behaviour (WASAPI). The driver is matched by CLSID first, then by name
(drivers reinstalled with a new CLSID), with duplicate names disambiguated by a
` (2)` suffix in display only.

## R11 — Testing without a driver

**Decision**: `AsioSession` talks to drivers through a small internal interface
`AsioDriverApi` (the subset of `IASIO` used). On Windows `ComAsioDriver` adapts `IASIO`;
everywhere, `FakeAsioDriver` (test-only, compiled into the engine like other self-tests)
simulates a driver with configurable formats, channel counts, latencies, a reset request
and a "stops calling back" failure, and drives `bufferSwitch` from a test thread.
New self-test `nota_asio_selftest()` covers format conversion, pair routing and silence,
duplex ordering, reset → event, failure → fallback flag and latency compensation. The
smoke test calls it on every platform. Windows CI has no ASIO driver, so the real-driver
path is verified by hand (quickstart.md).

**Rationale**: FR-016; matches the project's existing `*SelfTest()` pattern
(`XrunSelfTest`, `AudioRecordSelfTest`).
