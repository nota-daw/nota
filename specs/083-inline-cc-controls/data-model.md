# State and data

No new persisted entities or MIDI formats. ExtraControllerLane retains controller,
slot, row and container; height keys remain `extra:{slot}:cc:{controller}`. The primary
lane retains its mode-specific height key. Captions derive from ControllerLabel.

Selection resets the target gesture and updates that view only. Removal resets the
gesture and removes the view, leaving clip data and saved height entries intact.
Primary removal selects Velocity. Pending menu callbacks validate cache generation
and membership. Scale state derives from the lane controller.
