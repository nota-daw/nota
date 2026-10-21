**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before #232 (79e42a5) | develop (67a7fc8) | This PR |
|---|---:|---:|---:|
| Idle | 1,037 | 0 | 0 |
| 1 audio track | 1,136 | 2.2 | 2.3 |
| 8 audio tracks | 1,117 | 15.6 | 15.4 |
| 32 audio tracks | 1,116 | 29.6 | 30.9 |
| 8 synth tracks | 1,130 | 2.4 | 2.4 |
| 32 synth tracks | 1,127 | 8.2 | 8.6 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before #232 (79e42a5) | develop (67a7fc8) | This PR |
|---|---:|---:|---:|
| 1 audio track | 5.8 | 4.4 | 4.4 |
| 8 audio tracks | 11.0 | 6.0 | 8.5 |
| 32 audio tracks | 22.5 | 21.5 | 22.1 |
| 8 synth tracks | 2.8 | 2.1 | 2.1 |
| 32 synth tracks | 6.6 | 6.9 | 6.8 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before #232 (79e42a5) | develop (67a7fc8) | This PR |
|---|---:|---:|---:|
| 1 audio track | 67.8 | 4.4 | 4.4 |
| 8 audio tracks | 126 | 47.6 | 62.7 |
| 32 audio tracks | 252 | 197 | 207 |
| 8 synth tracks | 33.0 | 19.5 | 19.1 |
| 32 synth tracks | 77.9 | 63.7 | 66.2 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Before #232 (79e42a5) | develop (67a7fc8) | This PR |
|---|---:|---:|---:|
| 16 audio tracks · 8192-frame chunks | 12.5 | 69.8 | 14.1 |
| 16 synth tracks · 8192-frame chunks | 3.5 | 16.2 | 5.6 |
| 64 audio tracks · 8192-frame chunks | 38.8 | 282 | 40.2 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Before #232 (79e42a5) | develop (67a7fc8) | This PR |
|---|---:|---:|---:|
| 16 audio tracks · 8192-frame chunks | 309 | 69.8 | 96.2 |
| 16 synth tracks · 8192-frame chunks | 219 | 16.6 | 27.3 |
| 64 audio tracks · 8192-frame chunks | 610 | 282 | 335 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before #232 (79e42a5) | develop (67a7fc8) | This PR |
|---|---:|---:|---:|
| 64 audio tracks · 128-sample buffer | 0 | 0.19 | 0.29 |
| 128 audio tracks · 64-sample buffer | 0.03 | 0.11 | 0.03 |
| 192 audio tracks · 64-sample buffer | 0.12 | 6.0 | 1.6 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before #232 (79e42a5) | develop (67a7fc8) | This PR |
|---|---:|---:|---:|
| 64 audio tracks · 128-sample buffer | 1,127 | 59.9 | 62.2 |
| 128 audio tracks · 64-sample buffer | 1,124 | 76.4 | 77.5 |
| 192 audio tracks · 64-sample buffer | 1,128 | 225 | 131 |

<details><summary>Machine and builds</summary>

```
date: 2026-10-21T19:31:50Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: base=/tmp/bench/libs/base
build: dev=/tmp/bench/libs/dev
build: pr=/tmp/bench/libs/pr
```
</details>
