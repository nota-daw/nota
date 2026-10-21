# Feature Specification: Engine Performance Regression Gate

**Feature Branch**: `perf/engine-regression-gate`

**Created**: 2026-10-20

**Status**: Draft

**Input**: User description: "Write tests to ensure that any engine changes don't hinder performance at all. Any form of regression should be a fail." Context: benchmarking #212 (engine rebuild) found two regressions that CI did not catch — an idle engine keeping every core but one at 100% (#223) and EQ-8 / Prism / Auto Filter rendering 1.7–2.9× slower (#224).

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [ ] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [x] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - A slower engine change fails its pull request (Priority: P1)

A contributor opens a pull request that changes engine code. CI renders a fixed set of
engine scenarios with the PR's engine and with the engine of the PR's base, measures
how much work each does, and fails the PR if any scenario does more work than the base
beyond a small tolerance. The failure names each slower scenario and by how much.

**Why this priority**: This is the request: no engine change may make Nota slower
without someone noticing. It would have caught #224 (EQ-8 2.9× slower) before merge.

**Independent Test**: Push a branch that adds a deliberate busy loop to one effect's
processing. The gate fails and names that effect's scenario. Revert it, and the gate
passes.

**Acceptance Scenarios**:

1. **Given** a PR that makes EQ-8 do 2× the work per block, **When** CI runs, **Then**
   the performance check fails and its summary lists the EQ-8 scenario with its
   before, after and % change.
2. **Given** a PR that changes only C# or docs, **When** CI runs, **Then** the
   performance check passes (or is skipped) without building a second engine.
3. **Given** a PR that makes rendering faster, **When** CI runs, **Then** the check
   passes and the summary shows the improvement.

---

### User Story 2 - An idle engine that burns CPU fails on every OS (Priority: P1)

CI creates an engine, leaves it idle (transport stopped, nothing rendering) and
measures process CPU over a few seconds on macOS, Windows and Linux. Above a small
absolute limit, the check fails.

**Why this priority**: #223 (idle engine at 892% CPU on an M4) is the costliest
regression found so far: hot machines and drained batteries for every user. It is an
absolute property, so it needs no comparison against the base.

**Independent Test**: On current develop (with #223 unfixed) the check fails with a
clear message; with #223 fixed it passes.

**Acceptance Scenarios**:

1. **Given** an engine whose worker threads spin while idle, **When** the idle check
   runs, **Then** it fails and reports the measured CPU %.
2. **Given** an engine that parks its workers, **When** the idle check runs, **Then**
   it passes on all three OSes.

---

### User Story 3 - Contributors can see and reproduce the numbers (Priority: P2)

The check publishes a readable table in the CI run summary (scenario, base, PR,
change, verdict) and wall-clock timings for information. The same comparison runs
locally with one script so a contributor can reproduce a failure before pushing.

**Why this priority**: A gate nobody can read or reproduce gets disabled. The timings
also give marketing-grade speed numbers for each release.

**Independent Test**: Run the local script against two commits; it prints the same
table as CI.

**Acceptance Scenarios**:

1. **Given** a finished CI run, **When** a reviewer opens its summary, **Then** they see
   every scenario's work count and timing for base and PR.
2. **Given** a contributor's machine, **When** they run the local script with two
   commits, **Then** it builds both engines and prints the same comparison.

---

### Edge Cases

- A PR that intentionally makes a device heavier (for example, a new oversampling
  option on by default): the gate fails, and a maintainer can approve the regression
  explicitly in a visible, reviewable way (a label or a line in the PR), which the
  summary records. The gate is never silently disabled.
- A new device with no base counterpart: reported as "new", never a failure.
- A removed device: reported as "removed", never a failure.
- Base engine fails to build (broken base): the check reports that it could not compare
  and fails, rather than passing silently.
- Rendering output changes (not just speed): out of scope here; output correctness
  stays with the smoke test.
- Multi-threaded rendering: total work is counted across all threads, so moving work
  to more cores is neutral and spinning threads count as extra work.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: CI MUST compare the PR's engine against the engine built from the PR's
  base commit, in the same job, on the same machine, with the same build settings.
- **FR-002**: The comparison MUST use a measure of work that repeats within 0.5%
  between identical runs on CI (deterministic instruction counting), so that a small
  tolerance does not cause random failures.
- **FR-003**: A scenario MUST fail when its work exceeds the base by more than 1%.
  A failure of any scenario MUST fail the check.
- **FR-004**: The scenario set MUST cover each built-in instrument on one track, each
  built-in audio effect on one track, busy mixes of 1, 4, 8, 16 and 32 tracks, a
  sparse session (1 track in 4 playing) and a silent session (tracks, no clips).
- **FR-005**: Work MUST be counted across all engine threads, including thread-pool
  workers.
- **FR-006**: An idle-engine check MUST run on macOS, Windows and Linux and fail when
  an idle engine uses more than 5% of one core over 5 seconds.
- **FR-007**: Wall-clock timings MUST be measured for every scenario, base and PR
  interleaved over several rounds on the same machine. A scenario MUST fail when its
  fastest time grows by more than 15% (and by more than 0.25 ms per second of audio)
  and still does after a confirmation pass with more rounds.
  This catches slowdowns that do not add instructions (denormals, cache misses, thread
  hand-offs): measuring #212 showed EQ-8 running 2.9× slower on an M4 while executing
  35% fewer instructions.
- **FR-008**: The check MUST write a summary table (scenario, base, PR, % change,
  verdict) to the CI run summary.
- **FR-009**: A maintainer MUST be able to accept a known regression on a specific PR
  through a visible, reviewable action; the summary MUST record that it was accepted.
- **FR-010**: The check MUST run only when engine sources, the engine build or the
  benchmark itself change.
- **FR-011**: A local script MUST reproduce the comparison between any two commits.
- **FR-012**: Each scenario MUST also check that its output is not silent where sound
  is expected, so a "faster" result from a broken render cannot pass.

### Key Entities

- **Scenario**: a named engine setup (tracks, devices, notes) rendered for a fixed
  length of audio. Has a work count and a wall time for base and PR.
- **Result table**: one row per scenario with base, PR, change and verdict.
- **Regression acceptance**: a maintainer's explicit approval of named regressions on
  one PR.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: Re-running the check on an unchanged commit gives the same verdict 20
  times out of 20 (no flaky failures).
- **SC-002**: A deliberate 2% slowdown in any single device fails the check.
- **SC-003**: Run against #212 (`82babfe` → `64250a5`), the check fails and names the
  EQ-8, Prism and Auto Filter scenarios, and the idle check fails.
- **SC-004**: The performance job adds no more than 25 minutes of CI time, and runs in
  parallel with the existing build jobs.
- **SC-005**: A contributor can reproduce a CI failure locally with one command.

## Assumptions

- Work is measured by counting executed CPU instructions on Linux (Valgrind
  Callgrind). Instruction counts are steady on shared CI runners, where timing swings
  10–30%, so they can gate at 1%. They miss slowdowns that don't add instructions, which
  is why timing gates too, at a looser limit (FR-007). A slowdown smaller than 15% that
  adds no instructions can still slip through. That is the price of not failing PRs at
  random.
- Valgrind is a CI-only tool. It is not linked or shipped, so it adds no runtime
  dependency (CONTRIBUTING dependency policy).
- Instruction-count scenarios run on Linux only. The idle check runs on all three OSes.
- The benchmark program drives the engine only through the public C ABI
  (`nota_engine.h`, AR-7). It reuses the harness written for the #212 benchmarks.
- 1% and 5% are starting thresholds and can be tuned in one place.
- Until #223 and #224 are fixed, the gate fails on develop. It lands together with,
  or after, the #223 fix, so that develop stays green.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned (Valgrind is a CI tool only, not linked or shipped)
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) (no engine change)
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` (no behaviour change; the idle check could also live in the smoke test)
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them) (CI-only: FEATURES.md "platform/build/CI capability" entry)
- [x] UI change → follows `DESIGN.md` (Studio design language) (no UI)
