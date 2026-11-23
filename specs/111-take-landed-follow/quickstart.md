# Quickstart: validating #481

## Automated

```powershell
scripts/build-win.ps1
```

Runs the smoke test; `TakeLandedFollowTests` covers: values and refusals, the issue's example
(MIDI and audio), open take fires only after landing, stopped count-in / stopped while waiting /
global stop / empty take / overdub / Follow off never fire, retry fires once for the retried take,
Undo before the boundary cancels, Undo/Redo after never re-fires, scene row edits remap or turn
Off, save/reopen keeps rules and fires nothing. `Program.cs` checks the MCP tool and snapshot.

## Manual (app)

1. New project, 120 BPM, launch quantization 1 bar. Scenes: Verse (0), Chorus (1) with a clip on
   another track.
2. Arm an instrument track, select its empty Verse slot. Inspector → Take length 4 bars;
   When the take lands → Launch scene… Chorus, On → Global.
3. The empty slot shows the landing mark.
4. Record into the slot, play four bars. Chorus launches on the bar after the take lands — once.
5. Undo, record again but press Stop during the count-in (count-in 1 bar, from stop): no launch.
6. Save, reopen: the rule is still set, nothing launches on open.
