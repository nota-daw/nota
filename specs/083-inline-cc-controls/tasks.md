# Tasks: Controls inside MIDI CC lanes

**Input**: [spec.md](spec.md), [plan.md](plan.md), research/data model/UI contract.

## Phase 1: Setup

- [x] T001 Check out PR #425's existing branch and record original head in research.md.
- [x] T002 Complete spec/clarification/plan and quality checklist in this directory.
- [x] T003 Build baseline and capture before with specs/071-multiple-cc-lanes/validation/Capture.

## Phase 2: Shared foundation

- [x] T004 Refactor shared controller shell in src/managed/Nota.App/PianoRollView.ControllerLanes.cs.

## Phase 3: US1 — In-lane controls

- [x] T005 [US1] Use shared header for extra lanes in PianoRollView.ControllerLanes.cs.
- [x] T006 [US1] Host primary header and guard picker callbacks in PianoRollView.Expression.cs and PianoRollView.Controllers.cs.
- [x] T007 [US1] Check selection/removal/pointer isolation in specs/071-multiple-cc-lanes/validation/Program.cs.

## Phase 4: US2 — Sidebar creation

- [x] T008 [US2] Place add action below primary tabs and remove toolbar row in PianoRollView.cs.
- [x] T009 [US2] Budget sidebar and stacked clip heights in PianoRollView.ControllerLanes.cs and PianoRollView.ClipStrip.cs.
- [x] T010 [US2] Check short docked/floating and stacked-clip layouts in validation/Program.cs.

## Phase 5: US3 — Value scale

- [x] T011 [US3] Draw non-interactive scale with graph mapping in PianoRollView.ControllerLanes.cs.
- [x] T012 [US3] Check scale mapping, resized heights, zoom/scroll and folded gutters in validation/Program.cs.

## Phase 6: Polish and submission

- [x] T013 Update Languages/fr.json, CHANGELOG.md, FEATURES.md and EN/RU manual text.
- [x] T014 Run build-win.ps1, CCProbe and fresh-process height checks; inspect before/after screenshots; record validation.md.
- [x] T015 Run speckit-converge against these artifacts and resolve remaining work.
- [ ] T016 Commit and push to existing PR branch; verify PR #425 head includes the commit.

## Dependencies and strategy

Setup → shared foundation → US1 → US2 → US3 → polish. US1 is the first reviewable
slice, then add sidebar and scale behavior, then full regression validation.
Documentation and independent evidence capture can run alongside verification;
same-file lane work is sequential. No additional agent delegation is needed.
