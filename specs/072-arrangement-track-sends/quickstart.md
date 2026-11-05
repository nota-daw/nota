# Quickstart: validate sends in the arrangement track headers

## Automated

```bash
scripts/build.sh                                            # full build + smoke test
dotnet run --project tests/Nota.SmokeTest -- --sends        # this feature's checks alone
```

Expected: `SENDS PASSED` — send undo/redo and no-change, MCP `set_send` one step, send
lane playback, record-write + undo, save/load, return delete remaps lanes, and the design
token checks in the full run.

## Manual (the app)

1. New project, add two return tracks (A Reverb, B Delay) and an instrument track; resize
   the instrument row to Large. The header shows knobs A and B under volume|pan.
2. Drag A to ~40 %: the Mixer window's A fader, the Session mixer and the Sends card show
   40 %. Undo → 0 %; Redo → 40 %.
3. Toggle **Sends** in the header column's top strip off → the knob row disappears; the
   large header is back to its usual layout. On again. Set the row to Standard (64) → no
   sends; drag it to ~90 px → sends appear.
4. Automation mode → target menu → "Send A · Reverb"; draw a ramp; play → knob A turns.
5. Record on, play, move knob B → a "Send B · Delay" lane appears with the movement; one
   Undo removes the movement and the lane.
6. Save, reopen → lanes, levels and the toggle state are back.
7. Delete return A → lane B now reads "Send A · Delay" and still drives the delay; Undo
   restores the reverb return, its lane and the send levels.
8. Narrow the header column to its minimum with four returns → knobs only, tooltips name
   return and level.
