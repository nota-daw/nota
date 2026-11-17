# Quickstart: validating custom keyboard shortcuts (096)

## Automated

```bash
scripts/build.sh
```

The smoke test gains `KeyBindingTests` (catalog and conflict rules, persistence round trip,
reserved keys, reset, update migration) and an extended `PaletteTests.RegistryDrift` (every
action is laid out on the Shortcuts page; the defaults have no conflict). All must pass.

## In the app (macOS, then Windows / Linux when available)

Use a scratch data dir: `NOTA_DATA_DIR=/tmp/n096data`.

1. Settings → Shortcuts: every keyboard row shows a key-cap button; mouse rows are quiet text.
2. Split at playhead → click → press ⌘T → Confirm. Edit menu shows ⌘T. ⌘T splits, ⌘E does
   nothing. The row has the edited dot and "default ⌘E".
3. Piano roll "Transpose up" → ⌘D → conflict line names Duplicate → Replace → Duplicate shows
   "—" and edited. ⌘D in the piano roll transposes.
4. Arrangement "Nudge left" → ⇧F (a piano-roll key) → accepted with the "elsewhere" note.
5. Capture ⌘Q (macOS) / Alt+F4 → refused with the reason.
6. Notes: C → Q. Arm a MIDI track, Q plays C4, A doesn't.
7. Edited only → the four rows above. Reset one → back to default (Replace-check if taken).
   Reset all → confirm → nothing edited.
8. Restart with edits: they survive. Hand-edit `settings.json`: an unknown id and a junk key
   are skipped, the app starts.
9. Hover Record: the tooltip shows the current key after rebinding ⌘R.
10. Palette (⌘⇧P) "split": shows the current key.
