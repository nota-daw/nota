# Data Model: Release notes with a full change list, commit links and contributors

## Curated notes file (`release-notes/X.Y.Z.{en,ru}.md`)

| Part | Starts at | Content | Required |
|---|---|---|---|
| Summary | file start (after SPDX) | lede paragraph; `**N. Title**` + paragraph sections | lede yes, sections optional |
| Full list | `<!-- changes -->` | `**Group**` lines, each followed by `•` items | yes for new notes |
| Contributors | `<!-- contributors -->` | `**Title**` line + one line of links/names | yes for new notes |

Rules:

- Markers appear at most once each, in this order; contributors never precede changes.
- Group order: Added, Changed, Fixed, Removed; empty groups omitted. RU titles:
  Добавлено, Изменено, Исправлено, Удалено; contributors title: Contributors / Участники.
- No `**Also**` in new notes (the full list replaces it). Old notes: no markers, may have
  `**Also**`.
- EN and RU files have the same groups, items in the same order, the same commit links and
  the same contributors.

## Change item

`• <text> (<commit link>[, <commit link>…])`

- `<commit link>` = `[<7-char sha>](https://github.com/nota-daw/nota/commit/<40-char sha>)`.
- The sha is a commit in the release range on `develop`.
- One item per user-facing change; a change spread over several commits lists each.

## Contributor

- Source: GitHub `Commit.authors` for every commit in the range (internal ones included).
- Identity: GitHub login; rendered `[@login](https://github.com/login)`.
- Excluded: logins ending in `[bot]`, AI co-authors (`noreply@anthropic.com`, login
  `claude`).
- Author without a login: plain name, flagged to the maintainer (may be a second email of
  someone already listed — then drop it).
- Order: most commits first, ties by login.

## App model (`Changelog.cs`)

```text
ChangelogEntry(Version, Date, Sections, Lede)          # unchanged
ChangelogSection(Title, Items, Text, Kind = Feature)   # + Kind
SectionKind { Feature, Changes, Contributors }
```

Parser state transitions: start in `Feature`; `<!-- changes -->` → following sections are
`Changes`; `<!-- contributors -->` → following sections are `Contributors`. A
`Contributors` section's line is its `Text` (not a bullet). Changelog-fallback entries keep
`Feature` (the window distinguishes them by `IsReleaseNotes`, as today).

## Inline link (`WhatsNewWindow`)

While building inlines, each `[label](url)` records `(start, length, url)` in the block's
character space; a click whose hit-tested position falls inside a range opens `url`
(http/https only).
