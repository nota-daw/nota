# Bug Fix: Milestone 0.60.2 bugs

- **Slug**: 0602-milestone
- **Fixed**: 2026-11-15
- **Assessment**: ./assessment.md
- **Status**: applied (14 issues); #450, #552, #300, #596 not changed; #544 diagnostics only

## Changes

| Issue | File | Change |
|-------|------|--------|
| #530 | `src/managed/Nota.Application/Editing/ClipSet.cs` | `ActiveClip` answers the last active clip once the set is empty |
| #530, #537 | `src/managed/Nota.App/MainWindow.Detail.cs`, `MainWindow.Menu.cs`, `MainWindow.axaml.cs` | `DropDeadEditors`: after a track edit and after undo / redo, the roll prunes dead clips (closing when none is left, the floating pane shows the placeholder), the audio editor and the step grid close when their clip is gone; undo / redo reloads the audio editor |
| #537 | `src/managed/Nota.Application/Engine/WarpMarkers.cs` (new), `Nota.App/AudioClipEditorView.cs`, `Nota.Infrastructure/Persistence/AudioClipState.cs` | read every warp marker (grow the buffer to the count the engine reports) |
| #529 | `src/managed/Nota.App/ArrangementView.Sections.cs` | Duplicate = insert silence of the section's length at its end + sections after it moved + the copy, one undo group; an overlapping section keeps the marker-only copy |
| #604 | `src/managed/Nota.App/ArrangementView.LaneControl.cs` | a trim grabbed on a member of a multi-selection keeps the selection and moves every selected clip's edge by the same delta; one undo step, ordered so a grown edge never carves a clip before it is resized |
| #565 | `src/managed/Nota.App/ArrangementView.LaneControl.cs` | notes drawn shifted by how far a left-edge trim moved the start |
| #538 | `src/managed/Nota.App/MixerView.cs`, `MainWindow.Mixer.cs` | `TrackPicked` on a press anywhere on a strip → `SelectTrackFromOutside` |
| #548 | `src/managed/Nota.App/MainWindow.Input.cs` | unused arrows with nothing focused are handled (no focus walk); a press outside the menu bar drops its focus |
| #445 | `src/managed/Nota.App/PianoRollView.cs` | `BringIntoViewOnFocusChange = false` on the roll's body; edge autoscroll only after a move toward that edge |
| #467 | `src/native/nota.engine/src/engine/Engine_Tracks.cpp` | `carryMidiPiece` / `carryAudioPiece` in `splitClip` and both overwrite loops, only when the clip is deactivated or has envelopes (the carves stay lean for the perf gate) |
| #559 | `src/native/nota.engine/src/dsp/core/Parameter.h` (`BlockRamp`), `devices/effects/{Amp,Vintage,Crush,Ceiling,Compressor,Forge}.{h,cpp}` | per-block linear ramps |
| #573 | `src/managed/Nota.App/Controls/AuroraTableView.cs` | pixel source rect |
| #221 | `src/native/nota.engine/src/engine/EditorKeys.h` (new), `pluginhost/hosting/PluginEditorWindow.{h,cpp}`, `include/nota/nota_engine.h`, `src/abi/nota_engine_device.cpp`, `Nota.Infrastructure/Interop/*`, `IAudioEngine.cs`, `Nota.App/MainWindow.EditorKeys.cs` (new) | editor windows hand the app's claimed keys (note map, octave, velocity — from the user's bindings) to a queue the UI tick drains and plays like its own keys; macOS NSEvent monitor (not in the plug-in's text fields), Windows / Linux JUCE key handling with releases by key state; held keys released when the window deactivates |
| #164 | `src/managed/Nota.App/App.axaml.cs` | `Dispatcher.UIThread.UnhandledException`: the "wrong LayoutManager" throw from Avalonia's layout pass is logged and handled, every window re-laid out |
| #358 | `src/managed/Nota.Infrastructure/SampleStore/SampleLibraryIndex.cs`, `BackgroundIo.cs` (new) | no recursion into linked folders; save interval grows with the index; Linux: nice 19 + idle I/O class on the index threads |
| #544 | `src/native/nota.engine/pluginhost/hosting/PluginCommon.cpp` | log the format's reason a plug-in didn't load, and a non-instrument refusal |
| all | `tests/Nota.SmokeTest/Milestone0602Tests.cs` (new), `Program.cs` | smoke checks for #467, #537, #530 (`--m0602`) |
