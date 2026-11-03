**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before (#438) | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 3.5 | 3.8 |
| 8 audio tracks | 21.7 | 21.5 |
| 32 audio tracks | 32.4 | 32.8 |
| 8 synth tracks | 4.0 | 4.1 |
| 32 synth tracks | 14.6 | 14.2 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before (#438) | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.2 |
| 8 audio tracks | 8.0 | 7.8 |
| 32 audio tracks | 20.9 | 21.3 |
| 8 synth tracks | 2.0 | 2.0 |
| 32 synth tracks | 6.8 | 6.7 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (#438) | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.2 |
| 8 audio tracks | 61.4 | 60.9 |
| 32 audio tracks | 196 | 203 |
| 8 synth tracks | 17.5 | 16.8 |
| 32 synth tracks | 65.8 | 61.9 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before (#438) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 12.4 | 12.8 |
| 16 synth tracks · 8192-frame chunks | 5.9 | 9.6 |
| 64 audio tracks · 8192-frame chunks | 35.9 | 37.9 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (#438) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 89.8 | 88.9 |
| 16 synth tracks · 8192-frame chunks | 30.2 | 38.3 |
| 64 audio tracks · 8192-frame chunks | 302 | 320 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before (#438) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0 | 0.03 |
| 128 audio tracks · 64-sample buffer | 0.07 | 0.13 |
| 192 audio tracks · 64-sample buffer | 1.3 | 0.01 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before (#438) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 63.9 | 64.4 |
| 128 audio tracks · 64-sample buffer | 76.9 | 126 |
| 192 audio tracks · 64-sample buffer | 89.7 | 147 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-03T07:49:26Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/tmp/bench-450/libs/before
build: after=/tmp/bench-450/libs/after
```
</details>
