**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Develop | Preliminary fix |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 3.2 | 3.4 |
| 8 audio tracks | 20.6 | 18.8 |
| 32 audio tracks | 30.3 | 29.9 |
| 8 synth tracks | 3.7 | 3.9 |
| 32 synth tracks | 14.0 | 13.2 |
| 8 sidechained tracks | 20.6 | 21.3 |
| 32 sidechained tracks | 30.2 | 30.8 |
| 8 effect rack tracks | 5.0 | 5.5 |
| 8 true-peak limiter tracks | 5.6 | 4.9 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Develop | Preliminary fix |
|---|---:|---:|
| 1 audio track | 4.5 | 4.4 |
| 8 audio tracks | 6.3 | 6.2 |
| 32 audio tracks | 22.2 | 22.2 |
| 8 synth tracks | 3.0 | 2.9 |
| 32 synth tracks | 7.8 | 7.9 |
| 8 sidechained tracks | 12.4 | 12.4 |
| 32 sidechained tracks | 29.1 | 29.1 |
| 8 effect rack tracks | 3.4 | 2.8 |
| 8 true-peak limiter tracks | 3.0 | 3.3 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Develop | Preliminary fix |
|---|---:|---:|
| 1 audio track | 4.5 | 4.3 |
| 8 audio tracks | 52.2 | 51.3 |
| 32 audio tracks | 206 | 204 |
| 8 synth tracks | 24.7 | 24.2 |
| 32 synth tracks | 75.2 | 74.7 |
| 8 sidechained tracks | 54.9 | 55.3 |
| 32 sidechained tracks | 190 | 192 |
| 8 effect rack tracks | 27.1 | 24.0 |
| 8 true-peak limiter tracks | 25.4 | 28.2 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Develop | Preliminary fix |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 12.0 | 11.5 |
| 16 synth tracks · 8192-frame chunks | 4.4 | 7.9 |
| 64 audio tracks · 8192-frame chunks | 41.7 | 37.8 |
| 16 sidechained tracks · 8192-frame chunks | 17.7 | 17.9 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Develop | Preliminary fix |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 84.8 | 85.0 |
| 16 synth tracks · 8192-frame chunks | 24.1 | 32.2 |
| 64 audio tracks · 8192-frame chunks | 334 | 321 |
| 16 sidechained tracks · 8192-frame chunks | 95.1 | 94.7 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Develop | Preliminary fix |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 4.0 | 4.0 |
| 128 audio tracks · 64-sample buffer | 0.65 | 1.8 |
| 192 audio tracks · 64-sample buffer | 5.3 | 5.1 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Develop | Preliminary fix |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 50.2 | 52.9 |
| 128 audio tracks · 64-sample buffer | 72.8 | 74.6 |
| 192 audio tracks · 64-sample buffer | 166 | 170 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-25T16:04:52Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/private/tmp/nota-bug-batch-before/src/native/nota.engine/build
build: after=/Users/egorkhindikaynen/repos/nota/nota-bug-batch/src/native/nota.engine/build
```
</details>
