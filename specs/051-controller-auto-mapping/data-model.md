# Data Model: Controller auto-mapping (spec 051)

Entities from [spec.md](spec.md) § Key Entities, made concrete. C# names are indicative; the
on-disk formats are fixed by [contracts/](contracts/).

## MidiPort *(native, read through the C ABI)*

| Field | Type | Notes |
|-------|------|-------|
| `Uid` | string | stable endpoint uid (CoreMIDI `kMIDIPropertyUniqueID`; RtMidi: port name + occurrence index) |
| `Name` | string | display / port name used for matching |
| `Source` | int 0..15 | index the MIDI thread tags events with |
| `HasOutput` | bool | a paired output endpoint was found |
| `OutputUid` | string | the paired output (empty if none) |

Ports are re-read when the native **ports generation** counter changes (hot-plug).

## ClaimTable *(native, per source; published from managed)*

| Field | Type | Notes |
|-------|------|-------|
| `WholePort` | bool | every channel message from this source is a role (MCU / HUI / wizard capture) |
| `Keys` | set of (status nibble, channel, data1) | claimed messages when not `WholePort`; pitch-bend / channel pressure keyed with data1 = 0 |

Published as one immutable snapshot for all sources; replaced atomically. Rule: claimed →
surface queue, not instruments; unclaimed → today's path.

## ControllerProfile *(file — [contracts/controller-profile.md](contracts/controller-profile.md))*

| Field | Type | Notes |
|-------|------|-------|
| `Format` | int | 1; higher → refused with message |
| `Id` | string | unique id, e.g. `arturia.keylab-essential-61` |
| `Name` | string | shown to the user |
| `Protocol` | enum `generic` / `mcu` / `hui` | `mcu` / `hui` → `WholePort` claim, codec instead of `Controls` |
| `Match` | `{ portNames[], identity{manufacturer, family?, model?} }` | at least one present |
| `Controls` | list of `ControlDecl` | generic protocol only |
| `Init` / `Release` | list of hex byte strings | sent on connect / on release |
| `Pickup` | bool (default true) | default for the controller's pickup switch |
| `Source` | string | manufacturer document the profile is built from (required for shipped) |
| `Origin` | `shipped` / `user` | derived from where it was loaded |

### ControlDecl

| Field | Type | Notes |
|-------|------|-------|
| `Role` | `ControlRole` | see below |
| `Message` | `cc` / `cc14` / `nrpn` / `note` / `pitchbend` | |
| `Channel` | 1..16 | |
| `Number` | int | CC / note / NRPN number (MSB CC for `cc14`) |
| `Encoder` | `absolute` / `relative-twos` / `relative-signbit` / `relative-offset` | default `absolute` |
| `Motorised` | bool | default false |
| `Feedback` | optional `{ on, off, value }` templates | LED / ring output |

**Validation**: duplicate (message, channel, number) → later declaration skipped with a warning;
unknown role / message → control skipped; `Number` out of range → skipped; file-level errors
(bad JSON, unsupported format, no `Match`) → profile skipped. All warnings listed in Preferences
(FR-005, Story 5 scenario 2).

### ControlRole *(enum + index)*

`Knob(n)`, `Fader(n)`, `Button(n)`, `Pad(n)`, `TransportPlay`, `TransportStop`, `TransportRecord`,
`TransportLoop`, `TransportRewind`, `TransportForward`, `PageNext`, `PagePrev`, `BankNext`,
`BankPrev`, `TrackNext`, `TrackPrev`, `KnobModeDevice`, `KnobModeVolume`, `KnobModePan`,
`KnobModeSendNext`, `MasterFader`, `Jog`.
`n` is 1-based in files, 0-based in code. `Pad(n)` and `Button(n)` are informational: never
claimed (pads keep playing notes; a generic button has no built-in action in format 1 and stays
free for MIDI Learn).

## ConnectedController *(runtime, `ControllerService`)*

| Field | Type | Notes |
|-------|------|-------|
| `Port` | MidiPort | |
| `Profile` | ControllerProfile? | null = plain MIDI |
| `Enabled` | bool | persisted per port uid |
| `PickupOn` | bool | persisted per port uid; default from profile |
| `BankOrigin` | int | first track index of the bank (session) |
| `Page` | int | current page of the focused device (session; reset on focus change) |
| `KnobMode` | `Device` / `Volume` / `Pan` / `Send(k)` | session; default `Device` |
| `Pickup` | map (control → PickupState) | |

**State transitions**:

```text
Discovered ──(identity request sent)──► Identifying ──(reply or 500 ms timeout)──► Matched | Unmatched
Matched ──(Enabled)──► Active: Init sent, claims published, roles live
Active ──(user switches off / profile removed)──► Released: Release sent, claims withdrawn
Active ──(port disappears)──► Gone (state kept in memory for re-plug: bank, page, mode)
Gone ──(same uid reappears)──► Identifying … ► Active (bank/page/mode restored, full feedback)
```

## PersistedControllerChoice *(Settings, per port uid)*

`ProfileId?` (user override, `"none"` = plain MIDI, `"mcu"` / `"hui"` = forced protocol),
`Enabled`, `PickupOn`.

## DeviceAddress *(runtime)*

`TrackId`, `Section` (Midi / Instrument / Audio), `Index`, `RackPath?` (`RackDevice`, `Chain`,
`Slot`) — a device inside a rack. Produced by `DeviceChainView.FocusChanged`.

## DeviceTypeKey *(string)*

`builtin:inst:<kind>`, `builtin:fx:<kind>`, `builtin:midi:<kind>`, `rack:audio`, `rack:inst`,
`rack:midi`, `plugin:<pluginId>`.

## DevicePage

| Field | Type | Notes |
|-------|------|-------|
| `Name` | string? | optional page label ("Filter", "Env") |
| `Params` | list (≤ 8) of `ParamRef` | `null` entries leave a knob empty |

`ParamRef`: built-ins → parameter **name**; plug-ins → `PluginParamId`; racks → `macro:<n>`.
Resolved to an index when focus lands; unresolved refs → empty knob (spec edge case).

## DevicePageSet

`TypeKey` → ordered `DevicePage[]`. Sources, in precedence order while a project is open:
**project copy** (`devicepages.json`) → **user global** (`pages.json`) → **built-in catalog** /
**plug-in default** (parameters in order, 8 per page).
Edit → writes user global and the open project's copy (FR-018 / 018a). Reset → removes the type
from both.

## PickupState *(per control × target)*

| Field | Type | Notes |
|-------|------|-------|
| `Target` | resolved target id | changes on focus / bank / page / mode → reset |
| `LastPhysical` | double 0..1 | |
| `LastWritten` | double? | value this control last applied |
| `Caught` | bool | |

Transitions: target changes or `target value ≠ LastWritten` (external change) → `Caught = false`;
move straddles / within 2/127 of the value → `Caught = true`. Relative / motorised → always caught.

## SurfaceStripState *(MCU / HUI feedback cache, per strip)*

`TrackId?`, `FaderPos` (14-bit), `PanRing`, `Mute`, `Solo`, `Arm`, `Select`, `Name` (7 chars),
`Touched` (fader touch note down), `Meter`. Plus global: `Transport` LEDs, `TimeDisplay` digits,
`BankOrigin`. The differ compares the cache with engine state each tick and emits only changes;
`Touched` suppresses fader output for that strip.

## ControllerAction *(router output)*

`SetNormalized(target, value)`, `Nudge(target, delta)`, `Trigger(target)`, `PageStep(±1)`,
`BankStep(±8 | ±1)`, `SetKnobMode(mode)`, `Jog(delta)`, `TouchBegin(target)` / `TouchEnd(target)`.
`ControllerService` maps targets to `MidiTarget`s and runs them through `ControlApplier`.
