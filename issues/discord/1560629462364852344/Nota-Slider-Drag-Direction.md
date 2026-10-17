**Match Slider Drag Direction to Its Visual Orientation**

In the Clip editor’s ADSR section, the Attack, Decay and Sustain controls are displayed as horizontal sliders. In my current experience, however, I have to drag vertically to change their values, while the slider handles move horizontally.

This feels unintuitive, particularly with a MacBook trackpad: the gesture and the visible movement follow different axes. There may be a reason for the current behavior, but an orientation-matched interaction would feel more direct.

**Suggested behavior**
- Allow horizontal sliders to be adjusted by dragging left and right.
- Dragging right should increase the value; dragging left should decrease it.
- Keep vertical sliders vertically draggable.
- Preserve each parameter’s existing range, units and appropriate value scaling.
- Ensure that grabbing a handle starts from its current value without an unexpected jump.
- Preserve the existing double-click-to-reset behavior shown in the ADSR editor.

**Optional interaction preference**
If vertical dragging is intentional or preferred by some users, offer a setting such as:
- Follow slider orientation.
- Vertical dragging.

The chosen behavior should be consistent across comparable controls. Avoid silently combining both axes in a way that makes diagonal movement unpredictable.

**Precision and usability**
- Support fine adjustment through a clearly documented modifier.
- Show the current value and units while editing.
- Provide clear hover feedback and a cursor that reflects the active drag direction.
- Keep hit targets comfortable for mouse and trackpad use.
- Preserve keyboard adjustment and direct numerical entry where available.
- Make a completed drag one undoable action.

**Goal**
Make slider interaction match what the interface visually suggests, without removing an established alternative for users who prefer it. Start with the horizontal ADSR sliders and review other comparable controls for the same inconsistency.
