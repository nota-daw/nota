# Bug Assessment: Short clips wrap their name onto a second line

- **Slug**: clip-name-wrap
- **Created**: 2026-11-12
- **Source**: https://github.com/nota-daw/nota/issues/590 (host: github.com, allowlisted; written by the reporter in this session)
- **Verdict**: valid
- **Severity**: low

## Report (verbatim or summarized)

When a clip is narrower than its name, the name wraps onto a second line ("Au… / 8") and
spills over the waveform. Expected: one line, cut with an ellipsis ("Audio…").

## Symptom

A clip label in the arrangement breaks between words when the clip is short; each line is
trimmed separately. It should stay on one line and end in an ellipsis.

## Reproduction

1. Add an audio track and drop a sample into the arrangement.
2. Split the clip, or trim it shorter than its name.
3. The title wraps onto a second line.

## Suspected Code Paths

- `src/managed/Nota.App/ArrangementView.LaneControl.cs:1608` — clip name in the content view
  (`NotaFormattedText` with `MaxTextWidth` + `CharacterEllipsis`, no line limit).
- `src/managed/Nota.App/ArrangementView.LaneControl.cs:1657` — the same label in the title-band
  (classic) look.
- `src/managed/Nota.App/ArrangementView.Sections.cs:629` — section-span labels, same pattern.

## Root Cause Hypothesis

Avalonia's `FormattedText` word-wraps at `MaxTextWidth` and applies `Trimming` per line, so
without `MaxLineCount = 1` a long name becomes several trimmed lines. Confidence: high (other
views — `SessionView.Grid`, `PianoRollView.ClipStrip`, `StereoMapView` — already set it).

## Proposed Remediation

**Preferred**: set `MaxLineCount = 1` on the three arrangement labels.

**Files likely to change**:
- `src/managed/Nota.App/ArrangementView.LaneControl.cs`
- `src/managed/Nota.App/ArrangementView.Sections.cs`

**Tests to add or update**:
- Visual check: a short clip with a two-word name renders one ellipsised line.

## Risks & Considerations

- None beyond the label; layout height already assumes one line.

## Open Questions

- None.
