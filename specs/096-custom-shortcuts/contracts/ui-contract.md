# UI contract: Preferences → Shortcuts (096)

Follows `DESIGN.md` (Studio): flat grey shell, one accent in three strengths (solid = live,
soft tint = picked, accent line = values), Geist with tabular figures, sentence case.

## Page header

`[ Filter shortcuts… ] [Edited only ◻] [Reset all to defaults…]` — the velocity-wheel section
stays above. "Reset all" is disabled when nothing is edited. A "Needs a decision" group
appears first when an update produced conflicts (R6).

## Editable row (34 px, as today)

```
[⌘][E]  [+]            Split at playhead                       • ⌘E  ↺
 primary  alternative   action title                          edited mark, default, reset
```

- Key caps are a button (hover: soft tint). Click → capture. The alternative slot shows its
  caps, or a quiet `+` when empty.
- Edited: accent dot before the title, the default as quiet caps ("default ⌘E"), and a reset
  button `↺` (tooltip "Reset to default"). Unassigned shows "—" in the key column.
- Read-only rows: key text in tertiary colour, no button, a "fixed" hint on hover.

## Capture (in place, one row at a time)

```
[ Press a shortcut…  ]   Confirm  Cancel  Clear  Assign Esc
[ ⌘ ][ T ]               Confirm  Cancel  Clear
⚠ ⌘T is used by Transpose up (Piano roll).     Replace  Cancel
ⓘ Elsewhere ⌘T is New track — the piano roll wins while it has focus.
⛔ ⌘Q is reserved by macOS.
```

- The field has an accent outline while capturing; nothing fires while it is active.
- Confirm is the primary button; it is hidden while a conflict line shows (Replace takes its
  place). Esc = Cancel. Clicking elsewhere or leaving the window = Cancel.

## Reset all

Confirmation dialog: "Reset all shortcuts? Your N custom keys are replaced by the defaults."
`[Cancel] [Reset all]`.

## Filter

Words match the group title, the action title, aliases and the current keys in both spellings
(⌘T / "cmd t" / "ctrl+t"). "Edited only" narrows to edited rows (and "Needs a decision").
