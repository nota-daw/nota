# Quickstart: validating ASIO on Windows

## Automated (every platform, CI included)

```bash
scripts/build.sh            # macOS; Linux: scripts/build-linux.sh
```

```powershell
pwsh scripts/build-win.ps1  # Windows x64: also proves the SDK fetch + ASIO TU compile
```

Expected: smoke test green, including the line
`ASIO self-test (fake driver): formats, pair routing, duplex, reset, fallback, latency`.
No ASIO driver is needed. See [contracts/c-abi.md](contracts/c-abi.md) for what
`nota_asio_selftest` covers.

## Manual (Windows machine)

Prerequisites: a hardware interface with its ASIO driver, **or** FlexASIO
(github.com/dechamps/FlexASIO) installed; a loopback cable (out 1/2 → in 1/2) for the
recording check.

| # | Step | Expected |
|---|---|---|
| 1 | Preferences → Audio → Audio system = ASIO, pick the driver | Driver, Output, Input rows appear; Latency shows the driver's buffer and rate (US1) |
| 2 | Play test tone | Heard on the chosen output pair only; other outputs silent (FR-006) |
| 3 | Quit, relaunch | ASIO + same driver + same pairs reopen (US1-3) |
| 4 | Driver settings… → change buffer while playing | Playback resumes ≤ 2 s; Latency updates (US3, SC-004) — repeat 10× |
| 5 | Sample rate row → a rate the driver lacks | Row reverts, caption explains (US3-3) |
| 6 | Arm an audio track, record a click through the loopback for 10 min | Recorded clicks align with the source within 1 sample (SC-006) |
| 7 | Quit, unplug the interface (or open the driver in another DAW), launch | Starts on WASAPI ≤ 5 s, status names the driver and reason; Preferences still shows ASIO (US4, SC-005) |
| 8 | Unplug while playing | Falls back to WASAPI ≤ 3 s, transport position kept (US4-2) |
| 9 | Switch back to WASAPI | Another app can now open the ASIO driver (FR-012) |
| 10 | 16-track project, 64 frames @ 48 kHz, 10 min | 0 xruns on the reference machine (SC-001) |

Without a Windows machine, steps 1–10 are pending; record that in the PR.
