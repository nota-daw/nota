# Quickstart: validating MIDI routing (spec 115)

## Automated (the PR gate)

```powershell
pwsh scripts/build-win.ps1
```

On macOS use `scripts/build.sh`, on Linux `scripts/build-linux.sh`. These build native and
managed code and run `tests/Nota.SmokeTest`. To run only the routing tests after a build:

```powershell
dotnet run --project tests/Nota.SmokeTest -c Release --nologo -- --midi-routing
```

Each test uses test MIDI ports ([c-abi.md](contracts/c-abi.md) § Test hooks): inputs are fed
through `nota_midi_test_port_add`, and outputs are captured with
`nota_midi_test_output_read`.

| Test | Covers | Expected |
|---|---|---|
| Index self-test, 17 ports | #1011, R7 | Indices are distinct; the 17th port is over the limit; a track on input 16 receives nothing from 17 |
| Port A {3} + Port B {7} | Story 2 #1, FR-003 | A/3 and B/7 are monitored and recorded in one take; A/7 and port C are ignored |
| Channel set {3,7,11} | Story 2 #2, FR-004 | Recorded notes equal monitored notes, only on 3, 7 and 11 |
| Disable A while A and B are held | Story 2 #3, FR-005 | A's note ends, B's keeps sounding (instrument voice state) |
| Remove channel 7 while 7 and 11 are held | Story 2 #4 | The 7 note and its sustain release; 11 keeps sounding; other tracks are untouched |
| Same pitch from A/3 and B/7 | Edge case | One voice; it ends after the second release |
| Legacy shortcuts and a v35 fixture project | FR-001, SC-008 | Same filter as before; the fixture loads unchanged |
| Arp + Session clip → External MIDI `test:X` ch 5 | Story 1 #1, FR-008 | Arpeggiated notes on channel 5, each Note On paired, in order, due times on the beat grid |
| Same from live input | Story 1 #2 | The same post-effect stream |
| Stop / remove track / re-route / mute / undo while held | Story 1 #3, FR-010, SC-002 | Releases at the old output only; zero unpaired Note Ons |
| `nota_midi_test_port_remove`, then add again while held | Story 1 #4, FR-011 | Nothing is sent while removed; on return: Note Offs, CC 64=0, CC 123 on the used channels, then new events |
| Two routes on one output and channel | Edge case | Each sends its own Note Offs; one reset per channel |
| Saved uid gone, similar name present | Story 1 #5, FR-006, SC-003 | Status unavailable; zero bytes to the similar port |
| Keep channels with MPE input | Story 1 #7, R5 | The MPE configuration message, then each note on its own member channel with its bend and pressure there |
| Role "Stage Synth", two tracks ch 2 and 5, rebind | Story 4, FR-013–FR-017 | Preview lists the 2 tracks; refused while playing; after rebind, new notes reach the new port on 2 and 5; one undo step reverts it |
| `get_midi_routes` with the A, B, C, D setup | Story 3, FR-018–FR-021 | Rows and filters as in the spec; transport state unchanged |
| 8 External MIDI tracks + Arp, 60 s render | SC-005 | No dropouts; `dropped == 0` |

## Manual (hardware, before the PR)

1. **Real synth** (any hardware or software synth on a loopback port: IAC on macOS,
   loopMIDI on Windows, `snd-virmidi` on Linux): put External MIDI on a track, pick the port,
   set channel 5, play a Session clip with Nota Arp, and listen. Then play live from a
   controller.
2. **Unplug**: hold a chord through a USB synth, pull the cable, plug it back in. No hung
   notes, and the card shows unavailable → sending.
3. **Windows exclusivity**: use a controller whose paired output is also an External MIDI
   destination. The controller LEDs (spec 051) and routed notes both work, with no "port in
   use" error.
4. **Timing**: record the synth's audio back into Nota next to a Nota drum hit at 120 BPM.
   The offset stays steady within a few ms between bars, with no drift.
5. **Panel**: open the Mixer, then **MIDI routes**. Disconnect a controller and see its row
   flagged. Rename and reorder tracks, and see the panel follow.
6. **Over the limit**: connect 17 virtual inputs. Settings → Inputs shows the 17th as not
   connected; switch one off and the 17th connects.
