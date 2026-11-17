# Bug Fix: Short clips wrap their name onto a second line

- **Slug**: clip-name-wrap
- **Fixed**: 2026-11-12
- **Assessment**: ./assessment.md
- **Status**: applied

## Summary

The arrangement's clip names (both looks) and section-span labels set `MaxLineCount = 1`, so a
name wider than its clip stays on one line and ends in an ellipsis instead of word-wrapping.

## Changes

| File | Change | Notes |
|------|--------|-------|
| `src/managed/Nota.App/ArrangementView.LaneControl.cs` | modified | `MaxLineCount = 1` on the clip name in the content view and in the title band. |
| `src/managed/Nota.App/ArrangementView.Sections.cs` | modified | The same on section-span labels. |
| `CHANGELOG.md` | modified | `[Unreleased]` → Fixed entry. |

## Diff Highlights

```csharp
{ MaxTextWidth = Math.Max(8, rect.Width - 14), MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
```

## Verification

- Headless render of the arrangement zoomed out so clips are narrower than their names: one
  ellipsised line (see test.md).
