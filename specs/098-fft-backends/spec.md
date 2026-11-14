# Feature Specification: Accelerated FFT backends

**Created**: 2026-11-14  
**Status**: Draft  
**Input**: Approved issue #586

## User Scenarios & Testing

### Faster FFT processing (P1)

As a Nota user, I want FFT-based processing to use available platform acceleration while preserving the sound and analyzer results of existing projects.

**Independent test**: Compare convolution output and analyzer results against the scalar build at supported sample rates, and benchmark representative transform sizes.

1. Given an existing project using convolution, when it is rendered with an accelerated build, its output matches the scalar build within a measured floating-point tolerance.
2. Given an analyzer, when it processes the same signal with either build, its results match within a measured floating-point tolerance.
3. Given playback with convolution, when FFT processing runs, the audio thread does not allocate, block, or perform I/O.

### Edge Cases

- Small transforms and unsupported precision must retain a correct fallback.
- Unaligned inputs and outputs must produce correct results.
- Setup and teardown must remain outside the audio thread.

## Requirements

- **FR-001**: Enable the existing Accelerate backend on macOS and PFFFT on Linux and Windows, without changing RealFFT call sites.
- **FR-002**: Preserve transform normalization and results within a justified numerical tolerance; measure maximum errors.
- **FR-003**: Preserve real-time safety and existing public interfaces.
- **FR-004**: Pin and verify any new dependency and document its license and copyright notices.
- **FR-005**: Measure transform and engine performance against the scalar baseline.
- **FR-006**: Do not change analyzer windows or sizes, convolution partitioning, or introduce FFTW or IPP.

## Success Criteria

- **SC-001**: Convolution and analyzer comparisons pass at 44.1, 48, 96 and 192 kHz, covering convolution blocks 1024, 2048 and 4096.
- **SC-002**: Benchmarks cover FFT sizes 512, 1024, 2048, 4096, 8192 and 16384 and the convolution path; results and environment are recorded.
- **SC-003**: Platform builds and smoke tests pass on macOS, Linux and Windows, and the Linux performance gate passes.
- **SC-004**: Audio-thread execution contains no new allocation, blocking or I/O.

## Assumptions

- Null tests use a justified numerical tolerance, as explicitly agreed with the user; bit identity is not required.
- Double precision may retain the scalar backend where PFFFT does not support it.
