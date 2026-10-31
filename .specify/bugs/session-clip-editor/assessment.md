# Bug Assessment: Session clip editor — Clip tab, selection follow, playhead (#334)

- **Slug**: session-clip-editor
- **Created**: 2026-10-30
- **Source**: https://github.com/nota-daw/nota/issues/334 (host `github.com`, allowlisted)
- **Verdict**: valid (items 1–4); items 5–9 are feature requests that need product decisions
- **Severity**: medium — the Session clip editor can only be reached through a context menu

## Report (summarized)

"Clip edit bugs", macOS (Apple silicon), Nota 0.57.1. The reporter was working in Session view:

1. Clicking a clip in Session should switch the detail panel to the Clip editor, not Devices.
2. The **Clip** button of the Devices | Clip switch does nothing; the only way into the editor is
   right-click ▸ Edit.
3. With the clip editor open, selecting another clip in Session should bring that clip into view.
4. The playback marker doesn't show in the clip editor while the clip plays.
5. MIDI clip inspector: Start / Length, the Loop switch don't work; there are no loop in/out
   points (they should show even with Loop off) and no clip start/end points.
6. Notes / Envelopes switch does nothing.
7. Audio clips: show left and right waveforms stacked, or at least a toggle (the reporter likes
   the single combined view).
8. Audio clips have no loop on/off and no loop points.
9. The Texture warp mode lacks its two extra parameters.

## Symptom

In Session view a single click on a slot selects its track and hands the detail panel to
Devices; the Clip tab is disabled at that point, so neither the click nor the tab opens the clip.

## Reproduction

1. Session view, a MIDI track with a filled slot.
2. Click the slot → the detail panel shows Devices, the Clip tab is greyed.
3. Click Clip → nothing.
4. Right-click ▸ Edit opens the piano roll. Click another filled slot → the panel drops back to
   Devices (another track) or stays on the old clip (same track).

## Suspected Code Paths

- `src/managed/Nota.App/MainWindow.axaml.cs` — `SlotSelected` only calls `Timeline.Select`.
- `src/managed/Nota.App/SessionView.cs:Select` — `SlotSelected` fires only when the *track*
  changes, so a slot on the same track is never followed.
- `src/managed/Nota.App/MainWindow.Detail.cs:OnTrackSelected` — a Session MIDI editor keeps
  `_editorTrackId = -1`, so every slot selection calls `ResetClipEditor()`; `keepClipTab` looks
  only at the arrangement selection, so Session always takes the panel over to Devices.
- `MainWindow.Detail.cs:SyncClipTab` / `OnDetailClip` — in Session the Clip tab is enabled only
  while an editor is open, which the reset above just cleared.
- Playhead (item 4): the tick in `MainWindow.axaml.cs` follows `SessionSlotPlayhead` for the open
  slot; it reads `_sessionSlotTrack/_sessionSlotScene`, which `ResetClipEditor()` clears.

## Root Cause Hypothesis

Session slot selection was wired as "select the track" only, and the track-selection path assumes
the arrangement (clip identity in `_editorTrackId`, clip selection in the timeline). Confidence: high
for items 1–3; medium for item 4 (needs a run to confirm what clears the slot identity).

## Proposed Remediation

**Preferred**: Route Session slot selection through its own handler. A filled slot opens (or
re-shows) its editor in the Clip tab — MIDI roll or audio editor — then selects the track; an empty
slot keeps today's "select the track, show Devices". Fire `SlotSelected` on any slot change. Teach
`OnTrackSelected` that a Session editor on the selected track is the clip to keep. Enable the Clip
tab when the selected slot holds a clip, and let it open that slot.

**Files likely to change**: `MainWindow.Detail.cs`, `MainWindow.axaml.cs`, `MainWindow.axaml`
(tab tooltip), `SessionView.cs`.

**Tests**: the wiring lives in `MainWindow` (not linked into the smoke test); verify by running
the app (before/after screenshots). Smoke test must stay green.

## Risks & Considerations

- A Drum Rack user on the Pattern tab should stay on Pattern when the selection moves.
- The floating Devices/Clip window must keep its clip pane in step.

## Open Questions (items 5–9 — product decisions, not wiring bugs)

- [NEEDS CLARIFICATION: Clip loop model (items 5 and 8). Today a clip plays its whole length on
  repeat ("Clips always loop" in `ClipPropsView`); the engine has no per-clip loop region or
  start/end markers (`MidiClip.lengthBeats` only; Session slots have a Loop flag and a length).
  Should Nota adopt separate clip Start/End markers plus a Loop region (Loop start, Loop length,
  Loop on/off) for MIDI and audio clips, Arrangement and Session alike? If so, in the Arrangement
  is "Start" the clip's position on the timeline (as now) or the start marker inside the clip, and
  what plays with Loop off when the clip is longer than its end marker — silence?]
- [NEEDS CLARIFICATION: Session slot envelopes (item 6). Clip envelopes (Velocity / Volume) exist
  only for Arrangement MIDI clips; the switch is deliberately inactive for Session slots. Add
  envelopes to Session slots (engine + project file), or hide the switch there?]
- [NEEDS CLARIFICATION: Stereo waveform (item 7). Peaks are one mono min/max series. A split
  L/R view needs per-channel peaks in the ABI. Default view (combined, per the reporter), and
  should the toggle be per clip, per editor or an app preference (like the arrangement's
  Waveform style)?]
- [NEEDS CLARIFICATION: Texture warp (item 9). Texture uses a fixed 220 ms window / 55 ms hop
  (`WarpStream.cpp`). The two parameters are presumably Grain Size and Flux — confirm, with
  ranges and defaults, and whether existing projects keep today's sound at the default.]
