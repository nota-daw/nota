# Research: Release notes with a full change list, commit links and contributors

## R1. Marking the parts of the notes

**Decision**: Two HTML-comment markers split the notes file into its parts, in this order:

```
<!-- SPDX-License-Identifier: AGPL-3.0-only -->
lede + **N. Title** sections            ← summary (unchanged format)
<!-- changes -->
**Added** / **Changed** / **Fixed** / **Removed** + • bullets   ← full list
<!-- contributors -->
**Contributors** + one line of links    ← contributors
```

Group titles are free text in the file's language («**Добавлено**» in RU); the markers, not
the titles, say what a part is. Old files have no markers and parse as today.

**Rationale**: language-independent — `release-notes.sh` must cut the full list out of the
RU post as well as the EN one, and the app parser only sees EN. Comments are invisible on
GitHub and in Telegram once stripped, and the parser already skips `<!--` lines.

**Alternatives considered**: recognising group titles by name (needs an EN + RU word list
in two places, breaks on a reworded title); a `---` rule (renders as a visible line on
GitHub, and the github target already uses `---` before the install table).

## R2. Item and contributor syntax

**Decision**:

- Item: `• Text of the change ([a1b2c3d](https://github.com/nota-daw/nota/commit/<full sha>))`;
  several commits: `([a1b2c3d](…), [e4f5a6b](…))`. Link text = 7-char short hash, URL =
  full sha.
- Contributors: one line, `[@login](https://github.com/login)` joined with `, `. An author
  without a GitHub account: plain name, no link.

**Rationale**: the existing inline-Markdown renderer already handles `[text](url)`; GitHub
and Telegram render the same text. One line keeps the post short.

**Alternatives considered**: PR links (`#281`) — the user asked for commits; one bullet per
contributor — long for 5+ people.

## R3. The release range

**Decision**: previous tag = highest `v*` tag by version
(`git tag --list 'v*' --sort=-v:refname | head -1`), range `<prev-tag>..origin/develop`.

**Rationale**: release tags sit on `main`'s merge commits, which `develop` never contains,
so `git describe` on `develop` returns a stale tag (today: `v0.50.1` instead of
`v0.56.1`). `git log v0.56.1..develop` correctly excludes everything merged into that tag
(23 commits today). This also fixes the skill's existing P4 step 1.

The squash commits on `develop` become ancestors of `main` through the develop → main merge
commit, so their shas — and the links — stay valid after the release.

## R4. Contributors and co-authors

**Decision**: a new script `scripts/release-credits.sh <from> <to> <commits|contributors>`:

- `commits` — one line per commit in the range, `<short> <full sha> <subject>`, oldest
  first: the checklist the skill walks while writing the full list.
- `contributors` — the ready Contributors line. For every commit it asks GitHub's GraphQL
  API for `Commit.authors` (author **and** `Co-authored-by` trailers, each with
  `user.login` when the email maps to an account), deduplicates by login, drops bots
  (`[bot]` logins) and AI co-authors (`noreply@anthropic.com`, login `claude`), sorts by
  number of commits then name, and prints names without a login on stderr as a warning to
  resolve by hand.

**Rationale**: tested on `301d83c`: GraphQL returns the author with login, Claude as
co-author, and a second email of the same person with `user: null` — exactly the cases the
script must handle. REST `compare` gives only the primary author. `gh` is already required
by the release flow (`release-verify.sh`).

**Alternatives considered**: parsing `git log` emails (no logins; noreply addresses only for
some people); REST per commit + search by email (rate-limited, private emails miss).

## R5. News posts

**Decision**: `release-notes.sh post-en|post-ru` drop the lines from `<!-- changes -->` up
to `<!-- contributors -->`; the contributors part stays, then the download link. `github`
keeps everything. All targets strip every `<!-- … -->` line (today only the SPDX one).

**Rationale**: clarification 2026-10-22 (summary + contributors in posts). `release-verify.sh`
compares against the same render, so it needs no change.

## R6. Clickable links in What's New

**Decision**: keep links as accent-coloured `Run`s inside the wrapping `TextBlock`, record
each link's character range and URL while building the inlines, and on `PointerPressed`
hit-test the point (`TextBlock.TextLayout.HitTestPoint`) to find the link; open it with
`TopLevel.Launcher.LaunchUriAsync` (as `AppUpdates.OpenReleasePage` does). On
`PointerMoved` over a link the cursor turns into a hand; links get an underline on hover.

**Rationale**: text keeps wrapping naturally across the link; no new control per link;
the same path makes every link in old notes clickable too (FR-008). Only `http(s)` URLs
open.

**Alternatives considered**: `InlineUIContainer` + `HyperlinkButton` per link — a link
can't wrap mid-text and the button's padding breaks the baseline; a separate links row
under each bullet — clutters a 396 px column.

## R7. What's New layout for the new parts

**Decision**: `ChangelogSection` gains a `Kind` (`Feature`, `Changes`, `Contributors`);
`ReleaseNotesParser` sets it from the markers (sections before any marker keep today's
behaviour: `Feature`). The window renders:

- `Feature` — as today (13 px semibold title, paragraph, bullets);
- `Changes` — the first one gets a thin divider and an "All changes" heading (the features' title style) above it;
  each group uses the `SectionLabel` style (as the changelog fallback does) with bullets;
- `Contributors` — `SectionLabel` "Contributors" and the line as a wrapped paragraph.

Everything expanded (clarification 2026-10-22). Old notes and changelog fallbacks render
exactly as today.

**Rationale**: reuses the window's two existing heading styles, so the summary keeps its
"release notes" look and the full list looks like the familiar changelog list.
