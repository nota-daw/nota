# Bug Assessment: The render pool runs away to every thread under a mild overload

- **Slug**: render-pool-sizing
- **Created**: 2026-11-15
- **Source**: the smoke test's `overloaded` render-pool guards failing on develop (found while verifying PR #619)
- **Verdict**: valid
- **Severity**: medium (late blocks at small buffers under load)

## Symptom

`RenderPoolTests` "overloaded" (a session one thread renders in ~1.3 periods of a 64-frame
buffer) fails on develop: 143–183 of 4125 blocks late (budget ≤ 1 %), bursts of 4–7 within
50 ms. An engine from 2026-10-31 (b80b2cad) passes the same check run alongside it: 1–5 late.

## Root cause

With a device, the pool sizes its helpers from `max(leafWorkNs_, parWorkNs_)`. `parWorkNs_`
is the pool's summed task time, which over-reads the real cost — and the more threads run
the tasks, the more it over-reads (cold cores, shared caches). Sizing from it is a positive
feedback loop: a noisy block lifts it, more helpers join, tasks cost more, it rises again,
until the pool runs on 5–7 threads (trace: single-thread cost 1350 µs, `parWorkNs_` 3000+ µs)
and their wake-ups make late blocks. The mean block got faster, the tail worse (p99 ~2.7 ms on
a 1.33 ms period; worker CPU doubled, 10.7 s vs 5.3 s over 6 s).

Bisect (each candidate measured interleaved with the good build, since machine load moves
the numbers) names 34c6dc43 — an instrument-envelope change the test never runs. It only
shifted timing enough to tip the loop; the loop itself predates it.

## Remediation

Size from the single-thread cost; grow the sizing only on sustained overruns of the parallel
phase, never past the old (over-reading) bound; ease back while phases are short.
