# UI contract: Modular nodes and the parameter picker

The user-facing behaviour that implementation and tests check against. Visuals follow
DESIGN.md / nota-design and work in Studio Dark, Studio Light, Ember Graphite and Ember
Paper.

## 1. Device node

```
┌──────────────────────────────────────────┐
│ ● Compressor        DYNAMICS   XS S L  ✕ │  header (category tint, 34 px)
├──────────────────────────────────────────┤
│ ◉ Audio in                    Audio out ◉│  port block — fixed offset under the header
│ ◆ Threshold                              │  one CV input per modulated param
├──────────────────────────────────────────┤
│                                          │
│       S or L: the Detail card body       │  XS: today's parameter rows (≤ 8, scroll)
│                                          │
└──────────────────────────────────────────┘
```

- Size chip `XS · S · L`: the current size is the soft-tint "picked" state. S is
  absent when the device has no S. Clicking a segment resizes in place (top-left
  fixed, cables attached; user-placed nodes stay, auto-placed ones re-flow) and saves.
- Dragging the header moves the node. A click on the body goes to the card's own
  controls and never starts a node drag, chain move or fold.
- Bypassed: header power state as today; the body dims the way it does in Detail.
- Global view, modulators, In / Out: unchanged, no chip.

## 2. CV cable drag over a node

| Pointer is over | Feedback | On release |
|---|---|---|
| a parameter control on this node (card knob/value, XS row) | brass outline on that control; node outline also brass | link that parameter directly |
| elsewhere on a valid target node | node brass outline (as today) | open the picker (target list) |
| a Math / Scope node | as today | as today |
| another track's header (left column) | as today | open the picker (grouped cross-track list) |
| empty canvas | — | nothing |

## 3. Picker panel

- Opens at the drop point (or at the right-clicked parameter), and is kept on screen.
- Width 320. Height grows with content up to 420 and then the list scrolls.
- Layout: a title (target device or track name, or "Modulate *param*"), the search
  field (focused, placeholder "Search parameters"), then the list.
- List rows: 22 px; parameter label, plus muted group text when the search spans
  groups. Headings are 22 px, sentence case, ink 3, and not pickable.
- Keys: typing filters on every keystroke. ↑ / ↓ move the highlight (skipping
  headings). PageUp / PageDown jump a page. Enter picks. Esc closes.
- Mouse: hover highlights, click picks, and a click outside closes.
- Empty states: "No parameters" / "No matching parameters" (muted row, nothing pickable).
- Picking closes the panel, creates the link exactly like the old menu item did, and
  rebuilds the graph.

| Caller | Entries | Grouped |
|---|---|---|
| drop on a node (FR-001) | that device's modulatable params | no |
| drop on another track (FR-002) | MIDI FX → instrument → effects, each device's params | by device |
| right-click "Modulate from a parameter" (FR-002a) | this track's other device params | by device |
| right-click "Modulate from another track" (FR-002a) | modulators on other tracks | by track |

## 4. Strings (EN keys; RU twins required — FR-021)

`modular.size_xs`, `modular.size_s`, `modular.size_l`, `modular.size_tip`,
`modular.picker_search`, `modular.picker_no_params`, `modular.picker_no_matches`,
`modular.picker_param_fallback` ("Parameter {0}"), `modular.picker_title_modulate`
("Modulate {0}").
