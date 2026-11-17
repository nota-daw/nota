# Bug Assessment: Milestone 0.60.2 bugs

- **Slug**: 0602-milestone
- **Created**: 2026-11-15
- **Source**: the open `bug` issues of milestone 0.60.2 on https://github.com/nota-daw/nota (host: github.com — allowlisted), all labelled `approved`
- **Verdict**: valid (each below), except where noted
- **Severity**: low to high per issue

One assessment for the milestone's bugs, fixed together in one pull request. Each row names the
root cause found in the code; the fix record is `./fix.md`.

| Issue | Symptom | Root cause | Severity |
|-------|---------|------------|----------|
| #530 | Delete in a piano roll whose clip's track was removed crashes Nota | Removing a track never told the Clip editor; its `ClipSet` lost its last clip on the next commit and `ActiveClip` indexed an empty list (`ClipPropsView.OnRollChanged`) | high |
| #537 | After "To transients" the clip editor's playhead runs ahead of the waveform; undo doesn't fix it; an undone import stays in the editor | `AudioClipEditorView.Reload` read at most 128 warp markers (transient warp places up to 258), so `TotalBeats` (last marker read) was short of the warp; undo never reloaded the audio editor; nothing dropped an audio editor whose clip was undone away. `AudioClipState` truncated saved markers at 128 too | high (data loss on save) |
| #529 | Section ▸ Duplicate stacks the copy on the next section | The plain Duplicate added a marker at the section's end without moving anything | medium |
| #604 | Resizing a clip of a multi-selection selects it alone | The trim start called `Select(clip)`; only linked partners followed the edge | medium |
| #565 | Notes slide with a MIDI clip's left edge while it is dragged | `DrawNotes` places notes by their clip-local start; the preview moves the start, the engine re-bases notes only on release | low |
| #538 | Clicking a Mixer strip doesn't select its track | No press handler on the strip | medium |
| #548 | Up/Down open the File menu and keep the keyboard there | Unhandled arrows fell to Avalonia's directional navigation, which walked into the menu bar; custom-drawn views don't take focus on click, so the menu kept it | medium |
| #445 | Piano roll scrolls vertically as a selection starts | Pressing the note grid focuses it, and its ScrollViewer brings the (taller than the view) grid into view on focus; edge autoscroll also fired for drags that merely started near the top/bottom rows | medium |
| #467 | Split / overwrite drop a MIDI clip's deactivation and envelopes; audio pieces keep un-rebased envelopes | `splitClip` (MIDI branch) and the overwrite carves build pieces from notes and controllers only, and audio carves copy the whole clip's envelopes | medium |
| #559 | Output / Gain / Mix step once per block on several devices | Valve, Vintage, Crush, Ceiling, Compressor and Forge applied the block's value to every sample | low |
| #573 | Aurora wavetable stack magnified and cut off at 125 % | `DrawImage(bitmap, destRect)` takes the source rect in pixels; the cached bitmap is `scale ×` larger. Confirmed headless at 1.25× and 2× | medium |
| #221 | Computer keyboard doesn't play over a plug-in editor window | Native window: keys never reach the app. macOS had a hard-coded partial map (no A, no octave / velocity, no custom keys) into the plug-in; Windows / Linux had nothing | medium |
| #164 | macOS crash "InvalidateArrange on wrong LayoutManager" | A control moved between windows can stay queued in its old window's layout queue; the throw from Avalonia's render loop was unhandled | high (crash), not reproduced |
| #358 | Linux: a 500 GB+ sample folder freezes the system | Recursive walk followed linked folders (a link up the tree or to `/` never ends); the whole index was rewritten every 5 s (quadratic I/O); worker threads competed for the disk at normal I/O priority | high |
| #544 | KONTRA (VST3, 32 outs) fails to load on Linux | Not found: the format's error string was discarded. Diagnostics only (logs the reason); needs the reporter's log | — |
| #450 | Dropout storm, Consort + Arp at 64 frames | Not reproduced: a headless 90 s render of Consort + Arp looping 0–16 at 64 frames shows no voice growth and steady block times; the reporter's own headless render agrees. Needs the live project | — |
| #552 | Recorded level drops after ~2 s (Windows, WASAPI, USB headset) | Nota's capture path applies no gain. Most likely the endpoint's capture processing (AGC) — miniaudio doesn't request RAW mode. Not changed here | — |
| #300 | Linux: file-manager drops do nothing | URI-list fallback landed in 0.58.1 (#325); "no drop highlight" means no drag event reaches Avalonia. Needs a Linux session to see what arrives | — |
| #596 | NixOS: AppImage doesn't run | Packaging; a contributor offers a Nix flake. Not changed here | — |
