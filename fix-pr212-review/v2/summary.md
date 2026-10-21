**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| Idle | 0 | 1,213 | 0 |
| 1 audio track | 3.2 | 1,196 | 2.8 |
| 8 audio tracks | 22.7 | 1,245 | 22.8 |
| 32 audio tracks | 32.1 | 1,244 | 31.6 |
| 8 synth tracks | 3.3 | 1,202 | 4.2 |
| 32 synth tracks | 14.5 | 1,236 | 13.1 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| 1 audio track | 5.9 | 4.7 | 4.4 |
| 8 audio tracks | 35.9 | 12.5 | 8.5 |
| 32 audio tracks | 144 | 22.7 | 22.4 |
| 8 synth tracks | 7.6 | 3.2 | 2.1 |
| 32 synth tracks | 30.0 | 6.5 | 6.8 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| 1 audio track | 5.9 | 62.9 | 4.4 |
| 8 audio tracks | 35.8 | 157 | 62.9 |
| 32 audio tracks | 144 | 272 | 202 |
| 8 synth tracks | 7.6 | 40.6 | 19.8 |
| 32 synth tracks | 29.9 | 84.0 | 67.3 |

**Late blocks at small buffers (heavy sessions)** — % of blocks late, median. Lower is better.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| 64 audio tracks · 128-sample buffer | 2.3 | 0 | 2.7 |
| 128 audio tracks · 64-sample buffer | 0.07 | 0.01 | 0.11 |
| 192 audio tracks · 64-sample buffer | 21.9 | 0.01 | 12.0 |

**CPU at small buffers (heavy sessions)** — % of one core, median. Lower is better.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| 64 audio tracks · 128-sample buffer | 58.6 | 1,205 | 59.0 |
| 128 audio tracks · 64-sample buffer | 76.1 | 1,208 | 78.5 |
| 192 audio tracks · 64-sample buffer | 98.2 | 1,238 | 199 |

<details><summary>Machine and builds</summary>

```
date: 2026-10-21T09:02:47Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: Before #212 = 82babfe (develop before the engine rebuild)
build: After #212 = 64250a5 (engine identical on develop 2d26c78)
build: This PR = 62f905a (fix/pr212-review)
```
</details>
