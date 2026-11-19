# Research: Controller auto-mapping (spec 051)

Phase 0 of [`plan.md`](plan.md). Each entry: **Decision / Rationale / Alternatives**. Findings about
today's code are cited by path so the plan stays honest about what exists.

## What exists today (baseline)

- **MIDI input** — `src/native/nota.engine/src/audio_io/macos/MidiInput.mm` (CoreMIDI) and
  `src/audio_io/desktop/MidiInput_rtmidi.cpp` (RtMidi → WinMM / ALSA). One client, one input port,
  every enabled source connected; the source index (0..15) rides as the connection refCon.
  SysEx and system-common bytes are **skipped**; only channel-voice messages reach the callback.
- **Engine routing** — `Engine.cpp` (`midiInput_.open` lambda): every message goes to the per-source
  `MpeInput` (→ instruments) **on the MIDI thread**, and a *copy* of CC and note-on is pushed to the
  `midiControl_` SPSC queue as `ControlEvent{kind, channel, number, value}` — no source / port, no
  note-off, no pitch bend.
- **C ABI** — `nota_midi_refresh_devices`, `nota_midi_input_device_{count,uid,name}`,
  `nota_midi_set_input_enabled`, `nota_midi_apply`, `nota_midi_poll_control_events`. **There is no
  MIDI output and no SysEx in either direction.**
- **MIDI Learn** — `src/managed/Nota.App/MidiLearnService.cs`: drains control events each UI tick,
  binds or dispatches. `ApplyNormalized` is the one path that applies a value the way the on-screen
  control does: `BeginLatchWrite` (latching automation gesture) + `MixTouch` (one undo step per
  movement, 1 s pause, specs/009 + 016) + the engine setter. Mappings persist in the project bundle
  as `midimap.json`.
- **Device selection** — `Nota.Presentation/DeviceSelection.cs` (specs/044) owned privately by
  `DeviceChainView` (`_sel`, plus `_rackSel` for a device inside a rack, specs/042). No event leaves
  the view.
- **Parameter access** — `IAudioEngine`: top-level `DeviceParam*`, `PluginParam*` (normalized, with
  `PluginParamId`), `MidiEffectParam*`; rack-chain devices through `RackChainDeviceParam*`,
  `RackDevChainDeviceParam*`, `MidiRackChainDeviceParam*`, `RackChainInstrumentParam*`; built-in
  kind through `TrackDeviceBuiltinKind`, `TrackInstrumentKind`, `MidiEffectKind`; plug-in identity
  through `TrackDevicePluginId` / `TrackInstrumentPluginId`. Devices inside racks are **not**
  addressable by `MidiTarget` today (the nested proxies skip `MidiLearn.Bind`).
- **Shipped JSON precedent** — colour themes are `EmbeddedResource`s in `Nota.App.csproj`
  (`Theme\Themes\*.json`). Per-user data under `NotaPaths.DataDir` (`NOTA_DATA_DIR` for tests).
- **Smoke test** — references Nota.Application / Infrastructure / Presentation / Mcp / Remote and a
  few linked `Nota.App` files; it does **not** link `MidiLearnService`. Native self-tests
  (`nota_mpe_selftest`, `nota_engine_midi_ctl_selftest`) are the pattern for MIDI-thread logic.

## R1 — Native MIDI port layer (input identity, claim filter, SysEx, output)

**Decision**: Extend the engine's MIDI I/O, keeping the one-client model:

1. **Port identity**: expose the connected sources as *ports* with a stable uid, name, the paired
   output endpoint (same device / entity on CoreMIDI; same name on RtMidi) and a `source` index.
2. **Claim filter**: the managed side publishes, per source, a *claim set* — "whole port" (a
   control-surface port, e.g. MCU) or a list of (status, channel, data1) keys. It is an immutable
   table swapped in with an atomic pointer (old tables retired on the message thread, AR-5 style).
   The MIDI-thread callback looks a message up before `MpeInput`: claimed → pushed to a new
   **surface queue** (SPSC, `{source, status, channel, d1, d2}`) and *not* routed to instruments;
   unclaimed → today's path, unchanged.
3. **SysEx in**: complete SysEx messages from a source with a claim (or while an Identity Request is
   outstanding) are copied into a fixed-size SysEx ring (bounded length, e.g. 512 B per message;
   longer ones dropped) and drained with their source.
4. **Output**: open an output port; `send(port, bytes)` for short and SysEx messages, called from the
   managed UI thread (never the audio thread). CoreMIDI `MIDISend` / `MIDISendSysex` (or
   `MIDISendEventList`), RtMidi `RtMidiOut::sendMessage`.
5. **Hot-plug**: a CoreMIDI notify proc / RtMidi port-count poll bumps a *ports generation*
   counter; the managed side re-reads ports when it changes.

**Rationale**: Consumption (FR-011) can only happen where routing happens — on the MIDI thread
before `MpeInput`. A table lookup on a pre-built immutable snapshot keeps the callback alloc- and
lock-free (AR-6). Output from the UI thread keeps the audio thread untouched. RtMidi already ships
`RtMidiOut`, so no new dependency.

**Alternatives**: (a) Filter in managed code and re-inject unclaimed notes — adds UI-tick latency
to every note; rejected. (b) A second CoreMIDI input port for surfaces — needs the same claim logic
anyway and doubles connections; rejected. (c) A dedicated MIDI-output thread with a queue — not
needed: both backends' send calls are non-blocking for short messages; revisit if SysEx bursts
(scribble strips) show UI hitches.

## R2 — Where the logic lives

**Decision**: Pure, engine-free logic goes into a new `Nota.Application/Controllers/` namespace:
profile model + JSON loader/validator, profile matcher, message decoder (encoder modes, 14-bit,
NRPN), pickup, role router (control → logical action), bank / page / knob-mode state, device page
catalog, MCU and HUI codecs (bytes ↔ surface events / feedback). `Nota.App` gets a
`ControllerService` (UI-tick driver, engine wiring, feedback diffing), the shared value applier
extracted from `MidiLearnService`, and the UI. Native code only does port I/O and the claim filter.

**Rationale**: The smoke test can reach Nota.Application but not `Nota.App`, so this is what makes
Stories 1–4 testable end-to-end without hardware (Constitution IV). It also matches the layering:
Application = use cases, App = composition + views.

**Alternatives**: Putting it in `Nota.App` next to `MidiLearnService` — untestable by the gate;
rejected. Putting protocol logic in C++ — no real-time need, and harder to iterate; rejected.

## R3 — One value-apply path for MIDI Learn and controller roles

**Decision**: Extract `ApplyNormalized` / `Trigger` / `BeginLatchWrite` from `MidiLearnService`
into a `ControlApplier` (in `Nota.App`, over `IAudioEngine`) used by both MIDI Learn and the
controller roles; extend `MidiTarget` with the rack-child parameter kinds (R10) and with
`TransportLoop`, `TransportRewind/Forward`, `TrackArm`, `TrackSelect`.

**Rationale**: FR-010 demands the same undo / automation behaviour as the on-screen control; the
existing code already gets it right for MIDI Learn. One path means the two features cannot drift.

**Alternatives**: Duplicate the switch in the controller code — guaranteed drift; rejected.

## R4 — Profile file format

**Decision**: JSON, one file per controller, `"format": 1`. Shape in
[`contracts/controller-profile.md`](contracts/controller-profile.md). Shipped profiles are
`EmbeddedResource`s under `src/managed/Nota.App/Controllers/Profiles/*.json` (the themes pattern);
user profiles live in `<DataDir>/controllers/*.json`. Unknown fields are ignored (forward
compatible); a `format` above the supported one is refused with a message. Each shipped profile
carries a `source` field naming the manufacturer document it was built from (licensing audit,
FR-007).

**Rationale**: JSON is already the project's settings / sidecar format (`System.Text.Json`, no new
dependency), readable and diff-able for user sharing (FR-005).

**Alternatives**: YAML / TOML — new dependency; rejected. A binary or C#-coded table — not
user-editable; rejected.

## R5 — Recognition

**Decision**: On connect (or ports-generation change) for each new port: if it has an output, send
the Universal Identity Request `F0 7E 7F 06 01 F7` and wait up to **500 ms** for
`F0 7E <id> 06 02 <mfr 1|3 bytes> <family 2> <model 2> <version 4> F7`. Matching order:
user profiles before shipped; within a source, identity match (manufacturer + family [+ model])
before port-name match (regex, case-insensitive); ties → the more specific (more fields) wins, then
file name order. No reply → port name only. The chosen profile is remembered per port uid in
settings, together with the user's on/off switch (FR-006, FR-028), and the user can override the
choice (including "Mackie Control" on any port, FR-025).

**Rationale**: Identity Reply is the standard and survives renamed ports (Windows appends indices,
Linux ALSA names differ); port names are the fallback for controllers that do not answer
(edge case in the spec). 500 ms is well within SC-001's 10 s.

**Alternatives**: Port name only — fragile across OSes; identity only — misses non-answering
devices. Both rejected as sole mechanisms.

## R6 — Message decoding

**Decision** (`ControlDecoder`, per control declared in the profile):

| Mode | Raw → delta / value |
|------|---------------------|
| `absolute` | value / 127 (or / 16383 for 14-bit / NRPN) |
| `relative-twos` | 1..63 → +n, 65..127 → n − 128 |
| `relative-signbit` | 1..63 → +n, 65..127 → −(n − 64) |
| `relative-offset` (binary offset) | n − 64 |
| 14-bit CC | MSB on CC n (0..31) + LSB on n + 32; apply on MSB arrival using the last LSB, refine on LSB; if no LSB is ever seen, run at 7-bit |
| NRPN | 99 / 98 select, 6 (MSB) / 38 (LSB) data, 96 / 97 inc / dec; per-channel state |

Relative deltas scale by a fixed step (1/128 of range per tick) with acceleration = |n|
(the encoder's own magnitude). Pitch-bend controls (MCU faders) are 14-bit absolute.

**Rationale**: These are the four relative conventions in public controller charts (the issue lists
them). Decoding NRPN / 14-bit in managed code is safe because claimed messages never reach the
engine's reserved-CC refusal (`nota_engine.h`, specs/020); unclaimed ports keep today's behaviour.

**Alternatives**: Auto-detect relative mode at run time — ambiguous for small moves; only the setup
wizard detects it (R13), from a deliberate gesture.

## R7 — Soft takeover (pickup)

**Decision**: Per (control, target): store `lastPhysical` and `caught`. On any external change of
the target (focus / bank / page / mode switch, mouse, automation, undo — detected by comparing the
target's value with the last value this control wrote) set `caught = false`. While not caught, an
absolute movement applies only if `lastPhysical` and the new position straddle the current value,
or the new position is within **tolerance = 2 / 127** of it; then `caught = true`. Relative
controls and motorised faders are always caught. The pickup hint (FR-020) is a thin ghost tick on
the target control at the physical position, cleared when caught. Per-controller off switch →
always caught (jump).

**Rationale**: "Pickup" is the behaviour users know from Ableton / Bitwig; straddle detection
handles fast moves that skip over the value. 2/127 matches a knob's ±1-step jitter.

**Alternatives**: "Value scaling" (proportional catch-up) — smoother but surprising and harder to
test; rejected for v1.

## R8 — Device focus and parameter pages

**Decision**:

- `DeviceChainView` raises `FocusChanged(DeviceAddress?)` whenever its selection anchor, its rack
  selection or its track changes; `MainWindow` relays it to `ControllerService`. Focus rule
  (FR-016): the selection anchor; else the track's instrument; else its first device.
- `DeviceAddress` = track id + section (Midi / Instrument / Audio) + index + optional rack path
  (rack device index, chain, slot) for devices inside racks.
- A **`DevicePageCatalog`** (Nota.Application) holds the built-in pages, keyed by a *device type
  key*: `builtin:inst:<kind>`, `builtin:fx:<kind>`, `builtin:midi:<kind>`, `rack:audio`,
  `rack:inst`, `rack:midi` (racks → their macros), `plugin:<pluginId>`. Parameters are referenced
  by **stable name / id** (built-ins: parameter name; plug-ins: `PluginParamId`), resolved to an
  index when the focus lands, so a reordered parameter list does not shift a page.
- Plug-ins without pages: parameters in order, 8 per page (FR-015).
- User pages: `<DataDir>/controllers/pages.json`; each saved project keeps a copy of the pages for
  the device types it contains in a bundle sidecar `devicepages.json`; while that project is open,
  its copy wins (FR-018a). An edit writes both.

**Rationale**: Names / ids are what survive plug-in updates and engine parameter reorders; keying by
type matches the "every instance" clarification. Built-in pages live next to the managed device
cards (which already know their parameter names), not in the engine.

**Alternatives**: Pages declared in C++ per DSP class — needs ABI for metadata only the UI uses;
rejected. Index-based references — break on reorder; rejected.

## R9 — Knob modes and banks

**Decision**: Per connected controller: `KnobMode` (Device / Volume / Pan / Send n) and
`BankOrigin` (first track index of the bank). Banks walk the visible track order (regular, group
and return tracks; master excluded except the MCU master fader). Bank / channel shift clamps so the
bank never starts past the last track. Switching mode or bank invalidates pickup for all knobs.
The tracks under a controller are marked by a thin accent bar on their headers (FR-009), the knob
mode by the controller indicator's label.

**Rationale**: Mirrors common DAW behaviour and keeps the state per controller (two identical
controllers are independent, spec edge case).

## R10 — Parameters inside racks

**Decision**: Add `MidiTarget` kinds `RackChainDeviceParam`, `RackChainInstrumentParam` (and the
`RackDev…` / `MidiRack…` variants via the existing rack-index encoding) applied through the
existing rack-chain setters; their undo uses the same `MixMovement` mechanism with new `MixTarget`
kinds if the engine already records rack-child values (to verify in implementation — if it does
not, the value change is still applied but is coalesced into the rack's state undo as the card's
own knobs do today).

**Rationale**: The spec's edge case requires a device inside a rack to be controllable; the setters
exist, only the addressing is missing.

**Risk**: Undo coverage for rack-child parameters may differ from top-level ones; the task list
carries an explicit check.

**Checked (T048, 2026-11-19)**: the rack card's own knobs set rack-child parameters directly,
with no value undo and no automation lane. A controller knob does exactly the same
(`ControlApplier`, `MidiTargetKind.RackChainDeviceParam` / `RackChainInstrumentParam`).

## R11 — Mackie Control protocol

**Decision**: Implement the MCU subset in a pure codec (`McuCodec`) using the publicly documented
Mackie Control / Logic Control MIDI implementation:

- **Faders**: pitch bend on channels 1–8 (strips) and 9 (master), 14-bit; **touch** notes 104–112.
- **V-pots**: CC 16–23, relative sign-bit; **V-pot push** notes 32–39; **ring LEDs** CC 48–55.
- **Strip buttons**: Rec 0–7, Solo 8–15, Mute 16–23, Select 24–31 (note on 127 = press; LED = note
  on 127 / 1 / 0 for on / blink / off).
- **Bank / channel**: notes 46 / 47 (bank ←/→), 48 / 49 (channel ←/→).
- **Transport**: 91 rewind, 92 forward, 93 stop, 94 play, 95 record, 86 cycle (loop).
- **Jog**: CC 60, relative sign-bit.
- **Scribble strips**: SysEx `F0 00 00 66 14 12 <offset> <ASCII…> F7` (offset 0–55 top line,
  56–111 bottom line; 7 characters per strip); extenders use model byte `15`.
- **Time display**: CC 64–73 (7-segment digits, right to left); **meters**: channel pressure
  `(strip << 4) | level`.
- **Device query**: host sends `F0 00 00 66 14 00 F7` on connect; tolerate surfaces that skip the
  handshake.

Feedback runs on the UI tick (≈ 30 Hz): `ControllerService` keeps the last state sent per strip and
sends only differences (FR-023); a full refresh on connect, bank change and project load. While a
fader's touch note is down, no fader feedback is sent for that strip (FR-024) and the movement is a
latching automation gesture that ends on touch release.

**Rationale**: MCU is the de-facto protocol; the subset covers every FR-022 item. Diff-based
feedback bounds MIDI traffic and keeps SC-004 (≤ 100 ms) at a 33 ms tick.

**Alternatives**: Event-driven feedback hooks on every engine setter — wide, invasive change;
rejected for v1 (polling cached values is cheap: 8 strips × ~6 values).

## R12 — HUI

**Decision**: A second codec (`HuiCodec`) for the same strip / transport / bank model: zone-select
+ port-on/off button scheme (CC 0x0F zone, 0x2F port), faders as CC hi/lo pairs per strip
(0x00–0x07 / 0x20–0x27), V-pots CC 0x10–0x17, 4-character strip names via SysEx
`F0 00 00 66 05 00 10 <strip> <4 chars> F7`, and the HUI ping (note-on 0/0 every second, expect
reply). Lowest priority (Story 6, FR-026: SHOULD).

**Rationale**: Same façade as MCU means only the codec differs.

## R13 — Controller setup wizard (Story 5)

**Decision**: A wizard window steps through roles; for each it opens a capture window on the chosen
port (claim "whole port" during the wizard), takes the first deliberate movement, and classifies:
note vs CC vs pitch bend; 14-bit if a matching LSB CC n+32 follows; NRPN if 99/98 precede; relative
if values cluster around 1 / 127 or 63 / 65 / 64 (asks the user to "turn right, then left" for
encoders to tell two's-complement from sign-bit from binary offset). Output: a profile JSON in the
user folder; init SysEx and LED feedback are left for file editing (clarification 2).

## R14 — Seed profiles

**Decision**: Ship profiles for **Arturia KeyLab Essential 61 mk3** (hardware reference — confirmed with the maintainer on
2026-10-29), **Novation Launchkey MK3** (and MK4 if its programmer's reference is public),
**Akai MPK mini MK3**, **Behringer X-Touch Mini**, and **Generic Mackie Control**. Each profile cites
its manufacturer document in `source`. No Ableton Remote Script data; community mappings only after
an AGPL-compatibility check recorded in `LICENSES/third-party.md` (none planned). Profiles that
need an init SysEx (Launchkey DAW mode) take it from the manufacturer's programmer's reference.

**Rationale**: Matches the issue and clarification 5; documents keep the data clean for AGPL.

**Finding (T003, 2026-10-29)**: Arturia publishes no programmer's reference for the KeyLab
Essential mk3; its DAW control is **Mackie Control on a separate "MCU/HUI" port** (transport mode
MCU set in MIDI Control Center), standard MIDI on its "MIDI" port. So the reference device
exercises the MCU path: in PR 2 a `generic` profile describes the Mackie messages it sends
(transport notes, pitch-bend faders, relative-signbit encoders), and in PR 5 it moves to
`protocol: "mcu"`. The Akai MPK mini mk3 has no published default-CC chart (only its editor), and
Mackie publishes no wire spec — community protocol tables are used for facts only.

## R15 — Testing without hardware

**Decision**:

- **Engine**: `nota_midi_surface_selftest()` — injects bytes into the input-parse path with a claim
  table, checks claimed messages reach the surface queue and not `MpeInput`, unclaimed ones do the
  opposite, SysEx is reassembled, an output loopback buffer receives what `send` wrote. Called by
  the smoke test.
- **Managed**: smoke-test cases for the profile loader / matcher, decoders, pickup, router, page
  catalog, MCU / HUI codecs, and the feedback differ over a fake `IAudioEngine` state.
- **App-level**: an MCP tool `controller_inject` (dev builds / `NOTA_CONTROLLER_TEST=1` only)
  pushes raw bytes "from" a named virtual port through the same surface path, so headless runs can
  drive Stories 1–4 (FR-029).
- **Manual**: the KeyLab Essential 61 on real hardware; other devices via a CoreMIDI virtual source
  (macOS IAC bus) plus any MIDI monitor — see [`quickstart.md`](quickstart.md).

## R16 — Performance

**Decision**: Budgets: claim lookup O(1) per message (flat 16 × 3 × 16 × 128 bitset per source, or
"whole port" flag); surface queue 1024 entries; feedback ≤ 1 short message per changed value per
tick and ≤ 8 scribble SysEx (≤ 64 B) per tick; profile load / match off the UI tick (on connect).
SC-005 is checked by the existing CPU / xrun gate with a synthetic surface stream in the self-test.
