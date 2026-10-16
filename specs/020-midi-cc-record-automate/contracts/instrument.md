# Contract: `Instrument::controller`

```cpp
// Instrument-wide MIDI controller (specs/020). ctl per NOTA_CTL_* (0..127 CC, 128 wheel,
// 129 channel pressure); value normalised. Audio thread, between render() calls, at a
// sample-accurate block offset (playEvents splits the render there). Default: the four
// controllers built-ins already understand go to noteExpression(-1, …); the rest are ignored.
virtual void controller(int32_t ctl, float value);
```

| Implementer | Behaviour |
|---|---|
| `Instrument` (default) | 128 → `noteExpression(-1, ExprBend, v)`; 129 → `(-1, ExprPressure, v)`; 74 → `(-1, ExprSlide, v)`; 64 → `(-1, ExprSustain, v)`; otherwise no-op |
| Built-ins with a `modwheel` parameter (BassSynth, OperatorSynth, Consort, any other found by parameter id) | 1 → set the Mod Wheel parameter to `v` (the same atomic write automation does), then the default |
| `RackInstrument` / `DrumRack` | forward `controller` to every chain's instrument |
| Plug-in host (`pluginhost/PluginHost.cpp`) | 0..127 → `controllerEvent(1, ctl, round(v*127))` (CC64 exact, not thresholded); 128 → `pitchWheel(1, round((v+1)*8191.5))`; 129 → `channelPressureChange(1, round(v*127))`. Overrides the default, so pitch −1 expression is not sent twice. |
| Keys / Physical (sustain) | unchanged: `ExprSustain` value ≥ 0.5 = down |

## Wire inside the engine

- **Dim encoding**: controllers travel as `ExprEv{off, pitch = -1, dim = kCtlDimBase + ctl, value}`
  with `kCtlDimBase = 16`, and as live `MidiEvent{expr = kCtlDimBase + ctl}`.
- **Dispatch**: `playEvents` sends `dim >= kCtlDimBase` to `controller()`; per-note dims
  still go to `noteExpression`.
- **Live expression**: `applyLiveExpression` does the same split.
- **Remote and gamepad**: pitch −1 dims 0–3 from `nota_*_note_expression` are rewritten to
  controllers 128 / 129 / 74 / 64 at enqueue, so one path handles everything.
