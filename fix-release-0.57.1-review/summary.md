| Scenario | develop vs 0.56.1 (x86 CI) | this PR vs 0.56.1 (arm64) | work 0.56.1 → PR | wall 0.56.1 → PR (ms) | verdict |
|---|---:|---:|---|---|---|
| `effect-15` Nota Strata x8 on Synth | +27.28 % | +4.33 % | 1.22 M → 1.27 M | 1.75 ms → 1.71 ms | SLOWER (work) |
| `effect-02` Nota Reverb x8 on Synth | +12.12 % | -1.99 % | 89.55 M → 87.77 M | 30.41 ms → 30.49 ms | ok |
| `effect-12` Nota Crush x8 on Synth | +10.11 % | -2.01 % | 6.75 M → 6.61 M | 3.78 ms → 3.65 ms | ok |
| `instrument-16` Keys, one track | +9.98 % | -0.64 % | 68.79 M → 68.35 M | 2.29 ms → 2.38 ms | ok |
| `effect-03` Nota Delay x8 on Synth | +9.56 % | -24.34 % | 13.59 M → 10.28 M | 5.65 ms → 5.14 ms | ok |
| `effect-07` Nota Auto Filter x8 on Synth | +9.19 % | +0.44 % | 8.08 M → 8.12 M | 4.95 ms → 4.66 ms | ok |
| `effect-23` Nota Flanger x8 on Synth | +9.03 % | +1.98 % | 10.11 M → 10.31 M | 5.08 ms → 5.12 ms | SLOWER (work) |
| `effect-24` Nota Phaser x8 on Synth | +5.68 % | +0.73 % | 20.41 M → 20.56 M | 10.81 ms → 10.76 ms | ok |
| `effect-25` Nota Chorus x8 on Synth | +5.31 % | +0.68 % | 25.73 M → 25.90 M | 10.07 ms → 9.99 ms | ok |
| `effect-04` Nota Utility x8 on Synth | +5.21 % | -0.94 % | 14.65 M → 14.51 M | 6.35 ms → 6.61 ms | ok |
| `effect-base` Synth alone (effect baseline) | +3.32 % | -1.16 % | 35.55 M → 35.14 M | 1.42 ms → 1.38 ms | ok |
| `instrument-00` Synth, one track | +3.32 % | -1.08 % | 35.55 M → 35.17 M | 1.39 ms → 1.40 ms | ok |
| `effect-01` Nota Compressor x8 on Synth | +3.02 % | -9.17 % | 18.20 M → 16.53 M | 7.99 ms → 7.52 ms | ok |
| `instrument-15` Consort, one track | +2.67 % | +0.34 % | 162.81 M → 163.37 M | 7.29 ms → 7.47 ms | ok |
| `effect-19` Nota Shutter x8 on Synth | +2.52 % | +0.67 % | 9.34 M → 9.41 M | 4.18 ms → 4.16 ms | ok |
| `instrument-14` Pentad, one track | +2.22 % | +0.30 % | 273.38 M → 274.19 M | 12.54 ms → 12.62 ms | ok |
| `effect-16` Nota EQ-3 x8 on Synth | +1.37 % | +0.47 % | 18.75 M → 18.84 M | 8.19 ms → 8.17 ms | ok |
| `effect-21` Nota Prism x8 on Synth | +0.88 % | -0.90 % | 46.92 M → 46.50 M | 16.38 ms → 16.06 ms | ok |
