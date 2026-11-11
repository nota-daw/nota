**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 3.2 | 3.1 |
| 8 audio tracks | 15.0 | 14.7 |
| 32 audio tracks | 31.4 | 32.4 |
| 8 synth tracks | 3.5 | 3.5 |
| 32 synth tracks | 10.9 | 11.2 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.3 |
| 8 audio tracks | 5.9 | 5.8 |
| 32 audio tracks | 21.3 | 21.2 |
| 8 synth tracks | 1.9 | 2.1 |
| 32 synth tracks | 6.6 | 6.7 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.3 |
| 8 audio tracks | 48.7 | 48.7 |
| 32 audio tracks | 199 | 199 |
| 8 synth tracks | 15.8 | 19.3 |
| 32 synth tracks | 65.4 | 63.6 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 12.0 | 13.9 |
| 16 synth tracks · 8192-frame chunks | 7.0 | 8.8 |
| 64 audio tracks · 8192-frame chunks | 36.7 | 36.8 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 86.9 | 98.3 |
| 16 synth tracks · 8192-frame chunks | 30.7 | 36.2 |
| 64 audio tracks · 8192-frame chunks | 307 | 306 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.27 | 0.29 |
| 128 audio tracks · 64-sample buffer | 0.01 | 0.05 |
| 192 audio tracks · 64-sample buffer | 4.9 | 4.5 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 64.4 | 65.7 |
| 128 audio tracks · 64-sample buffer | 77.3 | 78.9 |
| 192 audio tracks · 64-sample buffer | 223 | 194 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-11T11:46:57Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/tmp/bench563/libs/before
build: after=/tmp/bench563/libs/after
```
</details>
