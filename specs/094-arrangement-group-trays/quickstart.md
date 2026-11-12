# Quickstart: verifying Arrangement group trays

## Prerequisites

- A Release engine build in `build/` (`scripts/build.sh` once).
- A test project: a top-level group "Group" holding a group "DRUMS" (two tracks: one MIDI,
  one Audio) and a group "Instruments" (four tracks), then a top-level MIDI track
  "Nota Consort". Fold "Instruments".

## 1. Smoke test (layout rules)

```bash
scripts/build.sh
```

Expect the new `GroupTrayTests` lines to pass, among them:

- a depth-2 track's content starts ≤ 12 px further in than a top-level track's (new look);
- DRUMS closes on "Audio 4", Instruments and Group both close on the folded Instruments row
  (inner first), nothing closes on "Nota Consort";
- the folded Instruments row reports 4; a folded group holding a nested group counts only
  tracks;
- classic spines at 0 / 10 / 20 px for depths 0 / 1 / 2;
- no row's closing strips leave less than 20 px on a Compact (30) or slim group (26) row.

## 2. Screens (both looks × four themes × three row heights)

Use the headless-Avalonia harness (MainWindow with its view model, engine-built tracks,
`CaptureRenderedFrame`), or run the app and screenshot:

1. New look, Studio Dark, Standard rows: trays as in
   [contracts/header-geometry.md](contracts/header-geometry.md); one colour stripe per
   member; "Nota Consort" clearly outside the tray; DRUMS group row's meter on the right.
2. Same at Compact and Large rows: meters fully inside each card (zoom on the bottom-right
   corner of a Compact track row — the meter stops 6 px above the edge).
3. Studio Light, Ember Graphite, Ember Paper: washes readable, text and chips legible.
4. Preferences → Appearance → Look: Classic: headers switch at once to the staircase;
   compare with 0.59.2 (`git worktree add /tmp/n0592 v0.59.2`, same project) side by side.
5. Back to New: trays return; fold states, row heights and the selection are kept.
6. Narrow the header column to its minimum: names trim, the M chips of all rows stay in one
   column.

## 3. Interactions

- Fold / unfold DRUMS and Group from the chevron: trays close on the right rows, the count
  appears only while folded.
- Select a group: its full card (fader, pan, meter) sits as the tray head.
- Drag a track out of DRUMS to the top level and back: trays redraw.
- Drag a row seam inside a tray: the tray follows, rows stay aligned with lanes.

## 4. Done when

All smoke-test lines pass, the screens match the contract in both looks and four themes,
and `/speckit-converge` reports Converged.
