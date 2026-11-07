# Quickstart: validating Nota Twin (specs/055)

How to prove each slice works. Interfaces: [contracts/c-abi.md](contracts/c-abi.md),
[contracts/mcp-tools.md](contracts/mcp-tools.md); params: [data-model.md](data-model.md).

## Prerequisites

- Release engine build in `src/native/nota.engine/build` (never `-O0` there: a debug engine
  hangs and xruns).
- macOS reference machine for SC-001; Linux with valgrind for the perf gate.

## 1. Build and smoke (every slice)

```bash
scripts/build.sh
```

Green = the engine builds, the app builds, and `tests/Nota.SmokeTest` passes including its
Twin section (contract § Behavioural contract, items 1–5 as their slice lands).

## 2. CPU budget (SC-001, SC-010)

```bash
src/native/nota.engine/build/nota_perf list | grep -i "twin\|pentad"
```

```bash
src/native/nota.engine/build/nota_perf run twin-full-12 5
```

```bash
src/native/nota.engine/build/nota_perf run pentad-full-16 5
```

The generic `instrument-18` scenario (one track, short chords) is added with the kind; the two
`*-full-*` scenarios are new: a sustained 12- / 16-note chord with every section on (both
filters, 3 LFOs, 16 routes, all FX for Twin). Expected: Twin at 12 voices (Single) and at 6 + 6 (Layer scenario) ≤ 2 × Pentad's
`wall_ms_per_s` at its maximum polyphony. On Linux, `scripts/perf-gate.sh develop` must not
regress existing scenarios (Twin adds code only; the `LadderFilter` it extends has no other user).

## 3. In the app (P1 slice)

```bash
dotnet run --project src/managed/Nota.App
```

1. `+ Instrument` → Nota Twin. Play a chord: Init sound, level ≈ Volt Init (US1-1).
2. Hold 8 notes; sweep Osc Wave, Mode, Route, both cutoffs — smooth, no clicks (US1-2).
3. Switch the card to B, change cutoff and Wave; sweep Morph — the sound travels; Morph
   endpoints match A / B (US2). Automate `Cutoff · B` — lane name shows the side.
4. KeyLab Essential mk3 (poly AT) or an MPE controller: press one key — only it opens (US3).
5. Record a take with expression, play it back — same sound (US3-4).

## 4. Later slices

- **P2**: Matrix tab — add LFO 3 → Osc 1 Wave and → multimode cutoff (opposite depths), fill
  64 slots, see "matrix full" (US4). FX on/off and full bypass (US5). Load every factory preset,
  play C major at velocity 100 across Morph — no silence, no clip (SC-004). S ↔ L, four themes (US7).
- **P3**: step-record 4 chords, play at 90 and 174 BPM, transpose by held key, record a Morph
  motion lane, "To clip", sequencer off — the clip sounds the same (US8, SC-009). Split at C3
  with two presets loaded into Lower / Upper, share 4 + 8, hold 6 keys low — 4 sound (US9).

## 5. UI evidence

Card screenshots via the headless Avalonia / in-app capture recipe used for other cards
(`nota-design` skill) — before/after for the PR (`nota-pr` skill).
