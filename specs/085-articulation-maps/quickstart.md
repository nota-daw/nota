# Quickstart: validating articulation maps

How to prove the feature works end to end. Each scenario says what it needs and what must happen.
Contracts: [`contracts/`](contracts/); data: [`data-model.md`](data-model.md).

## Prerequisites

- Windows x64, MSVC Build Tools 2022, CMake, Ninja, .NET SDK 10, PowerShell 7.
- **Native engine built.** This needs the CMake dependencies available offline (see `research.md`
  R12). Until then only the managed scenarios (A, B) can run.
- No vendor map files are in the repo (they are third-party works). Scenarios A and B need your
  own: a Cubase `.expressionmap` and the Studio One `.keyswitch` list for the same library. The
  smoke test covers the importers on small maps it writes itself.
- Scenario G needs Kontakt with Cinematic Studio Strings on the owner's machine.

## Automated

```powershell
# Managed only (no engine needed): importers
dotnet run --project tests/Nota.SmokeTest -- --articulations

# Everything (builds native + managed + smoke test)
pwsh -NoProfile -File .\scripts\build-win.ps1
```

## Scenarios

**A. Import a Cubase map** (manual, with your own file; the importer itself is covered by the smoke test on inline maps). For example `CSSS Violins 1 A.expressionmap` gives 44 articulations,
Long first with keyswitch 24/1, keyswitch 35/1, CC 58 = 81 in that order; the empty slot is
reported as skipped; the file's SHA-256 is unchanged.

**B. Import a Studio One list and compare** (manual, with your own files). For example `CSSS Violins 1 Attribute.keyswitch` gives
44 articulations; for each, its keyswitch notes equal the Cubase map's keyswitch notes of the same
name; the `unknown` action is reported.

**C. Offsets** (automated, needs engine). On a track with a recording test instrument: track offset
-100 ms and Legato -150 ms put a Legato note-on 250 ms before its grid position at 120 and at 60
BPM (same milliseconds, different beats); the notes in the clip are unchanged; offset 0 matches the
old behaviour sample for sample.

**D. Keyswitch timing** (automated, needs engine). With lead 30 ms the actions arrive 30 ms before
the note-on, in the file's order, before the note; a latching keyswitch is released 20 ms later; a
momentary one with its note; a chord with two articulations triggers each once.

**E. Seek, loop and restart** (automated, needs engine). Seeking into a phrase, a loop wrap and
Stop/Play each send the articulation in force once before the first note, never twice, and leave no
held keyswitch after Stop.

**F. Persistence and undo** (automated). Save then reopen restores the map, the track offset and
every note's articulation; a v29 project opens unchanged; assigning a map, changing a note's
articulation and editing a map each undo and redo; a deleted articulation leaves its notes playing as
the default.

**G. Real library** (manual, owner). Import the Cubase map, assign it to a Kontakt track with CSS
Violins, put Long, Legato and Staccato notes in a clip, press play: Kontakt must switch to the right
sound for each note (watch its keyswitch display). Set the track offset to about -80 ms and check
the attacks land on the beat by ear and against a click. **Not claimed to work until the owner has
done this.**

**H. Drag and drop** (manual). Drag `.expressionmap` and `.keyswitch` files from Explorer onto the
Articulation Map Manager, then onto the track inspector's Articulation section: a preview with
warnings appears, accepting assigns the map. A renamed text file is refused.

**I. Colours** (manual). Switch Note Color Mode: notes recolour, selection stays visible; switch
back and the track colours return.

## Results

- **H (2026-11-09, owner, Windows):** import from the inspector's file picker and drop from Explorer both open the preview; Assign to track puts the map on the track. Reported: works. Editing a map is not available yet (US5).
- **T043 (2026-11-09, owner):** an articulation set from the note menu did not play until the note was moved. Cause: the piano roll's changed-clip test (SameNotes) compared notes field by field and skipped ArticulationId, so an articulation-only edit was never sent to the engine. Fixed with NotaNote.Same (all fields; a smoke test fails if a field is ever left out) and engine self-test check 22 (an articulation-only edit makes the clip's timeline stale and it is rebuilt). Waiting for the owner's re-test.
- **T043 re-test (2026-11-09, owner):** articulation set from the note menu plays without moving the note. Works.
- **G (2026-11-09, owner, Windows, Kontakt + Cinematic Studio Strings, Violins 1):** the Cubase map assigned in the inspector, articulations set per note in the piano roll; on playback Kontakt switches articulation with each phrase. Works. Not yet checked by ear: offset compensation (no inspector field yet).
- **A–F (2026-11-09, T073, Windows, full smoke test 5401 pass / 0 fail, 153 articulation checks):**
  - *Note: A and B were run on the owner's Art Conductor files while they were local test fixtures; the files were then removed (third-party works) and the smoke test now checks the same rules on small inline maps.*
  - **A — passes.** The Cubase map imports 44 articulations; the empty slot is skipped and reported; Long is first with Keyswitch 24/1, Keyswitch 35/1, CC 58 = 81 in file order; importing never modifies the file (SHA-256 compared).
  - **B — passes.** The Studio One list imports 44; every keyswitch matches the Cubase map's for the same name (44 ok, 0 off); the `unknown` action is reported.
  - **C — passes.** Track −100 ms + Legato −150 ms plays the note 250 ms early at 120 BPM and at 60 BPM (12000 samples both times); the clip's notes are unchanged; with no offset the Session-slot note loop is unchanged.
  - **D — passes, one claim untested.** Lead (30 ms before), file order, the 20 ms latching release and the momentary hold are self-test checks 2–6 and 16. Not tested: a chord with two articulations. By design (data-model, `buildArticulationTimeline`) a chord's highest note sets the latching articulation, so two latching articulations in one chord send one switch, not one each; two momentary ones each send their own.
  - **E — passes, one claim untested.** A seek into a phrase, a landing inside a held momentary note and a loop wrap each send what is in force once (self-test 11–15), never twice. Not tested: no keyswitch left held after Stop. By design Stop's all-notes-off reaches every channel an articulation used (`PluginInstrument::allNotesOff`), but no check exercises it.
  - **F — passes, one claim covered indirectly.** Save and reopen restore the map, the track offset and each note's articulation; a v29 project migrates with no offsets or articulations; assigning a map, a note's articulation and the track offset each undo and redo; removing an articulation keeps the notes' ids. That such a note plays the default is the engine's `ArticulationTable::resolve` fallback, which the self-test doesn't hit with a missing id.
