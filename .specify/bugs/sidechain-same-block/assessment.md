# Bug Assessment: Export puts sidechain pumping a chunk late

- **Slug**: sidechain-same-block
- **Created**: 2026-11-19
- **Source**: https://github.com/nota-daw/nota/issues/676 (host: github.com, allowlisted) + the user's follow-up project `Bugged.nota`
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

#676: "totally out of sync on render. plays fine while working on DAW". 0.60.3 fixed the
tempo-synced LFOs of Nota Aurora / Volt, and the user reports nothing changed. Their follow-up
project `Bugged.nota`: a Drum Rack kick (four on the floor) keys a Nota Compressor on a Nota Volt
track (sidechain from the kick track, post-FX); loop 8.1–12.1; two returns (Reverb, Delay), an
empty audio track. The original project in the issue also carried a Nota Compressor.

## Symptom

In the exported file the Volt track pumps late against the kick — the gain dip lands near the
middle of the beat instead of just after the kick. Live in the app the pumping sits on the kick.

## Reproduction

1. Open `Bugged.nota` (Drum Rack kick → sidechain → Compressor on Volt).
2. Play from 8.1: the pump follows the kick.
3. Export the mix (or render offline in 8192-frame chunks, as `AudioExporter` does).
4. The Volt pump in the file lags by about one export chunk.

Measured with a harness (`/private/tmp/nbugged`: Volt soloed, 16 beats from 8.1 at 48 kHz, the
quietest 1/64 of the beat averaged over beats, and envelope cross-correlation against live):

| Render | Dip in the beat | Lag vs live |
|---|---|---|
| live, 512-frame blocks | 0.156 | — |
| offline, 256–2048 | 0.156 | 0 |
| offline, 4096 | 0.219 | ~95 ms |
| offline, 8192 (the exporter's chunk) | 0.406 | ~125 ms |

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine.h` (`routeBus_` / `routeBusNext_`, ~line 2879) — the
  route buses are double-buffered: consumers read the source's **previous block**, sources fill
  the next one, swapped at the end of `mixGraph`. The comment calls the block of detector latency
  "inaudible after attack/release" — true for a 256–512 frame device buffer, not for an 8192-frame
  export chunk (170 ms at 48 kHz).
- `Engine_Render.cpp:Engine::keyBuffer()` — hands every consumer `routeBus_` (previous block).
- `Engine_Render.cpp:Engine::mixGraph()` — Pass 1 renders every leaf's DSP (pool, then serial)
  before any route tap is written; the post-fader and pre-FX taps are written in the serial
  accumulate, so even a group or return consumer that runs after all leaves reads last block.
- `src/managed/Nota.Infrastructure/Audio/AudioExporter.cs` (`Chunk = 8192`) — export block size;
  `Engine::processBlock` does not split it (only at `kMaxBlock`, loop ends, tempo points).

## Root Cause Hypothesis

The sidechain key always arrives one engine block late. Live the block is the device buffer, so
the lag is a few ms; an export renders 8192-frame blocks, so every keyed device (compressor,
gate, ducking, Dynamic EQ, vocoder, Flux react) reacts ~170 ms late at 48 kHz, ~186 ms at
44.1 kHz. A larger live buffer (2048) is late too (~43 ms). Confidence: high (reproduced on the
user's project; lag follows the block size exactly where attack/release smoothing no longer
hides it).

## Proposed Remediation

**Preferred** (chosen by the maintainer): deliver the key in the same block.

- Per route slot, a "fresh" flag, cleared when the block's Next buses are cleared. `keyBuffer()`
  returns the slot's Next (this-block) buffer when the slot is fresh, else the previous block.
- Pass 1 renders leaves in **levels**: a leaf whose device / instrument / rack child listens to
  another leaf's slot gets a level above that leaf (longest path, capped; a cycle can't be
  ordered, so its edges stay one block late). Each level: the pool renders its eligible leaves,
  then its serial leaves render inline into their own `leafBuf` regions, then the level's pre-FX
  and post-fader taps are written and its slots marked fresh. The serial accumulate then only sums.
  Projects with no leaf-to-leaf key keep today's single dispatch and code path (perf gate).
- After Pass 1 every leaf slot is fresh, so group and return consumers get same-block keys from
  leaves; groups and returns mark their own slots fresh once written, so a later group/return
  hears an earlier one in the same block. A leaf keyed by a group or return (they run after the
  leaves), and any cycle, keep the previous block.
- Monitoring (#563, fixed-lag ring) and an armed track's input meter keep reading as today.

**Alternatives**:
- Export in device-sized chunks (split `renderOffline` into 256–512 frames): small change, export
  matches live, but the key keeps a block of lag live and depends on the buffer size.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Engine_Render.cpp` (`mixGraph`, `keyBuffer`, comments)
- `src/native/nota.engine/src/engine/Engine.h` (fresh flags, LeafWork fields, comments)
- `tests/Nota.SmokeTest/SidechainSyncTests.cs` (new), `tests/Nota.SmokeTest/Program.cs` (flag)

**Tests to add or update**:
- Kick track keys a compressor on a sustained synth: the pump dip lands at the same place in the
  beat live (512) and in offline renders of 256 … 8192 frames.
- Same-block key for a group consumer and a return consumer; a two-track key cycle renders
  finite and deterministic (two offline renders identical).
- Existing sidechain / vocoder / part / render-pool tests keep passing (`--hosted-render-pool`).

## Risks & Considerations

- Parallelism: a keyed leaf waits for its source (one more pool dispatch per level). Only
  projects with leaf-to-leaf keys pay it.
- Determinism: freshness flips only between dispatches (serial thread), so what a consumer
  hears does not depend on thread timing.
- Sound change live: keyed devices react ~one buffer earlier than before (tighter pumping).
  Saved projects sound tighter, not different in kind.
- The CI instruction-count perf gate: the no-sidechain path must stay as it is.
- Cross-thread: the pool's dispatch join orders the serial writes of taps / flags before the next
  level's tasks read them.

## Open Questions

- None blocking. Same-block keys from a group or return into a leaf stay a block late (would need
  groups rendered between leaf levels) — out of scope.
