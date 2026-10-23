Median of 5 interleaved runs, wall ms to render 1 s of audio.

| Scenario | 0.56.1 (main) | develop | This PR | This PR vs develop | This PR vs 0.56.1 |
|---|---:|---:|---:|---:|---:|
| EQ-8 x8 (`effect-00`) | 5.10 | 10.62 | 6.29 | -41 % | +23 % |
| Prism x8 (`effect-21`) | 14.66 | 26.66 | 15.61 | -41 % | +7 % |
| Auto Filter x8 (`effect-07`) | 4.66 | 6.73 | 5.50 | -18 % | +18 % |
| Compressor x8 (`effect-01`) | 5.75 | 5.72 | 5.50 | -4 % | -4 % |
| Dynamic EQ-8 x8 (`effect-13`) | 12.93 | 11.99 | 10.65 | -11 % | -18 % |
| EQ-3 x8 (`effect-16`) | 8.51 | 8.69 | 8.63 | -1 % | +1 % |
| Synth alone (`effect-base`) | 1.33 | 1.36 | 1.39 | +2 % | +5 % |

machine: Apple M4 Max, 14 cores, 36 GB  
os: macOS 26.6.2  
compiler: Apple clang version 21.0.0 (clang-2100.1.1.101)  
commits: main=eaabf0cb develop=87170235 fix=a8da703  
harness: tests/perf/nota_perf run <id> 1 (48 kHz, 512-frame blocks, 1 s of audio, wall ms), Release
