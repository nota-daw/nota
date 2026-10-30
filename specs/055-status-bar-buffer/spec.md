# Feature Specification: Sample rate and buffer size in the status bar

**Feature Branch**: `055-status-bar-buffer`

**Created**: 2026-10-30

**Status**: Implemented

**Input**: GitHub issue nota-daw/nota#373: "See the current buffer size at a glance and
easily change it if needed. Display the current sample rate and buffer size next to each
other in the lower right status area. Make the indicator clickable, ideally showing a
dropdown menu for changing the buffer size directly. Alternatively, clicking it could open
the relevant audio settings section."

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

**Existing behaviour this builds on**: the status bar's right side shows the sample rate
("48000 Hz") followed by the version button. Settings → Audio has a Buffer size combo
(Device default, 64–2048 frames) that stages the size and restarts the backend through
`MainWindowViewModel.ApplyAudioSettings`. Under ASIO the buffer is the driver's own setting
(specs/049, FR-008); in WASAPI exclusive mode the device's native period is used.

## User Scenarios & Testing

### User Story 1 - See the audio format at a glance (Priority: P1)

A producer wants to know what buffer the device runs at without opening Settings. The
lower right corner reads "48 kHz · 256".

**Independent Test**: Start Nota with a device running; the status bar shows the
negotiated rate and buffer. Change the buffer in Settings → Audio; the readout follows.

**Acceptance Scenarios**:

1. **Given** a running device at 48 kHz and 256 frames, **Then** the status bar reads
   "48 kHz · 256" beside the version.
2. **Given** the buffer changes (Settings, the status bar menu, or the ASIO driver's own
   panel), **Then** the readout shows the newly negotiated values.
3. **Given** no audio output could be opened, **Then** the readout shows the rate alone.

### User Story 2 - Change the buffer from the status bar (Priority: P1)

While tracking, the producer hears crackles and wants a bigger buffer, or wants a smaller
one for lower latency. A click on the readout opens a menu of buffer sizes.

**Independent Test**: Click the readout, pick 512; the device restarts at 512, the readout
shows it, and an open Settings → Audio pane shows 512 too.

**Acceptance Scenarios**:

1. **Given** shared-mode audio, **When** the user clicks the readout, **Then** a menu lists
   Device default and 64, 128, 256, 512, 1024, 2048 — the staged one ticked, each size with
   its latency at the current rate — and Audio settings… under them.
2. **When** the user picks a size, **Then** it is saved as the audio setting, the backend
   restarts on it and, if the transport was playing, playback continues from the same spot.
3. **When** the user picks Audio settings…, **Then** Settings opens on the Audio page.
4. **Given** the transport is recording, **Then** the sizes are disabled with a note to stop
   recording first (a restart would end the take).

### Edge Cases

- **ASIO running**: the driver owns the buffer. The menu offers Driver settings… (its own
  panel) instead of sizes; a change made there comes back as a reset and the readout updates.
- **WASAPI exclusive mode**: the device's own buffer is used; the menu says so instead of
  offering sizes.
- **ASIO chosen but fallen back to WASAPI**: the sizes are offered (shared WASAPI runs).
- **Picking the size already staged**: nothing restarts.

## Requirements

### Functional Requirements

- **FR-001**: The status bar MUST show the negotiated sample rate and buffer size side by
  side ("48 kHz · 256"), in the lower right, before the version.
- **FR-002**: The readout MUST update whenever the audio backend is (re)applied.
- **FR-003**: Clicking the readout MUST open a menu of the buffer sizes Settings → Audio
  offers (one shared list), with the staged choice ticked and each size's latency.
- **FR-004**: Picking a size MUST stage and persist it exactly as Settings does, apply it,
  keep playback going, and refresh an open Settings → Audio pane.
- **FR-005**: The menu MUST include Audio settings…, opening Settings on the Audio page.
- **FR-006**: Under a running ASIO driver the menu MUST NOT offer sizes; it offers the
  driver's settings panel. In WASAPI exclusive mode it MUST NOT offer sizes and says the
  device's buffer is used.
- **FR-007**: The menu MUST follow the Studio menu conventions (MenuKit, sentence case).

## Success Criteria

- **SC-001**: The buffer size is visible without opening any window.
- **SC-002**: Changing the buffer takes two clicks (readout, size) instead of four or more
  (menu, Settings, Audio, combo, choice).

## Assumptions

- Sample rate is shown but changed only in Settings; the issue asks for buffer changes.
- The readout uses kHz with one optional decimal ("44.1 kHz") and the buffer in frames,
  unitless; the tooltip names both.
- No engine or C ABI change: the existing audio-config calls are reused.

## Nota Compliance Checklist

- [x] Every new source file carries the AGPL-3.0-only SPDX header
- [x] No new dependency
- [x] Audio-thread impact: none — UI and the existing apply path on the UI thread
- [x] No new managed/native crossings
- [x] Smoke-test impact: pure formatting/ownership logic covered by `AudioFormatStatusTests`
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry and FEATURES.md
- [x] UI change → follows `DESIGN.md` (ghost button like the version beside it, MenuKit menu)
