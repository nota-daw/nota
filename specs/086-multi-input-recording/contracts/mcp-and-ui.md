# Contract: MCP tools and UI surfaces

## MCP (`src/managed/Nota.Mcp/Tools/MixerTools.cs`)

| Tool | Args → result | Notes |
|------|---------------|-------|
| `list_audio_inputs` | → `[{index (1-based), name, deviceName, enabled}]` | names after the Audio I/O setup |
| `set_track_input` | `trackId, first (1-based), width (1\|2)` | error for odd stereo first / bad width |
| `get_track_input` | `trackId` → `{first, width, label, available}` | |
| `list_midi_inputs` | → `[{uid, name, connected}]` | |
| `set_track_midi_input` | `trackId, uid? (null = All), channel (0 = All, 1–16)` | |
| `get_track_midi_input` | `trackId` → `{uid, name, channel}` | |

`set_input_channels` / `get_input_channels` stay (0/1/2 meaning unchanged); their
descriptions point at the new tools.

## Track audio input menu (track header ▾, mixer strip, Session I/O row, detail panel)

```
In ▾
  ─ Hardware ─────────────
  ● Vox            (In 1)
    Snare          (In 2)
    Input 3
    Input 4
  ─ Stereo ───────────────
    In 1/2
    Gtr            (In 3/4)
  ─ Tracks ───────────────   (unchanged: other tracks / sends / master)
  ─────────────────────────
    Audio I/O setup…         opens Preferences → Audio → Audio I/O
```

- Lists enabled inputs only (FR-017); a pick that is unavailable or switched off stays
  shown, greyed with "(not available)" / "(switched off)" (FR-011, FR-018).
- More than 16 mono inputs: the mono list becomes a submenu "Mono inputs ▸" and the pairs
  "Stereo pairs ▸".
- Short label: user name, else "In N" / "In N/M".

## Instrument track MIDI input menu

```
MIDI ▾
  ● All inputs
    KeyLab Essential 61 mk3
    Launchkey 49
  ─────────────────
  Channel ▸  ● All  1 2 … 16
  ─────────────────
    From track ▸            (unchanged MIDI In)
```

A disconnected device the track uses stays listed, greyed "(not connected)". An MPE
device with a single-channel filter shows a warning line under the channel list.

## Preferences → Audio → Audio I/O

One row per device input: number · device name · on/off switch · name field. Below, one
row per odd-aligned pair: "In 1/2" · name field. A "Reset names" action. Follows
`DESIGN.md` (Studio), sentence case, Geist tabular figures for numbers. Edits apply live
(FR-020).

## Arm tooltip

"Arm for recording (⌘-click to arm more tracks)" — Ctrl on Windows/Linux (FR-005a);
localised via the key-based catalogue (specs/077).

## Record feedback

When a take starts with armed tracks it can't record, the transport status shows
"Recording N tracks — M skipped" with a tooltip listing each skipped track and its reason.
