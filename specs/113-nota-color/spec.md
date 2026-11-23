# Feature Specification: Nota Color

**Feature Branch**: `feat/nota-color`
**Created**: 2026-11-16
**Status**: Draft
**Input**: Add a built-in audio effect for color bass and harmonic tuning.
**Issue**: [nota-daw/nota#977](https://github.com/nota-daw/nota/issues/977)

## Summary

Add **Nota Color** to the device browser as a built-in stereo audio effect.
Insert it on an audio or instrument track to retune spectral components within
a selected frequency band and add tuned resonance. Choose notes manually or
follow the MIDI from another instrument track. Both card sizes show the live
Dry and Tuned spectrum, the processing band and the latency of the selected FFT.

## Spectral retuning

- FR-026: Process the chosen band with semitone correction, out-of-scale
  attenuation, in-scale resonance and attack protection. The selected lower
  note wins ties. Empty and full masks pass delay-aligned dry audio.
- FR-027: Provide independent Amount, Resonance and Decay controls. Apply
  stereo-linked Morph to shifted components and Gate to the completed wet
  band. Both default off. The default latency is 1024 samples (see FR-035).
- FR-028: Provide one spectral processing mode. Keep kind 27, parameter indices
  0–23, automation, default values, scope shape and all six preset identities.
  Load former Polyphonic sessions using the retained values in HQ.
- FR-029: Verify routing, resonance, stereo, attacks, automation, latency and
  real-time behavior. Supply level-matched comparisons with the previous
  prototype. Evaluate timbre, attacks and decay separately in listening comparisons.

## Card sizes and sideband mute

- FR-030: Provide a compact S card with the spectrum, band handles, both
  sideband switches, Amount, Resonance and Mix. Keep the full L card as the
  default. Both sizes share the same device, settings and preset.
- FR-031: Mute frequencies below Low or above High independently in the wet
  signal. Mutes work with any note mask and at any Amount. Mix at zero
  plays the full time-aligned dry signal.
- FR-032: Draw Dry and Tuned as thin, distinct spectral lines. Dry follows the
  theme accent. Tuned uses its RGB inverse and follows live theme changes.

## User Scenarios & Testing

### User Story 1 — Color a bass (P1)
Add Nota Color from the device browser to an audio or instrument track and tune
its spectrum to C minor or another selected scale. A sustained out-of-scale tone
in the processing band shifts to a neighboring allowed note. Sub bass below
the band remains audible with Mute low off.

### User Story 2 — Shape and recall the sound (P2)
Choose custom notes or load one of the six factory presets. Set the band,
adjust the sound and save the project. Reopening or duplicating the device
restores its settings and card size. Automation updates the sound controls on
the card.

### User Story 3 — Follow chords from another track (P2)
Choose an instrument track in the **MIDI** menu. As its Arrangement clips play
or notes arrive live, the keys show the held pitch classes and tuning follows
the chord. Select **Manual** to restore the previous scale and notes.

### Edge Cases
Silence, non-finite inputs/params, empty masks, crossed band limits, rapid note changes, mono/antiphase stereo, variable blocks and sample rates, bypass, end-of-stream tail.

## Requirements
- FR-001: Add **Nota Color** to the device browser as a built-in audio effect.
- FR-002: Offer root and Major, Minor, Major pentatonic, Minor pentatonic and
  Custom scales in Manual mode. The keyboard edits twelve pitch classes.
- FR-003: Provide Amount, Resonance, Decay, Low, High, Mix and Output.
  Default to C minor, full wet, moderate resonance and a 150 Hz–8 kHz band.
- FR-004: Stereo-coherent spectral retuning with smooth control changes, bounded resonant tails and preserved outside-band content.
- FR-005: Report the processing latency and keep dry and wet audio aligned.
  Mix at zero and an empty Custom mask preserve the delayed input.
- FR-006: Sound parameters use the existing automation and preset paths.
  Cloning or saving a project retains the device settings.
- FR-007: No audio-thread allocation/locking/I/O and no new dependency.
- FR-008: Include six factory presets for tuning and color-bass sounds.
- FR-009: Display the dry input and shifted spectral components as separate
  lines on the live spectrum.

## Success Criteria
- SC-001: The shifted component of a sustained tone with a neighboring allowed
  note is within 10 cents of that note.
- SC-002: Delayed dry reconstruction has maximum error below 1e-5 for finite normal input.
- SC-003: All tested settings produce finite output, and silent tails decay below -90 dBFS.
- SC-004: Build and smoke tests pass, performance and latency are measured.
- SC-005: Listeners can compare level-matched dry and wet renders of FM bass,
  growls and chords.

## Assumptions
Use Nota's existing device and project conventions. The FFT setting lets
listeners choose the tradeoff between latency and frequency resolution.

## Selectable processing FFT

- FR-033: Color offers 512, 1024 and 2048 sample FFT windows. New devices and
  older projects use 1024. Both card sizes show a quiet FFT menu and its
  latency in milliseconds between the two sideband switches.
- FR-034: FFT is a per-device project setting retained on duplication. Sound
  presets, A/B, automation, MIDI Learn, CV and rack macros leave it alone.
  Explicit changes work with transport stopped, during playback, during
  count-in and while recording.
- FR-035: Report and compensate the selected window's processing latency,
  plus one additional window when Gate is enabled, and 2048 samples when
  Pre pitch is enabled. Recompute delay compensation when these settings change.
  Preserve the 1024-sample default sound and the existing 1026-float scope
  contract, including the independent long bass and short full-band analysis.
- Acceptance covers all three sizes and sample rates (44.1, 48 and 96 kHz),
  delay-aligned audio, zero render allocations, callback timings at 64 and
  256 frames, persistence and the cards at both display scales.

## MIDI note source

- FR-036: The S and L Color cards select Manual or another instrument track.
  The MIDI source follows Arrangement clips and live notes after track MIDI
  effects. The keyboard displays the resulting pitch classes. Selecting
  Manual restores the stored root, scale and custom notes.
- FR-037: MIDI drives regular inserts and devices in their Audio Effect Racks,
  including project render. Save the route as a project sidechain reference.
  Unkeyed projects use Manual. A missing source or empty chord fades out
  correction while retaining the selected latency.
- FR-038: Respect event offsets and overlapping notes, keep transitions short,
  and uphold the existing allocation-free audio and idle behaviour.

## Wet spectral gate

- FR-039: Analyse the aligned Dry and Wet spectra after resonance. When a
  Dry frequency bin falls below Gate threshold, close the stereo-linked
  gain on the corresponding Wet bin. Apply the gain inside the processing
  band, before the sidebands and Mix.
- FR-040: Change the Gate switch during playback. The threshold remains
  adjustable and automatable from −80 to 0 dBFS with a default of −60 dBFS.
  Projects retain the switch and threshold.
- FR-041: Show the total latency next to FFT in both card sizes. Reflect
  the gate's attenuation of shifted components in Tuned.

## Pre and Post pitch

- FR-042: Pre pitch offsets the selected band before tuning. Its enable
  switch can change during playback and updates delay compensation.
- FR-043: Post pitch offsets correction and resonance frequencies. Both
  pitch knobs support ±100 cents and automation at fixed latency.

## Earlier iterations

The entries below record prototype work. The device described above uses the
current spectral retuning path, controls and latency options.

### Naturalness extension (accepted 2026-11-17)
- FR-010: Track spectral peaks between frames, preserving synthesis-phase continuity across peak-bin and reference-channel changes; preserve relative stereo phase. New peaks start from input phase.
- FR-011: Morph 0–100% blends wet magnitudes toward dry magnitudes at the same frequencies, retaining wet phase, with stereo-linked gain; defaults off. Zero wet bins stay zero.
- FR-012: Gate uses dry magnitudes per frequency as a stereo-linked sidechain, threshold −80–0 dBFS, 6 dB knee, 5 ms opening and 50 ms closing; defaults off, threshold −60 dBFS.
- FR-013: Apply Morph then Gate after resonance and before synthesis, only inside the processing band; reflect their gains in Tuned telemetry.
- FR-014: Append Morph, Gate and Gate threshold as parameters 21–23. Old 21-param projects retain off defaults. Automation, MIDI Learn, presets, reset and persistence work normally.
- FR-015: Keep the 560×260 card with a second control row and at least 96 px graph height. Preserve latency and real-time safety.
Acceptance: target tones within 10 cents; block partition invariance; finite output and silence; no render allocations; stable mono/antiphase/right-only stereo; Morph/Gate endpoints and automation; old-state loading; before/after FM, growl, chord, attack and decay WAVs plus CPU measurements. Naturalness is evaluated by listening.

### Clean color bass iteration (accepted 2026-11-18)
User goal: cleaner color bass with a clear attack, stable tuning and useful resonant
character. Retain all 24 parameters and existing latency; measured CPU growth is accepted.

- FR-016: Sustained partials at note-selection boundaries settle on one allowed note,
  including 440/880/1760 Hz in C minor. Small frequency-estimation fluctuations must not
  repeatedly change the selected note. A fresh exact tie chooses the lower note.
- FR-017: Track moving partials without handing their phase/note state to unrelated weak
  peaks. Preserve stereo relations and smooth changes in FFT-bin region placement.
- FR-018: Automatically preserve localized attacks while retaining sustained color-bass
  coloration, without a new parameter or added latency.
- FR-019: With Gate off, Decay controls resonant release after the input stops; there is no
  hidden short release after 60 ms. Band/scale changes release obsolete resonance smoothly.
  Sleep only after delayed input, overlap buffers and audible resonant tails have drained.
- FR-020: Retain the 24-parameter layout, defaults, normalization, telemetry contract,
  kind 27, automation and old-state support. Keep three existing preset identities and add
  three presets demonstrating tight bass, clear growl and spacious resonance.
- FR-021: Verify Nota Color through the shared C ABI smoke suite. Record
  listening comparisons and CPU measurements in the verification report.

Acceptance: stable single-note boundary output within 10 cents; retained delayed-dry
reconstruction; finite automation/overlap/crossing signals; mono/antiphase/right-only stereo;
four sample rates and arbitrary blocks; no render allocations; Gate-off release follows
Decay and drains below −90 dBFS before sleep. Compare attacks and moving tones with the
baseline. Evaluate audible artifacts and timbre by listening.

### Polyphonic scale correction
- FR-022: On an audio or instrument track, Polyphonic moves individual notes of a
  sustained chord to the selected Color scale. It targets up to four confident
  voices in C3–C6. C4–E4–G4 becomes C4–Eb4–G4 in C minor. In-scale notes retain
  their tuning.
- FR-023: Preserve note order and separate voices. Prefer the smallest total
  movement, at most two semitones for each corrected voice, then the lower note
  on ties. Leave unresolved notes and ambiguous shared partials unshifted.
  Preserve stereo, attack and timing. The existing N-sample latency applies to
  both modes, including transitions.
- FR-024: A single appended, persisted Mode parameter selects Spectral or
  Polyphonic. Older projects and presets open in Spectral. Polyphonic uses the
  existing scale, amount, mix and output controls. Spectrum-specific controls
  are inactive. Switching during playback fades through delayed dry audio.
- FR-025: Verify synthesized triads, inversions and seventh chords, notes that
  would collide, scale changes, stereo, pitch error, unchanged latency and
  real-time safety. Supply level-matched recordings of piano chords for audition.
