---

description: "Task list for ASIO audio on Windows"
---

# Tasks: ASIO audio on Windows

**Input**: Design documents from `specs/049-asio-backend/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Requested by the spec (FR-016) — the device-free `nota_asio_selftest()` driven by
a fake driver, called from `tests/Nota.SmokeTest`. Each story adds its own cases to it.
Real-driver checks are manual ([quickstart.md](quickstart.md)).

**Organization**: Tasks are grouped by user story so each story can be implemented and tested on its own.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: Can run in parallel (different files, no dependencies)
- **[Story]**: Which user story this task belongs to (US1–US4)

## Path Conventions

- Engine root: `src/native/nota.engine/` (abbreviated `engine/` below). C ABI: `engine/include/nota/nota_engine.h`
- ASIO core (all platforms): `engine/src/audio_io/asio/`; Windows layer: `engine/src/audio_io/windows/`
- Managed: `src/managed/<Project>/`; smoke test: `tests/Nota.SmokeTest/Program.cs`
- Every new source file starts with the two-line AGPL-3.0-only SPDX header (CONTRIBUTING.md)

---

## Phase 1: Setup (Shared Infrastructure)

**Purpose**: dependency, license record, build wiring

- [X] T001 Add a `CPMAddPackage(NAME asiosdk URL "https://download.steinberg.net/sdk_downloads/ASIO-SDK_2.3.4_2025-10-15.zip" URL_HASH "SHA256=<computed>" DOWNLOAD_ONLY YES)` block guarded by `if(WIN32)` in `engine/cmake/dependencies.cmake`; compute the SHA256 from the downloaded zip and pin it (research R1)
- [X] T002 [P] Add the ASIO SDK row to `LICENSES/third-party.md` (version 2.3.4, purpose "ASIO driver I/O (Windows)", license **GPL-3.0 (dual: proprietary / GPLv3, used under GPLv3)**, status "✅ in use — CPM pinned zip, headers only, Windows only — see note 8") and write note 8: GPLv3 §13 / AGPLv3 §13 combination, headers-only use, no logo shipped, "ASIO" used only as the driver-type name per Steinberg's trademark rules, the source of the license text (SDK `LICENSE.txt`, steinberg.net/developers/asiosdk-open)
- [X] T003 [P] Add the ASIO SDK notice (copyright "(c) 2025, Steinberg Media Technologies GmbH", GPLv3, where to get it) to `LICENSES/THIRD-PARTY-NOTICES.md`; confirm the GPLv3 text is in `LICENSES/` (add `GPL-3.0-only.txt` if missing)
- [X] T004 In `engine/CMakeLists.txt` add `src/audio_io/asio/*.cpp` to the engine sources on every platform and `src/audio_io/windows/*.cpp` plus `${asiosdk_SOURCE_DIR}/common` include path under `WIN32`; define `NOTA_HAS_ASIO=1` on Windows only

---

## Phase 2: Foundational (Blocking Prerequisites)

**Purpose**: the platform-independent ASIO core, the config/interface hooks and the self-test entry point that every story builds on

**⚠️ CRITICAL**: no user story work can begin until this phase is complete

- [X] T005 [P] Create `engine/src/audio_io/asio/AsioSampleFormat.h/.cpp`: our own enum mirroring the ASIO sample-type values (so non-Windows builds need no SDK), and RT-safe pure functions `toFloat(const void* src, AsioSample type, float* dst, int frames, int stride)` / `fromFloat(...)` for Int16LSB, Int24LSB (packed 3-byte), Int32LSB, Int32LSB16/18/20/24, Float32LSB, Float64LSB; integer→float scales by `1/2^(bits-1)`, float→integer clamps to [-1, 1) and rounds, no dither; `isSupported(type)` returns false for every MSB and DSD type (research R6)
- [X] T006 [P] Create `engine/src/audio_io/asio/AsioDriverApi.h`: abstract interface with the IASIO subset AsioSession uses — `init`, `getDriverName`, `getChannels`, `getBufferSize`, `canSampleRate`, `getSampleRate`, `setSampleRate`, `getChannelInfo`, `createBuffers`, `disposeBuffers`, `start`, `stop`, `getLatencies`, `controlPanel`, `outputReady` — plus a `Callbacks` struct (bufferSwitch, sampleRateDidChange, asioMessage) (research R3, R11)
- [X] T007 Create `engine/src/audio_io/asio/AsioSession.h/.cpp`: owns one `AsioDriverApi`, implements the state machine in data-model.md (`Closed → Loaded → Prepared → Running`, `Failed(reason)` with reasons `NotInstalled, LoadFailed, InitFailed, UnsupportedFormat, NoOutputs, Timeout, Stopped`), allocates all conversion buffers in `prepare()` (never in the callback), and in `bufferSwitch(index)`: checks an `active_` atomic, converts the chosen input pair to interleaved stereo and calls the registered `InputCallback`, calls the `RenderCallback`, converts the stereo result to the chosen output pair, writes zeros to every other output channel each switch (FR-006), down-sums to a mono output / duplicates a mono input (spec edge case), calls `outputReady()` when the driver supports it. A single static `AsioSession* g_active` routes the callbacks (research R4). `asioMessage` only sets atomics (`resetRequested_`, `xrunPending_`) and answers `kAsioSelectorSupported` / `kAsioEngineVersion` / `kAsioSupportsTimeInfo` (AR-6) (depends on T005, T006)
- [X] T008 Create `engine/src/audio_io/asio/FakeAsioDriver.h/.cpp`: test-only `AsioDriverApi` with configurable channel counts and names, sample type per channel, buffer size, sample rates, input/output latencies, a scripted input signal, and switches for "init fails", "reports reset request", "stops calling back"; drives `bufferSwitch` from its own thread or synchronously via `pump(n)` and records what the host wrote to every output channel (depends on T006)
- [X] T009 [P] Extend `AudioConfig` in `engine/src/audio_io/AudioConfig.h` with `AudioSystem audioSystem = AudioSystem::Wasapi` (`enum class AudioSystem { Wasapi, Asio }`), `std::string asioDriverClsid`, `std::string asioDriverName`, `int32_t asioOutputPair = 0`, `int32_t asioInputPair = 0`; read/write them in `engine/src/audio_io/ConfigJson.cpp` as `"audioSystem": "wasapi" | "asio"`, `"asioDriver"`, `"asioDriverName"`, `"asioOutputPair"`, `"asioInputPair"` — missing keys mean today's behaviour (WASAPI); `audioSystem` reads as Wasapi on non-Windows (data-model.md, research R10)
- [X] T010 [P] Add to `AudioBackend` in `engine/src/audio_io/AudioBackend.h`: `virtual int32_t inputLatencyFrames() const { return 0; }`, `virtual int32_t outputLatencyFrames() const { return 0; }`, `virtual bool sharesInputClock() const { return false; }`, `virtual int32_t pollDeviceEvent() { return 0; }`, `virtual const char* fallbackReason() const { return ""; }` — CoreAudio and miniaudio keep the defaults (research R7, R8)
- [X] T011 Add `NOTA_API int32_t nota_asio_selftest(void);` to `engine/include/nota/nota_engine.h` and implement it in `engine/src/abi/nota_engine_audio.cpp` as a runner over `AsioSession` + `FakeAsioDriver` that returns 1 only if every case passes; start with the format cases: round-trip of ±full-scale, 0 and a sine through every supported type within 1 LSB, MSB/DSD refused (depends on T005, T007, T008)
- [X] T012 Mirror the self-test in managed code — `NativeMethods.AsioSelfTest` in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Audio.cs`, `public static bool AsioSelfTest()` in `src/managed/Nota.Infrastructure/Interop/NotaEngine.Audio.cs` — and add `Check(NotaEngine.AsioSelfTest(), "ASIO self-test (fake driver): formats, pair routing, duplex, reset, fallback, latency");` in `tests/Nota.SmokeTest/Program.cs` next to the other device-free self-tests (depends on T011)

**Checkpoint**: `scripts/build.sh` green on macOS with the new core compiled and its format cases passing

---

## Phase 3: User Story 1 — Play through an ASIO driver (Priority: P1) 🎯 MVP

**Goal**: choose ASIO + a driver in Preferences, hear the project through the chosen output pair, settings survive a relaunch

**Independent Test**: on Windows with any ASIO driver (or FlexASIO), select ASIO and the driver, play the test tone — sound on the chosen pair, Latency line shows the driver's buffer and rate (quickstart steps 1–3)

### Tests for User Story 1

- [X] T013 [P] [US1] Add self-test cases in `engine/src/abi/nota_engine_audio.cpp`: a fake 8-out driver with output pair 2 gets the render on channels 2/3 and exact zeros on 0,1,4–7 for 100 switches; a 1-out driver gets the mono down-sum; Int32LSB and Float32LSB drivers both produce the expected samples

### Implementation for User Story 1

- [X] T014 [P] [US1] Create `engine/src/audio_io/windows/AsioDriverList.h/.cpp`: enumerate `HKEY_LOCAL_MACHINE\SOFTWARE\ASIO\*` (native registry view only — no `WOW6432Node`, so 32-bit drivers are skipped) reading `CLSID` and `Description`; return `AsioDriverInfo { clsid, name, displayName }` with duplicate names suffixed " (2)", " (3)" for display; `find(clsid, name)` matches CLSID first, then name (research R3, R10)
- [X] T015 [US1] Create `engine/src/audio_io/windows/ComAsioDriver.h/.cpp`: an `AsioDriverApi` over `IASIO` (`iasiodrv.h`) created with `CoCreateInstance(clsid, nullptr, CLSCTX_INPROC_SERVER, clsid, …)`; all calls run on one dedicated control thread initialised with `CoInitializeEx(COINIT_APARTMENTTHREADED)` that pumps messages, each call posted and waited on with a timeout (5 s for load/init, 2 s otherwise) — a timeout returns `Failed(Timeout)` and abandons the driver object (research R4, SC-005) (depends on T006)
- [X] T016 [US1] Create `engine/src/audio_io/windows/AsioBackend.h/.cpp` implementing `AudioBackend` over a shared `AsioSession`: `start()` loads the configured driver, applies the requested sample rate if non-zero and supported, ignores `bufferFrames` (driver decides, FR-008), opens the output pair, starts; reports `sampleRate()`, `bufferFrames()`, `outputLatencyFrames()`; `stop()` stops and disposes and releases the driver (FR-012); xruns from `kAsioOverload`/resync feed `setXrunCallback` (FR-014) (depends on T007, T015)
- [X] T017 [US1] Change `engine/src/audio_io/AudioBackendFactory.h/.cpp` so `createAudioBackend(const AudioConfig&)` returns `AsioBackend` when `NOTA_HAS_ASIO` and `audioSystem == Asio`, else today's backend; keep one process-wide `AsioSession` shared by the ASIO facades; recreate the backend in `Engine::applyAudioConfig()` (`engine/src/engine/Engine.cpp`) when the audio system changes (depends on T016)
- [X] T018 [US1] Add the system/driver/pair calls from `contracts/c-abi.md` to `engine/include/nota/nota_engine.h` and `engine/src/abi/nota_engine_audio.cpp`: `nota_audio_set_system`, `nota_audio_system`, `nota_audio_active_system`, `nota_audio_asio_available`, `nota_audio_asio_driver_count/_id/_name` (filled by `nota_audio_refresh_devices`), `nota_audio_set_asio_driver`, `nota_audio_asio_driver`, `nota_audio_asio_channel_count/_name`, `nota_audio_set_asio_pair`, `nota_audio_asio_pair`, `nota_audio_output_latency_frames`; on macOS/Linux every one is a no-op (counts 0, available 0, setters `NOTA_OK`) (depends on T009, T017)
- [X] T019 [US1] Mirror them in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Audio.cs`, `src/managed/Nota.Infrastructure/Interop/NotaEngine.Audio.cs` and `src/managed/Nota.Application/Engine/IAudioEngine.cs` (`AudioSystem`, `ActiveAudioSystem`, `AsioAvailable`, `AsioDrivers`, `SetAsioDriver`, `AsioChannels(bool input)`, `AsioPair(bool input)`, `SetAsioPair`, `OutputLatencyFrames`); update any test doubles implementing `IAudioEngine` (depends on T018)
- [X] T020 [US1] In `src/managed/Nota.App/PreferencesWindow.cs` (Windows only) add the "Audio system" row (WASAPI | ASIO) and, when ASIO is chosen, the Driver row and Output-pair row per `contracts/preferences-ui.md`, hiding the WASAPI Output/Input/Exclusive rows; pairs are labelled from the driver's channel names ("Out 1 + 2"); each change calls `Apply()`; with no driver installed the ASIO option is disabled with the caption "No ASIO driver found. Install your interface's driver, or a generic one such as FlexASIO." and an ARM64-specific variant of the hint; follow DESIGN.md (nota-design skill) (depends on T019)
- [X] T021 [US1] Update `UpdateLatency()` in `src/managed/Nota.App/PreferencesWindow.cs` and the apply status in `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs` to show "ASIO · <driver>" and the output latency from `OutputLatencyFrames` when ASIO is active
- [X] T022 [US1] Add a smoke check in `tests/Nota.SmokeTest/Program.cs` that the new ABI is a safe no-op off Windows (`AsioAvailable == false`, driver count 0, setting system ASIO + apply leaves the active system WASAPI/native) and that `audio.json` keys round-trip through the engine's config load/save without touching the real backend

**Checkpoint**: US1 works on its own — ASIO playback, persistence, no-driver hint

---

## Phase 4: User Story 2 — Record through the same ASIO driver (Priority: P1)

**Goal**: inputs come from the same driver and clock; takes are aligned using the driver's reported latency

**Independent Test**: loopback a click out 1/2 → in 1/2, record 10 minutes — recorded clicks align with the source within one sample (quickstart step 6)

### Tests for User Story 2

- [X] T023 [P] [US2] Add self-test cases in `engine/src/abi/nota_engine_audio.cpp`: with a fake duplex driver the input callback for switch *n* is called before render of switch *n*; input pair 2 of a 4-in driver reaches the callback; a mono input is duplicated; a take recorded through the fake driver with input latency 100 and output latency 150 frames starts 250 frames earlier than the uncompensated position

### Implementation for User Story 2

- [X] T024 [P] [US2] Create `engine/src/audio_io/windows/AsioInput.h/.cpp` implementing `AudioInput` over the shared `AsioSession`: `start(cb, device)` registers the callback with the session (it never opens a second driver), ignores `device`, reports the session's sample rate; `stop()` unregisters; `inputLatencyFrames()` via the session (research R2) (depends on T007)
- [X] T025 [US2] Make `createAudioInput()` in `engine/src/audio_io/AudioBackendFactory.cpp` return `AsioInput` bound to the shared session when ASIO is the active system (after fallback, the WASAPI input), and make `AsioBackend::inputLatencyFrames()` / `sharesInputClock()` (true) report the driver values in `engine/src/audio_io/windows/AsioBackend.cpp` (depends on T017, T024)
- [X] T026 [US2] In `engine/src/engine/Engine_Recording.cpp`, when a hardware-input take is materialised, shift its start earlier by `backend_->inputLatencyFrames() + backend_->outputLatencyFrames()` (0 on every non-ASIO backend, so no behaviour change there) (research R7)
- [X] T027 [US2] In `Engine::pullMonitorInput` (`engine/src/engine/Engine_Recording.cpp`) use no cushion when `backend_->sharesInputClock()` — pop exactly the current block (research R8)
- [X] T028 [US2] Add `nota_audio_input_latency_frames` to `engine/include/nota/nota_engine.h` / `engine/src/abi/nota_engine_audio.cpp` and `InputLatencyFrames` to the managed interop and `IAudioEngine`; show the round trip on the Latency line when an input is in use (`src/managed/Nota.App/PreferencesWindow.cs`)
- [X] T029 [US2] Add the Input-pair row for ASIO in `src/managed/Nota.App/PreferencesWindow.cs`, listing only the open driver's input pairs; a driver with no inputs shows "No inputs on this driver" and recording reports the existing no-input message

**Checkpoint**: US1 + US2 — play and record through one ASIO driver

---

## Phase 5: User Story 3 — Change buffer size and sample rate (Priority: P2)

**Goal**: open the driver's panel; driver resets are picked up within 2 s without a restart; sample-rate requests are honoured or refused visibly

**Independent Test**: change the buffer in the driver panel during playback — audio resumes ≤ 2 s and the Latency line updates, 10 of 10 times (quickstart steps 4–5)

### Tests for User Story 3

- [X] T030 [P] [US3] Add self-test cases in `engine/src/abi/nota_engine_audio.cpp`: a fake `kAsioResetRequest` / `kAsioBufferSizeChange` / `kAsioLatenciesChanged` / `sampleRateDidChange` each produce exactly one `RESET_REQUESTED` event; a driver whose buffer size changes without a message is detected at the next switch and also produces one; an unsupported `setSampleRate` returns a refusal and leaves the running rate unchanged

### Implementation for User Story 3

- [X] T031 [US3] In `AsioSession` (`engine/src/audio_io/asio/AsioSession.cpp`) turn reset-type messages and an unexpected buffer size into `resetRequested_`, exposed through `AsioBackend::pollDeviceEvent()` returning `1` once per request (clear on read) (data-model.md "Device event")
- [X] T032 [US3] Add `nota_audio_poll_device_event` and `nota_audio_asio_open_control_panel` (posted to the control thread, returns immediately) to `engine/include/nota/nota_engine.h` / `engine/src/abi/nota_engine_audio.cpp`, and `PollDeviceEvent()` / `OpenAsioControlPanel()` to the managed interop and `IAudioEngine`
- [X] T033 [US3] In `MainWindowViewModel`'s engine tick (`src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs`, next to `ReportDropouts()`), poll the device event; on `RESET_REQUESTED` remember play state + position, run `ApplyAudioSettings()`, then resume playback at the same position if it was playing; status "Audio: driver settings changed · <n> frames" (FR-009)
- [X] T034 [US3] In `src/managed/Nota.App/PreferencesWindow.cs` add the "Driver settings…" button (enabled only while ASIO runs), make Buffer size read-only text "<n> frames (set in the driver's settings)" under ASIO, and on a refused sample rate revert the row and show the caption "The driver doesn't support <rate>" (FR-007, FR-008)
- [X] T035 [US3] Make `AsioBackend::start()` (`engine/src/audio_io/windows/AsioBackend.cpp`) report a refused sample-rate request so `nota_audio_apply` can surface it (the session keeps the driver's current rate instead of failing)

**Checkpoint**: US1–US3 — reset handling and the driver panel work

---

## Phase 6: User Story 4 — Recover when the driver fails (Priority: P2)

**Goal**: a missing, busy, broken or vanished driver never hangs or crashes Nota; it falls back to WASAPI with a reason and returns to ASIO when possible

**Independent Test**: launch with the saved driver unplugged or held by another DAW — Nota starts on WASAPI within 5 s, the status names the driver and reason, Preferences still shows ASIO (quickstart steps 7–9)

### Tests for User Story 4

- [X] T036 [P] [US4] Add self-test cases in `engine/src/abi/nota_engine_audio.cpp`: fake "init fails" → `Failed(InitFailed)` with a non-empty reason; unsupported (MSB) output type → `Failed(UnsupportedFormat)`; "stops calling back" while running → `DRIVER_FAILED` event within 2 s of the last callback; after `stop()` a late `bufferSwitch` from the fake is ignored (no render call)

### Implementation for User Story 4

- [X] T037 [US4] Add a watchdog in `AsioSession` (`engine/src/audio_io/asio/AsioSession.cpp`): the buffer callback stores a tick; the control thread checks it every 250 ms while Running and moves to `Failed(Stopped)` after 2 s without a callback, raising `DRIVER_FAILED` (data-model.md)
- [X] T038 [US4] In `Engine::start()` / `applyAudioConfig()` (`engine/src/engine/Engine.cpp`): if the ASIO backend fails to start, keep its reason, create the miniaudio backend and start it with system defaults (`outputDeviceUid = ""`, rate/buffer 0), keep `config_.audioSystem = Asio` in the saved config (FR-011), and make `nota_audio_apply` return `NOTA_OK` unless nothing at all opened
- [X] T039 [US4] Add `nota_audio_fallback_reason` to `engine/include/nota/nota_engine.h` / `engine/src/abi/nota_engine_audio.cpp` (driver name + human reason, "" when not in fallback) and `AudioFallbackReason` to the managed interop and `IAudioEngine`
- [X] T040 [US4] In `src/managed/Nota.Presentation/ViewModels/MainWindowViewModel.cs`: after launch and after every apply, if `AudioSystem == Asio && ActiveAudioSystem != Asio` set status "ASIO driver ‘<name>’ couldn't start — using WASAPI. <reason>"; on a `DRIVER_FAILED` event run `ApplyAudioSettings()` (which falls back), keeping the transport position
- [X] T041 [US4] In `src/managed/Nota.App/PreferencesWindow.cs` show "ASIO unavailable (<reason>) → WASAPI" on the Latency line while in fallback; reselecting the driver re-applies and returns to ASIO when it opens (US4-3)

**Checkpoint**: all four stories work independently

---

## Phase 7: Polish & Cross-Cutting Concerns

- [X] T042 [P] Update `BUILD.md` § Windows platform details: audio is WASAPI or ASIO (SDK fetched by CPM, GPLv3); remove "ASIO is not supported yet"; mention FlexASIO for testing without hardware
- [X] T043 [P] Update the platform table and § Audio engine in `ARCHITECTURE.md` (Windows: "WASAPI via miniaudio, or ASIO (`audio_io/windows/`)"; one shared duplex session behind `AudioBackend` + `AudioInput`)
- [X] T044 [P] Refresh the header comments of `engine/src/audio_io/AudioBackend.h`, `AudioInput.h`, `AudioConfig.h` and the C ABI block comment in `engine/include/nota/nota_engine.h` (they still say "CoreAudio" / "~/Library" only)
- [X] T045 Review the buffer-switch path in `engine/src/audio_io/asio/AsioSession.cpp` against AR-6 (no allocation, locks, logging or `std::function` copies; callbacks stored before start) and against misbehaving drivers (late callbacks, double `start`)

### Nota closing tasks *(always include — these are the PR gates)*

- [ ] T046 Run `scripts/build.sh` (macOS) until native + managed + smoke are green; run `pwsh scripts/build-win.ps1` on Windows (or confirm the Windows CI job) so the SDK fetch and the `windows/` layer compile and the smoke test passes without an ASIO driver
  - *2026-10-28: macOS `scripts/build.sh` green (native, managed, smoke incl. ASIO self-test, scan-scale). Windows build (`build-win.ps1` / CI) not run yet — the `windows/` layer is uncompiled.*
- [X] T047 AGPL-3.0-only SPDX two-line header on every new source file under `engine/src/audio_io/asio/` and `engine/src/audio_io/windows/`
- [ ] T048 New dependency: ASIO SDK row + note 8 in `LICENSES/third-party.md` and notice in `LICENSES/THIRD-PARTY-NOTICES.md` done (T002, T003); the PR description carries the GPLv3 ↔ AGPLv3 rationale (nota-pr skill)
  - *Register row, note 8, notice and `LICENSES/GPL-3.0-only.txt` done; the PR-description rationale remains (nota-pr).*
- [X] T049 `CHANGELOG.md` `[Unreleased]` entry under Added: ASIO on Windows — choose the driver and channel pair, driver settings, low-latency recording with latency compensation, automatic WASAPI fallback
- [X] T050 FEATURES.md via the `nota-features` skill (Windows platform line + Preferences → Audio); user manual EN + RU via the `nota-docs` skill (Preferences → Audio page, Windows setup); shortcuts: N/A
- [X] T051 Engine behaviour change → smoke test covers it via `nota_asio_selftest` (T011–T013, T023, T030, T036) and the ABI no-op check (T022)
- [ ] T052 Manual validation on a Windows machine per `quickstart.md` steps 1–10; record results (or "pending — no Windows hardware") in the PR
  - *Pending — no Windows hardware in this session.*

---

## Dependencies & Execution Order

### Phase Dependencies

- **Setup (Phase 1)**: none. T004 needs T001 for the include path
- **Foundational (Phase 2)**: after Setup; blocks all stories. T007 needs T005 + T006; T008 needs T006; T011 needs T005/T007/T008; T012 needs T011
- **US1 (Phase 3)**: after Foundational — the MVP
- **US2 (Phase 4)**: after Foundational; T025 needs US1's factory change (T017). Independently testable through the fake driver
- **US3 (Phase 5)**: after US1 (needs a running ASIO backend and the Preferences rows)
- **US4 (Phase 6)**: after US1 (needs the ASIO backend to fail from)
- **Polish (Phase 7)**: after the stories you ship

### Within Each Story

- Self-test cases first (they fail until the code lands), then engine, then ABI, then managed, then UI

### Parallel Opportunities

- T002 and T003 alongside T001
- T005, T006, T009, T010 together (different files)
- In US1: T013 and T014 together, then T015
- In US2: T023 and T024 together
- US3 and US4 can run in parallel once US1 is done (different files except `AsioSession.cpp` — T031 and T037 touch it; do them one after the other)
- T042, T043, T044 together

---

## Parallel Example: Phase 2

```bash
Task: "AsioSampleFormat conversions in engine/src/audio_io/asio/AsioSampleFormat.cpp"
Task: "AsioDriverApi interface in engine/src/audio_io/asio/AsioDriverApi.h"
Task: "AudioConfig fields + ConfigJson keys in engine/src/audio_io/AudioConfig.h, ConfigJson.cpp"
Task: "AudioBackend latency/clock virtuals in engine/src/audio_io/AudioBackend.h"
```

## Parallel Example: User Story 1

```bash
Task: "Self-test cases for pair routing and silence in engine/src/abi/nota_engine_audio.cpp"
Task: "Registry driver enumeration in engine/src/audio_io/windows/AsioDriverList.cpp"
```

---

## Implementation Strategy

### MVP First (User Story 1 only)

1. Phase 1 + Phase 2 — the core compiles and its format tests pass on macOS
2. Phase 3 — ASIO playback with driver choice and persistence
3. **Stop and validate** on Windows (quickstart steps 1–3), even before recording exists

### Incremental Delivery

1. Setup + Foundational → core verified by the fake driver
2. + US1 → playback (MVP)
3. + US2 → recording, the reason people buy interfaces — ship US1 + US2 together if possible
4. + US3 → driver panel and resets
5. + US4 → robust fallback
6. Polish + gates → one PR (constitution V: the pieces are unusable apart)

---

## Notes

- Windows-only code can only be compiled in CI or on a Windows machine from here; keep `windows/` thin and the logic in `asio/`, where the fake driver tests it on every platform
- Don't commit — the user reviews and commits (project memory)
- [P] = different files, no dependency on an unfinished task
