# Research: Key-Based Interface Texts and Downloadable Languages

Decisions behind [plan.md](plan.md). Spec: [spec.md](spec.md). Builds on spec 068
([research](../068-ui-language/research.md)); where nothing is said here, 068 stands.

## What the code looks like today (measured 2026-11-04)

| Fact | Value | Consequence |
|---|---|---|
| `Loc.T` / `F` / `Plural` calls in `src/managed` | ~4,630 in 243 files | migration must be a tool, not hand edits |
| `Loc.FC` calls | 0 | `FC` is removed, not migrated |
| `Loc.T(text, context)` calls / `\|` keys in `fr.json` | 3 / 4 | contexts become distinct keys by hand-checked rule |
| Calls whose text argument is not a literal | 1 (`TExtension.ProvideValue`) | every key is statically visible; the smoke test's regexes already find all of them |
| `{app:T …}` uses in `.axaml` | 118 | XAML is migrated by the same tool |
| `Loc.Plural` calls / plural entries in `fr.json` | ~60 / 53 | plural keys need a plural object in `en.json` |
| Interpolated text inside `Loc.T/F` | 0 (smoke test forbids it) | no call needs restructuring |
| Plural rules | hard-coded per code in `UiLanguage.Plural` (`fr` ⇒ 0 and 1 are one) | a downloaded language must bring its rule (R6) |
| Existing downloader | `RegistryClient.DownloadAsync(url, size, sha256, …)`, used by `ModelStore` with staging + swap | reused for languages (R8) |
| Update-check setting | `Settings.CheckForUpdates` (default on) | language checks follow it (R10) |

## R1 — File format: key-based JSON

**Decision**: JSON, `formatVersion: 2`, `strings` keyed by stable keys; a value is a string or
`{ "one": …, "other": … }` (more categories later). The same shape for `en.json` (embedded),
repository files and local files. `TranslationTable` keeps its validation and gains
key-format and plural-rule checks. *(Confirmed in spec Clarifications Q1.)*

**Rationale**: the app downloads files at run time, which JSON already does; plurals already
work; JSON key/value with plural objects is a first-class format in Weblate and Crowdin
("i18next JSON v4"-like); diffs stay clean; no dependency.

**Alternatives**: `.resx` + `ResourceManager` — compiled into satellite assemblies, a
downloaded `.resx` needs an extra reader (`System.Resources.Extensions`), no plurals, noisy
XML. gettext `.po` — good tooling but a parser dependency or a hand-written one, and
msgid-as-key is the very scheme being left. Avalonia resource dictionaries — XAML only.

## R2 — Key naming

**Decision**: `area.segment[.segment…]`, each segment `[a-z0-9_]+`, at least two segments,
≤ 80 characters: regex `^[a-z0-9_]+(\.[a-z0-9_]+)+$`.

- First segment = area, from the file that uses it: `PreferencesWindow*.cs` → `preferences`,
  `ArrangementView*.cs` → `arrangement`, `Controls/<Device>Views.cs` → `device.<device>`,
  `MainWindow*.cs` → `main`, `*Menu*` → `menu`, etc. The mapping is a table in the
  migration tool, reviewed once.
- Optional second segment = sub-area from the partial-file suffix
  (`PreferencesWindow.Language.cs` → `preferences.language`).
- Last segment = a slug of the English text: lower-case, ASCII words joined by `_`,
  first ≤ 6 words, punctuation and `{n}` dropped (`"Applies after restart"` →
  `applies_after_restart`, `"Delete {0} clips?"` → `delete_clips`).
- Suffixes for roles where one text has several: `.tooltip`, `.title`, `.placeholder`.
- **Shared texts**: an identical English text (same plural shape) used in ≥ 3 areas gets
  `common.<slug>` (`common.cancel`, `common.save`), so translators translate "Cancel" once.
  Used in 2 areas → two keys (cheap, and the meanings often drift).
- Collisions after slugging get `_2`, `_3`, which the review step renames.
- The 3 context calls / 4 `|` keys become separate keys named by their context
  (`menu.file.open` vs `clip.open`).

**Rationale**: readable at the call site (the slug carries the English meaning), groupable
by translators, stable when English text changes. A key is never derived again after the
migration: renaming English text keeps its key.

**Alternatives**: purely semantic hand-chosen keys for 3,600 texts — weeks of naming;
hash keys — unreadable; keeping English as key plus a separate id — the problem not solved.

## R3 — Call-site API

**Decision**:

```csharp
Loc.T("preferences.language.restart")
Loc.F("arrangement.delete_clips_confirm", count)
Loc.Plural("arrangement.selection.tracks", n)      // n is {0}; key holds { one, other }
{app:T preferences.language.title}                   // XAML
```

`FC` and every `context` parameter are removed. The English one/other strings disappear
from `Plural` calls (they live in `en.json`). English is built into `Loc`: `en.json` is embedded in
`Nota.Application` next to `Loc`, so English is there before `Install`, in every test
that never installs a language, and in projects that do not reference `Nota.App` (the smoke
test does not); `Install(table, code)` keeps its 068 signature. A key missing even from English (cannot happen past
the smoke test) shows the key and logs once.

**No generated constants / source generator** in this feature: it needs a Roslyn generator
project (build-time dependency, `Microsoft.CodeAnalysis`), and the smoke test already
proves every key exists. Can be added later without touching `en.json`.

## R4 — Migration tool

**Decision**: `scripts/loc-migrate.py` (Python 3 standard library only, like the other
`scripts/*.py`). Steps, each re-runnable on a clean tree:

1. Scan `src/managed/**/*.cs` and `*.axaml` with the same patterns as
   `DesignTokenCheck.Localization.cs` (`LocT`, `LocF`, `LocPlural`, `LocXaml`); fail on any
   call it cannot parse (there are none today).
2. Build the text → key map (R2), write `keymap.tsv` (text, context, plural, files, key)
   for review; a hand-edited `keymap.tsv` is read back on the next run, so reviewed names
   stick.
3. Write `Nota.Application/Localization/en.json` (`formatVersion: 2`, keys sorted by area) and convert
   `Languages/fr.json` through the map (orphans reported, not carried).
4. Rewrite call sites in place, keeping formatting; rewrite `Plural(n, "one", "other")` to
   `Plural("key", n)`.

The tool lives in the repository for the length of this feature (PR 1 and review fixes) and
is deleted in PR 3; `keymap.tsv` is not committed.

**Alternatives**: a Roslyn-based C# tool — exact but adds `Microsoft.CodeAnalysis` for a
one-off; hand edits — not reviewable.

## R5 — English as the reference

**Decision**: `src/managed/Nota.Application/Localization/en.json` is embedded in `Nota.Application` and loaded by `Loc` itself; it holds every key,
English text, plural shape. Its **key-set hash** = SHA-256 (hex, first 16 chars) over the
sorted lines `key \t shape \t placeholders` (`shape` = `text` or `plural`, `placeholders` =
sorted `{n}` indices). Changing only English wording keeps the hash; adding / removing a
key, switching text↔plural or changing placeholders changes it. Contributor rule (in
`CONTRIBUTING.md`): changing a text's placeholders or meaning ⇒ new key.

## R6 — Plural rules travel with the file

**Decision**: a language file names its rule: `"plural": "one_is_1"` (English, German…),
`"one_is_0_1"` (French). The app implements the named rules (`UiLanguage.Plural(rule, n)`);
an unknown rule is a load error for that file (language not offered). New languages that
need `few` / `many` add a rule id in a later app version and say so in the README.

**Alternatives**: a CLDR rule expression in the file — needs an expression evaluator;
per-code rules in the app (today) — breaks "add a language without an app release".

## R7 — Repository layout and publication

**Decision** for `nota-daw/nota-translations` (`main`):

```
README.md               translator guide
LICENSE                 AGPL-3.0-only text
languages/<code>.json   one file per language (formatVersion 2), never English
reference/<ver>.json    en.json of Nota release <ver>; reference/develop.json = develop head
index.json              generated — what the app reads
tools/validate.py       the checks (stdlib Python)
tools/sync_references.py
.github/workflows/check.yml      PRs: validate every language against every reference
.github/workflows/publish.yml    push to main / schedule / dispatch: sync references, regenerate index.json, commit
```

- **References are pulled, not pushed**: `publish.yml` lists `nota` tags `v*` and the
  `develop` branch through the public API and fetches `src/managed/Nota.Application/Localization/en.json`
  at each one lacking a reference (daily schedule + `workflow_dispatch`). No cross-repository
  secret. The nota release workflow is unchanged; the `release` skill gains a step "run
  publish in nota-translations" for an immediate sync.
- `publish.yml` commits to `main` as `github-actions[bot]`: the ruleset `main: PR, squash`
  gets the GitHub Actions integration as a bypass actor.
- `develop.json` lets translators work on keys before they ship; the app uses it only in dev
  builds (version `0.0.0-dev`).

## R8 — How the app downloads

**Decision**: `LanguageCatalog` (Infrastructure, next to `ModelStore`):

- Reads `https://raw.githubusercontent.com/nota-daw/nota-translations/main/index.json`
  (`NOTA_LANGUAGES_URL` overrides the base, for tests and staging).
- Each language entry pins `commit`, `path`, `size`, `sha256`; the file URL is
  `…/nota-translations/<commit>/<path>`, so the hash always matches what is fetched (no race
  with a newer `main`).
- `RegistryClient.DownloadAsync` with size + sha256; staging file + atomic move into
  `<data>/languages/<code>.json`, with `<code>.meta.json` (`source: repository`, `commit`,
  `sha256`, `version`). A failed download leaves the installed copy.
- Fit: the app computes its own key-set hash from embedded `en.json` (R5); it picks the
  entry's coverage for that hash; if the hash is not in `index.json`, the coverage of the
  newest older reference is shown (Clarifications Q3). The file is the same either way —
  unknown keys are ignored, missing ones are English.

## R9 — Local files

**Decision**: "Load file…" stores the path in `<data>/languages/<code>.meta.json` with
`source: local` (the file is read in place at each start, so a translator edits and
restarts). Problems are listed in the Language row and the log. `NOTA_UI_LANGUAGE` accepts
a code or a path to a language file (screenshots in nota-docs and nota-site use French
without installing it).

## R10 — Network policy

**Decision**:

- Listing available languages fetches `index.json` when the Language row is shown (at most
  once per run, cached), and at launch with the daily update check when
  `CheckForUpdates` is on.
- `CheckForUpdates = false` ⇒ no request Nota starts on its own: the row shows installed
  languages and a **Show available languages** button that fetches on click; the automatic
  download of a stored but missing language (story 5) becomes a **Download French** button.
- Downloads only on a user choice, or for story 5 when checks are on. Nothing is executed;
  files are parsed and validated as data.

## R11 — Validation in two places

**Decision**: `tools/validate.py` in nota-translations and `TranslationTable` in the app
implement [contracts/language-file.md](contracts/language-file.md). Drift is caught by
shared fixtures: `tests/Nota.SmokeTest/Fixtures/Languages/` holds valid and invalid files
with the expected verdict; the same fixture set is copied into `nota-translations/tools/fixtures`
and both checks must agree.

**Alternatives**: running a .NET tool from the app repo in the translations CI — one
implementation, but every translator PR builds .NET and checks out nota.

## R12 — What the smoke test checks after each PR

| PR | Checks |
|---|---|
| 1 | every key used in `.cs` / `.axaml` exists in `en.json`; no unused `en.json` key; key regex; `Plural` keys are plural objects, `T`/`F` keys are not; `F` placeholders consistent; embedded `fr.json` (v2) coverage + placeholders as today; raw-literal ratchet unchanged |
| 3 | the same minus embedded `fr`; catalog against a local fixture index (install, checksum mismatch, offline, update offered, remove, local file with errors, stored-but-missing language); fixture parity (R11) |

`LanguageProjectTests` switches from `fr.json` to the `xx` fixture in PR 3.
