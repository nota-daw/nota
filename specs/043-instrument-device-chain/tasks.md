---

description: "Tasks for spec 043: the instrument as a first-class device in the chain"
---

# Tasks: The instrument as a first-class device in the chain

**Input**: Design documents from `specs/043-instrument-device-chain/`:
- [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md), [data-model.md](data-model.md);
- the contracts [c-abi.md](contracts/c-abi.md), [ui.md](contracts/ui.md) and [mcp.md](contracts/mcp.md);
- [quickstart.md](quickstart.md).

**Tests**: The smoke test is the project's gate (constitution IV) and the plan asks for
`InstrumentChainTests`. Every new C ABI call gets smoke checks, written before its engine
implementation. UI is verified in the app per quickstart.md.

**Organization**: by user story — US1 select / replace / delete the instrument (P1), US2 the
Remove control (P1), US3 Session instrument drops (P2), US4 move a device to another track (P2).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1 / US2 / US3 / US4

---

## Phase 1: Setup

- [X] T001 Create `tests/Nota.SmokeTest/InstrumentChainTests.cs`:
  - the AGPL-3.0-only SPDX two-line header and a file comment listing what it covers (follow `DeviceCopyTests.cs`);
  - `internal static class InstrumentChainTests` with `public static IEnumerable<(bool ok, string label)> Run()` that chains empty `Replace()`, `Clipboard()`, `Transfer()` sections;
  - in `tests/Nota.SmokeTest/Program.cs`: run it after `DeviceCopyTests.Run()` in the full suite (both call sites, lines ~435 and ~15140) and add a `--instrument-chain` mode next to `--device-copy` (line ~432).

---

## Phase 2: Foundational (engine + interop; blocks every story)

**Purpose**: the replace path, the instrument clipboard and the cross-track transfer of
contracts/c-abi.md, mirrored in `IAudioEngine`.

### Smoke checks first

- [X] T002 [P] In `InstrumentChainTests.Replace()` (`tests/Nota.SmokeTest/InstrumentChainTests.cs`): on a Volt track with a MIDI clip and an EQ-8, `SetTrackBuiltinInstrument(t, 3)` gives kind 3 with 0 chains, then `4` gives kind 4, then back to Volt (6); clip count, device chain and track count unchanged after each; each is one undo step (`Undo()` returns the previous kind); kind 3 / 4 on an audio track returns false.
- [X] T003 [P] In `InstrumentChainTests.Clipboard()`: for each built-in kind in `{ 0, 2, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17 }` (as `DeviceCopyTests.InstrumentKinds`) set a few params off default on track A, `CopyInstrument(A)`, `DeviceClipboardKind() == 3`, `PasteInstrument(B)` on a Synth track → B's kind and every param equal A's; the clipboard still pastes after `RemoveTrack(A)`; `PasteInstrument` on an audio track returns −2; with an effect copied it returns −1; an Instrument Rack with two chains and a macro mapping, and a Drum Rack with a pad sample, paste with chain / pad count and mapping count equal; one undo restores B's own instrument; copying an audio effect afterwards makes the kind 1 again.
- [X] T004 [P] In `InstrumentChainTests.Transfer()`:
  - audio effect: Reverb on track 1 with a Mix automation lane (2 points) and an EQ-8 after it → `TransferDevice(1, Audio, 0, 2, -1, keepSource:false)` returns track 2's last index; track 1 has only the EQ-8 and its lanes on the EQ are re-indexed to 0; track 2 has the lane on the new index with the same points; one `Undo()` restores both tracks exactly (device kinds, lane targets, points);
  - copy: the same with `keepSource:true` → track 1 unchanged, track 2 has the reverb and no new lane;
  - MIDI effect: Arp moved between instrument tracks; to an audio track → −2;
  - instrument: Volt (params set, one instrument-param lane) on track 1 with a clip, Keys on track 2 → transfer family 2 → track 2 is Volt with the same params and the lane, track 1 is gone, flags bit 2 set; one undo restores both tracks with track 1's clip; instrument onto an audio track → −2, nothing changed;
  - sidechain: a Compressor on track 2 keyed from track 3, moved to track 3 → flags bit 0 set and its sidechain source is −1;
  - `dst == src` → −1.

### Engine

- [X] T005 Add the instrument clipboard to `src/native/nota.engine/src/engine/Engine.h`: `std::shared_ptr<Instrument> clipInstrument_;` beside `clipDevice_` (line ~1301), and declarations `bool copyInstrument(int32_t trackId)`, `int32_t pasteInstrument(int32_t trackId)`, `bool replaceTrackInstrument(int32_t trackId, std::shared_ptr<Instrument> inst)`, `int32_t transferDevice(int32_t src, int32_t family, int32_t index, int32_t dst, int32_t atIndex, bool keepSource, int32_t* outFlags)` with comments quoting contracts/c-abi.md.
- [X] T006 In `src/native/nota.engine/src/engine/Engine_Devices.cpp`, add `replaceTrackInstrument`: find the authoring track, refuse unless `TrackType::Instrument`, close the old instrument's editor, `cloneTrack`, set the instrument (sample rate set), `republishWithTrack`, `recomputePdc`. Route `setTrackBuiltinInstrument` and `setTrackInstrumentPlugin` through it.
- [X] T007 In `Engine::setTrackBuiltinInstrument` (`Engine_Devices.cpp:733`), accept kind 3 → `std::make_shared<RackInstrument>()` and kind 4 → `std::make_shared<DrumRack>()`, sample rate set as in `addInstrumentRackTrack` / `addDrumRackTrack` (`Engine_Rack.cpp:72–95`); update the `nota_track_set_builtin_instrument` comment in `include/nota/nota_engine.h` (line ~1388) to say "kind 3 = an empty Instrument Rack, 4 = an empty Drum Rack".
- [X] T008 In `Engine_Devices.cpp` (the #238 clipboard section, line ~760): `copyInstrument` stores `cloneInstrument(track)` with bypass copied (as `deepCloneTrack`, `Engine_Tracks.cpp:3105`) and resets `clipDevice_` / `clipMidi_`; `copyDevice` / `copyMidiEffect` reset `clipInstrument_` too; `deviceClipboardKind()` returns 3 for an instrument; `pasteInstrument` returns −1 with no instrument on the clipboard, −2 for a non-instrument track, −3 when the clone fails, else clones the clipboard (a temporary `Track` holding `clipInstrument_` passed to `cloneInstrument`, or a helper `cloneInstrumentFrom(const Instrument&)` factored out of `cloneInstrument`) and calls `replaceTrackInstrument`, returning 0. Check the rack copy paths (`RackDeviceClipboard`, specs/042) that reset `clipDevice_` / `clipMidi_` also reset `clipInstrument_`.
- [X] T009 In `src/native/nota.engine/src/engine/Engine_Automation.cpp`, add a helper `std::vector<AutomationLane> takeDeviceLanes(Track& src, int32_t family, int32_t index)` that removes and returns the lanes on that device — family 0: `DeviceParam` or `PluginParam` with `deviceIndex == index`; 1: `MidiDeviceParam` with `deviceIndex == index`; 2: `PluginParam` with `deviceIndex < 0` and any other instrument-param target the track uses — keeping whatever clip-envelope / lane-index bookkeeping `remapAutomationAfterDeviceChange` (line 509) keeps consistent, then shifting later lanes down as that function does for a removal.
- [X] T010 In `Engine_Devices.cpp`, implement `transferDevice` per research R4 and the "Device transfer" table in data-model.md, building **one** `Graph` (`inheritGraphLevel`, replace both tracks; drop the source track for an instrument move) and calling `publish` once, then `remapCvLinksAfterDeviceChange` for the source and `recomputePdc`:
  - return −1 for an unknown track / index or `dst == src`; −2 when "the target can't take this family (audio / return / group / master for an instrument or MIDI effect)"; −3 when the clone fails;
  - audio / MIDI effect: clone (`cloneDevice` / `cloneMidiDevice`), insert at `atIndex` (−1 = end) shifting the target's lanes as `insertDeviceClone` does; unless `keepSource`, remove from the source with `remapAutomationAfterDeviceChange(..., removedIndex)` after taking its lanes (T009) and append them to the target with `deviceIndex` rewritten;
  - instrument: clone, set as the target's instrument (editor of the old one closed); unless `keepSource`, take the source's instrument lanes onto the target and drop the source track (editors closed as `removeTrack`, `Engine_Tracks.cpp:3172`), set flag bit 2;
  - a cloned audio effect whose `sidechainSourceTrackId() == dst` gets −1 and flag bit 0; flag bit 1 when the source had CV links or rack macro mappings to the moved device.
- [X] T011 Add the C wrappers in `src/native/nota.engine/src/abi/nota_engine_device.cpp` and the declarations with the contract comments in `include/nota/nota_engine.h` next to the device clipboard block (line ~1538): `nota_track_copy_instrument`, `nota_track_paste_instrument`, `nota_track_transfer_device` (`out_flags` may be NULL); update the `clipboard_kind` comment to "0 empty, 1 audio effect, 2 MIDI effect, 3 instrument".

### Managed interop

- [X] T012 [P] Add the P/Invokes in `src/managed/Nota.Infrastructure/Interop/NativeMethods.Devices.cs` and the wrappers in `NotaEngine.Devices.cs`; add to `src/managed/Nota.Application/Engine/IAudioEngine.cs` (by `CopyMidiEffect`, line ~514): `bool CopyInstrument(int trackId)`, `int PasteInstrument(int trackId)`, `int TransferDevice(int srcTrackId, DeviceFamily family, int index, int dstTrackId, int atIndex, bool keepSource, out TransferFlags flags)`, a `DeviceFamily { Audio = 0, Midi = 1, Instrument = 2 }` enum and `[Flags] TransferFlags { None = 0, SidechainCleared = 1, LinksDropped = 2, SourceTrackRemoved = 4 }`; update the `DeviceClipboardKind()` comment to include 3 = instrument.
- [X] T013 Build with `scripts/build.sh` and run `dotnet run --project tests/Nota.SmokeTest -- --instrument-chain`: T002–T004 pass.

**Checkpoint**: the engine does everything the stories need; UI work can start.

---

## Phase 3: User Story 1 — Select, delete and replace the instrument (P1) 🎯 MVP

**Goal**: the instrument card is selectable; its menu has Replace…, Copy, Cut, Paste, Delete
track; Delete on it deletes the track after a confirmation when it has clips.

**Independent Test**: spec US1 / quickstart steps 1–3.

- [X] T014 [US1] In `src/managed/Nota.Presentation/DeviceInsertService.cs`, add `InsertPlacement.ReplaceInstrument` (doc comment: "swap the track's instrument even when it is a rack") and handle it in `InsertCore`: a built-in or plug-in instrument (or an instrument preset with `InPlace`) on an instrument track replaces it through `SetTrackBuiltinInstrument` / `SetTrackInstrumentPlugin` without the `CanReplaceInstrument` rack refusal, sets `InstrumentReplaced`; status "Changed instrument to <name>". Drops keep `Default` (rack → chain / pad).
- [X] T015 [US1] In `src/managed/Nota.App/DeviceChainView.Clipboard.cs`: make the instrument selectable — `SelectableKind` includes `ChainKind.Instrument`; track it with `_selDeviceIndex = -1` plus a `bool _instrumentSelected` (data-model.md "UI selection") so `HasDeviceSelection` and `IsSelected(ChainKind.Instrument, -1)` work; `SelectRackDevice` clears it; Focus routes keys here as for effects. Update the comment at line 27 (the instrument is now selectable).
- [X] T016 [US1] In `src/managed/Nota.App/DeviceChainView.Instruments.cs` and `DeviceChainView.Shell.cs`: the instrument card (all routes in `InstrumentCard()`: strategy cards, plug-in card, rack cards, folded strip) gets the selection ring and the click-to-select handler the effect cards use (header and body background; not on controls); `CanDelete: true` for the instrument; header tooltip "Double-click to fold · right-click for replace, copy and delete".
- [X] T017 [US1] In `DeviceChainView.Clipboard.cs`, route Copy / Cut / Paste / Delete for the instrument: Copy → `CopyInstrument`, `_clipFromTrack` and `_clip` (preset label) as for effects; Paste with an instrument on the clipboard and the instrument (or nothing) selected → `PasteInstrument(_trackId)` then `ForgetInstrumentState` + rebuild + `Changed`; Paste of an instrument while an effect is selected → status "Paste an instrument on an instrument card."; Paste of an effect while the instrument is selected → append to the matching section as today. Delete → raise a new `public event Action<int, bool>? TrackDeleteRequested` (trackId, cut: false); Cut → Copy then `TrackDeleteRequested(trackId, true)`.
- [X] T018 [US1] In the device menu (`ShowDeviceMenu`, `DeviceChainView.Clipboard.cs:230`), for `ChainKind.Instrument`: add **Replace…** as a submenu built from the Browser's instrument list (built-ins, Racks: Instrument Rack / Drum Rack, installed plug-in instruments — reuse the catalog source the Browser / `DeviceInsertService` uses), invoking a new `public event Action<BrowserItem>? InstrumentReplaceRequested`; enable Copy / Cut / Paste (Paste when `DeviceClipboardKind() == 3`); hide Duplicate and Group into rack; the danger item reads **Delete track** with `MenuKit.DeleteKey`.
- [X] T019 [US1] In `src/managed/Nota.App/MainWindow.Dnd.cs` (or the partial that wires `_deviceChain`): handle `InstrumentReplaceRequested` with `ApplyInsertResult(Insertion.Insert(item, new InsertTarget(t, InsertPlacement.ReplaceInstrument)))`; handle `TrackDeleteRequested(trackId, cut)`: if the track has any arrangement or Session clip, `await new ConfirmWindow("Delete track", $"“{name}” has clips. Delete the track with its clips?", "Delete", "Cancel").ShowDialog<bool>(this)` (contracts/ui.md wording), then delete through the Arrangement's `DeleteTracks` path (expose `ArrangementView.DeleteTrack(int id)` in `ArrangementView.Tracks.cs` that calls the private `DeleteTracks`), refresh Session and the device panel (show the new selected track or clear); status "Deleted <name>" / "Cut <instrument> — deleted <name>".

**Checkpoint**: US1 works on its own (quickstart 1–3).

---

## Phase 4: User Story 2 — A visible Remove control on every device header (P1)

**Goal**: an 18×18 `×` in every card header and folded strip; click / Enter / Space removes,
one undo step.

**Independent Test**: spec US2 / quickstart step 4.

- [X] T020 [P] [US2] Add a shared `RemoveButton(string tip, Action onRemove)` in `src/managed/Nota.App/DeviceCardKit.cs` (or beside `BuildCardShell` in `DeviceChainView.Shell.cs` if DeviceCardKit has no such helpers): 18×18, `GlyphKind.Close`, `NotaPalette.TextTertiary` → the theme's danger colour on hover, `Focusable = true` with the theme's focus ring, `KeyDown` Enter / Space → `onRemove`, `PointerPressed` handled (no drag, no selection); `ToolTip` = tip; follow `DESIGN.md` (no accent).
- [X] T021 [US2] In `BuildCardShell` (`DeviceChainView.Shell.cs:130`), add the Remove control to the right of the power slot in the header grid for every non-Utility card with `CanDelete` (effects, MIDI effects, plug-ins, racks, the instrument): effects / MIDI effects → the existing delete for that card (select it, `DeleteSelectedDevice`); the instrument → `TrackDeleteRequested(_trackId, false)` with tip "Remove track"; others "Remove device".
- [X] T022 [US2] In `FoldedStrip` (`DeviceChainView.Fold.cs:42`), add the same control on the strip (below the power switch), same routing as T021.
- [X] T023 [P] [US2] In `src/managed/Nota.App/DeviceCards/Rack/RackCardView.cs`, restyle `SlotDelBtn` (line ~117) to the T020 look (same glyph size, colours, focusability), keeping "Remove device"; check the rack chain compact cards of specs/040 (`RackCard.Chains.cs`) all show it.
- [X] T024 [US2] Check every card in all four themes (Studio Dark, Studio Light, Ember Graphite, Ember Paper) and with UI scale > 1: the × never overlaps the name, preset picker or badges on compact cards (shrink the name first, per the header's existing trimming).

**Checkpoint**: US2 works on its own (quickstart 4).

---

## Phase 5: User Story 3 — Drop an instrument in Session view (P2)

**Goal**: Browser instruments (and instrument presets) drop on Session columns and right of the
last column.

**Independent Test**: spec US3 / quickstart step 5.

- [X] T025 [US3] In `src/managed/Nota.Presentation/DeviceInsertService.cs`, add `public InsertResult DropOnSession(BrowserItem item, int trackId)` (one `Group`): an instrument on a plain instrument track → `TryReplaceInstrument`; on an Instrument / Drum Rack track → the chain / pad route `RouteToTrack` uses for racks; otherwise a new instrument track (`CreateInstrumentTrack(item, 0)`) with `CreatedTrack` and an `AfterTrackId = trackId` hint when `trackId > 0`; an instrument preset → `ApplyPreset(item, new InsertTarget(trackId, InPlace: true))` with the same fallbacks. Remove the "Drop instruments onto the arrangement." result at line ~295 (route instruments the same way there).
- [X] T026 [US3] In `src/managed/Nota.App/MainWindow.Dnd.cs` `OnSessionDrop`: the `default` (instrument) branch calls `Insertion.DropOnSession(item, trackId)` and `ApplyInsertResult`; when the result created a track after an anchor column, place it with `Timeline.PlaceNewTrack(newId, trackId)` inside the same undo group; select the new / replaced track's first slot. Instrument presets that today go through `RouteToTrack` keep doing so unless they are instrument presets (then `DropOnSession`).
- [X] T027 [US3] In `src/managed/Nota.App/SessionView.Grid.cs`: make `ColumnHeader` (line 214) a drop target (`DragDrop.SetAllowDrop`, DragOver accepting `BrowserView.IsAcceptableDrag`, drop → `OnItemDropped(item, c.TrackId, -1 /*scene*/, c.IsInstrument)`), with the header drop ring shown while hovered; add a drop target over the empty area right of the last column that calls `OnItemDropped(item, -1, -1, false)` with the grid's `_dropHover` wash; make `OnSessionDrop` treat `scene < 0` as "no slot" (no slot selection, select the track).
- [X] T028 [US3] Verify the Session slot-cell drop of an instrument (existing handler, `SessionView.Grid.cs:358`) now replaces / adds via T026, and a Sample / MIDI file drop on a header does nothing with a status line.

**Checkpoint**: US3 works on its own (quickstart 5).

---

## Phase 6: User Story 4 — Move a device to another track (P2)

**Goal**: Move to track ▸ in every device menu; dragging a card header onto a track header in
the Arrangement or Session moves it, ⌥ copies.

**Independent Test**: spec US4 / quickstart step 6.

- [X] T029 [US4] In `DeviceChainView.Clipboard.cs` `ShowDeviceMenu`: add **Move to track ▸** under Duplicate for effects, MIDI effects and the instrument: list the tracks from the engine track list that can take the family (audio effects: every track with a chain incl. returns and the master; MIDI effects and the instrument: instrument tracks), excluding `_trackId`, each with its name and colour swatch (`MenuKit` item with the track brush). Picking one raises a new `public event Action<DeviceMoveRequest>? MoveRequested` (`record DeviceMoveRequest(int SrcTrackId, DeviceFamily Family, int Index, int DstTrackId, bool Copy)`).
- [X] T030 [US4] In MainWindow (the partial that wires `_deviceChain`, e.g. `MainWindow.Dnd.cs`), add `OnDeviceMoveRequested(DeviceMoveRequest r)`: for an instrument move from a track with clips, the `ConfirmWindow` of contracts/ui.md ("Move the instrument and delete “<name>” with its clips?", "Move", "Cancel"); call `Engine.TransferDevice(..., atIndex: -1, keepSource: r.Copy, out var flags)`; on < 0 show the refusal ("<track> can't take <device>." / "Couldn't copy <device>."); on success refresh Arrangement, Session, the device panel (show the target track), select the moved device there, and set the status "Moved <device> to <track>" / "Copied …" plus " · sidechain cleared" / " · macro and modulation links left on <source>" from `flags`; when `SourceTrackRemoved`, drop the source from the Arrangement selection as `DeleteTracks` does.
- [X] T031 [US4] Create `src/managed/Nota.App/DeviceDrag.cs` (SPDX header; file comment pointing at research R5): `internal static class DeviceDrag` with `Current` `(int TrackId, DeviceChainView.ChainKind Kind, int Index)?`, `IsActive`, `CanTake(IAudioEngine e, int trackId)` (the family rules of T029, `trackId != Current.TrackId`), and `static async Task DragAsync(PointerPressedEventArgs press, (int, ChainKind, int) payload)` that adds a single `DataFormat.Text` sentinel ("nota-device-drag"), sets `Current`, awaits `DragDrop.DoDragDropAsync(press, data, DragDropEffects.Move | DragDropEffects.Copy)`, clears `Current` in `finally` — the `BrowserView.DragItemAsync` pattern (`BrowserView.cs:60`).
- [X] T032 [US4] In `HookDeviceDrag` (`DeviceChainView.Clipboard.cs`, the header drag) and the instrument card's header: keep the press args; in `PointerMoved`, when the pointer leaves the `DeviceChainView` bounds during a drag, end the in-panel drag (opacity, drop bar, capture released) and start `DeviceDrag.DragAsync(press, (_trackId, kind, di))`; the in-panel reorder is unchanged otherwise. Make the instrument card's header draggable this way too (no in-panel reorder for it).
- [X] T033 [US4] In `src/managed/Nota.App/ArrangementView.cs`: in `OnLaneDragOver` (line 1427) accept `DeviceDrag.IsActive` when `DeviceDrag.CanTake(row track)` (Move, or Copy with Alt), showing `SetDropTrack(row)`; in `OnLaneDrop` call a new `public event Action<int, bool>? DeviceDroppedOnTrack` (trackId, copy = `e.KeyModifiers.HasFlag(KeyModifiers.Alt)`); same for the footer rows (`OnFooterDragOver` / `OnFooterDrop`, returns and master for audio effects).
- [X] T034 [US4] In `SessionView.Grid.cs`: the column header and slot-grid drop targets (T027 and the existing grid handler) also accept `DeviceDrag` with `CanTake`, highlighting the column, and raise `DeviceDroppedOnTrack(trackId, alt)` (new event on `SessionView`).
- [X] T035 [US4] In MainWindow, wire both `DeviceDroppedOnTrack` events to `OnDeviceMoveRequested(new DeviceMoveRequest(DeviceDrag.Current.TrackId, family, index, trackId, copy))`, mapping `ChainKind` → `DeviceFamily`.

**Checkpoint**: US4 works on its own (quickstart 6–7).

---

## Phase 7: Polish & cross-cutting

- [X] T036 [P] MCP per contracts/mcp.md: `set_track_instrument(trackId, kind)` in `src/managed/Nota.Mcp/Tools/InstrumentTools.cs` and `move_device_to_track(trackId, chain, index, targetTrackId, atIndex = -1, copy = false)` in `src/managed/Nota.Mcp/Tools/DeviceTools.cs` (through `Mutate`, as the neighbouring tools), with the descriptions the contract gives; mention in `remove_track`'s description that it is how an instrument is deleted.
- [X] T037 [P] Shortcuts pane: add "Drag a device onto a track — move it there" and "⌥-drag a device onto a track — copy it there" under Devices in `src/managed/Nota.App/PreferencesWindow.cs` (run the `nota-shortcuts` skill to check nothing else drifted).
- [X] T038 [P] `CHANGELOG.md` `[Unreleased]`: Added — instrument card select / Replace… / copy-paste (nota-daw/nota#327, nota-daw/nota#146), Remove control on device headers (nota-daw/nota#110), Move to track and drag a device onto a track (nota-daw/nota#327), instrument drops in Session view (nota-daw/nota#313); Changed — Delete on the instrument deletes its track after a confirmation; `set_track_instrument` / `move_device_to_track` MCP tools.
- [X] T039 Run the `nota-features` skill (FEATURES.md) and the `nota-docs` skill (Devices and Session pages, EN + RU, screenshots of the instrument menu and the Remove control).
- [ ] T040 Run quickstart.md steps 1–9 in the app (macOS), incl. all four themes for step 4 and playback for step 9; fix what fails.
  - 2026-10-27, headless harness (/tmp/h043, real MainWindow): steps 1, 3, 4, 5, 6 (menu route) and 7
    (sample on a header) pass; screenshots in all four themes. Not yet checked: the drag of a
    device header out of the panel onto a track (needs a real pointer), step 8 (MCP against a
    running app), step 9 (playback).
- [ ] T041 `scripts/build.sh` green (smoke incl. `InstrumentChainTests`); then `/speckit-converge`.
  - 2026-10-27: build + full smoke pass except `RenderPoolTests` "overloaded" late-block budgets,
    which also fail on a clean develop on this machine under load (16 → 286 late blocks between
    two runs) — environmental, re-run on a quiet machine. `/speckit-converge` not run yet.

---

## Dependencies & execution order

- **Phase 1 → Phase 2** (T001 before T002–T004; T002–T004 before T005–T011; T011 before T012; T013 last in Phase 2).
- **US1** (T014–T019) needs Phase 2. **US2** (T020–T024) needs only T017's `TrackDeleteRequested` for the instrument's ×; effects' × is independent. **US3** (T025–T028) needs only T014 (`InsertPlacement` exists) — otherwise independent of US1 / US2. **US4** (T029–T035) needs Phase 2 and T019's confirmation helper.
- Within US4: T031 before T032–T034; T033 / T034 before T035.
- Polish after the stories it documents.

## Parallel opportunities

- Phase 2: T002, T003, T004 (one file, separate sections — write together or in sequence); T012 alongside T005–T011 once the C signatures in T011 are fixed.
- After Phase 2: US2's T020 / T023, US3's T025–T027 and US1 can proceed side by side (different files: `DeviceChainView.Shell/Fold`, `RackCardView`, `SessionView.Grid` / `DeviceInsertService`, `DeviceChainView.Clipboard`).
- Polish: T036, T037, T038 in parallel.

### Example (after Phase 2)

```text
US1: T014 DeviceInsertService.ReplaceInstrument  →  T015–T019 DeviceChainView + MainWindow
US2: T020 RemoveButton + T023 RackCardView        →  T021–T022 Shell / Fold
US3: T025 DropOnSession                           →  T026–T028 MainWindow.Dnd / SessionView.Grid
```

## Implementation strategy

1. **MVP**: Phase 1 + Phase 2 + US1 — the instrument is selectable, replaceable, copyable,
   deletable (quickstart 1–3, smoke green).
2. **+ US2**: the Remove control (#110) — small, ships with the MVP.
3. **+ US3**: Session drops (#313).
4. **+ US4**: Move to track and drag-to-track (#327's second half).
5. Polish, `scripts/build.sh`, `/speckit-converge`, then the PR (`nota-pr` skill) with
   before / after screenshots of the device header and the Session drop.

## Nota compliance (re-check before the PR)

- [X] New files (`InstrumentChainTests.cs`, `DeviceDrag.cs`) carry the AGPL-3.0-only SPDX header
- [X] No new dependency
- [X] No audio-thread change; every edit is one published graph (AR-5 / AR-6)
- [X] New calls only through `nota_engine.h` (AR-7), all covered by `InstrumentChainTests`
- [X] CHANGELOG, FEATURES.md, user docs, Shortcuts pane updated
- [X] UI follows `DESIGN.md` in all four themes

---

## Phase 8: Convergence

> 2026-10-27: the user chose to ship without T042–T046 (all LOW/MEDIUM polish); they stay open as follow-ups.

- [ ] T042 Select the instrument card with a click on its body background (not on its controls), as the header click does, in `src/managed/Nota.App/DeviceChainView.Shell.cs` (`BuildCardShell`, instrument cards only) per US1/AC1 (partial)
- [ ] T043 Refuse an instrument pasted into a rack chain with a reason ("An instrument pastes on an instrument card — replace a chain's instrument from its menu.") instead of "Nothing to paste here." in `src/managed/Nota.App/DeviceCards/Rack/RackCard.Clipboard.cs` (`RcPaste`, clipboard kind 3) per spec Edge Cases (partial)
- [ ] T044 Show each track's colour swatch beside its name in **Move to track ▸** in `src/managed/Nota.App/DeviceChainView.Move.cs` (`MoveToTrackGroup`, the track brush the Arrangement uses) per contracts/ui.md + T029 (partial)
- [ ] T045 Make Cut on the instrument ask with the contract's wording ("Move the instrument and delete “<name>” with its clips?" · **Move**) — or amend contracts/ui.md to the current "Delete the track with its clips?" — in `src/managed/Nota.App/MainWindow.DeviceMoves.cs` (`OnTrackDeleteRequestedAsync`, cut = true) per contracts/ui.md + T019 (contradicts)
- [ ] T046 Record in contracts/ui.md that rack chain compact effect cards drop the "Nota " prefix to fit the Remove control (`src/managed/Nota.App/DeviceCards/Rack/RackCard.Chains.cs`), or revert it, per contracts/ui.md (unrequested)
