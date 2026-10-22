# Quickstart: validating the release-notes full list

Prerequisites: a built tree (`scripts/build.sh`), `gh` signed in, `jq`.

## 1. Gather from a real range

```bash
scripts/release-credits.sh v0.56.1 origin/develop commits
```

Expect one line per commit since v0.56.1 (23 on 2026-10-22), oldest first.

```bash
scripts/release-credits.sh v0.56.1 origin/develop contributors
```

Expect one line with `[@login](https://github.com/login)` links — at least `ambertape` and
`rafi612` — no `claude`, no bots; any unmapped author as a `warn:` line on stderr.

## 2. Render a new-format sample

Write a throwaway `release-notes/9.9.9.en.md` / `.ru.md` from
[contracts/release-notes-format.md](contracts/release-notes-format.md) §1, then:

```bash
scripts/release-notes.sh 9.9.9 github
```

Expect summary, Added/Changed/Fixed, Contributors, install table; no `<!--` lines.

```bash
scripts/release-notes.sh 9.9.9 post-ru
```

Expect summary + «Участники» + download link, no full list. Old files
(`scripts/release-notes.sh 0.56.1 github`) render byte-identical to before. Delete the
9.9.9 files afterwards.

## 3. Smoke test

```bash
dotnet run --project tests/Nota.SmokeTest -- --release-notes
```

Expect all checks green: old sample unchanged, new sample yields `Feature` / `Changes` /
`Contributors` sections, every shipped notes file parses.

## 4. What's New

Bundle the 9.9.9 sample (or point the headless harness at it, see the UI-verify recipe with
an isolated `NOTA_DATA_DIR`) and open Help → What's New:

- summary first, then a divider + "All changes", groups as section labels with bullets,
  then Contributors — all expanded;
- hover a commit hash: hand cursor + underline; click: the commit page opens in the
  browser;
- versions with old-format notes look as before.

Capture a screenshot of the new layout for the PR.
