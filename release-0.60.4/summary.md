**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | 0.60.3 | This PR |
|---|---:|---:|
| Idle | 0 | 0 |
| 1 audio track | 2.6 | 2.4 |
| 8 audio tracks | 13.8 | 13.1 |
| 32 audio tracks | 26.4 | 25.8 |
| 8 synth tracks | 2.6 | 1.5 |
| 32 synth tracks | 7.7 | 7.8 |
| 8 sidechained tracks | 19.4 | 17.4 |
| 32 sidechained tracks | 29.6 | 31.6 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | 0.60.3 | This PR |
|---|---:|---:|
| 1 audio track | 5.6 | 4.3 |
| 8 audio tracks | 5.8 | 5.8 |
| 32 audio tracks | 21.5 | 21.0 |
| 8 synth tracks | 2.2 | 2.0 |
| 32 synth tracks | 6.8 | 6.7 |
| 8 sidechained tracks | 6.6 | 12.3 |
| 32 sidechained tracks | 23.7 | 28.6 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | 0.60.3 | This PR |
|---|---:|---:|
| 1 audio track | 5.5 | 4.3 |
| 8 audio tracks | 50.1 | 48.7 |
| 32 audio tracks | 201 | 197 |
| 8 synth tracks | 19.1 | 16.1 |
| 32 synth tracks | 63.6 | 64.6 |
| 8 sidechained tracks | 56.2 | 54.3 |
| 32 sidechained tracks | 220 | 183 |

**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | 0.60.3 | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 11.8 | 13.0 |
| 16 synth tracks · 8192-frame chunks | 3.9 | 6.8 |
| 64 audio tracks · 8192-frame chunks | 36.6 | 36.0 |
| 16 sidechained tracks · 8192-frame chunks | 13.6 | 18.3 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | 0.60.3 | This PR |
|---|---:|---:|
| 16 audio tracks · 8192-frame chunks | 86.4 | 83.2 |
| 16 synth tracks · 8192-frame chunks | 21.0 | 31.3 |
| 64 audio tracks · 8192-frame chunks | 307 | 303 |
| 16 sidechained tracks · 8192-frame chunks | 93.8 | 94.2 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | 0.60.3 | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 0.69 | 0.37 |
| 128 audio tracks · 64-sample buffer | 0.24 | 0.08 |
| 192 audio tracks · 64-sample buffer | 0.53 | 0.8 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | 0.60.3 | This PR |
|---|---:|---:|
| 64 audio tracks · 128-sample buffer | 40.1 | 43.0 |
| 128 audio tracks · 64-sample buffer | 78.1 | 79.2 |
| 192 audio tracks · 64-sample buffer | 133 | 156 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-19T06:56:19Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: before=/tmp/bench676/libs/before
build: after=/tmp/bench676/libs/after
```
</details>
