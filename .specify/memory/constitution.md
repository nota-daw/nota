# Nota Constitution

Nota is a real-time audio application: a C++20 engine behind a pure C ABI, driven by a
.NET/Avalonia app, licensed AGPL-3.0-only. These principles bind every contribution —
human or AI agent. Each one points at its source of truth rather than restating it, so
this file stays small and the authoritative docs stay authoritative.

## Core Principles

### 0. Approved Issue First (NON-NEGOTIABLE)

No change starts — no code, no spec, no doc edit — without a GitHub issue that a core
contributor has approved (the `approved` label, or `good first issue` / `help wanted`).
Without one, an agent stops and asks the user to open or find an issue and wait. Work
stays within the scope the issue agreed; the pull request links it. Release pull requests
cut by core contributors are exempt. See
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) § Start with an approved issue.

### I. Real-Time Audio Safety (NON-NEGOTIABLE)

The audio thread never allocates, locks, or does I/O (AR-6); data crosses into it through
the lock-free command queue (AR-4); structural edits publish a new immutable graph
snapshot (AR-5). A change that looks harmless in most codebases produces audible
dropouts here. The full rules: [`ARCHITECTURE.md`](../../ARCHITECTURE.md#architecture-rules-ar-).

### II. Boundary Discipline (NON-NEGOTIABLE)

Only a pure C ABI crosses the managed/native boundary (AR-7) — no C++ types, no
exceptions; extend `nota_engine.h` rather than working around it. JUCE stays confined to
`pluginhost/`; the core engine's JUCE-freedom is an architectural and licensing
constraint.

### III. Licensing Hygiene (NON-NEGOTIABLE)

Every contribution is AGPL-3.0-only. Every new source file carries the standard two-line
SPDX header. No dependency is added without a license check and a row in
[`LICENSES/third-party.md`](../../LICENSES/third-party.md). See
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) § Licensing and § Adding a dependency.

### IV. Verification Gate

The smoke test (`tests/Nota.SmokeTest`, run by `scripts/build.sh` and siblings) drives
the engine end-to-end through the C ABI and must pass before any pull request. Changes
to engine behaviour extend the smoke test to cover them.

### V. One Logical Change per Pull Request

Small, single-purpose pull requests. Comment headers explain *why* a file exists, not
what each line does. Match the style of the code around you.

### VI. Documentation Stays in Sync

User-visible changes get a `CHANGELOG.md` `[Unreleased]` entry. When working in this
repository with an agent that has the Nota skills, run `nota-features` (FEATURES.md),
`nota-docs` (user manual), and `nota-shortcuts` (keyboard bindings) as applicable. UI
work follows [`DESIGN.md`](../../DESIGN.md) (the Studio design language).

### VII. Spec-First for Non-Trivial Work

Any non-trivial feature or bugfix goes through the spec-driven workflow —
specification, plan, and tasks exist and are agreed before implementation. Specs stay
small and are committed under `specs/`. Typos and trivial fixes skip this (but not
principle 0).

### VIII. Safety Rules Are Never Weakened to Unblock a Feature

If principles I–III seem to stand in the way of a feature, the feature changes course or
escalates to a human maintainer. The rules themselves are not bent, even "temporarily",
even with a comment explaining why.

## Quality Gates

- `scripts/build.sh` (macOS; `scripts/build-linux.sh`, `scripts/build-win.ps1`) must exit
  green — it builds native + managed and runs the smoke test.
- `/speckit-converge` must report **Converged** before a spec-driven change is submitted.
- New public API surface at the C ABI boundary is reflected in `nota_engine.h` and
  covered by the smoke test.

## Governance

This constitution governs AI agents working in this repository;
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) remains canonical for humans, and
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) is the authority for the AR rules referenced
above. Amendments require a pull request that updates the source-of-truth doc first,
then this file, and must not weaken principles I–III.

**Version**: 1.1.0 | **Ratified**: 2026-10-11 | **Last Amended**: 2026-11-02
