# Quickstart Validation Guide: DSP Core Performance & Optimization

## 1. Prerequisites
- macOS, Linux, or Windows development environment.
- CMake $\ge 3.24$, C++20 compiler (AppleClang, GCC, or MSVC).
- .NET 10 SDK for smoke tests.

## 2. Verification Steps

### Step 1: Unit & Smoke Test Suite Execution
Execute the automated end-to-end smoke test suite:
```bash
scripts/build.sh
```
Or directly on macOS/Linux:
```bash
dotnet test tests/Nota.SmokeTest/Nota.SmokeTest.csproj
```
**Expected Outcome**: All smoke tests, including audio engine initialization, routing, DSP device processing, and bounce/render checks pass with exit code 0.

### Step 2: Numerical Verification of FastMath
Verify that FastMath approximations stay within design tolerances:
- `FastMath::exp2f` relative error $< 1.5 \times 10^{-5}$ across $[-60, 60]$.
- `FastMath::log2f` relative error $< 1.2 \times 10^{-4}$ across $[10^{-5}, 10^3]$.
- `FastMath::fastTanh` smooth monotonic saturation matching `std::tanh` within 1% error.
- `FastMath::fastSin` LFO oscillation producing zero audible artifacts or discontinuities.

### Step 3: Real-Time Audio Memory Check
Verify via static analysis or debugger that:
- No `malloc`, `operator new`, `std::vector` reallocations occur in:
  - `Delay::process()`
  - `Compressor::process()`
  - `Eq::process()`
  - `Chamber::process()`
