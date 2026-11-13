# Implementation Plan: Custom keyboard shortcuts

**Branch**: `feat/custom-shortcuts` | **Date**: 2026-11-13 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/096-custom-shortcuts/spec.md` (issue #61)

## Summary

Every keyboard shortcut becomes a catalog entry (id, scope, default primary + alternative)
in a new `KeyActionCatalog`, built from the command catalog plus ~50 view-level actions. A
`KeyBindings` service layers the user's overrides (stored in settings, only changed slots) on
the defaults, answers lookups, and finds conflicts by a layer rule (research R3). Every
hand-written key comparison in the views asks the service instead (`Keys.Is(e, id)`), keeping
its focus gates and order, so with no overrides nothing behaves differently. Preferences →
Shortcuts becomes an editor (capture, conflicts, edited mark, reset, Reset all), and the menu,
tooltips and palette re-read keys when bindings change.

## Nota: Read Before Planning

UI → [`DESIGN.md`](../../DESIGN.md) + `nota-design`; keyboard bindings → `nota-shortcuts`;
docs → `nota-docs`, `nota-features`. No engine work.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app only)

**Primary Dependencies**: Avalonia (existing). No new dependency.

**Storage**: `settings.json` (`Settings.KeyBindings`, via `ISettingsService`; `NOTA_DATA_DIR`
honoured as today).

**Audio-Thread Impact**: None — UI / input only; note-map keys still call the existing
`Engine.NoteOn/NoteOff` from the UI thread.

**Testing**: `tests/Nota.SmokeTest` (new `KeyBindingTests`, extended `PaletteTests`,
`ShortcutBuilderTests`); in-app check per [quickstart.md](quickstart.md).

**Target Platform**: macOS, Windows, Linux.

**Project Type**: desktop app.

**Performance Goals**: a key lookup is a dictionary hit per key press; no allocation per press
beyond today's.

**Constraints**: default behaviour unchanged (FR-023); macOS menu key equivalents fire before
views (R3); text fields keep typing.

**Scale/Scope**: ~110 command actions + ~50 new actions; ~12 handler files.

## Constitution Check

| Principle | Status |
|-----------|--------|
| 0 Approved issue | #61 `approved`, scope comment by the maintainer; PR `Closes #61` |
| I Real-time safety | N/A — no engine / audio-thread change |
| II Boundary | N/A — no C ABI change |
| III Licensing | new files carry SPDX headers; no dependency |
| IV Verification | `scripts/build.sh` + smoke tests extended (no engine behaviour change) |
| V One change | one feature PR; the nota-docs generator update is its own PR in that repo |
| VI Docs | CHANGELOG, FEATURES.md, nota-docs page + RU twin, nota-shortcuts |
| VII Spec-first | this spec / plan / tasks |
| VIII | no rule bent |

Post-design re-check: passes (nothing above changed).

## Project Structure

### Documentation (this feature)

```text
specs/096-custom-shortcuts/
├── spec.md · plan.md · research.md · data-model.md · quickstart.md
├── contracts/actions.md       # the action catalog (ids, scopes, defaults)
├── contracts/ui-contract.md   # the Shortcuts editor
└── tasks.md
```

### Source Code

```text
src/managed/Nota.Application/
├── Input/KeyScope.cs              # new: scopes + layer
├── Input/KeyAction.cs             # new: catalog entry
├── Input/KeyActionCatalog.cs      # new: commands imported + view actions
├── Input/KeyBindings.cs           # new: overrides, lookup, conflicts, reset, persistence
├── Input/ReservedKeys.cs          # new: per-platform OS-reserved chords
└── Settings.cs                    # + KeyBindings dictionary, KeyBindingOverride
src/managed/Nota.App/
├── Input/KeyTokens.cs             # new: Avalonia Key ⇄ token
├── Input/ShortcutAvaloniaExtensions.cs  # ToGesture via KeyTokens; FromKeyEvent
├── Input/Keys.cs                  # new: app facade (Is, Resolve, Capturing, KeyTips)
├── MainWindow.Input.cs            # handlers → Keys.Is
├── MainWindow.MenuKeys.cs         # gestures from bindings; refresh on change
├── PianoRollView.cs · PianoRollView.Controllers.cs · SessionView.cs · ModularView.cs
├── DeviceChainView.Clipboard.cs · BrowserView.Files.cs · AudioClipEditor / rack Esc paths
├── PreferencesWindow.cs           # ShortcutGroups → ShortcutLayout; editor UI
├── PreferencesWindow.Shortcuts.cs # new: capture, conflict line, reset, Reset all
└── App startup                    # KeyBindings registered in DI, loaded from settings
src/managed/Nota.Presentation/PaletteProviders.cs   # current key in hints
src/managed/Nota.Application/Localization/en.json   # new strings
tests/Nota.SmokeTest/KeyBindingTests.cs             # new
tests/Nota.SmokeTest/PaletteTests.cs                # layout covers every action
```

**Structure Decision**: domain-free logic (catalog, conflicts, persistence) in
Nota.Application so the smoke test can drive it without UI; Avalonia-specific mapping and
the editor in Nota.App.

## Delivery order

1. Catalog + KeyBindings + settings + tests (no behaviour change).
2. Handlers switch to `Keys.Is` (behaviour identical with defaults) — US1 dispatch.
3. Editor: capture / confirm / clear (US1), conflicts (US2), mark / filter / reset (US3),
   note map rows (US4).
4. Propagation: menu, tooltips, palette.
5. Docs, CHANGELOG, FEATURES, build.sh green.

## Complexity Tracking

None.
