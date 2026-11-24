# Contract: Controller mapping editor

## Window

Title: "<controller> — controller mapping". 720 × 640, resizable in height, Nota dialog look.

```
┌──────────────────────────────────────────────────────────────────────────────┐
│ KeyLab Essential 61 mk3 DAW                         [Learn everything again] │
│ Shipped profile · changes are saved as your own copy                          │
│ ┌─ All ─┐ Knobs 8  Faders 9  Transport 6  Track & page 6  Session 6  Extra 4  │
│ Other 12          [ ] Unassigned only            ● CC 21 · ch 1 · 64          │
│──────────────────────────────────────────────────────────────────────────────│
│ KNOBS                                                                         │
│  ◯ Knob 1                        CC 74 · ch 1 · relative             ×        │
│▌ ◯ Knob 2      Turn or press a control…                              Esc      │
│ TRANSPORT                                                                     │
│  ▶ Play                       •  —                                    ↺       │
│  ■ Stop                       •  Note 93 · ch 1                       ↺ ×     │
│     Note 93 drives Play.  [Move here]  [Cancel]                               │
│ EXTRA BUTTONS                                                                 │
│  ↶ Undo / redo        Note 20 · ch 1   Note 21 · ch 1                 ×       │
│  + Command…                                                                   │
│──────────────────────────────────────────────────────────────────────────────│
│ [Cancel]   Click a function, then press a control.               [Finish]    │
└──────────────────────────────────────────────────────────────────────────────┘
```

- Header: controller name (title), profile line by origin:
  shipped → "Shipped profile · changes are saved as your own copy";
  user copy of shipped → "Your copy of the shipped profile" + **Back to shipped profile** (ghost);
  user → "Your profile". **Learn everything again** (ghost) on the right.
- Group strip: chips "All" + each group with its assigned count; click scrolls to the group.
  "Unassigned only" toggle. Live readout (activity dot + last message) on the right.
- Rows (height 34): icon, name, control chip(s) (Mono, tabular), changed dot (accent), hover
  actions: revert ↺ (changed rows), clear × (assigned rows), remove (added command rows).
  Pair rows show two chips (left, right) or one chip "encoder" when two-way.
- Listening row: solid accent bar on the left + soft accent tint, text "Turn or press a control…"
  (pair: "Press the first button or turn an encoder" → "Now the second button").
  Ambiguous encoder: inline encoding combo + **Use** button.
- Conflict: an inline line under the row, warning colour: "<control> drives <function>." +
  **Move here** (primary) / **Cancel**.
- Selected (not listening) row: soft tint.
- "Other" group: read-only rows (no listening), at the end.
- Footer under a seam: Cancel (ghost), status text, Finish (primary; enabled always — no changes
  just closes).

## Keys

| Key | Action |
|---|---|
| ↑ / ↓ | move selection (stops listening) |
| Enter | listen on the selected row (in conflict: Move here; ambiguous: Use) |
| Delete / Backspace | clear the selected row |
| Esc | stop listening / cancel conflict; when idle: Cancel the window |
| Cmd/Ctrl+Enter | Finish |

## Find by touch

Idle, a captured message whose control is in the mapping → its row is selected, scrolled into
view and flashes; otherwise status "<control> isn't mapped — pick a function to give it one."

## + Command…

Flyout under the row: search box (focus), list of palette commands "Title — Category",
excluding ids that already have a row; Enter / click adds a row in Extra buttons and starts
listening on it. A command row with no control is dropped on save.

## Profile write rules

1. Nothing changed → Finish writes nothing.
2. Shipped → `<userDir>/<shipped file name>`, same `id`; user → its own file, in place.
3. Only `controls` is rebuilt; every other property kept verbatim.
4. Unchanged entries keep their JSON; new entries: `role`, `index`, `command`, `commandLeft`,
   `roleLeft`, `message`, `channel`, `number`, `encoder` as `ProfileWriter` writes them, plus
   `feedback` / `motorised` from the original entry of the same physical control.
5. After saving: `ReloadProfiles()`; the controller's choice stays as it was.
