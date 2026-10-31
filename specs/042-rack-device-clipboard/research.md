# Research: Move, copy and paste devices in rack chains

Findings from the code as it stands on `feat/rack-nested-instruments` (after specs/040 and 041).

## D1 — Why moving a rack chain device doesn't work today

- **Decision**: Replace the rack lane's grip-only reorder with header drag and a drop bar, matching the track chain.
- **Rationale**: `RackCard.Chains.cs` `RcGripReorder` hangs the move on the 8-px grip glyph in the card footer. It shows no drop bar and moves by `round(dx / (card width + 14))` steps on release. The grip is easy to miss and gives no feedback, which matches the report ("can't move effects"). The track chain (`DeviceChainView.Clipboard.cs` `HookDeviceDrag`) drags by the header, dims the card, shows `_devDropBar` at the gap under the pointer, and moves on release by the gap index.
- **Alternatives considered**: keeping the grip and adding a drop bar. Rejected: the header is the handle on the track chain; two handles would differ for no gain. The grip glyph stays as a visual cue and also starts a drag.

## D2 — Engine primitives that already exist

- **Move within a chain**: `RackCore::moveChainDevice` and `moveChainMidiDevice` (rack fork, undoable) already remap mappings by index. ABI: `nota_rack_move_chain_device`, `nota_rackdev_move_chain_device`, `nota_rack_chain_midi_move`, `nota_midirack_chain_device_move`. US1 within a chain needs no engine work.
- **The clipboard**: the engine owns one device clipboard: `Engine::clipDevice_` (an audio effect clone) / `clipMidi_` (a MIDI effect clone), filled by `copyDevice` / `copyMidiEffect` and read by `pasteDevice` / `pasteMidiEffect`, with `nota_device_clipboard_kind`. It holds full clones (`cloneDevice` copies params, state blob, bypass, fold and sidechain; hosted plug-ins and racks clone themselves).
- **Duplicate on the track chain**: `duplicateDevice` / `duplicateMidiEffect` exist in the engine and `IAudioEngine` but no UI uses them.
- **Mappings on remove**: `removeChainDevice` / `removeChainMidiDevice` drop the device's mappings and shift the rest. Undo restores them because the whole rack forks.

## D3 — One clipboard for track and rack chains

- **Decision**: Rack copy writes the same `clipDevice_` / `clipMidi_`, and rack paste reads them. New ABI: `nota_rackdev_chain_device_copy` / `_paste`, `nota_rackdev_chain_midi_copy` / `_paste`, addressed like other `rackdev` calls (−1 Instrument Rack, ≥0 Audio Effect Rack, −1000 − mi for a MIDI Effect Rack (the engine's `kMidiRackBase` address), `NOTA_RACK_NESTED_BASE − chain`).
- **Rationale**: FR-009 asks for one shared clipboard. Reusing the engine's means the track chain's existing ⌘V already pastes a rack copy (and the other way round) with no new managed state. The managed `DeviceChainView._clip` (`DeviceClip`) only carries preset labels for the A/B extras; it gains a "came from a rack" flag so Paste is enabled.
- **Alternatives considered**: a managed clipboard holding a preset document. Rejected: it loses state that isn't in params (hosted plug-in state, Arp lanes beyond params) and duplicates the engine's clone logic.

## D4 — Paste at a position, and mappings

- **Decision**: New `RackCore::insertChainDeviceInstance(chain, dev, at)` / `insertChainMidiDeviceInstance(chain, dev, at)`. They insert the clone and shift mappings at or after `at` up by one, like `insertDeviceClone` remaps automation on the track. A pasted device has no mappings (FR-011).
- **Rationale**: `addChainDeviceInstance` only appends. Add-then-move would make two forks (two undo steps) unless grouped, and is harder to reason about.

## D5 — Moving to another chain (drag onto a chain row)

- **Decision**: New `RackCore::moveChainDeviceToChain(fromChain, idx, toChain)` and a MIDI twin, in one fork. They take the device object out of `fromChain`, append it to `toChain`, rewrite the device's own mappings to `(toChain, newIdx)`, and shift `fromChain`'s later mappings down. ABI: `nota_rackdev_chain_device_move_to` / `nota_rackdev_chain_midi_move_to`.
- **Rationale**: FR-003a needs mappings to follow (FR-012). Cut and paste would drop them, so a real move is needed.
- **Audio-thread note**: the instance moves between chains in a new snapshot. Instrument Rack MIDI chains are reconciled by `syncChains` (specs/040 review fix): a MIDI effect that changes chain changes both chains' signatures, so held notes there end cleanly.

## D6 — Refusals and the sidechain

- **Decision**: Paste returns the new index or a negative reason code:
  - −2: the clipboard is empty, or holds the wrong family for the section;
  - −3: a rack inside a rack (`RackDevice` kind 5, `MidiRack` kind 6);
  - −5: a MIDI effect past chain 16 of an Instrument Rack.
  A sidechain key is cleared on the clone pasted into a chain (`setSidechainSourceTrackId(−1)`). `nota_device_clipboard_sidechain(e)` tells the UI beforehand so the status line can say the sidechain was dropped (FR-015).
- **No −4.** Every rack the clipboard reaches (Instrument, Drum, Audio Effect) hosts plug-ins and a MIDI Effect Rack has no audio section (−2), so the planned "can't host a plug-in" code never arises; it was dropped.
- **Rationale**: the engine is where the clone's type is known; the codes map 1:1 to the spec's status messages. Chain devices have no sidechain input (the chain device proxy already reports `DeviceAcceptsSidechain` false).

## D7 — Selection and keys

- **Decision**: `DeviceChainView` owns one selection that is either a track-chain device (as now: `_selChainKind` / `_selDeviceIndex`) or a rack chain device: a new `RackDeviceSel(int Rack, int Chain, bool Midi, int Index)` set through `DeviceCardContext.SelectRackDevice`. `OnKeyDown` routes ⌘C / ⌘X / ⌘V / ⌘D / Delete to the rack commands when a rack device is selected. Selecting either kind clears the other (FR-005).
- **Rationale**: Keys already reach `DeviceChainView.OnKeyDown`, and the rack card is built inside it. One owner keeps "paste goes where you last clicked" simple.
- **Paste with no device selected in the chosen chain**: the chain row's menu and the lane's "+" target menu get **Paste** (at the end of the matching section), so FR-008's "end of the chosen chain" is reachable without a selection.

## D8 — ⌘D

- **Decision**: ⌘D in `DeviceChainView.OnKeyDown` duplicates the selected device (track or rack) while the panel has focus, and its menu gains **Duplicate**. `MenuKit` gets a `DuplicateKey` hint; Preferences → Shortcuts lists "⌘D Duplicate the selected device" in the Devices group.
- **Check**: ⌘D elsewhere (piano roll duplicate notes, arrangement) is handled by those views while they have focus. The device panel handles only its own focus, so there is no conflict.
