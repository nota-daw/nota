# Quickstart: verify the clip Follow bypass

1. `scripts/build.sh` — the smoke test's Session block covers: bypass holds a clip past its
   follow time while another track follows; re-enable past the deadline fires at the next
   multiple (not at once); re-enable before the deadline fires on time; props round-trip,
   copy-slot carries it, undo restores it; save/load keeps it.
2. In the app: Session view, a 1-bar clip with Follow A = Next, After = 2 bars, launch it.
   Click the Follow switch in the inspector off → "Bypassed", section greyed, Bypass mark on
   the slot; the clip loops on. Switch it back on mid-bar after several bars → the next clip
   starts on the next 2-bar multiple since launch, not immediately.
3. Right-click the slot → "Restore follow actions" / "Bypass follow actions" toggles it.
4. Save, reopen → the clip is still bypassed with the same settings.
