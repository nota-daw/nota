# Bug Assessment: Short docked MIDI editors lose their notes

- **Slug**: docked-note-area (generated from report)
- **Created**: 2026-11-02
- **Source**: pasted PR #425 review follow-up
- **Verdict**: valid
- **Severity**: high

## Symptom and reproduction

The primary lane now reserves 72 pixels for its five tabs, but the docked Detail row
still permits 120 pixels for the entire editor. Its 32-pixel host toolbar and 38-pixel
clip header further reduce the space available for the roll. Shrink the actual docked
MIDI editor to 190 pixels (roughly 120 for the roll): notes have no usable viewport.
The earlier standalone 300-pixel test does not exercise these headers or host limits.
Approved issue #422 remains open; this is a correction to its resizable MIDI lanes.

## Root Cause and Suspected Code Paths

`MainWindow.Detail.cs::ShowDetail` and `FollowDetailVisibility` use 120 as the minimum
for every Detail tab. `FitControllerLanes` cannot fit ruler, 72-pixel primary lane,
divider, toolbar and notes into the remaining space. Confidence: high.

## Proposed Remediation

Raise the docked MIDI Clip editor minimum to reserve both headers, all roll chrome,
the optional clip strip and horizontal scrollbar, and a useful note viewport. Keep
other Detail tabs' existing minimum. Apply the limit when opening/restoring and
through the existing splitter/edge/window-fitting paths; test any interaction with
the docked Mixer at the minimum main-window size. Prefer a host layout correction
over shrinking or hiding the five primary tabs.

Files likely to change: `MainWindow.Detail.cs`, CCProbe, CHANGELOG.md, the lane spec,
plan and validation; other host files only if actual testing reveals a necessary
minimum-window adjustment. Update the manual handoff if visible resizing guidance changes.

## Tests to Add

Use the real MainWindow and ClipEditorView, with both headers. Reproduce at the old
docked heights; test minimum/saved heights, all tab hits, a note hit, optional strip
and scrollbar, extra lanes, hide/show, reset, maximize/restore and docked Mixer. Keep
the existing standalone lane tests. Capture before/after on the same actual host.
Run the Windows build/smoke/scanner gate.

## Risks & Considerations

A higher panel minimum takes space from the main view and Mixer. Verify actual
window bounds instead of checking only declared heights. No audio-thread or MIDI
data changes are needed. Extension configuration has no hooks.

## Open Questions

Exact host minimum and window-fitting consequences will be established by layout tests.
