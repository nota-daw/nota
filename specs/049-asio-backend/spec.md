# Feature Specification: ASIO audio on Windows

**Feature Branch**: `049-asio-backend`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "ASIO audio backend on Windows: add a native ASIO driver backend behind the engine's AudioBackend interface (alongside the existing miniaudio WASAPI backend, which stays as the fallback), so users with audio interfaces that ship ASIO drivers (or FlexASIO/ASIO4ALL) get low, stable latency. Includes: choosing the ASIO driver in Preferences → Audio, opening the driver's control panel, handling driver reset requests (buffer size / sample rate changed in the driver panel), sample-format conversion (Int16/Int24/Int32/Float32 LSB), duplex input+output on one driver, and graceful fallback to WASAPI if the driver fails to load. Licensing: Steinberg ASIO SDK is dual-licensed proprietary/GPLv3; GPLv3 is AGPLv3-compatible but needs the deliberate dependency decision required by CONTRIBUTING.md (third-party.md row + rationale)."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

Today Windows audio goes only through WASAPI (shared, or exclusive via a Preferences
switch), via miniaudio — `src/native/nota.engine/src/audio_io/desktop/MiniaudioBackend.*`
for output and `MiniaudioInput.*` for capture, chosen in `AudioBackendFactory.cpp`.
Output and input are **separate** objects (`AudioBackend`, `AudioInput`) that open
separate devices and hand the engine interleaved **stereo** float. ASIO is the opposite
shape: one driver per process, input and output on the same driver and the same clock,
one buffer callback for both, native per-channel buffers in the driver's sample format,
and the buffer size usually dictated by the driver's own control panel. Preferences →
Audio (`src/managed/Nota.App/PreferencesWindow.cs`) has output / input / sample rate /
buffer size rows plus the WASAPI-exclusive switch on Windows. `BUILD.md` currently says
"ASIO is not supported yet". The core engine must stay JUCE-free, so JUCE's ASIO device
type is not an option.

## Clarifications

### Session 2026-10-28

- Q: Is one stereo pair per direction enough, or must more than two inputs be available at once? → A: One output pair and one input pair; multi-input recording is a later feature.
- Q: ASIO in every official Windows build, or only opt-in builds where the builder supplies the SDK? → A: Every official build; the SDK ships under its GPLv3 option with the third-party row, notice and PR rationale.

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Play through an ASIO driver (Priority: P1)

A Windows user with an audio interface (Focusrite, RME, MOTU, Behringer…) opens
Preferences → Audio, switches the audio system from WASAPI to ASIO, picks their
interface's ASIO driver, and Nota plays through it at the driver's buffer size — far
lower and steadier latency than WASAPI shared mode, with no other app muted by Nota's
choice (that is the driver's business).

**Why this priority**: This is the whole point — without output through ASIO nothing
else in the feature has value. On its own it already serves the main audience.

**Independent Test**: On a Windows machine with any ASIO driver installed (a hardware
interface, or FlexASIO / ASIO4ALL), select ASIO and the driver, play a project and the
test tone; sound comes out of the interface and the Latency line shows the driver's
buffer size and sample rate.

**Acceptance Scenarios**:

1. **Given** at least one ASIO driver is installed, **When** the user opens Preferences →
   Audio, **Then** they can choose ASIO as the audio system and see every installed
   ASIO driver by name.
2. **Given** ASIO and a working driver are selected, **When** the project plays, **Then**
   audio comes out of the driver's output channels and the Latency line reports the
   driver's buffer size, sample rate and resulting latency.
3. **Given** the user selected ASIO and a driver, **When** they quit and relaunch Nota,
   **Then** the same audio system and driver are reopened.
4. **Given** no ASIO driver is installed, **When** the user opens Preferences → Audio,
   **Then** ASIO is shown as unavailable with a short hint (install the interface's
   driver, or a generic one), and WASAPI stays selected.

---

### User Story 2 - Record through the same ASIO driver (Priority: P1)

The same user arms an audio track and records their microphone or guitar through the
interface's inputs while monitoring the project, all on the one ASIO driver, with
recordings landing in time with the playback.

**Why this priority**: People buy interfaces to record. ASIO output with input still on
WASAPI (another device, another clock) would drift and defeat the purpose.

**Independent Test**: With ASIO selected, record-arm a track, record a click played back
into an input with a loopback cable; the recorded click lines up with the source within
the reported latency compensation.

**Acceptance Scenarios**:

1. **Given** ASIO is the audio system, **When** the user opens the Input row, **Then** it
   offers the selected driver's input channels and nothing from other drivers or WASAPI.
2. **Given** a track is armed with ASIO input, **When** the user records, **Then** the
   captured audio is sample-aligned with playback after the engine's usual latency
   compensation, with no drift over a 10-minute take.
3. **Given** the driver has no inputs, **When** the user arms a track, **Then** recording is
   unavailable with the same message Nota shows today when there is no input device.

---

### User Story 3 - Change buffer size and sample rate (Priority: P2)

The user wants lower latency for tracking or a bigger buffer for mixing. They press
"Driver settings…" in Preferences → Audio, the driver's own control panel opens, they
change the buffer size; Nota picks up the new size without a restart. Changing the
sample rate in Nota's own Sample rate row asks the driver for that rate.

**Why this priority**: Most ASIO drivers own the buffer size; without the control panel
and reset handling the user is stuck with whatever the driver started at, and a change in
the panel would silence or crash the app.

**Independent Test**: Open the driver panel from Preferences, change the buffer size while
the project plays; playback resumes within a couple of seconds and the Latency line shows
the new size. Change Nota's sample rate row; the driver switches rate and playback is at
correct pitch.

**Acceptance Scenarios**:

1. **Given** ASIO is selected, **When** the user presses "Driver settings…", **Then** the
   driver's own control panel opens.
2. **Given** the project is playing, **When** the driver asks the host to reset (buffer
   size, sample rate or channel layout changed in its panel or on the hardware), **Then**
   Nota restarts the driver with the new settings within 2 seconds, keeps the project
   state, and the Latency line updates.
3. **Given** the driver supports the requested rate, **When** the user picks a sample rate,
   **Then** the driver runs at it; **given** it does not, **Then** the row reverts and the
   user is told the driver refused that rate.
4. **Given** ASIO is selected, **When** the user looks at the Buffer size row, **Then** it is
   read-only and shows the driver's size, pointing at "Driver settings…" (ASIO drivers
   decide the size themselves).

---

### User Story 4 - Recover when the driver fails (Priority: P2)

The interface is unplugged, the driver is held by another program, or a broken driver
fails to load. Nota does not hang or crash: it tells the user what happened and keeps
working through WASAPI until the driver is usable again.

**Why this priority**: ASIO drivers are third-party code of uneven quality and only one
program can usually hold one. A DAW that dies on a bad driver loses the user's work.

**Independent Test**: Select a driver, quit Nota, unplug the interface (or open the driver
in another DAW first), launch Nota; it starts on WASAPI with a status message naming the
driver, and Preferences shows the reason.

**Acceptance Scenarios**:

1. **Given** the saved driver cannot be opened at launch, **When** Nota starts, **Then** it
   falls back to the system default WASAPI device, shows a status message naming the
   driver and the reason, and keeps ASIO as the user's saved choice for next time.
2. **Given** the driver stops responding or is unplugged while playing, **When** Nota
   detects it, **Then** it stops the driver, falls back to WASAPI within 3 seconds, and
   tells the user; the project and transport state are kept.
3. **Given** Nota is on the WASAPI fallback, **When** the user reselects the driver in
   Preferences and it opens, **Then** Nota moves back to ASIO.

---

### Edge Cases

- The driver uses a sample format other than 32-bit float (16-, 24- or 32-bit integer,
  little-endian): Nota converts to and from its internal float with correct scaling and
  no clicks; big-endian ("MSB") formats are refused with a clear message.
- The driver offers many channels (e.g. 18 in / 20 out): only the chosen stereo pair is
  used; all other output channels are fed silence, never garbage.
- The driver offers a mono-only output or input (one channel): mono output carries the
  mix down-summed, mono input is duplicated to both sides — the same as today's WASAPI
  handling of mono devices.
- The driver's buffer size is very small (32 frames) or very large (4096): playback works
  at both; very small sizes may raise CPU load and xruns are counted as today.
- The driver's buffer size changes in its panel while recording: the take is stopped
  cleanly up to the reset (what was recorded is kept), then the reset proceeds.
- The driver reports a latency or a resync: Nota uses the driver's reported input and
  output latency for recording compensation and the Latency line.
- Two ASIO drivers with the same name: both are listed, distinguishable (e.g. by a
  numeric suffix), and the saved choice reopens the right one.
- A 32-bit-only driver on 64-bit Windows: it is not listed (Nota cannot load it); a hint
  explains why a driver the user sees elsewhere may be missing.
- Generic wrappers (FlexASIO, ASIO4ALL) are just drivers: they appear in the list and
  work like any other.
- The user switches from ASIO back to WASAPI: the ASIO driver is released so other
  programs can use it, and the previous WASAPI output/input choices come back.
- The project is open on macOS or Linux after being saved on Windows with ASIO: nothing
  changes there — the audio system choice is a per-machine preference, not project state.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: On Windows, Preferences → Audio MUST offer an audio system choice — WASAPI
  (today's behaviour, the default) or ASIO. The choice is hidden on macOS and Linux.
- **FR-002**: When ASIO is chosen, the app MUST list every installed ASIO driver that the
  running build can load, by the driver's name, and let the user pick one.
- **FR-003**: With ASIO selected, the engine MUST run the whole audio graph off the
  driver's buffer callback, so output and input share one clock and one callback.
- **FR-004**: The user MUST be able to choose which output channel pair carries the master
  and which input channel pair feeds recording, from the driver's named channels. One
  output pair and one input pair is the whole scope of this version; recording more than
  two inputs at once (per-track input channels on a multi-input interface) is a separate,
  later feature.
- **FR-005**: The engine MUST convert between its internal float samples and the driver's
  little-endian 16-, 24-, 32-bit integer and 32-bit float formats, in both directions,
  without allocating, locking or doing I/O inside the buffer callback.
- **FR-006**: Channels the user did not choose MUST receive silence on every buffer.
- **FR-007**: Preferences MUST offer a "Driver settings…" action that opens the selected
  driver's own control panel.
- **FR-008**: When ASIO is selected, the Buffer size row MUST show the driver's current
  size read-only; the Sample rate row MUST request the chosen rate from the driver and
  report a refusal.
- **FR-009**: When the driver requests a reset (buffer size, sample rate, channels or
  latency changed), the app MUST stop and reopen the driver off the audio thread with the
  new settings, preserving project and transport state, within 2 seconds.
- **FR-010**: Recording latency compensation and the Latency line MUST use the driver's
  reported input and output latencies.
- **FR-011**: If the selected driver cannot be loaded, initialised or started — at launch
  or on selection — or stops delivering buffers while running, the app MUST fall back to
  WASAPI with the system default devices, show a status message naming the driver and
  the reason, and keep ASIO as the saved preference.
- **FR-012**: Switching away from ASIO, quitting, or falling back MUST release the driver
  so other programs can open it.
- **FR-013**: The audio system, driver and channel choices MUST persist per machine in the
  app settings, alongside today's audio settings, and be restored at launch.
- **FR-014**: Xruns reported by the driver (overload / resync) MUST feed the existing
  dropout counter.
- **FR-015**: ASIO support MUST be part of every official Windows build (installer and
  CI), with no opt-in flag. The ASIO SDK is used under its GPLv3 option as a dependency
  of the shipped binary: a row in `LICENSES/third-party.md`, a notice in
  `LICENSES/THIRD-PARTY-NOTICES.md`, and the AGPL-compatibility rationale in the pull
  request, as CONTRIBUTING.md requires for a copyleft dependency.
- **FR-016**: The smoke test MUST keep passing on machines without any ASIO driver (CI),
  and the engine's ASIO sample-format conversion and reset handling MUST be covered by
  tests that do not need a real driver.
- **FR-017**: User-visible documentation MUST change: `BUILD.md` no longer says ASIO is
  unsupported; `CHANGELOG.md`, `FEATURES.md` and the user manual describe the new option.

### Key Entities

- **Audio system**: which Windows audio path the engine uses — WASAPI or ASIO. Per-machine
  preference.
- **ASIO driver**: an installed driver identified by its registered name and identity;
  has input channels, output channels, supported sample rates, a current buffer size, a
  sample format per channel, and reported latencies.
- **Channel selection**: the output pair carrying the master and the input pair feeding
  recording, identified by the driver's channel indices and shown by their names.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: With a hardware interface's ASIO driver at a 64-frame buffer, 48 kHz, a
  project of 16 tracks with built-in instruments and effects plays for 10 minutes with
  zero xruns on the reference Windows test machine.
- **SC-002**: Reported round-trip latency through ASIO at the driver's smallest supported
  buffer is at least 3× lower than WASAPI shared mode on the same machine and interface.
- **SC-003**: A user who has an ASIO driver installed can go from opening Preferences to
  hearing audio through ASIO in under 30 seconds, without reading documentation.
- **SC-004**: A buffer-size change in the driver panel during playback resumes audio
  within 2 seconds, 10 times out of 10, with no crash.
- **SC-005**: Launching with the saved driver missing or busy reaches a working, audible
  WASAPI session with an explanation, 10 times out of 10; no launch hangs longer than
  5 seconds on a misbehaving driver.
- **SC-006**: A 10-minute loopback recording through ASIO shows no drift between the
  recorded and the played signal (offset stays within one sample after compensation).

## Assumptions

- ASIO is Windows-only; macOS (CoreAudio) and Linux (PulseAudio/ALSA) are unchanged.
- WASAPI stays the default audio system so nothing changes for users who never touch
  the setting, and it is the fallback whenever ASIO fails.
- Only one ASIO driver is open at a time (an ASIO limitation); Nota does not aggregate
  drivers or mix ASIO output with WASAPI input.
- The engine stays stereo internally in this version; ASIO adds channel *selection*, not
  multichannel routing (FR-004).
- Big-endian ("MSB") ASIO sample formats and DSD are out of scope; they are practically
  unused on Windows.
- Only drivers matching the build's architecture can load: the x64 build lists 64-bit
  drivers; the ARM64 build lists native ARM64 drivers (few exist today) and shows an
  explanatory hint when there are none.
- Driver panels and driver loading run on the app's main/UI thread or a dedicated
  non-real-time thread; the buffer callback only converts and renders.
- The Steinberg ASIO SDK's GPLv3 option is compatible with AGPL-3.0-only (GPLv3 §13 /
  AGPLv3 §13 permit combination); this still requires the explicit dependency decision
  CONTRIBUTING.md asks for, recorded in `LICENSES/third-party.md` and in the pull request.
  The exact current SDK license terms are re-checked at plan time.
- The audio system choice is a machine preference stored with today's audio settings,
  never in the project file.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned — **ASIO SDK (GPLv3 option); row + rationale required, see FR-015**
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — the ASIO buffer callback becomes the audio thread; driver reset/restart happens off it
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — driver list, channel lists, audio system choice and "open driver panel" are new C ABI calls
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — must pass with no ASIO driver present (FR-016)
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
