---
description: "Tasks: controller auto-mapping — profiles, device-follow knobs, Mackie Control"
---

# Tasks: Controller auto-mapping — profiles, device-follow knobs, Mackie Control

**Input**: [spec.md](spec.md), [plan.md](plan.md), [research.md](research.md),
[data-model.md](data-model.md), [contracts/c-abi.md](contracts/c-abi.md),
[contracts/controller-profile.md](contracts/controller-profile.md),
[contracts/device-pages.md](contracts/device-pages.md), [contracts/mcp.md](contracts/mcp.md),
[quickstart.md](quickstart.md)

**Tests**: required by the plan (Constitution IV, research R15) — native
`nota_midi_surface_selftest` plus managed cases in `T/ControllerTests.cs`, run by `scripts/build.sh`
and alone with `--controllers`.

**Path shorthands**: `E/` = `src/native/nota.engine/src/`, `H` = `src/native/nota.engine/include/nota/nota_engine.h`,
`M/` = `src/managed/`, `C/` = `src/managed/Nota.Application/Controllers/`,
`A/` = `src/managed/Nota.App/Controllers/`, `T/` = `tests/Nota.SmokeTest/`.

**Delivery**: one PR per phase group, in this order (plan § Delivery slices): PR 1 = Phase 2;
PR 2 = Phase 3 (US1); PR 3 = Phase 4 (US2); PR 4 = Phase 5 (US3); PR 5 = Phase 6 (US4);
PR 6 = Phase 7 (US5); PR 7 = Phase 8 (US6). Each PR also does its rows of Phase 9.

## Format: `[ID] [P?] [Story] Description`

- **[P]**: can run in parallel (different files, no dependency on an unfinished task)
- **[Story]**: US1…US6 from spec.md

---

## Phase 1: Setup

- [X] T001 Add `T/ControllerTests.cs` (SPDX header, `internal static class ControllerTests` with `public static IEnumerable<(bool ok, string label)> Run()`, empty for now) and register it in `T/Program.cs` as the `--controllers` flag (pattern of `--sidechain-parts`, ~L504) and in the full run (next to `SidechainPartTests.Run()`, ~L15284)
- [X] T002 [P] Create the folders `C/` (namespace `Nota.Application.Controllers`) and `A/` (namespace `Nota.App.Controllers`) and `A/Profiles/`; add `<EmbeddedResource Include="Controllers\Profiles\*.json" LogicalName="Nota.Controllers.%(Filename)%(Extension)" />` to `M/Nota.App/Nota.App.csproj` (pattern of the `Theme\Themes\*.json` line, ~L52)
- [X] T003 [P] Reference device confirmed: Arturia KeyLab Essential 61 **mk3**. Collect the manufacturer MIDI-implementation / programmer documents for KeyLab Essential 61, Behringer X-Touch Mini, Novation Launchkey MK3 (MK4 if public), Akai MPK mini MK3 and the Mackie Control / Logic Control implementation; record title + URL for each in a comment block at the top of `T/ControllerTests.cs` (they become each profile's `source`) — done: sources recorded; findings: the KeyLab Essential mk3 has no public programmer's reference and does its DAW control as **Mackie Control on its "MCU/HUI" port**; the MPK mini mk3 has no published default-CC chart (read it from its editor); MCU has no official spec (community protocol tables, licence to check)

---

## Phase 2: Foundational — engine MIDI port layer + managed plumbing (PR 1, blocks every story)

**Purpose**: ports with outputs, the claim filter, surface queue, SysEx in / out, test ports
([contracts/c-abi.md](contracts/c-abi.md)), the shared value applier, and the profile model all
stories read. No user-visible change.

### Engine

- [X] T004 `E/engine/MidiSurface.h` + `E/engine/MidiSurface.cpp` (new): `ClaimTable` (per source 0..15: `wholePort` flag + bitset over status {0x80, 0xB0, 0xD0, 0xE0} × channel 16 × data1 128), published through `std::atomic<const ClaimTable*>` with retirement on the message thread after an epoch counter advances (AR-5); `SurfaceQueue` = `SpscRingBuffer<SurfaceEvent{source,status,channel,d1,d2}, 1024>` + `std::atomic<uint32_t> dropped`; `SysexRing` = 16 slots × 512 B with source + length, lock-free single producer / single consumer; per-source 512 B SysEx assembly buffers; no allocation in any MIDI-thread path (AR-6)
- [X] T005 `E/audio_io/MidiInput.h`: add a second callback `using SysexCallback = std::function<void(int32_t source, const uint8_t* data, int32_t len)>` (complete F0…F7 messages, called on the MIDI thread), an `open` overload taking both, and `static uint32_t portsGeneration()`; keep the existing channel-message callback contract unchanged
- [X] T006 [P] `E/audio_io/macos/MidiInput.mm`: in `readProc` assemble SysEx across packets into the per-source buffer (real-time bytes 0xF8–0xFF interleaved inside SysEx are skipped, not terminators; > 512 B → discard until F7) and hand complete messages to the SysEx callback; create the client with a notify proc that bumps the ports generation on `kMIDIMsgSetupChanged` / object added / removed — done without a notify proc: `portsGeneration()` compares the source / destination uids on each call (a CoreMIDI notification only reaches the run loop of the thread that created the client)
- [X] T007 [P] `E/audio_io/desktop/MidiInput_rtmidi.cpp`: `ignoreTypes(false, true, true)` so SysEx arrives, route messages starting with 0xF0 to the SysEx callback (same 512 B cap); bump the ports generation when a poll of `getPortCount()` / port names (from the message thread, on `nota_midi_ports_generation`) sees a change
- [X] T008 [P] `E/audio_io/MidiOutput.h` (new): `class MidiOutput { bool open(const std::string& uid); bool send(const uint8_t*, int32_t); void close(); static std::vector<OutputInfo> list(); }` — message-thread only, never the audio thread (SPDX + "why" header)
- [X] T009 [P] `E/audio_io/macos/MidiOutput.mm` (new): CoreMIDI output port on the existing client name "Nota"; `MIDISend` for channel messages, `MIDISendSysex` with the request + buffer kept alive until its completion proc; destinations listed with `kMIDIPropertyUniqueID`; pair an input source with the destination of the same entity (`MIDIEndpointGetEntity`)
- [X] T010 [P] `E/audio_io/desktop/MidiOutput_rtmidi.cpp` (new): `RtMidiOut`, `openPort` by name match, `sendMessage`; pairing by identical port name (WinMM / ALSA)
- [X] T011 `src/native/nota.engine/CMakeLists.txt`: add `MidiSurface.cpp`, `MidiOutput.mm` (Apple list) and `MidiOutput_rtmidi.cpp` (desktop list, ~L220)
- [X] T012 `E/engine/Engine.h` + `E/engine/Engine.cpp`: own a `MidiSurface` and a map uid → `MidiOutput`; in the `midiInput_.open` channel lambda (~L150) look up `claims->claimed(source, status, channel, d1)` first — claimed → push to the surface queue (note-on velocity 0 reported as 0x80), additionally push the CC / note-on copy to `midiControl_` when the atomic `learnListening_` flag is set, and **return** (never `MpeInput`); unclaimed → existing code unchanged; wire the SysEx callback to the ring (accept only from sources with a claim, or any source while an identity request is outstanding ≤ 2 s); keep a port table (index → source, uid, name, paired output uid) rebuilt on `nota_midi_apply` and on generation change
- [X] T013 `E/engine/Engine.cpp`: test ports — `addTestPort(name)` assigns a free source index and adds a port row with a loopback output capturing sent bytes in a 64 KiB ring; `injectTestBytes(source, bytes)` runs the same parser path as `readProc` (factor the byte parser out of `MidiInput.mm` / `MidiInput_rtmidi.cpp` into a shared `E/audio_io/MidiParser.h` used by both backends and the test path)
- [X] T014 `H` + `E/abi/nota_engine_audio.cpp`: declare and implement every call in [contracts/c-abi.md](contracts/c-abi.md) — `nota_midi_ports_generation`, `nota_midi_port_count/source/uid/name/output_uid`, `NotaMidiClaim` + `nota_midi_set_claims` (reject entries with status ∉ {0x80,0xB0,0xD0,0xE0}, channel ∉ 0..15, data1 ∉ 0..127 → `NOTA_ERR_INVALID`, table unchanged), `nota_midi_poll_surface_events`, `nota_midi_surface_dropped`, `nota_midi_set_learn_listening`, `nota_midi_poll_sysex`, `nota_midi_send`, `nota_midi_test_port_add/inject/take_sent`, `nota_midi_surface_selftest`; comment block "MIDI control surfaces (specs/051)"
- [X] T015 `E/engine/MidiSurface.cpp`: `nota_midi_surface_selftest()` — the six checks listed in the contract (claimed → surface not instruments; unclaimed → instruments + learn copy; whole-port; SysEx across packets with interleaved 0xF8; 512 B cap; output loopback); returns the first failed check number

### Managed plumbing

- [X] T016 `M/Nota.Infrastructure/Interop/NativeMethods.Audio.cs`: P/Invoke for every new call (`NotaMidiClaim` as a sequential struct); `M/Nota.Application/Engine/IAudioEngine.cs` + the engine implementation: `MidiPortsGeneration`, `MidiPorts()` → `record MidiPortInfo(string Uid, string Name, int Source, string OutputUid)`, `SetMidiClaims(ReadOnlySpan<MidiClaim>)`, `SetMidiLearnListening(bool)`, `PollMidiSurfaceEvents(Span<int>)`, `PollMidiSysex(out int source, Span<byte>)`, `MidiSend(string outputUid, ReadOnlySpan<byte>)`, `MidiTestPortAdd/Inject/TakeSent`, `MidiSurfaceSelfTest()`; update any fake / proxy implementations of `IAudioEngine` so the solution builds
- [X] T017 [P] `T/ControllerTests.cs`: call `NotaEngine.MidiSurfaceSelfTest() == 0` ("MIDI surface: claim filter, SysEx, output loopback"); plus an engine-level case: add a test port, claim CC 20 ch1 on it, inject `B0 14 40` and `90 3C 64` → the CC shows in `PollMidiSurfaceEvents` and not in `PollMidiControlEvents`, the note shows in `PollMidiControlEvents` only; with `SetMidiLearnListening(true)` the claimed CC shows in **both** queues
- [X] T018 `A/ControlApplier.cs` (new): move `ApplyNormalized`, `Trigger`, `BeginLatchWrite`, `TrackFlag`, `MixTouch` and the `_mixUndo` table out of `M/Nota.App/MidiLearnService.cs` into this class (same behaviour: latching automation gesture, one undo step per movement ended by a 1 s pause, specs/009 + 016); add `Nudge(MidiTarget, double delta)` (relative: read current normalized value, clamp, apply) and `CurrentNormalized(MidiTarget)`; `MidiLearnService` takes a `ControlApplier` and calls it; register it as a singleton in `M/Nota.App/App.axaml.cs` (~L70)
- [X] T019 `M/Nota.App/MidiLearn.cs`: add `MidiTargetKind` values `TransportLoop`, `TransportRewind`, `TransportForward`, `TrackArm`, `TrackSelect` (+ factory methods, `IsButton` true for these five) and `TrackSend` (continuous; `ParamIndex` = send bus, applied through `SetTrackSend`, range 0..1, with a `MixTarget.Send(track, bus)` value-undo step and a latching write to the send's automation lane if the engine has one — if sends have no value undo, add it the way specs/009 did for pan (engine `MixKind` value) and extend the smoke test) and implement them in `ControlApplier.Trigger` (loop toggles the loop, rewind / forward step one bar via the existing transport navigation, arm toggles `SetTrackArmed`, select raises `TrackSelectRequested(int trackId)`, which `MainWindow` handles by selecting the track in the Arrangement / Mixer exactly as a header click does, so the device focus moves too (FR-016)); keep the persisted `Kind` integers of existing values unchanged (append only) — `TrackSend` is applied like the on-screen send knobs, which have no value undo and no lane: no undo step is added (FR-010 "same as the on-screen control")
- [X] T020 [P] `C/ControllerProfile.cs` (new): the model in [data-model.md](data-model.md) (`ControllerProfile`, `ControlDecl`, `ControlRole`, enums `Protocol` generic/mcu/hui, `MessageKind` cc/cc14/nrpn/note/pitchbend, `EncoderMode` absolute/relative-twos/relative-signbit/relative-offset) and `ProfileLoader.Load(string json, string fileName, ProfileOrigin origin) → (ControllerProfile? profile, IReadOnlyList<string> warnings)` implementing [contracts/controller-profile.md](contracts/controller-profile.md): `format` must be 1 ("needs a newer Nota" otherwise); `id` must match `[a-z0-9.-]+`; `name` and `match` (portNames or identity) required; shipped profiles require `source`; channel 1..16; number 0..127 (`nrpn` 0..16383); duplicate (message, channel, number) → later control skipped with a warning; unknown role / message → control skipped; unknown fields ignored; hex strings parsed to complete MIDI messages
- [X] T021 [P] `C/ProfileMatcher.cs` (new): `Match(IReadOnlyList<ControllerProfile> profiles, string portName, IdentityReply? identity) → ControllerProfile?` per research R5 (user before shipped; identity before port name; more identity fields beat fewer; then file-name order); `IdentityReply.TryParse(ReadOnlySpan<byte>)` for `F0 7E <id> 06 02 <mfr 1|3> <family 2> <model 2> <version 4> F7`; `IdentityRequest` = `F0 7E 7F 06 01 F7`
- [X] T022 [P] `T/ControllerTests.cs`: loader cases (valid example from the contract; format 2 refused; missing `match`; duplicate control skipped; bad hex), matcher cases (identity beats port name; user beats shipped; 1-byte and 3-byte manufacturer ids; no match → null)
- [X] T023 `A/ControllerService.cs` (new, skeleton): constructed in `M/Nota.App/MainWindow.axaml.cs` next to `_learn` (~L178) and ticked right after `_learn?.Tick()` (~L290); loads shipped profiles from embedded resources `Nota.Controllers.*.json` and user profiles from `NotaPaths.SubDir("controllers")`; on generation change re-reads `MidiPorts()`, sends the Identity Request to new ports with an output, collects replies from `PollMidiSysex` for 500 ms, then matches; keeps `ConnectedController` records (data-model state machine: Discovered → Identifying → Matched/Unmatched → Active → Released / Gone, with bank / page / mode kept across Gone → Active for the same uid); publishes no claims yet (roles come in US1) — also rescans the port list every 2 s, so inputs the engine opens late (after the audio device starts) are picked up

**Checkpoint**: PR 1 — build green, self-test passes, nothing user-visible changed; plugging a
keyboard behaves exactly as before.

---

## Phase 3: User Story 1 — A known controller works on plug-in (P1) 🎯 MVP (PR 2)

**Goal**: recognised controllers drive the transport and bank faders with no setup; keys and pads
still play.

**Independent Test**: quickstart § Slice 2 — connect the KeyLab Essential 61 (or inject its
identity / port name on a test port): notice shown, Play works, fader 1 moves track 1's volume with
one undo step, keys still play, switching it off restores plain MIDI.

### Tests for User Story 1

- [X] T024 [P] [US1] `T/ControllerTests.cs`: `ControlDecoder` cases — absolute 7-bit; `relative-twos` (1 → +1, 127 → −1, 65 → −63), `relative-signbit` (1 → +1, 65 → −1), `relative-offset` (65 → +1, 63 → −1); `cc14` MSB-then-LSB gives 14-bit value and MSB-only falls back to 7-bit; NRPN 99/98/6/38 sequence and 96/97 inc / dec per channel
- [X] T025 [P] [US1] `T/ControllerTests.cs`: `RoleRouter` cases — transport roles → `Trigger`; fader N with bank origin 8 → track index 8+N; bank-next / bank-prev clamp so the bank never starts past the last track; a control with an explicit MIDI Learn mapping is excluded from the claim set (FR-012) — including one learned *after* it was claimed (learn → claims rebuilt without it); `pad` and `button` roles are never claimed

### Implementation for User Story 1

- [X] T026 [P] [US1] `C/ControlDecoder.cs` (new): per research R6 — decode a surface event for a `ControlDecl` into `Absolute(double 0..1)` or `Relative(int steps)`; stateful per (source, channel) for 14-bit LSB pairing and NRPN parameter / data assembly
- [X] T027 [P] [US1] `C/ControllerState.cs` (new): per controller `BankOrigin`, `Page`, `KnobMode` (Device / Volume / Pan / Send(k)), `PickupOn`; bank step ±8 / ±1 clamped to `[0, max(0, trackCount − 8)]` and `[0, trackCount − 1]`
- [X] T028 [US1] `C/RoleRouter.cs` (new): `BuildClaims(profile, source, learnedSources) → MidiClaim[]` (all `controls` except `pad` / `button` (no built-in action in format 1) and except (message, channel, number) present in MIDI Learn mappings; `mcu` / `hui` → whole port) and `Route(decl, decoded, state, ITrackOrder) → ControllerAction[]` for transport, fader (volume of bank track N), master-fader, bank / track step roles; knob roles return `KnobAction(index, value)` for US2 to resolve (no-op until then)
- [X] T029 [US1] `A/ControllerService.cs`: when a controller becomes Active — send `init`, publish claims for all active controllers in one `SetMidiClaims` call (rebuild on any change: profile, enable switch, MIDI Learn `MappingsChanged`); each tick drain `PollMidiSurfaceEvents`, decode, route, and execute actions through `ControlApplier` (`TrackVolume` via the applier so undo + latching automation match the on-screen fader, FR-010); on Release send `release` and withdraw claims; `MidiLearnService.PendingChanged` → `SetMidiLearnListening(Pending != null)` — a claimed message arriving while learning binds the pending control and is **not** routed to its role; on `MappingsChanged` rebuild claims so the newly learned control is released (FR-012); on project load / new project reset every controller's bank, page and knob mode (`ControllerState.Reset()`), keep profiles and pickup switches, and request a full feedback refresh (spec edge case "Project switch") — also: Controllers tick runs before MIDI Learn's, so a message that binds a learn target never fires its role; on a port claimed whole, learned controls are handed to MIDI Learn (`DispatchFromController`)
- [X] T030 [US1] `A/ControllerService.cs`: LED feedback for transport roles that declare `feedback.on/off` — send on connect and whenever play / record / loop state changes (compare cached state each tick); send nothing for roles without `feedback` — Loop goes through the transport bar's toggle (`ControlApplier.ToggleLoop`), so an empty region falls back to four bars like the button
- [X] T031 [US1] `A/ControllerService.cs` + `M/Nota.App/MainWindow.axaml.cs` (window closing handler): on app exit send every active profile's `release`; for `mcu` / `hui` surfaces blank all scribble strips, drop motor faders to 0 and turn every LED off — before the engine shuts down (FR-004) — profile `release` sent from the closing handler; MCU/HUI blanking comes with PR 5
- [X] T032 [US1] `M/Nota.Application/Settings.cs`: `Dictionary<string, ControllerChoice> Controllers` keyed by port uid — `ControllerChoice { string? ProfileId; bool Enabled = true; bool PickupOn = true; }` (`ProfileId` null = automatic, `"none"` = plain MIDI); read / written by `ControllerService` (FR-028)
- [X] T033 [US1] `M/Nota.App/PreferencesWindow.cs` `MidiPane()` (~L642): add a "Controllers" section — one row per connected port: name, profile picker (Automatic / profiles that exist / None), on / off switch, and below the list the profile load warnings (file + reason) and a "Reload profiles" button; follow `DESIGN.md` (Studio: neutral rows, orange accent only for the on state)
- [X] T034 [US1] `M/Nota.App/MainWindow.axaml.cs`: show the connect notice "‹Port› connected — using the ‹profile› profile" through the existing status / notice mechanism when a controller becomes Active (once per connect, not on every tick)
- [X] T035 [US1] `M/Nota.App/ArrangementView.cs` (track headers) and the Mixer strips (`M/Nota.App/MainWindow.Mixer.cs`): a thin accent bar on the tracks under the current bank of any active controller with fader roles (FR-009); driven by a `ControllerService.BankChanged` event
- [X] T097 [US3] Card markers on the real KeyLab (2026-11-19): the knob number sat at the left of the knob's cell, nearer the next knob — it now tucks into the dial's top-left corner; the pickup hint (a rail with ticks under the control) read as a stray mark — on a knob it is now a hollow dot on its arc, on a fader a rail beside it
- [X] T036a [US1] `A/Profiles/arturia.keylab-essential-61-mk3-daw.json` + a `command` role (real-device check, 2026-11-19): in its DAW program with MIDI Control Center defaults the KeyLab's "MCU/HUI" port sends nothing — knobs CC 96–104, faders CC 105–113, transport / track / encoder / Undo · Redo · Metro · Save · Quantize · Tap buttons all arrive as CCs on the "MIDI" port; the profile matches that port by identity; the new `command` role (`"command": "edit.undo"`, any command-palette id) runs the command once per press via `ControllerService.RunCommand`; smoke checks: the role parses / routes, every shipped command id is declared
- [X] T036 [P] [US1] `A/Profiles/arturia.keylab-essential-61-mk3.json`: matched on the **"MCU/HUI" port** (names and identity read off the real device first — log them from `ControllerService`); until the MCU codec (PR 5) it is a `generic` profile describing the Mackie messages that port sends: transport notes 91–95 + 86 (LED feedback = the same note on 7F / 00), faders as pitch bend ch 1–9 (9 → master-fader), encoders CC 16–23 `relative-signbit`, bank / channel notes 46–49; in PR 5 it switches to `protocol: "mcu"`; `source` = the mk3 user manual + DAW-integration page; the "MIDI" port stays unprofiled (keys, pads, User-program knobs keep working as plain MIDI) — real device (2026-10-29): ports `KL Essential 61 mk3 MIDI / DINTHRU / MCU/HUI / ALV`; only `MIDI` answers the identity (00 20 6B · 02 00 · 05 74), so the profile matches `^(kl|keylab) essential 61 mk3 mcu/hui$` and sets `claimAll`; the matcher now needs both identity and port name when a profile names both. Roles on the real port still to be checked by hand (transport mode MCU)
- [X] T037 [P] [US1] `A/Profiles/behringer.x-touch-mini.json`: layer A of the default "standard mode" (8 encoders relative or absolute as documented, 16 buttons, fader as master-fader), LED-ring `feedback` per encoder; `source` filled
- [X] T038 [US1] `M/Nota.Mcp/Abstractions.cs` + `M/Nota.Mcp/Tools/ControllerTools.cs` (new) + `M/Nota.App/Mcp/McpAppServices.cs`: `IControllerAccess` and the tools `list_controllers`, `set_controller`, `list_controller_profiles`, `reload_controller_profiles`, and — only when `NOTA_CONTROLLER_TEST=1` — `controller_inject` / `controller_sent` over `MidiTestPortAdd/Inject/TakeSent` ([contracts/mcp.md](contracts/mcp.md)) — tools are discovered from the assembly, so the test tools are listed but refuse without `NOTA_CONTROLLER_TEST=1`
- [X] T039 [US1] `T/ControllerTests.cs`: end-to-end over the engine + `Nota.Application/Controllers` (no `Nota.App`): load the KeyLab profile JSON from the repo path, add a test port named like it, publish its claims, inject Play and fader 1 → the routed actions are `TransportPlay` and `TrackVolume(track 0)`; inject a keybed note → it is unclaimed (arrives in `PollMidiControlEvents`); two test ports with the same name → two controllers with independent bank origins

**Checkpoint**: Story 1 works on its own; MVP shippable.

---

## Phase 4: User Story 2 — Knobs follow the selected device (P1) (PR 3)

**Goal**: knobs drive the focused device's page; pages for every built-in, plug-in default, rack
macros, devices inside racks; knob modes Volume / Pan / Send.

**Independent Test**: quickstart § Slice 3.

### Tests for User Story 2

- [X] T040 [P] [US2] `T/ControllerTests.cs`: `DevicePageCatalog` has ≥ 1 page for **every** built-in instrument / audio effect / MIDI effect kind the engine reports (iterate the kind lists; fail naming the missing kind) and every referenced parameter name resolves on a freshly created device of that kind; plug-in default = parameters in order, 8 per page; racks → `macro:1..8`
- [X] T041 [P] [US2] `T/ControllerTests.cs`: focus rule (selection anchor → instrument → first device → none), page step clamps at first / last page, unresolved param ref → empty knob, knob mode Pan on bank origin 0 routes knob 3 to `TrackPan(track 2)`, Send mode skips tracks without that send, switching mode resets page-independent pickup state (flag only; behaviour in US3)

### Implementation for User Story 2

- [X] T042 [US2] `M/Nota.Application/Controllers/DeviceAddress.cs` (new): `record DeviceAddress(int TrackId, DeviceSection Section, int Index, RackPath? Rack)` with `record RackPath(int RackDevice, int Chain, int Slot, bool Midi)`; `DeviceTypeKey` builder per [data-model.md](data-model.md) (`builtin:inst:<kind>`, `builtin:fx:<kind>`, `builtin:midi:<kind>`, `rack:audio|inst|midi`, `plugin:<pluginId>`) — done as `FocusSection` (avoids a clash with `Nota.Presentation.DeviceSection`) and `RackPath(Chain, Slot, Midi)`; the rack itself is the address's section + index
- [X] T043 [US2] `M/Nota.App/DeviceChainView.Clipboard.cs` (`SelectDevice`, `SelectRackDevice`, `ClearDeviceSelection`, ~L60–L95) and `M/Nota.App/DeviceChainView.cs` (track change): raise `event Action<DeviceAddress?> FocusChanged` after every selection / track change, computing the focus rule of FR-016 (anchor → instrument → first device); relay it in `M/Nota.App/MainWindow.axaml.cs` to `ControllerService.SetFocus`; also subscribe to `ArrangementView.TrackSelected` and the Mixer's track selection so focus moves when the Devices panel is hidden (the panel's own selection wins when it shows that track); also handle device deletion (focus re-resolves on the next rebuild) — `DeviceChainView.ControllerFocusChanged(track, address)` after every rebuild and rack selection; `Timeline.TrackSelected` → `ControllerService.SetSelectedTrack` (the Mixer selects through the Arrangement); a 500 ms check re-resolves a removed / replaced device
- [X] T044 [P] [US2] `C/DevicePageCatalog.cs` (new): built-in pages for every **instrument** kind (Volt, Keys, Sampler, Rhythm, Grain, Pendulum, Flux, React … — every kind in `TrackInstrumentKind`), ordered for playing (filter / tone / envelope first), parameter names exactly as `PluginParamName` reports them — the pages are data: `C/DevicePages.json` (embedded, same format as `pages.json`), kind → slug table in `C/DevicePageCatalog.cs`; this keeps ~500 parameter names out of the localization allow-list
- [X] T045 [P] [US2] `C/DevicePageCatalog.Effects.cs` (new, partial): pages for every built-in **audio effect** kind (`TrackDeviceBuiltinKind`) — EQ, Compressor, Reverb, Delay, Auto Filter, Vintage, Vocoder, Utility, … — names exactly as `DeviceParamName` reports them — in `DevicePages.json` (see T044)
- [X] T046 [P] [US2] `C/DevicePageCatalog.Midi.cs` (new, partial): pages for every built-in **MIDI effect** kind (`MidiEffectKind`) — Arpeggiator and the rest; racks → one page `macro:1..8`; plug-in default builder (all parameters in order, 8 per page, refs `id:<PluginParamId>`) — in `DevicePages.json`; plug-in default in `DevicePageCatalog.PluginDefault`
- [X] T047 [US2] `A/FocusedDevice.cs` (new): resolves a `DeviceAddress` + page into up to 8 `MidiTarget?` + display names using the catalog (precedence hook for US5 page sets left as an interface `IDevicePageSource`); top-level devices → `DeviceParam` / `PluginParam` / `MidiDeviceParam`; racks → their macro targets (as `RackCard.Macros.cs` / `IRackAccess.MacroLearnTarget` build them) — `FocusedDevice` lives in `C/` (not `A/`), addressing targets with a neutral `ControllerTarget` so the smoke test can reach it; `ControllerService.ToMidiTarget` maps to `MidiTarget`
- [X] T048 [US2] `M/Nota.App/MidiLearn.cs` + `A/ControlApplier.cs`: rack-child targets — `MidiTargetKind.RackChainDeviceParam`, `RackChainInstrumentParam` (DeviceIndex = rack encoding as today: −1 Instrument Rack, ≥ 0 Audio Effect Rack device, ≤ `MidiRackBase` − mi MIDI Effect Rack; ParamIndex = param; chain + slot packed in a new `Sub` field of `MidiTarget` defaulting to −1 so existing persisted mappings load unchanged) applied through `RackChainDeviceParamSet` / `RackDevChainDeviceParamSet` / `MidiRackChainDeviceParam…` / `RackChainInstrumentParamSet`; **verify undo**: if the engine records no value-undo for rack-child params, coalesce exactly as the rack card's own knobs do and note it in research.md R10 — `MidiTarget.Sub` (chain × 1024 + slot). Undo checked: the rack card's own knobs record no value undo for rack-child parameters, so a knob sets them directly the same way (research R10)
- [X] T049 [US2] `C/RoleRouter.cs` + `A/ControllerService.cs`: knob roles in `KnobMode.Device` → `FocusedDevice` target N (absolute → `SetNormalized`, relative → `Nudge`); `PageNext` / `PagePrev` roles; `KnobModeDevice/Volume/Pan/SendNext` roles; Volume / Pan / Send(k) modes → bank track N's volume / pan / send k (`MidiTarget.TrackSend(track, k)` via `ControlApplier`; send index wraps to Device mode after the last send) (FR-016a) — knob routing is `RoleRouter.KnobTarget` (testable); Send mode skips return tracks
- [X] T050 [US2] `M/Nota.App/DeviceCards/DeviceCardKit.cs` (card header) + `M/Nota.App/DeviceCards/DeviceParamControls.cs` + `M/Nota.App/Controls/Knob.cs`: when a card is the focused device of an active controller in Device mode, draw a small accent marker on the 8 controls of the current page and "n / m" page text in the header (FR-017); controls find their marker through the existing `MidiBinding` target equality, so no per-card code; follow `DESIGN.md` (accent = live) — drawn by `A/ControllerFocusOverlay.cs` over the Devices panel and the Mixer (controls found by their MIDI Learn binding — no per-card code): knob numbers 1–8, an orange underline, and "n / m · page" on knob 1
- [X] T051 [US2] `M/Nota.App/MainWindow.axaml.cs` (top bar, next to the MIDI-learn button): a controller indicator showing the active controller(s) and the knob mode; clicking it opens a small menu to switch knob mode (Device / Volume / Pan / Send A, B, …) — "switchable from Nota" in FR-016a — `MainWindow.Controllers.cs`: chip after the MIDI button, menu of knob modes
- [X] T052 [P] [US2] `A/Profiles/novation.launchkey-mk3.json` (and `novation.launchkey-mk4.json` only if T003 found a public reference): DAW-mode `init` from the programmer's reference, knob-mode buttons mapped to `mode-*` roles, pots, faders (49 / 61 keys), transport, track / page buttons, pad LEDs not used; `source` filled — MK3 only (no public MK4 reference). Facts read off the guide's tables: pots CC 21–28, faders 53–60 + 61, Play/Stop/Record/Loop CC 115–118, Track ←/→ 102/103, all ch 16; init `9F 0C 7F`, `BF 09 02`. The device's pot-mode report (CC 9 with a value per mode) has no role in format 1 — switch knob modes from Nota
- [ ] T053 [P] [US2] `A/Profiles/akai.mpk-mini-mk3.json`: 8 knobs with the CCs and knob type of the factory program as read from the MPK mini Editor (no published chart; skip the profile if they can't be confirmed), pads left unclaimed, joystick unclaimed; `source` filled — **skipped**: Akai publishes no default-CC chart, so the factory CCs can't be confirmed from a document (the task's own rule); an MPK mini works through **Set up…** (T075)
- [X] T054 [US2] `M/Nota.Mcp/Tools/ControllerTools.cs` + `M/Nota.App/Mcp/McpAppServices.cs`: `get_controller_focus` per [contracts/mcp.md](contracts/mcp.md)

**Checkpoint**: Stories 1 + 2 work; device-follow usable (with jumps until US3).

---

## Phase 5: User Story 3 — No parameter jumps (P2) (PR 4)

**Goal**: pickup for absolute, non-motorised controls with an on-screen hint; per-controller switch.

**Independent Test**: quickstart § Slice 4.

### Tests for User Story 3

- [X] T055 [P] [US3] `T/ControllerTests.cs`: `Pickup` cases — value 0.8, physical 0.1 → moves to 0.5 do nothing; a jump 0.5 → 0.9 straddles 0.8 → caught and applied; within tolerance 2/127 → caught; external change (value ≠ LastWritten) → uncaught again; target change → uncaught; relative and motorised → always caught; pickup off → always caught

### Implementation for User Story 3

- [X] T056 [P] [US3] `C/Pickup.cs` (new): `PickupState { Target, LastPhysical, LastWritten, Caught }` and `bool Accept(double physical, double current)` per research R7 (tolerance = 2/127)
- [X] T057 [US3] `A/ControllerService.cs`: keep a `PickupState` per (controller, control); reset on focus / page / bank / mode change; before every absolute apply call `Accept` with `ControlApplier.CurrentNormalized(target)`; skip for relative / motorised / `PickupOn == false` — in `ControllerService.Apply` (generic roles) and `ProtocolSurface.Fader` (non-motorised MCU faders such as the KeyLab's)
- [X] T058 [US3] `M/Nota.App/Controls/Knob.cs`, `M/Nota.App/Controls/VFader.cs`, `M/Nota.App/Controls/MiniFader.cs`, `M/Nota.App/Controls/PanKnob.cs`: an optional "pickup ghost" (thin neutral tick at the physical position) set through a new `MidiBinding.SetPickupGhost(double?)` callback, cleared when caught (FR-020); no ghost when nothing waits — the hint is drawn by `ControllerFocusOverlay` (a rail under the control: grey tick = hardware, orange = value), not per control
- [X] T059 [US3] `M/Nota.App/PreferencesWindow.cs` Controllers rows: "Pickup" switch per controller bound to `ControllerChoice.PickupOn` (default from the profile's `pickup`) (FR-021)

**Checkpoint**: Stories 1–3 work; switching devices / banks never jumps a parameter.

---

## Phase 6: User Story 4 — Mackie Control surface with feedback (P2) (PR 5)

**Goal**: MCU strips, transport, banks, jog, time display, full diff feedback, touch handling.

**Independent Test**: quickstart § Slice 5 (real surface or `controller_inject` + `controller_sent`).

### Tests for User Story 4

- [X] T060 [P] [US4] `T/ControllerTests.cs`: `McuCodec` decode — pitch bend ch 3 → fader 3 14-bit; note 106 on/off → touch 3; CC 17 value 0x41 → V-pot 2 −1 (sign-bit); notes 0/8/16/24+n → rec/solo/mute/select n; 46/47 bank, 48/49 channel, 91–95 + 86 transport; CC 60 jog
- [X] T061 [P] [US4] `T/ControllerTests.cs`: `McuCodec` encode + `FeedbackDiffer` — scribble text for strip 2 → `F0 00 00 66 14 12 0E …7 chars… F7`; unchanged state → no bytes; fader change while touched → no pitch bend for that strip; bank change → full refresh of 8 strips; time display digits on CC 64–73

### Implementation for User Story 4

- [X] T062 [P] [US4] `C/Mcu/McuCodec.cs` (new): decode / encode per research R11 (faders, touch, V-pots + rings CC 48–55, strip buttons + LEDs, bank / channel, transport, jog, scribble SysEx with model byte 14 (main) / 15 (extender), time display, meters `(strip << 4) | level` on channel pressure, device query `F0 00 00 66 14 00 F7`)
- [X] T063 [P] [US4] `C/Mcu/SurfaceModel.cs` (new): `SurfaceStripState` (TrackId?, FaderPos 14-bit, PanRing, Mute, Solo, Arm, Select, Name 7 chars, Touched, Meter) + globals (transport LEDs, time digits, bank origin) per [data-model.md](data-model.md)
- [X] T064 [US4] `C/Mcu/FeedbackDiffer.cs` (new): compare wanted vs sent state → byte messages; caps per tick (≤ 8 scribble SysEx, one message per changed value); full refresh on demand; `Touched` suppresses fader output
- [X] T065 [US4] `A/McuSurface.cs` (new): drives a controller whose protocol is `mcu` — whole-port claim; on connect send device query, full refresh; each tick read engine state for the 8 bank tracks (volume, pan, mute, solo, arm, selection, name, peak meter level from the existing track meter read mapped to MCU 0–12 — at most one channel-pressure message per strip per tick) + master volume (master fader, pitch bend ch 9) + transport + playhead → wanted state → `FeedbackDiffer` → `MidiSend`; input: master fader → `MasterVolume`; faders → `TrackVolume` (touch on → begin a latching gesture, touch off → end it, FR-024), V-pots → `Nudge(TrackPan)`, strip buttons → mute / solo / arm / select targets, bank / channel → `ControllerState`, transport, jog → move the playhead (one 16th per step while stopped, scrub not required) — `A/ProtocolSurface.cs` drives MCU and HUI through `ISurfaceCodec`; V-pots follow the knob mode (Device = focused page); touch = `ControlApplier.TouchBegin/End`
- [X] T066 [US4] `A/Profiles/generic.mackie-control.json`: `protocol: "mcu"`, `match` on common MCU port names ("MCU", "Mackie Control", "X-Touch", "iCON", "Platform M") — no identity (MCU devices vary); `source` = the Mackie Control / Logic Control implementation document — and the KeyLab Essential 61 mk3 profile moved to `protocol: "mcu"` (T036); its faders have no motors, so it keeps `pickup: true`
- [X] T067 [US4] `M/Nota.App/PreferencesWindow.cs` Controllers rows: profile picker gets "Mackie Control" and "HUI" entries for any port (forced protocol, FR-025); show "Feedback unavailable — no MIDI output" when the port has no paired output (Story 4 scenario 6)
- [X] T068 [US4] `T/ControllerTests.cs`: end-to-end with a test port + the generic MCU profile — inject pitch bend on ch 1 → track 0 volume changes; set track 0 volume from the engine → `MidiTestTakeSent` contains a pitch bend on ch 1; bank-next → sent bytes include 8 scribble SysEx

**Checkpoint**: Stories 1–4 work; MCU surfaces fully usable.

---

## Phase 7: User Story 5 — Make it mine (P3) (PR 6)

**Goal**: Controller setup wizard → profile file; user profile reload / errors; device page editor
with global + project copies.

**Independent Test**: quickstart § Slice 6.

### Tests for User Story 5

- [X] T069 [P] [US5] `T/ControllerTests.cs`: `ControlClassifier` cases — a CC sweep → `cc` absolute; CC n followed by n+32 → `cc14`; 99/98/6/38 → `nrpn`; turn-right-then-left streams `01 01 7F 7F` → relative-twos, `01 01 41 41` → relative-signbit, `41 41 3F 3F` → relative-offset; note on/off → `note`
- [X] T070 [P] [US5] `T/ControllerTests.cs`: `DevicePagesStore` — global edit writes `pages.json` and the project copy; project copy wins while open; opening a project never rewrites the global file; reset removes the type from both; unknown `format` refused

### Implementation for User Story 5

- [X] T071 [P] [US5] `C/ControlClassifier.cs` (new): classify a captured event stream into a `ControlDecl` (message kind, channel, number, encoder mode) per research R13; returns "ambiguous" when it cannot tell, so the wizard asks
- [X] T072 [P] [US5] `C/DevicePagesStore.cs` (new): read / write [contracts/device-pages.md](contracts/device-pages.md) (`format: 1`, `types` → pages, refs name / `id:` / `macro:` / null); implements `IDevicePageSource` with precedence project copy → user global → catalog / plug-in default (FR-018, FR-018a)
- [X] T073 [US5] `M/Nota.App/MainWindow.Files.cs` (next to `_learn?.SaveMappings(dir)` ~L347 and `LoadMappings` ~L279): save `devicepages.json` with the edited pages for the device types the project contains; load it as the project copy (empty for projects without the file)
- [X] T074 [US5] `A/DevicePageEditor.cs` (new): opened from the device card's context menu "Edit controller pages…" — list of pages (add / remove / rename), 8 slots per page filled by picking from the device's parameter list or by drag from the card, struck-through unresolved refs, "Reset pages"; writes through `DevicePagesStore` (DESIGN.md) — parameters are picked from the device's list (no drag from the card)
- [X] T075 [US5] `A/ControllerSetupWindow.cs` (new): "Set up a controller…" from Preferences → Controllers on an unprofiled port; whole-port claim during capture; steps knobs 1–8 → faders → transport → page / bank (each skippable), "turn right, then left" prompt for encoders, `ControlClassifier` per step, asks only on "ambiguous"; on finish writes `<DataDir>/controllers/<slug>.json` (`format: 1`, `match.portNames` = escaped port name, identity if a reply was received) and reloads profiles (FR-030); claims released on cancel
- [X] T076 [US5] `M/Nota.App/PreferencesWindow.cs`: "Open profiles folder" button and a watch (or Reload) that re-reads user profiles without restart; errors listed per file (Story 5 scenario 2) — the Reload / Open profiles folder buttons (from PR 2) cover it; no file watcher

**Checkpoint**: Stories 1–5 work.

---

## Phase 8: User Story 6 — HUI surfaces (P3) (PR 7)

**Goal**: same strip / transport / bank model over HUI.

**Independent Test**: quickstart § Slice 7.

- [X] T077 [P] [US6] `T/ControllerTests.cs`: `HuiCodec` cases — zone / port button decode (CC 0x0F zone + 0x2F port on / off), fader hi/lo CC pairs per strip (0x00–0x07 / 0x20–0x27) → 14-bit, V-pot CC 0x10–0x17, 4-char strip name SysEx `F0 00 00 66 05 00 10 <strip> …4… F7`, ping reply
- [X] T078 [US6] `C/Hui/HuiCodec.cs` (new): decode / encode per research R12, mapping onto the same `SurfaceStripState` / `FeedbackDiffer` contract as MCU
- [X] T079 [US6] `A/HuiSurface.cs` (new): like `McuSurface` but with `HuiCodec`, the ping every second (note-on 0 0) and reconnect when replies stop for 3 s; selected through the forced "HUI" protocol entry (T067) — `ProtocolSurface` with `HuiCodec`: ping once a second, no answer for 3 s = feedback paused, the next answer refreshes everything

---

## Phase 8b: User Story 7 — Rebind from the control's menu (P2) (this PR)

**Goal**: Unbind / Bind to ▸ / Back to default in the right-click menu of any control a
controller drives (FR-031–FR-034).

**Independent Test**: spec Story 7.

- [X] T091 [US7] `C/ControllerOverrides.cs` (new): the unbinds (targets) and pins (hardware
  control → target) with track-free entries in `<DataDir>/controllers/overrides.json` + the
  project's `controlleroverrides.json`, track entries in the project only (track positions on save,
  like `midimap.json`); `Changed` event
- [X] T092 [US7] `ControllerService`: skip unbound targets (Execute, pickup ghosts, MCU feedback
  stays), drop a no-track unbound role and every pinned control from the claims, apply pins
  (absolute, through pickup) before roles; `BindingFor(target)` says which hardware control drives
  a target now (knob slot, bank fader / knob, role, pin)
- [X] T093 [US7] Device parameters: Unbind / Bind to ▸ Knob N edit the focused device's current
  page through `DevicePagesStore.Edit` (the parameter that held the slot leaves it)
- [X] T094 [US7] `A/Controllers/ControllerContextMenu.cs` (new): a window-level right-click
  handler finds the bound control (`MidiLearn.GetBinding` up the tree), adds a Controller submenu
  to the control's own menu or opens one; wired on the main window and the floating mixer /
  detail windows
- [X] T095 [P] [US7] `T/ControllerTests.cs`: overrides round-trip (global / project, track
  positions), unbind skips a bank track and releases a transport role's claim, a pin drives its
  target whatever the bank, `BindingFor`
- [X] T096 [US7] Docs (`control/controllers` EN + RU), CHANGELOG, FEATURES, en.json keys; harness
  shot of the menu — RU strings for the new en.json keys go with the rest (nota-translations)

- [X] T098 [US5] Setup wizard rework (FR-030a): `ControlPictogram` (knob / fader / button + icon /
  jog), live "Hearing" line, auto-advance on a learned step, No more ⟨group⟩, Back, learned
  controls ignored and named, steps for track ◀▶, Session buttons, Undo / Redo / Metronome / Save /
  Tap (command roles; `LearnedControl.Command`, `ProfileWriter` writes `command`) and the jog wheel;
  harness: auto-advance, taken control, group skip, button press, written profile
- [X] T098a [US5] Set up… stays after the first run: "Set up again…" on a controller with your
  profile, "Set up…" on a recognised generic one (your profile replaces the shipped one); hidden
  only for Mackie / HUI surfaces; the page rebuilds after the wizard
- [X] T098b [US5] Two-way encoders (FR-030b): `ControlRole.CommandLeft`, profile `commandLeft`,
  `RoleRouter` steps (relative steps / absolute deltas, up to 8 per message), `ProfileWriter`;
  wizard steps "clips encoder" / "tracks encoder"; window redesign (group strip, progress line,
  sunken dial, readout + activity light, learned chips, footer); smoke + harness checks
- [X] T098c [US5] Pairs as an encoder or two buttons (FR-030c): `ControlRole.LeftKind` + profile
  `roleLeft` (only the other half: `PairOf`), `RoleRouter` two-way roles; wizard pair steps (encoder
  → both halves, a button → first half then "now press …"), `PictogramKind.Pair`; tempo − / +
  commands (`transport.tempoDown/Up`, 1 BPM); smoke + harness checks
- [X] T099 [US8] Session commands `session.selectUp/Down/Left/Right`, `session.toggleSlot`,
  `session.stopTrack` (CommandCatalog + MainWindow + `SessionView.MoveSelection / ToggleSelection /
  StopSelectedTrack`); `ControllerService.SessionShown`: jog and track buttons walk the Session;
  KeyLab DAW profile: encoder push (CC 117) → `session.toggleSlot`; harness checks
- [X] T100 [US8] Docs EN + RU (wizard, Session), CHANGELOG, FEATURES, en.json keys

---

## Phase 9: Polish & cross-cutting (per PR, rows marked with their slice)

- [X] T080 [P] User docs via the `nota-docs` skill: new "Controllers" page EN + RU in `../nota-docs` (recognition, Preferences → Controllers, knob modes, pickup, Mackie Control, profile format with the contract example, setup wizard, page editor) — grow it with each PR from 2 to 7
- [X] T081 [P] `FEATURES.md` via the `nota-features` skill: a "Controller auto-mapping" entry under MIDI (grow per PR)
- [X] T082 [P] `.claude/skills/nota-instrument/SKILL.md`: add "declare controller pages in `Nota.Application/Controllers/DevicePageCatalog*.cs`" to the kind-registration checklist (T040 fails otherwise)
- [X] T083 Performance check (SC-005): with a test port injecting 8 faders + 8 knobs at ~100 msg/s each and MCU feedback on, run the CPU / xrun check from Preferences → Audio → Test and the live-note latency smoke case; also measure feedback latency — set track 3 volume from the engine and time until `controller_sent` holds the pitch bend (SC-004: ≤ 100 ms), and a bank-next until all 8 scribble SysEx are sent (≤ 200 ms); record all results in the PR description (PR 5) — 2026-11-19, MacBook (Apple silicon), headless harness with a real engine playing: 8 X-Touch knobs + 8 KeyLab MCU faders at ~1240 msg/s for 10 s → controller tick avg 1.0 ms, p99 3.0 ms, max 16.7 ms, **0 xruns**; track 3 volume set → its MCU pitch bend sent 0.3–1.3 ms after the next controller tick (in the app ≤ 33 ms, the UI tick: SC-004 ≤ 100 ms ✓); a channel step → the scribble-strip SysEx 0.5–11 ms after (≤ 200 ms ✓)
- [ ] T084 Run every section of [quickstart.md](quickstart.md) that the PR covers, on the KeyLab Essential 61 for slices 2–4

### Nota closing tasks *(every PR)*

- [X] T085 Run `scripts/build.sh` (or `scripts/build-linux.sh` / `scripts/build-win.ps1`) until native + managed + smoke test are green — 2026-11-19: native + managed + smoke green except the render-pool timing checks (`--perf`: four indivisible VST3 jobs, overloaded late blocks), which fail the same on `develop` on this machine under load (Rider at ~300 % CPU): run back to back, develop 135 / 144 late blocks, this branch 108 / 305; no render-path code changed
- [X] T086 AGPL-3.0-only SPDX two-line header on every new source file (see CONTRIBUTING.md) — includes `MidiSurface.*`, `MidiOutput*`, `MidiParser.h`, every file under `C/` and `A/`, `ControllerTools.cs`, `ControllerTests.cs`
- [X] T087 New dependency: N/A (RtMidi already listed in `LICENSES/third-party.md`; `RtMidiOut` is the same library) — confirm no community mapping data was used; each shipped profile has `source`
- [X] T088 `CHANGELOG.md` `[Unreleased]` entry for each user-visible PR (2–7); PR 1 → N/A
- [X] T089 `FEATURES.md` / docs / shortcuts: T080–T081; `nota-shortcuts` N/A (no keyboard bindings change)
- [X] T090 Engine behaviour change → smoke test extended: T015 + T017 (PR 1), T039 (PR 2), T068 (PR 5)

---

## Dependencies & Execution Order

### Phase dependencies

- **Setup (Phase 1)** → **Foundational (Phase 2)** blocks everything.
- **US1 (Phase 3)** depends on Phase 2.
- **US2 (Phase 4)** depends on US1 (router, service, claims).
- **US3 (Phase 5)** depends on US2 (knob targets to pick up); **US4 (Phase 6)** depends on US1 only
  — US3 and US4 may swap or run in parallel.
- **US5 (Phase 7)** depends on US2 (page source interface) and US1 (profiles).
- **US6 (Phase 8)** depends on US4 (surface model, differ).
- **Polish (Phase 9)** rows go with the PR they belong to.

### Within each story

Tests first (they fail), then pure `C/` logic, then `A/` wiring, then UI, then profiles / MCP.

### Parallel opportunities

- Phase 2: T006 ∥ T007 ∥ T008–T010 (separate backends); T020 ∥ T021 ∥ T017 once T014–T016 land.
- US1: T024 ∥ T025 ∥ T026 ∥ T027; T036 ∥ T037 (profiles) any time after T020.
- US2: T044 ∥ T045 ∥ T046 (catalog partials); T052 ∥ T053.
- US4: T060 ∥ T061 ∥ T062 ∥ T063.
- US5: T069 ∥ T070 ∥ T071 ∥ T072.

## Parallel Example: User Story 2

```bash
Task: "C/DevicePageCatalog.cs — pages for every instrument kind"
Task: "C/DevicePageCatalog.Effects.cs — pages for every audio effect kind"
Task: "C/DevicePageCatalog.Midi.cs — MIDI effects, racks, plug-in default"
Task: "A/Profiles/novation.launchkey-mk3.json"
Task: "A/Profiles/akai.mpk-mini-mk3.json"
```

## Implementation Strategy

### MVP first

1. Phases 1–2 (PR 1: engine port layer, invisible).
2. Phase 3 (PR 2: US1) → validate on the KeyLab Essential 61 → ship: "plug in and the transport
   and faders work".
3. Phase 4 (PR 3: US2) → the headline "knobs follow the device".

### Incremental delivery

PR 4 (pickup) and PR 5 (MCU) next, in either order; then PR 6 (make it mine) and PR 7 (HUI).
Each PR leaves the app releasable and carries its CHANGELOG / FEATURES / docs rows.

## Notes

- Do not run `openspec`-style store steps here — this repo uses Spec Kit under `specs/`.
- Keep `build/` a Release engine build when running the smoke test.
- The maintainer commits; leave changes uncommitted for review unless asked.
