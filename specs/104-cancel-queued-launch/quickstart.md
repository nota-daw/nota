# Quickstart: validate cancel of a queued launch

## Automated

```bash
scripts/build.sh                              # full gate, includes the new tests
dotnet run --project tests/Nota.SmokeTest -c Release -- --cancel-launch   # this feature's engine tests alone
(cd src/web/remote && npm test)               # hold logic of use:tap
```

`CancelLaunchTests` (smoke) proves, offline-rendered:

1. Slot: A plays, B queued on the same track, cancel → after the boundary A still plays at an
   unbroken position, B state 1; launch B again → it starts at the next boundary.
2. Scene (the issue's example): drum plays in scene 0; scene 1 = bass + empty drum slot; launch
   scene 1, cancel → drum plays, bass silent; the scene's follow never fires over 16 bars; its tempo
   never applies.
3. Boundary race: cancel pushed in the block holding the boundary → every track withdrawn; pushed
   one block later → every track started (never mixed).
4. Newer launch survives: scene 1 queued, then a direct launch on one of its tracks, scene cancel →
   that direct launch still starts.
5. Last slot cancelled by hand → the scene's follow / tempo dropped.
6. Stop All with a queued scene → its tempo change does not apply.
7. Follow-launched scene cancelled → scene follow ends, outgoing scene plays on.
8. Return codes: cancel with nothing queued → NOTA_ERR_UNKNOWN.

## By hand (desktop)

1. Two tracks, a 4-bar drum loop in scene 1, a bass clip in scene 2; launch quantum 1 bar.
2. Launch scene 1. Launch scene 2 — its button and the bass slot blink.
3. Alt+click scene 2's launch button before the bar ends → blinking stops, drum plays on.
4. Launch the bass slot, Alt+click it → it stops blinking; right-click a queued slot / scene →
   "Cancel launch" is there, and is absent once the clip plays.
5. Hover a queued launch button → the tooltip names Alt+click.

## By hand (Nota Remote)

Open the phone page, queue a clip, press and hold it → it stops blinking on the phone and on the
desktop; the release does not re-launch it. Same for a scene button.
