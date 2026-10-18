# Tasks: Hear notes in the piano roll

**Input**: [spec.md](spec.md)

- [x] T001 `PianoRollView`: `AuditionOn/Off` host hooks, app-wide `AuditionEnabled` + `AuditionToggled`, one-note `Audition/StopAudition`, `SelectPitch`
- [x] T002 Note grid: audition on double-click placement, note press and pitch change while dragging; stop on release / capture loss / detach
- [x] T003 Keys column: press plays (forced), >4 px vertical drag becomes the 019 zoom, ⇧-drag glides, ⌘-click selects the pitch; the sounding key lights
- [x] T004 `ClipEditorView`: speaker chip; `MainWindow`: wire both roll builders to `TrackNoteOn/Off` (skipped while recording), seed + save `Settings.PianoRollAudition`
- [x] T005 Settings → Shortcuts row, CHANGELOG, FEATURES.md, nota-docs piano-roll page EN + RU
- [x] T006 Verified in the headless app against the real engine (track meter reads the auditioned note), `scripts/build-linux.sh` green; macOS check on the Mac
