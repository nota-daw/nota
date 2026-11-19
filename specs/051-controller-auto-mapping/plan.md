# Implementation Plan: Controller auto-mapping — profiles, device-follow knobs, Mackie Control

**Branch**: `feat/controller-auto-mapping` | **Date**: 2026-10-29 | **Spec**: [spec.md](spec.md)

**Input**: Feature specification from `specs/051-controller-auto-mapping/spec.md`

## Summary

Make a connected MIDI controller useful with no setup: recognise it (Identity Reply / port name)
against JSON **controller profiles**, turn its controls into **roles** (transport, faders on a bank
of eight tracks, knobs on the **focused device's page** or in Volume / Pan / Send mode), stop
parameters jumping with **pickup**, and drive **Mackie Control** (later HUI) surfaces with full
feedback. Users can add profiles with a **Controller setup** wizard and edit device pages (global,
with a per-project copy).

Technical approach (see [research.md](research.md)): the engine gains a MIDI **port layer** — port
identity, a lock-free **claim filter** on the MIDI thread so role messages never reach instruments,
SysEx in, and MIDI / SysEx **output** — exposed through new C ABI calls. Everything else is managed:
pure, smoke-testable logic in `Nota.Application/Controllers/` (profiles, decoding, pickup, routing,
pages, MCU / HUI codecs) and a `ControllerService` in `Nota.App` that drives it on the UI tick and
applies values through the same path MIDI Learn uses (one undo step per movement, latching
automation).

Delivery follows clarification 1: one spec, several PRs in priority order (see *Delivery slices*).

## Nota: Read Before Planning

Consult the matching source of truth before designing:

- **Engine work** → [`ARCHITECTURE.md`](../../ARCHITECTURE.md): the AR rules table is binding (AR-4 lock-free queue, AR-5 immutable snapshots, AR-6 audio-thread restrictions, AR-7 pure C ABI, AR-9 `Transport` is the only source of musical time).
- **UI work** → [`DESIGN.md`](../../DESIGN.md) (Studio design language) and the `nota-design` skill.
- **A built-in instrument / audio or MIDI effect** → the `nota-instrument` skill (DSP class, kind registration across C++ and C#, device-card UI). Here: every built-in device gets a page declaration in `DevicePageCatalog`; a new device added later must add one (note for the skill).
- **Build / packaging** → [`BUILD.md`](../../BUILD.md).
- **Anything** → [`CONTRIBUTING.md`](../../CONTRIBUTING.md): licensing, dependency policy, PR expectations.

## Technical Context

**Language/Version**: C++20 (engine MIDI I/O, claim filter, ABI), C# / .NET 10 (everything else).

**Primary Dependencies**: Existing only — CoreMIDI (macOS), RtMidi 6.0.0 (`RtMidiIn` + now
`RtMidiOut`, WinMM / ALSA; already in `LICENSES/third-party.md`), `System.Text.Json`, Avalonia.
**No new dependency.**

**Storage**:
- Shipped profiles — `EmbeddedResource`s, `src/managed/Nota.App/Controllers/Profiles/*.json`.
- User profiles — `<DataDir>/controllers/*.json`.
- User device pages — `<DataDir>/controllers/pages.json`.
- Per-port choices (profile override, on/off, pickup on/off) — app settings (`Settings`).
- Project copy of device pages — bundle sidecar `devicepages.json` (next to `midimap.json`).
- Bank / page / knob mode — session state only, not saved.

**Audio-Thread Impact**: **None on the audio thread.** On the MIDI input thread: one lookup in an
immutable, atomically-published claim table per message (no alloc, no lock — AR-6); claimed
messages go to a new SPSC surface queue (AR-4) instead of `MpeInput`. SysEx copied into a fixed
ring. MIDI output is sent from the managed UI thread. Unclaimed traffic follows today's path
unchanged.

**Testing**: `tests/Nota.SmokeTest` — new native `nota_midi_surface_selftest` (claim filter,
SysEx reassembly, output loopback) + managed cases for `Nota.Application/Controllers` (profile
loading / matching, decoders, pickup, router, pages, MCU / HUI codecs, feedback differ). MCP
`controller_inject` (test-gated) for headless app runs. Manual: Arturia KeyLab Essential 61; virtual
ports for the rest ([quickstart.md](quickstart.md)).

**Target Platform**: macOS (CoreMIDI), Windows (WinMM via RtMidi), Linux (ALSA via RtMidi) —
wherever MIDI input works today.

**Project Type**: Desktop app (native engine + managed app).

**Performance Goals**: SC-002 knob re-target < 50 ms (next UI tick); SC-004 feedback ≤ 100 ms,
full bank refresh ≤ 200 ms; SC-005 no xruns / no added live-note latency under continuous
8-fader + 8-knob streams with feedback.

**Constraints**: Claim lookup O(1); surface queue 1024 events; SysEx ≤ 512 B per message (longer
dropped, logged); feedback ≤ one message per changed value per tick, ≤ 8 scribble SysEx per tick;
Identity Reply timeout 500 ms.

**Scale/Scope**: ≤ 16 MIDI sources (existing `kMaxSources`); typically 1–3 controllers; ~60 built-in
device types needing page declarations; 5 seed profiles.

## Constitution Check

*GATE: Must pass before Phase 0 research. Re-check after Phase 1 design.*

| Principle | Status | How |
|-----------|--------|-----|
| I. Real-time audio safety | ✅ | Nothing new on the audio thread. MIDI-thread work = O(1) lookup in an immutable snapshot + SPSC push; tables published atomically, retired on the message thread (AR-4/5/6). Output never from the audio thread. |
| II. Boundary discipline | ✅ | New calls are plain C in `nota_engine.h` ([contracts/c-abi.md](contracts/c-abi.md)); no JUCE involved (MIDI is outside `pluginhost/`). |
| III. Licensing hygiene | ✅ | No new dependency; SPDX headers on new files; seed profiles from manufacturer documents, `source` per profile; no Ableton Remote Script data. |
| IV. Verification gate | ✅ | `nota_midi_surface_selftest` + managed smoke cases; each PR extends the smoke test for what it adds. |
| V. One logical change per PR | ✅ | Seven slices below, each a coherent change that leaves the app releasable. |
| VI. Docs in sync | ✅ | CHANGELOG entry per user-visible slice; `nota-features`, `nota-docs` (new "Controllers" page EN + RU), `nota-shortcuts` not affected (no keyboard bindings). |
| VII. Spec-first | ✅ | This spec / plan / tasks. |
| VIII. Rules not weakened | ✅ | No exception requested. |

No violations → *Complexity Tracking* empty.

## Delivery slices (one PR each, in order)

| # | Slice | Stories / FRs | Ships |
|---|-------|---------------|-------|
| 1 | **Engine MIDI port layer** — port list with outputs, claim filter, surface queue, SysEx in/out, output send, ports generation, self-test | foundation for FR-002, 004, 011, 022–024 | ABI + smoke; no UI change |
| 2 | **Profiles, recognition, roles** — `Controllers` core (model, loader, matcher, decoder, router), `ControllerService`, `ControlApplier` extraction, transport + fader roles with banks, Preferences → MIDI "Controllers" section, connect notice, KeyLab Essential 61 + X-Touch Mini profiles | Story 1; FR-001–013, 027–029 (partly) | user-visible |
| 3 | **Device-follow knobs** — focus event, `DevicePageCatalog` (all built-ins + racks + plug-in default), card marker + page indicator, page roles, knob modes (Volume / Pan / Send), rack-child targets, remaining seed profiles (Launchkey, MPK mini) | Story 2; FR-014–017, 016a | user-visible |
| 4 | **Pickup** — pickup state, ghost-tick hint on knobs / faders, per-controller switch | Story 3; FR-019–021 | user-visible |
| 5 | **Mackie Control** — `McuCodec`, strip / transport / bank / jog / time display, diff feedback, touch handling, track-bank marker, generic MCU profile, explicit "Use as Mackie Control" | Story 4; FR-022–025 | user-visible |
| 6 | **Make it mine** — Controller setup wizard, user profile reload / errors, device page editor, global `pages.json` + project `devicepages.json` | Story 5; FR-018, 018a, 030 | user-visible |
| 7 | **HUI** — `HuiCodec` on the slice-5 façade | Story 6; FR-026 | user-visible |

Slices 4 and 5 are independent after 3 and may swap. Each slice carries its CHANGELOG / FEATURES /
docs updates.

## Project Structure

### Documentation (this feature)

```text
specs/051-controller-auto-mapping/
├── plan.md              # this file
├── research.md          # Phase 0
├── data-model.md        # Phase 1
├── quickstart.md        # Phase 1
├── contracts/
│   ├── c-abi.md                 # new nota_engine.h calls
│   ├── controller-profile.md    # profile JSON format (user-facing contract)
│   ├── device-pages.md          # pages.json / devicepages.json format
│   └── mcp.md                   # MCP tools
├── checklists/requirements.md
└── tasks.md             # /speckit-tasks
```

### Source Code (repository root)

```text
src/native/nota.engine/
├── include/nota/nota_engine.h                 # + MIDI port / claim / surface / sysex / output calls
├── src/audio_io/MidiInput.h                   # + SysEx + raw callback, port info
├── src/audio_io/MidiOutput.h                  # NEW — output port interface
├── src/audio_io/macos/MidiInput.mm            # SysEx reassembly, notify proc (hot-plug)
├── src/audio_io/macos/MidiOutput.mm           # NEW — CoreMIDI output
├── src/audio_io/desktop/MidiInput_rtmidi.cpp  # SysEx (ignoreTypes), hot-plug poll
├── src/audio_io/desktop/MidiOutput_rtmidi.cpp # NEW — RtMidiOut
├── src/engine/MidiSurface.h/.cpp              # NEW — claim table, surface queue, sysex ring, self-test
├── src/engine/Engine.cpp / Engine.h           # callback consults MidiSurface before MpeInput
└── src/abi/nota_engine_audio.cpp              # ABI shims

src/managed/
├── Nota.Application/Controllers/              # NEW — pure logic (smoke-testable)
│   ├── ControllerProfile.cs                   # model + JSON loader / validator
│   ├── ProfileMatcher.cs                      # identity / port-name matching
│   ├── ControlDecoder.cs                      # encoder modes, 14-bit, NRPN
│   ├── Pickup.cs
│   ├── RoleRouter.cs                          # control → ControllerAction
│   ├── ControllerState.cs                     # bank, page, knob mode per controller
│   ├── DevicePageCatalog.cs                   # built-in pages + plug-in default + overrides
│   ├── DevicePagesStore.cs                    # pages.json / devicepages.json
│   ├── Mcu/McuCodec.cs, Mcu/SurfaceModel.cs, Mcu/FeedbackDiffer.cs
│   └── Hui/HuiCodec.cs
├── Nota.Application/Engine/IAudioEngine.cs    # + MIDI port / surface / output members
├── Nota.Infrastructure/Interop/NativeMethods.Audio.cs  # P/Invoke for the new ABI (+ NotaEngine wrappers)
├── Nota.App/Controllers/                      # NEW
│   ├── ControllerService.cs                   # UI-tick driver, recognition, dispatch, feedback
│   ├── ControlApplier.cs                      # extracted from MidiLearnService
│   ├── Profiles/*.json                        # shipped profiles (EmbeddedResource)
│   ├── ControllerSetupWindow.cs               # wizard (slice 6)
│   └── DevicePageEditor.cs                    # (slice 6)
├── Nota.App/MidiLearnService.cs               # uses ControlApplier; explicit mapping beats role
├── Nota.App/MidiLearn.cs                      # + MidiTarget kinds (loop, arm, select, rack-child)
├── Nota.App/DeviceChainView*.cs               # FocusChanged event
├── Nota.App/DeviceCards/…                     # knob marker + page indicator, pickup hint
├── Nota.App/PreferencesWindow.cs              # MIDI pane → "Controllers" section
├── Nota.App/Mcp/McpAppServices.cs             # controller access for MCP
└── Nota.Mcp/Abstractions.cs, Tools/ControllerTools.cs  # IControllerAccess + tools (next to MidiControlTools)

tests/Nota.SmokeTest/
└── ControllerTests.cs                         # NEW — managed cases + native self-test call
```

**Structure Decision**: Engine-side work is limited to MIDI I/O plumbing (`audio_io/`, a new
`engine/MidiSurface`). Protocol and policy live in `Nota.Application/Controllers` so the smoke test
covers them; `Nota.App/Controllers` composes them with the engine and the UI. Shipped profiles follow
the colour-theme `EmbeddedResource` pattern.

## Phase 1 design artifacts

- [data-model.md](data-model.md) — entities, fields, state transitions.
- [contracts/c-abi.md](contracts/c-abi.md) — native boundary.
- [contracts/controller-profile.md](contracts/controller-profile.md) — profile file format.
- [contracts/device-pages.md](contracts/device-pages.md) — page files.
- [contracts/mcp.md](contracts/mcp.md) — MCP tools.
- [quickstart.md](quickstart.md) — validation scenarios.

## Post-design Constitution re-check

Re-checked after Phase 1: the ABI contract keeps every new call C-only with engine-owned buffers or
caller buffers (II); the claim table / surface queue design in `c-abi.md` keeps the MIDI thread
alloc- and lock-free (I); profile `source` fields and the "no Remote Script data" rule are in the
profile contract (III); every slice names its smoke coverage (IV). **Pass.**

## Open items for implementation (not blocking the plan)

- KeyLab Essential 61 is the **mk3** (confirmed 2026-10-29); its DAW-mode protocol comes from
  Arturia's mk3 documentation (research R14).
- Verify undo coverage for rack-child parameters (research R10) when slice 3 starts.
- Launchkey MK4 profile only if its programmer's reference is public.

## Complexity Tracking

None.
