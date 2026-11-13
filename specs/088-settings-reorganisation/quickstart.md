# Quickstart: verify the Settings reorganisation

## Build gate

```bash
scripts/build.sh
```

Passes, including the palette checks (every `prefs.*` command bound) and the page-id check
(every id asked for exists; every page has a command).

## Walk-through (EN, then RU)

1. Open Settings (⌘,). Sidebar reads: Devices — Audio, Outputs, Inputs, MIDI, Gamepads ·
   Remote control — Remote, AI control (MCP) · Plug-ins — Plug-ins, Downloads · General —
   Appearance, Language, Editing, Library, Shortcuts, Updates.
2. **Audio**: sample rate, buffer, latency, CPU check; no device combos, no input list.
3. **Outputs**: pick another output → sound moves; Play test tone → tone; switch page → tone stops.
4. **Inputs**: pick another input device → the input list rebuilds with that device's names.
5. **AI control (MCP)**: switch on → status "Listening on http://127.0.0.1:<port>/"; Copy config.
6. **Appearance**: theme, accent, waveform, track headers, scale, text size — nothing else.
7. **Language**: switch to Russian → restart prompt as before.
8. **Editing**: toggle the three switches; piano-roll labels / split fades / detail panel follow;
   restart → choices kept.
9. Palette (⌘K): "Settings: Outputs", "Settings: Inputs", "Settings: AI control (MCP)",
   "Settings: Language", "Settings: Editing" open their pages; "mcp", "language", "piano roll"
   find them.
10. Track input menu → "Audio I/O setup" opens Inputs. Audio-format menu → "Audio settings…"
    opens Audio. Downloads badge / install-pack links → Downloads.
11. Windows + ASIO: Outputs/Inputs show the driver name and "Change on the Audio page"; the
    output / input pair combos are on Outputs / Inputs.

## Screenshots

Headless-app harness (see memory: Nota UI verify recipe) — each new page and the reduced
Audio / Appearance in Studio Dark, EN + RU; used for the PR and for nota-docs.
