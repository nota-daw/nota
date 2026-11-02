# Research: Open the Detail panel on double-click

## R1 — Where does a selection open the panel today?

- **Decision**: gate in one place, `MainWindow.ShowDetail(height, honorPersist, resizable)`
  (`MainWindow.Detail.cs`), using a follow-only scope entered by the three selection-follow
  handlers.
- **Findings**:
  - `ShowDetail` is the only code that sets `DetailPanel.IsVisible = true` apart from the
    View → toggle (`OnMenuToggleClip`). It is reached from `ShowDevices` (Devices tab),
    `ShowClipPanel`, `ShowAudioClipPanel`, `ShowPatternPanel`.
  - Selection reaches it through:
    - `Timeline.TrackSelected → OnTrackSelected → ShowDevices(takeOverPanel: !keepClipTab)`
      (arrangement header, clip click, mixer strip, keyboard, `Timeline.Select(...)` from
      Freeze/Convert/imports, Modular `TrackActivated`);
    - `Timeline.ClipSelectionChanged → FollowClipSelection` (already returns when the Clip
      tab is not on screen, so it never opens a closed panel — wrapped anyway for safety);
    - `_session.SlotSelected → OnSessionSlotSelected → ShowSessionSlot` (a single click on a
      filled slot opens its editor today).
  - The popped-out Detail window never calls `ShowDetail` (all `Show*Panel` return early
    when `DetailFloating`), so it is unaffected by the gate — it keeps following.
- **Rationale**: one choke point means every present and future selection path obeys the
  rule; content still updates while hidden, so a later open shows the current selection.
- **Alternatives considered**:
  - Pass `takeOverPanel: false` from each caller — misses `ShowSessionSlot`/`ShowClipPanel`
    and every future caller.
  - Skip the follow handlers entirely while hidden — leaves stale content behind for the
    View-menu toggle to reveal.

## R2 — Gate rule

- **Decision**: inside the follow-only scope, `ShowDetail` returns before touching layout
  when `!DetailPanel.IsVisible && !Settings.OpenDetailOnSelect`. When the panel is visible
  it behaves exactly as today (tab switches, heights, maximise rules).
- **Scope mechanics**: an int depth counter (`_followOnly`) incremented/decremented in
  `try/finally` around the bodies of the three handlers; nested `Timeline.Select` calls from
  `OnSessionSlotSelected` stay inside it.

## R3 — Explicit "open" paths stay outside the scope

- Arrangement clip double-click: `LaneControl` → `OnClipDoubleClicked` →
  `MidiClipActivated/AudioClipActivated` → `OpenClipEditor` / `OpenAudioClipEditor` →
  `Show*Panel` → opens. Already correct; it sets `SelTrackId` directly and does not fire
  `TrackSelected`.
- Session slot double-click: `SlotEditRequested → OpenSessionClipEditor` → opens. The first
  press of the double-click goes through `SlotSelected` (quiet), the second opens.
- `view.showDevices`, the Devices/Clip/Pattern tab buttons, browser drops, Convert → opens.

## R4 — Track header double-click (Q1 = A)

- **Decision**: in `ArrangementView.AttachHeaderGestures`, `ClickCount == 2` (any track kind,
  left button) selects the track and raises a new `TrackDevicesRequested(int trackId)`
  event; `MainWindow` handles it with `ShowDevices(trackId)` (take over the panel). The
  `OpenPluginEditor(t.Id, -1)` call is removed there; the plug-in window stays on the
  **Open editor** button on the plug-in's card (`PluginCard`; `GenericInstrumentCard` has **Open GUI ↗**).
- The double-click check stays after the ComboBox/right-button guards and before the
  reorder-drag arming, as today.
- Group bars share `AttachHeaderGestures`, so a group track opens its Devices too.
- Row-seam, ADSR-handle, empty-lane and section double-clicks are handled by other
  controls first and are untouched.

## R5 — Session column header (Q2 = A)

- **Decision**: unchanged — double-click renames. A single click selects the track (quiet).

## R6 — Preference (Q3 = A)

- **Decision**: `Settings.OpenDetailOnSelect` (bool, default `false`), a `SwitchRow` in
  Preferences → Appearance in a new "Detail panel" section with a one-line caption, saved
  through `settings.Save()` like `PianoRollLabelAllNotes`. Read live (no cache) by the gate,
  so toggling takes effect immediately.

## R7 — View → toggle Detail panel

- **Finding**: `OnMenuToggleClip` only flips `DetailPanel.IsVisible`; with content updated
  while hidden, the row height may belong to the other tab.
- **Decision**: when the toggle opens the panel, re-run the checked tab's show
  (Devices → `ShowDevices(selected)`, Clip/Pattern → the matching `Show*Panel`) so the
  height and tab are right; closing stays a plain hide.

## R8 — Strings and docs

- New UI strings go into `Languages/fr.json` (en.json is the identity table); RU user docs
  live in `../nota-docs` and are updated through the nota-docs skill.
- Shortcuts pane: Mouse group gains "Double-click a track header → open its devices";
  "Double-click clip" text gains "opens the Detail panel if it is closed".
