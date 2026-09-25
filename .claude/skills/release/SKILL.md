---
name: release
description: Cut a Nota release — actualize CHANGELOG.md (promote [Unreleased] to a dated version and add a short human-readable summary of it), bump VERSION and the engine's src/native/nota.engine/VERSION, create the vX.Y.Z tag, then commit and push. Run this when the user asks to release, ship, cut a version, or tag a release. The GitHub Actions release workflow then builds every platform and drafts the release from the changelog section.
user-invocable: true
---

# Cutting a Nota release

A release is a deliberate act: `## [Unreleased]` in `CHANGELOG.md` is promoted to a dated
version, `VERSION` is bumped (and the native engine's own
`src/native/nota.engine/VERSION`), a `vX.Y.Z` tag is created, and pushing the tag triggers
`.github/workflows/release.yml` — which builds every target (x64 + arm64 across macOS,
Windows, Linux) and drafts the GitHub release using the version's changelog section as the
body.

Because the CI pulls that whole section verbatim into the release notes, and because
`[Unreleased]` can accumulate arbitrarily many, arbitrarily verbose entries, **this skill's
job is to lead that section with a short, human-readable summary** — so the release notes
open with something a person actually wants to read, with the full detail below.

**Argument** (`/release <arg>`, optional): the bump — `patch`, `minor` (default), `major`,
or an explicit `X.Y.Z`. Nota has historically bumped **minor** per release.

## 0. Preflight — stop if any of these fail

1. **On `main`.** `git rev-parse --abbrev-ref HEAD` must be `main`. Releases build only
   from main (the CI guard enforces it too). If not, stop and tell the user.
2. **Clean working tree.** `git status --porcelain` must be empty, so the release commit
   contains *only* the VERSION + engine VERSION + CHANGELOG changes. If dirty, stop and ask the user to
   commit or stash first.
3. **Up to date with origin.** `git fetch origin` then confirm local `main` is not behind
   `origin/main` (`git rev-list --left-right --count origin/main...HEAD`). If behind, stop.
4. **There is something to release.** `## [Unreleased]` must have real content (not just
   empty category headers). If empty, stop — nothing to ship.

## 1. Decide the new version

- Read the current version from `VERSION` (e.g. `0.37.0`).
- Apply the argument: `patch` → `x.y.(z+1)`, `minor` → `x.(y+1).0` (default), `major` →
  `(x+1).0.0`, or use the explicit `X.Y.Z` if one was given.
- Sanity-check it's strictly greater than the current version, and that neither the git tag
  `vX.Y.Z` nor a `## [X.Y.Z]` changelog section already exists. If either exists, stop.
- Get today's date: `date +%F` (do NOT hard-code it).

## 2. Actualize CHANGELOG.md

Read the entire `## [Unreleased]` block (everything from `## [Unreleased]` up to the next
`## [` heading), then rewrite the top of the file so it looks like:

```
## [Unreleased]

## [X.Y.Z] — YYYY-MM-DD

### Highlights
- <3–6 bullets summarising the release in plain language>

### Added
- <the existing detailed entries, kept as-is>
### Changed
- …
### Fixed
- …
### Removed
- …
```

Rules:
- **Write the Highlights yourself** — this is the whole point. Read every `[Unreleased]`
  entry and distil them into 3–6 short, plain-language bullets a musician (not a compiler)
  would understand: group related changes, lead with the biggest user-facing wins, drop the
  internal/mechanical noise. One line each, no jargon, present tense. If the release is
  dominated by one headline feature, say so in the first bullet.
- **Keep the full detail.** The existing category entries stay under Highlights unchanged —
  the changelog is the permanent record; Highlights is just the readable lede.
- Leave a fresh **empty** `## [Unreleased]` section at the very top for future work.
- Preserve the file's header/preamble and every older version section exactly.
- Keep the categories in the order `Added, Changed, Fixed, Removed`; omit any that are empty.

Show the user the promoted section (Highlights + headings) before continuing.

## 3. Bump VERSION

Write the new `X.Y.Z` to `VERSION` (no trailing newline changes beyond what's already
there; it's read with `.Trim()`). This is the single source of truth the CI checks the tag
against — it MUST equal the tag version.

## 3b. Bump the engine version

The native engine (`src/native/nota.engine`) has its own semver in
`src/native/nota.engine/VERSION`, independent of the app version. CMake reads it into
`project(... VERSION)` and bakes it into `nota_engine_version()` (shown in the About
window next to the app version). It moves only when the engine itself changed:

- Find the previous release tag: `git describe --tags --abbrev=0 --match 'v*'`.
- Check for engine changes since then:
  `git diff --quiet <prev-tag> HEAD -- src/native/nota.engine` (exit 1 = changed).
- **Changed** → bump the engine's **patch** (`a.b.c` → `a.b.(c+1)`). If the user asked
  for an explicit engine bump (e.g. "engine minor", or an explicit engine `X.Y.Z`), use
  that instead — minor for a C ABI change (new/changed `nota_*` exports), major for a
  breaking one.
- **Unchanged** → leave it as-is.

Tell the user the old → new engine version (or that it's unchanged).

## 4. Commit, tag, push

```bash
git add VERSION CHANGELOG.md src/native/nota.engine/VERSION
git commit -m "Release vX.Y.Z"
git tag -a "vX.Y.Z" -m "Nota vX.Y.Z"
git push origin main
git push origin "vX.Y.Z"
```

Notes:
- The commit and tag must sit **on main** — the CI's guard job rejects a tag whose commit
  isn't contained in `main`.
- Push the branch **before** the tag, so the tagged commit is already on the remote `main`
  when the guard runs.
- End the commit message body with the standard `Co-Authored-By` trailer if your harness
  requires it.

## 5. Report

Tell the user:
- the released version and tag,
- the engine version (bumped `a.b.c → a.b.d`, or unchanged),
- the Highlights you wrote,
- that the tag push kicked off `.github/workflows/release.yml`, which will produce the 5
  platform artifacts and a **draft** GitHub release (they review + publish manually),
- the release/actions URL to watch (derive from `git remote get-url origin`).

## Don't
- Don't release from a branch other than `main`, or with a dirty tree.
- Don't hard-code the date or reuse an existing version/tag.
- Don't discard the detailed changelog entries — summarise *in addition to* them, not instead.
- Don't publish the GitHub release from here; the workflow drafts it for human review.
