**Export and freeze render time** — ms per second of audio, median. Lower is faster.

| | Develop | PR a93d1add |
|---|---:|---:|
| 16 synth tracks · 8192-frame chunks | 7.2 | 7.6 |

**CPU work during an export** — CPU ms per second of audio, median. Lower uses less energy.

| | Develop | PR a93d1add |
|---|---:|---:|
| 16 synth tracks · 8192-frame chunks | 29.4 | 30.9 |

<details><summary>Machine and builds</summary>

```
date: 2026-11-25T16:36:41Z
os: Darwin 25.6.0 arm64
cpu: Apple M4 Max, 14 threads
macos: 26.6.2
runs: 5
build: after=/Users/egorkhindikaynen/repos/nota/nota-bug-batch/src/native/nota.engine/build
build: before=/private/tmp/nota-bug-batch-before/src/native/nota.engine/build
```
</details>
