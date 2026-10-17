# Bug Assessment: a tempo set over MCP never reaches the transport bar, and the stale value is saved

- **Slug**: mcp-tempo-ui-sync
- **Created**: 2026-10-17
- **Source**: pasted text (user observation, Nota 0.55.1 on Ubuntu 26.04)
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

> yes, the tempo is stating 121 BPM in the UI, so it seems changes from the API don't update
> the field

The reporter drives the tempo from a hardware knob through MCP `set_tempo`. After a series of
`set_tempo` calls (140 → 90 → 140 → 110), the engine played at 110 BPM while the transport bar
still showed 121.

## Symptom

`set_tempo` over MCP changes the engine's tempo, but `TransportViewModel.Bpm` keeps its old
value. So the BPM field shows the wrong tempo. Worse, everything that reads the view-model gets
the stale tempo: saving, autosave/crash recovery, and the export dialog. Expected: a tempo set
from MCP shows in the transport bar and is what gets saved, just like a tempo typed into the
field.

## Reproduction

1. Open a project at 120 BPM with the MCP server enabled.
2. Call `set_tempo` with `bpm: 90`.
3. `get_transport` reports `bpm: 90`, but the transport bar still shows 120.
4. Save the project and reopen it: it opens at 120. [NEEDS CLARIFICATION: the save step is
   inferred from `TransportSnapshot()` and not yet run end-to-end; it will be verified in
   `/speckit-bug-test`.]

## Suspected Code Paths

- `src/managed/Nota.Mcp/Tools/TransportTools.cs:33`: `SetTempo` calls `E.SetBpm` directly,
  bypassing `TransportViewModel`.
- `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs:188` (`Tick`): already
  follows the engine for state that MCP can change. Line 200 says "The loop record mode can
  also be set from outside the UI (MCP): follow the engine". It doesn't do this for `Bpm`.
- `src/managed/Nota.App/MainWindow.Recovery.cs:26` (`TransportSnapshot`) and
  `src/managed/Nota.App/MainWindow.Files.cs:63` (export): read `_vm.Transport.Bpm`, so they save
  and export the stale tempo.
- `src/managed/Nota.App/MainWindow.Remote.cs:161`: the Remote's `SetBpm` goes through
  `vm.Transport.Bpm`, so it doesn't have this bug.

## Root Cause Hypothesis

Two writers set the tempo, and only one goes through the view-model. Nothing reconciles them:
`Tick()` follows the engine for position, play state and loop record mode, but not for tempo.
`OnBpmChanged` writes to the engine but never reads from it. Confidence: high. The divergence
was observed directly (engine 110, field 121).

## Proposed Remediation

**Preferred**: follow the engine's tempo in `TransportViewModel.Tick()`, in the same way as the
existing `LoopRecordReplace` line. Read `_engine.Bpm`, round it to the field's 2 decimal places,
and assign `Bpm` only when it differs. The assignment raises `PropertyChanged`, so the BPM field
updates through the existing handler. The `OnBpmChanged` echo then calls `SetBpm` with the value
the engine already has, which changes nothing. `Tick()` runs on the UI clock, so this picks up
any out-of-band tempo source, not just MCP.

**Alternatives**:
- Route MCP `set_tempo` through the view-model, as the Remote does. `Nota.Mcp` is Avalonia-free
  and only sees `IAudioEngine`, so it would need a new port into the presentation layer. It
  also wouldn't help other out-of-band writers.

**Files likely to change**:
- `src/managed/Nota.Presentation/ViewModels/TransportViewModel.cs`
- `CHANGELOG.md`

**Tests to add or update**:
- The smoke test drives the engine, not the view-model. Verify by hand: `set_tempo` over MCP
  updates the BPM field within a frame, and a saved and reopened project keeps the MCP-set
  tempo. Also check that dragging the BPM field still works (no fight with the follow line
  during a drag).

## Risks & Considerations

- Fighting the user mid-drag: `OnBpmChanged` sets the engine synchronously, so by the next
  `Tick` the engine already matches the field. Compare with a small tolerance (rounded to 2
  decimal places) so float/decimal round-trips never cause a correction.
- Interaction with `tempo-unwarped-redraw`: once that fix is in, a tempo that arrives through
  `Tick` also raises `Bpm`'s `PropertyChanged`, so the arrangement redraws as well. The MCP path
  already refreshes, and the coalesced refresh makes the extra one cheap.

## Related (out of scope)

The same class of bug probably affects other transport settings that MCP can change:
`set_time_signature`, `set_metronome`, `set_master_volume` and `set_loop` also call the engine
directly. Time signature, metronome and master volume have no engine getters on `IAudioEngine`
(no `TimeSigNumerator`/`Metronome`/`MasterVolume` properties), so following them would need
new C ABI reads. The loop already has `SyncLoop()`. These are left for a separate report, and
this fix stays one logical change.

## Open Questions

- None.
