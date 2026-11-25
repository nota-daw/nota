# Feature Specification: Batch Track Meter Snapshots and On-Demand Device Telemetry

**Feature Branch**: `perf/batch-meters-telemetry-gate`  
**Created**: 2026-11-24  
**Status**: Draft  
**Input**: Phase 1 of nota-daw/nota#1050 — reduce large-project CPU and UI-thread overhead by batching track level meter queries and gating per-device CPU timing telemetry on active observation.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read all track meters in one linear pass (Priority: P1)

When working on a large project (64–100+ tracks) with the Arrangement headers, Mixer view, or Session mixer open, the application refreshes live level meters smoothly without repeating per-track lookups proportional to the square of the track count every visual frame.

**Why this priority**: The UI refreshes meters at ~30 Hz; looking up each track individually across all tracks turns a 100-track view refresh into ~10,000 track scans per tick and adds redundant managed/native boundary crossings.

**Independent Test**: Add 100 tracks to an engine instance, feed test audio, retrieve all track meters in a single snapshot call, and verify the readings match individual per-track meter queries while keeping lookup time linear.

**Acceptance Scenarios**:

1. **Given** a project with multiple tracks producing distinct output levels, **When** a caller queries the batch track-meter snapshot, **Then** every track's peak and RMS values match its per-track meter reading in a single call.
2. **Given** a project whose track count grows or shrinks, **When** the caller queries the required count or passes a smaller buffer, **Then** the interface reports the full track count safely without out-of-bounds writes or errors.
3. **Given** the Arrangement, Mixer, or Session mixer refreshes meters on the UI clock, **When** rendering a frame, **Then** track meters are fetched once per refresh pass rather than resolving each strip individually across the native boundary.

---

### User Story 2 - Skip per-device CPU clock measurement when nobody is viewing a device card (Priority: P1)

During playback or offline rendering of a project with dozens of tracks carrying built-in effects and instruments whose editors are closed, the audio engine avoids querying the OS wall-clock timer twice per block on every unobserved device, while immediately resuming live CPU timing once a user or diagnostic tool inspects that device.

**Why this priority**: In a 64-track session with 4 devices per track, querying the monotonic OS clock before and after every device `process()`/`render()` adds hundreds of clock reads per audio callback even when zero device cards are visible.

**Independent Test**: Render audio through built-in devices without opening their telemetry view and verify sound output is identical and audio renders without calling wall-clock timing; then read device telemetry (`scope`) and render further blocks to verify real CPU telemetry populates again.

**Acceptance Scenarios**:

1. **Given** built-in effects and instruments with closed editors, **When** audio blocks render under default on-demand telemetry mode, **Then** DSP audio output is unchanged and wall-clock timing overhead is skipped on the audio thread.
2. **Given** a user opens a device card or a test/tool requests device scope telemetry, **When** subsequent audio blocks render within the active observation window, **Then** live device CPU load readings resume and update smoothly without manual configuration.
3. **Given** an opened device card stops being polled, **When** the observation window expires, **Then** the device automatically stops spending audio-thread cycles on wall-clock measurements.
4. **Given** a diagnostic test or profiler wants deterministic telemetry behavior regardless of UI state, **When** telemetry mode is explicitly set to always on or always off, **Then** devices honor that mode immediately without allocating or locking on the audio thread.

---

### User Story 3 - Keep device visuals and status readouts responsive on first open (Priority: P2)

When a user opens a device card (such as Auto Filter, Eq, Chamber, or Compressor), visual indicators and status lines work cleanly without relying on a side-effect sentinel from CPU timing having run while closed.

**Why this priority**: Existing device views must remain visually accurate when CPU timing transitions to on-demand without broken indicators on the very first frame after opening a card.

**Independent Test**: Open device telemetry on Auto Filter after playback starts and verify live filter, LFO phase, and spectrum views activate immediately cleanly alongside CPU load updates on subsequent blocks.

**Acceptance Scenarios**:

1. **Given** a device rendered blocks before its card was opened, **When** the user opens its card (first telemetry read), **Then** active LFO/filter visualizations display immediately without requiring stale CPU measurements, and real CPU % appears after the next audio blocks render.

---

### Edge Cases

- Passing a `null` output pointer or zero/negative capacity to the batch track-meter query returns the track count safely (or zero for a null engine handle) without crashing.
- Deleting, reordering, or grouping tracks between UI ticks leaves no stale or mismatched meters; unknown track IDs fall back cleanly to silence/reset.
- Offline export (`renderOffline`) and multi-threaded `AudioThreadPool` rendering stay race-free and bit-identical (`RenderPoolTests`) when on-demand telemetry leases update concurrently across UI and worker threads.
- Master track meters (`MasterMeter`) and Cue meters remain independent and unaffected by regular track batch reads.
- Opening and closing cards repeatedly does not leak memory, allocate on the audio thread, or freeze sample-rate reporting (`S_SampleRate` remains populated regardless of CPU clock observation).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The engine MUST provide a single-call batch query returning post-fader peak and RMS meter entries paired with stable track identifiers for all tracks in graph order (`O(T)` time, `1` boundary crossing).
- **FR-002**: The engine MUST preserve the existing single-track meter query for targeted callers (e.g., single-strip queries or legacy tests) alongside the new batch interface.
- **FR-003**: The desktop views (`ArrangementView`, `MixerView`, and `SessionView`) MUST use the batch meter snapshot once per UI update pass instead of querying tracks individually across the native boundary.
- **FR-004**: Built-in audio effects and instruments that record block execution timing MUST delegate timing lifecycle and CPU load smoothing through a shared, human-readable telemetry abstraction rather than duplicating ad-hoc clocks and EMA math per device.
- **FR-005**: In default on-demand mode, built-in devices MUST skip monotonic wall-clock timing on the audio thread when no viewer has polled telemetry within the active observation lease window.
- **FR-006**: Reading a device's telemetry (`scopeRead` or CPU/telemetry status query) MUST renew its observation lease lock-free so active cards receive live CPU measurements within subsequent audio blocks and automatically return to zero-cost mode once closed.
- **FR-007**: The engine MUST expose an explicit, human-readable telemetry mode control (`on-demand`, `always-off`, `always-on`) across the C ABI and managed `IAudioEngine` interface.
- **FR-008**: All audio-thread state transitions for telemetry leases and smoothing MUST remain 100% real-time safe (`AR-6`: no allocations, no locks, no OS logging/I/O) and preserve audio signal bit-identity.
- **FR-009**: UI cards reading `scope` data MUST not treat unobserved `CPU == 0` as "device has not started processing audio" when the engine is actively running.

### Key Entities *(include if feature involves data)*

- **TrackMeterReading / NotaTrackMeterEntry**: Value record pairing a stable track ID with its post-fader stereo `NotaMeter` reading (left/right peak and RMS).
- **TelemetryMode / NotaTelemetryMode**: Enum controlling built-in device block timing behavior (`OnDemand`, `AlwaysOff`, `AlwaysOn`).
- **DeviceTelemetry**: Shared engine component owning block-timing state, lease expiration (measured in processed audio frames rather than extra clock calls), smoothed CPU percentage, and real-time safe observation checks.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Fetching meters for a 100-track engine takes 1 native call and returns identical per-track readings to 100 individual `TryGetTrackMeter` calls in linear `O(T)` time.
- **SC-002**: During continuous multi-track rendering with closed device cards under `OnDemand` telemetry, `0` wall-clock timer queries occur inside built-in device `process()` / `render()` callbacks after initialization.
- **SC-003**: Opening/polling a device's scope enables non-zero real CPU load reporting after rendering subsequent blocks, and stopping polling cleanly expires the lease without allocations or audio glitches.
- **SC-004**: All existing tests (`scripts/build.sh`, `Nota.SmokeTest`, `RenderPoolTests`, `EqAnalyzerTests`, `VocoderTests`) pass without regression, and `scripts/bench-engine.sh` demonstrates equal or lower CPU/wall time compared to `develop` baseline.
- **SC-005**: Engine readability gate met: shared `DeviceTelemetry` removes duplicate per-device clock/EMA boilerplate across built-in devices, and C ABI / C# signatures use self-explanatory domain types with full `SPDX-License-Identifier: AGPL-3.0-only` headers.

## Assumptions

- Existing built-in device telemetry array indices (`S_Cpu` slot positions inside each device's `scopeRead` buffer) MUST stay backwards-compatible because UI cards and `Nota.SmokeTest` index those slots directly.
- Spectrum analyzer buffers (such as `Eq`'s audio ring when `Analyzer != Off`) and `AutoFilter` histories tested immediately after offline rendering without pre-opening a card remain active when enabled, while Phase 1 specifically gates the expensive `steady_clock::now()` OS calls and adds the shared `DeviceTelemetry` abstraction cleanly.
- Track meter values stored atomically on each `Track` remain single-writer (audio thread) / multi-reader (UI thread) using relaxed atomic floats, matching existing meter concurrency semantics.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — zero third-party packages added
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command/atomic delivery for mode updates (AR-4/AR-6), immutable snapshot pass for batch meters (AR-5)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: `tests/Nota.SmokeTest` will be extended to cover batch track meters and on-demand telemetry lease + mode switching
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned
- [x] UI change → follows `DESIGN.md` (existing layout preserved; no visual regressions)
