# Tasks: Engine Performance Regression Gate

**Spec**: [spec.md](spec.md) | **Plan**: [plan.md](plan.md)

- [x] T001 Prototype: confirm Callgrind instruction counts repeat between runs with the thread pool (0.001%)
- [x] T002 [US1] `tests/perf/nota_perf.cpp`: scenario discovery (`list`) and `run <id>` with instrumentation bracketing
- [x] T003 [US2] `tests/perf/nota_idle_cpu.cpp`: idle-engine CPU check, macOS/Linux/Windows
- [x] T004 `tests/perf/CMakeLists.txt`: build both tools against any prebuilt engine
- [x] T005 [US1] `scripts/perf-gate.sh`: build base + head engines, run scenarios in parallel under Callgrind and natively
- [x] T006 [US1][US3] `scripts/perf-gate.py`: effect-share comparison, 1% limit, silence check, Markdown report + step summary, accept flag
- [x] T007 [US1][US2] `.github/workflows/perf.yml`: `work` job (Linux) + `idle` matrix (3 OSes), label acceptance
- [x] T008 [US3] Docs: BUILD.md "Performance gate", CONTRIBUTING.md CI note, CHANGELOG, FEATURES
- [x] T008b [US1] Timing check alongside instructions (EQ-8 in #212: −35% instructions, +38% time): interleaved rounds, fastest, confirmation pass
- [x] T009 Verify SC-003: `scripts/perf-gate.sh 82babfe 64250a5` fails on EQ-8, Prism, Auto Filter; idle check fails on 64250a5
- [x] T010 Verify SC-001: the same commit against itself passes, twice, with identical counts
- [ ] T011 Verify in CI: the workflow runs on the PR and posts its summary
