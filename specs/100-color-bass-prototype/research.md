# Research
- Decision: stereo-linked phase vocoder, rather than only a resonator bank. Rationale: target is spectral retuning, not just added pitched ringing. Alternative: comb filters cannot move arbitrary input partials into a scale.
- Decision: reuse Signalsmith RealFFT. Its N/2 complex outputs pack DC/Nyquist in bin zero; inverse needs 1/N scaling. Existing convolution demonstrates this contract.
- Decision: fixed quality mode and N-sample latency. Existing Device::latencySamples and track PDC support this. Host bypass skips processing and is not a latency-aligned comparison; use internal Mix=0 instead.
- Decision: energy-based stereo peak detection avoids cancellation for antiphase input. Shared shifts plus retained channel phase offset preserve stereo.
- Decision: bounded spectral tail envelopes, no unstable feedback gain. No new dependencies.

- Implementation detail: place complete peak regions at integer FFT-bin offsets, while exact phase advancement supplies the fractional frequency shift. This avoids interpolating opposite-phase Hann bins. Tones verify target accuracy, but moving peak regions may still produce audible prototype artifacts and require listening iteration.

## Naturalness extension
Chroma documents magnitude-domain Morph and a dry-sidechained per-bin Gate: https://www.xynth.audio/docs/plugins/chroma#spectral . Its full internal phase algorithm is not disclosed. Nota independently tracks local peaks and propagates output phase while preserving shared stereo rotation. Morph retains phase per FFT bin; reweighting resonant bins can still alter the resultant waveform phase, so carrier-phase tests isolate the no-resonance case. Neither effect promises sample-identical original audio or Chroma equivalence.

## Clean color bass decisions — 2026-11-18
- Current-source probes at 48 kHz, C minor, full Amount/Mix and Resonance=0 show 439 Hz
  settling at 415 Hz and 441 Hz at 466 Hz, but 440 Hz produces multiple components near
  440 Hz. At 880/1760 Hz the level also drops approximately 6/11 dB. The per-frame nearest
  note decision has no hysteresis. Decision: hold note identity across matched partials;
  smoothing the frequency shift alone does not stabilize target selection.
- The first FFT frame uses integer-bin frequency, which can select a different note from
  the settled instantaneous-frequency estimate. Decision: interpolate the initial peak
  estimate and defer note identity until enough signal is present for a reliable estimate.
- Retain the existing FFT/backend and independent DSP; no new dependency. Alternatives:
  replacing the complete shifter or increasing FFT length would enlarge scope or latency.
- Preserve relative phase across bins/channels and time. Phase-aware bin transitions must
  be tested, rather than naively interpolating opposite-phase Hann bins. Background:
  https://signalsmith-audio.co.uk/writing/2023/stretch-design/ .
- Existing checked-in verification names a native harness and shell runner that are absent
  from the current tree. Decision: restore reproducible source and baseline generation.

## Polyphonic voice tracking
- Use harmonic evidence and subtract the evidence of a selected note before
  choosing the next one. AudioHarmony uses the same starting principle for
  Audio to MIDI. The native detector uses existing Chroma peak frequencies and
  bounded scratch space to fit the 43–46 ms latency budget.
- At the chosen FFT size, adjacent fundamentals can merge. A narrow fundamental
  tolerance leaves a blended peak dry instead of moving both notes. The
  detector also models C2–B2 as dry voices so their harmonics cannot masquerade
  as notes in the correction range. Choosing to leave uncertainty audible
  protects the chord more reliably than forcing every partial onto the scale.
