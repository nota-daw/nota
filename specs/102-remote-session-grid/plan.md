# Implementation Plan: Nota Remote — Session grid: pinned launch and Stop controls, groups, and a choice of orientation

**Branch**: `feat/remote-session-grid` | **Date**: 2026-11-18 | **Spec**: [spec.md](spec.md) | **Issue**: nota-daw/nota#662

## Summary

The hub adds group tracks to the `ses` message, each with the ids of its members, and expands a
`launch` / `stopSlot` aimed at a group into its members — as the desktop's `LaunchGroup` /
`StopColumn` do. The phone's `Scenes.svelte` is rebuilt around one model of "lanes" (tracks and
groups, folded as the Mixer folds them) and "scenes", laid out either way by a new `sessionAxis`
preference; the strips that hold launch, Stop, Stop All and the track names are `position:
sticky` cells of the one CSS grid, so they stay put while the slots scroll under them.

## Technical Context

**Language/Version**: C# / .NET 10 (`src/managed/Nota.Remote`); TypeScript + Svelte 5 (`src/web/remote`).

**Primary Dependencies**: none new.

**Audio-Thread Impact**: none — the hub already calls `LaunchSlot` / `StopSlot` from its own
thread; a group call is a loop of those.

**Testing**: smoke test `RemoteTests.Session` gains a grouped pair: the group appears in `ses`
with `k` = its members, a group `launch` queues both members, a group `stopSlot` stops them.
Phone page: `npm run check`, `npm test`; a pure helper (`src/lib/grid.ts`: lanes, folding, group
summary) gets unit tests in `test/grid.test.ts`. Visual: the built page against
`mock/nota-mock.mjs` (with a group added) in headless Chrome, both orientations, scrolled to the
far corner — see [quickstart.md](quickstart.md).

## Design

- **Protocol** ([contracts/session-message.md](contracts/session-message.md)): `ses.tr[]` gains group
  entries `{ id, k: [memberIds], c: [] }` in project order (returns still left out). `launch {id
  , i}` and `stopSlot {id}` accept a group id. An older page ignores `k` and shows the group as a
  column of empty cells; an older Nota sends no groups and the page simply has none.
- **Hub**: `BuildSession` keeps groups, writes `k` from a `Members(groupId)` helper (every
  non-group, non-return track whose parent chain reaches the group — the desktop's `Under`) and an
  empty `c`. `Apply`: `launch` / `stopSlot` on a group loop over `Members`.
- **Lanes** (`grid.ts`, pure): from `proj.tracks` + `ses.tr` + the open-group set build the
  visible lanes `{ id, kind: 'track' | 'group', depth, open, members }` — a lane is hidden when any
  ancestor group is folded (the Mixer's rule, shared `prefs.groupsOpen`). `groupCell(members,
  ses, scene)` returns `{ has, of, playing, bars: [state per member] }`.
- **Layout** (`Scenes.svelte`): one CSS grid whose axes swap with `prefs.sessionAxis`
  (`'down'` default = scenes as rows; `'across'` = scenes as columns).
  - *down*: row 1 = corner + lane headers (sticky top); rows 2..n+1 = scene button (sticky left)
    + slots; last row = Stop All (sticky left+bottom) + lane Stop buttons (sticky bottom).
  - *across*: row 1 = Stop All (sticky top+left) + scene buttons (sticky top); then a row per lane:
    a header cell with name and Stop (sticky left) + slots.
  Sticky cells get an opaque `--bg` backing and a higher z-index; the corner cells a higher one
  still. Inside a group, member headers carry an indent and the group's colour bar.
- **Group slot**: as the desktop's — a launch triangle, "n of m", one 3 px bar per member (its
  colour / dim when empty / `--success` when playing), the cell washed with the group colour,
  stronger when a member plays; empty when no member has a clip.
- **Setting**: ⋯ menu → "Session grid" segmented control "Scenes down" / "Scenes across";
  `Prefs.sessionAxis` is stored with the other prefs (per phone).

## Constitution Check

- 0 Approved issue: #662 carries `approved`.
- I Real-time safety: no engine change.
- II Boundaries: no new C ABI; the hub uses existing engine calls.
- III Licensing: no new files without headers, no dependency.
- IV Verification: `scripts/build.sh` + smoke extension; page checks + unit test.
- V One change: the Scenes grid of Nota Remote only.
- VI Docs: CHANGELOG, FEATURES, nota-docs Remote page (EN + RU).
- VII Spec-first: this spec.

Post-design re-check: no violations.

## Project Structure

```text
src/managed/Nota.Remote/RemoteHub.State.cs   # BuildSession: groups + k
src/managed/Nota.Remote/RemoteHub.Apply.cs   # launch / stopSlot on a group
src/web/remote/src/lib/types.ts              # SessionTrack.k, Prefs.sessionAxis
src/web/remote/src/lib/grid.ts               # lanes, folding, group summary (new)
src/web/remote/src/lib/app.svelte.ts         # default pref
src/web/remote/src/screens/Scenes.svelte     # the grid
src/web/remote/src/components/Sheets.svelte  # the setting
src/web/remote/mock/nota-mock.mjs            # a group in the mock project
src/web/remote/test/grid.test.ts             # unit tests (new)
tests/Nota.SmokeTest/RemoteTests.cs          # group in ses, group launch / stop
```
