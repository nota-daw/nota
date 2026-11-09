---

description: "Tasks: Multi-Channel Input and Simultaneous Multi-Track Recording (#353, #528)"
---

# Tasks: Multi-Channel Input and Simultaneous Multi-Track Recording

**Input**: Design documents from `specs/086-multi-input-recording/`

**Prerequisites**: plan.md, spec.md, research.md, data-model.md, contracts/, quickstart.md

**Tests**: Required — constitution IV and SC-001…SC-007 need smoke coverage. New tests live in
`tests/Nota.SmokeTest/MultiInputRecordTests.cs`, `MultiInputSessionTests.cs`,
`MidiInputFilterTests.cs` and are registered in `tests/Nota.SmokeTest/Program.cs` next to
`RetryTakeTests.Run()` (~line 802).

**Organization**: grouped by user story (spec.md). US2 is split into an audio phase and a MIDI
phase because they ship in different PRs (plan.md § Delivery slices).

**Paths**: `E/` = `src/native/nota.engine/src/`, `H` = `src/native/nota.engine/include/nota/nota_engine.h`,
`M/` = `src/managed/`, `T/` = `tests/Nota.SmokeTest/`.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1–US5 from spec.md

---

## Phase 1: Setup

- [X] T001 Add `kMaxInputChannels = 64` with a why-comment to `E/audio_io/AudioInput.h` (the audio_io layer needs it; `E/engine/CaptureRing.h` and `Track.h` include it). `kMaxInternalLanes` / `kMaxMidiTakeLanes` come with T038 / T064
- [X] T002 Create `T/MultiInputRecordTests.cs` (SPDX header, `internal static class … { public static IEnumerable<(bool, string)> Run() }`) and register it in `T/Program.cs` (full run + `--multi-input`); `MultiInputSessionTests.cs` / `MidiInputFilterTests.cs` are created by T052 / T058

---

## Phase 2: Foundational (blocks every story) — PR 1

**Purpose**: the capture layer carries every device channel (research R1, R2). Existing picks
(Ext 1 / Ext 2 / Ext 1/2) behave exactly as before at the end of this phase.

- [X] T003 Change `InputCallback` to `void(const float* interleaved, int32_t numFrames, int32_t numChannels)` and add `virtual int32_t channelCount() const` and `virtual std::string channelName(int32_t i) const` to `AudioInput` in `E/audio_io/AudioInput.h` (comment: names are read on the control thread only)
- [X] T004 [P] CoreAudio: in `E/audio_io/macos/CoreAudioInput.mm` drop the stereo downmix in `inputProc` and pass `scratch` with `impl->channels` (clamped to `kMaxInputChannels`); read per-channel names via `kAudioObjectPropertyElementName` on `kAudioDevicePropertyScopeInput` at start; implement `channelCount/channelName`
- [X] T005 [P] miniaudio: in `E/audio_io/desktop/MiniaudioInput.cpp` set `config.capture.channels = 0` (native), read the opened count from `device.capture.channels`, clamp to 64, pass it to the callback; names return "" (UI shows "Input N")
- [X] T006 [P] ASIO: in `E/audio_io/asio/AsioSession.*` and `E/audio_io/windows/AsioInput.*` create buffers for every input channel the driver offers (up to 64, fewer if `ASIOCreateBuffers` refuses — retry with the count it accepts), convert all of them into one interleaved block per callback, keep `ASIOChannelInfo.name` per input; `asioInputPair` no longer steers capture (keep the field for saved configs)
- [X] T007 Define `CaptureLayoutSnapshot`, the seqlock `CaptureLayout` and the block-framed SPSC `CaptureRing` (record 2^21 floats, monitor 2^18; a 4-float header with layout generation, width, frames, flags) in the new `E/engine/CaptureRing.h`; a capture marker (specs/078) is a marker block; internal resampling keeps a stereo `InputQueue internalQueue_`
- [X] T008 In `E/engine/Engine.h` add `CaptureLayout captureLayout_`, per-thread snapshots (`layoutMsg_`, `layoutInput_`, `layoutAudio_`), a 16-entry layout history for the drain, `recordRing_`, `monitorRing_`, `internalQueue_`, cached device channel names
- [X] T009 Rewrite the input callback in `ensureAudioInput` (`E/engine/Engine_Recording.cpp`): load both layouts once per callback, copy their columns from the device frame into the record ring (when `hwRecordTap_` and the gate are open) and the monitor ring (when `hwMonitorTap_`); count dropped frames as today; no allocation, no lock
- [X] T010 `updateCaptureLayout()` on the message thread in `E/engine/Engine_Recording.cpp`: one layout = union of channels picked by armed / monitoring hardware-source audio tracks plus the hardware take in progress; published (seqlock) only on change and kept in the history; called from `updateMonitorInput()`, the input setters, take start and stop
- [X] T011 Replace `inputChannels_` in `E/engine/Track.h` with atomics `inputFirst_` (0-based) and `inputWidth_` (1|2) — rule from data-model.md: "width 2 ⇒ `first` is even", "0 ≤ first < kMaxInputChannels" — plus `setInput(first,width)`; keep `setInputChannels(c)` mapping 0 → (0,2), 1 → (0,1), 2 → (1,1); delete `foldInputChannels`; copy the pick in `E/engine/Engine.cpp:674` and `E/engine/Engine_Tracks.cpp:3677`
- [X] T012 `pullMonitorInput` in `E/engine/Engine_Recording.cpp`: `monitorHwBuf_` becomes `[frames × layout.count]`; on a monitor-layout generation change drop the backlog and re-prime; `E/engine/Engine_Render.cpp:2082` reads the track's own column(s) (mono → both sides, centred) instead of `foldInputChannels`
- [X] T013 Drain in `drainInputQueue` (`E/engine/Engine_Recording.cpp`) reads wide frames and, for now, extracts the single take's pick column(s) into the existing stereo `audioCaptureBuf_` (mono duplicated) so behaviour is unchanged until Phase 4
- [X] T014 Test hooks: `nota_engine_set_test_input_device(engine, channels)` (fake device, names "Test N") and `nota_take_record_test_n(...)` per contracts/c-abi.md in `H` and `E/abi/nota_engine_audio.cpp`; port `nota_take_record_test` onto the wide ring (2 columns)
- [ ] T015 Run `scripts/build.sh`: every existing recording / monitoring / retry smoke test still passes
  - 2026-11-08: full run green except two render-pool overload timing checks (RenderPoolTests "overloaded … late blocks"), which fail the same way on an untouched 0.59.2 engine on the same machine (load average 8–10: base 220/226 late, 086 154/46). Re-run `--perf` on a quiet machine before the PR.

**Checkpoint**: device channels reach the engine; nothing user-visible changed.

---

## Phase 3: User Story 1 — Pick any hardware input for a track (P1) 🎯 MVP — PR 1

**Goal**: an audio track can record and monitor any mono input or odd-aligned stereo pair.

**Independent Test**: 8-input test device, track on In 3 → clip equals input 3 only (quickstart 1).

### Tests

- [X] T016 [P] [US1] In `T/MultiInputRecordTests.cs`: 8-channel test device; pick (2,1) → clip with both sides equal to column 2 (still stored stereo until Phase 5); pick (2,2) → L = column 2, R = column 3; `set_input(3,2)` → `NOTA_ERR_INVALID_ARG`; `nota_audio_input_channel_count` = 8, names "Test 1…8"

### Implementation

- [X] T017 [US1] Add `nota_audio_input_channel_count`, `nota_audio_input_channel_name`, `nota_audio_input_probe`, `nota_track_set_input`, `nota_track_get_input` (packs `first | (width << 8)`), `nota_track_input_available` to `H` (docs per contracts/c-abi.md) and shims in `E/abi/nota_engine_audio.cpp`; reject width ∉ {1,2} and odd stereo first with `NOTA_ERR_INVALID_ARG`
- [X] T018 [P] [US1] Managed: `record struct InputPick(int First, int Width)` in `M/Nota.Application/Engine/EngineContracts.cs`; `IAudioEngine.SetTrackInput/GetTrackInput/TrackInputAvailable/AudioInputChannelCount/AudioInputChannelName/ProbeAudioInput` in `M/Nota.Application/Engine/IAudioEngine.cs`; P/Invoke in `M/Nota.Infrastructure/Interop/NativeMethods.Tracks.cs` and wrappers in `M/Nota.Infrastructure/Interop/NotaEngine.Tracks.cs`; update every other `IAudioEngine` implementation (test doubles) found by `grep -rn ": IAudioEngine" src tests`
- [X] T019 [US1] Replace the fixed three-entry list in `M/Nota.App/RecordInputPick.cs` with a builder over the device's channels: mono entries "Input N" / device name, pairs "In N/N+1"; `HardwareShort(InputPick)` → "In 3", "In 3/4" or the device name; keep internal-source entries as they are
- [X] T020 [US1] Use the builder everywhere the input is shown or picked: track header menu and box, mixer strip, Session I/O row (`M/Nota.App/SessionView*.cs`), detail panel `M/Nota.App/MainWindow.Detail.cs:115`; > 16 mono inputs → "Mono inputs ▸" / "Stereo pairs ▸" submenus (contracts/mcp-and-ui.md)
  - Every surface uses `RecordInputPick.HardwareEntries`; the track menu groups past 16 inputs under "Mono inputs ▸ / Stereo pairs ▸" (PR 3); combo boxes stay flat (they scroll).
- [X] T021 Localisation keys `record_input_pick.audio_input_mono`, `audio_inputs_stereo`, `in_n`, `in_pair`, `named_input_mono`, `not_available` in `M/Nota.Application/Localization/en.json` (other languages ship outside this repo); the `ext_1/ext_2/audio_input_*_mono/1_2_stereo` keys removed
- [X] T022 [P] [US1] MCP: `list_audio_inputs`, `set_track_input`, `get_track_input` in `M/Nota.Mcp/Tools/MixerTools.cs` (1-based in the tool, per contracts/mcp-and-ui.md); `set_input_channels`/`get_input_channels` descriptions point at them

**Checkpoint**: any input recordable on one track; Ext 1/Ext 2/Ext 1/2 users see "In 1", "In 2", "In 1/2".

---

## Phase 4: User Story 4 — Old projects and missing inputs (P2) — PR 1

**Goal**: old projects open with the same inputs; missing inputs are kept and shown.

**Independent Test**: quickstart 9 (migration) + 7 (unavailable).

### Tests

- [X] T023 Migration test in `T/MultiInputRecordTests.cs` on a captured document edited to look like 0.59.2 / pre-0.58 files: `InputChannels` 1/2/0 → picks (0,1), (1,1), (0,2); save writes `InputChannels` only when it can name the pick; a (2,1) pick saves `InputFirst=3, InputWidth=1`
- [X] T024 [P] [US4] Test: track on (5,1) with a 2-input test device → `nota_track_input_available` = 0, monitoring silent; switching the test device to 8 inputs → available = 1 with no re-pick

### Implementation

- [X] T025 [US4] `TrackModel` in `M/Nota.Infrastructure/Persistence/ProjectModel.cs`: add `InputFirst int?` (1-based) and `InputWidth int?`; load in `M/Nota.Infrastructure/Persistence/ProjectService.cs:~752`: when null derive from `InputChannels` ("0/null → (1,2), 1 → (1,1), 2 → (2,1)"); save at `~117`: write the new pair always and `InputChannels` only when expressible (1/2 → 0, 1/1 → 1, 2/1 → 2)
- [X] T026 Pickers list a pick the device lacks with "— not on this device" (`M/Nota.App/RecordInputPick.cs`); pickers the user opens probe the device once (engine caches the answer until the input device changes), views built on their own never open it

**Checkpoint (PR 1 ready)**: `scripts/build.sh` green; CHANGELOG "Changed: a track's input lists every input of the device" (T072 covers docs).

---

## Phase 5: User Story 2 — Record several audio tracks in one pass (P1) — PR 2

**Goal**: every armed audio track records its own input in one take; mono picks record mono.

**Independent Test**: quickstart 2–7, 10.

### Tests

- [X] T027 [P] [US2] `T/MultiInputRecordTests.cs`: four armed tracks on (0,1),(1,1),(2,2),(4,1) → four clips, equal start beat and frame count, bit-exact to their columns; mono picks give `SampleInfo.Channels == 1`
- [X] T028 [P] [US2] Same input twice → two identical clips; linked group of two audio tracks (#528, `GroupLinkTests` helpers) → two clips
- [ ] T029 [P] [US2] One `Undo` removes all clips of the take, `Redo` brings all back; `RetryTake` mid-take → no clips; stop after retry → one clip per track at the original start beat
  - Done: one Undo / Redo over all lanes. Open: Discard and Retry over a multi-track take needs a live take in the smoke harness.
- [X] T030 [P] [US2] Loop recording over 3 passes with two tracks → each track has 3 take lanes
- [X] T031 [P] [US2] Track on (5,1) + track on (0,1), 2-input device → one clip; `nota_engine_record_skipped` lists the first with reason 1
- [ ] T032 [P] [US2] Internal source: two tracks recording two other tracks' outputs plus one hardware track → three clips, aligned
  - Open: a take is hardware lanes or one internal-source lane for now (T038).
- [ ] T033 [P] [US2] Save the bundle; the mono take's WAV has 1 channel (`BundleContentTests` helpers)
  - Covered by `SampleInfo.Channels == 1` in T027 plus the generic `WriteSampleWav` (writes the buffer's channel count); no bundle round-trip yet.

### Implementation

- [X] T034 [US2] In `E/engine/Engine.h` define `AudioTakeLane` and `AudioTake` exactly as data-model.md (lane: `trackId`, `column`, `width` 1|2, `internalSource`, `buf`, `writePos`, `passes`, `scene` −1, `keepFrom`, `stopAt` −1; take: lanes, `startBeat`, `sampleRate`, `latencyFrames`, loop geometry, retry markers); remove `audioRecordTrackId_`, `audioCaptureBuf_`, `captureWritePos_`, `captureChannels_`, `capturePasses_`, loop-capture per-take fields that move into lanes
  - As built: `CaptureLane` (trackId, first, width, channels, buf, scratch, passes) in `captureLanes_`; cursor, loop geometry, start, clock and latency stay take-wide (hardware lanes share one device clock). Lane 0 = `audioRecordTrackId_`.
- [X] T035 [US2] `startArrangementTake` (`E/engine/Engine_Recording.cpp`): collect **every** armed audio track; a track whose pick is unavailable goes to the skipped list (reason 1); publish + freeze the record layout; build one lane per track (column from the layout); `noteTakeTrack` for each; one latency correction
- [X] T036 [US2] `startAudioRecording` becomes `startAudioTake(lanes)`: shared setup once (flush ring, `hwRecordTap_`, sample rate, `beginCaptureLayout`, transport roll); reserve each lane's `buf` for 30 s and grow in 30 s chunks
  - `startAudioRecordingLanes(ids)`; `startAudioRecording(id)` = one lane (Session).
- [X] T037 [US2] `drainInputQueue`: pop wide frames once, scatter column(s) into each running lane (mono → 1 channel); dropped frames insert silence in every lane; loop-punch cursor wrap and `finishLoopPass` per lane; retry boundary bookkeeping per take
- [ ] T038 [US2] Internal-source lanes: replace `internalRecordSource_` with a fixed `std::atomic<int32_t> internalSources_[kMaxInternalLanes]` and a second `WideQueue` fed in `mixGraph` (`E/engine/Engine_Render.cpp`), two columns per source; drained into its lanes in `drainInputQueue`
  - Open: internal sources still record one lane; armed tracks mixing hardware and internal sources skip the minority with reason 4 (`kSkipMixedSources`).
- [X] T039 [US2] `stopAudioRecording`: within `beginUndoGroup()`/`endUndoGroup()` and the take's `TakePushScope`, materialise every lane (loop passes → take lanes via `materialiseLoopTakes`; otherwise a clip from a `SampleBuffer` with `channels = width`); release the record layout
- [X] T040 [US2] `abortAudioRecording` and `E/engine/Engine_TakeRetry.cpp`: drop every lane; retry restarts all lanes with the same tracks
  - Abort / retry drop every lane (no multi-lane retry test yet, see T029).
- [X] T041 [US2] Live waveform / peaks while recording: `audioRecordPeaks`, `audioRecordLengthBeats`, `audioRecordTrackId` in `E/engine/Engine.h` take a track id (lane lookup); add `nota_engine_recording_tracks` and `nota_engine_record_skipped` to `H` + `E/abi/nota_engine_audio.cpp`; keep `nota_engine_audio_record_track` = first audio lane
  - ABI: `nota_engine_recording_tracks`, `nota_engine_audio_record_length_beats_of`, `nota_engine_audio_record_peaks_of`, `nota_engine_record_skipped`, test hook `nota_take_record_test_armed`.
- [X] T042 [US2] Managed: `IAudioEngine.RecordingTracks()`, `RecordSkipped()`, per-track recording peaks; update the arrangement live-take drawing (`M/Nota.App/ArrangementView*.cs`) to draw a growing take on every recording track
  - Arrangement draws a growing take on every recording track; Session view unchanged (one lane per Session take until Phase 7).
- [X] T043 [US2] Verify 1-channel recorded buffers through clip editor waveform, warp stream, export, bounce and "Slice to…" (fix any path that assumes 2 channels); record findings in a comment on T043 when done
  - Readers of clip samples are channel-aware already (mono imports); grep found only buffer creators that assume stereo.

**Checkpoint (PR 2 ready)**: audio part of #528 fixed; quickstart 2–7, 10 pass.

---

## Phase 6: User Story 5 — Audio I/O page (P3) — PR 3

**Goal**: switch inputs on/off and name them; menus follow; arming several tracks is discoverable.

**Independent Test**: spec Story 5 scenarios 1–8 (manual quickstart 1) + smoke T044.

### Tests

- [X] T044 [P] [US5] Smoke (managed only) in `T/MultiInputRecordTests.cs`: `InputSetupStore` per (system, uid) round-trip; menu builder with inputs 5–8 off lists 1–4 and pairs 1/2, 3/4 only; a name on input 1 shows in the short label; a pick on a switched-off input stays and reads "(switched off)"
  - Pure part in `Nota.Application/InputPickList.cs` + `InputSetups.cs` (the smoke test can't reach Nota.App); plus the engine skip (reason 3) and a settings JSON round trip.

### Implementation

- [X] T045 [P] [US5] `InputSetup` + `InputSetupStore` in `M/Nota.Application/Audio/InputSetup.cs` (new file, SPDX): per `(audioSystem, deviceUid)`: `Enabled[i]` default true, `Name[i]` null, `PairName[k]` null; persisted in the app preferences file next to the audio settings
  - As built: `InputSetup` + static `InputSetups` in `M/Nota.Application/InputSetups.cs`, stored in `Settings.InputSetups` keyed `device:<uid>` / `asio:<driver>`; app side `M/Nota.App/InputSetupAccess.cs`.
- [X] T046 [US5] Menu builder (`M/Nota.App/RecordInputPick.cs`) applies the setup: enabled-only lists, pairs only when both inputs on, display name order "user name → device name → Input N"; switched-off current pick greyed "(switched off)"; "Audio I/O setup…" entry at the bottom
  - List logic in `InputPickList.Build` (Application); `RecordInputPick.HardwareEntries` wraps it.
- [X] T047 [US5] Preferences → Audio → Audio I/O page in `M/Nota.App/PreferencesWindow.AudioIo.cs` (new partial, SPDX): row per input (number · device name · switch · name field), row per pair (label · name field), "Reset names"; probes the device via `ProbeAudioInput` when no track has it open; live apply (FR-020) via an `InputSetupStore.Changed` event that every input menu/label listens to; follow `DESIGN.md` (nota-design skill)
  - As built: an **Inputs** section of Settings → Audio (`PreferencesWindow.AudioIo.cs`), not a separate page; checked headless (dark).
- [X] T048 [US5] Engine skips switched-off inputs: a track on a switched-off input is treated like unavailable at take start — managed passes the skip list via a new `IAudioEngine.SetDisabledInputs(int[] channels)` → `nota_audio_input_set_disabled` in `H`/`E/abi/nota_engine_audio.cpp` (control-thread set read in `startArrangementTake`, reason 3 "switched off")
- [X] T049 [P] [US5] Arm tooltip "Arm for recording (⌘-click to arm more tracks)" (Ctrl on Windows/Linux) in `M/Nota.App/TrackArming.cs` callers (arrangement header, mixer strip, Session) + localisation keys (FR-005a)
  - Already in place: the Arm tooltip says "⌘-click to arm several tracks" (#332, specs/076).
- [X] T050 [P] [US5] Transport status line "Recording N tracks — M skipped" with a tooltip listing track + reason, from `RecordSkipped()` (`M/Nota.App/MainWindow*.cs` transport area) + localisation keys
  - Transport status line: "● Recording N tracks… Not recording: <track> (<why>)."
- [X] T051 [P] [US5] Localisation for the Audio I/O page strings in `M/Nota.Application/Localization/en.json` and the Russian catalogue
  - en.json only; other languages ship outside this repo.

**Checkpoint (PR 3 ready)**: #353 complete for Arrangement.

---

## Phase 7: User Story 3 — Several Session slots at once (P2) — PR 4

**Goal**: audio slots on different tracks record together; stopping one leaves the others.

**Independent Test**: quickstart 8.

### Tests

- [X] T052 [P] [US3] `T/MultiInputSessionTests.cs`: two armed tracks on (0,1),(1,1); launch empty slots in scene 0 for the same boundary; feed 4 bars; stop slot A after 2 bars → A has a 2-bar clip, B still recording; stop B → 4-bar clip; both start at the boundary; save/reopen → both slots, distinct sample ids
  - `T/MultiInputSessionTests.cs` (in `--multi-input` and the full run): two slots, stop one after 2 beats, the other after 4; lengths, mono, own input, save → load.
- [X] T053 [P] [US3] Retry on one Session lane retries only that slot; the other lane keeps its take
  - As built (FR-007, one retry unit; `RetryTake` names no slot): a retry drops and restarts every slot the take still records; a slot already stopped keeps its take. `RetryEverySlot` in `T/MultiInputSessionTests.cs`.

### Implementation

- [X] T054 [US3] Move `recordSessionTrackId_`, `recordSessionScene_`, `recordSessionAudio_` into `AudioTakeLane` (`scene`) in `E/engine/Engine.h`; `E/engine/Engine_Session.cpp` (~295, ~347) adds a lane to the running take (or starts one) when an empty slot on an armed audio track launches; the lane's `keepFrom` is the frame the launch boundary is crossed (same mechanism as `retryKeepFrom_`)
  - As built: `CaptureLane.scene` + `offset` (the take frame it joined at); `recordSessionSlot` on an audio track while a Session audio take runs calls `addSessionAudioLane` — from now, not from a launch boundary (Session audio takes start when launched today).
- [X] T055 [US3] Slot stop (`E/engine/Engine_Session.cpp:128`, `finalizeSessionRecord`) closes only its lane at the stop boundary (`stopAt`), materialises it into its slot, and leaves the take running while other lanes remain; the take ends when its last lane stops
  - `stopSessionRecordSlot(track)` (ABI `nota_session_stop_record_slot`, MCP `stop_session_record_slot`): lands that lane and relaunches its slot; the last lane ends the take as before; the first lane leaving hands the take to the next. The grid and inspector stop one slot; the toolbar stops all.
- [X] T056 [US3] A track armed after the Session take began records from its slot's boundary: its channels join the layout when it is armed (research R2); cover it in `T/MultiInputSessionTests.cs`
  - Arming adds the input to the layout at once; a slot launched later joins the running take (`addSessionAudioLane`).
- [X] T057 [US3] Retry (`E/engine/Engine_TakeRetry.cpp`) for a Session lane discards and restarts only that lane
  - As built: `TakeAttempt.sessionSlots` lists the slots still recording (a lane that lands leaves it via `keepTakeTrack`); `discardTake` restores each of those slots, `retryTake` relaunches the first and re-joins the others as lanes; the retry boundary trim is per lane (`offset`).

**Checkpoint (PR 4 ready)**: ultralooper's acceptance case in #353 passes.

---

## Phase 8: User Story 2 (MIDI part) — Several instrument tracks + MIDI input (P1) — PR 5

**Goal**: every armed instrument track records the MIDI its input filter lets through.

**Independent Test**: quickstart 11–12.

### Tests

- [X] T058 [P] [US2] `T/MidiInputFilterTests.cs`: tracks P (All) and Q (source 1, channel 1) armed; push notes source 0 ch 1, source 1 ch 1, source 1 ch 2 via `nota_engine_push_midi_for_test`, computer-keyboard note via `noteOn` → P records 4 notes, Q exactly 1
  - `T/MidiInputFilterTests.cs` (`--multi-input` and the full run): P records 60,62,64 and the keyboard's 67; Q only 62.
- [X] T059 [P] [US2] Linked group of two instrument tracks, arm one, record → two MIDI clips with the same notes; one `Undo` removes both; overdub into an existing clip under the playhead works per track
  - Same file: both linked tracks record 60,64; one Undo / Redo; one track overdubs its clip while the other opens one.
- [X] T060 [P] [US2] Project round-trip: `MidiInputUid` / `MidiInputChannel` saved and restored; a disconnected uid reads back as "(not connected)" and receives nothing
  - Same file: uid + channel 3 round-trip, read back not connected; that track records nothing from hardware or the keyboard.

### Implementation

- [X] T061 [US2] `MidiEvent` gains `int8_t source = -1; int8_t channel = 0;` in `E/engine/EngineTypes.h`; the MIDI callback in `E/engine/Engine.cpp:155` and the MPE sink pass them through; computer keyboard / gamepad / Remote / UI keep −1
  - As built: per-source `MpeSink` (`mpeSinks_`, set up on first use too) carries source + the message's channel; `Engine::onMidiMessage` is the one entry (callback and test push). Live expression coalesces per input.
- [X] T062 [US2] `Track` gains atomics `midiInputSource_` (−2 = All, default) and `midiInputChannel_` (0 = all, 1–16) in `E/engine/Track.h`; copied in clone paths (`Engine.cpp:674`, `Engine_Tracks.cpp:3677`)
  - Plus `Track::midiInputUid` (message thread), copied in both clone paths.
- [X] T063 [US2] Filter in `E/engine/Engine_Render.cpp` (~1287 `takesUntargeted`, ~1606 expression): an untargeted event reaches an armed track only when `(src == All || src == e.source) && (ch == 0 || ch == e.channel + 1)`; `e.source == −1` matches All only
  - `Engine::passesMidiInput` in `liveNotesFor`, `takesLiveEvent` (expression) and the Session slot monitor path; the audition target takes everything.
- [X] T064 [US2] MIDI take lanes: replace `recordTrackId_`/`recordTrackRt_` with a preallocated `MidiTakeLane[kMaxMidiTakeLanes]` table (per-lane `pendingActive[128]`, `recRoutedActive[128]`, clip index) published to the audio thread as count + ids; `startArrangementTake` opens one lane per armed instrument track (overdub/new clip per track, inside the take's `TakePushScope`); the capture path (~1100–1215) keys state by lane; `poll()` folds recorded notes into each lane's clip; loop-record-replace per lane; >16 armed → skipped reason 2
  - As built: audio thread `RtMidiLane rtMidiLanes_[16]` (pending + routed pairing per lane) fed by `midiLaneTrack_/Src_/Ch_` atomics + `midiLaneCount_`; `RecordedNote/Expr/Ctl.lane`; `foldRecordedTake()` (the old `poll()` tail) folds each lane via `foldMidiLane`, swapping the lane's `takeNotes/ctlFold/takeCtls` in; release-note table per track; new clips in one undo group; disarming one lane folds and closes it while the rest record.
- [X] T065 [US2] Session MIDI slots record per lane like T054–T055 (`E/engine/Engine_Session.cpp`)
  - As built: `MidiTakeLane.scene/growing/player`; a MIDI slot launched on another track during a Session MIDI take joins it (`addSessionMidiLane`) and records once its slot plays, on its own loop clock — the audio thread reads the lanes' slots from an immutable `SessionLaneSlots` table (`laneSlotsHist_`). `stopSessionRecordSlot` lands one slot (`finishSessionMidiLane`; the first slot hands the take to the next); fixed-length capture stops each slot on its own; retry restarts every slot still recording. Tests: `SessionSlotsTogether`, `SessionSlotsRetry`, `SessionFirstSlotStops` in `T/MidiInputFilterTests.cs`.
- [X] T066 [US2] ABI: `nota_track_set_midi_input`, `nota_track_get_midi_input`, `nota_midi_source_of_uid`, `nota_engine_push_midi_for_test` in `H` + `E/abi/` shims; source index ↔ uid map rebuilt on `openMidiInput`
  - As built: plus `nota_track_set_midi_input_uid` / `nota_track_get_midi_input_uid` — the engine keeps the uid on the track and re-resolves it in `openMidiInput`, so undo, duplicate and reconnects keep the pick (contracts/c-abi.md updated).
- [X] T067 [US2] Managed: `IAudioEngine.SetTrackMidiInput(int trackId, string? uid, int channel)` / `GetTrackMidiInput`; re-resolve uids after a MIDI device change; `TrackModel.MidiInputUid string?` (null = All) + `MidiInputChannel int?` (null/0 = All) saved/loaded in `ProjectService.cs`
  - As built: `SetTrackMidiInput(trackId, uid, channel)` / `GetTrackMidiInput` → `TrackMidiInput(Uid, Channel, Connected)`; project fields `MidiInputUid` / `MidiInputChannel`; no managed uid map (the engine keeps it).
- [ ] T068 [US2] MIDI input menu on instrument tracks (header, mixer, Session I/O, detail panel) per contracts/mcp-and-ui.md: All inputs / devices / Channel ▸ / From track ▸; disconnected device greyed "(not connected)"; MPE device + single channel → warning line
  - As built: `MidiInputPick` — **MIDI input ▸** in the track menu (devices, Channel ▸, MPE warning) and Device / Channel rows in the inspector. Open: the header box, mixer and Session I/O rows still show MIDI from only.
- [X] T069 [P] [US2] MCP `list_midi_inputs`, `set_track_midi_input`, `get_track_midi_input` in `M/Nota.Mcp/Tools/MixerTools.cs`
  - As built: `set_track_midi_input`, `get_track_midi_input`; the device list is the existing `list_midi_devices` (uid, name, enabled), so no `list_midi_inputs`.
- [ ] T070 [P] [US2] Localisation keys for the MIDI input menu and warning (`en.json` + Russian)
  - `en.json` `midi_input.*`. Open: Russian lives in the nota-translations repo.

**Checkpoint (PR 5 ready)**: #528 fixed for instrument groups; quickstart 11–12 pass.

---

## Phase 9: Polish & cross-cutting (per PR)

- [ ] T071 Each PR: `scripts/build.sh` green; manual hardware pass for the PR's quickstart section (KeyLab Essential 61 mk3 + a second controller for PR 5)
- [ ] T072 Each PR: `CHANGELOG.md` `[Unreleased]` entry; run `nota-features` (FEATURES.md "Audio: recording" — replace the Ext 1/Ext 2 text), `nota-docs` (recording page EN+RU, Audio I/O page, MIDI input), `nota-shortcuts` (⌘/Ctrl-click arm hint) where the PR changes them
  - PR 1: CHANGELOG `[Unreleased]` and FEATURES.md done; `nota-docs` (recording page EN+RU) still to do.
- [ ] T073 [P] Performance check (SC-004): 8 tracks armed, 128-sample buffer, 10 minutes vs 1 track; xrun counter from `DropoutCounterTests` instrumentation; result in the PR 2 description
- [ ] T074 [P] Open a follow-up issue draft (text only, for the user to file): "Stream long takes to disk while recording" (research R11)
- [ ] T075 Run `/speckit-converge` before the last PR; must report Converged

---

## Dependencies & Execution Order

- Phase 1 → Phase 2 (foundational) → US1 (Phase 3) → US4 (Phase 4) = **PR 1**.
- US2 audio (Phase 5) needs PR 1 = **PR 2**.
- US5 (Phase 6) needs Phases 3–5 = **PR 3**.
- US3 (Phase 7) needs Phase 5 = **PR 4** (independent of PR 3).
- US2 MIDI (Phase 8) needs Phase 5 only (lane/undo scaffolding) = **PR 5** (independent of PRs 3–4).
- Within a phase: tests first (they fail), then engine → ABI → managed → UI.

## Parallel opportunities

- Phase 2: T004, T005, T006 (three backends, different files) in parallel after T003.
- Phase 3: T018, T021, T022 in parallel after T017.
- Phase 5: all test tasks T027–T033 in parallel.
- After PR 2: PR 3, PR 4 and PR 5 can be built in parallel by different people (different files, except `Engine_Session.cpp` shared by T054–T055 and T065 — do T065 after PR 4 lands or coordinate).

## Implementation Strategy

1. **MVP = PR 1** (Phases 1–4): any input on any track, old projects unchanged. Ship in a release.
2. **PR 2**: the headline fix — several audio tracks per pass. Ship.
3. **PR 3 / 4 / 5** in any order; PR 3 closes #353, PR 5 closes #528.
4. Each PR stops at its checkpoint, runs T071–T072, and links the issue (`Refs #353` for PR 1–2 and 4, `Closes` where the table in plan.md says so).
