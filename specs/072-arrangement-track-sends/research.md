# Research: Send levels in the arrangement track headers

## R1 — How do sends become undoable?

- **Decision**: a new value-undo kind `MixKind::TrackSend` (ABI `NOTA_MIX_TRACK_SEND = 8`,
  bus in `device_index`), used through the existing gesture API (`nota_mix_gesture_begin/end`).
- **Rationale**: send levels are atomics outside the graph, exactly like volume/pan; a
  graph snapshot would not restore them. The gesture mechanism already gives one step per
  drag, no step for a no-change gesture, merging with automation-record checkpoints and
  skipping a step whose track is gone (specs/009).
- **Alternatives**: a structural snapshot per send change (wrong — atomics aren't in it, and
  a snapshot per drag tick bloats history); value 7 (taken by the internal `DeviceNotes`).

## R2 — How do sends become automatable?

- **Decision**: `AutomationTarget::Send = 5`, one lane per bus (`deviceIndex` = bus), played
  in `applyAutomation` by `Track::setSend`.
- **Rationale**: mirrors Volume/Pan (track-mix values written from the block-rate automation
  pass), so touch / latch / override / record and persistence work through the existing
  code paths with small switches. Lane storage already carries `deviceIndex`, and the
  project file saves target + device index generically, so no format bump is needed.
- **Alternatives**: four fixed targets Send A–D (5–8): clutters the enum and every switch;
  routing a send through a hidden utility device param: indirect, and breaks the Mixer sync.

## R3 — What happens to send lanes when a return is deleted?

- **Decision**: extend `compactReturns`: lanes to the deleted bus are dropped, lanes to later
  buses take the new bus index — in the same cloned track that remaps the send levels.
- **Rationale**: keeps a lane on the return it was drawn for; the delete is already one
  structural step, so undo restores lanes and levels together.

## R4 — Do send lanes follow clips moved across tracks?

- **Decision**: yes, like Volume/Pan (they are track-mix lanes, not tied to a device).

## R5 — Header layout and size

- **Decision**: a 24 px row of inline knobs (`Knob { Inline = true }`), each cell knob +
  letter over a mono percent; cells fall back to knob only when the column is too narrow.
  Standard rows show it from 88 px; large rows always (volume and pan then share a row so
  the 128 px preset fits); compact never. A per-project "Sends" toggle (default on) in the
  header column's top strip, hidden without returns, saved in `track-view.json`.
- **Rationale**: the inline knob is the design system's table-row knob; four mini faders
  don't fit 160 px with labels. Height thresholds follow the specs/015 tier logic (a
  layout shows when it fits). View state belongs in the sidecar with row heights.
- **Alternatives**: a Preferences option (rejected — row heights are per project, so is
  this); auto-growing rows when sends are on (rejected — changes the user's layout).

## R6 — One wiring for every send control

- **Decision**: a small `SendControl` helper (gesture + automation write + live follow
  guard) used by the header, Mixer window, Session mixer and Sends card; MCP `set_send` uses
  `MixEdit.Step`.
- **Rationale**: the spec requires the same undo/automation behaviour from every surface;
  one helper keeps them from drifting.
