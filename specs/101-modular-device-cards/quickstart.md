# Quickstart: validating #656

Prerequisites: a Release build (`scripts/build.sh` green), and for Q2 a VST3/AU plug-in
with ≥ 1,000 parameters (any large synth). Run the app against a scratch data dir:
`NOTA_DATA_DIR=$(mktemp -d)`.

The automated part:

```bash
dotnet run -c Release --project tests/Nota.ModularUiTest
```

It runs headless (no audio device) and prints `PASS` / `FAIL` for each check
below that is marked *(auto)*.

## Q1 — Picker on a node (Story 1, FR-001…FR-008)

1. Create an audio track with a Compressor. Open Modular and add an LFO.
2. Drag the LFO's CV out onto the compressor node's header and release.
   → The picker opens at the pointer with focus in the search field.
3. Type `thr` and press Enter. → A link to Threshold exists, a CV input labelled
   "Threshold" is on the node, and the panel is closed.
4. Repeat, then press Esc. → No new link.
5. *(auto)* The model filters, skips headings, uses fallback labels, and shows empty
   states.

## Q2 — Big plug-in (SC-001, SC-002, SC-003)

1. Load the large plug-in as an instrument. Drop an LFO cable on its node, away from
   any knob.
2. → The panel appears without a visible delay, is not taller than 420 px, and
   scrolls smoothly to the end.
3. Type part of a known parameter name and press Enter, within 5 s of the drop.
4. *(auto)* Filtering 5,000 synthetic entries takes < 50 ms per keystroke. The app
   logs open time when `NOTA_DEBUG_PICKER=1`, and it is < 150 ms.

## Q3 — Cross-track and right-click (Story 2, FR-002, FR-002a)

1. Track B has a Delay and an Auto Filter. From track A's LFO, drop the cable on B's
   header. → The picker is grouped Delay / Auto Filter. Search `freq` and confirm the
   matches stay under their headings. Enter links the B parameter.
2. In Modular, right-click a knob → "Modulate from a parameter" → the picker lists
   source params grouped by device. → "Modulate from another track" → modulators
   grouped by track.

## Q4 — Cards, sizes, drop on knob (Stories 3–4)

1. A track with Compressor (has S), an Audio Effect Rack (L only) and a plug-in. Open
   Modular. → Compressor at S, rack at L, plug-in at S.
2. Turn a knob on the Compressor card. → Detail shows the same value. Undo restores it.
3. Switch the Compressor to XS, then L. → Cables stay attached, the top-left stays, and
   other nodes don't move. *(auto: port anchors match the new width.)*
4. Look at Detail. → Its card size didn't change. *(auto: the `View` param is
   unchanged.)*
5. Drag an LFO cable over the Compressor card's Ratio knob. → The knob gets a brass
   outline. Release. → Linked to Ratio with no picker. *(auto)*
6. Save, close and reopen the project. → Sizes and positions are the same. *(auto:
   sidecar round-trip, and an old `[x, y]` file loads with defaults.)*

## Q5 — Load (SC-004)

A track with 6 devices (2 synth-heavy L cards). Switch to Modular. → The first draw
takes < 300 ms. Play automation on a card knob. → The Modular knob and the Detail knob
move together at 60 Hz with no audio xruns.

## Q6 — Themes

Repeat Q1 step 2 and Q4 step 1 in each of the four themes. → The picker, the chip and
the brass outline read correctly.
