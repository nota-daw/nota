**MIDI Bindings for Actions in Settings > Shortcuts**

Please extend Settings > Shortcuts so every applicable action can be triggered from a MIDI controller as well as a computer keyboard. MIDI should invoke the action directly, rather than simulate a keyboard shortcut.

**1. One action list for keyboard and MIDI**
- Keep the existing action names, categories, search and keyboard assignments.
- Add a MIDI binding area beside each action, showing both the controller/input-port name and the complete MIDI command.
- Example: “Foot Controller A · USB MIDI In · Ch 1 · CC 20 · Rising above 63”.
- Support keyboard and MIDI bindings simultaneously, including multiple MIDI bindings for one action.
- Show unassigned, assigned, disabled, disconnected and conflicting states clearly.
- Identify any actions that cannot safely support MIDI and explain the restriction rather than silently ignoring the assignment.

**2. MIDI Learn and manual configuration**
- Select Learn beside an action, optionally restrict it to a chosen input device, operate the desired controller, then review and confirm the captured assignment.
- Show the detected device, input port, MIDI channel, message type, number and trigger condition before saving.
- Allow manual entry and editing, plus cancellation with Escape without replacing the existing mapping.
- Prevent the learned input from triggering unrelated mapped actions while learning. Do not globally disrupt unrelated musical MIDI input.
- Filter incidental MIDI clock, active sensing and other background messages out of ordinary learning.

**3. Useful message and trigger support**
- Support MIDI notes, Control Change and Program Change for discrete commands.
- Allow explicit press/release, Note On/Note Off and threshold-crossing behavior where appropriate. Treat Note On with velocity zero correctly as Note Off.
- Support channel-specific assignments, with an explicit “Any Channel” option.
- Avoid firing a command twice from a single button press and release, or repeatedly from a continuous CC stream.
- Provide adjustable thresholds/hysteresis and optional retrigger protection where useful.
- Support momentary and toggle behavior only where meaningful for the action; distinguish “Toggle”, “Enable” and “Disable” commands where available.
- For compatible continuous actions, optionally support absolute/relative CCs, ranges, inversion and higher-resolution messages. Do not force parameter-mapping complexity onto simple command bindings.

**4. Multiple controllers and reliable identification**
- Bind to a specific controller and input port by default, using a stable device identifier where available; the displayed name alone should not determine identity.
- Allow friendly aliases and distinguish identical controller models or multiple ports.
- Preserve mappings when a controller disconnects and show it as offline. Safely release held momentary actions so they cannot remain stuck.
- Restore mappings on reconnection when the identity can be matched reliably. If identity is ambiguous, offer explicit reassignment rather than silently binding the wrong device.
- Offer “Any Device” only as an explicit choice.

**5. Conflict handling and scope**
- Detect conflicts using device/port, channel, message, trigger condition and overlapping action contexts.
- Show the conflicting action and let users cancel, reassign or intentionally create a multi-action binding where supported.
- Never silently overwrite another assignment or execute multiple actions unexpectedly.
- Distinguish application-wide bindings from context-sensitive commands and clearly explain which editor or selection a command targets.
- Make transport and other global actions reliable across normal editor focus changes, while respecting modal safety restrictions.
- Where the same input could also play an instrument, allow a clear consume/pass-through policy without disrupting other MIDI routes.

**6. Editing, visibility and recovery**
- Enable, disable, remove or edit individual bindings without changing the keyboard shortcut.
- Search/filter by action, controller, message, assigned/unassigned state and conflicts.
- Provide a small activity indicator or test feedback so users can verify which input was received and which action it triggered.
- Make mapping edits undoable. Confirm bulk deletion/reset and keep keyboard resets separate from MIDI resets.
- Support named mapping profiles and import/export. Clearly distinguish global controller preferences from project-specific mappings; loading a project must not silently overwrite global bindings.
- Avoid destructive actions during testing unless explicitly confirmed.

**7. Performance and optional feedback**
- Process command triggers responsively without blocking the audio thread or causing playback interruptions.
- Use the existing action system’s scheduling rules for commands that require musical quantization or safe deferred execution.
- Optionally provide action-state feedback to controllers with LEDs or displays when supported, using explicitly configured output ports and loop prevention.

The goal is a unified, readable command-assignment interface: every applicable action can have keyboard shortcuts and clearly identified MIDI controls, with safe conflict handling and dependable multi-controller operation.
