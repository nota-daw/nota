# Feature Specification: EQ spectrum analyzer

**Feature Branch**: `feat/eq8-analyzer`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: "EQ-8: a proper spectrum analyzer (nota-daw/nota#265). The analyzer
behind EQ-8's curve looks coarse, jumpy and hard to read. Proper FFT analysis with windowing,
enough bins and log-frequency smoothing; fast-attack, slower-release ballistics and an optional
peak hold; a Pre/Post toggle and a range setting; drawn as a filled low-contrast area under the
EQ curve; the same analyzer reused where EQ-3 and the other EQ cards show a spectrum. Analysis
off the audio thread."

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Read the mix through EQ-8 (Priority: P1)

A producer puts EQ-8 on a bus and plays the song. Today the spectrum is a thin, stepped line made
of 96 points that wobbles up and down with every frame, so it is hard to tell where the energy
sits. With the new analyzer, they see a smooth, filled shape under the EQ curve. It rises at once
when a kick hits and falls back gently, so the shape of the mix holds still enough to read, and a
peak line shows where it reached in the last moments.

**Why this priority**: This is the issue. An EQ is judged first by its analyzer, and a reviewer
said the current one would be better gone.

**Independent Test**: Play pink noise, a sine and a drum loop through EQ-8 and look at the card.
Pink noise reads as a nearly flat, steady shape; a 1 kHz sine reads as one narrow peak at 1 kHz
near its level; the drum loop's hits rise at once and fall away smoothly.

**Acceptance Scenarios**:

1. **Given** EQ-8 with the analyzer on Post, **When** audio plays, **Then** the output spectrum
   is drawn as a filled, low-contrast area under the EQ curve, with a smooth edge (no visible
   steps between points at the card's normal size), and the EQ curve and band nodes stay on top
   and fully readable.
2. **Given** a steady 1 kHz sine at −12 dBFS, **When** it plays, **Then** the spectrum shows one
   peak at 1 kHz, reading within ±3 dB of −12 dB, and the shape does not visibly flicker.
3. **Given** a drum loop, **When** a hit arrives, **Then** the spectrum rises within one display
   frame, and **When** the hit ends, **Then** it falls back over roughly a third of a second
   rather than dropping at once.
4. **Given** Post, **Then** the input spectrum is also shown, as a dim line, so the EQ's effect
   reads as the gap between the two. **Given** Pre, **Then** only the input spectrum is shown
   (filled). **Given** Off, **Then** no spectrum is drawn and no analysis runs.
5. **Given** the transport stops or the input goes silent, **Then** the spectrum falls away to
   nothing and does not freeze at its last shape.

---

### User Story 2 - Peak hold and range (Priority: P2)

A mixing engineer wants to see the loudest a resonance got over the last chorus, and to zoom the
analyzer in on the top of the mix.

**Why this priority**: The issue asks for both; they are what turns the display into a tool.

**Independent Test**: Turn Peak on, play a loop with one loud hit, and see a thin line hold at the
hit's level and then fall. Switch Range between its settings and see the vertical scale change.

**Acceptance Scenarios**:

1. **Given** Peak on, **When** audio plays, **Then** a thin line above the spectrum marks each
   frequency's highest recent level, holds for about 1.5 seconds and then falls.
2. **Given** Peak off, **Then** no peak line is drawn.
3. **Given** Range set to 60, 90 or 120 dB, **Then** the analyzer's vertical scale runs from
   0 dBFS at the top to −60, −90 or −120 dBFS at the bottom. The default is 90 dB. The EQ
   curve's own ±dB scale does not change.
4. **Given** the user sets Peak and Range, **When** the project is saved and reopened, or the
   device is duplicated or saved as a preset, **Then** both settings come back.
5. **Given** a project saved before this change, **When** it opens, **Then** EQ-8 loads with its
   bands, Pre/Post/Off choice and sound unchanged, Peak off and Range at 90 dB.

---

### User Story 3 - The same analyzer in EQ-3 (Priority: P3)

A user moves from EQ-8 to EQ-3 on another track. Its spectrum reads the same way: the same
smoothing, ballistics and fill, so levels compare across the two cards.

**Why this priority**: The issue asks for reuse where a spectrum is shown. EQ-3 shares EQ-8's old
96-band analysis code, so it moves to the shared analyzer with it.

**Independent Test**: Put EQ-8 (Post) and EQ-3 on the same signal. Their spectra match in shape
and level.

**Acceptance Scenarios**:

1. **Given** EQ-3 on a track, **When** audio plays, **Then** its spectrum uses the shared
   analyzer and is drawn as a filled area, like EQ-8's.
2. **Given** the same signal into EQ-8 (Post, flat) and EQ-3 (flat), **Then** both spectra read
   within 1 dB of each other at every frequency.

### Edge Cases

- **Sample rate**: at 44.1/48 kHz the analysis uses a 4096-point FFT, at 88.2 kHz and above
  8192, so the bins stay about 11 Hz apart. The frequency axis stays 20 Hz–20 kHz.
- **Low frequencies**: below a few hundred hertz a display point is narrower than an FFT bin; the
  curve interpolates between bins there instead of repeating one bin as a flat step.
- **Silence**: below the bottom of the range the area is not drawn at all (no line along the
  floor).
- **Several EQ cards open**: each analyses only while its card is shown, at no more than 30
  updates a second, so hidden cards cost nothing.
- **Offline render / freeze**: the analyzer never runs on the audio thread and never touches the
  rendered audio.
- **Mid/Side or Left/Right bands**: the spectrum stays the mono sum of the input and the output,
  as today.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The analysis MUST stay off the audio thread. The audio thread only writes samples
  into the existing lock-free rings; the FFT and all smoothing run when the card reads telemetry.
- **FR-002**: The analysis MUST use a Hann-windowed FFT of 4096 points (8192 above 50 kHz) over
  the latest input, at up to 30 updates per second.
- **FR-003**: The analyzer MUST map the FFT onto a log-frequency curve of at least 256 points from
  20 Hz to 20 kHz. Each point MUST read the energy in a 1/6-octave band around it (summed in the
  power domain), so neighbouring points overlap and the curve is smooth. Where the band is
  narrower than an FFT bin, the point MUST interpolate between the two nearest bins instead of
  repeating one bin.
- **FR-004**: Because each point reads a constant fraction of an octave, a sine MUST read at its
  own level and pink noise MUST read flat, with no extra tilt.
- **FR-005**: Each point MUST rise to a new higher level at once and fall at a fixed rate that
  takes about 300 ms to drop 20 dB.
- **FR-006**: With Peak on, each point's peak MUST hold for 1.5 s and then fall at half the
  spectrum's fall rate; with Peak off no peak data is drawn.
- **FR-007**: EQ-8 MUST add two saved parameters at the end of its parameter list: **Range**
  (60, 90 or 120 dB; default 90) and **Peak** (off or on; default off). Older projects and
  presets, which lack them, MUST load with the defaults and otherwise unchanged.
- **FR-008**: The EQ-8 card MUST show the spectrum as a filled, low-contrast area under the EQ
  curve: the output when on Post (with the input as a dim line), the input when on Pre. The peak
  line, when on, MUST be a thin line in the same hue.
- **FR-009**: The EQ-8 card MUST give Range and Peak controls next to the existing Pre/Post/Off
  control.
- **FR-010**: EQ-3 MUST use the same shared analyzer and the same fill drawing; it keeps its
  fixed 90 dB range and no peak line.
- **FR-011**: The analyzer MUST reset (fall to nothing) when the device is reset or the input is
  silent, and MUST NOT show a stale shape when the card is reopened.
- **FR-012**: The MCP device help for EQ-8 MUST list the new parameters.
- **FR-013**: EQ-8's input and output rings MUST hold the block's samples at the same indices,
  so each analysis window is contiguous audio (found while specifying: the output ring ran on
  from the input pass, leaving half of every window stale).

### Key Entities

- **Spectrum analyzer**: an engine object owned by a device. It reads a ring of mono samples,
  runs the FFT, and keeps the smoothed curve, the peak curve and their ages. One instance per
  analysed signal (EQ-8 has two: input and output).
- **Spectrum telemetry**: the curve points the card reads with the device's other telemetry.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: A steady sine at any frequency from 50 Hz to 15 kHz reads within ±3 dB of its
  level, at its frequency (within one display point).
- **SC-002**: Steady pink noise reads flat within ±3 dB from 100 Hz to 15 kHz (below about
  100 Hz a band is narrower than a bin and the reading follows the bins).
- **SC-003**: With a steady sine, consecutive frames differ by less than 0.5 dB at the sine's
  frequency; with steady pink noise, the mean frame-to-frame change across the curve is under
  1.5 dB.
- **SC-004**: After a full-scale burst stops, the curve falls 20 dB in 250–400 ms.
- **SC-005**: One analysis update of EQ-8 (two curves) takes under 1 ms on the CI machines, and
  the audio-thread cost of EQ-8 and EQ-3 is unchanged (the perf gate passes without an override).
- **SC-006**: Every existing EQ-8 and EQ-3 project, preset and smoke render plays back
  bit-identical audio.

## Assumptions

- The Pre/Post/Off control already exists and keeps its meaning and its parameter index.
- Peak hold time and fall rates are fixed values, not user settings; Lens remains the device for
  full analyzer control (FFT size, window, averaging, tilt).
- Lens keeps its own analyzer for now: it already has every control the issue asks for, and moving
  it is a refactor with no user-visible change. Auto Filter (which analyses in the app), Dynamic
  EQ, Crush and Prism are follow-ups.
- The analyzer's vertical range is a view setting saved with the device, like Pre/Post/Off.
