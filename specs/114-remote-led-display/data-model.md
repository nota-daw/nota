# Data model (spec 114)

- **LedFrame** — `W = 128`, `H = 48`, `Dots: byte[6144]` (0–15). Value object; equal when the
  dots are equal. Encodes to / from RLE4.
- **DisplayContext** — what a page's drawing reads: engine, `DeviceAddress`, `DisplayParams`,
  page index and name, knob names / values (8), turning knob (−1 none), time in seconds, live taps.
- **DeviceDisplay** — `(DisplayContext, LedFrame) → void` for one page of one type key; the
  registry maps `typeKey → pageName → DeviceDisplay`; missing → bars.
- **RemoteMacroDevice** (reworked) — one device of the track's chain: `Name`, `Address`,
  `TypeKey`, `Pages: [(Name, RemoteParam?[8])]`.
- **RemoteClient** additions — `MacroPage`, `LedKey` (address + page last rendered), `SentLed`
  (bytes), `TurnKnob`, `TurnUntil`.
- **Led cache in the hub** — per tick: `(address, page, overlay) → (frame bytes, seq)`; `seq` per
  `(address, page)` grows when the bytes change.
