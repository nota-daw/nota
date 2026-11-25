**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Develop | PR a93d1add |
|---|---:|---:|
| Idle | 0 | 0 |
| 8 effect rack tracks | 5.0 | 5.3 |
| 8 true-peak limiter tracks | 4.6 | 5.1 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Develop | PR a93d1add |
|---|---:|---:|
| 8 effect rack tracks | 2.4 | 2.5 |
| 8 true-peak limiter tracks | 2.9 | 3.2 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Develop | PR a93d1add |
|---|---:|---:|
| 8 effect rack tracks | 22.5 | 22.1 |
| 8 true-peak limiter tracks | 24.6 | 28.1 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Develop | PR a93d1add |
|---|---:|---:|
| 16 synth tracks · 8192-frame chunks | 7.3 | 9.5 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Develop | PR a93d1add |
|---|---:|---:|
| 16 synth tracks · 8192-frame chunks | 31.6 | 36.5 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-25T16:33:29Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/private/tmp/nota-bug-batch-before/src/native/nota.engine/build
build: after=/Users/egorkhindikaynen/repos/nota/nota-bug-batch/src/native/nota.engine/build
```
</details>
