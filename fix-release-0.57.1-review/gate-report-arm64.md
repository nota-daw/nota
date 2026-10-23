## Engine performance: eaabf0c → a572fef

**FAIL**: 3 scenario(s) slower than the base: more than 1% more CPU instructions, or more than 15% more wall time (or now silent / erroring).

Work = CPU instructions per render, all engine threads (Valgrind, deterministic; effects: the effect's own share). Wall = fastest of the interleaved rounds, ms to render 1 s of audio on this machine; a scenario over the time limit is re-timed with more rounds before it fails.

| Scenario | | Work base | Work PR | Change | Wall base | Wall PR | Verdict |
| --- | --- | ---: | ---: | ---: | ---: | ---: | --- |
| `effect-15` | Nota Strata x8 on Synth | 1.22 M | 1.27 M | +4.33% | 1.75 ms | 1.71 ms | SLOWER (work) |
| `effect-23` | Nota Flanger x8 on Synth | 10.11 M | 10.31 M | +1.98% | 5.08 ms | 5.12 ms | SLOWER (work) |
| `effect-14` | Nota Ceiling x8 on Synth | 29.39 M | 18.87 M | -35.80% | 8.65 ms | 11.38 ms | SLOWER (time +32%) |
| `effect-24` | Nota Phaser x8 on Synth | 20.41 M | 20.56 M | +0.73% | 10.81 ms | 10.76 ms | ok |
| `effect-25` | Nota Chorus x8 on Synth | 25.73 M | 25.90 M | +0.68% | 10.07 ms | 9.99 ms | ok |
| `effect-19` | Nota Shutter x8 on Synth | 9.34 M | 9.41 M | +0.67% | 4.18 ms | 4.16 ms | ok |
| `effect-16` | Nota EQ-3 x8 on Synth | 18.75 M | 18.84 M | +0.47% | 8.19 ms | 8.17 ms | ok |
| `effect-07` | Nota Auto Filter x8 on Synth | 8.08 M | 8.12 M | +0.44% | 4.95 ms | 4.66 ms | ok |
| `effect-05` | Nota Audio Effect Rack x8 on Synth | 1.23 M | 1.23 M | +0.40% | 1.84 ms | 1.82 ms | ok |
| `instrument-15` | Consort, one track | 162.81 M | 163.37 M | +0.34% | 7.29 ms | 7.47 ms | ok |
| `instrument-14` | Pentad, one track | 273.38 M | 274.19 M | +0.30% | 12.54 ms | 12.62 ms | ok |
| `effect-10` | Nota Auto Shift x8 on Synth | 309.44 M | 309.49 M | +0.02% | 132.22 ms | 126.50 ms | ok |
| `effect-22` | Nota Lens x8 on Synth | 3.53 M | 3.53 M | +0.00% | 5.13 ms | 4.50 ms | ok |
| `effect-26` | Nota Vocoder x8 on Synth |  |  |  |  |  | new |
| `instrument-16` | Keys, one track | 68.79 M | 68.35 M | -0.64% | 2.29 ms | 2.38 ms | ok |
| `instrument-09` | Operator, one track | 181.43 M | 180.18 M | -0.69% | 9.95 ms | 10.19 ms | ok |
| `effect-11` | Nota Beat Repeat x8 on Synth | 15.35 M | 15.22 M | -0.90% | 5.63 ms | 5.33 ms | ok |
| `effect-21` | Nota Prism x8 on Synth | 46.92 M | 46.50 M | -0.90% | 16.38 ms | 16.06 ms | ok |
| `effect-04` | Nota Utility x8 on Synth | 14.65 M | 14.51 M | -0.94% | 6.35 ms | 6.61 ms | ok |
| `effect-18` | Nota Level x8 on Synth | 33.77 M | 33.41 M | -1.07% | 14.39 ms | 13.71 ms | ok |
| `instrument-00` | Synth, one track | 35.55 M | 35.17 M | -1.08% | 1.39 ms | 1.40 ms | ok |
| `effect-base` | Synth alone (effect baseline) | 35.55 M | 35.14 M | -1.16% | 1.42 ms | 1.38 ms | ok |
| `instrument-13` | Monolith, one track | 117.77 M | 115.77 M | -1.70% | 12.79 ms | 11.76 ms | ok |
| `effect-02` | Nota Reverb x8 on Synth | 89.55 M | 87.77 M | -1.99% | 30.41 ms | 30.49 ms | ok |
| `effect-12` | Nota Crush x8 on Synth | 6.75 M | 6.61 M | -2.01% | 3.78 ms | 3.65 ms | ok |
| `instrument-02` | Physical, one track | 89.99 M | 88.15 M | -2.05% | 3.15 ms | 3.11 ms | ok |
| `instrument-17` | Mosaic (no program), one track | 5.63 M | 5.30 M | -5.94% | 0.24 ms | 0.23 ms | ok |
| `instrument-01` | Sampler (no sample), one track | 4.71 M | 4.30 M | -8.77% | 0.20 ms | 0.20 ms | ok |
| `effect-20` | Nota Chamber x8 on Synth | 283.49 M | 257.58 M | -9.14% | 120.83 ms | 114.70 ms | ok |
| `effect-01` | Nota Compressor x8 on Synth | 18.20 M | 16.53 M | -9.17% | 7.99 ms | 7.52 ms | ok |
| `instrument-12` | Rhythm Machine, one track | 76.25 M | 68.85 M | -9.70% | 4.45 ms | 2.99 ms | ok |
| `instrument-06` | Volt, one track | 97.50 M | 87.68 M | -10.07% | 3.42 ms | 3.26 ms | ok |
| `mix-8` | 8 tracks, all playing | 1,108.90 M | 959.03 M | -13.52% | 41.52 ms | 8.74 ms | ok |
| `mix-1` | 1 tracks, all playing | 140.78 M | 121.63 M | -13.60% | 5.40 ms | 4.88 ms | ok |
| `mix-4` | 4 tracks, all playing | 555.71 M | 478.89 M | -13.82% | 20.97 ms | 6.99 ms | ok |
| `mix-16` | 16 tracks, all playing | 2,215.58 M | 1,908.11 M | -13.88% | 84.51 ms | 17.27 ms | ok |
| `mix-32` | 32 tracks, all playing | 4,429.22 M | 3,814.16 M | -13.89% | 166.30 ms | 25.76 ms | ok |
| `effect-08` | Nota Vintage x8 on Synth | 50.71 M | 42.46 M | -16.27% | 20.63 ms | 20.61 ms | ok |
| `effect-06` | Nota Valve x8 on Synth | 37.57 M | 30.56 M | -18.66% | 14.13 ms | 11.58 ms | ok |
| `effect-17` | Nota Forge x8 on Synth | 33.04 M | 26.04 M | -21.21% | 20.35 ms | 9.60 ms | ok |
| `instrument-11` | Flux, one track | 168.05 M | 130.61 M | -22.28% | 9.97 ms | 6.17 ms | ok |
| `effect-03` | Nota Delay x8 on Synth | 13.59 M | 10.28 M | -24.34% | 5.65 ms | 5.14 ms | ok |
| `effect-13` | Nota Dynamic EQ-8 x8 on Synth | 53.95 M | 37.33 M | -30.81% | 17.77 ms | 12.31 ms | ok |
| `instrument-10` | Grain, one track | 266.86 M | 180.76 M | -32.26% | 9.48 ms | 7.47 ms | ok |
| `instrument-05` | Aurora, one track | 189.10 M | 124.61 M | -34.11% | 6.81 ms | 4.27 ms | ok |
| `instrument-07` | Bass, one track | 50.09 M | 32.04 M | -36.03% | 3.10 ms | 1.37 ms | ok |
| `effect-00` | Nota EQ-8 x8 on Synth | 17.22 M | 9.48 M | -44.98% | 5.53 ms | 5.12 ms | ok |
| `sparse-32` | 32 tracks, 1 in 4 playing | 2,830.52 M | 1,465.49 M | -48.23% | 106.45 ms | 16.26 ms | ok |
| `effect-09` | Nota Orbit x8 on Synth | 14.49 M | 6.41 M | -55.81% | 5.27 ms | 3.35 ms | ok |
| `aurora-supersaw-2x` | Aurora Super Saw x2 | 1,642.44 M | 716.90 M | -56.35% | 62.13 ms | 14.72 ms | ok |
| `instrument-08` | Pendulum, one track | 264.16 M | 85.06 M | -67.80% | 14.78 ms | 2.92 ms | ok |
| `silent-32` | 32 tracks, no clips | 2,238.39 M | 658.97 M | -70.56% | 84.45 ms | 6.94 ms | ok |

