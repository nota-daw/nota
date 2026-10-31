# Implementation Plan: ASIO audio on Windows

**Branch**: `049-asio-backend` | **Date**: 2026-10-28 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/049-asio-backend/spec.md`

## Summary

Add ASIO as a second audio system on Windows beside WASAPI. One `AsioSession` owns the
single open ASIO driver (loaded through its COM interface, enumerated from the registry)
and backs both engine-facing interfaces: `AsioBackend : AudioBackend` for output and
`AsioInput : AudioInput` for capture. Each driver buffer is converted from the driver's
format, given to the existing input rings, rendered, and converted back, on the driver's
thread. Driver resets reach the app as a polled device event and go through the existing
apply path. Any failure falls back to the miniaudio WASAPI backend. The Steinberg ASIO SDK
2.3.4 is fetched by CPM and used under its GPLv3 option, recorded in the license register.
Details: [research.md](research.md).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine, MSVC on Windows; the platform-independent pieces also build with clang on macOS/Linux), C# / .NET 10 (managed app)

**Primary Dependencies**: **NEW** — Steinberg ASIO SDK 2.3.4 (headers only: `asio.h`, `iasiodrv.h`, `asiosys.h`), GPLv3 option, CPM pinned URL + SHA256, Windows targets only (R1). Existing: miniaudio (WASAPI fallback), nlohmann/json (`audio.json`), Avalonia.

**Storage**: `audio.json` in `%APPDATA%\Nota` gains five keys (data-model.md); no project-file change.

**Audio-Thread Impact**: The ASIO `bufferSwitch` becomes the audio thread on Windows when ASIO is selected. It only converts samples, pushes input frames into the existing lock-free rings (AR-4) and calls the existing render callback — no allocation, locks or I/O (AR-6). All buffers are sized at `createBuffers` on the driver control thread. Driver load/init/reset/control panel run on a dedicated non-real-time thread (R4). `asioMessage` only sets atomics.

**Testing**: `tests/Nota.SmokeTest` calls a new device-free `nota_asio_selftest()` (fake driver, every platform); manual checks on Windows hardware ([quickstart.md](quickstart.md)).

**Target Platform**: Windows 10+ x64 and ARM64 (ASIO); macOS and Linux unchanged (ASIO calls are no-ops).

**Project Type**: desktop app — native engine + managed UI.

**Performance Goals**: 0 xruns for 10 min at 64 frames / 48 kHz on a 16-track project (SC-001); per-buffer conversion cost negligible next to render (O(frames × chosen channels) plus zeroing other outputs).

**Constraints**: one ASIO driver per process; no launch hang > 5 s on a broken driver (control-thread timeouts); reset → audio ≤ 2 s; stereo engine (one pair each way).

**Scale/Scope**: ~10 new engine files (≈ 1.2 k lines), ~15 new C ABI calls, one Preferences section, one license row.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|---|---|---|
| I. Real-time audio safety | ✅ | `bufferSwitch` converts + calls the existing render/input callbacks only; reset/restart off the audio thread; atomics from `asioMessage` (R4, R5) |
| II. Boundary discipline | ✅ | All new surface is plain C in `nota_engine.h` (contracts/c-abi.md); no JUCE — the core engine's own ASIO code, not JUCE's |
| III. Licensing hygiene | ✅ with a recorded decision | New copyleft dependency (ASIO SDK, GPLv3). Compatible with AGPL-3.0-only via GPLv3 §13 / AGPLv3 §13; register row + note + notice + PR rationale (R1). New files carry the SPDX header. See Complexity Tracking |
| IV. Verification gate | ✅ | `nota_asio_selftest` in the smoke test on every platform; `build-win.ps1` compiles the ASIO path |
| V. One logical change per PR | ✅ | One feature; engine, ABI, UI, docs land together (they're unusable apart). Recording latency compensation is generic but only switched on by ASIO |
| VI. Docs in sync | ✅ | CHANGELOG, FEATURES.md (nota-features), user manual EN+RU (nota-docs), BUILD.md, ARCHITECTURE.md table |
| VII. Spec-first | ✅ | spec → clarifications → this plan |
| VIII. Rules not bent | ✅ | No rule is weakened; the dependency goes through the documented policy |

**Post-design re-check (after Phase 1)**: unchanged — the design adds no audio-thread
work beyond conversion, no ABI escape hatches, and the single new dependency is headers
only.

## Project Structure

### Documentation (this feature)

```text
specs/049-asio-backend/
├── spec.md
├── plan.md              # this file
├── research.md          # R1–R11 decisions
├── data-model.md        # AudioConfig fields, session state machine, device events
├── quickstart.md        # automated + manual validation
├── contracts/
│   ├── c-abi.md         # new nota_audio_* calls
│   └── preferences-ui.md
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── cmake/dependencies.cmake              # + CPMAddPackage(asiosdk …) under WIN32, DOWNLOAD_ONLY
├── CMakeLists.txt                        # + windows/ ASIO sources on WIN32; asio_common sources everywhere
├── include/nota/nota_engine.h            # + calls in contracts/c-abi.md
└── src/
    ├── abi/nota_engine_audio.cpp         # C ABI glue, beside the M7-1 calls
    ├── audio_io/
    │   ├── AudioBackend.h                # + input/outputLatencyFrames(), sharesInputClock()
    │   ├── AudioConfig.h                 # + audioSystem, asioDriverClsid/Name, asioOutputPair/InputPair
    │   ├── AudioBackendFactory.{h,cpp}   # choose miniaudio vs ASIO pair by audioSystem; shared session
    │   ├── ConfigJson.cpp                    # read/write the new audio.json keys
    │   ├── asio/                         # platform-independent, built everywhere (self-test)
    │   │   ├── AsioSampleFormat.{h,cpp}  # Int16/24/32(+LSB16..24)/Float32/64 LSB ⇄ float; MSB/DSD refused
    │   │   ├── AsioDriverApi.h           # the IASIO subset AsioSession uses
    │   │   ├── AsioSession.{h,cpp}       # state machine, buffer switch, pair routing, silence, watchdog, events
    │   │   └── FakeAsioDriver.{h,cpp}    # test driver for nota_asio_selftest
    │   └── windows/                      # WIN32 only
    │       ├── AsioDriverList.{h,cpp}    # registry enumeration (HKLM\SOFTWARE\ASIO), de-duplicated names
    │       ├── ComAsioDriver.{h,cpp}     # IASIO adapter + STA control thread + timeouts
    │       ├── AsioBackend.{h,cpp}       # AudioBackend facade over AsioSession
    │       └── AsioInput.{h,cpp}         # AudioInput facade over AsioSession
    └── engine/
        ├── Engine.cpp                    # fallback to WASAPI on ASIO failure; device-event + reason accessors
        └── Engine_Recording.cpp          # latency-compensated take start; no monitor cushion when sharesInputClock()

src/managed/
├── Nota.Infrastructure/Interop/NativeMethods.Audio.cs, NotaEngine.Audio.cs   # mirror the ABI
├── Nota.Application/Engine/IAudioEngine.cs                                    # new members
├── Nota.Presentation/ViewModels/MainWindowViewModel.cs                        # poll device events in the tick; fallback status
└── Nota.App/PreferencesWindow.cs                                              # Audio system row + ASIO rows (contracts/preferences-ui.md)

tests/Nota.SmokeTest/Program.cs           # Check(NotaEngine.AsioSelfTest(), …); ABI no-op checks off Windows

LICENSES/third-party.md, LICENSES/THIRD-PARTY-NOTICES.md   # ASIO SDK row + note 8 (GPLv3 compatibility, trademark)
BUILD.md, ARCHITECTURE.md, CHANGELOG.md, FEATURES.md       # docs; user manual via nota-docs
```

**Structure Decision**: ASIO code lives beside the other device backends in
`src/audio_io/`, split so that everything not needing COM (`asio/`) builds on all
platforms for the self-test, and only the registry/COM/facade layer (`windows/`) is
Windows-only. The engine keeps its two interfaces; only the factory knows a session is
shared.

## Implementation order (for /speckit-tasks)

1. **Dependency & build**: CPM block for the SDK (WIN32), include path for `asio/` on all
   platforms (only our headers there; the SDK enum values `AsioSampleFormat` needs are
   mirrored as our own constants so non-Windows builds don't fetch the SDK). License row,
   note, notice.
2. **Pure core** (`asio/`): sample formats, `AsioDriverApi`, `AsioSession`,
   `FakeAsioDriver`, `nota_asio_selftest` + smoke check. — testable on macOS.
3. **Engine hooks**: `AudioBackend` latency/clock virtuals, recording compensation,
   monitor cushion, `AudioConfig` + JSON keys, factory selection, fallback to WASAPI,
   device-event + reason accessors, C ABI.
4. **Windows layer**: driver list, COM adapter + control thread, facades. Build on
   Windows (`build-win.ps1`).
5. **Managed + UI**: interop, `IAudioEngine`, Preferences rows, device-event polling,
   status texts.
6. **Docs**: BUILD.md (remove "ASIO is not supported yet"), ARCHITECTURE.md platform
   table, CHANGELOG, FEATURES.md, user manual (EN+RU).
7. **Manual validation** on Windows per quickstart.md.

## Risks

- **No Windows hardware in the loop here**: the COM/driver layer can only be compiled in
  CI and tested by hand. Mitigation: keep `windows/` thin; everything stateful lives in
  the fake-testable `AsioSession`.
- **Misbehaving drivers** (hang in `init`, call `bufferSwitch` after `stop`, change
  buffer size without a reset message): control-thread timeouts, an `active_` atomic
  checked at the top of `bufferSwitch`, and a buffer-size check each switch that turns a
  silent change into a reset request.
- **ARM64**: few native ARM64 ASIO drivers exist; the UI explains an empty list.

## Complexity Tracking

| Violation | Why Needed | Simpler Alternative Rejected Because |
|-----------|------------|-------------------------------------|
| New copyleft dependency (ASIO SDK, GPLv3) — CONTRIBUTING.md rejects copyleft deps "not already accounted for" | ASIO is impossible without the SDK's interface definitions; it is the feature | Proprietary SDK license can't be combined with AGPL distribution; an opt-in build was rejected by the user (spec § Clarifications Q2). GPLv3 is AGPL-compatible (§13), so the decision is recorded rather than the rule bent |
