# Quickstart: verifying the clip editor batch

## Prerequisites

- Release engine build in `src/native/nota.engine/build` (BUILD.md; `build/` stays Release).
- A scratch data dir: `export NOTA_DATA_DIR=$(mktemp -d)`.
- A drum loop with a known tempo (e.g. 120 BPM, 2 bars) and a longer unwarped sample.

## 1. Build and smoke test (the PR gate)

```bash
scripts/build.sh
```

Expect green. The pure mapping / tempo / remap / label math runs in its own section:

```bash
dotnet run --project tests/Nota.SmokeTest -c Release -- --clipeditor
```

Expect every check `ok`: `ClipTimeMap` round trips (warped, trimmed, reversed, unwarped),
`ClipTempo` (one segment equals the chip, scaling keeps segment ratios, ×2 / ÷2 range),
`WarpRemap` (identity when unchanged, monotone, end markers fixed), `RulerLabels` (bars and
time at each step size) — see [data-model.md](data-model.md).

## 2. Playhead (US1, #277 / #278)

1. Audio clip at 2.1, open it. Stop with the song position at 3.1 → playhead drawn; move to
   5.1 → gone.
2. Click the editor ruler at a hit → transport and arrangement playhead jump there (snapped
   with Warp + Snap on; free with Alt). Drag → scrubs. Space → plays from there.
3. Drag the playhead line over the wave → scrubs. In the piano roll: drag the line on the
   ruler and in the note grid; a click on the line without movement adds a note.
4. Reverse the clip, trim its Start bracket in → clicks still land where that audio plays.
5. Open a Session-slot clip in each editor → ruler and line show 🚫 and the tooltip; nothing
   seeks.

## 3. Rulers (US2, #275)

1. MIDI clip at 9.1 → ruler reads 9, 10 …; two MIDI clips selected → unchanged behaviour.
2. Warped audio clip at 9.1 → 9 at the Start bracket; trim the bracket in one bar → the
   bracket reads 9, audio before it 8. Unwarped clip → bars too.
3. Click the transport read-out → arrangement, piano-roll and audio rulers switch to m:ss;
   click again → bars. The labels at the clip start match the read-out when the playhead is
   there.

## 4. Keyboard (US3, #114)

1. Reproduce the report first: open an audio clip with ADSR shown, click the ADSR / props
   rail buttons, press Space and ⌘1–⌘5. Record what happened before the fix in research.md
   R4.
2. After the fix, for each region (props rail, wave, ADSR, envelope, piano-roll grid, MIDI
   tools) click it, press Space twice and ⌘1 → transport toggles, no button under focus
   re-activates, the grid changes and the status line names it.
3. Type into a field (clip name, BPM, ADSR value) → spaces type; Enter → Space plays again.
4. Repeat 2 with the detail panel floating.

## 5. Warp tempo (US4, #248)

1. Warped 120 BPM loop → Tempo reads 120.00. ÷2 → 60.00, the clip spans half the beats
   (a higher recorded tempo means more beats, so ×2 doubles the length); undo once → 120.00. ×2 / drag / type 87 → each one undo step.
2. Clip with several markers → tempo change keeps the chips' ratios.
3. Unwarped sample → Tempo shows the detected value or "—"; type 100 → Warp switches on at
   100 BPM; one undo returns to unwarped.

## 6. Live warp drag (US5, #140)

1. Drag a middle marker slowly → waveform on both sides stretches live, chips update.
2. Release → one undo step; waveform replaced by the engine's render (no visible jump
   beyond detail).
3. Drag again and press Esc → everything returns; nothing in undo.
4. While playing, drag a marker → no dropouts until release.

## 7. Screenshots for the PR

Headless harness per the UI-verify recipe (docked clip editor at 1440×900, Studio Dark and
Studio Light): audio editor with the playhead stopped inside the clip, the Tempo row, the
ruler in bars and in time, the piano roll ruler at bar 9, and a mid-drag warp preview.
Before / after pairs for #277, #275, #248.
