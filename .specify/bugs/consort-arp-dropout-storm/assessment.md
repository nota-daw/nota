# Bug Assessment: dropout storm with a Consort + Arp track at a 64-frame buffer

- **Slug**: consort-arp-dropout-storm
- **Created**: 2026-11-03
- **Source**: https://github.com/nota-daw/nota/issues/450 (host: github.com, allowlisted; issue written in this session from the measurements below). Reproducing project: `~/Documents/Nota Projects/demo21.nota`.
- **Verdict**: valid
- **Severity**: high. Sustained dropouts at small buffers in an ordinary mixed project (built-ins plus a couple of plug-ins) on a top-end Mac. The cause is in the engine's render scheduling, not in Consort, so any heavy built-in alongside plug-ins is exposed.

## Report (summarized)

> **Dropout storm with a Consort + Arp track at a 64-frame buffer, until Stop.** Clean for a
> few seconds, then dropouts build into a storm that lasts until Stop. Muting the Consort track
> cuts them about 8×. Consort dominates the IO thread's render, though the thread is only about
> 42 % busy on average. A headless render of the same project shows no growth.

## Symptom

At 48 kHz / 64 frames (period 1.333 ms), demo21 plays cleanly at first. Then dropouts build to
50–150 per 10 s and stay there until the transport stops. Average DSP load is 0.2–0.6 of the
period. Expected: no dropouts while the whole project costs well under one period on average
and the machine has 9 idle render threads.

## Reproduction

1. Audio: 48 kHz, 64-frame buffer, built-in output.
2. Open `demo21.nota`:
   - track 1: kind 2 + EQ-8 / Shutter / Delay / Reverb;
   - track 2: kind 8 + Delay / Reverb / Vintage;
   - track 3: kind 14 + Valve / Chamber;
   - track 4: Consort "Poly Pad" (2× oversampling, true poly, unison) + Arp "Down 1/8" + Delay / Chamber / Shutter / Reverb;
   - track 5: Splice Bridge (VST3);
   - track 6: Maschine 2 (AU).
3. Loop 0–16, play for a minute, and watch the dropout counter (log or MCP `get_engine_info`).

Measured on a bundle of `fix/plugin-block-midi` (#438, which already removes Splice's per-block
LaunchServices call and hands the device workgroup to AUs and pool workers), driven over MCP:

| Condition (30–40 s each) | Dropouts | `dspLoad` |
|---|---|---|
| all tracks, 30 s | ~500, building up over time | 0.2–0.64 |
| Consort track muted, 30 s | 63 | 0.16 |
| (before #438) Splice muted, 40 s | 141 | 0.55 |

A headless run of the same project (managed loader, `nota_engine_render_device_block`, 48 kHz /
64 frames, paced like a callback with a spin-wait) showed 0–2 late blocks per second over
2 minutes, with the mean flat at about 0.33 ms.

## Suspected Code Paths

- `src/native/nota.engine/src/engine/Engine_Render.cpp:2367–2455` — the live-device parallel
  decision (`rtParallel_`). The pool joins only when
  `alone && leafWorkNs_ > periodNs` (`:2447`) or a probe block misses. `leafWorkNs_` is the
  1/16-smoothed cost of the **eligible** (built-in) leaves, measured on blocks this thread
  renders alone. Two blind spots:
  1. **Serial work isn't counted.** Plug-in leaves are never eligible (`:2341–2347`) and always
     render on the callback thread, along with the master and returns. With built-ins at about
     40 % of the period and plug-ins at about 30 %, the block overruns while
     `leafWorkNs_ ≈ 0.4 × period`, so the pool never joins. The storm-time profile shows all
     9 workers parked (`__ulock_wait`) while the IO thread renders Consort, Chamber, Reverb and
     the plug-ins one after another.
  2. **Mean, not tail.** A smoothed mean must exceed the whole period before anything happens.
     Blocks with many arp note-ons, voice starts and envelope attacks overrun long before the
     mean does.
- `src/native/nota.engine/src/devices/instruments/Consort.cpp:766–777` (`renderChunk`) and
  `renderSpan`: the dominant single cost. Each span reloads all ~140 params and recomputes
  the patch cables, and the internal sequencer splits blocks further. This is legitimate work
  for a 2× oversampled 16-voice synth (smoke "Consort stress": 16 voices × 4 OS ≈ 1.7 ms per
  128 frames), but it makes track 4 the single biggest leaf.
- Why "builds over time, until Stop": voices and tails accumulate (pad releases, arp
  retriggers, delay and reverb tails held awake). Per-block cost rises over the first seconds
  of a loop. Stop flushes voices (`flushArrangementVoices`) and lets the effects sleep, so the
  next Play starts cheap.
- Why headless doesn't show it: the harness spin-waits between blocks, so its core never idles
  or downclocks, and there's no UI or other process load. The live IO thread sleeps between
  callbacks and shares the machine with the UI (about 1 core) and WindowServer. The same mean
  then has a fatter tail. That is exactly the case a mean-based join can't catch.

## Root Cause Hypothesis

The engine keeps a mixed project on one thread until the *built-in* part alone averages more
than a full buffer period. In demo21 the total, built-ins plus plug-ins, averages 50–70 % of a
64-frame period, and the tail (arp-dense blocks, voice-heavy moments, live-app scheduling
jitter) overruns regularly. The 9 render threads stay parked because the trigger looks at
neither the serial share nor the tail. Consort is simply the largest eligible leaf: muting it
lowers the total enough that the tail mostly fits. Confidence: **medium-high**. The profile and
the mute experiments fit, but the fix must be measured live to confirm.

## Proposed Remediation

**Preferred: join the pool on the whole block's deadline, not on the eligible mean.**
- Measure the callback's own render time per block (total `processBlock`, which `render()`
  already times for `cpuLoad_`).
- Join (`rtParallel_ = true`) when either:
  - a smoothed total exceeds a budget, say `kRtSizeLoad × period` (0.85);
  - or a late (or near-late, > 0.9 × period) block occurs and there is eligible work to move.

  Size `helpers` from the eligible work against the time left after the serial share
  (`period × kRtSizeLoad − serialNs`), not against the whole period.
- Keep the existing leave logic (hold, probe, doubling) so a fit block still releases the pool.
- Join only while the pool's workers are real-time in the device workgroup (#438): joining
  costs little now, where before it meant spinning on background threads.

**Alternatives**:
- Always render eligible leaves in parallel when the buffer is ≤ 128 frames. Simple, but it
  spends 2–3× CPU on light projects (the original reason for the adaptive join, see
  `Engine.h:2187`).
- Make Consort cheaper per span (hoist param loads and cable math out of `renderSpan` per
  block). Worth doing, but it moves the threshold rather than fixing the scheduling blind spot.

**Files likely to change**:
- `src/native/nota.engine/src/engine/Engine_Render.cpp` (parallel decision, helpers sizing)
- `src/native/nota.engine/src/engine/Engine.h` (state + the comment block that documents it)
- `tests/Nota.SmokeTest/RenderPoolTests.cs` (new guard)

**Tests to add or update**:
- RenderPoolTests: a session where a serial (non-eligible) share plus eligible built-ins
  overruns the period, while the eligible share alone is well under it. Device-block pacing at
  64 frames, a stub heavy serial leaf (or a sidechained/armed-routed track, which is forced
  serial). Expect the render threads to join and late blocks ≤ 1 %. Must fail on the current
  code.
- Keep "playing 16 tracks in real time, one thread keeps up: render threads stay parked"
  green, so light projects still run on one thread.
- Live check: demo21 at 48 kHz / 64 frames for 2 minutes, compared with the numbers above.

## Risks & Considerations

- Joining earlier costs CPU (cores wake from idle at low clocks). Keep the budget and the hold
  tuned, and measure with `scripts/bench-engine.sh` (rt scenarios).
- Overruns caused by plug-ins alone (e.g. `kontakt_problem.nota`, 4 × Kontakt ≈ 56 % of a core
  at 48 kHz / 64 frames, no eligible leaves) aren't helped by this. That needs plug-in leaves on
  the pool, a separate follow-up of #436.
- The two existing "overloaded" RenderPoolTests checks already fail on this Mac on `develop`.
  The new guard should be tuned so it's stable on CI runners.

## Open Questions

- [NEEDS CLARIFICATION: does demo21 drop out at 128 frames? If not, that confirms a tail/capacity problem rather than a defect in Consort's DSP.]
- [NEEDS CLARIFICATION: Consort's own per-block cost over a loop (telemetry `ScCpu`). Does it plateau once voices fill, or keep growing? A steady climb would point at a voice/arp leak instead.]
