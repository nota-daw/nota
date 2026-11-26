# Nota Vocoder benchmark for #1117

| Architecture | Last green base | Develop before fix | This PR | Change vs develop |
| --- | ---: | ---: | ---: | ---: |
| ARM64 native | 35.21 ms | 35.37 ms | 30.78 ms | -13.0% |
| x86-64 via Rosetta | 88.77 ms | 88.60 ms | 77.28 ms | -12.8% |

Each entry is the median wall time for three seconds of audio after a half-second
warm-up. The device runs 32 bands with an external stereo carrier at 48 kHz in
512-frame blocks. Eleven runs per version were interleaved in rotating order.
The benchmark times the vocoder DSP directly.

Apple M2 (Mac14,2), macOS 27.2.1, Apple Clang 21.0.0, Release `-O3`.
The x86-64 build runs through Rosetta on the same host.

| Version | Revision |
| --- | --- |
| Last green base | `5989d60a80f8a77d19a7759da4e486e697deecde` |
| Develop before fix | `637f20e8cffdbf3afb7be59af6e761d0d76f032c` |
| This PR | `e1dc34fe401de1b257faa6b33a6a2bb905d6ab75` |

The last green Windows CI run reported 104.2 ms for Vocoder and 148.5 ms for
Chamber. The failing develop run reported 85.3 ms for Vocoder and 72.5 ms for
Chamber. Those whole-engine timings are listed separately from the DSP
benchmark. [Last green Windows job](https://github.com/nota-daw/nota/actions/runs/45595774717/job/139141313479) · [Failing Windows job](https://github.com/nota-daw/nota/actions/runs/45890366199/job/140170117626)

## Reproduce

Copy `benchmark.cpp` alongside the checkout or export its path. Build it in
each revision's checkout and run `--sample` once per trial, alternating revisions.
Use the same architecture for every build in one comparison.

```sh
native=src/native/nota.engine
clang++ -arch arm64 -arch x86_64 -O3 -fno-math-errno -std=c++20 \
  -I "$native/src" -I "$native/include" /path/to/benchmark.cpp \
  "$native/src/devices/effects/Vocoder.cpp" -o /tmp/nota-vocoder-bench
arch -arm64 /tmp/nota-vocoder-bench --sample
arch -x86_64 /tmp/nota-vocoder-bench --sample
```

Each invocation prints one wall-time measurement in milliseconds. Supplying an
output file instead of `--sample` records audio and scope data for a deterministic
sequence of parameter changes, carrier transitions, hold and sample-rate resets.
