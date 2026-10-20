**CPU while idle and while playing** — % of one core, median. Playing = blocks paced like a 512-sample buffer at 48 kHz. Lower is better.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| Idle | 0 | 1,235 | 0 |
| 1 audio track | 2.8 | 1,226 | 2.3 |
| 8 audio tracks | 16.0 | 1,208 | 26.0 |
| 32 audio tracks | 32.0 | 1,246 | 74.2 |
| 8 synth tracks | 3.6 | 1,253 | 6.2 |
| 32 synth tracks | 9.8 | 1,241 | 14.1 |

**Offline render (bounce) time** — ms per second of audio, median. Lower is faster.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| 1 audio track | 5.9 | 4.7 | 4.4 |
| 8 audio tracks | 35.2 | 13.9 | 6.8 |
| 32 audio tracks | 143 | 21.3 | 20.8 |
| 8 synth tracks | 7.4 | 3.1 | 3.4 |
| 32 synth tracks | 29.7 | 6.3 | 6.6 |

**CPU work during an offline render** — CPU ms per second of audio, median. Lower uses less energy.

| | Before #212 | After #212 | This PR |
|---|---:|---:|---:|
| 1 audio track | 5.9 | 60.9 | 4.4 |
| 8 audio tracks | 35.2 | 186 | 62.4 |
| 32 audio tracks | 143 | 272 | 260 |
| 8 synth tracks | 7.4 | 41.3 | 34.3 |
| 32 synth tracks | 29.7 | 83.9 | 81.1 |

<details><summary>Machine and builds</summary>

```
date: 2026-10-20T20:41:03Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 3
build: Before #212 = 82babfe (develop before the engine rebuild)
build: After #212 = 64250a5 (engine identical on develop 2d26c78)
build: This PR = e04af2d (fix/pr212-review)
```
</details>
