# Bug Fix: Milestone 0.60.2 bugs, second batch

- **Slug**: 0602-milestone-2
- **Fixed**: 2026-11-16
- **Assessment**: ./assessment.md
- **Status**: applied (8 issues); #450, #544, #552, #300, #596 not changed

## Changes

| Issue | File | Change |
|-------|------|--------|
| #646 | `src/managed/Nota.App/MainWindow.axaml.cs`, `MainWindow.Menu.cs` | a press outside the detail area lets the roll's grid give up the keyboard; Copy / Cut / Delete ask the roll first only when the detail area was clicked last (the roll still answers when the Arrangement has nothing selected); a press on the in-window menu bar doesn't move "last used" |
| #647 | `src/managed/Nota.App/ArrangementView.cs`, `ArrangementView.LaneControl.cs`, `Nota.Application/Editing/ArrangementMoves.cs` | `RemovingClips` wraps delete, cut and the context-menu delete: it snapshots clip starts, runs the edit and raises `ClipsMoved` with `ArrangementMoves.AfterRemoval` — the open editors follow (a removed clip's editor closes) and undo / redo of the removal walk them back like a block move. `ClipsRemoving` drops a tool preview first |
| #648 | `src/managed/Nota.App/PianoRollView.cs`, `MainWindow.Detail.cs` | `Load` clears the preview backup, its owners and the ghost marks together; a clip-geometry reload cancels the preview before it re-reads the clip |
| #649 | `src/native/nota.engine/src/engine/Engine.cpp`, `Engine.h` | `releaseVanishedClipNotes`: while playing, `publishRaw` compares each instrument track an edit replaced with the old one and sends `ReleaseNote` for a pitch its clips sounded around the playhead before and not after |
| #649 | `src/native/nota.engine/src/devices/instruments/VoltSynth.cpp` | mono `noteOff` also releases voices other than 0 on that pitch |
| #649 | `src/managed/Nota.App/MainWindow.Detail.cs` | the roll's audition note-off goes to the track its note-on went to; the replaced editor stops its audition first |
| #645 | `src/native/nota.engine/src/devices/instruments/Monolith.cpp` | a note-on with no key held retriggers whatever Legato says; a pitch struck again while held retriggers unless Legato is on |
| #650 | `src/native/nota.engine/src/devices/racks/DrumRack.cpp`, `RackInstrument.cpp` | a silent pad / chain renders its (non-idle) instrument into scratch and drops it; the pad's decay envelope runs on |
| #644 | `src/native/nota.engine/src/engine/Engine_Render.cpp` | Humanize draws one jitter per step, shared by every note-on at that offset; its generator is `thread_local` |
| #644 | `src/native/nota.engine/src/devices/racks/DrumRack.{h,cpp}`, `RackCore.h` | a choke spares a pad of the group struck between the same two renders (the same sample) |
| #643 | `include/nota/nota_engine.h`, `src/abi/nota_engine_device.cpp`, `Engine.h`, `Engine_Rack.cpp`, `Nota.Infrastructure/Interop/*`, `IAudioEngine.cs` | `nota_rack_chain_accepts_sidechain` |
| #643 | `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs` | the chain-device proxy reads and sets the chain key (`RackChainSidechain` / `RackChainSetSidechainKey` / `RackChainSidechainPart`), gain, mix and tap included |
| all | `tests/Nota.SmokeTest/Milestone0602Tests.cs`, `ArrangementMovesTests.cs`, `CHANGELOG.md` | smoke checks (`--m0602`, `--moves`); changelog entries |

## Review follow-up (Greptile on #651)

| Finding | Change |
|---------|--------|
| A later note of the same pitch kept a deleted one ringing | `releaseVanishedClipNotes` looks half a beat around the playhead on the old side only; on the new side just at notes already held (0.05 beat ahead, for the render running ahead of the UI) |
| Range cut / delete left editors on old indices | `CutTimeSelection` and `DeleteTimeSelection` go through `RemovingClips`; `AfterRemoval` finds each kept clip by its start past the pieces a carve added (a clip whose head was cut closes its editor) |
| A silent MIDI chain aged its notes for the whole segment | the silent path renders event by event like the audible one |
