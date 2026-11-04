# Quickstart: validating key-based texts and downloadable languages

Prerequisites: a Release build (`scripts/build.sh`), `gh` signed in, Python 3.
Use a throw-away data root: `export NOTA_DATA_DIR=$(mktemp -d)`.

## PR 1 — keys, no visible change (story 1, SC-001…003)

Baseline (pre-migration) commit: `7b58fe881bc6503e0ff7508dc1bd4c180dc909bd` (`origin/develop`, 2026-11-04).

1. Before migrating, capture reference screens in English and French with the headless shots
   harness (`NOTA_UI_LANGUAGE=en` / `fr`), every window of the nota-docs shot list.
2. `python3 scripts/loc-migrate.py --plan` → review `keymap.tsv`; then
   `python3 scripts/loc-migrate.py --apply`.
3. `scripts/build.sh` → green; the localization block of the smoke test reports keys used =
   keys in `en.json`, 0 contexts, 0 text-keyed calls.
4. `python3 scripts/loc-migrate.py --verify <pre-migration commit>` → for every old call
   site, `en[new key]` equals the old English text and `fr[new key]` the old French value:
   0 differences. Re-capture the screens and compare with step 1 as a spot check (SC-002).
5. Change one English value in `en.json`, rebuild, run in French → same French text; smoke
   test green (SC-001).
6. Add `Loc.T("nope.missing")` → smoke test fails naming the key; add an unused key to
   `en.json` → fails naming it.

## PR 2 — nota-translations (story 2, SC-004)

1. `gh workflow run publish.yml --repo nota-daw/nota-translations` → `reference/develop.json`
   and `index.json` appear on the `published` branch, committed by the bot.
2. Open a PR that breaks a placeholder in `languages/fr.json` → `check.yml` fails naming the
   key, in < 5 min; fix it → passes and reports French coverage.
3. Run `tools/validate.py --fixtures` and the app smoke test's fixture parity check → same
   verdicts.

## PR 3 — downloading (stories 3–5, SC-005…008)

Serve a test copy: `python3 -m http.server -d <nota-translations checkout>` and
`export NOTA_LANGUAGES_URL=http://127.0.0.1:8000` (layout `<base>/published/index.json`,
`<base>/<commit>/<path>` reproduced by the smoke fixture builder).

1. Clean data dir, open Preferences → Language: English + Français · 100% (available).
   Pick it → `Downloading…` → restart note; restart → French (SC-005).
2. Stop the server, restart → French (SC-006); open the row → `More languages are available online`.
3. Bump `version` and content of `fr` in the copy, restart with the server up → `Update available`.
4. Corrupt the served file (checksum mismatch) → `Couldn't download…`, installed copy kept.
5. Settings with `"Language": "fr"`, empty `languages/`, server up → starts in English,
   status bar `Français is ready` + Restart → French after restart (SC-007). Server down →
   English, row says it will download when online.
6. **Load file…** with `tests/Nota.SmokeTest/Fixtures/Languages/xx-broken.json` → restart →
   valid entries translated, broken ones English, problems listed.
7. `CheckForUpdates` off → no request at start (watch the server log); **Show available
   languages** fetches on click.
8. Inspect the package (`.app` / installer / AppImage): only `en.json` embedded (SC-008).
9. `scripts/build.sh` → green, including the catalog smoke checks on the local fixture.
