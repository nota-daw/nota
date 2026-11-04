# Research

## Shared lane shell

Decision: compose native buttons above ControllerLane rather than inside its drawing
surface. Rationale: pointer routing stays independent and every point is reachable.
Rejected: overlaying curve points or duplicating the automation editor.

## Scale and alignment

Decision: use the current gutter and graph's ValueToY function. Rationale: normal
and folded Drum Rack time alignment survives unchanged. Rejected: an extra graph
column shifting the beat axis relative to notes.

## Layout budget

Decision: remove the toolbar row, reserve sidebar action height, and use actual
stacked strip height. Rationale: the PR subtracts just one 18 px row even when clips
stack; a review identifies hidden notes.

## Integration constraints

The IDE workspace is a source export without Git metadata. A shallow checkout of
the existing PR branch is used in a sibling directory; all PR commits remain ancestors
and pushes are ordinary fast forwards. The reference was found in PR #425's design
review comment: `pr-assets/pr425-cc-lanes-review/cc-lanes-mockup.png`. The final shell
uses compact backing only beneath its controls and a thin painted resize seam. The
existing six-pixel resize hit zone and a shallow row separate controls from editing.

Original PR head: `957d52865886483dd63f81f1065c70915c3f3667`.
