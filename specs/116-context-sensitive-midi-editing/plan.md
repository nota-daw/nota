# Implementation plan for #1122

Reuse PianoRollView and its existing selection, clip ownership, preview, snap and command infrastructure. Build in independently usable slices.

1. Add size-aware hit zones and direct relative velocity dragging. Keep edge resize and short-note movement accessible. Preview through the existing editor path. Restore notes and selection on Escape, and release capture and audition on every exit path. Check Arrangement and Session separately because their commit callbacks differ.
2. Add explicit split and join actions to the existing note menu. Split only inside the note with valid durations. Partition expression at the cut and translate the right portion to its new origin. Join only notes in the same clip and pitch with compatible articulation, velocity and expression. Keep reference clips read-only.
3. Extend the settings strip with editable position, length, pitch and velocity. Parse using Nota's number and musical-position conventions. Validate all target notes before committing and use clip-relative values for multi-clip edits.
4. Add middle-button pan and dotted grid choices while preserving existing straight and triplet settings. Reuse the beat and row viewport transforms and keep snap bypass on the established modifier.
5. Implement persistent mute through the managed and C ABI contracts, the native note model and project serialization. Default old projects to unmuted. Filter muted notes before publishing playback events, including articulation and expression, with no audio-thread allocation or locks. Check both Session and Arrangement playback, scheduling, undo, clip copying and project round trips.

Update localization, Shortcuts, FEATURES and CHANGELOG alongside each user-visible slice. The sibling nota-docs checkout is currently absent. Clip boundary editing belongs to #1121.

### Duplicate by dragging

Latch the duplication modifier at pointer press on an existing note's move region.
Use Ctrl on Windows/Linux and Command on macOS as the proposed default, scoped to
this region and a drag past the threshold. Alt/Option already bypasses snapping
during note drags, and Ctrl/Command remains velocity control during new-note
placement. Use the current move and placement rules for copied notes, and register
the new gesture in Shortcuts.

### P0: Alt/Option edge drag

Choose stretch mode at pointer press and keep it for the gesture. Use the selection
bounds and original note positions for each update, so repeated updates cannot
accumulate rounding errors. Preserve the grab offset from the moving selection
edge when grabbing an interior note edge. Snap only the moving outer edge. Read
Shift live as snap bypass within stretch mode. Clamp the common scale against
every selected note's clip bounds and minimum length. Reuse the pointer preview,
cancellation and single commit paths. Include the gesture in ShortcutLayout.

### Single-click note creation

Defer empty-grid creation until pointer release confirms a click. Crossing the
drag threshold routes to the existing marquee path. Guard the second click of a
double-click sequence that began on empty space. Reuse note placement defaults
and commit handling. Update the Piano Roll hint and Shortcuts gesture descriptions.
Refinement: with distinct starts, anchor the first note-on and snap the last note-on. Keep opposite-edge length scaling when all starts coincide.
