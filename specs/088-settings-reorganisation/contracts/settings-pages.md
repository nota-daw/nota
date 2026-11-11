# Contract: Settings pages

## Page ids (stable — code, palette, docs links ask for these)

`Audio`, `Outputs`, `Inputs`, `MIDI`, `Gamepads`, `Remote`, `MCP`, `Plug-ins`, `Downloads`,
`Appearance`, `Language`, `Editing`, `Library`, `Shortcuts`, `Updates`.

- `PreferencesWindow.ShowPage(id)` selects the page with that id; an unknown id selects the
  first page and does not throw.
- `MainWindow.ShowPreferencesPage(id)` reuses an open Settings window.
- Every `prefs.<x>` palette command maps to one id; the smoke test fails if a command names an
  id that is not in the sidebar, or a page has no command.

## Page contents (top to bottom)

**Audio** — Audio device: [Windows] Audio system · [ASIO] Driver + Driver settings… ·
Sample rate · Buffer size · [Windows WASAPI] Exclusive mode + caption · Latency.
Test: Performance (CPU check).

**Outputs** — Output: Device (combo; under ASIO the driver name + "Change on the Audio page") ·
[ASIO, running] Output pair. Test: Output check (test tone).

**Inputs** — Input: Device (combo; under ASIO as above) · [ASIO, running] Input pair or "no
inputs on this driver". Inputs (spec 086): hint · each input · stereo pairs — or the "no
inputs" caption.

**AI control (MCP)** — the existing MCP section (switch, port, status, Copy config + caption).

**Appearance** — UI: Colour theme (+ follow-system caption) · Accent · Waveform · Track headers ·
Interface scale · Text size (each with its caption).

**Language** — Language picker + caption.

**Editing** — Piano roll: label every note + caption · Arrangement: fade audio clips when
splitting + caption · Detail panel: open on select + caption.

## Strings (new en.json keys; RU in nota-translations)

| Key | English |
|---|---|
| `preferences.remote_control` | Remote control |
| `preferences.outputs` | Outputs |
| `preferences.outputs_subtitle` | Where Nota plays and the test tone |
| `preferences.inputs_subtitle` | What Nota records from and what each input is called |
| `preferences.mcp_subtitle` | Let an AI assistant work in Nota |
| `preferences.language_subtitle` | The language of menus and panels |
| `preferences.editing` | Editing |
| `preferences.editing_subtitle` | Piano roll, arrangement and the detail panel |
| `preferences.appearance_subtitle` | Theme, scale, text and waveforms |
| `preferences.audio_subtitle` | Sample rate, buffer and latency |
| `preferences.arrangement` | Arrangement |
| `preferences.asio_device_on_audio` | {0} — the ASIO driver is both input and output. |
| `preferences.change_on_audio_page` | Change on the Audio page |
| `commands.settings_outputs` … `_inputs`, `_mcp`, `_language`, `_editing` | Settings: Outputs … |
| `main.no_audio_output_could_be_outputs` | … Choose a device in Settings → Outputs |
| `main.no_audio_output_device_opened_outputs` | … Choose a device in Settings → Outputs |

Existing keys reused where the wording is the same (`preferences.inputs`,
`preferences.ai_control_mcp`, `preferences.language`, `common.piano_roll`,
`preferences.detail_panel`). Keys that no longer have a caller are removed.
