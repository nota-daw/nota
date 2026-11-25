# Bug Verification: Rack and Grain user presets lose structural state

- **Slug**: rack-grain-presets
- **Verified**: 2026-11-25
- **Assessment**: [assessment.md](assessment.md)
- **Fix**: [fix.md](fix.md)
- **Status**: verified

## Reproduction after fix

Run:

```sh
dotnet run --project tests/Nota.SmokeTest/Nota.SmokeTest.csproj --no-build -- --rack-presets
```

Result: all five checks pass:

1. Instrument Rack chains and child devices are recalled.
2. Applying an Instrument Rack preset in place undoes as one step.
3. Audio Effect Rack chains and child devices are recalled.
4. A Grain in a rack chain restores its referenced sample and root note.
5. A track Nota Grain restores its referenced sample and root note.

## Regression checks

`scripts/build.sh` completed its native, managed and smoke-test stages. The documentation site also passes `npm run build && npm run check`; the documentation checker reports its existing source-path warnings only.

## Residual risk

Grain user presets intentionally retain a file reference rather than embedding audio. If that file is unavailable on the receiving computer, preset application reports the localized sample-load warning and leaves the fresh Grain sample empty.
