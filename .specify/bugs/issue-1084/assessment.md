# Bug Assessment: Rack parallel latency

- **Slug**: issue-1084
- **Created**: 2026-11-25
- **Source**: https://github.com/nota-daw/nota/issues/1084 (github.com, allowlisted)
- **Verdict**: valid
- **Severity**: high

## Report and symptom

An Audio Effect Rack reports the longest latency while summing faster chains and its dry path without alignment. Two paths with a 96-sample difference produce separate impulses.

## Reproduction

Use parallel empty and 2 ms lookahead Compressor chains, then compare their impulse with the wet/dry case. Reporter reproduced in native components; full-app testing remains pending.

## Suspected code and root cause

`RackDevice::process` sums chain output and mixes the original input directly; `latencySamples` reports the maximum or series latency. No matching delay lines exist in the processing path. Confidence: high.

## Proposed remediation

Preallocate real-time-safe delay storage and align each active parallel chain to the longest path, and dry to wet latency, when rack compensation is enabled. Handle series, changing chains, bypass, sample rate and latency changes consistently; never allocate or lock in process. Test impulses across blocks and topology changes, and benchmark the render path.

**Files likely to change**: `src/native/nota.engine/src/devices/racks/RackDevice.*`, rack state and smoke tests; changelog.

**Risks**: Audio-thread allocation, stale delay tails and dynamic latency updates; no assumption of unlimited plug-in latency.

## Open questions

None.
