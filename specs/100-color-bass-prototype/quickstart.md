# Quickstart
Build with `scripts/build.sh`. Launch `dotnet run --project src/managed/Nota.App -c Release`. Search for Nota Color (prototype), drop it after a bass instrument or on an audio track. Start with C minor and full Mix. Use the note buttons for Custom. Use Mix=0 for a latency-aligned dry comparison. Save/load or duplicate to check recall.
Native DSP validation/listening generator: `scripts/test-color.sh`. Outputs WAV pairs and metrics under `build/color-prototype/`. Managed focused checks: `dotnet run --project tests/Nota.SmokeTest -c Release -- --color`.
Subjective listening acceptance remains pending the user's audition; no claim of exact Chroma matching.

Try Morph at 35% and Gate on at −60 dBFS as a starting point. Morph at 100% may attenuate shifted frequencies absent in dry audio. Both default off. Listening copies include attack and decay, and `*-natural-wet.wav` versions with these settings.
