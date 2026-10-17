**“Custom” Export Range Cannot Be Selected**

In Nota v0.33.1 on macOS, I cannot select “Custom” under Range in the Export audio window.

**Context**
- I am currently exploring the application and looking for bugs and workflow improvements.
- I have not done substantial work in this project yet, so this is not a fully developed song or a completely empty project.
- I do not know whether this is a bug, an unfinished feature or a prerequisite that my project does not currently meet.

**Steps observed**
1. Open Export audio.
2. Locate the Range options: Loop, Full song and Custom.
3. Click Custom.

**Actual result**
I cannot activate Custom or define a custom export range. It is not clear what prevents selection or what I need to do to enable it.

**Expected behavior**
- If custom ranges are supported, allow users to select Custom and explicitly specify the export start and end positions.
- Display the resulting duration and validate that the end is after the start.
- If an arrangement selection or another prerequisite is required, explain it directly in the dialog or a tooltip, with a clear instruction for enabling the option.
- If the feature is not implemented yet, label it accordingly rather than leaving its status ambiguous.
- Keep the selected range, displayed start/end positions and actual exported range consistent.

Please clarify the intended behavior and, if necessary, improve the unavailable-state explanation. This report does not assume that the underlying cause has already been identified.
