# Bug Verification: Rack path alignment

- **Slug**: issue-1084
- **Tested**: 2026-11-25
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial

The native Rack impulse test passed for parallel and dry/wet paths at 32 and 257 frames. Dynamic latency, bypass, nested racks and desktop capture remain unverified; the performance comparison is in progress.

## CI follow-up (2026-11-26)

Windows CI exposed a stack overflow in the expanded native self-test: its RackDevice fixtures contain large inline block buffers. The fixtures now live on the heap. The same C ABI self-test passes in a native pthread explicitly limited to a 1 MiB stack, matching the Windows default stack budget. The impulse, PDC-off and fork-history assertions are unchanged.
