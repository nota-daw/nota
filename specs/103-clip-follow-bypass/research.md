# Research: Bypass one Session clip's Follow configuration

## R1 — Where the follow decision is made today

`Engine::sessionPreApply` (audio thread, `Engine_Session.cpp`) runs once per block for a
playing track. It returns early on a queued request, handles Repeat, then — if the global
switch is on and A or B is not None — compares `SessionPlayer::playedBeats` (beats since the
slot launched; reset by `maybeApply` on every launch and by a None outcome) with the follow
time `due` (`followBeats`, or one pass). Once `played + block/2 >= due` it picks A/B and queues
the target with quantum 0 (exact).

Consequence: if the check is merely skipped while bypassed, `playedBeats` keeps growing, and the
first block after the bypass ends satisfies `played >= due` → an immediate catch-up action. That
is the failure #499 names; the global switch has it today and keeps it (out of scope).

## R2 — Resume rule (clarified: next multiple of the follow time since the count started)

**Decision**: keep an audio-thread-only `followBase` in `SessionPlayer` (beats subtracted from
`playedBeats` before the comparison) and a `followHeld` flag. While the playing slot is bypassed
the audio thread sets `followHeld`. On the first block where the slot is no longer bypassed and
`followHeld` is set, it sets `followBase = floor(played / due) * due` and clears `followHeld`.
The next fire is then at `followBase + due`, the first multiple of `due` after `played`.
`followBase` and `followHeld` reset to 0 / false wherever `playedBeats` resets (launch in
`maybeApply`, None outcome).

- Bypass ended before the first deadline: `floor(played/due) = 0` → base 0 → fires at `due`,
  exactly as without the bypass (FR-006 second sentence).
- Bypass ended at beat 50 with due 32 → base 32 → fires at 64 (spec example).
- Re-enable lands exactly on a multiple (played = 64.0): base 64 → fires at 96, never "now".
  A block-centred tolerance (`+ block/2`) as in the fire check is not applied to the base, so the
  base never rounds up into the present block.
- `due` is read at resume time, so edits made to the follow time while bypassed take effect.

**Alternatives rejected**: mutating `playedBeats` (it also drives the fixed-length capture,
the recording elapsed readout and the automation chase flag on its first block — `== 0.0`);
having the message thread compute the base on toggle (it would race the audio thread's clock
and needs a new command; the slot snapshot already carries the flag, AR-5).

## R3 — Where the flag lives and how it travels

**Decision**: `bool followBypass` on `SessionSlot` (the immutable, published slot), set through
the existing `setSessionClipProps` clone-and-republish path. That path already pushes one undo
step (`republishWithTrack` → `pushUndo`), and slot copy / scene duplicate / scene move copy the
whole `SessionSlot`, so FR-010 and FR-011 come for free.

The C ABI struct `NotaSessionClipProps` gains `int32_t follow_bypass` at the end. It ends today
with `int32_t jump_scene` after a `double`, so the new field fills existing tail padding — the
struct stays 64 bytes (`follow_bypass` at offset 60). The C# mirror gains `int FollowBypass` at the end.

## R4 — Order of checks in `sessionPreApply`

Bypass handling goes before the queued-launch guard and the global-switch check, so the resume
base is taken when the clip is un-bypassed even while a launch is queued (and later cancelled,
e.g. its row removed) or the global switch is off at that moment. The bypass return itself comes
after the guard and after Repeat, so a queued launch and Repeat still take priority. The global switch keeps its
existing resume behaviour (Assumptions).

## R5 — Persistence

`SessionSlotDto.FollowBypass` (bool, default false). Format v35 with a no-op migration, in
line with every earlier additive field (an older app refuses a v35 file rather than silently
dropping the bypass). Written through `WithClipProps` / `ApplyClipProps`.

## R6 — UI idioms (DESIGN.md / nota-design)

- Inspector: the Follow action section head gets the same `SwitchTrack` the Session toolbar uses
  for the global Follow switch — on = this clip follows, off = bypassed. When off the head reads
  "Bypassed" and the section goes `Inactive.Set(..., interactive: true)` (still editable), the
  idiom used today for "Off globally". With the global switch off the switch shows dim
  (`SwitchTrack` already has a dim state) and the head keeps "Off globally" — the two states stay
  distinct.
- Grid: a bypassed clip whose A or B is not None draws the `Bypass` glyph (ring with a slash)
  left of the length readout in its top row, in a secondary ink.
- Context menu: "Bypass follow actions" / "Restore follow actions" in the filled-slot menu, next
  to Launch/Edit/Rename, with the `Follow` glyph.

## R7 — MCP

`get_session` clip records gain `FollowBypass`; `set_session_clip` gains `followBypass` (bool?,
null = unchanged). The tool description says bypass keeps the settings and holds the clip.
