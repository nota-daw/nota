# Plugin scan timing — develop (f585070) vs fix/plugin-scan-hang-windows

Machine: Apple M4 Max, macOS 26. Rider, a Debug Nota.App and GitKraken were running
(load average 3.5–7). Each run: a fresh `NOTA_DATA_DIR` with `scanpaths.txt` holding
300 empty `.vst3` bundles, then `Nota.SmokeTest --scan <worker>`. That is a full first
scan of 57 AU + 14 real VST3 + 300 fakes = 371 candidates. Wall time includes the
smoke binary's ~5 s start-up checks.

## When Kontakt 8 loads in ~3 s (first session)

| | develop | this branch |
|---|---:|---:|
| Full first scan, s | 46.4 | **13.3** |
| Plugins found | 72 | 72 |

## When Kontakt 8 takes ~32 s to load (later the same day, 3 interleaved runs)

| | develop | this branch |
|---|---:|---:|
| Full first scan, s (runs) | 104.9 / 102.6 / 102.1 | 99.4 / 100.1 / 101.6 |
| Plugins found | **70** (both Kontakt 8 lost) | **72** |

Kontakt dominates the second set: the branch gives it 30 s in the parallel pass and
then up to 2 min alone, so it is found. develop loses it. Raw numbers: `scan-times.csv`.
