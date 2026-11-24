# Quickstart: validating the next-take length per slot

## Automated (the PR gate)

```bash
scripts/build.sh
```

Runs the smoke test; the `SlotTakeLengthTests` block must print all checks OK (see
[research.md § R11](research.md) for the list). The retry check reports "waiting for #678"
until that bug is fixed.

## By hand in the app (120 BPM, 4/4, launch quantization 1 bar)

1. Session view, two instrument tracks armed, toolbar **Fixed = Off**.
2. Select an empty slot A on track 1 → inspector **Take length** → **3 bars**. The grid cell
   shows "3 bars".
3. Select an empty slot B on track 2 → **8 bars**.
4. Play, record into A: recording starts on the next bar, stops after 3 bars, the clip loops;
   clip length reads 3.0.0. Toolbar Fixed still Off; B still shows "8 bars"; tempo unchanged.
5. Toolbar Fixed = 2 bars; set a third empty slot to **Open**; record into it: it runs until
   stopped. A Global slot records 2 bars and its inspector reads "Global (2 bars)".
6. Record into B and cancel before the bar (stop the pending take): B empty, still "8 bars".
7. Undo the take in A: A empty, "3 bars" again. Redo: the clip is back.
8. Delete A's clip: A shows "3 bars" again.
9. Change B's value, press Undo: it goes back.
10. Save, close, reopen: every value is as it was.
11. Time signature 7/8, slot prepared 3 bars: the take is 21 beats (3 bars of 7/8).
