# Quickstart: verify bank automation

1. `scripts/build.sh` — the smoke test covers params, lane timing, Pattern mode, RTH3 load and
   the Twin pattern lane.
2. In the app: add a Nota Rhythm track, copy bank A to B and add a fill to B.
3. Arrangement → the track's automation menu → **Perform › Bank**. Draw A, then B for bar 8,
   then A from bar 9 (snap to bars).
4. Play from bar 7: bar 8 plays the fill from its first step; the card's B button lights up
   during bar 8 and the step strip shows B.
5. Export bars 7–9 and compare with playback — same hits.
6. Right-click a bank → *Switch banks at the pattern start*, stop, play, click C mid-bar: C is tinted, then takes over on
   the next bar.
7. Open a project saved with an older build on bank C: the card shows C and plays C.
