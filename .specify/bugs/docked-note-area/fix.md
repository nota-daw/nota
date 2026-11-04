# Bug Fix: Short docked MIDI editors lose their notes

- **Slug**: docked-note-area (from assessment context)
- **Fixed**: 2026-11-02
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary and Changes

`MainWindow.Detail.cs` reserves a 300-pixel minimum for MIDI Clip content; other
Detail content retains 120 pixels. Opening, default/reset heights and visibility
restoration use the content-specific minimum. Existing splitter and edge dragging
respect the row minimum, as does window fitting.

`FitBottomPanels` also calculates the main-window minimum from visible panel minima
and measured window chrome, bounded by the original window minimum. This prevents a
docked Mixer from pushing the larger MIDI editor past the window's bottom. Hiding
panels or maximizing the clip editor releases the extra minimum.

The changelog, spec, plan and paired EN/RU manual handoff describe this resizing
behavior. No new shortcuts or capability-list entries are needed. The external
nota-docs checkout is absent; no site build is claimed.

## Tests Added or Updated

CCProbe now hosts the real ClipEditorView in the real MainWindow with both headers.
It restores a 190-pixel preference, exercises hide/show, reset, maximize/restore,
clip strip and scrollbar, extra CC lanes and the panel's edge drag. Actual hit tests
and pointer events select a note and all five tabs. It checks the docked Mixer fits
inside the window, hiding it restores the base minimum, and non-MIDI content keeps
the previous minimum. Before/after captures use the full actual window bounds.

## Local Verification

Old code: `Docked layout: panel 190, roll 119, notes 0`; regression exits 1.
Fixed code: `panel 300, roll 229, notes 104`, with 57 pixels of notes remaining when
the optional clip strip, scrollbar and extra lanes are all present (over four default
12-pixel note rows). The actual note-selection and tab-selection checks pass.
Full results are recorded in test.md.

## Deviations from Assessment

Host testing exposed that panel minima together exceeded the original main-window
minimum with a docked Mixer. The assessment anticipated this risk; the correction
stays in MainWindow.Detail.cs and adds the required aggregate window minimum.
The invariant tested is at least 48 pixels of reachable notes (four default rows),
alongside actual note selection, rather than just checking a declared panel height.
No extension hooks are registered.

## Follow-ups

Complete the Windows gate, attach full-window evidence and update PR #425.
