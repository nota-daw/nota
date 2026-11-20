# Quickstart: verifying Session → Arrangement recording

## Automated (the gate)

```bash
scripts/build.sh
```

The smoke test runs the new `SessionToArrangementTests` group. Every case is device-free: it
renders offline blocks at 48 kHz, 120 BPM, 4/4, with launch quantize 1 bar unless the case says
otherwise.

| Case | Checks |
|---|---|
| Two tracks, launches at bar 1 and bar 4, a stop in bar 8, Stop at bar 13 | clip edges on grid beats; a 2-bar slot repeated over 12 bars; a 1-bar slot over 4 bars (US1, SC-002) |
| Render compare | the Arrangement render after Back to Arrangement matches the live pass render (SC-001) |
| Legato and Record started mid-play | the written clip starts inside the slot content (US1 s6, s7) |
| Follow actions and Repeat | one clip per slot, back to back |
| Cancelled launch | nothing written (US1 s8) |
| Audio slot | one audio clip per repetition, warp kept |
| Replace | existing clip split at the span edges; an untouched track is byte-identical (US2, SC-005) |
| Armed instrument plus slot | played notes merged into the written clip, none outside it lost (US3 s2) |
| Monitored audio track | the slot writes nothing, the input take is recorded |
| Undo / Redo | one step restores the exact pre-pass state (SC-004) |
| ⌘. retry | nothing of the pass remains, recording restarts (FR-011) |
| Loop on | the last pass wins in the loop range (FR-012) |
| Scene tempo | Tempo lane created with a step at the scene beat; with an existing lane, nothing is written |
| Nothing armed | status 3 with a slot playing; status 1 (auto-arm path) with the Session silent |

## By hand (the real app)

1. Make a project with a drum slot (2 bars), a bass slot (1 bar) and an 8-bar Arrangement clip
   on a third track.
2. Arm nothing. Launch the drums in Session, press Record (status: "Recording the Session
   performance"), launch the bass during bar 3, stop it two bars later, then press Stop.
3. Open the Arrangement: the drums run from where Record started to Stop, the bass covers its
   two bars, and the third track is unchanged.
4. Click Back to Arrangement, then Play: it sounds like the performance.
5. Undo once: everything from the pass is gone. Redo brings it back.
