# Bug assessment — Chroma bass spectrum

The approved prototype plan calls for narrower Dry and Tuned peaks. The same
1024-sample display window serves the full frequency range. At 44.1 kHz its
bins are about 43 Hz apart, so a single bass tone spans many columns on the
logarithmic axis. The S and L renderer already draws single-pixel lines.

Add a telemetry-only stereo bass analyzer on the existing Dry and Tuned taps.
Use anti-alias filtering and integer decimation, a Hann window near 180 ms,
and an FFT grid no coarser than 6 Hz. Sum channel powers and refresh the bass
every other publication. Use the bass estimate up to 250 Hz and blend into
the existing display between 250 and 400 Hz. Prepare storage at sample-rate
setup and preserve the audio processing, scope format and card rendering.

Verify two-tone resolution and antiphase input at 44.1, 48 and 96 kHz, the
transition and pixel spacing on both cards, bitwise audio equality, callback
cost at 64 and 256 frames, and zero render allocations.
