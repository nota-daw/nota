# Quickstart: checking the new look

1. Build: `scripts/build-linux.sh` (or `scripts/build.sh` on macOS). In a cloud container set
   `CI=true` and configure the engine with the CPM source overrides (research R7).
2. Run `dotnet run --project src/managed/Nota.App` and open the demo project.
3. Switch Studio Dark → Studio Light → Ember Graphite → Ember Paper (Preferences → Appearance).
   Every number is Geist and doesn't jitter while a knob turns; picked options are a soft tint.
4. Arrangement: track headers are two rows; select a clip (outline, not orange); add nine
   tracks and check the colour order; expand a group (bar down the members).
5. Open a MIDI clip (settings strip above the notes) and an audio clip (one column, no scroll).
6. Device panel: add each built-in device from the browser and compare with the style guide's
   Devices section. On EQ-8, fold and unfold the right column; switch tracks and back.
7. Modular view on the Keys track: straight cables, tinted headers, bypass greys the header.
8. Toolbar: Follow shows the new icon.
9. `DesignTokenCheck` output in the smoke test lists no new offenders.
