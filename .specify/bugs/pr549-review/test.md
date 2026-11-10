# Bug Verification: PR #549 articulation review

- **Slug**: pr549-review (reused from the assessment/fix context)
- **Tested**: 2026-11-09
- **Assessment**: ./assessment.md
- **Fix**: ./fix.md
- **Result**: partial — all seven functional findings verified on Windows; Linux performance gate pending

## Summary

The final `scripts/build-win.ps1` run exited zero with the latest code, including the no-map/no-offset specialization and default/explicit MIDI channel alias regression. Native and managed builds, scanner self-test, full engine/application smoke suite, and large-folder plugin scan check all passed. No functional regression remains in the automated reproductions listed in the assessment.

The Linux instruction-count gate cannot run on this Windows host: WSL has no installed distribution and cannot start without virtualization. Performance optimizations are implemented, but their CI result is not yet known. Validation was completed before committing or pushing these fixes.

## Checks performed

| Check | Command / action | Result | Evidence |
|---|---|---|---|
| Focused articulation suite | `dotnet run --project tests/Nota.SmokeTest -c Release -- --articulations` | pass | 412 passing checks, zero failures; `articulations.log`. |
| Final native/managed build and regression suite | `scripts/build-win.ps1` in VS 2022 Developer PowerShell | pass, exit 0 | `build-win-verified.log`; `SCANNER SELFTEST PASSED`, `SMOKE TEST PASSED`, `SCAN SCALE PASSED`. |
| Native scheduler and Session delivery probes | Included in the final full smoke suite via `ArticulationSelfTest` | pass | Returned zero; checks through 35 cover the seven findings and added edge cases. |
| Source whitespace | `git diff --check` | pass | No whitespace errors. |
| Linux performance gate | `scripts/perf-gate.sh` | not-run | Requires Linux/Valgrind; WSL cannot run here. |
| Manual hosted-library / UI audition | Kontakt/sample library and map-manager window | not-run | Automated tests exercise scheduling, instrument delivery and manager data flow; no manual UI claim. |

## Reproduction coverage

1. **Chased switch release:** native checks 26 and 34 deliver the release exactly 960 samples after the chase at 48 kHz, across 512-frame and one-frame segments. Check 35 also verifies a later-block release through Session rendering.
2. **Short-loop offsets:** native checks 29/30 and C ABI onset tests preserve +1500 ms across both 500 ms loops and 31.25 ms loops (48 passes). Check 33 preserves a release several passes after its source note.
3. **Launch restoration:** checks 25/35 restore a switch whose lead precedes launch, including a real legato launch request through `SessionPlayer::maybeApply` and `renderSessionSlotRaw`.
4. **Expression timing:** check 32 captures note-on and opening pressure at the same sample, with the initial curve value, for both positive and negative combined track/articulation offsets.
5. **Project map selection:** the manager smoke test saves/applies a newer library map, undoes it on the tracks, reopens the catalog, and saves a small edit. Both tracks retain their restored offsets.
6. **Overlapping clips:** check 27 preserves A → B → A latching changes across separate clips rather than discarding the second A.
7. **Shared momentary holds:** checks 28/31 prevent early releases for overlapping notes and different articulations in separate clips. Default channel 0 and explicit channel 1 are tested as the same physical MIDI channel.

## Output excerpts

```text
ARTICULATIONS PASSED
[PASS] a +1500 ms offset survives 48 passes of a 31.25 ms Session loop (72000 samples, want 72000)
[PASS] saving a small edit after undo preserves the restored project offsets on every track
[PASS] ... (failed check: 0)
SCANNER SELFTEST PASSED
SMOKE TEST PASSED
SCAN SCALE PASSED
```

The first full attempt exposed a stack overflow in the newly added test harness. Moving its local engine to the heap corrected it; the final full run above passes. Local logs are ignored by the report directory's `.gitignore`.

## Residual risks and recommendation

The seven functional fixes are verified by automated reproductions on Windows. Linux/macOS were not built locally, and the remote performance gate had not evaluated these changes at verification time. Keep performance task R1 open until Linux CI confirms the instruction-count limits. Existing deferred feature/convergence tasks are outside this review repair and remain recorded in the feature task list. The user subsequently authorized separate fix commits on PR #549; these results apply to their combined source tree.
