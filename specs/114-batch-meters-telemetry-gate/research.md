# Research: Batch Track Meter Snapshots and On-Demand Device Telemetry

**Phase**: Phase 1 of [nota-daw/nota#1050](https://github.com/nota-daw/nota/issues/1050)  
**Date**: 2026-11-24  
**Spec**: [spec.md](spec.md)

## 1. Track Meter Polling Bottleneck (`O(T^2)` per UI frame)

- **Decision**: Add `nota_engine_read_track_meters(const NotaEngine*, NotaTrackMeterEntry*, int32_t capacity)` returning all track meters in one graph pass, and back `IAudioEngine.ReadTrackMeters(Span<TrackMeterReading>)` in C# so `ArrangementView`, `MixerView`, and `SessionView` populate a single reused lookup map once per UI refresh pass (`30 Hz`).
- **Evidence**:
  - `ArrangementView.UpdateMeters()` iterates `_meters` and `_footerMeters`, calling `TryGetTrackMeter(id)` individually.
  - `MixerView.UpdateMeters()` and `MixerView.UpdateHiddenState()` iterate visible strips and hidden group members calling `TryGetTrackMeter(id)` individually.
  - `SessionView.Mixer.cs` calls `TryGetTrackMeter(c.TrackId)` per channel strip.
  - In C++, `Engine::trackMeter(trackId, ...)` calls `findTrackAuthoring(trackId)`, which performs a linear scan over `authoring_->tracks` copying `std::shared_ptr<Track>` for each lookup — turning `T` strip updates into `O(T^2)` iterations and `T` P/Invoke calls per tick.
- **Alternatives Considered**:
  - *Hash map on `Engine` keyed by `trackId`*: Would still keep `T` P/Invoke crossings per tick. Rejected because batching crosses the boundary once in `O(T)` and requires zero dynamic allocations in the C++ snapshot read.
  - *Keep old `nota_engine_track_meter` only*: Rejected as primary path because UI views still pay `O(T)` managed/native transitions and linear lookups. Keeping it only for targeted single-track callers (e.g., MCP `get_track_meters`, ProtocolSurface, legacy smoke tests) maintains compatibility.

## 2. Per-Device CPU Clock Telemetry (`steady_clock::now()`)

- **Decision**: Introduce `nota::dsp::DeviceTelemetry` (`src/native/nota.engine/src/dsp/core/DeviceTelemetry.h`) using RAII `ScopedBlockTimer` and an audio-frame-counted observation lease renewed automatically inside `scopeRead()`.
- **Evidence**:
  - Across `src/native/nota.engine/src/devices/`, 24 built-in devices (`22` effects + `Consort` and `Pentad` instruments) duplicate `double cpuS_` / `std::atomic<float> cpuA_` boilerplate and invoke `std::chrono::steady_clock::now()` twice on every audio block even when zero device cards are open.
  - Some effects (e.g., `Shutter.cpp`, `Utility.cpp`) had duplicated timing cleanup on early return branches. An RAII `ScopedBlockTimer` handles early returns cleanly with zero duplication.
  - `AutoFilterDeviceBody.cs:372` checked `Sc(S_Cpu) > 0` as a proxy for "has audio processed at least once" rather than `Sc(S_SampleRate) > 0` (or active audio). Switching `AutoFilterDeviceBody.cs` to check `Sc(S_SampleRate) > 0` avoids relying on always-on CPU timing side effects on the first frame.
  - Tests such as `Reverb` (`Program.cs:5408`), `AutoFilter`, `EqAnalyzerTests`, and `VocoderTests` test audio output, FFT spectra, and envelopes rather than `S_Cpu` on closed cards; keeping DSP/analyzer buffers untouched while gating OS clock reads preserves 100% test compatibility.
- **Concurrency / AR-6 Proof**:
  - UI/scope readers increment a relaxed monotonic `std::atomic<uint32_t> observeRequests_` (`observe()`).
  - The audio thread tracks `lastSeenObserve_` and decrements a thread-local `rtLeaseFrames_` counter (no CAS loops, no clock queries while `rtLeaseFrames_ <= 0`, no heap allocation, no mutex).
  - Each `DeviceTelemetry` defaults to standalone `TelemetryMode::OnDemand` with optional explicit mode overrides propagated cleanly from `Engine::setTelemetryMode(...)` off the audio thread.
- **Alternatives Considered**:
  - *Global static telemetry flag*: Rejected because smoke tests and benchmarks run multiple `NotaEngine` instances in parallel across threads.
  - *Pointer back to `Engine`'s atomic mode*: Rejected to avoid lifetime coupling if a cloned device outlives a transient helper object. Direct value propagation via `setTelemetryMode()` on `Engine` topology updates is allocation-free and lifetime-safe.
