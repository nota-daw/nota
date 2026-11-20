# Bug fix — Chroma bass spectrum

- **Assessment** [Chroma bass spectrum](assessment.md)
- **Status** Applied

Chroma filters the Dry and Tuned stereo taps with a sixth-order Butterworth
low-pass at 650 Hz, then decimates to at most 5.6 kHz. Each channel uses a
180 ms Hann window and a 1024-point FFT. The results sum channel powers and
match the existing analyzer's gain before a smoothstep blend from 250 to
400 Hz. The bass estimate refreshes on alternate publications. The scope
continues to return 512 values per trace and report 1024 samples of latency.

`Chroma::setSampleRate` prepares the FFT, buffers, filter coefficients and
blend weights. The existing card renderer draws both traces. The changelog,
feature list and bilingual manual describe the sharper bass display.

Native regression tests cover two-tone resolution, stereo polarity, alias
rejection and publication cadence. The managed smoke test reads the
antiphase fixture through the C ABI.
