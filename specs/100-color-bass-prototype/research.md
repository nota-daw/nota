# Research
- Decision: stereo-linked phase vocoder, rather than only a resonator bank. Rationale: target is spectral retuning, not just added pitched ringing. Alternative: comb filters cannot move arbitrary input partials into a scale.
- Decision: reuse Signalsmith RealFFT. Its N/2 complex outputs pack DC/Nyquist in bin zero; inverse needs 1/N scaling. Existing convolution demonstrates this contract.
- Decision: fixed quality mode and N-sample latency. Existing Device::latencySamples and track PDC support this. Host bypass skips processing and is not a latency-aligned comparison; use internal Mix=0 instead.
- Decision: energy-based stereo peak detection avoids cancellation for antiphase input. Shared shifts plus retained channel phase offset preserve stereo.
- Decision: bounded spectral tail envelopes, no unstable feedback gain. No new dependencies.

- Implementation detail: place complete peak regions at integer FFT-bin offsets, while exact phase advancement supplies the fractional frequency shift. This avoids interpolating opposite-phase Hann bins. Tones verify target accuracy, but moving peak regions may still produce audible prototype artifacts and require listening iteration.

## Naturalness extension
Chroma documents magnitude-domain Morph and a dry-sidechained per-bin Gate: https://www.xynth.audio/docs/plugins/chroma#spectral . Its full internal phase algorithm is not disclosed. Nota independently tracks local peaks and propagates output phase while preserving shared stereo rotation. Morph retains phase per FFT bin; reweighting resonant bins can still alter the resultant waveform phase, so carrier-phase tests isolate the no-resonance case. Neither effect promises sample-identical original audio or Chroma equivalence.
