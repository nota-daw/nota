# Feature Specification: Sidechain from individual pads, voices and chains

**Feature Branch**: `feat/sidechain-sub-sources`

**Created**: 2026-10-28

**Status**: Draft

**Input**: User description: GitHub issue [nota-daw/nota#190](https://github.com/nota-daw/nota/issues/190) —
"Sidechain from individual pads / voices / chains (Drum Rack, Rhythm, Instrument Rack)". A sidechain
key can only be a whole track today; a user wants a Compressor on a bass track to duck from just the
kick pad of a Drum Rack on another track, the way Ableton's sidechain picker lists every chain of a
rack with its own Pre/Post tap.

## Nota Context *(fill in — drives plan and tasks)*

**Feature area** *(pick the main one)*:

- [x] Engine (C++, `src/native/nota.engine`) — real-time audio rules apply, see `ARCHITECTURE.md` AR table
- [x] Managed app (C#, `src/managed/*`) — domain, application, infrastructure, presentation
- [x] UI (Avalonia, `src/managed/Nota.Presentation`) — follow `DESIGN.md` (Studio)
- [ ] Plugin hosting (`pluginhost/`) — the only place JUCE is allowed
- [ ] Build / packaging / CI (`scripts/`, `.github/`) — see `BUILD.md`
- [ ] Docs only

Main area: Engine (per-part capture inside the rack / Rhythm render and the sidechain routing),
with the persistence, MCP and picker UI on top.

**Nota docs to keep in mind** *(links relative to `specs/<slug>/`, where this file lands)*:
[`ARCHITECTURE.md`](../../ARCHITECTURE.md) (AR rules),
[`CONTRIBUTING.md`](../../CONTRIBUTING.md) (licensing, dependency policy, PR
expectations), [`BUILD.md`](../../BUILD.md), [`DESIGN.md`](../../DESIGN.md),
[`FEATURES.md`](../../FEATURES.md).

**Where this touches today's behaviour** (context for the plan, not a design):

- Sidechain keys are addressed by source track only; the engine mirrors each referenced source track
  into one of a fixed pool of 8 route slots, with a post-FX/post-fader and a pre-FX/pre-fader tap.
- Drum Rack, Instrument Rack and Nota Rhythm sum their parts inside the instrument, so no per-part
  signal exists outside it.
- Rack chains already carry a runtime-unique identity (used by lanes, CV links and CC routes that
  point into a chain), but that identity is not saved; saved references into a rack use the chain's
  position at save time.
- The engine already stores a sidechain key for devices *inside* a rack chain (for save / load), but
  the rack card does not let the user set one.

## Clarifications

### Session 2026-10-28

- Q: Should a muted (or non-soloed) part still feed its sidechain key — the "ghost kick"? → A: Yes.
  A part that some key references keeps rendering while muted / not soloed; it stays out of the mix
  but feeds both its Pre and Post taps. Unreferenced muted parts still skip rendering.
- Q: Do sub-sources share the route pool with whole-track sources, and what happens when it is full?
  → A: One shared pool, enlarged from 8 to 16 sources (whole tracks and parts alike). When no slot is
  free, sources not already in use show as disabled in the picker with the hint "No free routes".
- Q: Are devices inside rack chains enabled as sidechain consumers in this feature? → A: No — a
  separate follow-up; this feature only adds sub-sources.
- Q: Which tap does Nota Flux's React key use when its source is a part? → A: Always Post, as
  React does for a whole track today; Flux gets the sub-source tree but no Pre/Post control.
- Q: (from planning) Is choosing a sidechain source undoable today, and what happens to a key when
  its source is deleted? → A: Not undoable today, so FR-015 keeps that; a deleted source leaves the
  key in place but silent until undo restores the source — parts follow the same rule (FR-004).

## User Scenarios & Testing *(mandatory)*

### User Story 1 - Duck the bass from just the kick pad (Priority: P1)

A producer has a "Drums" track with a Nota Drum Rack (Kick, Snare, Hat pads) and a "Bass" track with
a synth followed by a Compressor. In the Compressor's sidechain source picker they expand "Drums",
pick "Kick", choose Pre or Post, and the bass now ducks only on kick hits. Snare and hat hits leave
the bass alone.

**Why this priority**: This is the reported use case and the reason the issue exists. It delivers the
whole value on its own for the most common setup (drum rack + bass), and every other story reuses
what it builds (sub-source addressing, per-part capture, the tree picker).

**Independent Test**: Build the two tracks above with a pattern where kick and hat never coincide,
key the Compressor off "Drums · Kick", play, and confirm gain reduction appears only on kick hits — in
the Compressor's GR meter, through Listen, and in a deterministic render.

**Acceptance Scenarios**:

1. **Given** a Drum Rack track "Drums" with Kick and Hat pads and a Compressor on "Bass",
   **When** the user opens the Compressor's sidechain source picker,
   **Then** "Drums" offers "Whole track" plus one entry per pad, labelled with the pad's name.
2. **Given** the Compressor is keyed to "Drums · Kick" with the Post tap,
   **When** the pattern plays,
   **Then** gain reduction follows kick hits only, and the hat pad never causes gain reduction.
3. **Given** the Compressor is keyed to "Drums · Kick" with the Pre tap and the Kick pad has an insert
   that changes its level (e.g. a Utility at −20 dB),
   **When** the pattern plays,
   **Then** the detector hears the kick before that insert (the level change does not alter the key),
   while with Post it hears the kick after its inserts and its pad gain/pan.
4. **Given** the Compressor is keyed to "Drums · Kick",
   **When** the user turns on Listen or looks at the source name on the card,
   **Then** the source reads "Drums · Kick" with the tap shown (e.g. "Drums · Kick pre").
5. **Given** the Compressor is keyed to "Drums · Kick",
   **When** the user picks "Drums ▸ Whole track",
   **Then** the key returns to today's whole-track behaviour.

---

### User Story 2 - The key stays on the same pad through edits and reloads (Priority: P1)

The user keeps working on the drum kit after setting the key: they reorder pads, add a clap pad,
delete the snare, rename the kick to "Kick 909", save, close and reopen the project. The Compressor
still keys off the same kick pad throughout.

**Why this priority**: A key that silently jumps to another pad after a reorder or a reload is worse
than no feature — the mix changes without the user noticing. Without this, Story 1 is not shippable.

**Independent Test**: Set a key to one pad, then perform reorder / insert / delete-other / rename /
save-reopen, and after each step confirm the GR still follows that pad and the picker still shows it
selected.

**Acceptance Scenarios**:

1. **Given** a key on "Drums · Kick", **When** the user moves the Kick pad to another position or
   another note, **Then** the key still follows the Kick pad.
2. **Given** a key on "Drums · Kick", **When** the user adds or deletes *other* pads, **Then** the key
   still follows the Kick pad.
3. **Given** a key on "Drums · Kick", **When** the user renames the pad, **Then** the source name on
   the consuming device updates to the new pad name.
4. **Given** a key on "Drums · Kick", **When** the user deletes the Kick pad, **Then** the device
   hears no key (as it does today when a source track is deleted) and its source reads as missing;
   undo of the delete brings the pad back and the key works again.
5. **Given** a key on "Drums · Kick", **When** the user replaces the Drum Rack on "Drums" with an
   instrument that has no parts, **Then** the device hears no key and its source reads as missing, as
   in scenario 4.
6. **Given** a saved project with a key on a pad, **When** it is reopened, **Then** the key points at
   the same pad with the same tap.
7. **Given** a project saved by an older version with whole-track keys, **When** it is opened,
   **Then** every key loads unchanged as "Whole track".

---

### User Story 3 - Key off a Nota Rhythm voice (Priority: P2)

A user drives drums with Nota Rhythm. They key a Compressor (or Auto Filter, Shutter, …) on a pad
track off Rhythm's kick voice only.

**Why this priority**: Rhythm is Nota's own drum machine and has the same problem; its voices are
fixed (8), so it is a smaller slice once Story 1 exists.

**Independent Test**: A Rhythm track with kick and hat voices playing on alternate steps; key a
Compressor off "Rhythm · Voice 1 (Kick)"; GR follows only that voice.

**Acceptance Scenarios**:

1. **Given** a Nota Rhythm track, **When** the user opens a sidechain picker on another track,
   **Then** the Rhythm track offers "Whole track" plus its 8 voices, each labelled with its number and
   the voice's current name/model.
2. **Given** a key on one Rhythm voice with Pre, **When** it plays, **Then** the detector hears that
   voice before its voice-FX chain; with Post, after its voice-FX and its level/pan, before the voices
   are summed into Rhythm's Glue/Volume stage.

---

### User Story 4 - Key off an Instrument Rack chain (Priority: P2)

A user has an Instrument Rack layering a sub-bass chain and a pad chain on one track and wants a
device on another track to react only to the sub-bass chain.

**Why this priority**: Same mechanism as Story 1 for the other rack type; less common than drums.

**Independent Test**: An Instrument Rack with two chains playing different notes; key a device off one
chain; it reacts only to that chain.

**Acceptance Scenarios**:

1. **Given** an Instrument Rack track, **When** the user opens a sidechain picker elsewhere,
   **Then** the track offers "Whole track" plus one entry per chain, labelled with the chain name.
2. **Given** a key on one chain, **When** the rack's chains are reordered, added or deleted (other than
   the keyed one), **Then** the key follows the same chain (as in Story 2).

---

### User Story 5 - Every sidechain consumer gets sub-sources (Priority: P2)

Whatever device the user keys — Compressor, Auto Filter, Auto Gain, Auto Shift, Ceiling, Dynamic EQ,
Prism, Shutter, Nota Vocoder's modulator, a hosted plug-in's sidechain input, or Nota Flux's React —
the source picker offers the same tree with sub-sources. Devices keep their Pre/Post control; Nota
Flux's React has none and always listens to Post.

**Why this priority**: Consistency: one picker everywhere. Each consumer is cheap once the picker and
routing exist.

**Independent Test**: For each consumer, key it off a Drum Rack pad and confirm it reacts only to
that pad.

**Acceptance Scenarios**:

1. **Given** any device that accepts a sidechain key (or Nota Flux's React), **When** the user opens
   its source picker, **Then** it shows the same tree of tracks and sub-sources.
2. **Given** Nota Flux's React keyed off "Drums · Kick", **When** the pattern plays, **Then** React
   follows the kick pad's Post signal only, and the card offers no Pre/Post choice.
3. **Given** an AI agent connected over MCP, **When** it sets or reads a device's sidechain source,
   **Then** it can address a sub-source (track + part) and read back which part is selected.

---

### Edge Cases

- **Muted or non-soloed part ("ghost kick")**: a muted pad / chain / voice, or one silenced because
  another is soloed, still feeds its key (Pre and Post) when some key references it; it stays out of
  the mix. An unreferenced muted part keeps skipping its render as today.
- **Muted source track**: a muted (or solo-gated) source track keeps rendering while it is keyed, as
  today. Part taps sit inside the instrument, before the track's own devices and fader, so — like
  today's whole-track Pre tap — they stay live while the track is muted.
- **Route budget exhausted**: whole tracks and parts share one pool of 16 sources. When it is full,
  every source not already in use is disabled in the picker with the hint "No free routes"; sources
  already in use stay selectable for further consumers (several consumers keying the same part share
  one route).
- **Devices inside rack chains as consumers**: out of scope — a Compressor *inside* a Drum Rack pad's
  chain still cannot be keyed; tracked as a separate follow-up.
- **Nested racks**: a Drum Rack pad that itself holds an Instrument Rack — only the outer pad is a
  sub-source; the inner rack's chains are not listed.
- **Keying a part of the device's own track**: the existing rule for a track keying itself stays as it
  is today; sub-sources of the consumer's own track follow the same rule.
- **Source track deleted or its instrument bypassed / turned off**: same as today's whole-track
  behaviour (deleted → no key until undo brings it back; silent instrument → silent key).
- **Frozen source track**: a frozen track plays its freeze buffer and does not render its parts, so
  part keys on it are silent while it is frozen; whole-track keys work as today. The picker marks a
  frozen track's parts as unavailable.
- **Pad with no sound loaded / chain with no instrument**: listed, produces a silent key.
- **Audio Effect Rack chains**: not sub-sources in this feature (they are effect chains on a track,
  not sounding parts of an instrument).
- **Hosted plug-ins with multiple outputs**: out of scope (listed in the issue as "later").
- **Clipboard / duplicate**: duplicating the consuming device or the whole consuming track keeps the
  sub-source key; duplicating the *source* track does not re-point existing keys to the copy.
- **Undo**: choosing a sidechain source is not an undo step today; picking a part keeps that
  behaviour (making key edits undoable is a separate change).

## Requirements *(mandatory)*

### Functional Requirements

**Sources**

- **FR-001**: A sidechain source MUST be either a whole track (today's behaviour) or one sounding part
  of the instrument on a track ("sub-source").
- **FR-002**: Sub-sources MUST exist for: each pad (chain) of a Nota Drum Rack, each chain of a Nota
  Instrument Rack, and each of the 8 voices of Nota Rhythm. Only the first level of a rack is offered.
- **FR-003**: A sub-source MUST keep pointing at the same part when other parts are added, deleted or
  reordered, when the part is moved or renamed, and across save / reopen.
- **FR-004**: When the referenced part (or the instrument that holds it) is removed, the consumer
  MUST hear no key and show its source as missing — as when a source track is deleted today — and an
  undo that brings the part back MUST make the key work again. A project saved meanwhile stores no
  source for that key.

**Taps**

- **FR-005**: For a sub-source, the Pre tap MUST deliver the part's raw signal: the part's instrument
  output before its own insert chain (Drum Rack pad / rack chain inserts; Rhythm voice-FX) and before
  any per-pad shaping applied after the instrument (the Drum Rack pad's decay envelope).
- **FR-006**: For a sub-source, the Post tap MUST deliver the part's signal after its insert chain and
  its own gain/pan (and, for a Drum Rack pad, its decay envelope) — exactly what the part contributes
  to the sum — before it is summed with the other parts (before rack volume / Rhythm Glue and Volume).
- **FR-007**: Whole-track sources MUST keep today's Pre/Post meaning unchanged.
- **FR-007a**: A part referenced by at least one key MUST keep producing its Pre and Post key signal
  while it is muted or silenced by another part's solo, without being heard in the mix. Parts no key
  references MUST keep today's mute/solo behaviour (no render).

**Route budget**

- **FR-007b**: Whole-track and sub-source keys MUST share one pool of 16 distinct sources; several
  consumers keying the same source and tap combination MUST share one route.
- **FR-007c**: When the pool is full, the picker MUST show every source not already in use as
  disabled with the hint "No free routes"; already-used sources stay selectable. Loading a project
  that needs more than 16 sources MUST keep the first 16 and clear the rest, with a one-time notice.

**Consumers and UI**

- **FR-008**: Every sidechain consumer — all devices that accept a sidechain key (built-in and hosted
  plug-in sidechain inputs) and the instrument-level key of Nota Flux (React) — MUST be able to select
  sub-sources. Nota Flux's React MUST always listen to the Post tap of its source (whole track or
  part) and shows no Pre/Post choice.
- **FR-009**: The source picker MUST present sources as a tree: each track, and under tracks whose
  instrument has parts, "Whole track" followed by the parts in their current order with their current
  names. Tracks without parts stay single entries as today.
- **FR-010**: The consuming device MUST show the selected source as "Track · Part" with the tap
  (e.g. "Drums · Kick pre") on the card and in the Listen readout.
- **FR-011**: Only parts that are actually used as a key MAY cost extra processing; a project with no
  sub-source keys MUST perform as it does today.

**Persistence, automation, agents**

- **FR-012**: The selected sub-source and tap MUST be saved with the project and restored on load.
- **FR-013**: Projects from older versions MUST load with every key as "Whole track", unchanged in
  sound.
- **FR-014**: The engine's public interface MUST let a client set and read a sub-source key for a
  device and for an instrument-level (React) key, and the MCP device tools MUST expose the same.
- **FR-015**: Choosing a part behaves like choosing a track today with respect to undo (not an undo
  step); the plan MUST NOT make whole-track keys behave differently.

**Verification**

- **FR-016**: The end-to-end smoke test MUST cover a deterministic render of a Drum Rack with a kick
  pad and a hat pad on alternating steps, keyed by a Compressor on another track via the kick pad, and
  show gain reduction on kick hits and none on hat hits — for both Pre and Post.

### Key Entities

- **Sidechain source**: what a consumer listens to — a source track, an optional part of that track's
  instrument, and a tap (Pre / Post). "No part" means the whole track.
- **Part (sub-source)**: one sounding element of a multi-part instrument — a Drum Rack pad, an
  Instrument Rack chain, or a Rhythm voice. Has a stable identity within its instrument, a display
  name, and an order position (which may change without changing identity).
- **Sidechain consumer**: a device that accepts a key (or an instrument with a React key); holds one
  sidechain source plus its existing shaping settings (gain, mix).

## Success Criteria *(mandatory)*

### Measurable Outcomes

- **SC-001**: In the reported setup (Drum Rack kick + hat, Compressor on a bass track keyed off the
  kick pad), hat hits cause no gain reduction (below 0.1 dB) while kick hits cause the same reduction
  as a whole-track key on a kick-only track, for both Pre and Post.
- **SC-002**: A user can switch a key from a whole track to one pad in at most 3 interactions from the
  device card (open picker, expand track, pick part; tap stays as set).
- **SC-003**: After any sequence of add / delete-other / reorder / rename / save-reopen, the key still
  follows the originally chosen part in 100% of the covered test cases.
- **SC-004**: 100% of projects saved by previous versions open with their sidechain keys and their
  rendered output unchanged.
- **SC-005**: A project with no sub-source keys shows no measurable change in audio-thread CPU load
  versus the previous version; each active sub-source key adds no more cost than a whole-track key.
- **SC-005a**: A muted kick pad referenced by a key produces no audible output on the source track
  yet drives the same gain reduction as when unmuted.
- **SC-006**: All sidechain consumers listed in FR-008 can be keyed off a sub-source, verified per
  consumer.

## Assumptions

- Racks: only the first level of a Drum Rack / Instrument Rack is offered as sub-sources; nested racks
  inside a pad are reached through the outer pad.
- Hosted plug-ins with multiple output buses are a later feature, not part of this one.
- Devices inside rack chains as sidechain consumers are a separate follow-up (Clarifications
  2026-10-28).
- Audio Effect Rack and MIDI Rack chains are not sub-sources.
- Rhythm voices are identified by their fixed slot (1–8); their labels show the current voice name.
- The rule about a track keying itself is unchanged and applies to its sub-sources too.
- Saved projects need a format change with a migration; old whole-track keys map to "no part".
- Nota Auto Shift is not offered parts: its key is the source track's MIDI notes (its MIDI
  target), and a pad or voice has no notes of its own.
- Each card keeps its own picker style (dropdown, flyout or menu); parts appear under their track
  in each — indented in lists, as "Track · Part" in menus.
- Pre/Post remain a per-consumer setting (as today), not a per-part one; Nota Flux's React has no
  such setting and always uses Post.
- The tree picker follows the existing Studio picker styling (`DESIGN.md`); no new design language.

## Nota Compliance Checklist *(review before implementation; re-check in tasks.md)*

- [x] Every new source file will carry the AGPL-3.0-only SPDX header (two lines, see CONTRIBUTING.md)
- [x] No new dependency — or: license checked, AGPL-compatible, `LICENSES/third-party.md` row planned
- [x] Audio-thread impact considered: no alloc / lock / I/O on the audio thread (AR-6), command queue for data in (AR-4), immutable snapshots for structural edits (AR-5) — per-part capture buffers pre-allocated; part selection published with the routing snapshot
- [x] Managed/native crossings go through the pure C ABI in `nota_engine.h` only (AR-7); no JUCE outside `pluginhost/`
- [x] Smoke-test impact assessed: engine behaviour change → extend `tests/Nota.SmokeTest` (FR-016)
- [x] User-visible change → `CHANGELOG.md` `[Unreleased]` entry planned (and FEATURES.md / user docs via the nota-features / nota-docs skills when working with an agent that has them)
- [x] UI change → follows `DESIGN.md` (Studio design language)
