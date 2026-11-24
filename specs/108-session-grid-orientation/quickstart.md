# Quickstart: verifying spec 108 (Session grid orientation)

## Prerequisites

- Worktree `~/repos/nota/nota-968`, engine built in `build/` (Release).
- For remotes: the mock (`node src/web/remote/mock/nota-mock.mjs`, :7790, code 1234) or the real
  app with Remote on; Nota NX desktop build in `../nota-nx` (`make -f Makefile.desktop`).

## 1. Build and smoke

```bash
scripts/build.sh
```

Expected: green. The Remote section checks `ses` gains `ax:"across"` when the host's
`SessionAcross` is true and loses it when false; the `GridNav` check maps arrows to scene / track
steps per orientation and the `session.select*` ids to the same steps in both.

## 2. Desktop, by hand or via the headless harness

1. Project with a group of two tracks, one plain track, a return, 6 scenes with clips.
2. Preferences → Editing → Session → **Scenes across**. Expected: the open Session view turns at
   once — scene headers across the top, track rows with headers at the left, Stop + mixer
   blocks at the right, Stop All above them; every clip in its scene / track.
3. Launch a clip, a scene, stop a track, Stop All; drag a clip to another scene (Alt copies);
   drag a scene sideways to reorder; fold the group; drop a Browser instrument on a track
   header. Expected: same results as in Scenes down.
4. Arrow keys: → next scene, ↓ next track, ↑ off the first track selects the scene, ↓ past the
   last track reaches the return.
5. Toolbar: hide / show I/O, Sends, Mixer — the lane height follows.
6. Toolbar turn button → back to Scenes down; restart Nota → the last choice is kept.
7. Controller (KeyLab DAW mode or a shortcut bound to "Select the next scene"): steps by scene in
   both orientations.

## 3. Nota Remote

1. Fresh phone (or one with old `sessionAxis: 'down'`): ⋯ → Session grid shows **As in Nota**
   selected. Turn the desktop: the phone's Scenes grid turns within a tick.
2. Pick **Scenes down** on the phone: it stays down when the desktop turns.
3. `npm test` in `src/web/remote` — migration and effective-axis cases pass.

## 4. Nota NX

1. `make -f Makefile.desktop && make -f Makefile.desktop test` in `../nota-nx`.
2. `NX_SCRIPT` shots of Scenes with the desktop (or mock) in each orientation and with each
   NX setting; check the pinned strips and Stop All.
3. Switch build: `DEVKITPRO=/opt/devkitpro … make` → `nota-nx.nro` with nacp + romfs in ASET.
