# Contract: controller profile file (format 1)

A user-facing format (FR-005): one JSON file per controller. Shipped profiles are embedded in the
app; user profiles live in `<DataDir>/controllers/` (macOS `~/Library/Application Support/Nota/
controllers/`, Windows `%APPDATA%\Nota\controllers\`, Linux `~/.config/nota/controllers/`). A user
profile with the same `id` as a shipped one replaces it; otherwise matching rules decide (below).
The user docs page "Controllers" documents this format with the same example.

```jsonc
{
  "format": 1,
  "id": "arturia.keylab-essential-61",
  "name": "Arturia KeyLab Essential 61",
  "protocol": "generic",                      // "generic" | "mcu" | "hui"
  "source": "Arturia KeyLab Essential — User Manual, MIDI implementation chart (v1.x)",
  "match": {
    "portNames": ["^KeyLab Essential 61( MIDI)?$", "^Arturia KeyLab Essential 61"],
    "identity": { "manufacturer": "00 20 6B", "family": "02 00", "model": "05 54" }
  },
  "init":    ["F0 00 20 6B 7F 42 02 00 40 51 00 F7"],   // sent on connect (e.g. DAW mode)
  "release": [],                                        // sent when Nota lets go
  "pickup": true,
  "controls": [
    { "role": "knob",   "index": 1, "message": "cc", "channel": 1, "number": 74, "encoder": "relative-twos" },
    { "role": "fader",  "index": 1, "message": "cc", "channel": 1, "number": 73 },
    { "role": "play",   "message": "note", "channel": 1, "number": 94,
      "feedback": { "on": "90 5E 7F", "off": "90 5E 00" } },
    { "role": "page-next", "message": "cc", "channel": 1, "number": 113 },
    { "role": "fader",  "index": 9, "message": "pitchbend", "channel": 9, "motorised": true }
  ]
}
```

*(Numbers above illustrate the shape only; real values come from the cited document.)*

## Fields

| Field | Req. | Values |
|-------|------|--------|
| `format` | ✔ | `1`. A higher value → the file is skipped: "needs a newer Nota". |
| `id` | ✔ | `[a-z0-9.-]+`, unique. |
| `name` | ✔ | display name. |
| `protocol` | – | `generic` (default) uses `controls`; `mcu` / `hui` use the built-in codec and ignore `controls`. |
| `source` | shipped ✔ | the manufacturer document; required for shipped profiles (licensing, FR-007). |
| `match.portNames` | one of | .NET regexes, case-insensitive, matched against the port name. |
| `match.identity` | one of | hex bytes from the Identity Reply: `manufacturer` (1 or 3 bytes), optional `family` (2), `model` (2). |
| `init` / `release` | – | hex strings, each one or more complete MIDI messages. |
| `pickup` | – | default for the per-controller pickup switch (`true`). |
| `claimAll` | – | `true` = every channel message of the port is a control (a keyboard's DAW port): the whole port is claimed, so an undeclared button or touch note never plays a note. Default `false`. |
| `controls[]` | generic | see below. |

### `controls[]`

| Field | Req. | Values |
|-------|------|--------|
| `role` | ✔ | `knob`, `fader`, `button`, `pad`, `master-fader`, `play`, `stop`, `record`, `loop`, `rewind`, `forward`, `page-next`, `page-prev`, `bank-next`, `bank-prev`, `track-next`, `track-prev`, `mode-device`, `mode-volume`, `mode-pan`, `mode-send`, `jog`, `command` |
| `index` | knob / fader / button / pad | 1-based |
| `command` | command | the command a `command` button runs once per press — an id from the command palette's catalog (`edit.undo`, `edit.redo`, `file.save`, `transport.metronome`, `transport.tapTempo`, `roll.quantize` …); one that isn't available at the moment (nothing to undo, no piano roll open) does nothing |
| `roleLeft` | – | a two-way button role on an encoder or knob: `role` per step right, `roleLeft` per step left — only the other half of a pair: `track-next` / `track-prev`, `bank-next` / `bank-prev`, `page-next` / `page-prev`, `forward` / `rewind` |
| `commandLeft` | – | a two-way `command` control (an encoder or knob): `command` runs once per step turned right (a knob: its value going up), `commandLeft` once per step left — e.g. `session.selectDown` / `session.selectUp` |
| `message` | ✔ | `cc`, `cc14` (MSB CC `number`, LSB `number + 32`), `nrpn` (`number` 0..16383), `note`, `pitchbend` |
| `channel` | ✔ | 1..16 |
| `number` | except pitchbend | 0..127 (`nrpn` 0..16383) |
| `encoder` | – | `absolute` (default), `relative-twos`, `relative-signbit`, `relative-offset` |
| `motorised` | – | `false` (default); motorised controls skip pickup and receive value feedback |
| `feedback` | – | `{ "on": hex, "off": hex }` for buttons; `{ "value": hex-template }` for rings / motor faders — `vv` = 7-bit value, `ll hh` = 14-bit |

## Matching (research R5)

1. User profiles before shipped.
2. Identity match before port-name match; more identity fields beat fewer. A profile that names
   **both** an identity and port names needs both to match — a multi-port device answers the
   identity on one port or all of them (the KeyLab Essential mk3 answers only on its "MIDI" port).
3. Ties → file name order. The choice can always be overridden per port in Preferences.

## Claiming

A `generic` profile claims exactly the (message, channel, number) of its `controls`, except
`pad` and `button`, which stay unclaimed: pads keep playing notes, and a generic `button N` has no
built-in action in format 1 — the role only names it, and it stays free for MIDI Learn. `mcu` / `hui`
claim the whole port. A control that also has an explicit MIDI Learn mapping is left unclaimed and
routed to MIDI Learn (FR-012).

## Errors

Invalid JSON, unsupported `format`, missing `id` / `name` / `match` → file skipped. Bad control
entries → that control skipped. Every skip is listed with file name and reason in Preferences →
MIDI → Controllers (Story 5 scenario 2). Nothing is ever fatal.
