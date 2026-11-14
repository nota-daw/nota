# Tasks: FFT backends

## Foundation
- [ ] T001 Verify PFFFT source, supported platforms, API and licenses; pin dependency.
- [ ] T002 Establish scalar baseline and compare deterministic FFT results.

## Accelerated processing (P1)
- [ ] T003 Configure Accelerate on macOS and PFFFT on Linux and Windows in engine CMake.
- [ ] T004 Record third-party notices and license details for the chosen PFFFT revision.
- [ ] T005 Validate FFT size, precision, alignment and setup/teardown safety.
- [ ] T006 Compare convolution and analyzer output at all issue sample rates.
- [ ] T007 Benchmark FFT and engine scenarios, run perf gate and platform builds.
- [ ] T008 Run speckit-converge and document remaining verification.
