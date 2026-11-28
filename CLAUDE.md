<!-- SPDX-License-Identifier: AGPL-3.0-only -->
<!-- Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms. -->

# CLAUDE.md

Nota is a real-time audio application: a C++20 engine behind a pure C ABI, driven by a
.NET 10 / Avalonia app. Read [`CONTRIBUTING.md`](CONTRIBUTING.md) before changing
anything — the rules there are binding (AGPL headers, dependency policy, audio-thread
restrictions).

**Before any change — code, spec, docs, even a typo — confirm there is a GitHub issue
for it that a core contributor has approved** (it carries the `approved` label, or
`good first issue` / `help wanted`; check with `gh issue view <N> --repo nota-daw/nota`).
If the user hasn't named one, or it isn't approved, **stop**: don't write a spec, don't
edit files. Tell them to open or find an issue and wait for approval
([`CONTRIBUTING.md`](CONTRIBUTING.md) § Start with an approved issue), and offer to draft
the issue text. Once it is approved, stay inside the scope the issue agreed and link it
from the pull request (`Closes #N`). Release pull requests cut by core contributors are
exempt.

- The project constitution for agents lives at `.specify/memory/constitution.md`.
- Non-trivial work is spec-driven. **When the user asks for a feature or behaviour
  change, do not start implementing: open with `/speckit-specify <the request>` and
  follow the phases** — `/speckit-specify` → `/speckit-clarify` → `/speckit-plan` →
  `/speckit-tasks` → `/speckit-implement` → `/speckit-converge`.
  Artifacts land in `specs/<slug>/`; see [`CONTRIBUTING.md`](CONTRIBUTING.md) §
  Spec-driven workflow. `specs/000-example/` shows a completed run.
  Trivial work — typos, small fixes, doc edits — skips the spec (not the approved
  issue) and goes straight to the change.
- Bug reports go through `/speckit-bug-assess` → `/speckit-bug-fix` → `/speckit-bug-test`.
- The `nota-*` skills in `.claude/skills/` (design, docs, features, instrument,
  shortcuts, release, pr) encode project-specific workflows — prefer them over improvising.
  `.claude/skills/` is their source; after editing one, run `scripts/sync-agent-skills.sh`
  to refresh the copies other agents read (CI checks they match).
- Before any pull request: `scripts/build.sh` (or the Linux/Windows siblings) must pass;
  it runs the smoke test that drives the engine end-to-end.
- **Native engine readability & architecture gates** ([`ARCHITECTURE.md`](ARCHITECTURE.md),
  [`.specify/memory/constitution.md`](.specify/memory/constitution.md), [`.clangd`](.clangd)):
  keep strict `PUBLIC include/` vs `PRIVATE src/` module boundaries (`test_headers_compile`
  green, zero leaked third-party/OS headers in `include/`, JUCE confined to `pluginhost/` +
  `pluginscanner/src/backends/juce/` with `0` dynamic exports, Objective-C++ in `*_mac.mm` only);
  keep public `Engine::*` domain headers `< 180` lines and split `> 400`-line built-in devices
  into focused `Params`/`Dsp`/`Device` files (no `> 1,000`-line monoliths); use OOP registries
  rather than `switch` factories and RAII without raw `new`/`delete` in `modules/*` domain/API
  code; pass `NOTA_STRICT_WARNINGS_AS_ERRORS=ON` across `dev`/`dev-cpp`/`dev-asan`/`release`
  with zero `clangd` missing/unused-include diagnostics.
- **Mandatory `clang-format` gate**: before finishing any edit to native engine, perf, or
  benchmark code (`src/native/nota.engine/`, `tests/perf/`, `scripts/bench/`), unconditionally
  run `clang-format -i` (using root [`.clang-format`](.clang-format)) on all modified
  C/C++/ObjC++ files.
