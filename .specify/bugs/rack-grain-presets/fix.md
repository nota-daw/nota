# Bug Fix: Rack and Grain user presets lose structural state

- **Slug**: rack-grain-presets
- **Fixed**: 2026-11-25
- **Assessment**: [assessment.md](assessment.md)
- **Status**: applied

## Changes

- Capture the structural state blob for Instrument Rack, Drum Rack and Audio Effect Rack presets, and restore it on new and existing targets.
- Group an in-place rack restore into one undo operation.
- Persist Nota Grain's external sample path and root note, both for a track Grain and a Grain in a rack chain. Missing files return the existing localized load warning.
- Add a read-only C ABI/managed interop query for a chained Grain's sample metadata, and retain source paths whenever a Grain sample is loaded.
- Add smoke coverage for instrument-rack structure, undo, audio-effect-rack structure, and Grain sample recall in tracks and racks.
- Update the changelog, feature inventory, and EN/RU device documentation.

## Compatibility

All new preset fields are optional. Existing parameter-only preset files keep their former recall behavior.

## Verification

- `cmake --build src/native/nota.engine/build`
- `dotnet build tests/Nota.SmokeTest/Nota.SmokeTest.csproj --no-restore`
- `dotnet run --project tests/Nota.SmokeTest/Nota.SmokeTest.csproj --no-build -- --rack-presets`
- `scripts/build.sh`
- `npm run build && npm run check` in `../nota-docs`

## Deviations

None.
