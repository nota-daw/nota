**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 2.3 | 2.0 |
| 8 audio tracks | 8.1 | 13.9 |
| 32 audio tracks | 25.5 | 25.7 |
| 8 synth tracks | 2.5 | 2.8 |
| 32 synth tracks | 9.7 | 7.9 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 4.7 | 4.6 |
| 8 audio tracks | 6.1 | 6.0 |
| 32 audio tracks | 24.7 | 22.1 |
| 8 synth tracks | 3.1 | 3.0 |
| 32 synth tracks | 7.8 | 7.9 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 4.7 | 4.6 |
| 8 audio tracks | 51.0 | 50.7 |
| 32 audio tracks | 217 | 202 |
| 8 synth tracks | 24.1 | 24.0 |
| 32 synth tracks | 73.6 | 74.5 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 11.6 | 12.8 |
| 16 synth tracks · 8192-frame chunks | 4.2 | 8.4 |
| 64 audio tracks · 8192-frame chunks | 40.1 | 40.3 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 85.6 | 91.0 |
| 16 synth tracks · 8192-frame chunks | 21.5 | 28.9 |
| 64 audio tracks · 8192-frame chunks | 331 | 331 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.43 | 0.4 |
| 128 audio tracks · 64-sample buffer | 0.23 | 0.45 |
| 192 audio tracks · 64-sample buffer | 5.8 | 3.2 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 54.0 | 49.7 |
| 128 audio tracks · 64-sample buffer | 73.7 | 79.5 |
| 192 audio tracks · 64-sample buffer | 210 | 94.8 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-11T07:07:34Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/tmp/bench089/libs/before
build: after=/tmp/bench089/libs/after
```
</details>
