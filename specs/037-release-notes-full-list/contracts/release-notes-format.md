# Contract: release notes format and release scripts

## 1. Notes file (example, EN)

```markdown
<!-- SPDX-License-Identifier: AGPL-3.0-only -->
Nota 0.57 is about editing time. Copy and cut a time range across tracks, and the clip
editors move the playhead and show arrangement bars.

**1. Time ranges you can copy and cut**
A short paragraph, as today.

**2. Clip editors that follow the arrangement**
A short paragraph.

<!-- changes -->
**Added**
• Warp tempo next to the Warp switch, typeable, with ×2 and ÷2 ([994d0fb](https://github.com/nota-daw/nota/commit/994d0fb6…))

**Changed**
• Context menus open at their full width ([ca2d305](https://github.com/nota-daw/nota/commit/ca2d305e…))

**Fixed**
• Double-click resets knobs and sliders again ([92a0608](https://github.com/nota-daw/nota/commit/92a0608b…))
• Aurora's heavy presets play without dropouts ([2304e1a](https://github.com/nota-daw/nota/commit/2304e1ad…))

<!-- contributors -->
**Contributors**
[@ambertape](https://github.com/ambertape), [@rafi612](https://github.com/rafi612)
```

RU: same markers, links and order; titles «Добавлено», «Изменено», «Исправлено»,
«Удалено», «Участники».

## 2. `scripts/release-notes.sh <version> <github|post-en|post-ru>`

| target | summary | full list | contributors | extra |
|---|---|---|---|---|
| `github` | ✓ | ✓ | ✓ | title, install table (as today) |
| `post-en` / `post-ru` | ✓ | — | ✓ | title, download link (as today) |

- All `<!-- … -->` lines are removed from every output.
- Old-format files (no markers) render as today.
- `release-verify.sh` is unchanged and compares the draft with the `github` output.

## 3. `scripts/release-credits.sh <from> <to> <commits|contributors>` (new)

- `<from>` / `<to>`: git refs; the range is `<from>..<to>` (e.g. `v0.56.1 origin/develop`).
- `commits`: stdout, one line per commit, oldest first: `<short>\t<sha>\t<subject>`.
  No network access.
- `contributors`: stdout, the Contributors line (format in §1). Uses `gh api graphql`
  against `nota-daw/nota`; needs `gh` signed in. Authors without a GitHub login are
  printed by name and listed on stderr as `warn: no GitHub login for <name> <email>`.
  Exit 1 with a message if `gh` is missing or a commit is not on GitHub (unpushed).
- Never writes files.

## 4. What's New (user-visible)

- Summary: unchanged look.
- Before the first `Changes` section: a 1 px divider and an "All changes" heading in the features' title style; each
  group: `SectionLabel` title + bullets; commit links in accent colour.
- Contributors: `SectionLabel` title + wrapped line of links.
- Any link: hand cursor and underline on hover; click opens it in the default browser.
- Old notes and changelog fallbacks: unchanged (links become clickable).
