# Research: Settings reorganisation

## R1 — Select pages by id, not position

- **Decision**: The `Nav` table stays the single source of order. `Select(int)` stays the
  internal primitive (nav items are built with their index), but every caller goes through
  `Select(string id)` / `IndexOf(id)`; `BuildPane` switches on `page.Id`. The constants
  `RemoteIndex`, `DownloadsIndex`, `UpdatesIndex` and the literal `0` / `7` cases are removed.
  An unknown id selects page 0 (edge case: an old caller).
- **Rationale**: Inserting Outputs/Inputs at positions 1–2 and MCP after Remote shifts every
  index; positional constants silently open the wrong page (the issue's own risk).
- **Alternatives**: Re-number the constants — rejected, it breaks again with the next page.

## R2 — What moves where on the audio side

- **Decision**:
  - *Audio*: audio system (Windows), ASIO driver + "Driver settings…", sample rate, buffer size,
    exclusive mode (Windows WASAPI), latency, CPU check ("Performance" row).
  - *Outputs*: output device combo (WASAPI / CoreAudio / ALSA…), test tone ("Output check" row).
    Under ASIO: a read-only "Device" row naming the driver with "Change on the Audio page" link,
    the ASIO output pair combo, test tone.
  - *Inputs*: input device combo, then the spec-086 Inputs section (host rebuilt on change).
    Under ASIO: the driver row as above, the ASIO input pair combo (or "no inputs on this
    driver"), then the Inputs section.
- **Rationale**: The ASIO output/input *pair* is an output/input choice, so it moves with its
  side; the driver itself is the device for both sides and the format, so it stays on Audio
  (spec assumption). Exclusive mode dictates rate/buffer → stays with them.
- **Alternatives**: Duplicate the driver picker on all three pages — rejected (three places to
  change one thing, three rebuild paths).

## R3 — Rebuild and test lifecycle across three pages

- **Decision**: `RebuildAudioPane()` already re-selects the current page; it now applies to
  whichever of Audio / Outputs / Inputs is shown. `OnAudioDeviceRestarted` rebuilds when the
  selected page id is one of those three. `Apply()` keeps updating latency and inputs (both
  null-safe when their page isn't shown — `_latencyValue` / `_inputsHost` are cleared on
  `Select`). `StopTests()` on every `Select` keeps stopping the tone/CPU check on leaving.
- **Rationale**: Same behaviour as today, just per page.

## R4 — Editing page contents

- **Decision**: Three sections in this order: Piano roll ("Label every note…"), Arrangement
  ("Fade audio clips when splitting"), Detail panel ("Open the detail panel when…"). Each keeps
  its caption. `SplitFades` moves off Appearance (it sat under Waveform but is behaviour).
- **Alternatives**: a single-switch "Piano roll" page (issue's first idea) — rejected by the
  widened issue: Editing gathers all editing behaviour and answers the issue's open question.

## R5 — Icons

- **Decision**: Outputs → `Speaker`; Inputs → `Input`; AI control (MCP) → `Action`;
  Editing → `Edit`; Language → new `Globe` path glyph in the 24-unit `Paths` style.
- **Rationale**: Reuse where a glyph already says the thing; only language lacks one.

## R6 — Strings and keys

- **Decision**: New `en.json` keys for: group "Remote control"; page titles Outputs, Inputs,
  AI control (MCP) (reuses `preferences.ai_control_mcp`), Language (reuses
  `preferences.language`), Editing; subtitles for each new page; a new Appearance subtitle key
  (the old `preferences.theme_scale_waveforms_and_ai` is removed — the wording changed, so a
  translation of the old one must not stand in for it); the ASIO note; palette titles
  `commands.settings_outputs|inputs|mcp|language|editing`. Status text "Choose a device in
  Settings → Audio" changes to "… → Outputs" under new keys. Russian goes to
  `nota-translations/languages/ru.json` (the app falls back to English until it lands).
- **Rationale**: Keys are named after their English wording (spec 068); changed wording → new key.

## R7 — Entry points

| Caller | Today | After |
|---|---|---|
| Palette `prefs.*` | 10 pages | 15 pages (`prefs.outputs`, `prefs.inputs`, `prefs.mcp`, `prefs.language`, `prefs.editing`) |
| Audio-format menu "Audio settings…" | Audio | Audio (rate/buffer live there) |
| Record-input menu "Audio I/O setup" | Audio | Inputs |
| Status "no audio output … Settings → Audio" | text only | text says Outputs |
| Skin preview `ShowPage("Appearance")` | Appearance | unchanged |
| `ShowRemote`, `ShowDownloads`, Updates | by index | by id |

Palette keywords: `prefs.appearance` drops "language langue francais english" (→ `prefs.language`);
`prefs.mcp` gets "ai claude mcp server port"; `prefs.editing` gets "piano roll labels note names
split fades detail panel"; `prefs.inputs` "input channels microphone names"; `prefs.outputs`
"output speakers test tone".
