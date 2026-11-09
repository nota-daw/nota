# Contract: import formats

What the importers read. Everything below is taken from real files (a Babylonwaves Art Conductor
map for Cinematic Studio Strings, supplied by the owner and read locally; they are third-party
works and are not in the repo); anything not shown by those files is listed as unknown, not
guessed. The smoke test checks the importers on small maps written in the same shape.

## Cubase `.expressionmap` (XML, root `InstrumentMap`)

Source: `expressionmaps/CSSS Violins 1 A.expressionmap`.

```
InstrumentMap
  string name="name"                                   map name
  member "slotvisuals"  -> list of USlotVisuals        a copy of each slot's visuals (ignored)
  member "slots"        -> list of PSoundSlot          one articulation each
    obj PSlotThruTrigger "remote"                      remote key (status 144, data1)   -- ignored
    obj PSlotMidiAction  "action"
      member "midiMessages" -> list of POutputEvent    ordered output: {status, data1, data2}
      int channel (-1 = note's own), key, key2, controller1num, controller1value (editor mirrors)
    member "sv" -> list of USlotVisuals                text (short name), description, group,
                                                       articulationtype, symbol, displaytype
    member "name" -> string "s"                        articulation name
    int color                                          Cubase palette INDEX (not RGB)
```

| Source | Becomes |
|---|---|
| `POutputEvent` status 144 | keyswitch note (`data1` pitch, `data2` velocity) |
| status 176, `data1` = 0 / 32 | bank select MSB / LSB (`data2`) |
| status 176, other `data1` | control change |
| status 192 | program change (`data1`) |
| any other status | warning, kept in `metadata["unsupported.midi"]`, not sent |
| status low nibble 0 | channel = note's own; non-zero nibble n -> channel n + 1 |
| slot `color` | `sourceColorIndex`; `colorArgb` stays null |
| `sv/group` > 0 | `groupId`; the group's name stays empty (the file has none) and the interface shows a numbered, translated label |
| slot with no name | name `#N` (its 1-based position): saved data is language-neutral, never interface text |
| first slot with no name and no events | skipped, reported |

Seen in the owner's file: 45 slots, 44 articulations, events 144 (88) and 176 (44), every
articulation has three actions, channel -1 throughout, group 0 throughout.

**Unknown** (no file shows it): program change and bank select events, non-zero groups,
slot-level channels other than -1, latching vs momentary (not stored). Two interpretations are
assumptions, not facts from a Cubase file: status 192 is a program change (MIDI standard), and a
status low nibble of 0 means "the note's own channel" (the owner's file writes nibble 0 together with
slot channel -1; whether Cubase treats nibble 0 as channel 1 is unknown). Both are covered by
synthetic tests only and the import preview must label them unverified.

## Studio One `.keyswitch` (XML, root `Music.KeySwitchList`)

Sources: the owner's `CSSS Violins 1 Attribute.keyswitch` and `CSSS Lite Ensemble Attribute.keyswitch` (read locally, not in the repo).

```
<Music.KeySwitchList name="...">
  <Attributes name="Long" id="1" pitch="24" activation="note24.1|note35.1|unknown"/>
```

| Token in `activation` | Becomes |
|---|---|
| `noteP.V` | keyswitch pitch P, velocity V (`note24.1` = Cubase 144/24/1) |
| `noteP` | keyswitch pitch P, velocity 127 (`note35` = Cubase 144/35/127) |
| `unknown` | nothing; reported (in the CSS twin it is the CC 58 action) |
| anything else | warning, skipped |

Entry `id` 0 / name `---` with an empty activation is the empty default and is skipped. Colours,
groups, channels and switch mode do not exist in this format.

Verified: all 44 entries of the Violins file have keyswitch notes equal to the Cubase map's for the
same name (checked by script on the real files and by the smoke test).

## Studio One `.soundvariation`

**No example file has been supplied.** There is no parser, no claim of support and no test. When a
real file arrives, the first task is to read it and extend this contract before writing code.

## Common rules for every importer

- Decide by content (`Sniff`), not extension.
- Refuse files over 16 MiB; XML with `DtdProcessing.Prohibit` and no resolver.
- Never modify the source file (a test hashes it before and after).
- Return a map plus a list of messages (info, warning, error); an error returns no map.
- Never throw to the caller for bad content; only I/O errors are caught and reported.
