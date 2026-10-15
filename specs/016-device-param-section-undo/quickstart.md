# Quickstart: verify undo for device parameters and sections

## Automated

```bash
CI=true dotnet run --project tests/Nota.SmokeTest -c Release -- --paramundo
```

## By hand

1. New project → instrument track with Nota Bass. Draw a few notes, then open Devices and
   drag Cutoff from 400 Hz to 300 Hz. ⌘Z → Cutoff is 400 Hz again (the notes stay). ⌘Z again
   → the last note edit is undone. ⌘⇧Z twice → both come back (#30).
2. On an effect, double-click a knob (reset), flip a switch, type a value into a read-out:
   each is one ⌘Z.
3. View ▸ Toggle sections. Drag out a section, rename it, drag its right edge, move it,
   duplicate it, delete one. ⌘Z repeatedly → each edit comes off in reverse order; ⌘⇧Z
   replays them (#64).
4. Save, close, reopen → sections are there, and no ⌘Z removes them.
5. Open a project saved by v0.55.1 with sections → they appear; save → the bundle no longer
   holds `sections.json`.
