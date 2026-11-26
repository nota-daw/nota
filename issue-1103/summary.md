# Engine benchmark — spec 115 (MIDI routing)

Builds: before = 1ad9149b (develop + the spec, no code), after = 3e9c2de8 (feat/midi-routing).
Machine: AMD Ryzen 7 5700G, Windows 10 Pro 19045, MSVC 14.44, Release. 3 runs, builds interleaved run by run.
Harness: scripts/bench/engine-bench.cpp built with MSVC against each nota_engine.dll (the script
itself is POSIX-only: a copy with GetProcessTimes for CPU and GetProcAddress in place of dlsym).
Medians of 3; CPU is process CPU time per second of audio (Windows counts it in ~15.6 ms ticks,
so small rows move in steps of that size). Late blocks: real-time blocks that missed their deadline, summed over the runs.

| Scenario | CPU ms/s before | CPU ms/s after | Change | Wall ms/s before | Wall ms/s after | Late blocks before / after (3 runs) |
|---|---:|---:|---:|---:|---:|---:|
| audio 1 offline 512 | 21.9 | 21.9 | +0.0 % | 21.4 | 21.5 | 0 / 0 |
| audio 8 offline 512 | 176.6 | 173.4 | -1.8 % | 46.9 | 45.1 | 0 / 0 |
| audio 32 offline 512 | 704.7 | 700.0 | -0.7 % | 179.6 | 180.3 | 0 / 0 |
| inst 8 offline 512 | 42.2 | 43.8 | +3.8 % | 11.6 | 11.8 | 0 / 0 |
| inst 32 offline 512 | 142.2 | 140.6 | -1.1 % | 38.4 | 38.7 | 0 / 0 |
| audio 16 export 8192 | 325.0 | 287.5 | -11.5 % | 89.6 | 86.2 | 0 / 0 |
| inst 16 export 8192 | 21.9 | 31.2 | +42.5 % | 16.0 | 16.0 | 0 / 0 |
| audio 64 export 8192 | 1276.6 | 1307.8 | +2.4 % | 343.6 | 345.3 | 0 / 0 |
| audio 1 rt 512 | 0.0 | 0.0 | — | 1000.6 | 1000.6 | 0 / 0 |
| audio 8 rt 512 | 123.4 | 115.6 | -6.3 % | 1000.6 | 1000.6 | 3 / 0 |
| audio 32 rt 512 | 671.9 | 656.2 | -2.3 % | 1000.6 | 1000.6 | 12 / 14 |
| inst 8 rt 512 | 0.0 | 0.0 | — | 1000.5 | 1000.6 | 0 / 0 |
| inst 32 rt 512 | 75.0 | 78.1 | +4.1 % | 1000.6 | 1000.6 | 0 / 0 |
| audio 64 rt 128 | 1535.9 | 1442.2 | -6.1 % | 999.8 | 999.8 | 64 / 54 |
| audio 128 rt 64 | 4082.8 | 4073.4 | -0.2 % | 1035.9 | 1031.2 | 10960 / 10151 |
| audio 192 rt 64 | 6378.1 | 6318.8 | -0.9 % | 1631.8 | 1616.6 | 22495 / 22498 |
| keyed 8 offline 512 | 193.8 | 201.6 | +4.0 % | 74.3 | 75.8 | 0 / 0 |
| keyed 32 offline 512 | 789.1 | 762.5 | -3.4 % | 221.9 | 217.1 | 0 / 0 |
| keyed 16 export 8192 | 348.4 | 356.2 | +2.2 % | 109.1 | 111.5 | 0 / 0 |
| keyed 8 rt 512 | 156.2 | 170.3 | +9.0 % | 1000.7 | 1000.6 | 0 / 0 |
| keyed 32 rt 512 | 762.5 | 740.6 | -2.9 % | 1000.6 | 1000.5 | 54 / 19 |
