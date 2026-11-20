# Research: Session view — turn the grid (spec 108)

## R1 — How to turn the desktop grid

**Decision**: keep the Session view's three-strip construction and flip its axis. Today
(`SessionView.cs` constructor, `Refresh`, `SessionView.Grid.cs` `AddSceneRail` / `AddColumn`):

| Strip | Scenes down (today) | Scenes across |
|---|---|---|
| `_headerRow` | horizontal; corner + one `ColumnHeader` per column; pinned at the **top** | vertical; corner + one header per lane; pinned at the **left** |
| `_cellsRow` | horizontal; scene rail first, then one vertical stack of cells per column; scrolls **up/down** (inner `ScrollViewer`) | vertical; scene header row first, then one horizontal stack of cells per lane; scrolls **sideways** (inner `ScrollViewer`) |
| `_footerRow` | horizontal; rail foot (+ Scene, Stop All, mixer row labels) + per column (spacer, Stop, mixer); pinned at the **bottom** | vertical; corner foot (Stop All, mixer section labels) + per lane (Stop, mixer block); pinned at the **right** |
| outer `ScrollViewer` | sideways | up/down |

Building is done once per `Refresh()`; the orientation is read there, so a switch is one
`Refresh()`. Slot / group / blank cells already paint into their own `Bounds`, so they only get
their size from the lane (width = `TrackColW`, height = the lane height) — no painting change.
`ColumnHeader` takes a size and a turned arrangement (a Stop button moves out of it — see R3).
`SceneCell` gets the turned size (`TrackColW × HeaderH`) and its launch box / drag axis follow.

**Rationale**: one code path, minimal risk to the many behaviours in the cells (launch, drag,
record previews, follow marks); exact transposition of today's pinning rules, so both
orientations behave alike (today the scene rail scrolls away sideways; turned, the scene row
scrolls away vertically).

**Alternatives considered**: a second view class (duplicates ~3 000 lines); a
`LayoutTransform` rotate (text would turn too); pinning the scene row on both axes with two
synchronised scroll viewers (more than the issue asks, and not what Scenes down does today —
can follow later for both orientations).

## R2 — Lane height and the mixer block (clarification Q1 = right end of each row)

**Decision**: in Scenes across each mixer section (I/O, Sends, Mixer) is a column of its rows,
`TrackColW` wide, and the shown sections sit side by side at the right of the lane, after the
Stop button. The lane height is `max(CellH, tallest shown section)`; with every section hidden
it is `CellH` (58). Today's sections are 78 (I/O), ≤ 87 (four sends) and 79 (Mixer) px tall, so a
lane with the mixer shown is ~80–87 px; the slot cells stretch to it. The row labels that the
scene rail carries today (Input, Monitor, Pan, Volume, Send A…) become section titles in the
top-right corner ("I/O", "Sends", "Mixer"); the controls keep their own readouts and gain
tooltips with their row names.

**Rationale**: reuses `BuildMixer` rows unchanged; the toolbar toggles keep working because
`Refresh()` rebuilds.

**Alternatives**: one-line compact mixer (new controls, more design work); fixed 58 px lanes with
scrolling sections (cramped, nothing gained).

## R3 — Stop, Stop All, + Scene in the turned layout

**Decision**: per lane, the right strip starts with a Stop button (`StopColW` = 40 px wide, lane
tall, the stop glyph); the corner above it holds Stop All (glyph + "All", tooltip as today).
"+ Scene" moves from the rail foot to the end of the scene header row (a ghost button the size
of a scene header). Return / Master lanes have a blank where Stop is, as their columns do today.

## R4 — Keyboard and commands: grid meaning vs screen direction

**Decision**: introduce a semantic step `SessionStep { PrevScene, NextScene, PrevTrack, NextTrack }`
and implement `Move` on it (today's rules: past the first track → the scene; past the last track
→ Return / Master; on a scene, NextTrack → first track). Arrow keys map by orientation
(down: ↑↓ scenes, ←→ tracks; across: ←→ scenes, ↑↓ tracks). The commands keep their ids
(`session.selectUp/Down/Left/Right`) for saved shortcuts and controller profiles, are wired to
`PrevScene/NextScene/PrevTrack/NextTrack` and renamed in `en.json`: "Select the previous scene",
"…next scene", "…previous track", "…next track". The controller setup labels ("Slot up / down /
left / right") become "Previous / next scene", "Previous / next track".

**Rationale**: FR-006/FR-007; the controller paths (`ControllerService`, `ProtocolSurface`,
`MainWindow.Commands`) already call these ids — no change there.

## R5 — Where the preference lives and how it applies

**Decision**: `Settings.SessionGrid` (string, `"down"` default / `"across"`), saved with the app
settings. Preferences → Editing gets a "Session" section with a two-way segmented choice;
the Session toolbar gets a toggle button (a turn glyph, tooltip "Scenes across" / "Scenes
down"). Both write the setting and `Save()`; `SessionView` listens to `ISettingsService.Changed`
and refreshes when the value differs from what it built. `MainWindow` tells the Remote hub (R6).

## R6 — Remote protocol and the phone (clarification Q2 = "As in Nota" default)

**Decision**: `IRemoteHost` gains `bool SessionAcross { get; }` (default `false` so test hosts
keep compiling). `BuildSession` writes `"ax": "across"` only when true (absent = down, which is
also what an older Nota sends). The `ses` message is already resent when its text changes, so a
switch reaches every phone on the next tick.

Phone (`src/web/remote`): the pref becomes `sessionGrid: 'nota' | 'down' | 'across'` (default
`'nota'`). Migration on load: an old `sessionAxis: 'across'` → `'across'`; anything else →
`'nota'` (prefs are saved whole, so "never picked" and "picked down" can't be told apart; both
look identical until the desktop turns). `across = sessionGrid === 'across' || (sessionGrid ===
'nota' && ses.ax === 'across')`. The ⋯ sheet shows three buttons; its caption names the
effective layout ("As in Nota — scenes down now").

## R7 — Nota NX

**Decision**: parse `ax` into `S.notaAcross`; add `Prefs::sessionGrid` (0 = As in Nota,
1 = Scenes down, 2 = Scenes across), saved in the config like `uiScale`, with a three-way
`segGroup` in the settings sheet. `scenes::draw` gets the turned variant: scene launch buttons
pinned at the top, track name + Stop pinned at the left, Stop All in the top-left corner; the
stick / touch `Pan` already scrolls by screen axes. NX has no D-pad navigation on Scenes today, so
there is nothing to turn there (spec FR-009 updated). Ships in `nota-nx`, built with the desktop
target (`make -f Makefile.desktop`) and the devkitPro NRO check (see memory "Nota NX").

## R8 — Tests

- `tests/Nota.SmokeTest/RemoteTests.cs`: the fake host's `SessionAcross` flips; `ses` carries
  `ax:"across"`, then loses it.
- `src/web/remote/test/grid.test.ts` (vitest): the effective-axis helper and the prefs migration.
- Desktop: a headless-Avalonia harness (memory "Nota UI verify recipe") for screenshots of both
  orientations; `SessionView` navigation through a pure `GridNav` helper unit-checked in the
  smoke test (step mapping per orientation).
