---
name: nota-pr
description: Open a Nota pull request the project's way — branch off develop, commit in the repo's style under the maintainer's identity, push over HTTPS, and write the description from the PR template. Evidence is part of the PR, attached through the pr-assets branch: a user-visible UI change carries before/after screenshots; a performance change (engine, DSP, threading, render path, CPU/memory) carries a before/after benchmark with charts. Use whenever the user asks to open, create or prepare a PR, or to "push this for review".
user-invocable: true
---

# Opening a pull request

A Nota PR is reviewed by reading its description, so the description carries the proof: what
changed, why, and what it looks like or costs before and after. Images and benchmark charts
never go into `develop` — they live on the orphan **`pr-assets`** branch (see
`CONTRIBUTING.md` § Images for pull requests) and are embedded by raw URL.

## 1. Branch and commit

- Branch from `develop` (never `main`): `git checkout -b <type>/<slug>` — `fix/…`, `feat/…`,
  `docs/…`, `chore/…`. If the work is already on `develop` uncommitted, create the branch
  first; the working tree comes along.
- `scripts/build.sh` (or `build-linux.sh` / `build-win.ps1`) must pass — it runs the smoke test.
- Commit message: `(fix) Summary`, `(feat) …`, `(docs) …`, `(refactor) …`, `(style) …`; a body
  that says what and why in plain words. **No AI attribution** (no `Co-Authored-By`, no
  "Generated with" line) in commits or the PR body.
- Author and committer: `Egor Khindikaynen <khek@ambertape.ru>`:
  `git -c user.name="Egor Khindikaynen" -c user.email=khek@ambertape.ru commit …`
  (and `--author` the same, if the local identity differs).
- Spec and bug artifacts (`specs/<slug>/`, `.specify/bugs/<slug>/`) ship with the change they
  describe. Never commit build dirs, `.cpm-cache/`, benchmark output or harness binaries.

## 2. Decide what evidence the PR needs

| The change… | Attach |
|---|---|
| alters anything the user sees (a window, panel, device card, menu, theme, layout, custom-drawn view) | **before / after screenshots**, same size, theme and content |
| aims at or may affect performance (engine render path, DSP, threads, allocation, CPU, memory, startup) | **benchmark**: before vs after (and vs the last good build when fixing a regression), with charts + a table |
| both | both |
| neither (docs, refactor with no runtime effect, CI) | nothing — say so in the Summary |

When unsure whether a change is user-visible, run the app and look.

## 3. Screenshots (UI changes)

1. Capture **before** on the base commit and **after** on the branch, with the same window size,
   theme (default Studio Dark unless the change is theme-specific — then one pair per theme),
   interface scale and project content. Reuse the capture tooling the `nota-design` /
   `nota-docs` skills describe (headless Avalonia render or the app's own capture hook) rather
   than hand screenshots, so both shots are pixel-comparable.
2. Name them `<what>-before.png` / `<what>-after.png`; a side-by-side `<what>-both.png` is
   welcome for small changes.

## 4. Benchmarks (performance changes)

Two layers, and a performance change needs both:

- **Regression guards** — `tests/Nota.SmokeTest/RenderPoolTests.cs`, part of every smoke run
  (`scripts/build.sh`, CI on all three platforms; alone: `dotnet run --project
  tests/Nota.SmokeTest -c Release -- --perf`). Hardware-independent checks: render threads
  idle at ~0 CPU (their own thread clocks), parallel vs single-thread CPU ratios while playing
  and bouncing, bounce not slower than one thread, bit-identical output, tracks edited while
  rendering. When you touch the render path, threading or allocation, keep these green; when
  you fix a performance bug, add a guard here that fails on the old code (prove it: put the
  old behaviour back briefly and watch the check fail).
- **Benchmark numbers** for the PR description, below.

The engine benchmark is in the repo:

- `scripts/bench-engine.sh [-r runs] <out-dir> <label>=<engine-lib-dir> ...` — links
  `scripts/bench/engine-bench.cpp` against each Release `libnota_engine` and runs idle, offline
  render (bounce) and real-time-paced scenarios for audio and synth tracks, interleaving the
  builds run by run. Writes `results.csv` + `machine.txt`.
- `scripts/bench-chart.py <out-dir> <label>="Display name" ...` — medians, three charts
  (`cpu-playing`, `bounce-time`, `bounce-cpu`; SVG + 2x PNG) and `summary.md` (tables + machine).

Recipe:

1. Build each revision you compare **in Release**, each in its own checkout so nothing is shared:
   `git worktree add --detach /tmp/nota-<label> <commit>`, then configure + `ninja nota_engine` in
   its `src/native/nota.engine/build`. Revisions before the CPM switch (#212) need
   `git submodule update --init --recursive --depth 1` first. The branch itself can use the main
   `build/` dir (it must stay Release).
2. Copy each `libnota_engine.*` into its own folder (`/tmp/bench/libs/<label>/`) so later rebuilds
   don't change what you measured.
3. Run on a quiet machine, on power, ≥3 runs: `scripts/bench-engine.sh -r 3 /tmp/bench/out
   before=… after=…`. Look at the spread in `results.csv`; rerun if runs disagree a lot.
4. Chart it: `scripts/bench-chart.py /tmp/bench/out before="Before" after="This PR"`. Open the
   PNGs and check them (labels, overlap) before attaching.
5. Report honestly: put regressions in the table too and say why they're acceptable or what
   follows up. A benchmark also needs the commits measured and the machine (from `machine.txt`).

If the change needs a scenario the harness lacks, extend `engine-bench.cpp` (stick to C ABI
functions every compared build exports) and the scenario list in `bench-engine.sh`, in the same PR.

## 5. Put the assets on pr-assets

One folder per PR, named after the change or issue (`fix-pr212-review/`, `issue-223/`). Commit on
top of `pr-assets` — never force-push or rewrite it; old PRs link to its files.

```sh
git fetch origin pr-assets
git worktree add /tmp/nota-assets origin/pr-assets -B pr-assets
mkdir -p /tmp/nota-assets/<folder> && cp <pngs> <results.csv> <summary.md> /tmp/nota-assets/<folder>/
git -C /tmp/nota-assets add <folder>
git -C /tmp/nota-assets -c user.name="Egor Khindikaynen" -c user.email=khek@ambertape.ru commit -m "Images for <change>"
git -C /tmp/nota-assets -c credential.helper= -c credential.helper='!gh auth git-credential' \
    push https://github.com/nota-daw/nota.git HEAD:pr-assets
git worktree remove /tmp/nota-assets
```

For benchmarks commit the CSV and `summary.md` next to the charts, so the numbers stay checkable.
Embed with `https://raw.githubusercontent.com/nota-daw/nota/pr-assets/<folder>/<file>.png`.

## 6. Push and open the PR

- Push over HTTPS (SSH push to nota-daw/nota is denied):
  `git -c credential.helper= -c credential.helper='!gh auth git-credential' push -u https://github.com/nota-daw/nota.git <branch>`,
  then `git fetch origin <branch> && git branch -u origin/<branch>`.
- `gh pr create --repo nota-daw/nota --base develop --head <branch> --title "<commit-style title>" --body-file <file>`.
- Body: follow `.github/PULL_REQUEST_TEMPLATE.md` (Summary, Spec, Checklist — tick only what was
  actually done; leave platforms you didn't build unticked). Add sections as needed:
  - **Screenshots** — a table, before | after:
    `| Before | After |` / `|---|---|` / `| ![before](raw-url) | ![after](raw-url) |`
  - **Benchmark** — the charts, the `summary.md` tables, the commits measured, the machine, and
    a sentence on how to reproduce (`scripts/bench-engine.sh …`).
  - **Testing** — what was run and what was not.
  - Link the issue (`Fixes #N` / `Refs #N`) and, for review follow-ups, the PR being followed up.
- Base is `develop`. Don't enable auto-merge; don't merge.
