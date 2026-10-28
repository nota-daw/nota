# Quickstart: checking Fold by hand

1. Add a Nota Volt track and a MIDI clip. In the piano roll, turn **Scale** on: A, Minor.
2. Press **Fold**. Only A B C D E F G rows remain. Draw notes; drag one up a row (E→F, F→G).
3. Draw an F♯ with Fold off, then fold again. The F♯ row stays, marked out of scale.
4. Press ↑ with notes selected: each note moves one row. Press ⇧↑: up an octave.
5. Add a Drum Rack kit (Kompakt). Open its clip and press **Fold**. There's one row per pad,
   named Kick, Snare and so on. Click a key: the pad sounds.
6. Rename a pad in the rack. The row label changes.
7. Open another clip on the drum track; it opens folded. Save, reopen: still folded. The Volt
   track opens as you left it.
8. Smoke: `CI=true dotnet tests/Nota.SmokeTest/bin/Release/net10.0/Nota.SmokeTest.dll --fold`.
