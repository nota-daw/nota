# Quickstart: verify the track inspector

1. `scripts/build-linux.sh` (or `build.sh` / `build-win.ps1`) → smoke test passes.
2. Run the app, add an audio track and an instrument track. Click the last icon on the
   browser rail (Track inspector): it shows the selected track.
3. Audio track: rename it (Enter), set Record input to Ext 1, turn Monitor input on, pick a
   colour and Large height, press Freeze track. The header follows each change; ⌘Z undoes the
   colour as it would from the menu.
4. Instrument track: MIDI from lists the other instrument tracks.
5. Mute the shown track from its header, then click another track: the inspector shows the
   mute at once, then switches to the new track.
6. Select a return, then the master: the inspector shows their reduced sections. Delete the
   shown track: "No track selected".
