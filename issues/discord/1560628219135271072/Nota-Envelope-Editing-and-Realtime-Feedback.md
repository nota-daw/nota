**Faster Envelope Point Editing and Continuous Real-Time Audio Feedback**

Improve envelope editing in Nota in two connected areas: adding and removing points with the primary mouse button, and hearing adjustments continuously while dragging points during playback.

**1. Current behavior observed**

In my current workflow:
- Adding an envelope point uses a primary click, while removing one requires a secondary click.
- During playback, dragging an envelope point does not audibly update the envelope until I release the mouse button or trackpad.

This makes editing unnecessarily cumbersome, particularly with a MacBook trackpad and when fine-tuning an envelope while a loop plays repeatedly.

**2. Add and remove points using primary clicks**

Suggested interaction:
- Primary-click an empty, editable area of the envelope to add a point.
- Single-click an existing point to select it.
- Drag an existing point to move it.
- Double-click an existing point to remove it.
- Retain secondary-click removal as an alternative, either directly or through the existing context menu.

The distinction between clicking and dragging should be reliable. Selecting or beginning to drag a point must not accidentally delete it, and double-clicking an existing point must not create an additional point underneath it.

Use clear hover and selection feedback, with practical hit targets for both mouse and trackpad input. Keep interaction predictable at different zoom levels and UI scales.

**3. Configurable interaction and safe deletion**

Allow users to enable or change the primary-click removal gesture in Settings so existing workflows remain available.

If double-click already performs another useful action, such as numerical editing, resolve that conflict explicitly through the interaction settings and retain a discoverable way to perform both actions.

Also:
- Support deleting selected editable points through an appropriate keyboard command.
- Respect protected endpoints or other structural constraints. If a point cannot be deleted, indicate why.
- Preserve existing snapping, modifier-key and multi-selection behavior.
- Make point insertion and deletion fully undoable and redoable.
- Avoid introducing confirmation dialogs for ordinary, reversible point edits.

**4. Continuous audible updates while dragging**

Apply envelope edits continuously during the drag instead of waiting for mouse-up or trackpad release.

Expected behavior:
- Hear applicable changes while the pointer is still held down.
- Update both the visual envelope and the audio consistently.
- Continue normal playback and loop playback without interruption, transport restart or manual retriggering.
- Apply the latest edited curve as playback reaches the affected portion of the envelope, including on subsequent loop passes.
- Do not imply that an edit to a region already played can change audio that has already been heard.
- Apply the same principle consistently to editable envelope parameters, including supported point positions, values and curve-shape controls.

If a specific parameter cannot support continuous updates, make that limitation explicit rather than silently applying all edits only on release.

**5. Smooth, real-time-safe processing**

The editing path should avoid clicks, zipper noise, glitches and unnecessary added latency.

Use parameter-appropriate transitions where necessary, while preserving intentional sharp envelope shapes and timing. Avoid excessive smoothing that makes the result lag behind the edit or changes the intended sound.

UI updates and audio processing must remain responsive during rapid dragging, dense envelopes, repeated loop playback and high project load. Safe update handling should prevent incomplete envelope states from reaching the audio engine.

**6. Undo, cancellation and editing transactions**

Treat one continuous drag as one undoable edit, rather than creating an undo entry for every intermediate pointer movement.

Where consistent with Nota’s interaction model:
- Escape during an active drag should cancel that edit and restore its starting state.
- Releasing the point should commit the current result without a second, unexpected audible change.
- Undo and redo should restore the corresponding envelope reliably, including during playback.

**Goal**

Make envelope editing fast, predictable and comfortable with a mouse or trackpad, while providing the continuous auditory feedback needed for precise sound design.

These are improvements to the existing editing workflow and responsiveness, rather than a request for a separate envelope system.
