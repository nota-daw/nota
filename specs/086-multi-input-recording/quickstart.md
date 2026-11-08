# Quickstart: verify multi-channel input and multi-track recording

## Automated (the PR gate)

```bash
scripts/build.sh
```

New smoke tests in `tests/Nota.SmokeTest` (offline, `nota_engine_set_test_input_device` +
`nota_take_record_test_n` / `nota_engine_push_midi_for_test`, see
[contracts/c-abi.md](contracts/c-abi.md)):

1. **Pick** (picks are 0-based `(first, width)`) — 8-input test device; a track on (2,1)
   ("In 3") gets a 1-channel clip equal to column 2; a track on (2,2) ("In 3/4") gets a
   stereo clip of columns 2/3; `set_input(3,2)` (odd stereo first) returns
   `NOTA_ERR_INVALID_ARG`.
2. **Multi-track** — four armed tracks on four inputs, one take: four clips, equal start beat
   and frame count, each equal to its own column (bit-exact); one `Undo` removes all four,
   `Redo` restores all four.
3. **Same input twice** — two tracks on (0,1): two identical clips.
4. **Linked group (#528)** — two audio tracks in a linked group, arm one, record: two clips.
5. **Loop** — multi-track take over 3 loop passes: each track has 3 take lanes.
6. **Retry** — `RetryTake` mid multi-track take: no clips left; stop after retry → one clip
   per track at the original start beat.
7. **Unavailable** — a track on (5,1) with a 2-input device: no clip, listed by
   `nota_engine_record_skipped` with reason 1; the other armed track records.
8. **Session** — two tracks, empty slots launched for the same scene boundary; stop one slot
   after 2 bars, the other after 4: two clips, both starting at the boundary, lengths 2 and
   4 bars; the first stop leaves the second recording.
9. **Migration** — load fixtures saved by 0.59.2 with `InputChannels` 0/1/2: picks (0,2),
   (0,1), (1,1); save → `InputChannels` still written; a (3,1) pick saves no `InputChannels`.
10. **Mono file** — the saved bundle's WAV for a mono take has 1 channel.
11. **MIDI** — instrument tracks P (All) and Q (source 1, channel 1), both armed; notes from
    source 0 ch 1 and source 1 ch 1 and source 1 ch 2: P records all three, Q only the
    source-1 channel-1 note. Computer-keyboard notes (source −1) reach P only.
12. **MIDI linked group** — two instrument tracks linked, arm one, play: two MIDI clips.

## Manual (real hardware)

Prerequisites: a multi-input interface (or macOS Aggregate Device / BlackHole 16ch), the
Arturia KeyLab Essential 61 mk3 plus a second MIDI controller.

1. Preferences → Audio → Audio I/O: inputs listed with device names; switch off 5–8, name
   input 1 "Vox". Track input menu shows only 1–4, "Vox", pairs 1/2 and 3/4.
2. Four audio tracks on inputs 1–4, ⌘-click Arm on each, record 30 s: four clips, aligned
   (clap test: transients line up to the sample in the clip editor).
3. Two instrument tracks, one per controller (channel All), both armed: each records only
   its own keyboard.
4. Session: two tracks, launch empty slots on the same scene, stop one early: the other keeps
   recording; save, reopen, both slots there.
5. Performance (SC-004): 8 tracks armed at a 128-sample buffer for 10 minutes: no more xruns
   than one track over the same time (Preferences → Audio shows the counter).
6. Unplug the interface mid-take: every lane stops, captured audio kept, a message says why.
