# Tasks: Arrangement header sends, Large only

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: Where the knobs show (US1)

- [x] T001 Show the send row only at `LargeRowHeight` and taller in `src/managed/Nota.App/ArrangementView.Sends.cs`, and remove the **Sends** key, `SetShowSends` and the `showSends` sidecar flag (`ArrangementView.cs`, `ArrangementView.Sizing.cs`) per FR-001, FR-002, FR-007

## Phase 2: Not undoable, not automatable (US3)

- [x] T002 `SendControl` only calls `SetTrackSend`. MCP `set_send` does the same, in `src/managed/Nota.Mcp/Tools/TrackTools.cs`, per FR-005
- [x] T003 Reject `MixKind::TrackSend` in `mixValue` / `applyMix` (`Engine.cpp`) and reject `AutomationTarget::Send` in `addAutomationLane` and `applyAutomation` (`Engine_Automation.cpp`, `Automation.h`, `nota_engine.h`) per FR-006
- [x] T004 Remove send entries from the lane menu (`ArrangementView.Automation.cs`) and from MCP `add_automation_lane` (`AutomationTools.cs`) per FR-006
- [x] T005 Rewrite `tests/Nota.SmokeTest/SendAutomationTests.cs` so a send move is not an undo step and a send lane is not created

## Phase 3: Docs

- [x] T006 CHANGELOG `[Unreleased]`, FEATURES.md, and the arrangement page in nota-docs (EN + RU)

## Dependencies

T001 is the visible change. T002–T005 are the engine and wiring. T006 last.
