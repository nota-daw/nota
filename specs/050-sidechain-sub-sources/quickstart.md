# Quickstart: validating sidechain sub-sources

## Automated (the PR gate)

```bash
scripts/build.sh
```

`tests/Nota.SmokeTest/SidechainPartTests.cs` must pass. It covers:

1. **Kick pad only (FR-016, SC-001)** — Drum Rack with two Sampler pads (kick, hat) loaded from a
   fixture WAV, kick on steps 1/3, hat on 2/4. Bass track: a sustained deterministic clip → Compressor
   keyed `(drums, kickPad)`. Offline render; per block read `DeviceGainReduction`. Expect GR ≥ 3 dB in
   kick blocks and < 0.1 dB in hat blocks — once with Pre, once with Post.
2. **Pre vs Post** — a Utility at −20 dB on the kick pad: Pre GR unchanged, Post GR drops.
3. **Ghost kick (FR-007a, SC-005a)** — mute the kick pad: the Drum Rack track's output loses the
   kick, GR on the bass stays.
4. **Stable identity (FR-003, SC-003)** — move the kick pad, add a pad, delete the hat: the key
   still follows the kick (GR pattern and `sidechain_key` index).
5. **Dormant key (FR-004)** — delete the kick pad → part -2, no GR; undo → key live again.
6. **Rhythm voice and Instrument Rack chain** — same check as 1 against Rhythm voice 1 and a chain.
7. **React** — Flux keyed off the kick pad reacts on kick blocks only.
8. **Route pool (FR-007b/c)** — 16 distinct sources set, the 17th returns -2; shared sources reuse.
9. **Persistence (FR-012/013)** — save → reopen keeps `(source, part, tap)`; the `racks-v8` fixture
   and a v27 project with whole-track keys open unchanged.
10. **Idle cost (FR-011)** — the engine perf gate (specs/034) shows no regression with no part keys.

## Manual (the app)

1. New project → "Drums" (Drum Rack: Kick, Hat), "Bass" (any synth + Compressor).
2. Compressor → Sidechain tab → source dropdown: "Drums" with indented "Kick", "Hat". Pick "Kick".
3. Play: the GR meter moves on kicks only; Listen reads "Listen: … Drums · Kick post …".
4. Rename the pad → the card follows. Mute the pad → you still hear the duck, not the kick.
5. Save, reopen → still "Drums · Kick".
6. Flux on another track → React source "Drums ▸ Kick" (no Pre/Post control).

Screenshots of the picker go into the PR via the `nota-pr` skill.
