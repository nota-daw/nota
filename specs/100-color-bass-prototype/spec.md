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
