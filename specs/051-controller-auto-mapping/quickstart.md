# Quickstart: validating controller auto-mapping (spec 051)

How to prove each slice works. Formats live in [contracts/](contracts/); entities in
[data-model.md](data-model.md).

## Prerequisites

- Release engine build (`build/` stays Release — a Debug engine hangs / xruns).
- `scripts/build.sh` (macOS) / `build-linux.sh` / `build-win.ps1` green — runs the smoke test.
- For hardware checks: Arturia KeyLab Essential 61 (reference device).
- For other devices without hardware: either `NOTA_CONTROLLER_TEST=1` + MCP `controller_inject`
  (headless), or on macOS an IAC Driver bus (Audio MIDI Setup → IAC Driver → "Device is online")
  driven by any MIDI sender, with a MIDI monitor on the bus to watch feedback.

## Gate (every slice)

```bash
scripts/build.sh
```

Expected: the smoke test passes, including `nota_midi_surface_selftest() == 0` (slice 1) and the
`ControllerTests` cases added by each slice.

## Slice 1 — engine port layer

Covered by the self-test: claimed vs unclaimed routing, whole-port claims, SysEx reassembly and
512-byte cap, output loopback. Manual sanity: plug any keyboard — notes and MIDI Learn behave
exactly as before (no claims are published yet).

## Slice 2 — Story 1 (plug in and it works)

1. Start Nota with no controller; connect the KeyLab Essential 61.
   **Expect** within a few seconds: "KeyLab Essential 61 connected — using the Arturia KeyLab
   Essential 61 profile"; Preferences → MIDI → Controllers lists it with the profile, switched on.
2. Press Play / Stop / Record / Loop on the controller → the transport follows.
3. Move fader 1 → track 1's volume follows; ⌘Z undoes the whole movement in one step.
4. Play keys / pads → notes reach the armed track as before.
5. Switch the controller off in Preferences → transport buttons no longer act; keys still play;
   restart Nota → still off.
6. Headless variant: `NOTA_CONTROLLER_TEST=1`, MCP `controller_inject` with the profile's port name
   and an Identity Reply, then `list_controllers` shows the profile; inject the Play note →
   `get_transport` reports playing.

## Slice 3 — Story 2 (knobs follow the device)

1. Select Nota Volt → card shows the knob marker on page 1 and "1 / n"; turn knob 1 → page 1's
   first parameter moves.
2. Page-next → marker moves to page 2.
3. Select a Compressor on another track → knob 1 moves its first page parameter.
4. Select a hosted plug-in → knobs drive its first eight parameters.
5. Select a device inside an Instrument Rack chain → knobs drive that device; select the rack →
   knobs drive its macros.
6. Knob mode Pan (mode button or the on-screen controller indicator) → knob N pans track N of the
   bank; back to Device → focused device again.
7. MCP `get_controller_focus` reports the same device, page and parameter names.

## Slice 4 — Story 3 (pickup)

1. Set a parameter to 80 % with the mouse, park knob 1 at 10 %; select another device and back.
2. Turn knob 1 slowly up → no change and a ghost tick on the on-screen knob until ~80 %, then it
   follows.
3. Switch pickup off for the controller → the same move jumps immediately.

## Slice 5 — Story 4 (Mackie Control)

With an MCU surface (or `controller_inject` with the generic MCU profile, then `controller_sent`):

1. Connect → strips show the first eight track names, fader positions, mute / solo / arm / select
   LEDs (inspect `controller_sent` for pitch-bend and scribble SysEx).
2. Drag track 3's fader in Nota → strip 3's motor fader moves (pitch bend on channel 3 within
   ~100 ms).
3. Touch fader 3 (note 106 on) while automation plays → no fader feedback while touched;
   automation is overridden; release → one undo step.
4. Bank → → strips show tracks 9–16; track headers mark the tracks under the surface.
5. Jog → playhead moves; time display shows the position.

## Slice 6 — Story 5 (make it mine)

1. Preferences → MIDI → Controllers → "Set up a controller…" on an unprofiled port; assign knobs
   1–8 and Play; finish → a file appears in `<DataDir>/controllers/`; reconnect → recognised.
2. Add `init` to that file, press "Reload profiles" → the SysEx is sent on next connect (MIDI
   monitor).
3. Break the file's JSON → Reload shows the file name and the error; Nota keeps working.
4. Edit a plug-in's page 1 to four chosen parameters → another instance follows; save the project,
   open it on a machine (or a clean `NOTA_DATA_DIR`) without those global pages → the project's
   pages are used; the global file there is unchanged.

## Slice 7 — Story 6 (HUI)

As slice 5 with the protocol forced to HUI on the port.
