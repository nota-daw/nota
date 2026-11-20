# Chroma MIDI menu verification

- **Slug** chroma-midi-menu
- **Tested** 2026-11-20
- **Assessment** [assessment.md](assessment.md)
- **Fix** [fix.md](fix.md)
- **Result** Verified

Before the fix, the headless reproduction found two configured choices and
zero visible rows on both cards. After the fix, the test inspected each
arranged menu row and its header in the displayed presenter, then selected
the instrument track with the pointer and returned to `Manual` with the keyboard.

The menu opened with a pointer click and with Space. Radio marks followed the
selection. Reopening reflected added and removed tracks, including a project
with `Manual` as the sole choice. Escape dismissed the menu. All 16 combinations
of card size, theme and display scale passed. The headless build and all 47
focused Chroma integration checks passed.
