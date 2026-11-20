# Contract: Session selection and controller focus in Remote (spec 107)

Additive to [specs/102 `ses`](../../102-remote-session-grid/contracts/session-message.md).
Clients that don't know a field or a message type ignore it (the web app and Nota NX both
dispatch on `t` and fall through).

## `ses` gains `sel`

```jsonc
{ "t": "ses", "n": 8, "q": [], "tr": [ … ],
  "sel": { "id": 6, "i": 2 } }   // a slot: track id, scene index
// or  "sel": { "row": 2 }       // a scene row
// or  "sel": null               // nothing (Session not showing in Nota, or a Return/Master column)
```

- `id` is a track or group id present in `tr` (never a return).
- Clients outline the slot (or the whole row) like the desktop's selection; no auto-scroll.

## New message `foc` (Nota → every phone, on change)

```jsonc
{ "t": "foc", "on": true,
  "ctl": "KeyLab Essential 61 mk3",     // the lead controller (or "2 controllers")
  "mode": "device",                     // "device" | "volume" | "pan" | "send"
  "send": 0,                            // send index, mode "send" only
  "id": 6, "tn": "Bass",                // focused track (mode device), 0 / "" when none
  "dev": "Twin",                        // focused device name, null when the track has none
  "md": -1,                             // the device's index in this track's `mac.devs` list
                                        // as Remote numbers it (−1 = instrument / rack slot,
                                        // ≥0 audio effect), null when Remote can't show it
  "pg": 1, "pn": 4, "pgn": "Filter",    // page (0-based), page count, page name ("" = none)
  "bank": [9, 16] }                     // 1-based track positions of the bank (other modes)
{ "t": "foc", "on": false }             // no controller with knobs
```

- Sent once after connect and whenever any field changes.
- Display: `tn · dev · pg+1 / pn` in Device mode; `Volume · 9–16`, `Send A · 9–16` otherwise.

## Behaviour: Macros follow

When the phone follows Nota's selection (`proj.follow`), the controller focus changes, its
track is the phone's track and `md` is not null, Nota switches the phone's Macros device to it
(the phone receives the usual `mac` with the new `di`). A manual pick on the phone stays until
the focus changes again.

- A phone takes `mac.di` as its own device choice (so a later `sub` doesn't send a stale one).
- If the focus moved while another screen showed, the `sub` that opens Macros keeps Nota's
  device and ignores the phone's `i`; a `sub` while Macros already shows is a pick and wins.

## Phone → Nota

No new commands.
