# Implementation plan

Reuse the Arrangement MIDI clip trim path. It already rebases note starts and
controller points without deleting them. Note expression is relative to its note
and must retain its original offsets. Clip velocity and volume envelope points
currently do not move with a left trim. Shift those points on the authoring side,
preserving their curves and keeping hidden points available for re-extension.

Add a dedicated boundary row in the Piano Roll ruler. Preview bounds and duration
in the view, then send one validated trim on release. Keep the active clip identity
stable during the gesture, including when multiple clips are open. Disable boundary
editing when the host cannot safely identify or resize the clip. The host reloads
the editor and Arrangement from the committed engine state.

Persist Auto Expand MIDI Clip in Settings, off by default. Calculate proposed
bounds from the full extent of the edit in timeline beats before clamping notes.
With expansion enabled, keep edits owned by their intended clip. Clamp left growth
at timeline zero and validate overlaps and clip identity before writing. Apply
the bounds change and rebased replacement notes inside the existing undo group.
Use the same transaction for create, move, resize, duplicate and paste.

Keep native updates on the authoring side and publish through the existing
immutable graph path. Reuse the current C ABI unless it cannot express an atomic
validated edit. Initial scope is Arrangement MIDI clips. PR #1124 stays separate.
