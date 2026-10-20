# Plan: `src/` Root Cleanup (spec 031)

## Summary

Finish the 030 modularization: relocate the 9 already-split `Engine_*.cpp` TUs + `PluginHostBridge.h` to their domain folders, extract free types from the `Engine.h` god header, and migrate the last two flat DSP twins (`CompensationDelay.h`, `Oversampler.h`) onto `dsp/primitives/` with equivalence harnesses. No behavior changes except the two audited migrations.

## Nota: Read Before Planning

Constitution + project rules carry over from 030: AGPL SPDX headers on new files, AR-6 (no audio-thread allocation/lock/block), C ABI frozen, `./scripts/build.sh` green + smoke PASS before each commit, `git commit --no-gpg-sign` (signing broken in this env), spec folder stays untracked until merge.

Hard-won rules from 030 that apply directly:
- **Read-index semantics are never assumed** — the T041 `DelayLine` off-by-one (`orig.read(d) == readLinear(d-1)`, found via a −19 dB null) is the template for the PDC/oversampler equivalence tests.
- **FastMath only downstream/published**; measurement/control paths stay `std::`-exact (threshold gates amplify 1 ulp into block-scale flips — T040 AutoGain, T043 Forge LFO revert −65 → −130 dB). The −60 dB rule decides every substitution; revert what costs more.
- **Pre-size on the message thread**; audio path never grows (`ensureMidiScratch`/`ensureTrackScratch` pattern in `publishRaw`, before the live-graph swap).
- **Debug with bisection + temporary probes, remove before commit**; verify with standalone old-vs-new null harnesses (`clang++` + PCM compare), not by reasoning.

## Technical Context

- Root inventory (13 files): `Engine.h` (~1800 lines), `Engine.cpp`, `Engine_{Automation,Devices,Modulation,Rack,Recording,Render,Session,Tracks}.cpp`, `CompensationDelay.h`, `Oversampler.h`, `PluginHostBridge.h`.
- `Oversampler.h` includers: `devices/effects/{Amp,Vintage,Forge}.h` (likely resolving to the flat via `-I` order, NOT the primitive) + `dsp/primitives/Oversampler.cpp` (resolves to itself via same-dir-first). ODR status unknown — P0 audit decides.
- `Track::pdc` (`engine/Track.h:267`) uses the flat `CompensationDelay.h`; the primitive twin adds `isIdle()` — semantics must be compared field-by-field before migration.
- `PluginHostBridge.h` users: 6× `Engine_*.cpp`; `pluginhost/` is currently NOT on the engine include path (P1 adds it if needed).
- `Engine.h` movable free types: SPSC queue aliases (`MidiQueue`, `RecordedQueue`, `InputQueue`, …), `WarpBuildCursor`; `LeafWork`, pool/detector members stay (class coupling).

## Constitution Check

- No new dependencies, no JUCE outside `pluginhost/`, no C ABI change, no audio-thread allocation introduced. Moves are renames; migrations are equivalence-gated. Proceed.

## Project Structure

### Documentation (this feature)

- `specs/031-src-root-cleanup/spec.md` (this plan's input), `plan.md` (this file), `tasks.md` (checklist, 6 tasks P0–P4), `research.md` (P0 verdicts).

### Source Code Target Layout (`src/native/nota.engine/`)

- `src/engine/Engine.cpp`, `src/engine/Engine_{Automation,Devices,Modulation,Rack,Recording,Render,Session,Tracks}.cpp`, `src/engine/EngineTypes.h` (new).
- `pluginhost/bridge/PluginHostBridge.h` (moved).
- Deleted: `src/CompensationDelay.h` (after T006), `src/Oversampler.h` (after T007 verdict).

## Complexity Tracking

| Item | Why risky | Mitigation |
|---|---|---|
| PDC twin migration | Read-index/wrap semantics may differ (T041 precedent) | Standalone null harness BEFORE the swap; keep flat on mismatch |
| Oversampler twin migration | Two definitions may already coexist (ODR); saturation curves are ear-critical | P0 audit first; per-rate nulls; −60 dB rule + reverts |
| `Engine.h` type extraction | Hidden coupling (member-adjacent helpers) | Move only free types; build after each move; keep `LeafWork`/members |
| Moves (9 TUs + bridge) | Stale CMake paths break configure | Same-commit CMake update; `git rm` old paths; include-path check for bridge |
