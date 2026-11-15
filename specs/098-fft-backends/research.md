# FFT backend research

Signalsmith's RealFFT uses a complex transform of half the real size. The pinned Signalsmith PFFFT wrapper specializes float only; double retains the scalar implementation. The wrapper falls back below 16 complex points, copies unaligned buffers to preallocated aligned storage, and its destructor calls resize(0), which can allocate. Destruction as well as setup must therefore be kept off the audio thread.

PFFFT v1.3.0 (revision 179a48d486cbee8ec5416e5c163ad575c2efe1f1) supplies the API used by Signalsmith. It is built from its C sources rather than its upstream CMake project to avoid global build-setting changes. Its BSD-style terms and copyright notices are recorded in LICENSES.

A preliminary local macOS arm64 comparison of RealFFT forward and inverse results using a deterministic two-tone signal at sizes 16, 32, 64, 512, 1024, 2048, 4096, 8192 and 16384 found maximum absolute normalized differences of 1.073e-6 for Accelerate and 1.133e-6 for the previously selected PFFFT revision against scalar. This is not a replacement for the full acceptance tests; the comparison was performed outside the repository and has not yet been made reproducible in CI.

Repeating the same PFFFT comparison with v1.3.0 produced a maximum absolute normalized difference of 1.070e-6; all 64,736 values were finite.
