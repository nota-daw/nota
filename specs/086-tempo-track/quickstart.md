# Validation guide

Run `pwsh scripts/build-win.ps1` from a VS developer environment. Targeted checks:
`dotnet run --project tests/Nota.SmokeTest -c Release -- --tempo`.
Launch `dotnet run --project src/managed/Nota.App -c Release`.

Open the tempo lane, add/move/edit points and choose each shape. Drag both curve
handles. Right drag a marquee, Delete, undo once and redo; initial point remains.
Repeat after zoom/scroll. Escape and empty clicks clear selection.

Import unwarped reference audio. Ctrl hover shows the time-scale indicator;
release/exit hides it. Ctrl drag three successive bars onto the performance.
Verify preview, markers and fixed earlier anchors/audio. Repeat with Alt to omit
the ending marker. Escape cancels. Check marker and available transient snaps.

Save/reload; compare all timing data. Seek between beats and play with metronome:
no immediate extra click. Loop across a tempo change; MIDI, audio and clicks align.
