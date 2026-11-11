**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 2.7 | 2.3 |
| 8 audio tracks | 20.6 | 17.9 |
| 32 audio tracks | 31.1 | 32.7 |
| 8 synth tracks | 3.3 | 3.4 |
| 32 synth tracks | 10.4 | 10.9 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 4.5 | 5.6 |
| 8 audio tracks | 5.9 | 6.1 |
| 32 audio tracks | 21.7 | 22.3 |
| 8 synth tracks | 2.0 | 2.2 |
| 32 synth tracks | 6.8 | 6.9 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 4.5 | 5.6 |
| 8 audio tracks | 50.4 | 50.2 |
| 32 audio tracks | 201 | 203 |
| 8 synth tracks | 17.5 | 20.1 |
| 32 synth tracks | 64.0 | 66.6 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 12.0 | 11.6 |
| 16 synth tracks · 8192-frame chunks | 8.0 | 5.1 |
| 64 audio tracks · 8192-frame chunks | 36.8 | 36.9 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 86.5 | 84.0 |
| 16 synth tracks · 8192-frame chunks | 35.9 | 26.0 |
| 64 audio tracks · 8192-frame chunks | 306 | 305 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.77 | 0.69 |
| 128 audio tracks · 64-sample buffer | 0.2 | 3.6 |
| 192 audio tracks · 64-sample buffer | 6.0 | 6.2 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 63.3 | 61.9 |
| 128 audio tracks · 64-sample buffer | 76.8 | 116 |
| 192 audio tracks · 64-sample buffer | 203 | 227 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-11T11:56:07Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: after=/tmp/bench563/libs/after
build: before=/tmp/bench563/libs/before
```
</details>
