# Contract: Macros pages, LED frames and navigation (spec 113)

Additive to the Macros message of specs/014 / 090 and `foc` of
[specs/107](../../107-controller-focus-display/contracts/remote-focus.md). A client that does not
know a field or a message ignores it.

## `mac` (Nota → phone) — one entry per device, with pages

```jsonc
{ "t": "mac", "id": 6,
  "di": 1,                         // the device (index into devs)
  "devs": ["Arp", "Volt", "Reverb"],  // the track's chain: MIDI effects, instrument, audio effects
  "pg": 0,                         // the page (index into pgs)
  "pgs": ["Main", "Osc", "Filter", "LFO"],
  "k": [ { "n": "Filter 1 Freq", "v": 0.55, "txt": "890 Hz", "a": false, "map": null }, … ] }  // 8
```

- `devs` no longer holds "Name · 1/3" page entries; paging moves to `pg` / `pgs`.
- An older phone that ignores `pg` / `pgs` still sees eight knobs of the current page.

## `led` (Nota → phone) — while the phone shows Macros, only when the frame changed

```jsonc
{ "t": "led", "id": 6, "di": 1, "pg": 0,
  "w": 128, "h": 48, "bpp": 4, "enc": "rle4", "seq": 412,
  "f": "<base64>" }
```

- `f` decodes to bytes; each byte is a run: `count = (b >> 4) + 1`, `level = b & 15`, rows top
  to bottom, dots left to right, `w × h` dots in all.
- `seq` grows by one for every distinct frame Nota renders for that page; two phones on the same
  page get the same `seq` and bytes.
- The phone paints level 0 as a faint unlit dot and 1–15 brighter, in the track's colour.

## `nav` (phone → Nota)

```jsonc
{ "t": "nav", "s": "track",  "i": 1 }    // or -1: the next / previous track
{ "t": "nav", "s": "device", "i": 1 }    // next / previous device in the chain
{ "t": "nav", "s": "page",   "i": 1 }    // next / previous page
{ "t": "nav", "s": "pageAt", "i": 2 }    // a page by index
{ "t": "nav", "s": "deviceAt", "i": 0 }  // a device by index (the device sheet)
```

- Needs control access (a notes-only phone gets the usual toast).
- With Follow on, Nota also moves its selection (track), its focused device (device) and the lead
  controller's page (page), so the controller follows; the phone gets a fresh `mac` either way.
- At an end of its range a step does nothing.

## `foc` gains `ms`

`"ms": "midi" | "inst" | "audio"` with `"mi"`: the focused device's section and index, so a phone
can follow a MIDI-effect focus (`md` stays for older phones).
