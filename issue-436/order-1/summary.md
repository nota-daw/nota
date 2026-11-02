**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | develop | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 2.1 | 1.5 |
| 8 audio tracks | 16.0 | 18.7 |
| 32 audio tracks | 29.6 | 28.8 |
| 8 synth tracks | 3.5 | 3.7 |
| 32 synth tracks | 13.3 | 12.9 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | develop | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.7 |
| 8 audio tracks | 8.1 | 8.3 |
| 32 audio tracks | 22.0 | 22.3 |
| 8 synth tracks | 2.5 | 2.3 |
| 32 synth tracks | 7.1 | 7.2 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | develop | This PR |
|---|---:|---:|
| 1 audio track | 5.6 | 4.7 |
| 8 audio tracks | 62.2 | 62.5 |
| 32 audio tracks | 206 | 209 |
| 8 synth tracks | 21.9 | 21.1 |
| 32 synth tracks | 69.1 | 68.3 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | develop | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 13.9 | 14.0 |
| 16 synth tracks · 8192-frame chunks | 5.0 | 9.0 |
| 64 audio tracks · 8192-frame chunks | 39.0 | 39.8 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | develop | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 95.3 | 96.4 |
| 16 synth tracks · 8192-frame chunks | 25.7 | 33.4 |
| 64 audio tracks · 8192-frame chunks | 327 | 332 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | develop | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 5.5 | 5.4 |
| 128 audio tracks · 64-sample buffer | 0.01 | 0.47 |
| 192 audio tracks · 64-sample buffer | 0.53 | 1.1 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | develop | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 50.9 | 52.1 |
| 128 audio tracks · 64-sample buffer | 77.6 | 78.1 |
| 192 audio tracks · 64-sample buffer | 138 | 134 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-02T17:05:36Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/tmp/bench-436/libs/before
build: after=/tmp/bench-436/libs/after
```
</details>
