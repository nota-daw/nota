<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# Contributing to Nota

Thanks for wanting to help. This document covers the things that are specific to this
project — the licensing agreement, the architecture rules you must not break, and how to
get a change reviewed.

Start with [`ARCHITECTURE.md`](ARCHITECTURE.md). Nota is a real-time audio application,
and a change that looks harmless in most codebases (allocating in a callback, taking a
lock, logging from the wrong thread) will produce audible dropouts here.

## Start with an approved issue

**Every change needs an issue that a core contributor has approved — before you write
code, a spec, or a prompt for an agent.** This includes typos and one-line fixes.
Pull requests without one are closed unreviewed, however good the work is.

1. Find an existing issue or [open one](https://github.com/nota-daw/nota/issues/new/choose):
   what you want to change and why. For a bug, the report itself is enough.
2. Wait for a core contributor (see [`.github/CODEOWNERS`](.github/CODEOWNERS)) to add
   the **`approved`** label. They may ask questions, narrow the scope or decline —
   that is the point: it costs a comment, not a weekend of your time and tokens.
3. Comment that you are taking it, so two people don't build the same thing.
4. Build exactly what the issue agreed. If the scope needs to change along the way, say
   so in the issue first.
5. Link it from the pull request: `Closes #N` (or `Refs #N`).

Issues labelled `good first issue` or `help wanted` are already approved. Release pull
requests cut by core contributors are the one exception.

## Licensing your contribution

Nota is licensed under `AGPL-3.0-only` (see [`LICENSES/`](LICENSES/)).

> By submitting a contribution, you agree that your work is licensed under
> **AGPL-3.0-only**.

If you are not comfortable with that, please open an issue to discuss instead of sending
code.

**If you are employed as a programmer**, check your employment agreement before
contributing. Many contracts assign copyright in your code to your employer, in which
case they — not you — must grant the license above.

Every new source file needs the standard two-line header:

```c
// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
```

## Adding a dependency

Don't add one without checking it first. Every dependency must be AGPL-compatible.

1. Confirm the license qualifies.
2. Add a row to [`LICENSES/third-party.md`](LICENSES/third-party.md).
3. Say so in the pull request.

A copyleft dependency that is not already accounted for will be rejected — the JUCE
situation (which is why this project is AGPL rather than GPL) is the one exception, and
it took a deliberate architectural decision to contain it.

## Rules you must not break

These are described in full in [`ARCHITECTURE.md`](ARCHITECTURE.md#architecture-rules-ar-).
The short version:

- **The audio thread never allocates, locks, or does I/O** (AR-6). No `new`, no `malloc`,
  no `std::string`, no mutex, no file access, no logging. If you need to get data to the
  audio thread, use the lock-free command queue (AR-4).
- **Structural edits publish a new immutable graph snapshot** (AR-5). Do not mutate a
  live graph in place.
- **Only a pure C ABI crosses the managed/native boundary** (AR-7). No C++ types, no
  exceptions. Extend `nota_engine.h` rather than working around it.
- **No JUCE outside `pluginhost/`.** The core engine's JUCE-freedom is both an
  architectural and a licensing constraint.
- **`Transport` is the only source of musical time** (AR-9).

## Spec-driven workflow

For anything non-trivial — a feature, a behaviour change, a hard bug — don't start with
code. Start with a spec. This repository runs [Spec Kit](https://github.com/github/spec-kit),
and the AI agents it supports (Claude Code, Copilot, Cursor, Gemini, Codex) already have
the commands installed. The workflow:

1. `/speckit-specify` — write `spec.md`: what and why, user stories, requirements.
2. `/speckit-clarify` *(optional)* — tighten underspecified areas.
3. `/speckit-plan` — write `plan.md`: technical approach, audio-thread impact.
4. `/speckit-tasks` — break it into dependency-ordered `tasks.md`.
5. `/speckit-implement` — execute the tasks.
6. `/speckit-converge` — verify nothing was missed; repeat until it reports
   **Converged**.

Artifacts live in `specs/<slug>/` and are committed alongside the code. A completed
example is kept at [`specs/000-example/`](specs/000-example/) — read it before your
first run. Bug reports can take the analogous `/speckit-bug-assess` → `/speckit-bug-fix`
→ `/speckit-bug-test` path; the report directory lands in `.specify/bugs/<slug>/`.

Two things govern agents working here: the project constitution at
`.specify/memory/constitution.md` (principles distilled from this document and
[`ARCHITECTURE.md`](ARCHITECTURE.md)), and this document — which remains canonical for
humans. You don't need an AI agent to contribute: writing `spec.md` and `plan.md` by
hand from the templates in `.specify/templates/` is equally welcome.

The project skills (`nota-design`, `nota-docs`, `nota-features`, `nota-instrument`,
`nota-pr`, `nota-shortcuts`, `release`) are written once in `.claude/skills/` and copied
into the directories the other agents read (`.agents/skills/`, `.github/skills/`,
`.cursor/skills/`, `.gemini/skills/`). Edit the source, then run
`scripts/sync-agent-skills.sh`; CI fails if a copy is out of date. Typos, small fixes
and doc edits don't need a spec — but they still need an approved issue (see
[Start with an approved issue](#start-with-an-approved-issue)).

**If you work with an AI agent**, the agent instructions in [`AGENTS.md`](AGENTS.md) and
[`CLAUDE.md`](CLAUDE.md) tell it to check for an approved issue before it does anything.
Give it the issue number up front; don't override that check.

## Translating Nota

The desktop app's interface is translated through one mechanism (spec
[068](specs/068-ui-language/)): the English text in the code is the key, and each language is one
JSON file, `src/managed/Nota.App/Languages/<code>.json`, embedded in the app. English needs no
file of strings; French is `fr.json`.

- **Every new user-visible text goes through `Loc`.** Write `Loc.T("Save")` for a text,
  `Loc.F("Delete {0} clips?", n)` for a text with values (the whole sentence is the key, never
  a piece of it) and `Loc.Plural(n, "{0} clip", "{0} clips")` for a counted text; in XAML write
  `{app:T 'Save'}`. The smoke test fails on a new plain literal that reads as interface text, and
  on a `Loc` text that French does not cover. Text that is not translated on purpose — user
  content, device and parameter names, proper nouns, units, logs, the Remote web page and MCP
  — is listed with its reason as `# data:` in `tests/Nota.SmokeTest/LocalizationAllowList.txt`.
- **Add a language**: copy `fr.json` to `<code>.json` (two lowercase letters), set `code` and
  `name` (the language's own name, shown in Preferences → Appearance → Language), translate
  the strings you can and delete the rest — anything missing shows in English. Plural rules
  live in `UiLanguage.Plural`; a language without a rule counts like English. Also list the code in
  `CFBundleLocalizations` of `scripts/bundle-mac.sh` and `scripts/package-dmg.sh` (the smoke test checks it), so
  macOS shows its own text — Hide, Quit, the open panels — in that language too. The file format
  is in [specs/068-ui-language/contracts/translation-file.md](specs/068-ui-language/contracts/translation-file.md).
- **Translate or fix a string**: edit its value in the language file. Keep the placeholders
  (`{0}`, `{1}`) and give a counted text both forms (`one`, `other`; French counts 0 and 1 as
  one). Follow the glossary in [specs/068-ui-language/glossary.md](specs/068-ui-language/glossary.md)
  (infinitive for actions, sentence case, a narrow no-break space before `:` `;` `?` `!`).
  Device and parameter names stay in English.
- **Look at it**: run `NOTA_UI_LANGUAGE=fr` to start in a language for that run without
  touching the saved setting, and check that nothing is clipped or overlaps at each interface
  scale. `NOTA_LOC_WRITE_BASELINE=1` rewrites the allow-list after you wrap or add text.

## Building

See [`README.md`](README.md) for prerequisites and per-platform build commands, and
[`BUILD.md`](BUILD.md) for details. In short:

```bash
scripts/build.sh          # macOS   — native + managed + smoke test
pwsh scripts/build-win.ps1  # Windows (Developer PowerShell for VS 2022)
scripts/build-linux.sh    # Linux
dotnet run --project src/managed/Nota.App
```

The build scripts run the smoke test (`tests/Nota.SmokeTest`), which drives the engine
end-to-end through the C ABI. **It must pass before you open a pull request.**

## Branches

- **`develop`** is where work lands. Branch from it and open your pull request against
  it.
- **`main`** holds released code only. It moves forward when a release is cut from
  `develop`; pull requests straight into `main` are not accepted.

Name your branch after the change — `feat/stereo-pan-law`, `fix/clip-fade-click` — and
keep it up to date by rebasing on, or merging in, the latest `develop`. Pull requests are
squash-merged, so the pull request title becomes the commit message on `develop`: write
it in the same form as the existing history, `(feat) …`, `(fix) …`, `(docs) …`,
`(chore) …`.

## Continuous integration

Every pull request is built on macOS, Windows and Linux, and the smoke test runs on each
(`.github/workflows/ci.yml`). All three must pass before a pull request can be merged.
If this is your first contribution, a maintainer approves the CI run before it starts.

A pull request that touches the engine also runs the performance gate
(`.github/workflows/perf.yml`, see [BUILD.md](BUILD.md#performance-gate)). It fails when
any engine scenario does more than 1% more work (CPU instructions) or takes more than
15% longer than on the base branch, or when an idle engine uses CPU. If a slowdown is the price of a deliberate change (a better-sounding
default, say), say why in the PR; a maintainer accepts it by adding the
`perf-regression-accepted` label, and the report still lists it.

## Pull requests

- Target `develop`.
- Link the approved issue it implements (`Closes #N`).
- One logical change per pull request.
- Match the style of the code around you. Comment headers explain *why* a file exists,
  not what each line does — keep that convention.
- If you change engine behaviour, extend the smoke test to cover it.
- If you change user-visible behaviour, add a `CHANGELOG.md` entry.
- Say which platforms you actually built and tested on.
- For a visible change, show it: add before/after screenshots to the description (see
  below).

### Screenshots and other images

Images for pull request descriptions and issues live on the **`pr-assets`** branch. It
is an orphan branch with no code that is never merged, so images don't bloat `develop`
and stay available after a branch is deleted. Use that one branch rather than creating a
new branch per pull request.

- Put each pull request's images in their own folder, named after the change or the
  issue, for example `fold/` or `issue-137/`. Images attached to issues from outside
  GitHub go under `issues/`.
- Add images by committing on top of `pr-assets`. Never force-push it or rewrite its
  history, because existing descriptions link to its files.
- Embed with the raw URL:
  `![After](https://raw.githubusercontent.com/nota-daw/nota/pr-assets/<folder>/<file>.png)`

```sh
git fetch origin pr-assets
git worktree add ../nota-assets pr-assets
mkdir -p ../nota-assets/my-change && cp after.png ../nota-assets/my-change/
git -C ../nota-assets add my-change && git -C ../nota-assets commit -m "Images for my change"
git -C ../nota-assets push origin HEAD:pr-assets
```

A user-visible UI change carries before/after screenshots there; a performance change
carries a before/after benchmark — `scripts/bench-engine.sh` measures engine builds through
the C ABI and `scripts/bench-chart.py` turns the results into charts and a table. The
smoke test's render-pool guards (`RenderPoolTests`, `-- --perf` alone) catch the
regressions automatically; a performance fix adds a guard there.

You can also drag images straight into the GitHub description box when you write it by
hand. The branch is for images added by scripts or tools, and for anything you want to
keep alongside the repository.

## Reporting bugs

Include your OS and version, the Nota version (Help -> About), your audio device and
buffer size, and — for audio problems — whether it reproduces with plugins disabled.
Crash and dropout reports are much more useful with a project that reproduces them.
