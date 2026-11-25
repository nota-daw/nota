**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | develop (5989d60a) | This PR (#1050) |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 6.1 | 5.5 |
| 8 audio tracks | 32.0 | 31.9 |
| 32 audio tracks | 40.1 | 39.9 |
| 8 synth tracks | 7.3 | 7.9 |
| 32 synth tracks | 23.1 | 22.9 |
| 8 sidechained tracks | 33.7 | 34.3 |
| 32 sidechained tracks | 41.8 | 41.9 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | develop (5989d60a) | This PR (#1050) |
|---|---:|---:|
| 1 audio track | 8.3 | 7.1 |
| 8 audio tracks | 16.1 | 15.8 |
| 32 audio tracks | 62.0 | 61.6 |
| 8 synth tracks | 3.6 | 3.5 |
| 32 synth tracks | 14.0 | 14.0 |
| 8 sidechained tracks | 26.1 | 26.0 |
| 32 sidechained tracks | 77.2 | 76.3 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | develop (5989d60a) | This PR (#1050) |
|---|---:|---:|
| 1 audio track | 8.3 | 7.1 |
| 8 audio tracks | 63.6 | 62.8 |
| 32 audio tracks | 247 | 246 |
| 8 synth tracks | 13.6 | 13.5 |
| 32 synth tracks | 55.1 | 55.1 |
| 8 sidechained tracks | 71.7 | 71.5 |
| 32 sidechained tracks | 271 | 268 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | develop (5989d60a) | This PR (#1050) |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 31.3 | 31.1 |
| 16 synth tracks · 8192-frame chunks | 9.1 | 12.7 |
| 64 audio tracks · 8192-frame chunks | 124 | 123 |
| 16 sidechained tracks · 8192-frame chunks | 41.3 | 41.3 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | develop (5989d60a) | This PR (#1050) |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 120 | 119 |
| 16 synth tracks · 8192-frame chunks | 29.8 | 41.6 |
| 64 audio tracks · 8192-frame chunks | 476 | 475 |
| 16 sidechained tracks · 8192-frame chunks | 133 | 134 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | develop (5989d60a) | This PR (#1050) |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.43 | 0.4 |
| 128 audio tracks · 64-sample buffer | 1.5 | 0.67 |
| 192 audio tracks · 64-sample buffer | 1.8 | 0.65 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | develop (5989d60a) | This PR (#1050) |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 87.6 | 89.8 |
| 128 audio tracks · 64-sample buffer | 184 | 212 |
| 192 audio tracks · 64-sample buffer | 270 | 256 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-25T18:31:16Z
os: Darwin 27.2.0 arm64
cpu: Apple M2, 8 threads
macos: 27.2.1
runs: 3
build: before=/tmp/bench-1050/before
build: after=/tmp/bench-1050/after
```
</details>
