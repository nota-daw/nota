# Bug Assessment: Held notes and release tails cut at the loop seam; notes recorded across it shrink

- **Slug**: loop-seam-note-cut
- **Created**: 2026-10-16
- **Source**: https://github.com/nota-daw/nota/issues/29 (host: github.com, policy: allowlisted)
- **Verdict**: valid
- **Severity**: medium

## Report (verbatim or summarized)

Issue #29 (reported on 0.52.1, Windows x64, label `bug`). Title: "Recording when playing MIDI & it loops a bar suddenly stops while holding a key if it loops the bar during the hold, and recording live midi on loop 1 bar with same melody punched in on repeat makes it stack overlay at different timings (perhaps a toggle for this)".

> if u hold a key down and it loops it suddenly stops

Comment from the reporter:

> It Basically Just Stops The Audio Like Say I Have An String It Lasts A Long Duration After Being Played, But It Cuts Off On Loop If U Are Looping Something

**Scope of this assessment**: held live notes and release tails cut at the loop seam, plus the length of a note recorded across the seam. The report's other half is that overdub passes stack notes, and it asks for an Overdub/Replace toggle. That half is expected behaviour today and a feature request. It is **out of scope** here and goes through `/speckit-specify`.

## Symptom

With the transport loop on, every sounding voice on Arrangement-driven instrument tracks is hard-cut when the playhead wraps from the loop end to the loop start. That includes a key the player is still holding and the release tail of a long pad or string. Expected: a held key keeps sounding, and tails ring out naturally. A note that ends at the loop end gets a normal note-off and releases.

While loop-recording in the Arrangement, a key pressed before the seam and released after it is stored as a 0.1-beat stub at its start position. A routed (MIDI-from-track) note gets the same treatment, with its length clamped to 0 and then 0.1. Expected: the recorded note keeps the part that was played.

## Reproduction

1. New project, add an instrument track with a sustaining built-in (e.g. Volt pad with long release) and arm it.
2. Turn the transport loop on over 1 bar (beats 0–4) and press Play (or Record).
3. Press and hold a key around beat 3.5 and keep holding past the loop end.
   **Observed**: the note goes silent at the wrap. **Expected**: it keeps sounding until released.
4. Release a short note just before the loop end on a long-release patch.
   **Observed**: the tail is cut at the wrap (only the ~1.5 ms declick fade remains). **Expected**: the tail rings into the next pass.
5. With Record on, hold a key from ~beat 3.5 across the wrap and release at ~beat 0.5 of the next pass, then stop.
   **Observed**: the take holds a 0.1-beat note at 3.5. **Expected**: see Open Questions (split vs. truncate), but not a 0.1-beat stub.

Step 4 also applies to clip playback without recording: any clip note whose release crosses the loop end is cut.

## Suspected Code Paths

- `src/native/nota.engine/src/Engine_Render.cpp:1077-1084` (`flushArrangementVoices` lambda) — calls `instrument->allNotesOff()` and `md->reset()` on every non-Session instrument track.
- `src/native/nota.engine/src/Engine_Render.cpp:1101-1107` (loop wrap in `processBlock`) — calls `flushArrangementVoices()` on every wrap. This is the cut.
- `src/native/nota.engine/src/VoltSynth.h:266`, `OperatorSynth.h:194`, `Keys.h:327`, `RhythmMachine.h:493` — built-in `allNotesOff()` sets `v.active = false`, so the voice is removed with no release stage. That is why the tails vanish.
- `src/native/nota.engine/pluginhost/PluginHost.cpp:705` — hosted plugins get CC123 All Notes Off. It also kills the live-held key, though most plugins release rather than cut.
- `src/native/nota.engine/src/Arpeggiator.h:131` — `reset()` clears `held_`/`physDown_`, so an arp on the track also forgets a key the player is still holding at the seam.
- `src/native/nota.engine/src/Engine_Render.cpp:609-648` (`gatherInstrumentNotes`) — clip note-offs are emitted only when `offS` falls inside the current block. A clip note running past the loop end never gets its off in that pass. The flush exists to cover this, so removing the flush needs a replacement off.
- `src/native/nota.engine/src/Engine_Render.cpp:513-522` (`drainLiveMidi`) — `len = nowBeat - pendingStart_`. The wrap fix (`len += slotLen`) is only applied for Session slots (`if (slot && len < 0.0)`). An Arrangement-loop note across the seam gets a negative length.
- `src/native/nota.engine/src/Engine_Render.cpp:296` (`collectRecorded`) — `n.lengthBeats = r.lengthBeats > 0.0 ? r.lengthBeats : 0.1`, which turns the negative length into the 0.1-beat stub.
- `src/native/nota.engine/src/Engine_Render.cpp:580-590` (`captureRoutedNotes`) — the same problem for routed notes: `std::max(0.0, beat - recRoutedStart_)` gives 0, which becomes 0.1.
- `src/native/nota.engine/src/Engine_Render.cpp:545-556` (`captureLiveExpr`) — expression offsets have the same Session-only wrap handling, so MPE curves on a cross-seam note go out of range.
- `src/native/nota.engine/src/Engine_Render.cpp:430-450` (`publishHeldSnapshot`) — the #56 held-note preview reports `pendingStart_` from the previous pass, so after a wrap the preview note's start is later than "now".

## Root Cause Hypothesis

The loop wrap reuses the stop/seek "flush stuck voices" path. That path hard-kills every voice on Arrangement tracks, so a clip note running past the loop end can't hang. It doesn't tell clip-owned voices from live-input voices. And because the built-ins' `allNotesOff()` deactivates voices outright, release tails go too. Separately, the live and routed capture compute note length as `now - start` on the unwrapped beat clock, and only the Session-slot path corrects for the wrap. In the Arrangement loop, a cross-seam note gets a negative length, which `collectRecorded` replaces with 0.1. Confidence: **high** (all three effects follow directly from the cited lines).

## Proposed Remediation

**Preferred**:

1. *Release clip notes at the seam instead of flushing the instrument.* On a loop wrap, stop calling `flushArrangementVoices()`. Set a flag for the first segment after the wrap. In `gatherInstrumentNotes`, while the flag is set, emit a note-off at offset 0 for every clip note that was sounding at the loop end (`onS < loopEndS && offS > loopEndS`, after the clip-end clamp; inactive clips included, as for normal offs). These offs go through the track's MIDI FX chain like any other event, so the arp, chord, transpose and similar devices map them correctly. They also reach routed destinations, and instruments play their release stage. Note chase already retriggers notes spanning the loop start, and `midiEvLess` sorts offs before ons at the same offset, so a note spanning the whole loop still re-articulates as it does today. Live events are untouched, so a held key keeps sounding. Drop the `md->reset()` on wrap so the arp keeps live-held keys (see Open Questions on arp phase). Keep `flushArrangementVoices()` for stop and seek, which this report doesn't cover.
2. *Split recorded notes at the seam.* In `processBlock`, when a wrap happens while recording into the Arrangement (no `recordSlotPlayer_`), handle every `pendingActive_` pitch and every `recRoutedActive_` pitch the same way. Push a `RecordedNote` from its start to `loopEnd`, then restart it at `loopStart` with the same velocity. The continuation becomes its own note when the key is released, and the held-note preview stays correct. As a safety net, `collectRecorded`/`drainLiveMidi` no longer turn a negative length into 0.1. Clamp to the loop end instead, though with step 2 that should no longer happen. `captureLiveExpr` follows the restarted `pendingStart_` automatically.

**Alternatives**:
- *Per-track "clip-sounding" ledger at the instrument input (post-FX)*, flushed with `noteOff` per pitch at the seam. It's exact with respect to what the instrument received, but after the MIDI FX the engine can't tell clip notes from live ones. It also adds state per track. The pre-FX synthetic offs are simpler and stateless.
- *Add `Instrument::releaseAll()` (soft) and re-press the live-held keys after the wrap.* It's simple, but every held key re-attacks on each pass, which is audible and wrong for pads.
- *Recording: truncate at the loop end* instead of splitting. It's one note and simpler, but the part played after the wrap is lost. See Open Questions.

**Files likely to change**:
- `src/native/nota.engine/src/Engine_Render.cpp` (wrap handling in `processBlock`, `gatherInstrumentNotes`, `drainLiveMidi`, `captureRoutedNotes`, `collectRecorded`)
- `src/native/nota.engine/src/Engine.h` (seam flag / loop-end sample for the post-wrap segment)
- `tests/Nota.SmokeTest/LiveRecordTests.cs` (new cases), possibly a loop-playback case in `tests/Nota.SmokeTest/Program.cs`
- `CHANGELOG.md` ([Unreleased] → Fixed)

**Tests to add or update** (smoke test, `RenderOffline` with a 1-bar loop at 120 BPM):
- *Held key survives the seam*: arm a long-sustain built-in, loop 0–4, `NoteOn` at beat 3.5, render past the wrap. RMS in the window just after the wrap stays above a floor (not silence).
- *Release tail survives the seam*: a long-release patch, a clip note 3.0–3.9. Energy right after the wrap is non-zero and decays (tail present).
- *Clip note crossing the loop end does not hang*: a clip note 3.0–6.0 in a 0–4 loop on a sustain patch. Once the next pass has no note at that pitch, the instrument's voice count (or RMS) falls back to 0.
- *Recorded note across the seam*: Record, `NoteOn` at 3.5, release at 0.5 of the next pass. The take holds a note at 3.5 of ~0.5 beats and a note at 0.0 of ~0.5 beats. There must be no 0.1-beat stub. Run the routed variant the same way (`Routed()` pattern).
- *Session slot behaviour unchanged*: the existing `SessionSlot()` test still passes.

## Risks & Considerations

- **Hanging notes**: the hard flush is the current guarantee against stuck notes on the wrap. The replacement offs have to cover every clip note crossing the loop end. That includes clips trimmed or deactivated mid-pass, and notes whose start is before the clip (#56 pickups). Stop and seek keep the hard flush as a backstop.
- **MIDI FX with internal state** (arp, note length, random): without `reset()` on the wrap, an arp keeps its step position across the seam instead of restarting at step 1 each pass. This could change how existing loops sound, so see Open Questions. Note-length FX that schedule their own offs keep working because they see the synthetic offs.
- **Same pitch held live and ending in a clip at the seam**: the synthetic off for the clip note also releases the live voice. This is an edge case and matches how a normal clip note-off behaves today.
- **Hosted plugins** now get individual note-offs instead of CC123 at every seam. That's better behaviour, but VST/AU instruments should be spot-checked.
- **Audio thread rules**: everything stays allocation-free (fixed arrays and flags). No locks.
- **Declick**: the seam fade (`Engine_Render.cpp:1141-1157`) still applies. With tails surviving, the fade is now an audible notch on sustained material, though at ~1.5 ms it's probably inaudible. Keep it for audio clips.

## Open Questions

- [NEEDS CLARIFICATION: A note held across the loop seam while recording. Split it into "start→loop end" plus "loop start→release" (proposed), or truncate it at the loop end?]
- [NEEDS CLARIFICATION: Should a track's arpeggiator still restart its pattern at step 1 on every loop pass (today's behaviour via `reset()`)? If so, use a phase restart (`midiCommand(1)`-style) that keeps held keys instead of the full `reset()`.]
- [NEEDS CLARIFICATION: Should stopping the transport also let tails ring out (release) instead of hard-cutting? Same mechanism, but not part of this report.]
