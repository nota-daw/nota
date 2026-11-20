# Quickstart: validate the mapping editor

## Automated

```bash
scripts/build.sh            # includes the smoke test (ControllerRemapTests)
```

`ControllerRemapTests` covers: single assign changes one entry; Move clears the other function;
two-way encoder Move clears both halves; Clear / Revert; feedback + init survive a round-trip;
a shipped edit saved with the same id overrides the shipped profile after reload; a command row
round-trips; nothing changed → identical JSON.

## By hand (KeyLab Essential 61 mk3, DAW port → shipped generic profile)

1. Preferences → MIDI → KeyLab DAW → **Edit mapping…** → the editor lists every function with
   its control; header says "Shipped profile".
2. Click **Undo / redo**, press a spare pad-bank button, then a second one → chips update, row
   marked; **Finish**. Press the button in the arrangement → undo. `<DataDir>/controllers/` now
   has `arturia.keylab-essential-61-mk3-daw.json`; init SysEx still makes the KeyLab enter DAW
   mode; transport LEDs still light.
3. Reopen → "Your copy of the shipped profile". Pick **Stop**, press the Play button → "drives
   Play" → **Move here** → Play shows "—"; Finish; the button stops the transport.
4. Without listening, move fader 3 → Fader 3 row flashes.
5. **+ Command…** → type "dupl" → Duplicate → press a spare button → Finish → it duplicates.
6. Restart Nota → all of the above is still there.
7. **Learn everything again** → the wizard starts with Knob 1 already learned; Enter through
   every step → Finish → profile unchanged.
8. **Back to shipped profile** → confirm → the file is gone, the shipped mapping is back.
9. Repeat 1–3 on a wizard-made profile (unprofiled port → wizard → Finish → editor).
