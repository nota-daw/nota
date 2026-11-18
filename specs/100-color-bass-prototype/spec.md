# Feature Specification: Nota Color prototype

**Feature Branch**: `feat/color-bass-prototype`
**Created**: 2026-11-16
**Status**: Accepted for local prototype
**Input**: A clean Chroma-like color bass effect in Nota, quality over latency, manual notes, isolated branch. The user explicitly defers the GitHub issue until listening approval.

## User Scenarios & Testing
### User Story 1 — Color a bass (P1)
Add Nota Color to an audio or instrument track and tune its spectrum to C minor or another selected scale. This is the core listening experiment.
Acceptance: an out-of-scale sustained tone in the processing band moves to the nearest selected note; subbass outside the band stays intact; resonance and decay change coloration without runaway output.
### User Story 2 — Shape and recall the sound (P2)
Choose custom pitch classes, adjust parameters and save/duplicate a project.
Acceptance: manual notes select Custom, empty Custom passes delayed dry audio; parameters survive clone/save/load; automation updates the panel; three presets are available.
### User Story 3 — Evaluate quality (P2)
Compare level-matched dry/wet WAVs and run the effect in the application.
Acceptance: synthetic FM, growl and chord examples are reproducible; numerical tests and build results are recorded; subjective approval remains the user's decision.
### Edge Cases
Silence, non-finite inputs/params, empty masks, crossed band limits, rapid note changes, mono/antiphase stereo, variable blocks and sample rates, bypass, end-of-stream tail.

## Requirements
- FR-001: An experimental built-in audio effect, named Nota Color (prototype), selectable in the device browser.
- FR-002: Root, major/minor/pentatonic major/pentatonic minor/Custom and twelve manual pitch classes, without MIDI routing.
- FR-003: Amount, Resonance, Decay, low/high processing limits, Dry/wet, Output; defaults C minor, full wet, moderate resonance, 150 Hz–8 kHz.
- FR-004: Stereo-coherent spectral retuning with smooth control changes, bounded resonant tails and preserved outside-band content.
- FR-005: Correct latency reporting and internally aligned dry/wet; dry/wet zero and empty Custom preserve delayed input.
- FR-006: Parameters participate in existing automation, preset, clone and project persistence paths.
- FR-007: No audio-thread allocation/locking/I/O and no new dependency.
- FR-009: Display dry input in the theme accent and shifted spectral components in blue as soft vertical lines.
- FR-008: Three presets and reproducible level-matched listening examples with technical validation.

## Success Criteria
- SC-001: Steady test tones in the selected band settle within 10 cents of the target note.
- SC-002: Delayed dry reconstruction has maximum error below 1e-5 for finite normal input.
- SC-003: All tested settings produce finite output, and silent tails decay below -90 dBFS.
- SC-004: Build and smoke tests pass, performance and latency are measured.
- SC-005: User can audition FM, growl and chord dry/wet pairs; similarity to Chroma is not claimed without reference audio.

## Assumptions
Local prototype only, no issue/PR/push. Existing Device ABI and UI conventions. Quality takes precedence over latency. Listening approval is separate from implementation acceptance.

## Naturalness extension (accepted 2026-11-17)
- FR-010: Track spectral peaks between frames, preserving synthesis-phase continuity across peak-bin and reference-channel changes; preserve relative stereo phase. New peaks start from input phase.
- FR-011: Morph 0–100% blends wet magnitudes toward dry magnitudes at the same frequencies, retaining wet phase, with stereo-linked gain; defaults off. Zero wet bins stay zero.
- FR-012: Gate uses dry magnitudes per frequency as a stereo-linked sidechain, threshold −80–0 dBFS, 6 dB knee, 5 ms opening and 50 ms closing; defaults off, threshold −60 dBFS.
- FR-013: Apply Morph then Gate after resonance and before synthesis, only inside the processing band; reflect their gains in Tuned telemetry.
- FR-014: Append Morph, Gate and Gate threshold as parameters 21–23. Old 21-param projects retain off defaults. Automation, MIDI Learn, presets, reset and persistence work normally.
- FR-015: Keep the 560×260 card with a second control row and at least 96 px graph height. Preserve latency and real-time safety.
Acceptance: target tones within 10 cents; block partition invariance; finite output and silence; no render allocations; stable mono/antiphase/right-only stereo; Morph/Gate endpoints and automation; old-state loading; before/after FM, growl, chord, attack and decay WAVs plus CPU measurements. Naturalness is evaluated by listening, without claiming Chroma DSP equivalence.

## Clean color bass iteration (accepted 2026-11-18)
User goal: cleaner color bass with a clear attack, stable tuning and useful resonant
character. Retain all 24 parameters and existing latency; measured CPU growth is accepted.
The user explicitly continues the local-prototype exception: defer the issue until final polish.

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
- FR-021: Check in a runnable native DSP test/listening generator. Supply before/after
  level-matched FM, growl, chord, attack and decay WAVs and repeatable CPU/block measurements.

Acceptance: stable single-note boundary output within 10 cents; retained delayed-dry
reconstruction; finite automation/overlap/crossing signals; mono/antiphase/right-only stereo;
four sample rates and arbitrary blocks; no render allocations; Gate-off release follows
Decay and drains below −90 dBFS before sleep. Compare attacks and moving tones with the
baseline. Human listening decides whether the result is cleaner and more interesting;
synthetic checks alone do not establish professional quality or superiority to Xynth.

## Polyphonic scale correction
- FR-022: On an audio or instrument track, Polyphonic moves individual notes of a
  sustained chord to the selected Chroma scale. It targets up to four confident
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

The approved local-prototype scope continues. The user selected real-time track
processing, Chroma's own scale selector and its existing latency.
