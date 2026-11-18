# Bug Assessment: tempo-synced synth LFOs land somewhere else in the beat on every play and export

- **Slug**: export-render-sync
- **Created**: 2026-11-18
- **Source**: https://github.com/nota-daw/nota/issues/676 (host github.com, allowlisted; filed this session from a user report)
- **Verdict**: valid
- **Severity**: high

## Report (verbatim or summarized)

Issue #676, "Export: tracks drift out of sync in the rendered file, play in time in the app". Reported by a user: "totally out of sync on render. plays fine while working on DAW". By the user's screenshot and audio, the Nota Aurora tracks (one at the top level, two inside a group, chords and held notes) come out of sync with the rest (Nota Volt tracks, an audio track). The project uses Nota Delay, Nota Reverb, Nota Compressor and Nota EQ-8.

## Symptom

A Nota Aurora (or Nota Volt) patch whose LFO is synced to the tempo (pulsing, gating, wobbling on 1/4, 1/8, 1/16) pulses on the beat while the user works, but in the export the pulse sits at another place in the beat, so the part sounds off the grid. Expected: a synced LFO pulses at the same place in the beat on every play and in every export.

## Reproduction

Measured with a harness against the 0.60.2 engine (`/private/tmp/n676harness`): Aurora holding one note, LFO 1 synced to 1 beat, routed to Level; where in the beat the dip lands (0..1), averaged over beats 8–24:

| history before Play          | play from 0 | play again | export |
|------------------------------|-------------|------------|--------|
| fresh engine                 | 0.797       | 0.656      | 0.047  |
| 1.37 s played elsewhere first| 0.625       | 0.312      | 0.922  |
| 2.90 s played elsewhere first| 0.531       | 0.625      | 0.266  |

1. Add a Nota Aurora track and pick a factory preset with a synced LFO (Shimmer Arp, Wobble Wave, Chime Sequence, Glacier Pad) or route a synced LFO to Level.
2. Hold a chord over a few bars, play: the pulse sits somewhere in the beat.
3. Stop, play again from the same place: the pulse sits elsewhere.
4. Export: elsewhere again.

What the report doesn't say: which Aurora patches the user had. [NEEDS CLARIFICATION: the user's project file would confirm]

Ruled out with the same harness (no difference between live and export, to 0.2 ms):
- note timing per instrument at block sizes 64 / 512 / 2048 / 8192, device vs offline render;
- export sample rate 44.1 / 48 / 96 kHz against a 48 / 44.1 kHz device;
- a project-like layout (Volt, Volt, Aurora, group of two Auroras) with Delay, Reverb, EQ-8, Compressor, 60 s;
- tempo ramps and tempo steps; audio clips.
- Plugin delay compensation through groups (#667 part 1) is real (a grouped track lands one extra look-ahead late, 10 ms at max look-ahead) but is the same live and in the export, and the Compressor's look-ahead is off by default. Out of scope here; stays in #667.

## Suspected Code Paths

- `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp:152` — `setTransport` keeps only `samplesPerBeat`; the beat position is thrown away.
- `src/native/nota.engine/src/devices/instruments/WavetableSynth.cpp:696-707` — `lfoPhase1_` / `lfoPhase2_` free-run from wherever they last were; they also stop while Aurora sleeps (the engine skips an idle instrument's render), so silence shifts them further.
- `src/native/nota.engine/src/devices/instruments/WavetableSynth.h:354` — `lfoInc`: the synced rate, with no phase.
- `src/native/nota.engine/src/devices/instruments/VoltSynth.h:197`, `VoltSynth.cpp:422-433`, `VoltSynth.cpp:681` — the same in Nota Volt (Wobble Bass, Growl Bass, Sample & Hold presets).
- Reference: `src/native/nota.engine/src/devices/effects/AutoPan.cpp:64-99` — the effects (AutoPan, AutoFilter, Chorus, Phaser, Flanger, Forge, Shutter) already lock a synced LFO to the transport beat while playing.

## Root Cause Hypothesis

Aurora's and Volt's tempo-synced LFOs only take the tempo from the transport, not the position. Their phase carries over from whatever ran before (earlier plays, the sleep between notes, the export's re-prepare), so the pulse lands at a different place in the beat on each play and in each export. Confidence: high for the mechanism (measured); medium that it is what the user heard, as the patches in their project aren't known.

## Proposed Remediation

**Preferred**: lock a synced LFO to the transport while it plays, like the effects do. Both synths keep the block's start beat and the playing flag from `setTransport`; at the start of each `render()`, when an LFO's sync is on and the transport plays, its phase is set from the beat: `phase = frac(beatStart / divisionBeats)`. Inside the block it advances by the synced increment as now (exact for a steady tempo; re-derived each block, so tempo changes and seeks are followed). Free-rate LFOs and a stopped transport keep free-running as now, so notes played from the keyboard with the transport stopped still pulse. Sample & hold takes a new value when the phase wraps, as now.

**Alternatives**:
- Lock per sample (as AutoPan does) — exact under tempo ramps inside a block, but costs a division per sample in the voice loop that the 036 perf work removed; per-block is within a sample at a steady tempo.
- Reset the phase on Play — fixes "export vs play from the same spot" but not "export from bar 1 vs play from bar 9".

**Files likely to change**:
- `src/native/nota.engine/src/devices/instruments/WavetableSynth.h` / `.cpp`
- `src/native/nota.engine/src/devices/instruments/VoltSynth.h` / `.cpp`
- `tests/Nota.SmokeTest/` — a new test file, registered in `Program.cs`
- `CHANGELOG.md` ([Unreleased] Fixed)

**Tests to add or update**:
- Aurora and Volt, LFO 1 and LFO 2 synced to Level: the dip lands at the same place in the beat on a first play, a second play, after playing elsewhere, and in an export at 8192-frame blocks at another sample rate; and it's on the beat grid (dip phase = the shape's expected phase).
- A free-rate LFO still free-runs (unchanged), and a synced LFO with the transport stopped still moves.

## Risks & Considerations

- Sound change: projects that relied on the random phase will pulse in a different place, now the same every time. This is the intended behaviour and matches the effects.
- Aurora's sleep: an idle Aurora skips render; on wake the phase is re-derived at the block start, so no state to catch up.
- Audio-thread rules: no allocation; two doubles and a bool more per synth.
- Factory preset hashes / golden renders that include these presets may shift.

## Open Questions

- [NEEDS CLARIFICATION: the user's project file, to confirm the Aurora patches use a synced LFO]
