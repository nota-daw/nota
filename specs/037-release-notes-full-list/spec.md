# Feature Specification: Release notes with a full change list, commit links and contributors

**Feature Branch**: `feat/release-notes-full-list`

**Created**: 2026-10-22

**Status**: Draft (clarified 2026-10-22)

**Input**: User description: "Update the release skill so the release texts have this structure: first, as before, an introduction of the release listing the main work done (a summary); then the full list of all work done — Added, Changed, Fixed; each item ends with a link to its commit; at the bottom, the contributors who took part in the release, linked to them. The What's New window must support this layout."

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

What exists today:

- **Release skill** (`.claude/skills/release/SKILL.md` § P4) writes
  `release-notes/X.Y.Z.en.md` and `X.Y.Z.ru.md`: a lede, numbered bold feature sections
  (`**1. Title**` + a paragraph) and a final `**Also**` block of `•` bullets. No headings
  (`#`), so the text pastes into Telegram as is. The full change history lives only in
  `CHANGELOG.md`, which is never shown for a version that has curated notes.
- **Rendering** (`scripts/release-notes.sh <version> github|post-en|post-ru`): the one
  source of the published text — the GitHub release body (used by `release.yml`, checked
  word for word by `scripts/release-verify.sh`) and the EN / RU news posts.
- **What's New** (`src/managed/Nota.App/WhatsNewWindow.cs`, 460 px fixed, non-resizable):
  for each unseen version shows the curated EN notes (bundled as
  `Assets/ReleaseNotes/X.Y.Z.en.md`, parsed by `ReleaseNotesParser` in `Changelog.cs`) or
  falls back to the version's `CHANGELOG.md` section. Inline Markdown supports bold,
  italic, code and links — but a link shows only its text in accent colour and is not
  clickable.
- **Smoke test** (`tests/Nota.SmokeTest/ReleaseNotesTests.cs`) compiles `Changelog.cs`
  directly, checks the parser against a sample and parses every shipped
  `release-notes/*.en.md`.
- **History**: the repo squash-merges PRs into `develop`, so one commit ≈ one PR; commit
  subjects follow `(feat|fix|chore|ci) Subject (#NNN)`. Authors include outside
  contributors (e.g. GitHub `noreply` addresses), so a commit author maps to a GitHub
  account.

No engine change.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - The release text has a summary, the full list and the people behind it (Priority: P1)

The maintainer runs `/release prepare`. Next to the familiar summary (lede + numbered
headline features), the curated notes now carry the complete list of what the release
changed, grouped as **Added**, **Changed** and **Fixed**, each item ending with a link to
the commit that brought it, and a closing **Contributors** line naming everyone who took
part, each linked to their GitHub profile. The GitHub release body shows exactly this.

**Why this priority**: it is the request itself — users and contributors see everything
that shipped and who made it, and the commit links make each line traceable.

**Independent Test**: prepare notes for a version, render the GitHub target, and check
the four parts appear in order, every full-list item ends in a commit link that opens on
GitHub, and every contributor links to their profile.

**Acceptance Scenarios**:

1. **Given** a release range with feature, fix and chore commits from two authors, **When**
   the skill writes the notes, **Then** they contain the summary, then Added / Changed /
   Fixed groups (empty groups omitted), then Contributors listing both authors.
2. **Given** a user-visible change, **When** it appears in the full list, **Then** its line
   ends with a link to the commit on `github.com/nota-daw/nota` showing the short hash.
3. **Given** a purely internal commit (CI, refactor, tests), **When** the full list is
   written, **Then** it is left out of the list, but its author still counts as a
   contributor.
4. **Given** the RU notes, **When** they are written, **Then** they have the same structure
   and links, with group titles and items written natively in Russian.

---

### User Story 2 - What's New shows the new structure (Priority: P1)

After updating, a user opens What's New. They read the summary first, then the full
Added / Changed / Fixed list with each item's commit link, then the contributors. Links
open in the browser.

**Why this priority**: the user explicitly asked for What's New support; without it the
bundled notes would display wrongly in the app.

**Independent Test**: bundle a notes file in the new format and open What's New: the
summary, the three groups and the contributors render in order, readable in the 460 px
window, and clicking a commit or contributor link opens it in the browser.

**Acceptance Scenarios**:

1. **Given** new-format notes, **When** What's New opens, **Then** the summary sections
   show as today, followed by Added / Changed / Fixed group headings with bullets, followed
   by the contributors.
2. **Given** a bullet ending in a commit link, **When** the user clicks the link, **Then**
   the commit page opens in the default browser.
3. **Given** a long full list, **When** What's New opens, **Then** the full list is shown
   expanded, in order after the summary (no folding); the window scrolls.

---

### User Story 3 - Older notes and news posts keep working (Priority: P2)

Notes written before this change (summary + **Also**) still display correctly in What's
New, and the release tooling keeps producing valid GitHub and news-channel texts.

**Why this priority**: shipped versions bundle old-format notes; a regression there shows
broken text to every user who upgrades across several versions.

**Independent Test**: the smoke test parses every shipped `release-notes/*.en.md`
(old format) and a new-format sample; What's New shows a mix of both versions correctly.

**Acceptance Scenarios**:

1. **Given** a 0.56.1-style file, **When** parsed, **Then** it yields the same lede and
   sections as before this change.
2. **Given** a news post render (`post-en` / `post-ru`), **When** produced, **Then** its
   it carries the summary and the contributors line but not the full list; the release
   link at the bottom leads to the GitHub release with the full list.

### Edge Cases

- A change landed in several commits (a fix and its follow-up): one item, links to each
  commit separated by commas.
- A commit with several authors (`Co-authored-by:` trailers): every co-author is a
  contributor.
- An author whose commit email doesn't map to a GitHub account: listed by name without a
  link, and the skill flags it to the maintainer.
- Bots (e.g. dependabot) are not listed as contributors.
- A patch release with only fixes: summary + **Fixed** + **Contributors**.
- A release with no headline feature: lede alone, then the full list.
- The **Also** block is replaced by the full list in new notes (no double listing).
- Group titles in RU notes (e.g. «Добавлено», «Изменено», «Исправлено», «Участники») — the
  app bundles only EN, so What's New must recognise the EN titles; RU titles only need to
  render on GitHub/Telegram.

## Clarifications

### Session 2026-10-22

- Q: How does What's New show the full list? → A: Expanded in full after the summary, no
  folding.
- Q: What goes into the news-channel posts? → A: Summary + contributors only; the full
  list stays on GitHub (and in What's New).

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The release skill MUST define the curated notes format as four parts in
  order: (1) summary — lede plus numbered headline sections, as today; (2) full list in
  groups **Added**, **Changed**, **Fixed** (and **Removed** when something was removed),
  empty groups omitted; (3) each full-list item ending with a link to its commit(s);
  (4) **Contributors**.
- **FR-002**: The full list MUST cover every user-facing change in the release range,
  reconciled with the code as P4 already requires (merged duplicates, nothing added and
  removed within the release), and MUST NOT include internal-only commits.
- **FR-003**: A commit link MUST point to `https://github.com/nota-daw/nota/commit/<sha>`
  and show the short hash as its text; the sha MUST be the commit as it exists on
  `develop` (the squash commit), so the link resolves after release.
- **FR-004**: Contributors MUST be every distinct human author and co-author of a commit
  in the release range (including internal commits), each linked to
  `https://github.com/<login>`, shown by GitHub login.
- **FR-005**: The skill MUST give the maintainer a way to gather items, commits and
  contributors for the range (commands to run), and MUST say how to resolve an author to a
  GitHub login.
- **FR-006**: The format MUST stay Telegram-pasteable: no `#` headings; group titles use
  the same bold-line convention as today.
- **FR-007**: What's New MUST render new-format notes as: summary, then each group with
  its title and bullets, then the contributors, in the Studio style.
- **FR-008**: Links in What's New (commit links, contributor links and any other link in
  notes) MUST be clickable and open in the default browser.
- **FR-009**: What's New MUST keep rendering old-format notes and changelog fallbacks
  exactly as before.
- **FR-010**: `scripts/release-notes.sh` MUST render the GitHub target with all four parts;
  `release-verify.sh` keeps comparing the draft with it. The news-post targets
  (`post-en`, `post-ru`) MUST carry the summary and the contributors but leave out the
  full list (Telegram's 4096-character limit), pointing to the GitHub release for it.
- **FR-011**: The smoke test MUST cover parsing of the new format (groups, commit-linked
  items, contributors) and keep parsing all shipped notes.

### Key Entities

- **Curated notes**: one version's text per language — summary, change groups,
  contributors.
- **Change item**: one user-facing change — text plus one or more commit links.
- **Change group**: Added / Changed / Fixed / Removed with its items.
- **Contributor**: a GitHub login (name and profile link) of a person with a commit in the
  release range.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: 100 % of user-facing commits in a release range appear as items in the full
  list, and 100 % of the items carry a commit link that opens a real commit page.
- **SC-002**: Every human author in the release range appears once in Contributors.
- **SC-003**: All shipped release notes (old format) still parse and display identically
  in What's New.
- **SC-004**: A user opening What's New sees the release summary first, the full list
  right after it (expanded), and can reach any commit from the window in one click.
- **SC-005**: The published GitHub release body matches the curated notes word for word
  (release-verify passes).

## Assumptions

- Commit links use squash commits on `develop`; history is not rewritten after a release
  so the links stay valid (`main` takes merge commits, keeping the same shas).
- Contributor logins come from the GitHub API for each commit in the range (the `gh` CLI
  is already used by the release flow); a commit's `noreply` email also yields the login.
- The full list is curated by the skill (one item per user-facing change), not dumped
  from commit subjects; CHANGELOG.md stays the raw log and is not rewritten.
- Existing release notes (0.5x) are not rewritten into the new format.
- What's New stays 460 px fixed width; links are rendered as accent text as today, now
  clickable.
- FEATURES.md / user docs are not affected beyond a CHANGELOG entry for the What's New
  change.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — n/a, UI and tooling only
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/` — no crossing
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` — parser tests extended (FR-011)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
