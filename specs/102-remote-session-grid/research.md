# Research: Nota Remote — Session grid

## Pinning the launch and Stop strips

- **Decision**: `position: sticky` on the header / footer / rail cells of the single CSS grid
  inside the one scroll area.
- **Rationale**: one scroller keeps both axes in step for free (no scroll syncing between four
  panes), native momentum scrolling on iOS stays intact, and sticky works on grid items in
  Safari 15+ and Chrome.
- **Alternatives**: separate header / rail scrollers synced in JS (jitter on iOS momentum scroll,
  more code); a transform-based virtual grid (overkill for ≤ a few hundred cells).

## Group launch and stop

- **Decision**: the hub expands a group id in `launch` / `stopSlot` into its members.
- **Rationale**: one message → all members launched in the same hub tick, as the desktop does;
  the phone needn't know how deep groups nest to act correctly.
- **Alternatives**: the phone sends one `launch` per member (a burst of messages; members could
  straddle a launch-quantum boundary on a slow link).

## Group membership

- **Decision**: members = every non-group, non-return track whose parent chain reaches the group
  (the desktop's `SessionView.Under`), sent as `k`.
- **Rationale**: matches the desktop's "n of m" and launch set exactly.

## Folding

- **Decision**: reuse the Mixer's `prefs.groupsOpen` (groups start folded).
- **Rationale**: the issue asks for groups "as on the desktop"; one fold state per phone avoids
  two lists that drift apart.
- **Alternatives**: a separate Scenes fold set — rejected as surprising.
