# Implementation Plan: Release notes with a full change list, commit links and contributors

**Branch**: `feat/release-notes-full-list` | **Date**: 2026-10-22 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/037-release-notes-full-list/spec.md`

## Summary

Curated release notes get two more parts after the familiar summary: a full Added /
Changed / Fixed (/ Removed) list whose items end in commit links, and a Contributors line
linking every human author and co-author to their GitHub profile. Two HTML-comment
markers (`<!-- changes -->`, `<!-- contributors -->`) delimit the parts, so the format
works in any language and old files parse unchanged. A new `scripts/release-credits.sh`
lists the range's commits and builds the Contributors line from GitHub's GraphQL
`Commit.authors`. `release-notes.sh` keeps all parts for GitHub and drops the full list
from the news posts. What's New learns the section kinds, renders the full list
expanded under an "All changes" caption, and makes every link clickable. The release skill
§ P4 documents the new format and the gathering commands, and fixes its previous-tag
lookup. Details: [research.md](research.md).

## Nota: Read Before Planning

- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio) and the `nota-design` skill — the
  "All changes" divider and caption, link hover state.
- **Build / packaging** → [`BUILD.md`](../../BUILD.md) — the release scripts.
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md).

## Technical Context

**Language/Version**: C# / .NET 10 (`Nota.App`), Bash (release scripts), Markdown (skill)

**Primary Dependencies**: Avalonia 12.1.4 (existing); `gh` CLI and `jq` (already used by
`release-verify.sh`); no new dependency

**Storage**: N/A — `release-notes/*.md` files; the app bundles `*.en.md` as today

**Audio-Thread Impact**: None — What's New window and release tooling only.

**Testing**: smoke test `--release-notes` section extended (new-format sample: kinds,
commit-linked items, contributors; link ranges of the inline parser if split out as pure
code); every shipped notes file still parses. Scripts checked by running them on the
`v0.56.1..develop` range. Window checked with the headless Avalonia harness / real app per
[quickstart.md](quickstart.md).

**Target Platform**: macOS, Windows, Linux (desktop app); the release scripts run on the
maintainer's macOS and in `release.yml` (Ubuntu)

**Project Type**: desktop app + release tooling

**Performance Goals**: N/A (a release has tens of commits; one GraphQL call per commit is
fine; may batch with aliases)

**Constraints**: notes stay Telegram-pasteable (no `#` headings); `release-verify.sh`
equality with the GitHub draft unchanged; old notes render identically

**Scale/Scope**: 2 C# files + 1 smoke test file, 2 scripts (1 new), the release skill, a
CHANGELOG entry

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status |
|---|---|
| I. Real-time audio safety | Pass — no audio code |
| II. Boundary discipline | Pass — no C ABI involvement |
| III. Licensing hygiene | Pass — new script gets the SPDX header; no dependency |
| IV. Verification gate | Pass — `scripts/build.sh`; release-notes smoke section extended |
| V. One logical change per PR | Pass — one change: the release-notes format end to end (writer, renderer, reader) |
| VI. Docs in sync | Planned — CHANGELOG `[Unreleased]` (Changed: What's New shows all changes, contributors, clickable links); FEATURES.md via nota-features if it lists What's New; no shortcut change |
| VII. Spec first | Pass — spec, clarifications, this plan |
| VIII. Rules not weakened | Pass |

Post-design re-check: unchanged, all gates pass.

## Project Structure

### Documentation (this feature)

```text
specs/037-release-notes-full-list/
├── plan.md              # this file
├── research.md          # R1–R7
├── data-model.md        # notes parts, ChangelogSection.Kind, contributor rules
├── quickstart.md        # scripts on a real range, smoke section, What's New check
├── contracts/
│   └── release-notes-format.md   # the file format + script interfaces
├── checklists/
│   └── requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
.claude/skills/release/SKILL.md      # P4: four-part format, markers, gathering commands,
                                     # previous tag by version (R3); P8/verify unchanged
scripts/
├── release-credits.sh               # NEW — commits | contributors for a range (R4)
└── release-notes.sh                 # strip all comments; posts drop the full list (R5)
src/managed/Nota.App/
├── Changelog.cs                     # SectionKind + ReleaseNotesParser markers (R1, R7)
└── WhatsNewWindow.cs                # kinds rendering, "All changes", clickable links (R6, R7)
tests/Nota.SmokeTest/
└── ReleaseNotesTests.cs             # new-format sample, old sample unchanged
CHANGELOG.md                         # [Unreleased] entry
```

**Structure Decision**: everything stays where the format already lives — parser in
`Changelog.cs` (Avalonia-free, linked into the smoke test), rendering in
`WhatsNewWindow.cs`, release tooling in `scripts/` next to `release-notes.sh`. No new
project or folder.

## Complexity Tracking

None.
