# Feature Specification: Audio dropouts in the status bar

**Feature Branch**: `068-status-bar-dropouts`

**Created**: 2026-11-01

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#271: "A simple xruns counter/indicator in the bottom
margin next to the sample rate would be a useful addition to see if something is wrong or
if the buffer size is set too low for the system to handle."

## Nota Context

**Feature area**:

- [ ] Engine (C++, `src/native/nota.engine`)
- [x] Managed app (C#, `src/managed/*`) — presentation view model
- [x] UI (Avalonia) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting
- [ ] Build / packaging / CI
- [ ] Docs only

**Nota docs to keep in mind**: [`ARCHITECTURE.md`](../../ARCHITECTURE.md),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Existing behaviour this builds on**: the engine counts device dropouts since launch
(`nota_engine_xrun_count`, M7-8). `MainWindowViewModel.ReportDropouts` polls it on the
playhead clock, logs each new one and flashes "⚠ Audio dropout (n)" in the status text,
which the next message replaces. Settings → Audio's CPU check counts dropouts over its five
seconds. The status bar's right side shows the sample rate and buffer ("48 kHz · 256",
specs/061) and the version.

## User Scenarios & Testing

### User Story 1 - See whether the system keeps up (Priority: P1)

A producer hears a click and wants to know whether the audio device actually missed a
buffer, without opening Settings.

**Independent Test**: Start Nota; the status bar shows no dropout counter. Force a dropout
(a tiny buffer under heavy load); the counter appears and reads "1 dropout" in red.

**Acceptance Scenarios**:

1. **Given** no dropout since the device started, **Then** the status bar shows no
   counter, so a healthy session adds nothing to read.
2. **When** the device reports a dropout, **Then** the counter appears beside the sample
   rate in the danger ink and counts up ("1 dropout", "3 dropouts").
3. **When** the user hovers the counter, **Then** the tooltip says what a dropout is, what
   usually helps, and that a click resets it.

### User Story 2 - Check whether a change helped (Priority: P1)

After raising the buffer size or freezing a track, the producer wants to see whether
dropouts stop.

**Independent Test**: With dropouts counted, click the counter; it disappears and comes
back only for new ones.

**Acceptance Scenarios**:

1. **When** the user clicks the counter, **Then** it resets to 0 and hides until the next
   dropout, which reads "1 dropout".
2. **When** the audio device, sample rate or buffer size changes (Settings → Audio, the
   status bar's buffer menu, an ASIO driver reset), **Then** the counter resets to 0 and
   hides.

### Edge Cases

- **A failed apply**: the device didn't restart, so the count stays.
- **Dropouts between the last clock tick and a reset**: they don't count, but the next tick
  still logs them, so the log never misses one.
- **Backends that don't report dropouts** (miniaudio: PulseAudio / ALSA, WASAPI): the count
  stays at 0, so the counter never appears. The engine's count only moves where the backend
  reports dropouts (Core Audio, ASIO).

## Requirements

### Functional Requirements

- **FR-001**: The status bar MUST show the number of dropouts since the device started or
  the last reset ("1 dropout", "N dropouts"), left of the sample rate, and MUST hide it
  while that number is 0.
- **FR-002**: The count MUST use the danger ink (DESIGN.md: red is overload).
- **FR-003**: Clicking the counter MUST reset it to 0.
- **FR-004**: Every audio apply that restarts the device MUST reset it to 0.
- **FR-005**: The tooltip MUST explain a dropout and the usual fix (a larger buffer,
  freezing heavy tracks) and say that a click resets it.
- **FR-006**: The text MUST change only when the count does (no per-tick relayout).

## Success Criteria

- **SC-001**: Whether the device has dropped out is visible without opening any window.
- **SC-002**: A setting change can be judged by one click and watching the count.

## Assumptions

- No engine or C ABI change: the engine's running count is reused and the reset point is
  kept on the managed side, so the CPU check and the MCP `get_engine_info` tool keep
  reading the count since launch.
- Teaching the miniaudio backends to report dropouts is engine work for another change.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: none — the UI thread reads an existing atomic counter
- [x] No new managed/native crossings
- [x] Smoke-test impact: counting, reset and text covered by `DropoutCounterTests`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (ghost button like its neighbours, caption text,
      shown in danger ink only while something is wrong)
