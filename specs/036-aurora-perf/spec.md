# Feature Specification: Aurora CPU Optimization

**Feature Branch**: `036-aurora-perf`

**Created**: 2026-10-21

**Status**: Implemented

**Input**: User description: "Aurora synthesizer is poorly optimized — with heavy presets like Super Saw and two instances it catches stutters (dropouts)." Context: analysis (see conversation) identified per-sample transcendental calls (`exp2` per unison voice, `tan` per filter update, `sin` in LFO/chorus), doubled mid+side filtering on spread unison, and serial-first adaptive track parallelism as the likely causes; Apple hardware hides them via faster libm, NEON/FMA, larger caches and larger default audio buffers. Agreed repair order: measure → inaudible hoisting → threshold-gated fast-math → optional Eco mode → engine buffering/parallelism fixes.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [ ] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [ ] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Heavy Aurora arrangement plays without dropouts (Priority: P1)

A musician on x86 Linux loads two Aurora tracks with the factory Super Saw
preset, plays polyphonic material (chords of up to 6 voices), and hears clean
audio with no stutters, clicks, or dropouts for the whole session.

**Why this priority**: This is the reported bug. Everything else is a means to
this end.

**Independent Test**: Load the reference session on the reference machine,
play the reference MIDI passage, listen and check the dropout/xrun counter —
fully testable without any other story implemented.

**Acceptance Scenarios**:

1. **Given** two Aurora instances on the Super Saw factory preset at 48 kHz / 256-frame buffer, **When** a 6-voice chord passage plays for 5 minutes, **Then** no audible dropout occurs and the xrun counter stays at zero.
2. **Given** the same setup, **When** playback starts (first seconds), **Then** no start-up stutter occurs (no serial-first ramp-up penalty).

---

### User Story 2 - Existing Aurora sound is preserved (Priority: P1)

A musician who relies on current Aurora factory presets (pads, leads, basses)
updates Nota and hears no difference in their projects.

**Why this priority**: An optimization that changes the sound is a regression
for every existing project. Sound preservation is a release gate, independent
of how much faster the synth gets.

**Independent Test**: Render a fixed set of factory presets offline before and
after each optimization step and compare (null test) — testable with zero
performance work done.

**Acceptance Scenarios**:

1. **Given** the Tier-1 (hoisting-only) optimizations, **When** factory presets are rendered offline, **Then** output is bit-identical to the pre-change render.
2. **Given** any Tier-2 (approximation) optimization, **When** the null test runs, **Then** the difference stays below the agreed threshold, otherwise the change is reverted.

---

### User Story 3 - Repeatable performance measurement (Priority: P2)

A developer runs a single command on x86 Linux and gets Aurora render timings
(p50/p99 block time) for the reference scenario, so every optimization step
can be proven — not felt.

**Why this priority**: Without a baseline on the affected platform, any
"optimization" is guesswork (M2 numbers systematically understate the cost).

**Independent Test**: Run the benchmark command twice on the reference
machine; both runs report numbers within ±10% — no DSP change required.

**Acceptance Scenarios**:

1. **Given** a clean checkout, **When** the developer runs the Aurora benchmark command, **Then** it reports p50/p99 block render time for the two-instance Super Saw scenario.
2. **Given** two consecutive runs on an idle machine, **When** results are compared, **Then** they agree within ±10%.

---

### Non-goal - Quality switch (explicitly excluded)

A Full/Eco quality switch on the Aurora card was considered and **rejected**:
the optimization must stay fully invisible — no UI changes, no audible change
beyond the null-test threshold. If inaudible optimization does not close the
budget, the approach returns to the drawing board instead of degrading quality
silently or visibly.

---

### Edge Cases

- What happens when the reference scenario is exceeded (3+ instances, 16-voice stack, smaller 128-frame buffer)? Budget headroom (SC-002) should absorb reasonable excess; hard overload must degrade via voice-stealing, not deadlock or crash.
- How does the synth behave at other sample rates (44.1 kHz) — does the optimization hold, not just at 48 kHz?
- What happens to chorus/reverb tails and release envelopes under voice early-out (Story 4 / Tier-3 work)? Tails must not click or cut audibly.
- Mono bass presets (e.g. Sub Bass, `mono=1`) must gain nothing-or-benefit — never regress — from unison-targeted changes.

## Requirements *(mandatory)*

### Functional Requirements

- **FR-001**: The reference scenario (two Aurora instances, factory Super Saw preset, polyphonic passage up to 6 voices) MUST play without audible dropouts at 48 kHz with a 256-frame buffer on the reference x86 Linux machine.
- **FR-002**: The reference scenario's p99 block render time MUST stay under 50% of the block time budget, leaving headroom for effects and mixing.
- **FR-003**: Tier-1 optimizations (constant hoisting, dead-computation removal) MUST be bit-identical in offline renders of all factory Aurora presets.
- **FR-004**: Every approximation (if any is adopted) MUST stay below −50 dB residual in the null test, consistent with previous practice; anything measuring worse MUST be reverted.
- **FR-005**: A repeatable one-command benchmark for the reference scenario MUST exist, runnable on x86 Linux, reporting p50/p99 block times.
- **FR-006**: No user-visible quality switch is in scope. The optimization MUST stay fully invisible: no UI changes, and no sound change beyond FR-003/FR-004.
- **FR-007**: No change in this feature MUST add allocation, locking, or I/O on the audio thread (AR-6); audio-rate data flow keeps using existing snapshot/command-queue patterns (AR-4/AR-5).
- **FR-008**: Engine-level playback MUST NOT stutter by design on slower machines (adaptive parallelism must engage before — not after — the first audible overload; Linux default buffering must be documented).

### Key Entities *(include if feature involves data)*

- **Reference scenario**: two Aurora instances + factory Super Saw preset + fixed polyphonic passage + 48 kHz / 256-frame buffer on the reference x86 Linux machine; the yardstick for every measurement.
- **Null-test threshold**: the maximum allowed residual difference between pre- and post-change offline renders; the gate for every approximation.
- **Benchmark report**: p50/p99 block render times for the reference scenario, reproducible within ±10% run-to-run.

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: The reference passage plays 5 minutes with zero audible dropouts and zero counted xruns on the reference machine.
- **SC-002**: p99 block render time of the reference scenario is under 50% of the block budget (2.66 ms at 48 kHz / 256 frames).
- **SC-003**: All factory Aurora presets pass the null-test gate (bit-identical Tier-1; within threshold for any adopted Tier-2 change).
- **SC-004**: The benchmark command reproduces its numbers within ±10% across consecutive runs on an idle machine.
- **SC-005**: `scripts/build-linux.sh` (build + smoke test) passes; no new audio-thread rule violations; a `CHANGELOG.md` `[Unreleased]` entry describes the user-facing effect.

## Assumptions

- The reference machine is the reporter's affected x86 Linux machine (exact model, audio backend and buffer settings to be recorded in the plan phase); Apple M2 results are explicitly NOT acceptance evidence.
- Suspected hotspots (per-sample `exp2` detune/pitch factors, `tan` in filter-coefficient updates, `sin` in LFO/chorus, doubled mid+side filtering, serial-first track parallelism) are investigation leads from code reading — the benchmark + profiler in the plan phase confirm or refute them before any DSP change.
- The Super Saw factory preset definition (`table=0`, both oscillators on, full unison, spread, drive/chorus/reverb amounts) is taken as the fixed reference load; preset content itself does not change.
- Previously reverted fast-math approximations (LFO/chorus/drive-fold sine, measured around −44…−51 dB residual) stay reverted: the agreed gate is −50 dB, consistent with previous practice.
- No new dependencies; no C ABI changes expected (all work is inside existing DSP code and playback configuration).

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [ ] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [ ] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [ ] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5)
- [ ] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [ ] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest`
- [ ] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [ ] UI change → follows `DESIGN.md` (Studio design language)
