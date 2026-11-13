# Contract: the `clips` message (Nota → phone)

Clip previews for the Scenes grid (#557). One text frame of JSON, alongside `ses` (which is
unchanged: slot state, length, progress).

```json
{
  "t": "clips",
  "all": false,
  "s": [
    { "id": 12, "i": 0, "nm": "Bass 1", "col": "#4FA3E0", "len": 16, "k": "n",
      "n": [[0, 4, 36], [8, 4, 36], [16, 2, 43], [24, 8, 41]] },
    { "id": 15, "i": 2, "nm": "Vox take", "col": "#D96A9A", "len": 32, "k": "p",
      "p": [3, 12, 40, 88, 99, 61, "…48 values"] },
    { "id": 12, "i": 3, "k": "x" }
  ]
}
```

| Field | Meaning |
|---|---|
| `all` | `true` on the first send to a phone that opened Scenes (or reconnected): the phone drops previews it holds for slots not in this batch series. `false` for change batches. |
| `s` | Slots: `id` track id, `i` scene index. |
| `nm` | Clip name as the desktop slot shows it (its own name, else "<track> <scene+1>"). |
| `col` | Clip colour as the desktop draws it (clip's own, else the track's), `#RRGGBB`. |
| `len` | Clip length in beats (2 decimals). For a recording slot: whole bars recorded so far. |
| `k` | `n` notes, `p` peaks, `x` the slot is empty now (drop its preview). |
| `n` | Note bars `[start, len, pitch]`: start and len in 1/64 of `len` (integers 0–64, len ≥ 1), pitch 0–127. At most 128. |
| `p` | 48 peak magnitudes, 0–99, normalised to the clip's loudest bucket. |
| `rec` | `true` while the slot is recording (the phone draws in the record colour). |

## Rules

- **C1** Sent only to a phone whose screen is Scenes (`screen` subscription, as `ses`).
- **C2** A slot is sent to a phone only when its signature differs from the one that phone
  last got for it. Nothing changes → nothing is sent.
- **C3** On a phone opening Scenes or connecting while on it: every filled slot, in batches
  of at most 64 slots, the first with `all: true` and following ones `all: false`.
- **C4** A recording slot is re-sent at most 4 times a second while it grows.
- **C5** An older phone ignores the message (unknown `t`).
- **C6** Group and return tracks are never in `s` (the phone's grid has no such columns).
