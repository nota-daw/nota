**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | develop 89bc635 | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 3.0 | 2.9 |
| 8 audio tracks | 19.5 | 16.7 |
| 32 audio tracks | 29.9 | 27.6 |
| 8 synth tracks | 3.5 | 3.9 |
| 32 synth tracks | 11.8 | 11.1 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | develop 89bc635 | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.3 |
| 8 audio tracks | 6.0 | 7.7 |
| 32 audio tracks | 21.9 | 21.3 |
| 8 synth tracks | 2.0 | 1.9 |
| 32 synth tracks | 6.7 | 6.8 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | develop 89bc635 | This PR |
|---|---:|---:|
| 1 audio track | 5.7 | 4.3 |
| 8 audio tracks | 48.4 | 60.1 |
| 32 audio tracks | 206 | 203 |
| 8 synth tracks | 16.8 | 18.5 |
| 32 synth tracks | 64.9 | 63.0 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | develop 89bc635 | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 14.2 | 13.3 |
| 16 synth tracks · 8192-frame chunks | 7.2 | 9.9 |
| 64 audio tracks · 8192-frame chunks | 39.2 | 38.3 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | develop 89bc635 | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 95.0 | 92.4 |
| 16 synth tracks · 8192-frame chunks | 32.8 | 38.7 |
| 64 audio tracks · 8192-frame chunks | 332 | 324 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | develop 89bc635 | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.11 | 0.21 |
| 128 audio tracks · 64-sample buffer | 0.17 | 0.05 |
| 192 audio tracks · 64-sample buffer | 1.6 | 0.84 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | develop 89bc635 | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 59.8 | 61.1 |
| 128 audio tracks · 64-sample buffer | 74.9 | 76.5 |
| 192 audio tracks · 64-sample buffer | 109 | 117 |

<details><summary>Machine and builds</summary>

```
date: 2026-10-29T06:44:51Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 5
build: before=/tmp/bench-libs/before
build: after=/tmp/bench-libs/after
```
</details>
