# Tasks: Default waveform rendering

- [X] T001 `SampleBuffer` RMS table and `rmsRange` (`src/native/nota.engine/src/media/SampleBuffer.*`)
- [X] T002 Optional RMS output on the clip peak queries (`src/native/nota.engine/src/engine/Engine*.{h,cpp}`)
- [X] T003 `nota_clip_get_levels` + `NOTA_CLIP_LEVELS_*` (`nota_engine.h`, `abi/nota_engine_track.cpp`)
- [X] T004 Interop and `IAudioEngine` optional `outRms` (`Nota.Infrastructure/Interop`, `Nota.Application/Engine`)
- [X] T005 `WaveformPainter` (`src/managed/Nota.App/WaveformPainter.cs`)
- [X] T006 Lanes: RMS plumbing and the Smooth path (`ArrangementView*.cs`)
- [X] T007 Clip editor: RMS plumbing, painter, device-pixel detail (`AudioClipEditorView.cs`)
- [X] T008 `WaveformStyle` Smooth / Solid / Bars, preference, setting doc (`ArrangementView.cs`, `PreferencesWindow.cs`, `Settings.cs`)
- [X] T009 Smoke test RMS checks (`tests/Nota.SmokeTest/Program.cs`)
- [X] T010 `CHANGELOG.md`, `FEATURES.md`
- [X] T011 Before/after screenshots at three zoom levels (in the PR description)
