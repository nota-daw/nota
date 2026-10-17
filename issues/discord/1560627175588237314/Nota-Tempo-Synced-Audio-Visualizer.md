**Native Tempo-Synced Oscilloscope and Audio Visualizer**

Add a native visualizer to Nota that combines useful signal comparison with polished, customizable presentation for production, recording, streaming and live performance.

**Core functional scope**
- Beat, bar and waveform views synchronized to the project tempo.
- Main-signal and sidechain waveform overlays.
- An optional combined waveform with color indicating which source dominates at each point.
- Mono, stereo, left-channel and right-channel viewing.
- Visual-only level scaling, including a useful range such as −24 to +24 dB, without changing the audio.
- Smooth, anti-aliased, GPU-accelerated rendering with a 60 fps target where appropriate.
- Editable themes, glow controls, theme saving, importing, exporting and folder organization.
- Optional transparent-PNG logo overlay with beat-reactive animation.
- An unobtrusive presentation mode without mandatory third-party branding.
- Transparent audio passthrough with no additional audio latency.

**Additional proposed Nota refinements**

**Signal selection and comparison**
- Select tracks, buses, returns, master output and available sidechains directly.
- Make pre-FX versus post-FX inspection explicit.
- Label sources and provide independent colors, visibility and display gain.
- Offer overlay, stacked and combined views so dense signals remain readable.
- State whether a combined waveform is the actual sample sum or another display representation.
- Provide consistent timing alignment and show when latency compensation affects the comparison.

**Inspection controls**
- Adjustable beat/bar span, zoom, freeze and frame capture.
- Optional amplitude grid, time grid, zero line and peak/clipping indicators.
- Keep meter units and display scaling clearly labelled.
- Provide free-running inspection when transport is stopped or no meaningful tempo exists.
- Follow tempo changes, time signatures, loop boundaries and transport repositioning without misleading visual jumps.
- Preserve brief transients when reducing data for display rather than simply dropping samples.

**Presentation**
- Resizable, dockable and detachable views, with a clean full-screen option for an external display.
- Minimal analysis themes alongside optional decorative themes.
- Configurable logo position, size, opacity and animation strength.
- Reduced-motion support and an option to disable glow or beat-reactive effects.
- Keyboard-accessible controls, readable contrast and clear focus indication.
- Save presentation and analysis presets independently where useful.

**Performance and reliability**
- Keep rendering and graphics work away from the real-time audio thread.
- Use bounded buffering and avoid blocking audio when the UI stalls.
- Allow reduced frame rates and quality settings; suspend unnecessary rendering when hidden.
- Adapt to high-DPI displays without excessive GPU or battery usage.
- Recover safely from a graphics-device failure without interrupting audio.
- Treat zero added audio latency as a design requirement, not a claim that visual display latency is zero.

**Goal**
Provide a built-in, efficient tool for understanding timing and signal relationships, while also producing clean, customizable visuals.

**Reference only**
[Wide Blue Sound Tracer](https://www.widebluesound.com/tracer/)
The additional inspection, accessibility and workflow controls above are proposed Nota extensions, not claims about the reference product.
