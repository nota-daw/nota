# Research: The instrument as a first-class device in the chain

Findings from the code on `develop` (a85331b), one decision per unknown in the plan's
Technical Context.

## R1. Copying an instrument with its full state

- **Decision**: Reuse `Engine::cloneInstrument(const Track&)` (Engine_Devices.cpp), the clone
  `duplicateTrack` / `deepCloneTrack` already use, and keep the result on the shared device
  clipboard as a third family: `clipInstrument_` beside `clipDevice_` / `clipMidi_`;
  `deviceClipboardKind()` returns 3 for it. Bypass is copied as `deepCloneTrack` does.
- **Rationale**: "Duplicate track keeps every built-in instrument's params + state" is already
  covered by `DeviceCopyTests.DuplicateInstruments`; racks and hosted plug-ins clone themselves
  through `Instrument::clone()`. One clipboard keeps the rule from #238 / specs/042 that a new
  copy replaces whatever was copied before.
- **Alternatives considered**: A managed-side state blob round-trip (`get_state` / `set_state`)
  — rejected: built-in synths keep part of their state in params, racks in children, and
  `cloneInstrument` already gets all of it right.

## R2. Replacing a track's instrument (Paste, Replace…, Session drop, move target)

- **Decision**: One engine path, `Engine::replaceTrackInstrument(trackId, std::shared_ptr<Instrument>)`:
  clone the track, swap `instrument`, close the old instrument's editor, `republishWithTrack`
  (one undo step), `recomputePdc`. `setTrackBuiltinInstrument`, `setTrackInstrumentPlugin` and
  the new paste call go through it. `setTrackBuiltinInstrument` also accepts kinds 3 / 4 and
  builds an empty `RackInstrument` / `DrumRack` the way `addInstrumentRackTrack` /
  `addDrumRackTrack` do, so **Replace… → Instrument Rack / Drum Rack** works.
- Managed: `DeviceInsertService.CanReplaceInstrument` refuses rack tracks today ("never swap an
  Instrument / Drum Rack in place") because a Browser drop on a rack adds a chain / pad. That
  stays the rule for **drops**; Replace…, Paste and a move target pass a new
  `InsertPlacement.ReplaceInstrument` that swaps even a rack.
- **Automation on the replaced instrument**: unchanged from today's Browser replace — lanes stay
  on the track (a built-in → built-in of the same kind keeps working; others read nothing). A
  moved instrument's own lanes are handled in R4.
- **Alternatives considered**: Building replace in managed code from remove + add — rejected:
  it would be two snapshots and lose the track's place in the chain.

## R3. Deleting the instrument = deleting the track

- **Decision**: Delete / Remove control / Cut on the instrument call the Arrangement's existing
  `DeleteTracks` path (`ArrangementView.Tracks.cs`, engine `remove_tracks`), reached through a
  new `DeviceChainView.TrackDeleteRequested` event that MainWindow wires to the Arrangement.
  Before it, a `ConfirmWindow` ("Delete track" / "Delete" / "Cancel") when the track has clips
  (arrangement or Session).
- **Rationale**: Clarification Q1. No new engine state; the selection and refresh logic of
  `DeleteTracks` stays the one place that knows how to drop a track.
- **Note**: Delete Track from the header menu stays unconfirmed (out of scope; the
  confirmation is for the device panel route, where removing a track is less expected).

## R4. Moving a device to another track in one undo step

- **Decision**: New engine call `transferDevice(srcTrack, family, index, dstTrack, atIndex,
  keepSource)` that builds **one** new graph with both tracks changed and publishes it once:
  - a move hands the device object itself over (a plug-in that won't load twice still moves, a
    Reverb keeps its tail); a copy, or a device whose settings must change on the way (a key on
    the target, an Arp's CC target), is cloned so the object in undo history is never edited;
  - audio effect / MIDI effect: insert on the target
    at `atIndex` (−1 = end), and unless `keepSource` remove it from the source
    (`remapAutomationAfterDeviceChange` + `remapCvLinksAfterDeviceChange` on the source, the
    same calls `removeDevice` makes);
  - the instrument: replace the target's instrument (R2), and unless `keepSource` drop the
    source track from the graph (as `removeTrack`, editors closed);
  - automation: when moving (not copying), the source lanes that target the device
    (`DeviceParam` / `PluginParam` with its index, `MidiDeviceParam`; for the instrument
    `PluginParam` with `deviceIndex < 0` and the instrument's params) move to the target with
    `deviceIndex` rewritten to the new position; the target's lanes after the insert point shift
    as in `insertDeviceClone`;
  - a sidechain key whose source is the target track is cleared (the device would key off
    itself); result flags tell the UI which status note to show;
  - CV links and rack macro mappings to the device stay behind (they belong to the source track)
    — `remapCvLinksAfterDeviceChange` drops them as it does for a removed device.
- **Rationale**: A managed undo group around copy + paste + remove would work for effects, but
  could not carry automation lanes, and two publishes of a moving instrument would briefly
  sound it on two tracks. One publish is atomic for the audio thread (AR-5).
- **Alternatives considered**: Managed `BeginUndoGroup` + existing calls — rejected for the
  reasons above; a dedicated "swap" — not needed (clarification Q2: replace).

## R5. Dragging a device card onto a track header

- **Decision**: Keep the in-panel drag as it is (pointer capture, drop bar). When the pointer
  leaves the device panel's bounds during a header drag, hand over to a system drag:
  `DragDrop.DoDragDropAsync` with the single native text sentinel and the payload in a static,
  the pattern `BrowserView.DragItemAsync` uses (one pasteboard item, so macOS's NSDragging
  session doesn't crash). The press args are kept from `PointerPressed`, as BrowserView does.
  A new `DeviceDrag` static (`Current`: track, family, index) is what targets check.
- Targets: Arrangement — `OnLaneDragOver` / `OnLaneDrop` (rows, with the existing
  `_headerDropRing`) and the footer (returns); Session — `ColumnHeader` and the slot grid of a
  column. A target lights only when it can take the family (instrument / MIDI effect →
  instrument tracks; audio effect → any track with a chain) and isn't the source track.
  ⌥ (Alt) on drop → `keepSource` (copy).
- **Rationale**: Avalonia's DnD is the only way across views; the in-process static avoids a
  custom data format (the macOS crash noted in BrowserView).
- **Alternatives considered**: Hit-testing the whole window during a captured pointer drag —
  rejected: the Arrangement / Session would need their own hover code paths for a second kind
  of drag.

## R6. Session view instrument drops

- **Decision**: `OnSessionDrop`'s `default` branch calls a new `DeviceInsertService.DropOnSession(item,
  trackId)`: on a plain instrument track → `TryReplaceInstrument`; on a rack → a chain / pad
  (same as `RouteToTrack` for racks); otherwise a new instrument track placed right after the
  column (`PlaceNewTrack`) or at the end when `trackId <= 0`. Presets keep their current routing
  (`ApplyPreset` with `InPlace`).
- Targets: the existing slot-grid handler (`SessionView.Grid.cs:358`), plus new drop handlers on
  `ColumnHeader` and on the empty area right of the last column (trackId = −1). The hover look is
  the grid's existing `_dropHover` wash; the header uses the same ring the Arrangement shows.
- The "Drop instruments onto the arrangement." strings in `MainWindow.Dnd.cs` and
  `DeviceInsertService.cs:295` go away.

## R7. The Remove control

- **Decision**: An 18×18 icon button (`GlyphKind.Close`, `TextTertiary` → `TextPrimary` on hover — not
  `Danger`: DESIGN.md keeps red for overload and makes a destructive key an ordinary one,
  tooltip "Remove device" / "Remove track" on the instrument) in the card header to the right of
  the power slot, built once in `BuildCardShell` (`DeviceChainView.Shell.cs`) and in
  `FoldedStrip` (`DeviceChainView.Fold.cs`), so built-ins, plug-ins and racks get it from the same
  code (`DeviceCardKit.RemoveButton`). It is `Focusable`, activates on Enter / Space, and is
  skipped for `Utility` cards. The rack chain compact cards get the same control; the legacy
  `SlotDelBtn` now returns it.
- **Rationale**: One place, every card type (SC-002). `DESIGN.md`: the accent stays for live /
  chosen state, so the control is neutral until hovered.

## R8. MCP

- **Decision**: New tools `set_track_instrument(trackId, kind)` (built-in replace, racks
  included — plug-ins keep `set_track_instrument_plugin`), `copy_instrument` / `paste_instrument`
  are not added (the AI has no clipboard workflow); `move_device_to_track(trackId, chain:
  "audio"|"midi"|"instrument", index, targetTrackId, atIndex = -1, copy = false)`. Deleting the
  instrument is `remove_track` (documented in the tool description).

## R9. Tests

- **Decision**: A new smoke file `tests/Nota.SmokeTest/InstrumentChainTests.cs` (run from
  `Program.cs` like `DeviceCopyTests`, also alone with `--instrument-chain`): instrument copy →
  paste across kinds keeps params; replace to and from racks; move audio effect / MIDI effect
  with an automation lane; move instrument removes the source and replaces the target, one undo
  restores both; `keepSource` leaves the source and carries no lanes; sidechain cleared when the
  target is the key. UI behaviour is checked in the app (quickstart).
