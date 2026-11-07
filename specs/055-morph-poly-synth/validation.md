# Validation results: Nota Twin (specs/055)

The quickstart run (T081), recorded for the PR description. macOS (Apple Silicon), Release engine.

## 1. Build and smoke

- `scripts/build.sh`: the engine and the app build. The smoke test in that run had 4653 PASS
  and 2 FAIL: the two "overloaded … late blocks" render-pool checks on Synth tracks. That pair
  is unstable on this machine and fails on the base engine too.
- Smoke test run again on its own, after the convergence tasks (T094–T099): **SMOKE TEST
  PASSED** (4653 PASS, 0 FAIL). The same "overloaded" pair failed in one of three runs. The Twin section
  (`--twin`) passes, including:
  - determinism: the same render twice, after save / load, at 96 kHz and back, frozen vs live
    (SC-008);
  - copy / paste and an Instrument Rack chain;
  - Split / Layer;
  - the sequencer and the arpeggiator;
  - the MCP tools;
  - all 57 presets on both sides.

## 2. CPU budget (SC-001, SC-010)

`nota_perf run <scenario> 5`, wall ms per second of audio:

| Scenario | ms/s | vs Pentad |
|---|---|---|
| pentad-full-16 (16-voice chord) | 47.1 | 1.00× |
| twin-full-12 (12 voices, both filters, 16 routes, FX) | 86.5 | 1.84× |
| twin-layer-6x6 (Layer 6 + 6, both parts' FX) | 82.5 | 1.75× |

Both Twin scenarios are within the 2× budget. An earlier, quieter run gave 39.0 / 73.4 / 70.3
(1.88× / 1.80×).

## Beyond the plan

`contracts/c-abi.md` § "Added during implementation" lists these additions and what each one is for:

- note-off release velocity (FR-030);
- `nota_audition_instrument_state` (sequenced presets preview their pattern);
- `Delay::A_Reset` / `Reverb::A_Reset` (fresh-device reset, so renders repeat exactly, SC-008).

## 3–4. In the app

Each US1–US9 scenario is covered by a smoke check that drives the engine directly. Two parts
were not run by hand:

- **US3 hardware pass:** the KeyLab Essential mk3 poly-aftertouch / MPE check is still to do.
- **SC-007 timing:** the 5-minute "morphing pad from Init" walkthrough is written in the user
  docs (`devices/instruments/twin`) but has not been timed by a person.

Card screenshots for every tab, both sizes, the Split / Upper views and four themes are in the
docs (`devices/instruments/twin/*`) and in the card harness.
