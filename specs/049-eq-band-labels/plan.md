# Implementation Plan: EQ band labels and notes

**Branch**: `feat/eq-band-labels` | **Spec**: [spec.md](spec.md) | **Issue**: nota-daw/nota#135

## Summary

EQ-8 gets a label and a note per band slot, kept by the device (message-thread data, never read
by `process()`), saved through the device's state blob, and edited through one engine call that
is one undo step. The graph draws the labels next to their nodes with a simple collision pass;
a click on a label opens a note popup. A new display-only parameter, **Labels**, hides them.

## Technical Context

- **Engine**: `Eq` (`devices/effects/Eq.h/.cpp`) keeps `label_[8]`, `note_[8]` as `std::string`.
  `getState()` returns them (empty when there are none, so a plain EQ-8 saves exactly as before);
  `setState()` reads them. The audio thread never touches them.
- **State format**: `"NEQ1"`, then for each of 8 bands: u16 label length, label bytes (UTF-8),
  u16 note length, note bytes. Unknown magic or a short blob reads as "no annotations".
- **Undo**: a new `MixKind::DeviceNotes` (7). `Engine::setEqBandText(track, device, band, label,
  note)` appends the device's whole annotation blob before and after the edit to an
  append-only table (`noteStates_`, index 0 = empty) and pushes a mix-only `UndoEntry` whose
  before/after are those indices. `applyMix` restores the blob with `Device::setState`. This is
  the preset-tag pattern (specs/048) without a per-device tag: the device's own text is the
  current value, so loading a project needs no table entry.
- **Copies**: device copy / duplicate and track duplicate already copy `getState()` for built-ins
  (`Engine_Devices.cpp`), so annotations follow with no change.
- **Presets**: `PresetService.Capture` saves an EQ-8's labels and notes as a readable
  `BandText` list (band, label, note) in the preset JSON; `Apply` / `ApplyInPlace` give the
  device exactly that list (none for a factory preset), and `ResetInPlace` (Init) clears it.
  Other built-ins keep today's preset format.
- **Labels switch**: `Eq::Labels` appended as param 62 (0/1, default 1, display only, like
  AnaPeak). Older projects lack it and get the default.
- **C ABI**: `nota_eq_set_band_text(engine, track, device, band, label, note)` and
  `nota_eq_band_text(engine, track, device, band, which /*0 label, 1 note*/, buf, cap)` (UTF-8,
  returns the required size). Wrapped as `IAudioEngine.EqSetBandText` / `EqBandText`.
- **MCP**: `Eq::deviceText` lists labelled bands and their notes.
- **UI** (`Eq8Views.cs`, `EqDeviceBody.cs`):
  - `Eq8Curve` draws labels: each label is a pill above its node (below for nodes in the top
    third); a greedy pass in frequency order moves a label up/down by its height until it no
    longer overlaps a placed one, clamps it inside the graph, ellipsizes if still no room, and
    draws a leader line when it moved off its node. Note mark = small dot at the pill's right.
  - Hit-testing a label happens before node hit-testing only for a click (press + release without
    a move); drags start on nodes as before.
  - Note popup: a `Popup` with a label `TextBox` (MaxLength 24, no line breaks) and a note
    `TextBox` (AcceptsReturn, MaxLength 2000); commit on close / Ctrl-Cmd+Enter; Escape closes.
  - Band panel: Label field + Note button; chips show the label in place of the type.
  - Labels switch next to Peak in the analyzer bar.
- **Empty-space bell**: `Eq8Curve.AddBandAt` clears the chosen slot's annotation in the same undo
  group as the band's params.

## Constitution Check

- Audio thread: no new audio-thread work; `process()` unchanged. ✓
- C ABI stays pure C (char pointers, ints). ✓
- No new dependency. ✓
- Docs EN + RU, CHANGELOG entry. ✓

## Testing

- Smoke test `--eq-labels`:
  - set labels/notes on bands, read them back;
  - get/set state round trip (byte-identical);
  - an empty EQ-8 has an empty state blob;
  - undo / redo of an edit;
  - device duplicate keeps them;
  - render a signal with and without annotations: identical output;
  - UTF-8 (Cyrillic) round trip; over-long text is truncated to the limits.
- Project round trip via `ProjectService` in the same test.
- Preset capture / apply round trip.
