# Quickstart: Fade audio clips when they are split

1. Build on Linux: `CC=gcc CXX=g++ scripts/build-linux.sh` (the smoke test includes `--split-fades`).
2. Alone: `dotnet run --project tests/Nota.SmokeTest -c Release -- --split-fades`.
3. In the app: split an audio clip. The two pieces show short corner handles. Drag one. Undo once and the clip is whole.
4. Settings → Appearance → turn **Fade audio clips when splitting** off. The next split is a hard cut.
5. Select one side of a join and run **Fade at splits** from the palette or the clip menu. Both sides gain the short fade. There is no shortcut.
