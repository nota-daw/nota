**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | develop | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 3.3 | 3.5 |
| 8 audio tracks | 21.5 | 21.4 |
| 32 audio tracks | 30.7 | 30.6 |
| 8 synth tracks | 3.8 | 4.0 |
| 32 synth tracks | 13.8 | 14.0 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | develop | This PR |
|---|---:|---:|
| 1 audio track | 4.4 | 5.6 |
| 8 audio tracks | 8.1 | 8.3 |
| 32 audio tracks | 22.4 | 22.3 |
| 8 synth tracks | 2.3 | 2.3 |
| 32 synth tracks | 7.1 | 7.1 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | develop | This PR |
|---|---:|---:|
| 1 audio track | 4.4 | 5.6 |
| 8 audio tracks | 61.8 | 62.8 |
| 32 audio tracks | 210 | 210 |
| 8 synth tracks | 21.1 | 21.2 |
| 32 synth tracks | 68.6 | 68.2 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | develop | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 14.6 | 14.1 |
| 16 synth tracks · 8192-frame chunks | 10.9 | 6.7 |
| 64 audio tracks · 8192-frame chunks | 39.2 | 39.2 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | develop | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 100 | 97.0 |
| 16 synth tracks · 8192-frame chunks | 35.1 | 28.3 |
| 64 audio tracks · 8192-frame chunks | 330 | 330 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | develop | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 5.7 | 5.5 |
| 128 audio tracks · 64-sample buffer | 0.07 | 0.04 |
| 192 audio tracks · 64-sample buffer | 9.4 | 1.2 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | develop | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 51.6 | 51.6 |
| 128 audio tracks · 64-sample buffer | 76.9 | 77.6 |
| 192 audio tracks · 64-sample buffer | 119 | 137 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-02T17:15:01Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: after=/tmp/bench-436/libs/after
build: before=/tmp/bench-436/libs/before
```
</details>
