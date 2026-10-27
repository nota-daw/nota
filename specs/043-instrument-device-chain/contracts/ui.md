# UI contract

## Device panel — the instrument card

- Click on its header or body background selects it (selection ring as effects); clears an
  effect or rack-chain selection.
- Right-click menu, top to bottom:
  - header (name · "Instrument"), presets as today;
  - **Replace…** ▸ submenu: Instruments (built-ins), Racks (Instrument Rack, Drum Rack),
    Plug-ins (installed instrument plug-ins) — the Browser's instrument list;
  - **Copy** ⌘C · **Cut** ⌘X · **Paste** ⌘V (enabled when the clipboard holds an instrument);
  - **Move to track ▸** — instrument tracks other than this one, by name and colour;
  - **Delete track** (danger, Delete key) — the instrument's Delete.
- Delete / Backspace with the instrument selected = **Delete track**.
- Delete track, Cut and Move to track on the instrument show, when the source track has any
  clip (arrangement or Session):
  `ConfirmWindow("Delete track", "“<name>” has clips. Delete the track with its clips?", "Delete", "Cancel")`.
  Cut's and Move's wording: "Move the instrument and delete “<name>” with its clips?" / "Move".

## Device panel — effects (audio and MIDI)

- Menu gains **Move to track ▸** under Duplicate: the tracks that can take the device (audio
  effects: any track with a chain incl. returns and the master; MIDI effects: instrument
  tracks), the current track excluded.
- Paste with the clipboard holding an instrument → status: "Paste an instrument on an
  instrument card."

## Remove control (#110)

- Every card header (full and folded): an 18×18 `×` right of the power slot (at the foot of a
  folded strip); `TextTertiary` at rest, `TextPrimary` on hover, a `BorderStrong` seam ring while
  focused — never red, which DESIGN.md keeps for overload; tooltip "Remove device", on the
  instrument "Remove track".
- Click, Enter or Space → the same action as the card's Delete. Not on Utility cards.
- Rack chain compact cards (specs/040) get the same control in their header (not on the chain's
  instrument, which is replaced from its menu); the legacy `SlotDelBtn` is the same control.

## Drag a device out of the panel

- Header drag inside the panel: unchanged (drop bar, reorder).
- Leaving the panel turns it into a system drag. Valid targets:
  - Arrangement: track rows (header ring + lane wash, as Browser drops) and the return / master
    footer rows;
  - Session: column headers and the column's slots.
- A target lights only if it can take the device and is not the source track.
- Drop = move; ⌥ (Alt) held on drop = copy. Status: "Moved <device> to <track>" / "Copied …",
  plus " · sidechain cleared" / " · macro and modulation links left on <source>" when flagged.

## Session view — Browser instrument drops (#313)

| Drop on | Instrument / instrument preset does |
|---------|-------------------------------------|
| plain instrument track: header or slot | replaces its instrument |
| Instrument Rack / Drum Rack track | adds a chain / pad (as in the Arrangement) |
| audio, return or group column | new instrument track right after the column |
| right of the last column | new instrument track at the end |

The new track is selected; one undo step each. The old status "Drop instruments onto the
arrangement." is gone.

## Shortcuts pane

- Adds: "⌥-drag a device onto a track — copy it there" and "Drag a device onto a track — move it
  there" under Devices.
