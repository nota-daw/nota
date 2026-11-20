# Chroma MIDI menu opens empty

- **Slug** chroma-midi-menu
- **Source** User screenshot of the MIDI popup with instrument tracks available
- **Verdict** Valid
- **Severity** Medium

Opening MIDI on either Chroma card displays a popup with no rows. The local
headless reproduction found `Manual` and `Chord source` in the menu collection
but zero rows in the displayed presenter. The earlier test exercised the
collection and invoked a choice directly, so it missed the rendering failure.

Chroma fills an initially empty flyout in `Opening`, after Avalonia creates its
presenter. In the same reproduction, filling a fresh menu before displaying it
made both rows visible on S and L. Use that order and verify the displayed
presenter with real pointer and keyboard input.
