# Bug Verification: Nota Chroma spectrum column gaps

- **Slug**: chroma-spectrum-column-gaps
- **Tested**: 2026-11-20
- **Assessment**: [assessment.md](assessment.md)
- **Fix**: [fix.md](fix.md)
- **Result**: verified

| Check | Result | Evidence |
|---|---|---|
| Flat input at 234, 512, 522 and 783 columns | Pass | Every output column retained the input level. |
| Narrow peak and silent input | Pass | The compact view retained the spike and invalid samples stayed silent. |
| Interpolation at the first missing column | Pass | A linear ramp matched the level at the display column's center. |
| Physical pixel scan in L at 2× | Pass | A scan through each flat trace found 315 single-pixel lines spaced two pixels apart across the inspected 630-pixel span. |
| Live Studio Dark cards at 1× and 2× | Pass | Captured and inspected S and L with a playing clip. |
| Release app build and focused smoke test | Pass | The app built and all 18 Chroma checks passed. |
| Manual shots and check | Pass | Captured S and L in both Studio themes. The checker reported zero errors. |

The headless renders and focused visual fixture are in
`build/color-prototype/spectrum-column-gaps`. The full engine suite was
outside the focused visual verification.
