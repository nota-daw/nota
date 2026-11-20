# Implementation Plan: Session view — turn the grid, scenes across and tracks down

**Branch**: `feat/session-grid-orientation` | **Date**: 2026-11-20 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/108-session-grid-orientation/spec.md` (issue
nota-daw/nota#968)

## Summary

An app preference, Session grid = Scenes down (today) / Scenes across, turns the desktop Session
view by flipping the axis of its existing three-strip layout (headers / cells / Stop + mixer),
so every cell keeps its behaviour and painting ([research R1–R3](research.md)). Selection moves
become grid-meaning steps: arrow keys follow the screen, the `session.select*` commands (and so
controllers) keep stepping by scene / track ([R4](research.md)). The desktop tells remotes its
orientation in the `ses` message (`ax`); Nota Remote and Nota NX get "As in Nota" (default) /
Scenes down / Scenes across, and NX learns to draw Scenes across ([R6, R7](research.md)).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) and the `nota-design` skill: turned headers keep
  the stripe / name / kind typography; the toolbar toggle is a ghost icon button like the I/O,
  Sends and Mixer toggles.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (managed app, Remote hub); TypeScript + Svelte 5 (phone page);
C++17 / SDL2 (Nota NX, separate repo)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: app settings JSON (`Settings.SessionGrid`); phone localStorage `prefs`; NX
`config.json` `prefs`

**Audio-Thread Impact**: None — a view preference; no engine call is added or changed.

**Testing**: `tests/Nota.SmokeTest` (Remote `ses` axis; `GridNav` step mapping), vitest in
`src/web/remote/test`, NX desktop build + scripted shots, headless-Avalonia screenshots of the
desktop view in both orientations

**Target Platform**: macOS / Windows / Linux desktop; phone browsers; Nintendo Switch (homebrew)

**Project Type**: desktop app + web client + homebrew client

**Performance Goals**: a switch redraws a 32 × 32 grid in < 0.5 s (SC-001) — it is one
`Refresh()`, which the view already does on every structural change

**Constraints**: command ids, controller profiles and saved shortcuts unchanged (FR-007);
older Nota ↔ newer remote and newer Nota ↔ older remote keep working (FR-010)

**Scale/Scope**: ~7 files in `Nota.App`, 1 in `Nota.Application` (+ `en.json`), 2 in
`Nota.Remote`, 4 in `src/web/remote`, 4 in `nota-nx`, plus CHANGELOG, FEATURES, docs

## Constitution Check

| Principle | Status |
|---|---|
| 0 Approved issue | ✅ #968 carries `approved` |
| I Real-time audio safety | ✅ no audio-thread code touched |
| II Boundary discipline | ✅ no C ABI change, no JUCE |
| III Licensing | ✅ no dependency; the new file `SessionView.Axis.cs` carries the SPDX header |
| IV Verification gate | ✅ smoke test extended (Remote `ax`, `GridNav`); `scripts/build.sh` before PR |
| V One logical change | ✅ one feature; the NX part is its own commit in its own repo |
| VI Docs in sync | ✅ CHANGELOG, FEATURES (nota-features), user docs (nota-docs), command names (nota-shortcuts) |
| VII Spec-first | ✅ this spec / plan / tasks |
| VIII Safety rules | ✅ nothing bent |

Re-check after design: unchanged — the design adds no engine, ABI or dependency surface.

## Project Structure

### Documentation (this feature)

```text
specs/108-session-grid-orientation/
├── spec.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── remote-ses-axis.md
│   └── desktop-ui.md
├── checklists/requirements.md
└── tasks.md            # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Application/
│   ├── Settings.cs                          # SessionGrid ("down" | "across")
│   └── Localization/en.json                 # command names, preference + toolbar strings
├── Nota.App/
│   ├── SessionView.cs                       # axis-aware strips + scroll viewers; Move on SessionStep; settings listener
│   ├── SessionView.Grid.cs                  # AddSceneRail / AddColumn turned; ColumnHeader + SceneCell sizes; SceneAt by X; drag marker
│   ├── SessionView.Mixer.cs                 # mixer block side by side + section titles in Scenes across
│   ├── SessionView.Toolbar.cs               # the turn toggle
│   ├── SessionView.Axis.cs (new)            # SessionStep, GridNav (arrow → step per orientation), lane geometry
│   ├── PreferencesWindow.Editing.cs         # Session section
│   ├── MainWindow.Commands.cs               # session.select* → steps
│   ├── MainWindow.Remote.cs                 # IRemoteHost.SessionAcross
│   └── Controllers/ControllerSetupWindow.cs # labels (via en.json keys)
└── Nota.Remote/
    ├── RemoteContracts.cs                   # IRemoteHost.SessionAcross (default false)
    └── RemoteHub.State.cs                   # "ax" in BuildSession

src/web/remote/src/
├── lib/app.svelte.ts                        # sessionGrid pref + migration + effective axis
├── lib/types.ts                             # Session.ax
├── components/Sheets.svelte                 # three-way choice
└── screens/Scenes.svelte                    # use the effective axis

tests/Nota.SmokeTest/                        # RemoteTests: ax; GridNav mapping check
src/web/remote/test/grid.test.ts             # migration + effective axis

../nota-nx/src/                              # state.hpp/.cpp (pref), link.cpp (parse ax),
                                             # screens.cpp (Scenes across), overlays.cpp (setting)
```

**Structure Decision**: extend the existing Session view partials and Remote layers; one new
partial (`SessionView.Axis.cs`) holds the orientation-specific geometry and navigation so the
other partials only ask "across?" at build time.

## Phases (for /speckit-tasks)

1. **Foundation** — `Settings.SessionGrid`; `SessionStep` + `GridNav`; `Move` on steps; commands
   wired to steps; command / controller-setup names. (Delivers US2's controller half with no
   visual change.)
2. **US1 desktop turn** — axis-aware `Refresh`; turned headers, scene row, "+ Scene"; cells
   sized by lane; scene drag by X; Browser drops; group folding; selection; arrow keys by
   orientation; preference UI + toolbar toggle + live apply.
3. **US3 mixer block** — sections side by side at the right, lane height, Stop strip + Stop All,
   section titles, Return / Master lanes.
4. **US4 remotes** — `ax` in `ses` (+ smoke check); phone pref + migration + sheet (+ vitest);
   NX pref, parse, Scenes across drawing, settings sheet (+ desktop build, shots, NRO check).
5. **Polish** — CHANGELOG, FEATURES.md, nota-docs page + shots (both orientations), nota-shortcuts
   (renamed commands), RU strings via the translations repo, `scripts/build.sh` green,
   `/speckit-converge`.

## Complexity Tracking

None.
