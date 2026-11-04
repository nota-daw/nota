---

description: "Tasks for 077 key-based interface texts and downloadable languages"
---

# Tasks: Key-Based Interface Texts and Downloadable Languages

**Input**: Design documents from `specs/077-key-based-localization/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/](contracts/), [quickstart.md](quickstart.md)

**Tests**: Included. The spec makes the smoke test part of the feature (FR-006, research
R12), and the constitution makes it the PR gate.

**Organization**: by user story; the stories map onto the three pull requests of the plan
(US1 → PR 1 in nota, US2 → PR 2 in nota-translations, US3 + US5 + US4 → PR 3 in nota).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1…US5 from spec.md
- Paths: nota paths are relative to the nota repository root; `nota-translations/…` paths
  are relative to a checkout of `nota-daw/nota-translations`.

---

## Phase 1: Setup

**Purpose**: baseline for the "no visible change" proof and the migration tool skeleton.

- [X] T001 Record the pre-migration commit SHA of `develop` in `specs/077-key-based-localization/quickstart.md` § PR 1 (the `--verify` baseline), and capture English + French reference screens with the headless shots harness (`NOTA_UI_LANGUAGE=en` / `fr`) into `/tmp/nota-077-before/` (not committed)
- [X] T002 Create `scripts/loc-migrate.py` (SPDX header; Python 3 stdlib only) with the CLI `--plan`, `--apply`, `--verify <commit>` and a module docstring citing research R4; no logic yet

---

## Phase 2: Foundational (blocks every story)

**Purpose**: the format-v2 language layer the app, the migration and the repository checks all share.

- [X] T003 Rework `src/managed/Nota.Application/Localization/TranslationTable.cs` to contract `contracts/language-file.md`: `formatVersion` must be `2` (1 and higher rejected with the message "language file format N is not supported"); `code` lower-case BCP-47 primary tag equal to the file name; keys must match `^[a-z0-9_]+(\.[a-z0-9_]+)+$` and be ≤ 80 chars; required `plural` (except `en`); values a non-empty string or `{ "other" required, "one" optional }`; invalid entries go to a `Problems` list `(key, problem)` instead of failing the file; remove `KeyOf(text, context)`
- [X] T004 Add key-shape and placeholder validation against a reference table to `TranslationTable` (`ValidateAgainst(TranslationTable english)`): shape mismatch (text vs plural), `other` must use exactly the reference's placeholders, `one` may drop but not add, unknown keys reported; returns problems and coverage percent (`translated valid entries of reference keys ÷ reference keys`, rounded down)
- [X] T005 Add `KeySetHash` to `TranslationTable`: SHA-256, lower-case hex, first 16 chars, over the ordinally sorted lines `key \t (text|plural) \t sorted placeholder indices joined by ","` joined with `\n` (contract language-file § Key-set hash)
- [X] T006 [P] Replace per-code plural rules in `src/managed/Nota.Application/Localization/UiLanguage.cs` with rule ids: `Plural(string rule, long n)` for `one_is_1` and `one_is_0_1`, `IsKnownRule(string)`; keep `Normalize` / `IsPending`
- [X] T007 Rework `src/managed/Nota.Application/Localization/Loc.cs` to `contracts/loc-api.md`: English built in: `en.json` embedded in `Nota.Application` (logical name `Nota.Localization.en.json`) and loaded lazily by `Loc`; `Install(TranslationTable? table, string code)` unchanged; `T(key)`, `F(key, args)`, `Plural(key, n)` with lookup running → English → key (+ one log line per key via a `MissingKey` event); plural form by the running table's `PluralRule`; remove `FC` and every `context` parameter; keep the format fallback and `UsedBeforeInstall`
- [X] T008 [P] Update `src/managed/Nota.App/TExtension.cs` to a key-only markup extension (`{app:T menu.file.save_as}`), removing `Context`; update its header comment
- [X] T009 [P] Write v2 fixtures in `tests/Nota.SmokeTest/Fixtures/Languages/`: `en-ref.json` (small reference), `xx.json` (valid, `one_is_1`), `xx-broken.json` (bad placeholder, shape mismatch, unknown key, empty value), `xx-v1.json` (format 1, rejected), `xx-badrule.json` (unknown `plural`), plus `verdicts.json` listing each file's expected load result, problems and coverage (shared with nota-translations, research R11)
- [X] T010 Rewrite `DesignTokenCheck.RunLanguage` in `tests/Nota.SmokeTest/DesignTokenCheck.Localization.cs` for v2: fixture verdicts from `verdicts.json`, plural rules by id, key-set hash stable under English wording change and changed by key add/remove/shape/placeholder change, lookup fallback running → English → key

**Implementation notes (PR 1)**: English moved to `src/managed/Nota.Application/Localization/en.json`, embedded beside `Loc` (the smoke test and other projects that do not reference `Nota.App` need English too). Keys shared by exactly two groups get a key per group; `common.*` from three groups (research R2). T001: before-screens were not captured; instead `--verify` proves text equality per call site, and a headless harness (main window, native menu, 10 Preferences pages, mixer, EN + FR) found no key shown and no missing-key event. T021: the allow-list did not change (2644 entries, none stale). T020 also accepts `string.Format(culture, Loc.T(key), …)` (4 call sites format with `NotaNum.Culture`). The render-pool "overloaded" smoke checks flaked while the machine was busy; an idle rerun passed.

**Checkpoint**: the language layer builds and its smoke checks pass on fixtures; call sites are not migrated yet (the build is red until US1's rewrite lands — do Phase 2 and US1 on one branch).

---

## Phase 3: User Story 1 — Change an English text without breaking translations (P1) 🎯 MVP · PR 1

**Goal**: every interface text is looked up by a stable key; `en.json` is the only English source; French still embedded; nothing visible changes.

**Independent test**: quickstart § PR 1 steps 3–6.

### Migration tool

- [X] T011 [US1] Implement scanning in `scripts/loc-migrate.py`: port the `LocT`, `LocF`, `LocPlural`, `LocXaml` patterns from `tests/Nota.SmokeTest/DesignTokenCheck.Localization.cs` (lines ~196–203) over `src/managed/**/*.cs` and `src/managed/**/*.axaml`; collect (text, context, plural one/other, file, line); exit non-zero listing any `Loc.` call it cannot parse (excluding `TExtension.cs`)
- [X] T012 [US1] Implement key generation (research R2) in `scripts/loc-migrate.py`: area table from file name (`PreferencesWindow*` → `preferences`, `ArrangementView*` → `arrangement`, `Controls/<X>Views.cs` → `device.<x>`, `MainWindow*` → `main`, menus → `menu`, … — unknown file → fail with the file name so the table is extended); sub-area from the partial suffix; slug = first ≤ 6 ASCII words of the English text, lower-case, `_`-joined, punctuation and `{n}` dropped; `common.<slug>` when the same text and shape is used in ≥ 3 areas; the 3 context calls / 4 `|` keys get distinct keys by context; collisions `_2`, `_3`; every key validated against `^[a-z0-9_]+(\.[a-z0-9_]+)+$`, ≤ 80 chars
- [X] T013 [US1] Implement `--plan` in `scripts/loc-migrate.py`: write `keymap.tsv` (text, context, plural, files, key) at the repo root (git-ignored via `.gitignore`); read an existing `keymap.tsv` back so hand-renamed keys stick; validate uniqueness after edits
- [X] T014 [US1] Implement `--apply` in `scripts/loc-migrate.py`: write `src/managed/Nota.Application/Localization/en.json` (`formatVersion: 2`, `code: en`, `name: English`, keys grouped by area, sorted), convert `src/managed/Nota.App/Languages/fr.json` to v2 (`plural: one_is_0_1`, keys via the map, `notes` updated, orphans printed not carried), rewrite call sites in place (`Loc.T("text"[, "ctx"])` → `Loc.T("key")`, `Loc.F("text", …)` → `Loc.F("key", …)`, `Loc.Plural(n, "one", "other"[, ctx])` → `Loc.Plural("key", n)`, `{app:T 'text'[, Context=c]}` → `{app:T key}`), preserving surrounding formatting
- [X] T015 [US1] Implement `--verify <commit>` in `scripts/loc-migrate.py`: for every call site at `<commit>` and its migrated counterpart, assert `en[key]` equals the old English (and plural one/other) and `fr[key]` equals the old French value; print differences and exit non-zero if any

### Run the migration

- [X] T016 [US1] Run `python3 scripts/loc-migrate.py --plan`, review `keymap.tsv` area by area (rename unclear keys, merge true duplicates into `common.*`), re-run `--plan` until clean
- [X] T017 [US1] Run `python3 scripts/loc-migrate.py --apply`; build; fix any call site the rewrite left uncompilable by hand (list them in the PR description)
- [X] T018 [US1] Update `src/managed/Nota.App/Languages/NotaLanguages.cs` to offer English from `Loc.English` (no `en.json` in `Nota.App/Languages` any more) plus the embedded `fr.json`, log an English load error if any; and `src/managed/Nota.App/App.axaml.cs` `TranslateAppMenu` to look the macOS app-menu texts up by key; add the `EmbeddedResource` for `Localization/en.json` to `src/managed/Nota.Application/Nota.Application.csproj`
- [X] T019 [US1] Run `python3 scripts/loc-migrate.py --verify <T001 SHA>` → 0 differences; spot-check screens against `/tmp/nota-077-before/` (SC-002)

### Smoke checks on keys

- [X] T020 [US1] Rewrite `DesignTokenCheck.RunLocalization` in `tests/Nota.SmokeTest/DesignTokenCheck.Localization.cs`: extract key literals from `Loc.T/F/Plural` and `{app:T key}`; fail on a computed (non-literal) key, a key missing from `en.json`, an `en.json` key used nowhere, a `Plural` key that is not a plural object or a `T`/`F` key that is one, `F` placeholders not matching the English pattern; keep French coverage / orphan / placeholder checks against the embedded `fr.json` (until US3); keep the raw-literal ratchet
- [X] T021 [US1] Regenerate `tests/Nota.SmokeTest/LocalizationAllowList.txt` with `NOTA_LOC_WRITE_BASELINE=1` only if line matching changed; the list must not grow (diff reviewed)
- [X] T022 [P] [US1] Update `tests/Nota.SmokeTest/LanguageProjectTests.cs` to parse the v2 `fr.json` with the new `TranslationTable.Parse`

### Contributor docs

- [X] T023 [P] [US1] Rewrite `CONTRIBUTING.md` § Translating Nota (or the interface-text section): keys and the naming convention (research R2), adding a text = one `en.json` entry + `Loc.T("key")`, changing wording edits only `en.json`, changing meaning or placeholders = new key, plurals, no contexts; mention `nota-translations` as "coming in the next release" (finalised in T053)
- [X] T024 [P] [US1] Update the header comments of `Loc.cs`, `TranslationTable.cs`, `NotaLanguages.cs`, `TExtension.cs` to cite spec 077 (keys) instead of "the English source text is the key"

**Checkpoint (PR 1)**: `scripts/build.sh` green; quickstart § PR 1 passes; open PR 1 (`Closes` not yet — `Part of #476`).

---

## Phase 4: User Story 2 — Translate Nota without touching the app (P2) · PR 2 (nota-translations)

**Goal**: the repository validates languages against Nota's references and publishes `index.json`.

**Independent test**: quickstart § PR 2.

- [ ] T025 [P] [US2] Add `nota-translations/LICENSE` (full AGPL-3.0 text, same as nota's `LICENSE`)
- [ ] T026 [P] [US2] Write `nota-translations/README.md`: what the repo is, file shape (link to a copy of the contract in `nota-translations/FORMAT.md`), keys and the English reference (`reference/<version>.json`, `develop.json`), plurals and rule ids, placeholders, coverage, how to open a PR, how to preview a file in Nota (Load file…), licence
- [ ] T027 [P] [US2] Copy `specs/077-key-based-localization/contracts/language-file.md` and `contracts/index.md` into `nota-translations/FORMAT.md` (translator-facing wording, same rules)
- [ ] T028 [US2] Write `nota-translations/tools/validate.py` (stdlib; SPDX header): implements `contracts/language-file.md` exactly as `TranslationTable` (T003–T005): parse, key regex, plural rule ids, shape/placeholder checks against each `reference/*.json`, unknown key = error when in no reference / warning when only in older ones, coverage per key-set hash; `--fixtures tools/fixtures` mode compares against `verdicts.json`; exit non-zero with `languages/<code>.json: <key> — <problem>` lines
- [ ] T029 [P] [US2] Copy the fixtures and `verdicts.json` from `tests/Nota.SmokeTest/Fixtures/Languages/` (T009) into `nota-translations/tools/fixtures/`
- [ ] T030 [US2] Write `nota-translations/tools/sync_references.py` (stdlib): list `nota-daw/nota` tags `v*` and the `develop` head via the public GitHub API, fetch `src/managed/Nota.Application/Localization/en.json` at each tag lacking `reference/<version>.json` (skip tags whose file is missing or format < 2), always refresh `reference/develop.json`
- [ ] T031 [US2] Add index generation to `nota-translations/tools/validate.py` (`--write-index`): `index.json` per `contracts/index.md` — `formatVersion: 1`, `generated`, `references` (version → key-set hash), per valid language `code`, `name`, `version` (previous index version + 1 when the file's sha256 changed, else kept), `commit` (the `HEAD` being indexed), `path`, `size`, `sha256`, `coverage` per hash
- [ ] T032 [US2] Add `nota-translations/.github/workflows/check.yml`: on pull_request → `python3 tools/validate.py --fixtures tools/fixtures` and `python3 tools/validate.py languages/*.json`; job name `check`
- [ ] T033 [US2] Add `nota-translations/.github/workflows/publish.yml`: on push to `main`, daily schedule, `workflow_dispatch` → sync references, validate, write index, commit `reference/` + `index.json` as `github-actions[bot]` only if changed; the commit's own SHA is not the indexed one, so index `commit` = the content commit before the bot commit (document in the workflow)
- [ ] T034 [US2] Move French: `git mv`-equivalent import of `src/managed/Nota.App/Languages/fr.json` (v2, from PR 1) into `nota-translations/languages/fr.json`, with the commit message crediting its translators from `git log --format='%an' -- src/managed/Nota.App/Languages/fr.json` in nota (FR-012)
- [ ] T035 [US2] Repository settings via `gh api`: add the GitHub Actions integration (id 15368) as a bypass actor to ruleset `main: PR, squash` (id 26514643); after the first green `check` run, add `check` as a required status check
- [ ] T036 [US2] Push the initial content (README, LICENSE, FORMAT, tools, workflows, `languages/fr.json`) as the first commit on `main` (sets the default branch), run `gh workflow run publish.yml`, confirm `reference/develop.json` and `index.json` land; then open a test PR breaking a placeholder → `check` fails naming the key; close it

**Checkpoint (PR 2)**: quickstart § PR 2 passes.

---

## Phase 5: User Story 3 — Get a language from Preferences (P2) · PR 3

**Goal**: the app lists, downloads, updates and removes languages; ships English only.

**Independent test**: quickstart § PR 3 steps 1–4, 7–8.

- [ ] T037 [P] [US3] Create `src/managed/Nota.Infrastructure/Languages/InstalledLanguages.cs`: root `<data>/languages` (from `NotaPaths`); per code `<code>.json` + `<code>.meta.json` `{ "source": "repository" | "local", "path"?, "version"?, "commit"?, "sha256"?, "installed" }`; `List()`, `Load(code)` (repository → file, local → stored path), `InstallFrom(stagingFile, meta)` with atomic move, `Remove(code)`, cleanup of leftover `.staging-<code>.json` on start
- [ ] T038 [US3] Create `src/managed/Nota.Infrastructure/Languages/LanguageCatalog.cs`: fetch `<base>/main/index.json` (`<base>` = `https://raw.githubusercontent.com/nota-daw/nota-translations`, `NOTA_LANGUAGES_URL` overrides) once per run (cached); ignore an index with a higher `formatVersion` major ("update Nota to see more languages"); fit by own key-set hash, else the newest reference with version ≤ the app's (contracts/index.md § App behaviour); `DownloadAsync(entry)` via `RegistryClient.DownloadAsync(<base>/<commit>/<path>, size, sha256, staging, …)`, then parse + validate before `InstalledLanguages.InstallFrom`; any failure leaves the installed copy
- [ ] T039 [US3] Register `LanguageCatalog` and `InstalledLanguages` in the app's service setup (where `ModelStore` is registered) and expose `UpdateAvailable(code)` comparing installed `version` with the index
- [ ] T040 [US3] Update `src/managed/Nota.App/Languages/NotaLanguages.cs`: offered languages = embedded English + `InstalledLanguages.List()`; `Find(code)` loads installed files through `TranslationTable.Parse` + `ValidateAgainst(english)` (problems logged); no network on this path
- [ ] T041 [US3] Rebuild `src/managed/Nota.App/PreferencesWindow.Language.cs` per `contracts/language-row.md`: combo items English / installed (`· 97%` muted, or `· local file`) / separator / available (`· 82%` + download glyph); picking an available one downloads with inline `Downloading…` progress, then the existing restart note; failure `Couldn't download <Name> — <reason>` and the combo returns to the running language; `Update available` + **Update**; **Remove**; `More languages are available online` when the index is unreachable; follows `DESIGN.md` (Studio) — every new text via `Loc.T` with new keys in `en.json`
- [ ] T042 [US3] Network policy (research R10) in `PreferencesWindow.Language.cs` and the launch path: fetch the index when the row is shown, and at launch with the daily update check only when `Settings.CheckForUpdates` is on; when off, show **Show available languages** (fetch on click) and never request on Nota's own initiative
- [ ] T043 [US3] Remove `src/managed/Nota.App/Languages/fr.json` and its embedding: `src/managed/Nota.App/Nota.App.csproj` embeds no language file (remove the `Languages\*.json` item and its comment at line ~56)
- [ ] T044 [US3] Smoke: create `tests/Nota.SmokeTest/LanguageCatalogTests.cs` with an in-process `HttpMessageHandler` serving a fixture index + files (`tests/Nota.SmokeTest/Fixtures/Languages/index/`): list + fit + coverage, install, checksum mismatch keeps the old copy, higher index `formatVersion` ignored, offline → installed only, update offered when `version` grows, remove, `CheckForUpdates` off → zero requests at start; wire into `tests/Nota.SmokeTest/Program.cs`
- [ ] T045 [US3] Smoke: in `DesignTokenCheck.RunLocalization` drop the embedded-French coverage checks (French is validated in nota-translations) and add "the app embeds only `en.json`" (SC-008); switch `tests/Nota.SmokeTest/LanguageProjectTests.cs` from `fr.json` to the `xx.json` fixture

**Checkpoint**: quickstart § PR 3 steps 1–4, 7–8 pass.

---

## Phase 6: User Story 5 — Existing French users keep French after updating (P1) · PR 3

**Goal**: a stored but missing language is fetched in the background and applied after a restart.

**Independent test**: quickstart § PR 3 step 5.

*P1 but placed after US3: it is built on the catalog and installed store from US3.*

- [ ] T046 [US5] In `src/managed/Nota.App/App.axaml.cs` after the main window is shown: if `Settings.Language` ≠ `en`, has no valid installed file and `CheckForUpdates` is on, download it in the background through `LanguageCatalog` (start-up never waits); on success raise a "language ready" state shared with the Language row
- [ ] T047 [US5] Show the "<Name> is ready" + **Restart** notice once per run in the status bar in `src/managed/Nota.App/MainWindow.Updates.cs` (same pattern as the app update notice), and in the Language row; offline / checks off → row text `<Name> will be downloaded when Nota is online` or **Download <Name>** (contracts/language-row.md)
- [ ] T048 [US5] Smoke in `tests/Nota.SmokeTest/LanguageCatalogTests.cs`: stored `fr`, empty `languages/` → English this run, background install, ready state set; handler offline → English, stored choice kept, no install

---

## Phase 7: User Story 4 — Preview a translation in progress (P3) · PR 3

**Goal**: load a local file as the running language and see its problems.

**Independent test**: quickstart § PR 3 step 6.

- [ ] T049 [US4] Add **Load file…** to `src/managed/Nota.App/PreferencesWindow.Language.cs`: file picker for `.json`, parse + validate against English, store `source: local` + path through `InstalledLanguages`, select it, restart note; list up to 10 `key — problem` lines then `…and N more (see the log)`; a local language is never replaced by a download
- [ ] T050 [US4] `NOTA_UI_LANGUAGE` accepts a path to a language file as well as a code (this run only, never saved) in `App.axaml.cs` `InstallLanguage`; update its doc comment
- [ ] T051 [US4] Smoke in `tests/Nota.SmokeTest/LanguageCatalogTests.cs`: local `xx-broken.json` → loads, valid entries translated, broken ones English, problems match `verdicts.json`; missing local path at start → English + problem

---

## Phase 8: Polish & Cross-Cutting

- [ ] T052 Delete `scripts/loc-migrate.py` and the `keymap.tsv` ignore line in `.gitignore` (research R4)
- [ ] T053 [P] Finalise `CONTRIBUTING.md` § Translating Nota: translations live in `nota-daw/nota-translations`; adding a text in the app = `en.json` only
- [ ] T054 [P] Add the "sync references" step (`gh workflow run publish.yml --repo nota-daw/nota-translations` after the tag build) to `.claude/skills/release/SKILL.md`, then run `scripts/sync-agent-skills.sh`
- [ ] T055 [P] Fixture parity: confirm `tests/Nota.SmokeTest/Fixtures/Languages/verdicts.json` and `nota-translations/tools/fixtures/verdicts.json` are identical and both checks agree (R11)
- [ ] T056 Run every step of `specs/077-key-based-localization/quickstart.md` and note results in the PR descriptions

### Nota closing tasks *(PR gates — run for each nota PR)*

- [X] T057 Run `scripts/build.sh` (and `scripts/build-linux.sh` / `scripts/build-win.ps1` via CI) until native + managed + smoke test are green
- [X] T058 AGPL-3.0-only SPDX two-line header on every new source file (`scripts/loc-migrate.py`, `LanguageCatalog.cs`, `InstalledLanguages.cs`, `LanguageCatalogTests.cs`, nota-translations `tools/*.py`)
- [X] T059 New dependency: none → N/A
- [ ] T060 `CHANGELOG.md` `[Unreleased]`: PR 1 → none (no user-visible change; mention in the PR); PR 3 → Changed "Languages other than English are downloaded from Preferences → Language and kept up to date; the app ships with English only" + Added "Load a language file to preview a translation"
- [ ] T061 FEATURES.md via the `nota-features` skill and the user manual via `nota-docs` (EN + RU page on choosing, updating and previewing a language; shots harness uses `NOTA_UI_LANGUAGE=<path>` for French); `nota-docs/scripts/shortcuts.mjs` reads `PreferencesWindow.ShortcutGroups` by literal text — make it resolve `Loc.T("key")` through `src/managed/Nota.Application/Localization/en.json` (needed as soon as PR 1 is on `develop`); shortcuts: N/A
- [X] T062 Engine behaviour change: none → N/A

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (1)** → **Foundational (2)** → **US1 (3)**. Phase 2 and US1 share a branch: the build is red between T007 and T017.
- **US2 (4)** needs US1 merged (it publishes v2 keys from `develop`).
- **US3 (5)** needs US2's `index.json` to exist (tests use fixtures, but the release needs the real repo).
- **US5 (6)** needs US3 (T037–T040). **US4 (7)** needs T037 and T040.
- **Polish (8)** after the stories of each PR; T052–T054 in PR 3.

### Pull requests

| PR | Tasks |
|---|---|
| 1 (nota) | T001–T024, T057–T059, T062 |
| 2 (nota-translations) | T025–T036 |
| 3 (nota) | T037–T056, T057–T062 |

### Within each story

Language layer before call sites; store before catalog; catalog before UI; smoke checks
written with the code they cover.

### Parallel opportunities

- Phase 2: T006, T008, T009 alongside T003–T005.
- US1: T022, T023, T024 alongside the migration run (different files).
- US2: T025, T026, T027, T029 together; T028/T030/T031 sequential (same tool files).
- US3: T037 alongside T038's design; T044 can be written against the contracts before T041.
- PR 2 can be built while PR 1 is in review (on a v2 `fr.json` produced by the script).

## Parallel example: User Story 2

```bash
Task: "Add nota-translations/LICENSE"
Task: "Write nota-translations/README.md"
Task: "Write nota-translations/FORMAT.md from the contracts"
Task: "Copy fixtures into nota-translations/tools/fixtures/"
```

## Implementation strategy

1. **MVP = PR 1 (US1)**: keys everywhere, `en.json` as source, French still embedded. It
   delivers the issue's priority and stands alone — stop and validate with quickstart § PR 1.
2. **PR 2 (US2)**: repository and checks; no app change, safe to merge any time after PR 1.
3. **PR 3 (US3 + US5 + US4)**: downloads, then the upgrade path, then local preview; merge
   only with PR 2 live, and ship PR 2's content and PR 3 in the same release.
