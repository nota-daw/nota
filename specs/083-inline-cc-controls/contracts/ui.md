# Lane UI contract

- One `AddControllerLane` button lives below primary sidebar entries.
- Controller shells expose `ControllerPicker` and `RemoveControllerLane` buttons;
  picker uses ControllerLabel, caret glyph, and existing picker menu.
- `ControllerValueScale` occupies the matching left gutter, ignores pointer input,
  and aligns values with graph mapping below its header.
- Graph x-origin remains note grid x-origin through fold/zoom/scroll.
- Headers never route pointer presses into ControllerLane; they are siblings.
- Existing resize dividers and settings keys remain authoritative.
