# Implementation Plan: Interface Language Selector (English + French)

**Branch**: `068-ui-language` | **Date**: 2026-11-01 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `/specs/068-ui-language/spec.md`

## Summary

Add an app-wide interface language (English default, Français) chosen in Preferences →
Appearance and applied at the next start. The mechanism is a small, dependency-free
translation layer: English source text is the lookup key, translations live in one
embedded JSON file per language (the same pattern as the colour themes), and every
user-visible text in the desktop app goes through one call (`Loc.T` / `Loc.Plural`). A
missing translation falls back to the English text, so wrapping a literal can never
change what an English user sees. Because the language is fixed for the whole run, no
view needs to rebuild at runtime, which is what makes translating a code-built UI
tractable. Completeness and "no new raw literal" are enforced by the smoke test
(a coverage check plus a shrink-only ratchet, as spec 012 did for labels).

Work is delivered as an infrastructure slice followed by area-by-area translation slices,
each keeping English output identical (see **Delivery slices**).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) § Labels and copy: French labels
  keep sentence case; the French set follows the same rule as English.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): no new dependency (none is
  added); a "Translating Nota" section is added (FR-012).
- Engine, instruments, build/packaging: not touched.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app). No C++ / engine change.

**Primary Dependencies**: Avalonia 12.1.4 (existing). **No new dependency**: lookup, plural
rules and JSON reading use the BCL (`System.Text.Json`).

**Storage**: one new field `Settings.Language` (string, language code, default `"en"`) in
the existing JSON settings file, beside `InterfaceScale`. Translation files are embedded
resources of `Nota.App` (`Languages/*.json`, logical name `Nota.Languages.<code>.json`).
Nothing in project files.

**Audio-Thread Impact**: None. UI/managed-only. Lookups happen on the UI thread and from
background tasks; the table is immutable after install, so reads are lock-free. No
string lookup is added to any audio-thread or engine-callback path (AR-4/5/6 untouched).

**Testing**: extend `tests/Nota.SmokeTest` with a `LocalizationCheck` (see
[quickstart.md](quickstart.md)): pure lookup/plural/fallback behaviour, French coverage of
every key found in source, placeholder parity, orphan keys, shrink-only raw-literal
ratchet, settings fallback, and English-output identity. Visual layout check of French at
every interface-scale preset is manual (quickstart) plus headless screenshots.

**Target Platform**: macOS, Windows, Linux (same as the app). Restart relaunch differs
per platform (see research R5).

**Project Type**: desktop-app (Avalonia, layered: Domain / Application / Infrastructure /
Presentation / App).

**Performance Goals**: language install at startup under 20 ms; a lookup is a dictionary
read (no allocation on the hit path when there are no arguments).

**Constraints**: English output must stay byte-identical when English is selected (SC and
User Story 3); languages must be addable by dropping in one JSON file (FR-011).

**Scale/Scope**: ~2,100 literals in `Nota.App`, ~300 in `Nota.Application` (command
palette, status text), ~100 in `Nota.Presentation`, plus a subset of `Nota.Infrastructure`
messages that reach the user. Device/parameter names and `Nota.Mcp` descriptions are out
of scope (FR-004, Assumptions). Counts are rough regex hits and include non-UI literals;
the real set is established by the ratchet baseline in slice 0.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | Note |
|---|---|---|
| I. Real-time audio safety | Pass | No audio-thread or engine code touched. |
| II. Boundary discipline | Pass | No C ABI change, no JUCE. |
| III. Licensing hygiene | Pass | No dependency. New `.cs` files carry the SPDX header; JSON data files follow the existing theme-JSON convention (no header; the repo's AGPL licence covers them). |
| IV. Verification gate | Pass | Smoke test extended; `scripts/build.sh` must stay green. No engine behaviour change. |
| V. One logical change per PR | Pass, by design | Delivered in slices (below), each independently mergeable and English-identical. |
| VI. Docs in sync | Planned | CHANGELOG `[Unreleased]`, FEATURES.md (`nota-features`), user manual page (`nota-docs`), CONTRIBUTING "Translating Nota". |
| VII. Spec-first | Pass | This workflow. |
| VIII. Rules never weakened | Pass | No rule bent. |

**Post-design re-check**: unchanged — the design added no dependency, no engine change and
no new layer.

## Delivery slices

Each slice lands with English unchanged and `scripts/build.sh` green.

| Slice | Content | Spec coverage |
|---|---|---|
| 0 | Infrastructure: `Loc`, language table + plural rules, `Settings.Language`, startup install, `LocalizationCheck` (coverage + parity + fallback), raw-literal ratchet baseline. No visible change yet. | FR-005–008, 011 |
| 1 | The selector: Preferences → Appearance row, drop-down with an inline "Applies after restart" note and Restart button (no popup) + relaunch, and the Preferences window + the menu bar + the dialogs it uses, translated. First end-to-end French. | US1, FR-001, 002 |
| 2 | Main shell: transport, toolbar, context menus, command palette, status bar, welcome / about / what's-new. | FR-003 |
| 3 | Editing views: Arrangement, Session, Mixer, Piano roll, clip / audio editors, Browser, Modular chrome. | FR-003 |
| 4 | Dialogs, confirmations, progress and error messages (incl. user-facing Infrastructure messages), device-window chrome (titles, tooltips; not parameter names). | FR-003, 008 |
| 5 | Close-out: ratchet allow-list reduced to data only, layout pass of French at every scale, docs (CHANGELOG, FEATURES, manual, CONTRIBUTING). | FR-010, 012, 013, SC-001, 004 |

Slices 2–4 can be reordered or split further by area; the ratchet makes the remaining
work visible at every point.

## Project Structure

### Documentation (this feature)

```text
specs/068-ui-language/
├── plan.md              # This file
├── research.md          # Phase 0: decisions and alternatives
├── data-model.md        # Phase 1: entities and rules
├── quickstart.md        # Phase 1: how to validate end to end
├── contracts/
│   ├── translation-file.md   # JSON language file format
│   └── loc-api.md            # the call surface contributors use
└── tasks.md             # Phase 2 (/speckit-tasks — not created here)
```

### Source Code (repository root)

```text
src/managed/
├── Nota.Application/
│   ├── Settings.cs                  # + Language (string, default "en")
│   └── Localization/
│       ├── Loc.cs                   # T / F / Plural; install; English fallback
│       ├── UiLanguage.cs            # available codes, Normalize, plural rule per language
│       └── TranslationTable.cs      # parse + immutable lookup (pure; no UI, no I/O)
├── Nota.App/
│   ├── Languages/
│   │   ├── en.json                  # name + (optional) context notes; English is the key
│   │   └── fr.json                  # the French translation set
│   ├── Nota.App.csproj              # + EmbeddedResource Languages\*.json
│   ├── AppRestart.cs                # relaunch helper (per platform)
│   ├── App.axaml.cs                 # install language right after settings load
│   └── PreferencesWindow.cs         # + Language row in the Appearance pane
└── (all other files: literals wrapped in Loc.T, slice by slice)

tests/Nota.SmokeTest/
├── LocalizationCheck.cs             # coverage, parity, plurals, fallback, ratchet
└── LocalizationAllowList.txt        # raw literals not yet wrapped; shrink-only

CONTRIBUTING.md                      # + "Translating Nota"
CHANGELOG.md / FEATURES.md           # + entries
```

**Structure Decision**: `Loc` lives in `Nota.Application` because that is the lowest layer
that Presentation, Infrastructure and App all already reference and it needs no UI types;
Domain has essentially no user text and stays untouched. The JSON files are embedded in
`Nota.App` beside the themes, and `App` hands the parsed table to `Loc` at startup, so the
pure logic stays UI-free and the smoke test can walk it (as it does `InterfaceScale`).

## Risks

- **R1 — Static text initialised too early.** Static fields and tables (Preferences
  `Page` list, command registry titles) are built once; if they run before `Loc` is
  installed they freeze in English. Mitigation: install the language first thing after the
  settings service is resolved in `App.axaml.cs`; `Loc.T` records a "used before install"
  flag in debug and the smoke test asserts it is never set.
- **R2 — Volume and drift.** ~2–3k literals; new ones will keep arriving. Mitigation: the
  shrink-only ratchet turns "translate everything" into a measurable list and blocks new
  raw literals (FR-012).
- **R3 — Concatenated sentences.** Some text is assembled from fragments, which cannot be
  translated correctly. Mitigation: convert to one `Loc.F` / `Loc.Plural` sentence when
  touched; the ratchet flags interpolated label strings so they are visible.
- **R4 — French is longer.** Layout clipping at high scale. Mitigation: slice-5 layout pass
  at every preset; headless screenshots with the language override.
- **R5 — Relaunch is platform-specific** and there is no existing restart code. Mitigation:
  research R5; fall back to a clear "quit and reopen Nota" message if relaunch cannot be
  arranged.
- **R6 — Existing label checks** (`DesignTokenCheck.Labels`) read literals as text; wrapping
  in `Loc.T(...)` must not hide them. Mitigation: register `Loc.T` as a label-builder
  helper in that check.

## Complexity Tracking

No constitution violations to justify.
