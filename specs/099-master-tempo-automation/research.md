# Research: Tempo as master-track automation

Findings from the code on `develop` at `45e7281b` (#568 merged as `43aa3b43`).

## What exists today

- **Master track.** The engine already models the master as a `Track` (`kMasterTrackId =
  1000000`, `Graph::masterTrack`) holding the master device chain; `findTrackAuthoring`
  resolves it, so `nota_track_add_automation_lane` / `set_points` already reach it. But
  `Engine::applyAutomation` iterates only `g->tracks`, so lanes on the master track never play.
- **Master volume automation** is a separate graph-level lane (`Graph::masterVolume`,
  `nota_engine_master_volume_automation_*`, project field `masterVolumeAutomation`, v7).
- **Master pan** does not exist anywhere (engine, mixer, project).
- **Tempo map (#568).** `Graph::tempoMap` (immutable `TempoMap`, compiled on the authoring
  thread by adaptive Simpson into beat→seconds nodes; render only searches). Points are
  `NotaTempoPoint {beat, bpm, shape 0 step/1 linear/2 Bezier, x1..y2, incoming_bpm}`, first
  point at beat 0, 1–999 BPM. `Engine::setTempoPoints` publishes a new graph and reprojects
  unwarped clips and takes so they keep their physical time. Transport, MIDI, warp,
  metronome, master devices, freeze, bounce and export already consume the map.
- **Tempo UI (#568).** `ArrangementView.Tempo.cs` (lane row, point/handle editing, marquee,
  Ctrl-drag `TempoMapping.MapBar`), axis helpers (`TempoSeconds`, `XToBeat`, …) used across
  the Arrangement, `Nota.Application/Editing/TempoMapping.cs`, `TempoPoint.cs`,
  `ShortcutLayout` "tempo.*" group, `tests/Nota.TempoUiTest`.
- **Automation curve.** `AutomationPoint {beat, value, curve∈[-1,1]}`; segment shape
  `expm1(k·t)/expm1(k)`, `k = −curve·8` (`AutomationLane::shape`, mirrored in
  `Nota.Application.Editing.AutomationCurve`). Two points at the same beat = a jump.
- **Persistence.** Format v33; `tempoPoints` (v33, #568) and `masterVolumeAutomation` at top
  level; `MasterDto` has devices but no automation or pan.

## Decisions

### R1 — The master Tempo lane is the single source of truth; the engine derives the map

- **Decision**: New `AutomationTarget::Tempo = 6`, valid only on the master track. Its points
  live in `masterTrack->automation` like any lane. The engine derives `Graph::tempoMap` from
  that lane in one authoring-thread chokepoint (`deriveTempoMap(Graph&)`), run from
  `publish` / `publishRaw` whenever the master Tempo lane differs from the points the current
  map was compiled from. The derivation also does the unwarped-clip reprojection that
  `setTempoPoints` does today.
- **Rationale**: Every existing automation path (set points, live drag, copy/paste, MCP)
  then changes tempo for free, and undo/redo restores lane + map + clip positions together
  because they are one snapshot.
- **Exception (found in implementation)**: section move / duplicate / delete and insert time
  skip the Tempo lane. Those edits place clips and takes on the old map in the same snapshot;
  moving the ruler under them at the same time broke take/comp sample anchoring (the #568
  `TempoMapCopyTakeTests` caught it). The map stayed put under those edits in #568 too.
- **Alternatives**: Managed side converts the lane to `nota_tempo_set_points` after each
  edit — rejected: two sources of truth, every engine-side lane edit (sections, insert time)
  would need a managed echo, undo could split them.

### R2 — Tempo map segments use the automation curve exactly

- **Decision**: `TempoMap` gets one segment shape: the automation curve (`AutomationLane::shape`
  with the point's `curve`). Points come from the lane: value = BPM clamped 20–300; a
  same-beat pair becomes one map point with `incomingBpm` (left value) and `bpm` (right
  value). If the lane's first point is after beat 0, a map point at 0 holds its value.
  Step / linear / Bezier shapes and the `NotaTempoPoint` ABI struct are removed (unreleased).
- **Rationale**: What is drawn is exactly what plays (SC-004); one shape function shared with
  every other lane.

### R3 — Migration of #568 projects happens in the managed loader

- **Decision**: Migration step v33→v34 converts `tempoPoints` into a master-track lane
  `{target: "tempo"}`: step → value pair at one beat; linear → curve 0; Bezier → split into
  sub-segments (adaptive, ≤ 64 per segment) each fitted with the closest automation curve
  until the integrated beat→seconds error of the segment is < 0.25 ms; values clamped to
  20–300 and a `tempoClamped` flag set so the app shows the one-time notice (FR-008a).
  `incoming_bpm` anchors become same-beat pairs. Pure C# (`TempoMigration` in
  `Nota.Infrastructure/Persistence`), unit-tested in the smoke test against an independent
  integration of the old curve.
- **Alternatives**: Keep the old shapes in the engine for old projects — rejected: two tempo
  models forever, and the old lane UI would be needed to edit them.

### R4 — Master lanes play like track lanes; Tempo is skipped there

- **Decision**: `applyAutomation` also walks `g->masterTrack->automation`: DeviceParam,
  PluginParam, MidiDeviceParam (none on master), nested rack lanes, Pan → master pan atomic.
  Volume on the master keeps using the existing graph-level `masterVolume` lane (UI shows it
  as the "Volume" target; no data change). Tempo lanes are skipped in `applyAutomation` —
  they act through the map.
- **Rationale**: Minimum engine change; master device automation follows the same rules as
  track device automation (device removal drops its lanes — reuse the track logic, which
  already resolves through `findTrackAuthoring`).

### R5 — Master pan is a new master stage

- **Decision**: A master pan stage after the master device chain and before the master
  fader, using the project pan law (`Graph::panLaw`); stored on the master `Track`'s pan, an
  atomic read in render; `nota_engine_set_master_pan` / `nota_engine_master_pan`; the Mixer
  master strip gets a pan knob like track strips; `MasterDto.Pan` persisted (absent = 0).
- **Rationale**: A target needs a parameter the musician can also set by hand.

### R6 — Tempo lane range is view state

- **Decision**: `tempoLaneMin` / `tempoLaneMax` in the bundle's `track-view.json` sidecar,
  next to the track and master row heights (absent = default per FR-003a). Not engine data,
  not an undo step.

### R7 — Transport tempo field while automated

- **Decision**: `IAudioEngine.IsTempoAutomated` (master Tempo lane has points). The transport
  VM polls it with the existing engine follow tick; when true the field is read-only and
  shows `TempoAt(playhead beat)`; `SetBpm` is ignored by the engine while a Tempo lane
  exists (MCP `set_tempo` returns an error naming the master Tempo lane). Tap tempo and MIDI
  Learn on the field do nothing while it is read-only.

### R8 — What #568 code is removed vs kept

- **Removed**: the tempo lane row and its gestures (`TempoLane`, `BuildTempoRow`, marquee,
  handles, Ctrl-drag mapping and marker insertion), `TempoMapping.cs`, the "tempo.*"
  shortcut group and strings, `tests/Nota.TempoUiTest` (its axis/waveform checks move to the
  smoke/UI harness where still relevant), `nota_tempo_set_points`, `nota_tempo_point_count`,
  `nota_tempo_get_point`, `nota_tempo_preview_*`, `ITempoPreview`, `TempoPoint` (except the
  migration DTO).
- **Kept**: `TempoMap` (reshaped per R2), all render/transport/warp/freeze/bounce/export
  consumers, `nota_tempo_beat_to_seconds` / `seconds_to_beat` / `bpm_at`, the time-linear
  axis helpers (moved out of `ArrangementView.Tempo.cs` into `ArrangementView.TimeAxis.cs`),
  the #568 correctness and rendering smoke tests (re-pointed at lane-driven maps).

### R9 — MCP

- **Decision**: `AutomationTools` target names gain `tempo` (master only) and accept the
  master track id; `get_overview` already exposes it — verify, else add `masterTrackId`.
  `set_tempo` errors while Tempo is automated.

### R10 — Delivery in pull requests (constitution V)

- **Decision**: One spec, two pull requests, both `Refs #93`, the second `Closes #93`:
  1. Tempo as master automation: master target picker (Volume/Tempo), engine derivation,
     removal of the #568 lane and Ctrl-drag mapping, migration v34, transport field, MCP.
  2. Master Pan + master device-parameter targets (R4 pan/devices, R5).
- **Rationale**: Each PR is one logical change and independently testable; PR 1 alone fixes
  the reported problem.
