# Tasks: Keep held notes safe when changing a track's MIDI source

**Input**: [spec.md](spec.md), [plan.md](plan.md)

## Phase 1: User Stories 1 and 2 — switch the route, repeated changes (P1)

- [x] T001 `routedBits` / `routedFrom` in `Track::LiveHeld` (`src/native/nota.engine/src/engine/Track.h`) (FR-001)
- [x] T002 Track the route's notes and release them on a new MIDI In in `Engine::renderInstrumentRaw`; `Engine::releaseRouted` (FR-002, FR-004, FR-005)
- [x] T003 `MidiSourceSwitchTests` in `tests/Nota.SmokeTest`: switch to None / another track, the new route, switch away and back, own notes kept; in the full run and alone as `--midi-source`

## Phase 2: User Story 3 — the source track is removed (P2)

- [x] T004 Release the notes of a MIDI In whose track isn't in the graph (FR-003)
- [x] T005 Removed source in `MidiInputPick.Compact`, the track menu, the inspector and the detail chip; `en.json` keys (FR-006)
- [x] T006 Smoke test: removal releases, MIDI In still names the track, undo plays the route again

## Phase 3: Polish

- [x] T007 `CHANGELOG.md` `[Unreleased]` and `FEATURES.md` entries
- [x] T008 Native engine and `Nota.sln` build on Linux; the smoke test's `--midi-source`, `--session-input` and `--localization` runs pass
