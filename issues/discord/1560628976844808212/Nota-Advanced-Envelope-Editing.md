**Advanced Envelope Editing – Multi-Point Selection, Transformations and Independent Reset**

Nota’s sample envelope editor currently appears to lack important multi-point editing capabilities. In my workflow, I cannot select all existing points and transform them together, and I cannot find a straightforward way to reset the Volume and Panning envelopes independently.

Expand the editor into a comprehensive, precise and keyboard-friendly envelope-editing workflow. Ableton Live and Bitwig Studio can serve as workflow references, including relevant publicly documented beta improvements where useful. The requirements below describe the requested Nota behavior; they do not imply that every listed operation exists in either reference application.

**1. Flexible point and time-range selection**
- Select individual points, multiple points or all editable points.
- Support Cmd+A on macOS and Ctrl+A where appropriate, scoped to the focused envelope editor.
- Provide marquee/rubber-band selection.
- Support Shift-click and an appropriate platform-specific additive/toggle-selection modifier without conflicting with context-menu gestures.
- Select points within a defined time range.
- Add points to a selection, remove points from it, invert the selection and deselect all.
- Clearly distinguish selected points, selected time ranges and the active envelope.
- Preserve a selection while zooming or scrolling.

**2. Group movement and transformations**
- Move selected points horizontally, vertically or along a constrained axis.
- Preserve relative timing and values during group movement.
- Scale horizontally to stretch or compress timing.
- Scale vertically to expand or compress the value range.
- Stretch from either edge or around a configurable anchor, such as the first point, last point, selection center or a custom position.
- Support skew and tilt transformations with a preview of their effect.
- Mirror in time within the selected range and flip/invert values around a clearly defined reference.
- Distinguish value inversion from audio polarity inversion.
- Apply relative offsets or set absolute values to multiple points.
- Provide numerical editing for group offsets, scale factors, anchors and point values.
- Support fine adjustment, axis constraints and a temporary snap override.
- Snap to the grid, nearby points and meaningful positions such as sample or loop boundaries.

**3. Predictable transformation limits**
- Keep points inside the valid time and parameter ranges.
- Define how the editor handles points that would overlap, cross or reach a boundary.
- Prefer limiting a group transformation as a whole when individual clamping would unexpectedly distort its shape; make any alternative behavior explicit.
- Avoid silently losing or merging points.
- Show the transformation anchor and affected range while editing.
- Make the value domain clear: scaling in decibels and scaling linear gain are different operations. Panning transformations should use an explicit center or selected anchor.

**4. Curve and shape editing**
- Support straight, stepped and curved segments.
- Adjust curve tension and curvature directly.
- Convert interpolation types for individual or multiple selected segments.
- Insert predefined ramps, sine, triangle, square and ADSR-style shapes.
- Fit generated shapes to a chosen range, with an explicit choice to replace or combine with existing points.
- Smooth or simplify dense envelopes with adjustable strength or tolerance and a preview.
- Preserve important endpoints and allow selected points to be protected during simplification.
- Draw points freehand and manipulate existing segments directly.
- Keep interpolation and curve handles predictable after moving, copying or transforming points.

**5. Clipboard and standard editing**
- Copy, cut, paste, duplicate and delete selected points.
- Duplicate a selected region or an entire envelope.
- Paste at a chosen time position while preserving relative spacing and relevant curve information.
- Define how pasted points interact with existing points: merge or replace, with a clear default.
- Warn or explain when copying between incompatible parameter types requires conversion.
- Support standard platform-specific keyboard shortcuts and an intuitive context menu.
- Expose relevant commands through Nota’s Command Palette and shortcut settings.

**6. Independent envelope reset**
- Provide a clearly accessible Reset Volume Envelope command.
- Provide a separate Reset Panning Envelope command.
- Reset only the chosen envelope, leaving the other envelope unchanged.
- Remove custom points and restore the parameter’s defined default state, retaining any structurally required endpoints.
- Show what that default means rather than treating “empty,” “zero” and “default” as interchangeable.
- Offer Reset All Envelopes as a distinct, clearly labelled operation.
- Keep Clear Selected Points separate from resetting an envelope.
- Where supported, reset only a selected time range while preserving the envelope outside it.
- Handle range-edge continuity explicitly, retaining or inserting boundary points when needed.
- Make every reset undoable.

**7. Numerical precision and navigation**
- Display exact point values, units and time positions.
- Support direct numerical entry and distinguish mixed values in multi-point selections.
- Provide appropriate time displays, such as musical time or elapsed time, with sample-level precision where applicable.
- Support precise zooming, scrolling and editing of tightly spaced points.
- Provide practical hit targets and clear hover/focus feedback.
- Keep mouse, trackpad and keyboard workflows consistent.
- Explain modifier gestures through discoverable tooltips or help.

**8. Continuous real-time audio feedback**
- Apply edits continuously while dragging, including during repeated loop playback, without waiting for pointer release.
- Keep playback running without interruption or manual retriggering.
- Make changes audible as playback reaches the affected part of the envelope; edits cannot change audio already played.
- Keep the visual curve and the active audio-processing state consistent.
- Avoid clicks, glitches and unnecessary latency through real-time-safe updates and parameter-appropriate transitions.
- Preserve intentional sharp shapes rather than applying indiscriminate smoothing.
- Clearly identify any parameter-specific limitation.

**9. Undo, redo and cancellation**
- Support undo/redo for every point, curve, clipboard, transformation and reset operation.
- Treat one continuous drag or transformation as one undo step.
- Allow Escape to cancel an active edit where consistent with Nota’s interaction model.
- Preserve unaffected points, the other envelope and unrelated sample settings.
- Save and reload the edited envelope and its interpolation accurately.

**Goal**

Provide a complete, modern envelope-editing workflow rather than only basic multi-selection. Selecting and transforming any number of points together, hearing changes during editing, and independently resetting Volume and Panning should be fundamental capabilities.

The aim is a workflow comparable in breadth and precision to advanced DAW automation editors, while remaining understandable, discoverable and comfortable for everyday sample editing.
