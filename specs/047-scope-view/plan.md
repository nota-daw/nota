# Plan: Scope view

## Engine

- `ScopeHistory` (Modulation.h): a ring of 8192 (value, dt) pairs and a 64-bit write count.
  8192 blocks hold the longest window (8 s) down to 64-frame blocks at 48 kHz.
- A `Modulator` holds a `shared_ptr<ScopeHistory>`, allocated in `addModulator` for Scope
  nodes only (message thread). Snapshot copies of the track share it, so the trace carries on
  across a republish. The audio thread only writes values into it (`pushScope(v, dt)`, no
  allocation); the old snapshot drops its reference wherever it is retired today.
- `readScope(values, dts, cap)` returns the newest entries oldest → newest, at most the
  recorded count and at most `kScopeLen − 256` (a margin the writer may reach while the UI
  copies).
- Fields 13–18 on the generic modulator get/set: window ms, sync, window beats, zoom (snapped
  to 1/2/4/8), trigger, height. The engine only stores them.
- ABI: `nota_track_modulator_scope_timed`. The untimed `nota_track_modulator_scope` stays and
  now also returns the newest values.

## App

- `ScopeTrace` (Nota.Application): window seconds, knob maps, labels, and `Layout`, which lays
  the history into the window (free running, or from the latest rising crossing of 0 with a
  full window after it, interpolated between blocks).
- `ModularView.Scope.cs`: the Scope node (trace, resize grip, window/zoom knobs, Free / Rise /
  Freeze chips) and `ScopeViz`. A pop-out glyph in the node header opens `ScopeWindow`, which
  builds the same trace and controls. Both follow the engine fields every tick and rebuild
  when a chip changes on the other copy. Freeze lives in the Modular view (not saved).
- Persistence: `ModulatorDto` gains optional Scope fields with the defaults; Capture writes
  them, Apply sets them for Scope nodes. No format bump.

## Tests

`--scope` (`ScopeViewTests`): history per block with time steps, newest-first reads, a full
ring, untimed read, survives a republish; defaults, clamps, save/load, older-project defaults;
`ScopeTrace` cycles in a window, beat window, trigger stability, no-crossing fallback,
right-aligned short history, layout cost, labels.
