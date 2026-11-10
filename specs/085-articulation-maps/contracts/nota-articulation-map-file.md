# Contract: Nota articulation map file (JSON)

Used in three places with the same shape: the library folder (`articulations/*.notamap`), the
`export` command, and `TrackDto.ArticulationMap` inside a project (embedded as a JSON object, not a
string). UTF-8, camelCase, written with `System.Text.Json`. Properties this version does not know
are ignored on read (not preserved); a newer `version` is refused instead (see Rules).

```json
{
  "format": "nota-articulation-map",
  "version": 1,
  "id": "3f9c...32 hex",
  "name": "CSSS Violins 1 Attribute",
  "instrumentName": "Cinematic Studio Strings",
  "description": null,
  "defaultArticulationId": 1,
  "groups": [ { "id": 1, "name": "Group 1" } ],
  "articulations": [
    {
      "id": 1,
      "name": "Long",
      "shortName": "Long",
      "colorArgb": 4283215696,
      "sourceColorIndex": 2,
      "groupId": 0,
      "trigger": "latching",
      "actions": [
        { "kind": "keyswitch",     "channel": null, "data1": 24, "data2": 1 },
        { "kind": "keyswitch",     "channel": null, "data1": 35, "data2": 1 },
        { "kind": "controlChange", "channel": null, "data1": 58, "data2": 81 }
      ],
      "noteOffsetMs": 0,
      "triggerLeadMs": 0,
      "enabled": true,
      "metadata": { "description": "Long" }
    }
  ],
  "metadata": { "source.format": "expressionmap", "source.file": "CSSS Violins 1 A.expressionmap" }
}
```

## Rules

- `format` must be `nota-articulation-map`; `version` higher than the reader supports is refused
  with a message, not partially read.
- `kind` is one of `keyswitch`, `controlChange`, `programChange`, `bankSelectMsb`, `bankSelectLsb`.
- `trigger` is `latching` or `momentary`.
- Loading repairs deterministically and reports what it repaired: a missing or duplicate
  articulation id gets the next free id (notes keep pointing at the file's ids, so a repaired id is
  reported as a warning); an action out of MIDI range is dropped; a default id that does not exist
  becomes null; offsets are clamped to ±2000 ms and leads to 0..2000.
- A file larger than 16 MiB, nested deeper than 64 levels, or that is not valid JSON is refused.
- A missing name becomes the file name (or "#" + the map id inside a project); a missing map id gets a new one.
- Disabled articulations are written and read like the others; only the engine table leaves them out.
- Exporting then importing yields an equal map (tested).
