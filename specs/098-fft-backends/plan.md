# Implementation plan: FFT backends

Enable Signalsmith's Accelerate backend on Apple and its PFFFT backend elsewhere, retaining the scalar fallback for unsupported precisions and sizes. No C ABI or RealFFT call-site changes are planned.

Pin and verify PFFFT and its license before integration. Apply backend definitions consistently to all engine translation units that instantiate Signalsmith templates; link platform libraries privately. FFT setup and destruction must happen off the audio thread. Verify unaligned buffers and small transforms as well as double precision.

Compare scalar and accelerated FFTs using deterministic signals, including forward/inverse transforms and small and unaligned buffers. Compare convolution and analyzer results at the specified sample rates with a documented tolerance. Benchmark representative FFT sizes and engine scenarios against the baseline; run platform builds and the Linux performance gate. Document any unavailable platform verification rather than claiming it passed.

Constitution: preserve audio-thread safety, C ABI, licensing, and platform smoke-test gates. No new public data model or interface.
