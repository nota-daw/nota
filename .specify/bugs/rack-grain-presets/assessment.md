# Bug Assessment: Rack and Grain user presets lose structural state

- **Slug**: rack-grain-presets
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1044
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

Issue [#1044](https://github.com/nota-daw/nota/issues/1044) reports that Drum Rack, Instrument Rack and Nota Grain cannot be saved as user presets, and that Audio Effect Rack presets omit their chains. A saved rack must restore its chains, devices, mappings and macros; Grain must restore the selected sample as well as its sound.

URL policy: `github.com` — allowlisted.

## Symptom

Saving a Drum Rack, Instrument Rack or Grain returns no preset, so no preset appears in the browser. Saving an Audio Effect Rack writes a parameter-only preset, so applying it reconstructs an empty rack rather than its chains.

## Reproduction

1. Create a Drum Rack or Instrument Rack, add a chain/pad and child device, then choose **Save preset** from its card.
2. Observe that no user preset is saved.
3. Create an Audio Effect Rack with a chain and child device, save and apply its preset.
4. Observe that the rebuilt rack has no saved chain content.

## Suspected Code Paths

- `src/managed/Nota.Infrastructure/Persistence/PresetService.cs:27` — `Capture` returns `null` for instrument kinds 3, 4 and 10.
- `src/managed/Nota.Infrastructure/Persistence/PresetService.cs:73` — built-in effects, including Audio Effect Rack kind 5, only capture sound parameters.
- `src/managed/Nota.Infrastructure/Persistence/PresetService.cs:239` — the apply paths do not restore a structural state blob for racks.
- `src/native/nota.engine/src/devices/racks/RackCore.cpp:636` — the engine already serializes full rack state, including chains, child devices and mappings.
- `src/managed/Nota.Infrastructure/Persistence/ProjectService.cs:145` — project persistence demonstrates how Grain preserves sample data and root note.

## Root Cause Hypothesis

Confidence: high. Preset capture has a deliberate guard for the two instrument racks and Grain because parameter values alone are insufficient. Audio Effect Rack takes the generic built-in-effect branch, which likewise persists only parameters. The underlying engine exposes the rack state blob through `GetPluginState`/`SetPluginState`, but `PresetService` does not carry or apply it for these device kinds.

## Proposed Remediation

**Preferred**: extend the preset document with optional state and Grain sample-reference metadata. Capture and restore the engine's structural state blob for Instrument Rack, Drum Rack and Audio Effect Rack; capture Grain's loaded sample source and root note alongside its existing named parameters. Add an undoable state-application route for structural rack replacement so applying an in-place rack preset is one undo operation. Preserve backwards compatibility with old parameter-only preset documents.

**Files likely to change**:

- `src/managed/Nota.Infrastructure/Persistence/PresetModel.cs`
- `src/managed/Nota.Infrastructure/Persistence/PresetService.cs`
- `src/managed/Nota.Application/Engine/IAudioEngine.cs`
- `src/managed/Nota.Infrastructure/Interop/*`
- `src/native/nota.engine/include/nota_engine.h`
- `src/native/nota.engine/src/abi/*`
- `src/native/nota.engine/src/engine/*`
- `tests/Nota.SmokeTest/*`
- `CHANGELOG.md`

**Tests to add or update**:

- Save/load/apply an Instrument Rack and Drum Rack preset, asserting their serialized state restores chains and mappings.
- Save/load/apply an Audio Effect Rack preset, asserting its chain child devices return.
- Save/load/apply a Grain preset, asserting its parameter values and referenced sample/root note return.
- Apply a rack preset in place and verify undo restores the prior rack as one operation.

## Risks & Considerations

- Rack blobs may name plug-ins that are unavailable when a preset is recalled; the existing rack deserializer must retain its safe missing-plugin behavior.
- Preset files currently store JSON only; Grain sample references must remain paths, not embedded audio, to avoid unexpectedly huge preset files.
- State replacement must remain on the message thread and publish an immutable graph snapshot; it must not mutate a live audio graph.

## Open Questions

- None for the approved scope; unavailable external sample paths should leave the fresh Grain sample empty and report a user-facing warning.
