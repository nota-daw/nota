**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 3.7 | 4.0 |
| 8 audio tracks | 22.4 | 24.1 |
| 32 audio tracks | 34.6 | 35.5 |
| 8 synth tracks | 4.9 | 4.8 |
| 32 synth tracks | 17.3 | 17.6 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.2 |
| 8 audio tracks | 5.8 | 8.6 |
| 32 audio tracks | 20.9 | 21.9 |
| 8 synth tracks | 2.0 | 1.9 |
| 32 synth tracks | 6.5 | 6.7 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.2 |
| 8 audio tracks | 45.4 | 62.7 |
| 32 audio tracks | 195 | 207 |
| 8 synth tracks | 17.8 | 14.6 |
| 32 synth tracks | 60.6 | 58.8 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 10.8 | 16.6 |
| 16 synth tracks · 8192-frame chunks | 8.9 | 12.2 |
| 64 audio tracks · 8192-frame chunks | 36.1 | 39.9 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 80.6 | 114 |
| 16 synth tracks · 8192-frame chunks | 36.0 | 42.9 |
| 64 audio tracks · 8192-frame chunks | 302 | 337 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.11 | 0.13 |
| 128 audio tracks · 64-sample buffer | 0.07 | 0.12 |
| 192 audio tracks · 64-sample buffer | 0.56 | 0.24 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 71.0 | 70.9 |
| 128 audio tracks · 64-sample buffer | 79.0 | 79.0 |
| 192 audio tracks · 64-sample buffer | 124 | 87.7 |

<details><summary>Machine and builds</summary>

```
date: 2026-10-27T21:11:41Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/tmp/bench/libs/before
build: after=/tmp/bench/libs/after
```
</details>
