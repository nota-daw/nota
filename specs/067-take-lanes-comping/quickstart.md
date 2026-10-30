# Quickstart: Take lanes and comping

1. New project, add an audio track, arm it, pick an input.
2. Turn the loop on over 1 bar (drag in the ruler) and press Record.
3. Play/sing three passes, then Stop. The track plays the third pass; its header shows a takes
   chevron with **3**.
4. Click the chevron: lanes **Take 1**, **Take 2**, **Take 3** open under the track. Take 3 is
   highlighted over the whole bar.
5. Drag across beats 1–2 on Take 1. Playback now plays Take 1 there; Take 1's lane highlights
   that section and Take 3's dims it. Undo restores Take 3 there.
6. Right-click a lane: **Delete take** (what plays doesn't change), **Flatten takes** (the comp
   becomes one clip, the lanes go away).
7. Save, close, reopen: the same takes and comp.

Automated: `dotnet run --project tests/Nota.SmokeTest -c Release -- --takes`.
