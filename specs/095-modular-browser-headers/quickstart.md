# Quickstart: validating 095

## Prerequisites

- `scripts/build.sh` green (Release engine in `build/`, smoke test included).
- A project with: an instrument track, an audio track, a group holding both with an Auto Filter
  on it, a return with a reverb.

## Q1 — Browser tab (US1, US4)

1. Arrangement view: the browser rail has **no** Modular tab.
2. Switch to Modular: the Modular tab appears after MIDI effects with the patch-cable icon and is
   selected (first time). Seven rows, each with category and description.
3. Search "wobble" → LFO; "sidechain" → Envelope Follower; "monitor" → Scope.
4. Drag Envelope Follower onto empty canvas → the node lands at the drop point, unconnected;
   ⌘Z removes it in one step. Double-click LFO → it lands mid-view.
5. Drag a node onto the header column / device panel → refused.
6. Switch to Global view, double-click a node → nothing added, status explains.
7. Pick Audio effects, go to Arrangement (browser shows the tab it had there), come back → Audio
   effects is shown.
8. The Modular toolbar has no **+ Add**; ⌘⇧P in Modular still lists Modular nodes.

## Q2 — Headers (US2)

1. Modular Track view: the column shows the same cards as the arrangement (groups as trays,
   returns and master pinned below), in the current look; switch look in Preferences → both
   views follow.
2. Mute, solo, arm, rename, recolour, change volume/pan, reorder by drag, fold the group — each
   shows in the arrangement and mixer.
3. Play: meters move.
4. Click the audio track's header → its graph shows, the track is selected app-wide.
5. View → Track headers on the right: the column jumps right at once; toggle back.
6. Global view hides the column; Track view brings it back.
7. Drag an LFO's CV out onto another track's header → the cross-track parameter menu opens;
   the column's divider is brass while dragging.
8. Back in Arrangement: overview, sections, ruler, lanes, scrollbar all there, unchanged;
   ⌘⇧C (Copy to Session) and arrow-nudging clips work there and do nothing in Modular.

## Q3 — Show in Modular (US5)

From the arrangement on the instrument track, the group and the return, and from the session
on the audio track: **Show in Modular** opens Modular on that track. The master's menus have no
item. In Modular the item is disabled on the shown track.

## Q4 — Group graph (US3)

Click the group's header in Modular: Tracks in → Auto Filter → Out. Add an LFO from the browser,
patch it to the filter cutoff, play: the cutoff moves. Save, reopen: patch and node positions
kept. Clicking the master row leaves the graph as it was.

## Q5 — Automated

`scripts/build.sh` → smoke test passes incl. `ModularBrowserTests`, `DesignTokenCheck`,
localisation check.
