# Implementation Plan: Nota Color
## Current iteration — HQ engine

- Derive candidate pitch classes from the manual scale. Split the audio band
  with independent Butterworth sections. Filter both pitch branches before
  phase-vocoder transposition (1024/256), and align dry and side bands.
- Add the spectrally gated correction to the aligned mid band. Cascade pairs
  of notch and peaking biquads, compensate level according to note density and
  protect transients. Use a 150 ms blend between two prepared paths when the mask changes.
- Use the original normalized control ranges. Amount blends the working band,
  Resonance follows a nonlinear gain curve, and Decay scales peaking Q
  around the nominal 120 ms setting. Use the HQ correction FFT for Morph and
  Gate. The scope analyzes aligned dry and the actual correction separately.
- Retire the last parameter and reuse the generic preset/project paths to load
  the remaining values. Scope and ABI shapes stay stable. Update the card in
  place, the prototype manual, and the release-facing feature descriptions.
- Test the production DSP through the native harness and C ABI. Capture
  repeatable baseline/current renders. Assess tone, attacks and decay by
  comparing renders with matched input, time alignment and playback level.

## Card sizes and sideband mute

Use the shared S/L header and persisted View parameter. Add the mute switches
on both sizes. Keep the old Mode index inactive and append the new parameters.
Filter unnamed slots out of parameter pickers and preset capture.

Delay the low and high outputs independently and ramp their mute gains over
10 ms. With an empty or full note mask, fade the wet contribution into the
phase-consistent crossover reconstruction while a mute engages. With both
mutes off, the inactive mask uses the full delayed dry.

Aggregate both spectra separately on a two-pixel grid. Draw one physical
pixel and leave one clear between columns. Keep the smooth vertical fade.
Update the inverse accent brush and gradient on theme changes and use the
same hue in the legend.

## Earlier iterations

### Summary
Local quality-first prototype, kind 27, C++20 Device with C# Avalonia card. No new dependency or C ABI functions. Accepted user plan is the source of scope.
## Technical Context
Signalsmith RealFFT, 2048-point Hann analysis/synthesis at <=48 kHz, hop N/8, N doubled with sample rate (maximum 32768). Stereo linked peak detection using summed channel powers (not mono sum), instantaneous frequency from phase differences, nearest allowed equal-tempered note, identity phase locking across each peak region. Preserve outside-band bins. Normalized overlap-add (sum Hann² = 3), unnormalized IFFT scaled by 1/N. Packed DC/Nyquist remain untouched. Fixed N-sample latency with exact delayed dry path.
Spectral peaks shift by a shared frequency shift (integer-bin region placement with exact phase advancement per hop); phase accumulators retain temporal coherence; each channel retains its input phase relationship. Smoothed amount and pitch shift, bounded resonant spectral envelope, exponential decay with input-driven gate. Parameters are normalized atomic floats, buffers prepared on message thread. Existing bypass semantics remain unchanged; use Dry/wet for time-aligned comparison.
## Constitution Check
User explicitly authorizes deferring issue until prototype listening. All real-time, ABI, licensing, UI and verification rules remain applicable. No hooks configured. New source headers required. Single local branch, no push/PR.
## Structure and Integration
Color.h/.cpp under devices/effects, CMake source registration, Engine device factory/clone/rack paths, palette category/catalog, ColorDeviceBody and device card factory, three FactoryPresetCatalog presets. Existing generic parameter persistence/automation. New text through localization keys. Docs and FEATURES updated with prototype status.
## Validation
Native executable directly exercises DSP, allocation-free render, dry reconstruction, tuning, stereo, block invariance, sample rates, extreme params, silence/tails and latency. Produces FM/growl/chord level-matched WAVs and performance metrics in ignored build directory. Managed smoke tests cover device identity, parameter persistence/clone and rendering. Full scripts/build.sh. Headless card preview if available; document actual evidence and limits.

## Spectrum visualization
Publish separate 512-bin log-frequency dry and shifted contributions through the existing scope ABI. Track shifted resonance independently in prepared buffers. Render theme-aware accent and blue lines with vertical intensity falloff; preserve band interaction and audio DSP.

## Naturalness extension
Use local one-to-one peak matching within four FFT bins, minimizing frequency distance, then preferring energy and lower index. Carry prior output phase forward by the target-frequency increment and derive a common rotation against current input phase. Never globally reset phase. Stereo-linked Morph interpolates magnitudes after resonance, with 20 ms parameter smoothing; Gate follows using a soft knee and 5/50 ms attack/release. Append indices 21–23, disabled by default; threshold default normalized .25 (−60 dBFS). Prepare all track/gate buffers off-thread. UI adds a second row, retaining original parameter order, dimensions and graph minimum. No new C ABI functions or dependencies.

## Clean color bass iteration
Continue this feature rather than creating a second device/spec. All 24 parameters and
N-sample latency stay fixed. Add native tests and WAV/benchmark generation under
tests/native/ChromaTest.cpp and scripts/test-color.sh, compiling the actual DSP and the
existing Signalsmith FFT. A source override supports the baseline without duplicating DSP
inside the test. Tests run before implementation to record failures, then after each change.

Track a selected MIDI note per peak, with 15-cent hysteresis beyond the nearest-note
boundary, deterministic lower-note ties, and re-selection when the mask changes.
Use magnitude interpolation for the initial frequency and phase-derived frequency for
established partials. Prioritize strong peaks, match nearby predicted frequencies, reject
implausible energy/frequency jumps and retain a shared stereo phase reference.

Smooth integer-bin placement changes over two hops with phase referred to the frame
centre, preserving whole-region relative phase. Measure the transition against the baseline
and keep exact zero-shift reconstruction. Local stereo-linked positive spectral flux
protects attacks by temporarily blending affected bins towards the original spectrum;
return to the tuned spectrum over 20 ms, before Morph/Gate and without extra lookahead.

Remove the hidden 60-ms-input-silence release cap. Retain Decay's 20–1000 ms exponential
pole-time mapping; smoothly release obsolete tails on scale/band changes. Measure actual
post-mix output and prepared-buffer tail state; only permit sleep once input/OLA drain
and output stays below −90 dBFS. Parameter changes and non-silent input wake normally.

Keep existing preset IDs; add Tight bass, Clear growl and Spacious resonance. Update the
focused C-ABI smoke tests (six presets plus boundary regression), FEATURES, changelog and
EN/RU manual. Supply baseline/current WAV pairs and CPU median/max-block measurements.
Run full scripts/build.sh and converge; distinguish any reproduced unrelated deadline
failures from the focused Chroma results. Listening acceptance remains human-owned.

## Polyphonic scale correction
Reuse Chroma's FFT, tracked peaks and synthesis. Score note candidates from the
weighted energy of reliably located partials, removing evidence assigned to each
selected voice before selecting another. Fit and track up to four fundamentals
across frames, allowing upper piano partials to deviate slightly. Use fixed
scratch space. Map the stabilized chord jointly with an ordered, injective
assignment that keeps in-scale voices fixed and prefers correcting more voices,
then minimizing distance and choosing the lower note on ties. Retain source
audio when assignment or partial ownership is uncertain. Feed each assigned
partial a single frequency ratio shared by its note.

Append Mode at index 24 (0 Spectral, 1 Polyphonic). Keep FFT length, hop and
scope layout. Skip spectral coloration in Polyphonic. Fade the wet mix to
aligned dry, reset spectral synthesis state at a frame boundary, allow the new
overlap buffers to fill, and fade it back in. Integrate the selector using Chroma's
existing card controls, localization and generic parameter persistence. Existing
presets use the default mode. No new ABI, dependency, or project-key routing.

## Selectable processing FFT

Append a project-only normalized parameter at index 28 (0 / 0.5 / 1).
Prepare all processing FFTs, windows and frame coefficients at sample-rate
setup. Keep storage large enough for 2048 samples and apply the selected
configuration at block entry. Use an N/4 hop and N-sample matched dry delay.
Keep scope analysis at the existing two window lengths and cadence.

Guard explicit edits in the engine before changing the setting, accounting
for queued starts. Route configuration writes for inserts and rack children
through the same latency refresh. Keep preset and continuous-control paths
away from the configuration parameter. Use the existing ABI and serialized
parameter arrays.

Place a keyboard-accessible menu and millisecond readout between both
sideband toggles on both cards. Use live scope metadata for the readout.

## MIDI note source

Use Auto Shift's whole-track MIDI key and sidechain project reference. Add
the post-MIDI-FX held notes at the beginning of the block to the existing
event feed, so a consumer can resume after skipped render blocks. Follow the
Arrangement transport's existing note flush and chase.

Keep pitch-class selection and timed dual-path transitions in Chroma. Prime
the inactive path for one FFT window, crossfade for one hop, and coalesce
pending updates to the latest mask. Append the MIDI mask to optional scope
telemetry after the existing 1026 values. Use the shared S/L keyboard with a
compact source menu.
