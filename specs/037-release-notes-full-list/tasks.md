---

description: "Tasks: release notes with a full change list, commit links and contributors"
---

# Tasks: Release notes with a full change list, commit links and contributors

**Input**: Design documents from `specs/037-release-notes-full-list/`

**Prerequisites**: [plan.md](plan.md), [spec.md](spec.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/release-notes-format.md](contracts/release-notes-format.md),
[quickstart.md](quickstart.md)

**Tests**: requested by the spec (FR-011) — the smoke test's `--release-notes` section is
extended; scripts are checked by running them on a real range.

**Organization**: grouped by user story (US1 release text, US2 What's New, US3 old notes
and news posts).

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependencies)
- **[Story]**: the user story the task belongs to

---

## Phase 1: Setup

- [X] T001 Create the working branch `feat/release-notes-full-list` off an up-to-date `develop`, carrying the untracked `specs/037-release-notes-full-list/` along

---

## Phase 2: Foundational (shared format)

**Purpose**: the parser model every other part reads — markers and section kinds.

- [X] T002 In `src/managed/Nota.App/Changelog.cs` add `public enum SectionKind { Feature, Changes, Contributors }` and a trailing `SectionKind Kind = SectionKind.Feature` parameter to `ChangelogSection`; update the file's header comment to describe the four-part format and the `<!-- changes -->` / `<!-- contributors -->` markers (data-model.md § App model)
- [X] T003 In `ReleaseNotesParser.Parse` (`src/managed/Nota.App/Changelog.cs`): before skipping a `<!--` line, recognise exactly `<!-- changes -->` and `<!-- contributors -->` (trimmed, case-insensitive) — flush the current section and switch the kind for every following section; in a `Contributors` section the non-bullet line goes to `Text`; sections before any marker stay `Feature`, so files without markers parse exactly as today. Contributors must never precede changes in a valid file — a `<!-- changes -->` after `<!-- contributors -->` is ignored
- [X] T004 Extend `tests/Nota.SmokeTest/ReleaseNotesTests.cs` with a new-format sample (contract §1): lede + 2 feature sections → `Feature`; `**Added**`, `**Fixed**` → `Changes` with their bullets, an item ending in `([a1b2c3d](https://github.com/nota-daw/nota/commit/…))` kept verbatim; `**Contributors**` → `Contributors` with the link line as `Text`; the marker lines don't leak into lede, text or items; keep the existing old-format sample assertions unchanged (they also prove `Kind == Feature`)

**Checkpoint**: `dotnet run --project tests/Nota.SmokeTest -- --release-notes` green.

---

## Phase 3: User Story 1 — the release text has summary, full list, contributors (P1) 🎯 MVP

**Goal**: `/release prepare` writes notes in the four-part format with commit links and
linked contributors; the GitHub body shows all of it.

**Independent Test**: quickstart §1 and §2 (`github` target) on the `v0.56.1..origin/develop` range.

- [X] T005 [P] [US1] Create `scripts/release-credits.sh` (SPDX header, `set -euo pipefail`, usage line) per contract §3: `commits` prints `git log --reverse --format='%h%x09%H%x09%s' <from>..<to>`, no network; `contributors` checks `gh` and `jq`, for each sha in the range queries `gh api graphql` for `repository(owner:"nota-daw",name:"nota"){object(oid:$oid){... on Commit{authors(first:20){nodes{name email user{login}}}}}}` (batch several shas per query with aliases if simple), fails with a clear message when a commit is not on GitHub, drops logins ending in `[bot]`, login `claude` and email `noreply@anthropic.com`, counts commits per login, prints one line `[@login](https://github.com/login)` joined by `, ` (most commits first, ties by login), and for authors with `user: null` prints `warn: no GitHub login for <name> <email>` to stderr and appends the plain name only if its email/name doesn't belong to a listed login's other commits; `chmod +x`
- [X] T006 [P] [US1] In `scripts/release-notes.sh` change `body()` to drop every line matching `^<!--.*-->$` (not only the SPDX one) so the markers never reach any output; `github` keeps all parts; update the header comment to name the four parts
- [X] T007 [US1] Rewrite `.claude/skills/release/SKILL.md` § P4: step 1 *Gather* takes the previous tag by version (`git tag --list 'v*' --sort=-v:refname | head -1`, explain why `git describe` on develop is wrong — research R3) and runs `scripts/release-credits.sh <prev-tag> origin/develop commits` / `contributors`; step 2 *Reconcile* also builds the full list (every user-facing change of the range, one item per change, each with its commit link(s), internal commits left out but their authors kept); step 3 *Write* shows the full four-part format from contract §1 with both markers, the group order Added / Changed / Fixed / Removed (empty omitted), RU titles (Добавлено, Изменено, Исправлено, Удалено, Участники), the item syntax `• text ([short](https://github.com/nota-daw/nota/commit/<sha>))`, the contributors line, "no **Also** in new notes — the full list replaces it", and that EN and RU share groups, order, links and contributors; step 4 says the posts carry summary + contributors and the GitHub body everything (show `github` too). Update the intro table of targets and the "Don't" list (don't hand-edit contributors; resolve every `warn:` line)
- [X] T008 [US1] Also in `.claude/skills/release/SKILL.md`: fix the release poster step / P8 wording if it references "Also"; mention in P8 that the develop → main PR body (`github` output) now includes the full list

**Checkpoint**: quickstart §1 lists the range's commits and the contributors (≥ `ambertape`, `rafi612`, no `claude`); a sample rendered with `github` shows all four parts and no `<!--` lines.

---

## Phase 4: User Story 2 — What's New shows the new structure (P1)

**Goal**: the window renders summary → "All changes" groups → contributors, all expanded,
with clickable links.

**Independent Test**: quickstart §4 with the 9.9.9 sample in the headless harness / real app.

- [X] T009 [US2] In `src/managed/Nota.App/WhatsNewWindow.cs` `EntryBlock`: for release notes render sections by `Kind` — `Feature` as today; before the first `Changes` section add a 1 px divider (`Brush.Border`-style seam token per DESIGN.md) and an "All changes" heading in the features' title style; each `Changes` section uses the `SectionLabel` title style (as the changelog fallback) with its bullets; a `Contributors` section uses `SectionLabel` + `Paragraph(section.Text, "Brush.TextSecondary")`; everything expanded; changelog-fallback entries unchanged
- [X] T010 [US2] In `AppendInlineMarkdown` (`src/managed/Nota.App/WhatsNewWindow.cs`) record for each `[label](url)` the character range of its run in the block's text and the URL (keep a per-block list, e.g. attached via a private field/dictionary keyed by the `TextBlock`); on the block's `PointerPressed` hit-test the point with `block.TextLayout.HitTestPoint(...)`, and if the position falls inside a link range with an `http`/`https` URL open it via `TopLevel.GetTopLevel(this)?.Launcher.LaunchUriAsync(new Uri(url))` and mark the event handled; on `PointerMoved` set a hand cursor and underline (`TextDecorations.Underline`) the hovered link's run, reset on leave. Applies to every link in old notes too (FR-008)
- [X] T011 [US2] Verify with the UI-verify recipe (isolated `NOTA_DATA_DIR`, headless Avalonia render or the real app): a temporary 9.9.9 EN sample shows the new layout within the 396 px column without clipping; hovering a hash shows the hand cursor; clicking opens the commit URL; capture a screenshot for the PR (outside the repo, `/tmp`); remove the temporary sample afterwards

**Checkpoint**: the new layout renders; old versions look as before.

---

## Phase 5: User Story 3 — old notes and news posts keep working (P2)

**Goal**: shipped notes display unchanged; posts omit the full list.

**Independent Test**: quickstart §2 (`post-ru`, old `0.56.1 github` byte-identical) and §3.

- [X] T012 [P] [US3] In `scripts/release-notes.sh` for `post-en` / `post-ru` drop the lines from `<!-- changes -->` up to (not including) `<!-- contributors -->` before stripping comments, then collapse runs of blank lines; files without markers render as today
- [X] T013 [US3] Check that `scripts/release-notes.sh 0.56.1 github`, `post-en`, `post-ru` are byte-identical before and after the change (save outputs from `origin/develop` first with `git stash`/a temp worktree, diff after)
- [X] T014 [P] [US3] In `tests/Nota.SmokeTest/ReleaseNotesTests.cs` add: for every shipped `release-notes/*.en.md`, a file that contains `<!-- changes -->` must also contain `<!-- contributors -->` after it, have at least one `Changes` section, and every `Changes` bullet must end with a commit link matching `\(\[[0-9a-f]{7}\]\(https://github\.com/nota-daw/nota/commit/[0-9a-f]{40}\)(, \[[0-9a-f]{7}\]\(https://github\.com/nota-daw/nota/commit/[0-9a-f]{40}\))*\)$`; files without markers only need today's checks

**Checkpoint**: smoke test green, old renders identical.

---

## Phase 6: Polish & Cross-Cutting

- [X] T015 Run quickstart.md §1–§4 end to end and note results
- [X] T016 [P] Update the header comments of `src/managed/Nota.App/WhatsNewWindow.cs` and `tests/Nota.SmokeTest/ReleaseNotesTests.cs` to mention the full list, contributors and clickable links

### Nota closing tasks *(PR gates)*

- [X] T017 Run `scripts/build.sh` until native + managed + smoke test are green
- [X] T018 AGPL-3.0-only SPDX header on `scripts/release-credits.sh` (the only new file)
- [X] T019 New dependency: N/A (`gh`, `jq` already used by `release-verify.sh`)
- [X] T020 `CHANGELOG.md` `[Unreleased]` → Changed: What's New shows each release's full list of changes with commit links and its contributors; links in What's New open in the browser
- [X] T021 `nota-features`: update FEATURES.md if it describes What's New / release notes; `nota-docs`: check the What's New mention in the user docs; `nota-shortcuts`: N/A
- [X] T022 Engine behaviour change: N/A (parser tests extended in T004, T014)

---

## Dependencies & Execution Order

- **Setup (T001)** → **Foundational (T002–T004)** → stories.
- **US1** (T005–T008): T005, T006 parallel; T007–T008 after them (the skill names the scripts' interfaces). Doesn't need US2.
- **US2** (T009–T011): needs T002–T003; independent of US1's scripts.
- **US3** (T012–T014): T012 touches `release-notes.sh` after T006 (same file); T014 after T004.
- **Polish** after all stories.

### Parallel Opportunities

- T005 ∥ T006 ∥ T009 (different files) once the foundation is in.
- T014 ∥ T012 (test file vs. script).

## Parallel Example: User Story 1

```bash
Task: "Create scripts/release-credits.sh (commits | contributors)"
Task: "release-notes.sh: strip every comment line in body()"
```

## Implementation Strategy

1. T001–T004: the format and parser, proven by the smoke test.
2. US1 (MVP): the release produces the new text — usable right away for the next release
   on GitHub.
3. US2: What's New displays it; ship together with US1 so the next release's bundled notes
   render correctly.
4. US3: guard the old notes and the post format.
5. Closing gates, then `/speckit-converge`.

## Notes

- One PR for the whole feature; the user commits and pushes (no commits by the agent).
- Old `release-notes/0.5x.*` files are not rewritten.

## Phase 7: Convergence

- [X] T023 Make `scripts/release-credits.sh contributors` fail when GitHub lacks a commit: for an unknown sha `gh api graphql … --jq '… .value'` prints an empty line, not `null`, so the `grep -qx 'null'` check never fires and that commit's authors are silently dropped — emit each answer as compact JSON (e.g. `--jq '.data.repository | to_entries[] | (.value // {missing: true}) | tojson'` or count non-null answers with `oid`), compare with the number of shas queried, and exit 1 with "some commits … aren't on GitHub yet — push them first"; check with a range ending in a local unpushed commit per FR-004, contract §3 (partial)
- [X] T024 Match the part markers one way everywhere — trimmed, case-insensitive, whitespace inside the comment tolerated (`<!--\s*changes\s*-->`): `ReleaseNotesParser` in `src/managed/Nota.App/Changelog.cs` (a shared static `Regex` per marker instead of `Equals`), the awk patterns in `scripts/release-notes.sh` (`tolower($0)`), and the shipped-file check in `tests/Nota.SmokeTest/ReleaseNotesTests.cs` (use the parser's regexes instead of `IndexOf(..., Ordinal)`) per data-model: Curated notes file, T003/T012/T014 (partial)
