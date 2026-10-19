---
name: release
description: Cut a Nota release in three phases. `prepare` (on a release/X.Y.Z branch from develop) promotes CHANGELOG.md [Unreleased] to a dated version, writes the curated EN + RU release notes in release-notes/ (the one source for the GitHub release and the news channels), bumps the app and engine versions, makes the release image and the PR texts. `tag` (on main, after the user merged the release PR into develop and the develop → main PR) creates the vX.Y.Z tag. `verify` (after the user pushed the tag) waits for the build and checks the GitHub draft against the curated notes. Run this when the user asks to release, ship, cut a version, tag a release or check a release build.
user-invocable: true
---

# Cutting a Nota release

Two files describe a release, for two readers:

- **`CHANGELOG.md`** is the raw working log. Entries land under `## [Unreleased]` as work
  merges ("what we did, we write"), so it can repeat itself or hold changes that a later
  entry undid. At release time it is only promoted to `## [X.Y.Z] — date` and is never
  rewritten. The CI guard requires the section, and What's New falls back to it for a
  version without curated notes.
- **`release-notes/X.Y.Z.en.md`** and **`release-notes/X.Y.Z.ru.md`** are the curated
  text: summarised, deduplicated, checked against the code. **Everything published comes
  from them**, and the app bundles the EN file for its What's New window, so the notes must
  be committed on the release branch before the tag is built. `scripts/release-notes.sh X.Y.Z <target>` renders them for each place:

  | target    | where it goes                                   |
  |-----------|-------------------------------------------------|
  | `github`  | GitHub release body (the workflow uses this)    |
  | `post-en` | English news channels                           |
  | `post-ru` | Russian news channels                           |

The release runs through the branches like this. **The user does every commit, push and PR
by hand.** This skill never commits, pushes or opens a PR (see the user-commits rule).

```
release/X.Y.Z ── /release prepare ── user commits, pushes, opens PR → develop (squash)
develop       ── CI green, user merges ── user opens PR develop → main (merge commit)
main          ── CI green, user merges ── /release tag ── user pushes the tag
CI            ── release.yml builds 6 files + draft release ── /release verify ── user publishes
```

`develop` and `main` are protected (rulesets): changes land only through a pull request
with the `macOS`, `Windows` and `Linux` CI checks green; `develop` takes squash merges,
`main` takes merge commits only, so `develop` → `main` never rewrites history. Release
tags `v*` can be created only by maintainers.

**Argument** (`/release <phase> [bump]`): the phase is `prepare` (default), `tag` or
`verify`. For `prepare` the bump is `minor` or `patch` (see step P2).

---

## Phase 1 — `prepare` (on a `release/X.Y.Z` branch from `develop`)

### P1. Preflight. Stop if any check fails

1. On `develop`: `git rev-parse --abbrev-ref HEAD`.
2. The tree is clean (`git status --porcelain` is empty). Otherwise ask the user to commit
   or stash first, so the release changes stay separate.
3. Up to date: `git fetch origin`, then `git rev-list --left-right --count origin/develop...HEAD`
   must show `0` behind and `0` ahead (unpushed commits on `develop` can't reach it any
   more except through a pull request).
4. `## [Unreleased]` has real entries.
5. Once the version is chosen (P2), create the branch: `git switch -c release/X.Y.Z`. This is
   the only branch operation the skill does itself; every later step edits files on it.

### P2. Choose the bump

The app and the engine always move together, by the same kind of bump:

| bump    | when                                         | app             | engine          |
|---------|----------------------------------------------|-----------------|-----------------|
| `minor` | a major feature (new device, view, workflow) | 0.51.2 → 0.52.1 | 0.30.3 → 0.31.1 |
| `patch` | fixes, polish, small additions               | 0.51.2 → 0.51.3 | 0.30.3 → 0.30.4 |

A component that resets starts at **1, never 0**. If the user gave no bump, propose one
from the `[Unreleased]` content and confirm it with them. Preview it with
`scripts/release-bump.sh <bump> --dry-run`. It refuses a version whose tag already exists.

### P3. Promote CHANGELOG.md

Rename `## [Unreleased]` to `## [X.Y.Z] — YYYY-MM-DD` (date from `date +%F`). Add a fresh
empty `## [Unreleased]` above it. **Don't touch the entries**: no Highlights and no
rewording. The changelog stays the honest log. Keep the preamble and older sections as they
are.

### P4. Write the curated notes, EN and RU

This is the main work of the phase. Create `release-notes/X.Y.Z.en.md` and
`release-notes/X.Y.Z.ru.md`.

1. **Gather.** Read the whole `[X.Y.Z]` section you just promoted, then
   `git log --oneline <prev-tag>..HEAD` (previous tag:
   `git describe --tags --abbrev=0 --match 'v*'`).
2. **Reconcile.** Merge entries that describe the same thing. Where entries conflict (a
   shortcut reassigned twice, a feature added and then reworked), **the code decides**:
   read the handler, the default or the UI and describe what ships. Drop anything that
   was added and then removed within the same release, and leave out internal or
   mechanical changes (refactors, CI, tests) unless a user can feel them.
3. **Write.** Each language natively, not as a translation. Format (bold titles and `•`
   bullets, no `#` headings, so the text pastes into Telegram as-is):

   ```
   <!-- SPDX-License-Identifier: AGPL-3.0-only -->
   One or two sentences on what defines this release.

   **1. Biggest user-facing win**
   A short paragraph: what it is and why a musician cares. Not every changelog detail.

   **2. Next feature**
   …

   **Also**
   • visual changes, notable fixes, small conveniences, one line each
   • any behaviour change users must know: a moved or removed shortcut, a changed default
   ```

   Rank the sections by importance. A patch release can be just the lede plus **Also**.
   Leave out the title and the download link, because `release-notes.sh` adds them per
   target. Use the user's product language: "track", "device card", "piano roll", not class
   names.
4. Show the user both rendered posts (`scripts/release-notes.sh X.Y.Z post-en`, `post-ru`)
   and iterate until they approve. The rest of the release depends on this text.

### P5. Bump the versions

`scripts/release-bump.sh <bump>` writes `VERSION` and `src/native/nota.engine/VERSION`
(both without a trailing newline, read with `.Trim()` / CMake). The CI checks the tag against
`VERSION`, so the two must agree.

### P6. Feature list and docs

- Run `nota-features` in `audit` mode: fold this version's entries into `FEATURES.md` and
  set its header to `X.Y.Z`. That change goes into the release commit.
- Run `nota-docs` in `audit` mode over the same entries. Its changes are committed in
  `../nota-docs`. Missing docs don't block the release; list them in the report.

### P7. Release image

Build a 16:9 poster in `../media/news/X.Y.Z-src/` (outside the repo). Start from the last
release's `poster.html` (`ls -d ../media/news/*-src | sort -V | tail -1`) and copy its
fonts.

- Header: the logo, `Nota X.Y.Z` (version in brass), a one-line subtitle taken from the
  notes' lede, and a mono tag row with the 2–4 headline features.
- Two or three cards with real screenshots of the headline features. Shoot them from the
  app (see the UI-verify recipe, with an isolated `NOTA_DATA_DIR`) or reuse fresh shots
  from `../nota-docs`. Use Ember Graphite colours (`nota-design`).
- Render it at 2×:

  ```bash
  "/Applications/Google Chrome.app/Contents/MacOS/Google Chrome" --headless=new \
    --hide-scrollbars --window-size=1280,720 --force-device-scale-factor=2 \
    --screenshot="$PWD/../media/news/X.Y.Z.png" "file://$PWD/../media/news/X.Y.Z-src/poster.html"
  ```

- Look at the PNG (Read it) and check for overlaps, clipped text or empty space before
  showing it to the user. Also save the rendered posts next to it as
  `../media/news/X.Y.Z.en.md` and `X.Y.Z.ru.md` (the output of `post-en` / `post-ru`).

### P8. Hand over to the user

Don't commit. Give the user:

- the version change (`app a → b`, `engine c → d`) and the list of changed files:
  `CHANGELOG.md`, `VERSION`, `src/native/nota.engine/VERSION`, `FEATURES.md`,
  `release-notes/X.Y.Z.{en,ru}.md`,
- the commit message `(chore) Release X.Y.Z` and the push command
  `git push -u origin release/X.Y.Z`,
- the **PR into develop**: title `(chore) Release X.Y.Z` (it becomes the squash commit
  message), body = a short list of the changed files,
- the **PR text** for develop → main, opened after the first one is merged. Title
  `Release X.Y.Z`, body = the output of `scripts/release-notes.sh X.Y.Z github`, so the
  reviewer reads exactly what will be published. Merge it with **Create a merge commit**
  (the only method `main` allows),
- the image path and any docs gaps,
- the next step: merge both PRs once CI is green, then run `/release tag`.

---

## Phase 2 — `tag` (on `main`, after the merge)

1. If the user is still on develop, tell them to run `git switch main && git pull --ff-only`.
   Don't switch branches on their tree yourself if it has changes.
2. Run `scripts/release-tag.sh`. It repeats the CI guard locally (on main, clean, equal to
   `origin/main`, the tag is free locally and on origin, the `## [X.Y.Z]` section and both
   notes files exist) and then creates the annotated tag `vX.Y.Z`. **It doesn't push.**
3. Give the user the push command and the Actions link
   (`https://github.com/nota-daw/nota/actions/workflows/release.yml`):

   ```bash
   git push origin vX.Y.Z
   ```

If the script fails, report its message as is. Common causes: the PR isn't merged yet, or
local main is behind. After the tag, `develop` needs no sync: the merge commit on `main`
already has `develop` as a parent.

---

## Phase 3 — `verify` (after the tag push)

1. Find the run: `gh run list --repo nota-daw/nota --workflow release.yml --limit 3`. Wait
   for it with `gh run watch <id> --repo nota-daw/nota --exit-status` (run it in the
   background, because a build takes about 20–30 min). Don't poll in a loop.
2. If a job failed, show its log tail: `gh run view <id> --log-failed`.
3. When it's green, run `scripts/release-verify.sh X.Y.Z`. It checks that the draft body
   equals `release-notes.sh X.Y.Z github` word for word and that all six files are
   attached (dmg arm64/x86_64, exe x64/arm64, AppImage x86_64/aarch64).
4. Report the result. If the body differs (someone edited the draft, or the notes changed
   after the tag), show the diff and fix it at the source. Don't edit the draft by hand to
   make it match. The user reviews the draft and publishes it themselves, then posts
   `../media/news/X.Y.Z.{en,ru}.md` with `X.Y.Z.png` to the channels.

---

## Don't

- Don't commit, push, open PRs, push tags or publish the release. All of these are the
  user's steps.
- Don't reword or summarise inside `CHANGELOG.md`. Curated text goes only to
  `release-notes/`.
- Don't write published text anywhere except `release-notes/`. GitHub and the channels
  must come from the same files.
- Don't bump only one of app or engine, and don't reset a component to 0.
- Don't hard-code the date or reuse an existing version or tag.
