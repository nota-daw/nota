# Tasks: Reset a note's velocity to the default

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md), [contracts/velocity-lane.md](contracts/velocity-lane.md)

**Tests**: the pure rule gets smoke checks (spec SC-003). The lane's pointer wiring has no headless harness; it is checked by hand from [quickstart.md](quickstart.md).

**Format**: `[ID] [P?] [Story] Description`. Paths are from the repo root.

## Phase 1: Setup

- [X] T001 Branch `feat/velocity-reset-default` from `develop` (done with the spec); confirm `scripts/build.sh` is green before the first edit (baseline).

## Phase 2: Foundational (blocks every story)

- [X] T002 Create `src/managed/Nota.Application/Editing/VelocityReset.cs` (SPDX header, comment header saying why it exists, namespace `Nota.Application.Editing`): `public const float Default = 0.8f;` `IsDefault(float v)` true when `|v − Default| < 0.5 / 127`; `Targets(IReadOnlyCollection<int> selection, int hit, int count, Func<int,bool> isLocked, IReadOnlyList<float> velocities)` returning the indices to reset: the selection's unlocked notes when `hit` is in it, else `{hit}`; locked and out-of-range indices excluded; notes already at the default excluded (data-model: "Reset targets"). The view writes `Default` itself; there is no `Apply`.
- [X] T003 [P] Create `tests/Nota.SmokeTest/VelocityResetTests.cs` with `public static IEnumerable<(bool Ok, string Label)> Run()` in the style of `VelocityWheelTests.cs`: default is 0.8; stem outside the selection → only that note; stem inside → every selected note; a locked note is never a target; a note at 0.8 and one within half a MIDI step of it are not targets (nothing to do → empty); a note one MIDI step away is a target; out-of-range indices are dropped. Register it in `tests/Nota.SmokeTest/Program.cs` at both places `VelocityWheelTests.Run()` is called (the full run and the `--` alone run, lines ≈308 and ≈15323).
- [X] T004 Make the default single-sourced in `src/managed/Nota.App/PianoRollView.cs`: `s_lastVel` starts at `VelocityReset.Default` (line ≈241) and `KeyVelocity` (line ≈1850) reads it; update the comment above `s_lastVel` ("0.8") to point at the constant.

**Checkpoint**: `dotnet run --project tests/Nota.SmokeTest -c Release -- --velocity-reset` (add the alone-run flag next to the others in `Program.cs`) passes.

## Phase 3: User Story 1 — Put one velocity back (P1) 🎯 MVP

**Goal**: double-click a stem → that note goes to 0.8; one ⌘Z returns to the value before the double-click.

**Independent test**: quickstart §2, steps 1–3, 6, 7.

- [X] T005 [US1] In `PianoRollView.VelocityLane.OnPointerPressed` (`src/managed/Nota.App/PianoRollView.cs`, ≈line 2815): stop closing the burst unconditionally. Close it (`_o.EndVelocityWheel()`) unless this is the second press of a double-click (`e.ClickCount == 2`) on the same stem as the open lane burst; remember that stem when a lane gesture opens the burst.
- [X] T006 [US1] Make the lane's click/drag a velocity burst (research R3): on the first press of a gesture, open `_velWheelBurst` and, when `HasLivePath`, seed the undo step with `CommitNotes(_notes.ToList(), _owner.ToList(), seed: true)` before `Apply`; push changes with `PushLive` instead of committing at release when `HasLivePath`; a Session slot (no live path) keeps its notes in the view and commits when the burst closes (`EndVelocityWheel` already does). Restart `_velWheelTimer` on release (extract the timer start from `WheelVelocity` into a small `RestartVelocityBurstTimer()` and call it from both). Set `_velWheelNote = _velNote` so `Remember` runs at close. `OnPointerReleased` no longer calls `CommitNotes()` itself.
- [X] T007 [US1] On `ClickCount == 2` (after T005's guard): compute `VelocityReset.Targets(...)` for the hit stem, set each target's velocity to `VelocityReset.Default`, `PushLive`/hold as T006, `Invalidate()`, `Changed?.Invoke()`; no targets → do nothing (no seed, no step). Keep the burst open; do not start a drag for the second press (`_drag = false`).
- [X] T008 [US1] Verify by hand (quickstart §2 steps 1–3, 6, 7; §4 playing loop and locked clip). Fix what the pass finds.

**Checkpoint**: a single note resets with one double-click and one undo.

## Phase 4: User Story 2 — Put a selection back (P2)

**Goal**: the stem of a selected note resets the whole selection; a stem outside the selection resets only itself and becomes the selection.

**Independent test**: quickstart §2 steps 4–5.

- [X] T009 [US2] In the double-click branch of T007, apply the selection rule from `VelocityReset.Targets` (selected stem → whole selection kept; other stem → `_selection.Clear(); _selection.Add(hit)` as a single press already does, after `SettlePlacement()` as the existing press code does). Do not let the first press's group-drag state (`_grabbed`) carry into the reset.
- [X] T010 [US2] Verify by hand (quickstart §2 steps 4–5): both notes at 0.8 after one double-click; one ⌘Z restores both; the unselected stem case moves the selection.

## Phase 5: User Story 3 — Reset from a menu (P2)

**Goal**: right-click a stem → **Reset velocity**.

**Independent test**: quickstart §3.

- [X] T011 [US3] In `VelocityLane`, handle the right press: pick the stem with `Nearest`, none → return (no menu); apply the selection rule of T009; open a `MenuFlyout` with `MenuKit.Item(Loc.T("Reset velocity"), click: …, enabled: targets.Count > 0)` anchored on the lane (pattern: `PianoRollView.Expression.cs` `ShowClearMenu`, but with `MenuKit.Item` for the disabled state). The click handler runs the same reset path as T007 as its own burst (open, seed, apply, close at once: `EndVelocityWheel()`).
- [X] T012 [P] [US3] Add the French entry for `Reset velocity` to `src/managed/Nota.App/Languages/fr.json` (`Réinitialiser la vélocité`).
- [X] T013 [US3] Verify by hand (quickstart §3) including: the Pres / Bend tabs keep their own menus; English and French (`NOTA_UI_LANGUAGE=fr`).

## Phase 6: User Story 4 — Documented (P3)

**Goal**: the gestures are findable.

- [X] T014 [US4] Run the `nota-shortcuts` skill. In `src/managed/Nota.App/PreferencesWindow.cs` (Piano roll group, next to `Drag velocity stem`, ≈line 1529) add two rows: `Double-click a velocity stem` → `Put the note back to the default velocity (0.8) · the whole selection's when the note is selected`; `Right-click a velocity stem` → `Reset velocity from a menu`. Add both rows' texts to `fr.json` (T014 and T012 may share one edit pass). Check the pane on screen at 100 % in English and French.
- [X] T015 [P] [US4] `CHANGELOG.md` `[Unreleased]` Added entry (user-facing, in the file's voice); run the `nota-features` skill for `FEATURES.md` (the Piano Roll bullet list: add the reset next to the velocity stem line).
- [X] T016 [P] [US4] Run the `nota-docs` skill: piano-roll page of the user manual in `../nota-docs`, EN + RU, plus `coverage.yml` (sources: `PianoRollView.cs`, `VelocityReset.cs`); `npm run build && npm run check` there. A separate PR in that repo. Also: `scripts/shortcuts.mjs` could not read the app's Shortcuts list since the strings went through `Loc.T`; it now reads through the wrapper and `npm run shortcuts` regenerates `reference/shortcuts` (EN + RU), which had drifted from the app.

## Phase 7: Polish and gate

- [X] T017 `scripts/build.sh` green (macOS; run it with `MSBUILDDISABLENODEREUSE=1 DOTNET_CLI_USE_MSBUILD_SERVER=0` and an idle machine — the two "overloaded" timing checks are load-sensitive).
- [X] T018 `/speckit-converge` reports Converged; tick the boxes above.
- [ ] T019 Take before/after screenshots of the lane (the menu open on a stem, English) for the PR description, crop to the window; open the PR with the `nota-pr` skill (branch off `develop`, `(feat) …`, your noreply email, no AI trailer); the docs PR follows.

## Dependencies

- T001 → T002 → T003, T004 (T003 and T004 are independent files).
- US1 (T005–T008) needs T002 and T004. US2 (T009–T010) needs US1. US3 (T011–T013) needs T006's burst helpers and T002. US4 (T014–T016) can start once the gesture texts are fixed (after T011).
- Polish needs everything above.

## Parallel opportunities

- T003 ∥ T004 (different files).
- T012 ∥ T011 once the key text is fixed; T015 ∥ T016.

## Implementation strategy

MVP = Phase 2 + User Story 1: a working double-click on one stem with one undo step, shippable on its own. Add the selection rule (US2), the menu (US3), then the docs (US4); the PR can carry all four since they are one small feature in one file plus a helper.

## Phase 8: Convergence

- [X] T020 Leave tool-preview (ghost) notes out of the reset: in `src/managed/Nota.App/PianoRollView.cs` `VelocityLane.ResetVelocity` and `CanReset`, pass `i => _o.IsLocked(i) || _o.IsGhost(i)` instead of `_o.IsLocked` to `VelocityReset.Targets`, per FR-007 and the Edge Cases ("ghost notes … are not reset") (partial)
