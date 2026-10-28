# Contract: Preferences → Audio (Windows)

Studio design language (`DESIGN.md`): existing `Row` / `Combo` / `Caption` helpers,
sentence case.

```text
Audio device
  Audio system    [ WASAPI ▾ | ASIO ]                       ← new, Windows only
── when WASAPI ──  (unchanged: Output, Input, Sample rate, Buffer size, Exclusive mode)
── when ASIO ──
  Driver          [ Focusrite USB ASIO ▾ ]   [ Driver settings… ]
  Output          [ Output 1 + 2 ▾ ]                         ← pairs of the open driver
  Input           [ Analog 1 + 2 ▾ ]
  Sample rate     [ 48 kHz ▾ ]                               ← request; refusal reverts + caption
  Buffer size     256 frames (set in the driver's settings)  ← read-only text
  Latency         5.3 ms · 256 frames @ 48 kHz · round trip 11.2 ms
```

- No ASIO driver installed: the ASIO option is disabled with the caption "No ASIO driver
  found. Install your interface's driver, or a generic one such as FlexASIO." WASAPI
  stays selected.
- Fallback active: the Latency line reads "ASIO unavailable (<reason>) → WASAPI"; the
  status bar shows "ASIO driver ‘<name>’ couldn't start — using WASAPI. <reason>".
- Changing Audio system, Driver, Output or Input calls the existing `Apply()`.
- "Driver settings…" is enabled only while ASIO is running.
- Device event polling (in `MainWindowViewModel`'s tick): `RESET_REQUESTED` → apply,
  resume playback if it was playing, status "Audio: driver settings changed · 128 frames";
  `DRIVER_FAILED` → apply (falls back), status as above.
