**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 3.4 | 3.3 |
| 8 audio tracks | 21.2 | 21.6 |
| 32 audio tracks | 34.0 | 34.0 |
| 8 synth tracks | 3.5 | 3.6 |
| 32 synth tracks | 12.0 | 13.3 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 4.3 | 5.6 |
| 8 audio tracks | 5.7 | 5.8 |
| 32 audio tracks | 21.1 | 21.1 |
| 8 synth tracks | 2.0 | 1.9 |
| 32 synth tracks | 6.7 | 6.6 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 1 audio track | 4.3 | 5.6 |
| 8 audio tracks | 46.5 | 44.9 |
| 32 audio tracks | 196 | 197 |
| 8 synth tracks | 15.3 | 16.2 |
| 32 synth tracks | 60.7 | 60.6 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 14.3 | 12.8 |
| 16 synth tracks · 8192-frame chunks | 11.9 | 7.7 |
| 64 audio tracks · 8192-frame chunks | 36.3 | 36.3 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before (develop) | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 103 | 93.4 |
| 16 synth tracks · 8192-frame chunks | 40.1 | 33.5 |
| 64 audio tracks · 8192-frame chunks | 303 | 303 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.45 | 0.13 |
| 128 audio tracks · 64-sample buffer | 0.13 | 0.04 |
| 192 audio tracks · 64-sample buffer | 3.3 | 6.0 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before (develop) | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 68.3 | 70.2 |
| 128 audio tracks · 64-sample buffer | 79.2 | 79.6 |
| 192 audio tracks · 64-sample buffer | 116 | 241 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-11T07:17:46Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: after=/tmp/bench089/libs/after
build: before=/tmp/bench089/libs/before
```
</details>
