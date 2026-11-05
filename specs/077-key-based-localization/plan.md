# Implementation Plan: Key-Based Interface Texts and Downloadable Languages

**Branch**: `feat/key-based-localization` | **Date**: 2026-11-04 | **Spec**: [spec.md](spec.md)

**Issue**: [nota-daw/nota#476](https://github.com/nota-daw/nota/issues/476)

**Input**: Feature specification from `specs/077-key-based-localization/spec.md`

## Summary

Switch every interface text from English-text-as-key to stable keys (`Loc.T("settings.title")`),
with an embedded `en.json` as the single English source, using a repeatable migration
script over the ~4,630 call sites and the French file. Then move translations to
`nota-daw/nota-translations` (validated by its own CI, with an index of languages,
coverage per Nota key set, and references pulled from Nota releases), and let the app
download, update, remove and preview languages from Preferences while shipping English
only. Format stays JSON (version 2, keyed); the downloader, staging/swap and checksum
pattern of `ModelStore` are reused. Details: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio): the Language row grows
  ([contracts/language-row.md](contracts/language-row.md)); sentence case, no dialogs,
  orange only for the selected item.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): no new dependency; the
  "Translating Nota" section is rewritten (keys, `en.json`, link to nota-translations).
- **Release** → the `release` skill gains "sync references in nota-translations".
- Engine, instruments, plugin hosting: not touched.

## Technical Context

**Language/Version**: C# / .NET 10 (managed app, smoke test); Python 3 stdlib (migration
script in nota, validator and publisher in nota-translations); GitHub Actions YAML.

**Primary Dependencies**: Avalonia (existing). No new dependency in either repository.

**Storage**: `Nota.Application/Localization/en.json` embedded in `Nota.Application` (English built into `Loc`); `<data>/languages/<code>.json` +
`<code>.meta.json` under the Nota data root (`NOTA_DATA_DIR` in tests); `Settings.Language`
unchanged.

**Audio-Thread Impact**: None — UI text and file/network I/O on non-audio threads only.

**Testing**: `tests/Nota.SmokeTest` (localization checks move to keys; catalog checks on a
local fixture index); `scripts/loc-migrate.py --verify` for SC-002; `tools/validate.py`
+ shared fixtures in nota-translations.

**Target Platform**: macOS, Windows, Linux desktop (all builds).

**Project Type**: desktop app + a data repository with CI.

**Performance Goals**: start-up unchanged (no network on the start path; loading two JSON
tables of ~4k entries ≤ 30 ms, as today); a language download < 10 s on a typical
connection (files ~200 KB).

**Constraints**: offline-capable; nothing fetched is executed; a bad file never stops Nota
starting; English output byte-identical after PR 1.

**Scale/Scope**: ~3,650 keys, ~4,630 call sites in 243 `.cs` files + 118 XAML uses, 1
translated language (French).

## Constitution Check

*GATE: before Phase 0 — re-checked after Phase 1.*

| Principle | Status |
|---|---|
| 0. Approved issue | ✅ #476 carries `approved` |
| I. Real-time audio safety | ✅ no engine / audio-thread code |
| II. Boundary discipline | ✅ no C ABI change, no JUCE |
| III. Licensing hygiene | ✅ no dependency; new `.cs` / `.py` files carry SPDX headers; nota-translations is AGPL-3.0-only (LICENSE file) |
| IV. Verification gate | ✅ smoke test extended (R12); `scripts/build.sh` before each PR |
| V. One logical change per PR | ✅ three PRs (Clarifications Q5); PR 1 is mechanical and reviewable via `keymap.tsv` |
| VI. Docs in sync | ✅ CHANGELOG (PR 3: user-visible; PR 1: Changed/dev note), FEATURES.md, nota-docs page (choose / preview a language), CONTRIBUTING |
| VII. Spec first | ✅ this spec |
| VIII. No weakened rules | ✅ |

Post-design re-check: unchanged — no violation, Complexity Tracking empty.

## Delivery

| PR | Repository | Content | User-visible |
|---|---|---|---|
| 1 | nota | `loc-migrate.py`; `en.json` v2 + `fr.json` v2 (still embedded); `Loc` / `TExtension` / `TranslationTable` / `UiLanguage` on keys and plural rules; call sites rewritten; smoke test on keys; CONTRIBUTING keys section | No |
| 2 | nota-translations | README, LICENSE, `languages/fr.json` (moved with `git log` attribution in the commit), `tools/validate.py`, `tools/sync_references.py`, fixtures, `check.yml`, `publish.yml`, first `reference/develop.json`; ruleset bypass for GitHub Actions + required check `check` | No |
| 3 | nota | `LanguageCatalog` + installed-language store; `NotaLanguages` reads `<data>/languages` and local files; Language row and status-bar notice; story-5 background download; `NOTA_UI_LANGUAGE` path; remove embedded `fr.json`; smoke catalog checks; delete `loc-migrate.py`; CHANGELOG / FEATURES / nota-docs; `release` skill step | Yes |

PR 2 can merge any time after PR 1; PR 3 merges after PR 2 and both ship in the same
release.

## Project Structure

### Documentation (this feature)

```text
specs/077-key-based-localization/
├── spec.md
├── plan.md
├── research.md
├── data-model.md
├── quickstart.md
├── contracts/
│   ├── language-file.md
│   ├── index.md
│   ├── loc-api.md
│   └── language-row.md
└── tasks.md            # /speckit-tasks
```

### Source Code

```text
src/managed/
├── Nota.Application/Localization/
│   ├── en.json                    # PR1: new, the English reference, embedded here
│   ├── Loc.cs                     # PR1: key API, built-in English fallback
│   ├── TranslationTable.cs        # PR1: format v2, key regex, plural rule, problems, key-set hash
│   └── UiLanguage.cs              # PR1: plural rules by id
├── Nota.App/
│   ├── Languages/fr.json          # PR1: converted; PR3: removed
│   ├── Languages/NotaLanguages.cs # PR1: en first; PR3: installed + local sources
│   ├── TExtension.cs              # PR1: key only
│   ├── App.axaml.cs               # PR3: InstallLanguage sources, story-5 trigger
│   ├── PreferencesWindow.Language.cs  # PR3: the row (contracts/language-row.md)
│   ├── MainWindow.Updates.cs      # PR3: "ready — Restart" notice
│   └── (243 files)                # PR1: call sites rewritten by the script
└── Nota.Infrastructure/
    └── Languages/                 # PR3: new
        ├── LanguageCatalog.cs     # index fetch, download (RegistryClient), fit
        └── InstalledLanguages.cs  # <data>/languages, meta, staging, remove, local

tests/Nota.SmokeTest/
├── DesignTokenCheck.Localization.cs   # PR1: key checks; PR3: drop fr coverage
├── LanguageCatalogTests.cs            # PR3: fixture index, install/update/offline/mismatch/local/story 5
├── LanguageProjectTests.cs            # PR3: xx fixture instead of fr.json
├── Fixtures/Languages/                # PR1: v2 fixtures (valid, broken, plural mismatch); PR3: index fixture
└── LocalizationAllowList.txt          # unchanged entries; paths unchanged

scripts/loc-migrate.py                 # PR1: new; PR3: deleted

nota-translations/                     # PR2 (layout: research R7)
```

**Structure Decision**: localization core stays in `Nota.Application/Localization`
(pure, testable); network and disk for languages go to `Nota.Infrastructure/Languages`,
beside `ModelStore`, reusing `RegistryClient`; UI stays in `Nota.App`.

## Risks

| Risk | Mitigation |
|---|---|
| PR 1 touches 243 files: merge conflicts with open branches | land it in a quiet window; the script is re-runnable on a rebased tree (`--plan` reuses `keymap.tsv`) |
| Regex rewrite misses or mangles a call | the script fails on any unparsed `Loc.` call; `--verify` compares old vs new texts for every site; smoke test unused/missing key checks |
| Key names poor after auto-generation | `keymap.tsv` review before `--apply`; renames are cheap until PR 2 publishes keys |
| C# and Python validators drift | shared fixture verdicts (R11) |
| Actions bot bypass on `main` misused | only `publish.yml` writes; it only changes `reference/` and `index.json` |
| Users offline after the update lose French temporarily | accepted (Clarifications Q2, Q4); row and status bar explain |

## Complexity Tracking

None.
