# Chroma root names are truncated

- **Slug** chroma-root-clipping
- **Source** User screenshot of the root picker on the large Chroma card
- **Verdict** Valid
- **Severity** Low

The root picker has a 34-unit outer width. The border, horizontal padding
and chevron leave 10 units for its label, so two-character roots can
collapse to an ellipsis. Rebalance the manual row to leave enough room for
the widest note name and a gap between controls.
