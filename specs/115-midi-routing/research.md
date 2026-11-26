# Research: MIDI routing (spec 115)

Every decision below was taken against the code on `develop` at 5989d60a. Paths are relative
to the repository root.

## R1 — Where an External MIDI track taps its stream

- **Decision**: External MIDI is a built-in `Instrument` (next free kind, currently 19 — grep
  before picking). It plugs into the existing instrument call path with
  `takesTimedEvents() == true`, `handlesSustain() == true` and `takesArticulations() == true`,
  the way `PluginInstrument` does. `noteOn / noteOff / noteOffVelocity / controller /
  noteExpression / programChange / keyswitchOn/Off` become raw MIDI bytes at `eventOffset`.
  `render()` writes silence.
- **Rationale**: the instrument already receives exactly the stream FR-008 asks for: post
  track MIDI effects, from clips (Arrangement and Session) and live input, in order and at
  sample offsets. It also gets the MIDI In layering (spec 093) and articulations (spec 085)
  for free. Replace, delete-deletes-track, undo, presets, freeze exclusion and persistence
  follow the instrument rules the clarification (A2) asked for.
- **Alternatives considered**: tapping `blockMidi` in `computeBlockMidi` (it has no
  controllers, expression or articulations, and is skipped for silent tracks), or a
  track-level output flag beside the instrument (rejected in clarification Q1).

## R2 — Getting bytes off the audio thread (AR-6)

- **Decision**: each External MIDI instrument owns a pre-allocated `SpscRingBuffer` of
  `OutMidiEv { uint64 dueNs; uint8 len; uint8 bytes[3]; }` (capacity 4096). The audio thread
  pushes and never blocks; on overflow it drops the event and counts it (a relaxed atomic the
  UI can show). A new **MIDI out thread** (`engine/MidiOutService`) drains all rings. Their
  list is published like the graph snapshot: an immutable vector swapped atomically and freed
  on the message thread (AR-5).
- **Timing**: `dueNs` = the host time of the block's first frame, plus `off / sampleRate`,
  plus the device's reported output latency, so the external synth lines up with Nota's own
  audio. On macOS the event goes to CoreMIDI at once with that timestamp (CoreMIDI schedules
  it). On RtMidi (WinMM, ALSA) the thread waits until `dueNs − 1 ms` on a timed wait and sends.
  The track's MIDI offset (ms) applies as it does to any instrument.
- **Rationale**: tracks render in parallel on pool workers, so one ring per instrument keeps
  every ring single-producer. A dedicated thread keeps message-thread stalls (layout, GC)
  out of the timing (SC-005).
- **Alternatives**: draining on the message thread (jitter of a UI frame); one MPSC queue
  (needs CAS loops on the audio thread for no gain).

## R3 — Sharing a physical output with controller feedback

- **Decision**: `MidiOutService` becomes the only owner of `MidiOutput` objects. The
  message-thread `Engine::midiSend` (controller LEDs, SysEx — spec 051) stops opening ports
  itself and pushes into the service's own SPSC byte queue, with SysEx carried as a length
  plus bytes. `midiOutputs_` moves into the service.
- **Rationale**: WinMM opens an output port exclusively. A second handle opened for routing
  would fail whenever the controller's paired output is also a routing destination.
- **Alternatives**: two handles (fails on Windows); a lock around `midiOutputs_` (the audio
  thread never touches it, but the sender would contend with the UI).

## R4 — Owned-note books, unplug and reconnect (FR-010, FR-011)

- **Decision**: the MIDI out thread keeps, per route (the External MIDI instrument's
  `Instrument::uid`), a book of the notes on (16 × 128 bits) and the held pedals per channel
  it has *actually sent*, and the output uid they went to. On a route change (new uid,
  channel or mode, delivered as a config event through the same ring so it is ordered with
  the notes), on removal, on stop or mute (the instrument's `allNotesOff`), the thread sends
  the book's releases to the *old* output and clears the book. When a send fails or
  `MidiInput::portsGeneration()` shows the output gone, the route is marked unavailable and
  its book is kept. When the same uid comes back, the thread sends the book's Note Offs and
  pedal releases, then CC 123 and CC 64 = 0 on each channel in the book, skipping channels
  where another route that stayed available holds notes on that output (clarification Q4).
  Only then does it resume.
- **Rationale**: the book lives where the bytes leave, so it is exact even when the ring
  dropped events or the route changed mid-block.

## R5 — Keep channels (clarification Q3, amended)

- **Finding**: post-MIDI-effect events (`MidiEv`, `devices/MidiDevice.h`) and clip notes
  carry no MIDI channel, and the instrument API addresses notes by pitch. An event's played
  channel is therefore gone before any instrument sees it.
- **Decision**: **Keep channels** = MPE lower zone output. Each new note takes the
  least-recently freed member channel 2–16. Its pitch bend, pressure and slide (CC 74) go on
  that channel. Instrument-wide controllers, the wheel and the pedal go on channel 1. On
  entering the mode, and after a reset, the thread sends the MPE Configuration Message (RPN 6)
  for 15 member channels. If more than 15 notes sound at once, notes share channels
  round-robin. The spec's FR-007 is amended to say this. Per-played-channel output for
  multitimbral modules is a follow-up after #744.
- **Alternatives**: carrying the source channel through `MidiEv` and every MIDI effect. Out
  of the issue's scope, and clips would still have no channels.

## R6 — Input routes: per-port channel masks (FR-002 … FR-005)

- **Decision**: replace `Track::midiInputSource_` / `midiInputChannel_` with
  `std::atomic<bool> midiAllInputs_` plus `std::atomic<uint16_t> midiInMask_[MidiInput::kMaxSources]`,
  where bit *c* means channel *c+1*. `acceptsMidiFrom(source, ch)` becomes
  `all || (source >= 0 && mask[source] >> ch & 1)`. Non-hardware events (`source == -1`:
  computer keyboard, Remote, gamepad) pass only All inputs, as today. The legacy pair maps
  onto it: one device and channel 0 is mask `0xFFFF` on that source, channel *n* is bit
  *n−1*. The take lanes (`midiLaneSrc_/Ch_`, `rtLaneSrc_/Ch_`) get the same mask arrays, so
  monitoring and recording share one test (FR-004). `laneTakes` and `laneStartCtl` use it.
- **Saving by identity**: `Track::midiInputUid` becomes a fixed list of `{uid, mask}` on the
  authoring track (message thread). The engine resolves uids to source indices whenever ports
  change (`portsGeneration`) and republishes the masks. An unresolved uid contributes nothing
  and is reported missing (FR-006).
- **Release on removal (FR-005)**: `LiveHeld` already records `source[128]` and
  `channel[128]` per held pitch. A per-track `std::atomic<uint32_t> inputGen_` is bumped on
  every filter change. When the audio thread sees a new generation, it walks the held bits
  once and ends the notes whose owner no longer passes, plus that owner's held controllers
  (the `liveCtlIn_` state), exactly as the MIDI In switch does (spec 093).
- **Overlapping owners**: a pitch keeps up to 4 `(source, channel)` owners in fixed arrays
  in `LiveHeld` (no allocation). It ends when the last owner releases it or drops out of the
  filter (spec edge case, amended).
- **Alternatives**: a list of filter entries scanned per event (unbounded on the audio
  thread); a 16×16 bit matrix in one 256-bit atomic (not lock-free on every target).

## R7 — Ports beyond the limit (#1011, first task)

- **Decision**: `MidiInput` (RtMidi `MidiInput_rtmidi.cpp`, CoreMIDI `MidiInput.mm`) stops
  clamping indices to `kMaxSources − 1`. The first `kMaxSources` enabled ports are connected
  as today. Any further port is **not connected** and is listed with
  `PortInfo::overLimit = true`. Settings → Inputs shows it as "Not connected — Nota listens to
  16 inputs; switch one off", and routing pickers show it as unavailable. `sourceUids()` never
  repeats an index.
- **Rationale**: a shared index cross-routes notes (#1011) and shares a `MidiParser`, which
  mixes partial messages (#1019's mechanism). Not connecting is the only option that routes
  nothing wrongly. With 16 ports already open, the cost is low.
- **Verification**: a native self-test (`nota_engine_midi_index_selftest`, like
  `nota_engine_midi_ctl_selftest`) feeds the index assignment 17 simulated ports and checks
  that the indices are distinct and the 17th is reported over the limit. The smoke test calls
  it.

## R8 — Output identity, ambiguity and roles (FR-006, FR-009, FR-013 … FR-017)

- **Decision**: a saved output is `{uid, name}`. On load and on every `portsGeneration`, the
  managed `MidiRouteResolver` (Nota.Application) matches it: an exact uid match is
  **available**; no uid match is **missing**, even when a port with the same name exists
  (FR-006); two connected outputs with the same uid (RtMidi uids are names, and Windows can
  list twins) is **ambiguous**. The engine only ever gets a resolved uid or the empty string
  ("send nothing").
- **Roles** live in the project document (`MidiOutputRoles`). A destination stores either a
  port or a role id. Rebinding a role is a managed operation: with transport stopped, compute
  the affected tracks, show them, and on confirm push the new uid to each External MIDI
  instrument in one undo step. R4 releases the old output's notes.
- **Rationale**: identity policy and the "transport stopped" rule are UI and project
  concerns. The engine stays a simple "send to this uid" (AR-7 boundary stays small).

## R9 — The MIDI routes panel (FR-018 … FR-021)

- **Decision**: `MidiRoutesQuery` (Nota.Application) builds `MidiRouteRow`s from the project
  document plus `MidiRouteResolver` availability. `MidiRoutesPanel` (Nota.App) is a side
  panel docked right in `MixerView`, toggled from a Mixer header button and from View → MIDI
  routes. It refreshes on project-changed and ports-changed events; it never writes. Visuals
  follow `DESIGN.md` and the nota-design skill: a flat grey shell, sentence case, track colour
  chips, and the "unavailable" state in the same style as the missing-plugin badge.

## R10 — Persistence and format (FR-012, FR-017, FR-024)

- **Decision**: project format **36**. `TrackDto.MidiInputs: List<MidiInputRouteDto>?`
  (absent means the old `MidiInputUid` / `MidiInputChannel` apply, so v35 files load
  unchanged, SC-008). External MIDI is `InstrumentDto { Kind = 19 }` with a new
  `ExternalMidi: ExternalMidiDto` (port uid and name or role id, `ChannelMode`).
  `ProjectDocument.MidiOutputRoles: List<MidiOutputRoleDto>?`. An older Nota already refuses
  `ProjectFormat > CurrentFormatVersion` with a message (`ProjectService.cs`, the `formatVersion > CurrentFormatVersion` check on load), which meets
  FR-024.

## R11 — MCP (FR-023)

- **Decision**: `set_track_midi_input` gains an optional `inputs` array
  (`[{uid, channels:[…]}]`); the old `uid` and `channel` arguments keep working.
  `get_track_midi_input` returns the list. New tools: `list_midi_outputs`,
  `set_external_midi` (trackId, `outputUid` | `role`, `channel` 1–16 | `"keep"`),
  `get_external_midi`, `list_midi_output_roles`, `create_midi_output_role`,
  `rebind_midi_output_role` (refused while playing), `delete_midi_output_role`,
  `get_midi_routes` (the panel's rows). Exact shapes are in
  [contracts/mcp-tools.md](contracts/mcp-tools.md).

## R12 — Testing

- **Decision**: the smoke test gains `MidiRoutingTests.cs`, built on the existing test ports
  (`nota_midi_test_port_add` for inputs, `test:<name>` outputs whose sent bytes are captured).
  Two small test-only ABI additions: `nota_midi_test_port_remove` (unplug) and
  `nota_midi_test_output_read` (drain captured bytes with timestamps). Every acceptance
  scenario that does not need real hardware runs there (see quickstart.md). The rest
  (hardware synth, real unplug, WinMM exclusivity) is a manual checklist in quickstart.md.

## As built (implementation notes)

Where the code settled differently from the decisions above:

- **R2 timing:** every platform waits on the MIDI out thread until a message is due (1 ms loop,
  `timeBeginPeriod(1)` on Windows) and sends then; CoreMIDI timestamps are not used. An offline
  render (export, bounce) never reaches a real port — only test ports take it.
- **R2/R4 config:** a destination never changes inside an instance. `setExternalMidi` and a role
  rebind publish a new `ExternalMidi`; the old instance leaving the graph is what releases its
  book (after a 100 ms grace, at once in `pump()`), and undo restores the old instance. No
  `Config` events in the ring. `allNotesOff` (any thread) sets a flag the audio thread turns into
  a flush, so the ring keeps one producer.
- **R3:** `midiSend` calls the service synchronously under its mutex (it returns the real result);
  the service owns every `MidiOutput`.
- **R5:** the MPE Configuration Message (RPN 6, 15 members) goes out before the first event in
  Keep channels mode.
- **R6:** the filter is `MidiInputFilter` (every input on a channel mask, or per-source masks);
  the input generation is unique across all track versions, so a clone or undo is always checked.
  Input changes are a new track version — one undo step (FR-022). The legacy index-only pick is
  stored as the pseudo uid `source:N`.
- **R8:** the engine keeps the saved uid (and name) itself; it treats a uid listed twice as not
  present, so ambiguity needs no managed hold. `MidiRouteResolver` serves the UI and MCP.
- **R9:** the panel's toggle is in the Mixer footer's Show row (the Mixer has no header bar), plus
  View ▸ MIDI routes.
- **Roles** live in the engine's graph (`Graph::midiRoles`) so a rebind and its tracks are one undo
  step; the project document saves them as `MidiOutputRoles`.
