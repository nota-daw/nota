# Data model: MIDI routing (spec 115)

The model has two layers. The **project document** (managed, persisted) holds identities and
user choices. The **engine state** (native, runtime) holds only resolved indices and uids.
`MidiRouteResolver` maps the first onto the second whenever the project or the port list
changes.

## Project document (format 36)

### MidiInputRouteDto — one entry of a track's input list (FR-002, FR-006)

| Field | Type | Rule |
|---|---|---|
| `Uid` | string | The port's stable uid as `MidiInput::PortInfo::uid` reports it. Required |
| `Name` | string | Display name at save time, used to show a missing port. Never used for matching |
| `Channels` | int (16-bit mask) | Bit *n* = channel *n+1*. Must not be 0: the UI won't save an empty entry, and load drops one |

`TrackDto.MidiInputs: List<MidiInputRouteDto>?`

- `null` → the legacy fields apply unchanged: `MidiInputUid` (absent = All inputs) and
  `MidiInputChannel` (0 = all, 1–16). This is how v35 projects load (SC-008).
- Non-empty list → an explicit port list. The legacy fields are written for the first entry
  only, so a v35 reader would still see something sensible. Format 36 stops such a reader
  anyway.
- Uids are unique within one list; a duplicate merges its mask into the first entry on load.
- Picking All inputs or a single device in the UI writes `MidiInputs = null` and the legacy
  fields, so the old shortcuts round-trip exactly (FR-001).

### ExternalMidiDto — on `InstrumentDto` when `Kind == 19` (FR-007, FR-012)

| Field | Type | Rule |
|---|---|---|
| `OutputUid` | string? | Port target. Exactly one of `OutputUid` / `RoleId` is set, or neither (no destination yet) |
| `OutputName` | string? | Display name at save time, for "missing" display |
| `RoleId` | string? | Target role's `Id` |
| `Channel` | int | 1–16 = route channel; 0 = **Keep channels** (MPE member channels, R5). Default 1 |

### MidiOutputRoleDto — `ProjectDocument.MidiOutputRoles: List<MidiOutputRoleDto>?` (FR-013, FR-017)

| Field | Type | Rule |
|---|---|---|
| `Id` | string | GUID, stable across renames |
| `Name` | string | Unique within the project (case-insensitive), 1–40 characters, for example "Stage Synth" |
| `OutputUid` | string? | Bound port; null = unbound (role unavailable) |
| `OutputName` | string? | Display name at bind time |

Deleting a role that External MIDI tracks still use is refused with their names listed. The
user rebinds or re-targets them first.

## Managed runtime

### Availability (output of MidiRouteResolver)

`enum RouteAvailability { Available, Missing, Ambiguous, Unbound, OverLimit }`

- Input entry: `Available` (uid connected, within the limit), `OverLimit` (connected but not
  opened, R7), `Missing`, or `Ambiguous` (two connected inputs report the same uid).
- Output target: a port resolves like an input. A role resolves through its binding;
  `Unbound` means it has no port.
- The resolver pushes `resolvedUid = Available ? uid : ""` to the engine. The empty string
  means "send nothing".

### MidiRouteRow (MidiRoutesQuery → panel, MCP `get_midi_routes`) (FR-018, FR-019)

| Field | Meaning |
|---|---|
| `TrackId`, `TrackName`, `TrackColor` | Receiving or sending track, current name |
| `Inputs` | `All inputs`, or the list of (port name, channels text such as "3, 7, 11", availability) |
| `MidiFrom` | Source track name, `Removed track`, or none |
| `Destination` | Local instrument name, or External MIDI: port or role name, the bound port for a role, `Ch 5` or `Keep channels`, availability |

Filters: by source (a port or a track) matches rows whose inputs or `MidiFrom` contain it;
by destination (a port or a role) matches External MIDI rows that target it.

### Role rebind (FR-015, FR-016)

`MidiOutputRoles.PreviewRebind(roleId) → affected tracks` (External MIDI tracks whose
`RoleId` equals it). `Rebind(roleId, newUid)` checks that transport is stopped, sets the
role's binding, re-resolves and pushes the new uid to each affected instrument, and records
one undo step. Channels are untouched by construction: they live on the instrument, not on
the role.

## Engine state (native)

### Track input filter (replaces `midiInputSource_` / `midiInputChannel_`)

```text
std::atomic<bool>     midiAllInputs_ { true }
std::atomic<uint16_t> midiInMask_[MidiInput::kMaxSources] { 0 }
std::atomic<uint32_t> midiInGen_ { 0 }          // bumped on every change (release trigger)
std::vector<InputRouteUid> midiInputUids        // authoring copy (message thread): {uid, mask}
```

- `acceptsMidiFrom(src, ch) = all || (src >= 0 && src < kMaxSources && (mask[src] >> ch) & 1)`.
- Legacy setter `setMidiInput(source, channel)` maps to masks: all channels = `0xFFFF`,
  channel *n* = `1 << (n−1)`, `kMidiAllInputs` = all.
- Take lanes mirror this: `midiLaneAll_[k]`, `midiLaneMask_[k][kMaxSources]` and the `rt*`
  copies.

### LiveHeld owners (audio thread only)

`int8_t ownerSrc[128][4], ownerCh[128][4]; uint8_t ownerN[128]`. These replace
`source[128]` / `channel[128]`. A note-on adds its owner (when full, the oldest is replaced).
A note-off removes it, and the instrument note-off happens only when `ownerN` reaches 0. When
the generation changes, owners that no longer pass are removed; pitches whose count drops to
0 are ended. The held controllers of the removed (source, channel) pairs go back to rest
(spec 093 path).

### ExternalMidi instrument (kind 19)

| Member | Thread | Purpose |
|---|---|---|
| `std::atomic<uint64_t> configGen` + `pendingUid` / `pendingChannel` (set via command queue) | msg → audio | New destination. The audio thread pushes a `Config` event into the ring at the next block start |
| `SpscRingBuffer<OutMidiEv, 4096> ring` | audio → MIDI out | `{dueNs, kind: Bytes/Config/Flush, len, bytes[3] / configRef}` |
| `uint8_t mpeChan[128]`, LRU of member channels | audio | Keep channels: pitch → member channel 2–16 |
| `std::atomic<uint32_t> dropped` | audio → UI | Ring overflow count, shown on the card if > 0 |
| `std::atomic<int32_t> status` | MIDI out → UI | 0 no destination, 1 sending, 2 unavailable (open or send failed) |

Inside an Instrument or Drum Rack chain, kind 19 is not offered and `RackCore::makeInstrument`
refuses it. A chain's notes are a rack-internal stream, and spec 115 routes whole tracks.
Freeze and bounce skip External MIDI tracks: they have no audio to print.

### MidiOutService (engine-owned thread)

| State | Purpose |
|---|---|
| `std::map<std::string, std::unique_ptr<MidiOutput>> outputs` | Every open output; the only owner (R3) |
| `std::shared_ptr<const std::vector<RingRef>> rings` (atomic swap) | Rings of live External MIDI instruments |
| `SpscRingBuffer<Bytes, …> controlQueue` | Message-thread sends (`midiSend`: LEDs, SysEx) |
| `RouteBook books[instrumentUid]` | `{outputUid, notesOn[16][2]uint64, pedals[16], available}` (R4) |

**Route book transitions**

```text
          Config(uid≠"")            send fails / port gone
 (none) ───────────────▶ Sending ─────────────────────────▶ Unavailable
   ▲                      │  ▲                                  │
   │ Config("") or        │  └──── same uid back: Note Offs, pedals up,
   │ instrument removed   │        CC123 + CC64=0 per channel used ◀┘
   │ (book released at    │
   │  old output first)   └─ Config(other uid/channel/mode): release book at old output, clear, apply
```

The validation rules come from the spec: FR-005, FR-006, FR-010, FR-011 and the edge cases
on shared channels and channel-mode changes.
