# Bug Assessment: Milestone 0.60.2 bugs, second batch

- **Slug**: 0602-milestone-2
- **Created**: 2026-11-16
- **Source**: the open `bug` issues of milestone 0.60.2 on https://github.com/nota-daw/nota (host: github.com — allowlisted), all labelled `approved`
- **Verdict**: valid (each below), except where noted
- **Severity**: low to high per issue

One assessment for the milestone's bugs still open after `0602-milestone` (#619), fixed together
in one pull request. Each row names the root cause found in the code; the fix record is `./fix.md`.

| Issue | Symptom | Root cause | Severity |
|-------|---------|------------|----------|
| #646 | Delete removes the piano roll's selected notes, not the clip just clicked in the Arrangement | The Arrangement takes no keyboard focus on a click, so the roll's note grid kept it and its own Delete handler ran; `OnMenuDeleteSel` (and Copy / Cut) also asked the roll first whatever was clicked last | medium |
| #647 | Deleting the open MIDI clip leaves its roll on screen; an edit there crashes | Nothing told the editors about a clip delete or cut. Removing a clip shifts the later clips of its track down an index, so the editor's `(track, clip)` named a clip that was gone or another one | high (crash) |
| #648 | Crash on a tool change after resizing a clip under a MIDI tool preview | `PianoRollView.Load` cleared `_previewBackup` but not `_previewOwners`; the next `ShowPreview` paired a fresh backup with the stale owners and indexed past it. The reload also read the previewed notes (streamed to the engine with no undo step) as the clip's own | high (crash) |
| #649 | Notes keep sounding after their note-off | Clip notes get their note-off only when the render reaches it; an edit that removes a sounding note (clip delete / cut, note edit, undo, a preview put back) leaves none to send. Volt's mono `noteOff` released only voice 0, so voices a poly patch started before turning mono hung. The roll's audition note-off went to whatever track the editor showed by then, not to the note-on's track | high |
| #645 | Monolith: back-to-back notes of one pitch — only the first sounds | `noteOn` retriggered only when the amp stage was `Off` or the selected pitch changed. At a tie the engine sends the note-off first, so the next note-on of the same pitch met a releasing voice with an unchanged pitch and never retriggered | medium |
| #650 | Drum Rack: unmuting a pad mid-playback fires a stray hit | A silent (muted / solo-gated) pad still took note-ons, but `DrumRack::render` skipped its instrument, so the voice stayed frozen at its start and played on unmute. Same in `RackInstrument::render` | medium |
| #644 | Kompakt kit: open hat on every 1/8 skips random hits | Not in the hat alone: an open-hat clip renders every hit (checked offline at 33–4096-frame blocks, with and without the loop, swing, humanize and the pad FX). Most likely a closed hat on the same steps: Humanize jittered each note-on on its own, so the open and closed hat (one choke group) landed in a random order and the later one cut the other — a different step each pass | medium |
| #643 | A Compressor in a Drum Rack chain can't take a sidechain — the picker falls back to Internal | `RackChainDeviceEngineProxy` stubbed every sidechain call (accepts → false, key → None, set → -1); Mix and Tap went through to the track's device at the same index | medium |
| #450 | Dropout storm, Consort + Arp at 64 frames | Not reproduced again: Consort + Arp + Delay + Reverb looping 0–16 offline at 64-frame blocks for 12 passes keeps a steady block time. Needs the live project; #619's render-pool sizing fix addresses the one feedback loop found that builds up until Stop | — |
| #544 | KONTRA (VST3) fails to load on Linux | Not the outputs: KONTRA's macOS VST3 loads in Nota with all 16 stereo buses enabled and renders. The Linux failure is in the format's instantiation; #619 logs its reason. Needs the reporter's log | — |
| #552 | Recorded level drops after ~2 s (Windows, WASAPI, USB headset) | As in `0602-milestone`: Nota's capture path applies no gain; most likely the endpoint's capture processing (AGC). Needs a Windows test | — |
| #300 | Linux: file-manager drops do nothing | Avalonia 12.0 had no XDND drop target on X11; 12.1.4 (Nota since 0.56.1) does, and the URI-list fallback landed in #325. Needs a Linux session to confirm | — |
| #596 | NixOS: AppImage doesn't run | Packaging; a contributor offers a Nix flake. Not changed here | — |
