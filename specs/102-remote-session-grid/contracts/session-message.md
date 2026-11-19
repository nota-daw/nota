# Contract: the `ses` message and Session commands (spec 102)

## Nota → phone: `ses`

```jsonc
{
  "t": "ses",
  "n": 8,                       // scene count
  "tr": [
    { "id": 5, "k": [6, 7], "c": [] },              // a group: k = member track ids, no slots of its own
    { "id": 6, "c": [[3, 4, 0.25], [1, 8, 0], …] }, // a track: per scene [state, length beats, progress]
    { "id": 7, "c": [[0, 0, 0], …] }
  ]
}
```

- Entries are in project order; return tracks are never sent.
- `k` is present only on groups: every non-group, non-return track below the group, nested
  groups included. A group with no members sends `k: []`.
- A phone that doesn't know `k` treats a group as a track with no clips (empty cells).

## Phone → Nota

| Message | Track id may be a group | Effect on a group |
|---|---|---|
| `{ "t": "launch", "id": G, "i": scene }` | yes | launches each member's slot in `scene` |
| `{ "t": "stopSlot", "id": G }` | yes | stops each member |
| `{ "t": "scene", "i": scene }` | — | unchanged |
| `{ "t": "stopAll" }` | — | unchanged |

Play-only phones are refused as before.
