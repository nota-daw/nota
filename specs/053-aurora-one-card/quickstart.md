# Quickstart: validating Aurora one card and custom wavetables

**Feature**: `053-aurora-one-card` | **Date**: 2026-10-29

Commands run from the repo root. Performance numbers (§ 4, § 5) count only on the **036
reference machine** (i7-12700, Linux, performance governor;
`specs/036-aurora-perf/baseline-env.md`). Record them in `perf-baseline.md` in this folder.

## 0. Build and gates

```sh
scripts/build-linux.sh                     # native + managed + smoke test (macOS: scripts/build.sh)
dotnet run --project tests/Nota.SmokeTest -- --aurora-null    # 036 sound gate: factory presets bit-identical
dotnet run --project tests/Nota.SmokeTest -- --aurora-tables  # this feature's ABI-level tests
```

Expected:

- all green;
- `--aurora-null` reports −240 dBFS (bit-identical) for the ten presets, as in
  036 baseline-env;
- `--aurora-tables` covers the [c-abi](contracts/c-abi.md) and
  [state-blob](contracts/state-blob.md) contracts.

## 1. One card (US1)

1. `dotnet run --project src/managed/Nota.App`, add Aurora, and load the factory preset
   **Glacier Pad** (both oscillators on).
2. On the Osc tab, check that both windows, both type and warp selectors and 2 × 6 knobs are
   visible, and that there is no "1 / 2" switch ([ui-osc-tab](contracts/ui-osc-tab.md)).
3. Change Osc 2 to Chroma. Only Osc 2's window and sound change.
4. Switch Osc 2 off. Its column takes the unavailable look, and nothing moves.
5. Open a project saved by the base build. The same values show, and the 036 null gate
   (§ 0) is green.
6. Do steps 2–4 in Studio Dark and Studio Light, and at Interface scale 100 % and 150 %.
   Nothing overlaps (DESIGN.md § Graphs, "room for the words").

## 2. Import (US2)

Fixtures are written by `dotnet run --project tests/Nota.SmokeTest -- --aurora-fixtures <dir>`:

| File | Expect |
|---|---|
| `cycle-2048.wav` (one saw cycle) | 1 frame. Caption "1 frame". Plays at the note's pitch |
| `table-64x2048.wav` (`clm ` chunk) | 64 frames. Position sweeps through all of them |
| `table-300x2048.wav` | 256 frames, with the notice "first 256 frames" |
| `vowel-2s.flac` (stereo) | 16 frames (recording), morphing along the file |
| `long-90s.mp3` | 16 frames from the first 60 s, with the notice "shortened to 60 s" |
| `silence.wav` | refused: "This file is silent". The oscillator is unchanged |
| `broken.wav`, `x.aiff` | refused with the format message. The picker does not list AIFF |

For each file: drop it onto Osc 1's column, then use Osc 2's Import… for the next one.
Check that:

- the other oscillator is unaffected;
- Undo restores the previous table, and Redo re-applies it;
- picking a built-in and then the import name again switches back with notes still
  sounding.

Persistence:

1. Save the project as `t.nota`. Move the fixture folder away, then close and reopen.
   The sound is the same; render the same bar before and after for SC-007.
2. Look in `t.nota/samples/`: an identical table used by two Auroras is one file.
3. Delete one `samples/<hash>.wav` and reopen. The warning names the track, the oscillator
   and the file; the selector reads "Missing: <name>"; the oscillator plays its built-in
   table.
4. Save a user preset from an Aurora with a 256-frame table, then load it on a new track and
   on a second machine. The table travels with it. Then load a factory preset: both
   oscillators return to built-in tables.
5. Put the Aurora into an Instrument Rack chain and onto a Drum Rack pad, duplicate the
   track, copy and paste the device, and save and reopen. The table survives each step.

## 3. Audio behaviour

- **Click-free loops.** Hold C3 with each imported table and sweep Position. There is no
  click at the loop point, and no DC thump on note-on.
- **Aliasing.** Play `table-64x2048.wav` (bright frames) at C7 with Warp Off. Compare it
  with the built-in Chroma table at a similar brightness: there are no audible fold-back
  tones. The smoke test also checks the alias energy, at most −60 dB (T028).
- **Import during playback.** Play a chord loop and drop a table onto Osc 2. There is no
  dropout (the xrun counter stays at 0). Held notes may stop, which is the Mosaic
  precedent.
- **Memory.** Import ten 256-frame tables in a row. Process RSS rises by about 4 MB per
  undo step kept, and stays flat after clearing undo history.

## 4. UI CPU budget (US3, SC-003 / SC-004 / SC-005)

```sh
dotnet run --project tests/Nota.SmokeTest -- --aurora-ui-fixture /tmp/aui            # v28 scenario
dotnet run --project tests/Nota.SmokeTest -- --aurora-ui-fixture /tmp/aui --builtin  # v27 baseline
```

The fixture holds 4 Auroras, each with both oscillators on 256-frame generated tables (the
baseline uses Formant and Chroma instead). LFO 1 → Osc 1 position and LFO 2 → Osc 2 position
run at 2 Hz, over a 4-bar 6-note chord loop.

1. **Baseline**: the base build opens `/tmp/aui/baseline.nota`, with the Aurora card on the
   Osc tab and playback looping. After 10 s of warm-up:
   ```sh
   pidstat -t -p $(pgrep -f Nota.App | head -1) 1 60 > base.txt
   ```
   Average the `%CPU` of the row whose TID equals the PID (the UI thread).
2. **Feature**: the feature build opens `/tmp/aui/scenario.nota` the same way, with
   `NOTA_UI_PROBE=aurora` set:
   ```sh
   NOTA_UI_PROBE=aurora dotnet run -c Release --project src/managed/Nota.App -- /tmp/aui/scenario.nota 2> probe.txt
   pidstat -t -p <pid> 1 60 > feat.txt
   ```
3. **Pass**:
   - the UI thread in `feat.txt` is at most the `base.txt` value + 2.0 points, and the
     card's share is at most 5 % (SC-003);
   - `probe.txt` "aurora paint p99" is at most 2.0 ms (SC-004);
   - clicking through the 4 Aurora tracks, "aurora card build→first paint" is at most
     100 ms, and the longest UI frame is at most 50 ms (SC-005).
   Run everything twice; the runs agree within ±10 %.
4. **Hidden**: minimise the window for 30 s. The probe reports 0 paints (FR-018).

## 5. Audio cost (SC-006)

```sh
cmake -G Ninja -S tests/perf -B build/perf-tools -DCMAKE_BUILD_TYPE=Release
cmake --build build/perf-tools --target nota_perf
./build/perf-tools/nota_perf run aurora-supersaw-2x 30   # within ±10 % of ~28 ms/s (036)
./build/perf-tools/nota_perf run aurora-imported-2x 30   # ≤ 1.10 × aurora-supersaw-2x
```
