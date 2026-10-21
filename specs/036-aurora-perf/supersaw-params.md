# Super Saw preset param map (Aurora, kind 5)

Source: `src/managed/Nota.Infrastructure/Persistence/FactoryPresetCatalog.cs:263-268`
(`Inst("aurora", 5, "Super Saw", …)`). Unlisted params keep init-patch values
(see `WavetableSynth` ctor). Shared by the `aurora-supersaw-2x` benchmark
(T010) and the null-test suite (T020) — both apply by param **id** through the
C ABI (`nota_plugin_param_set` + `nota_plugin_param_id`), never by index.

| Param id      | Value | Notes                    |
|---------------|-------|--------------------------|
| `table`       | 0     | Analog bank              |
| `position`    | 0.9   |                          |
| `osc2on`      | 1     | Osc2 enabled             |
| `osc2table`   | 0     | Analog bank              |
| `osc2position`| 0.9   |                          |
| `osc2level`   | 0.8   |                          |
| `osc2detune`  | 0.62  |                          |
| `unison`      | 1     | = 7 voices (1 + 6)       |
| `unidetune`   | 0.4   | = 20 cents               |
| `unispread`   | 0.9   | stereo → mid+side filters|
| `cutoff`      | 0.8   |                          |
| `resonance`   | 0.14  |                          |
| `fil1env`     | 0.55  |                          |
| `attack`      | 0     |                          |
| `decay`       | 0.4   |                          |
| `sustain`     | 0.85  |                          |
| `release`     | 0.28  |                          |
| `fxdrive`     | 0.2   | Tube, block ON, amt 0.2  |
| `fxchorus`    | 0.26  | block ON                 |
| `fxreverb`    | 0.24  | block ON                 |
| `gain`        | 0.72  |                          |

Resulting load per voice per sample: 2 oscillators × 7 unison = 14 wavetable
reads, mid + side filter pairs, global drive → chorus → reverb.
