# Bug Verification: The render pool runs away to every thread under a mild overload

- **Slug**: render-pool-sizing
- **Tested**: 2026-11-15
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: verified (macOS, M4 Max)

| Check | Result |
|-------|--------|
| `--perf` "overloaded", 6 interleaved rounds (load 2.3–4.6): b80b2cad / branch / branch + fix | late 1–5 / 143–183 / 0–2 of 4125; bursts 1–3 / 4–7 / 0–2 |
| Native harness (same session, 9 helpers): steady | fix 0.5–0.7 % late vs b80b2cad 0.6–1.0 %, worker CPU 8.0 s vs develop 10.7 s |
| Native harness: +50 % tracks added while the pool runs | fix 0.3–0.7 % vs b80b2cad 0.2–0.4 % (caught up) |
| Full smoke test | passed |

## Review follow-up (2026-11-16): serial room

| Check | Result |
|-------|--------|
| `--perf`, 4 interleaved rounds, before (cd2aed95) / after (83e3e603), load 4–7 | "overloaded" late 6/12/10/2 vs 4/1/7/4 of 4125, bursts ≤ 4 vs ≤ 3; "four indivisible VST3 jobs" misses one round each (8/100 before, 29/100 after) — the machine's load, not the change |
| Full smoke test | passed (one later run missed the timing guards under the same load) |
