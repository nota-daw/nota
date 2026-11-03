**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before (#438) | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 2.3 | 2.4 |
| 8 audio tracks | 18.8 | 15.4 |
| 32 audio tracks | 28.7 | 29.3 |
| 8 synth tracks | 3.6 | 3.1 |
| 32 synth tracks | 13.0 | 10.1 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before (#438) | This PR |
|---|---:|---:|
| 1 audio track | 4.4 | 5.7 |
| 8 audio tracks | 8.1 | 8.1 |
| 32 audio tracks | 21.5 | 22.0 |
| 8 synth tracks | 2.2 | 2.5 |
| 32 synth tracks | 6.9 | 6.9 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (#438) | This PR |
|---|---:|---:|
| 1 audio track | 4.4 | 5.7 |
| 8 audio tracks | 61.5 | 61.2 |
| 32 audio tracks | 204 | 207 |
| 8 synth tracks | 20.0 | 22.3 |
| 32 synth tracks | 68.3 | 67.3 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before (#438) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 12.2 | 11.9 |
| 16 synth tracks · 8192-frame chunks | 5.8 | 4.7 |
| 64 audio tracks · 8192-frame chunks | 39.9 | 39.7 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (#438) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 89.2 | 82.8 |
| 16 synth tracks · 8192-frame chunks | 27.8 | 24.2 |
| 64 audio tracks · 8192-frame chunks | 325 | 331 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before (#438) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 2.7 | 2.1 |
| 128 audio tracks · 64-sample buffer | 0.09 | 2.2 |
| 192 audio tracks · 64-sample buffer | 2.0 | 0.75 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before (#438) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 53.4 | 79.4 |
| 128 audio tracks · 64-sample buffer | 75.2 | 94.8 |
| 192 audio tracks · 64-sample buffer | 116 | 122 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-03T07:58:22Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: after=/tmp/bench-450/libs/after
build: before=/tmp/bench-450/libs/before
```
</details>
