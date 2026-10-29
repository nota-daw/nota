# Control audit: numeric parameter controls

The acceptance list from nota-daw/nota#240 (FR-017). Every control type that sets a
numeric value, and every place that builds one outside the shared kits. Each row ends
**converted**, **covered** (it goes through a converted kit/helper) or **exempt** (with a
reason). The first implementation task re-checks this list against the code and adds
anything missing; the last one ticks every row.

Legend for *Gesture today*: DC = double-click, RC = right-click, ⌥ = Alt-click.
*Plan*: **A** = `ValueEntry.Attach` (one value), **H** = `IValueEntryHost` (handles),
**K** = covered by a kit (`KnobCell`, `SliderRow`, `ClipEditorKit`), **R** = adds an
A/D/S/R read-out row.

## 1. Shared controls

| ☑ | Control | File | Gesture today | Plan |
|---|---|---|---|---|
| ☑ | `Knob` | Controls/Knob.cs | DC reset | A — `Format` property; `KnobCell` attaches when set; DC branch removed, ⌥ reset |
| ☑ | `PanKnob` | Controls/PanKnob.cs | — | A — L/R/C read-out, default C |
| ☑ | `MiniFader` | Controls/MiniFader.cs | DC reset | A — dB read-out; DC branch removed |
| ☑ | `VFader` | Controls/VFader.cs | — (a click jumped the value) | A — dB read-out, default 0 dB; the drag is now relative, so the first click of a double-click moves nothing |
| ☑ | `SliderTrack` | Controls/SliderTrack.cs | DC `Reset` | A via its row; `Reset` moves to ⌥ |
| ☑ | `RangeSliderTrack` | Controls/RangeSliderTrack.cs | DC `Reset` | H — `IValueHandles` (nearer handle); Velocity attaches Out low / Out high, the reset rides on the low field |
| ☑ | `DragNumber` | Controls/DragNumber.cs | DC types | already typed — move to the shared box and parser, add ⌥ reset where it has a default |
| ☑ | `PanBar` | Controls/PanBar.cs | DC reset to C | A — L/R/C read-out, default C |
| ☑ | `ValueBar` | Controls/ValueBar.cs | DC reset | A — ⌥ fallback in the control; no call sites today, so nothing to attach |
| ☑ | `MixBar` | Controls/MixBar.cs | — | **exempt** — display only (no pointer input) |
| ☑ | `PerformWheel` | Controls/PerformWheel.cs | DC reset | A for the mod wheel; the spring pitch wheel is **exempt** (it returns to centre on release, a typed value would not stay) — ⌥ reset kept on both |
| ☑ | `ChordShiftTrack` | Controls/ChordShiftTrack.cs | DC reset to 0 | A — semitones −12…+12, ⌥ reset |
| ☑ | `DeviceCardKit.KnobCell` | DeviceCardKit.cs | — | K — attach knob + value read-out |
| ☑ | `DeviceCardKit.SliderRow` | DeviceCardKit.cs | DC types | K — optional pure `textAt`; DC reset on the track → ⌥ |
| ☑ | `ClipEditorKit` rows | ClipEditorKit.cs | DC types | K — same as `SliderRow` |

## 2. Knob factories and direct builders

| ☑ | Site | Plan |
|---|---|---|
| ☑ | DeviceCards/DeviceParamControls.cs (built-in device params; generic knob) | set `Format` |
| ☑ | DeviceCards/InstrumentControls.cs | set `Format` |
| ☑ | DeviceCards/PluginCard.cs (VST3/AU generic params) | `Format` from `PluginParamText` via probe — exempt from pure `TextAt` (plug-in text is only readable at the current value); probe path allowed here, documented |
| ☑ | DeviceCards/Rack/RackCardView.cs (×5), Rack/MacroPanel.cs | set `Format`; merge menu into `MacroPanel.ShowMenu` |
| ☑ | ModularView.cs (×5) | set `Format`; merge into `ShowModulateMenu` |
| ☑ | DeviceChainView.cs | `Format` set on the Sends knobs (attached by `KnobCell`) |
| ☑ | Device bodies: AutoFilter, AutoShift, BeatRepeat, Ceiling, Chamber, Compressor, Crush, Delay, DynamicEq, Eq, Forge, Lens, Prism, Reverb, Shutter, Utility, Valve, Vintage, Vocoder | set `Format` in each body's knob helper |
| ☑ | MIDI bodies: Arp, Chord, Generic, Length, Random, Velocity | set `Format` |
| ☑ | Instrument cards: Aurora (×2), Bass, Consort, Flux, Grain, Monolith, Mosaic (×2), Pendulum, Physical, Rhythm, Sampler, Volt | set `Format` |
| ☑ | `SliderTrack` outside kits: AutoGain, DynamicEq, Eq, Forge, Strata, Physical, Arp (×2), Chord, Length (×2), Random, Velocity (×2), Rack drum | already `ValueEntry.Attach` on read-out in most — verify each; DC `Reset` → ⌥ |
| ☑ | `MiniFader` sites: ArrangementView (×2), RackCardView (×2), MainWindow, MixerView, SessionView.Mixer (×2) | A with the track's dB / pan text |
| ☑ | `VFader`, `PanKnob`: MixerView | A |
| ☑ | Preview player volume (MainWindow / PreviewPlayer, "Double-click volume = reset to 0 dB" in Shortcuts) | A; reset → ⌥ |

## 3. Graph controls (handles that set numbers)

| ☑ | Control | File | Plan |
|---|---|---|---|
| ☑ | `AutoFilterCurve` | Controls/AutoFilterCurve.cs | `AttachAll` — cutoff → resonance (a drag anywhere moves it) |
| ☑ | `AfFader`, `AfEnvView`, `AfLfoView` | Controls/AutoFilterViews.cs | A (Shape faders, Vintage state faders) · `AttachAll` env amount · LFO amount → phase |
| ☑ | `ChorusVoicesView`, `ChorusFieldView` | Controls/ChorusViews.cs | `AttachAll` Amount; field view **exempt** (display only) |
| ☑ | `CompFader`, `CompTransferView` (threshold node), `CompMotionView`, `CompKeyView` | Controls/CompViews.cs | A (threshold, make-up / key gain) · `AttachAll` threshold → ratio · H HP → Q, LP → Q; motion and history **exempt** (display only) |
| ☑ | `ConsortFilterCurve`, `ConsortDelayView`, `ConsortPitchLane`, `ConsortStepGrid` | Controls/ConsortViews.cs | `AttachAll` cutoff → reso; delay view **exempt** (display only); step and pitch lanes **exempt** (step editors) |
| ☑ | `ConsortPatchMatrix` | Controls/ConsortViews.cs | H per cell — see §4 |
| ☑ | `CrQuantView`, `CrSpectrumView`, `CrTransferView` | Controls/CrushViews.cs | `AttachAll` bits → rate · H Nyquist (rate) / post filter · H drive |
| ☑ | `DelayTaps`, `DelayFader`, `DelayFilterCurve` | Controls/DelayViews.cs | taps **exempt** (display only) · A · H low cut / high cut |
| ☑ | `DynamicEqCurve` | Controls/DynamicEqCurve.cs | H — band freq → gain → Q (`DynEq.BandFields`); DC keeps band on/off and empty-space add; **Type value…** in the band menu |
| ☑ | `Eq3Fader`, `Eq3Graph` | Controls/Eq3Views.cs | A (reset lifts the kill) · H crossovers (box over the handle) and band zones |
| ☑ | `Eq8Curve` | Controls/Eq8Views.cs | H — band freq → gain → Q; DC keeps band on/off and empty-space add; **Type value…** in the band menu |
| ☑ | `FlangerCombView`, `FlangerSweepView`, `FlangerBar` | Controls/FlangerViews.cs | `AttachAll` delay → feedback on the comb (the view that drags); sweep and bar **exempt** (display only) |
| ☑ | `FluxVectorPad` | Controls/FluxVectorPad.cs | `AttachAll` X → Y; DC keeps its `DoubleTapped` reset (fields carry no default) |
| ☑ | `GrainWaveViz` | Controls/GrainWaveViz.cs | `AttachAll` position (spray is not dragged here); DC keeps `DoubleTapped` |
| ☑ | `KeysPickupView`, `KeysTremView` | Controls/KeysViews.cs | host — symmetry → distance (none for Clav); trem view **exempt** (display only) |
| ☑ | `LensVSlider` | Controls/LensViews.cs | A — the parameter the rail drives now; spectrum / scope / waterfall **exempt** (display) |
| ☑ | `LvHistoryView`, `LvGainFader` | Controls/LevelViews.cs | A target · A gain (+ the ceiling box) |
| ☑ | `OrbitGainView`, `OrbitPanBar` | Controls/OrbitViews.cs | `AttachAll` amount → phase; pan bar **exempt** (display only) |
| ☑ | `PhaserResponseView`, `PhaserSweepView` | Controls/PhaserViews.cs | `AttachAll` center → feedback on the response (the view that drags); sweep **exempt** (display) |
| ☑ | `ReverbTailView` | Controls/ReverbViews.cs | H — decay (the tail) / pre-delay (the marker) |
| ☑ | `RhythmHitView`, `RhythmSampleView` | Controls/RhythmViews.cs | `AttachAll` decay → tune · H start / end (DC keeps loading a file); `RhythmStepStrip` **exempt** (step editor) |
| ☑ | `ShSignalView`, `ShEnvelopeView`, `ShKeyView` | Controls/ShutterViews.cs | H via `ShDragView : IValueHandles` (also Crush, Ceiling, Forge, Beat Repeat views); the fill bars (Complexity / Smooth / Swing) A; `ShPill` **exempt** (display), `ShPatternView` **exempt** (step editor) |
| ☑ | `VtCurveView`, `VtWearView`, `VtHarmView` | Controls/VintageViews.cs | `AttachAll` drive · depth → rate (of what the window shows) · drive |
| ☑ | `VocoderGraph` | Controls/VocoderGraph.cs | H — low edge / high edge |
| ☑ | `VoltFilter` | Controls/VoltFilter.cs | host — cutoff → reso (all four cards); right-click no longer drags |
| ☑ | `SynthViz` Filter pad | Controls/SynthViz.cs | host — cutoff → reso |
| ☑ | `AuroraLfoStrip`, `AuroraStack`, `StrataWave`, `PadWave`, `ChordKeysViz` | Controls/ | **exempt** — display only (no pointer input) |
| ☑ | `ArpGrid` | Controls/ArpGrid.cs | **exempt** (pattern editor, ⌥ already used); the Arp steppers are A |
| ☑ | `StereoMapView` handles | StereoMapView.cs | host — pan → volume per track dot (master: select only); DC centres the pan as before; ⌥ double-click 0 dB is gone (⌥-click types; type 0) |
| ☑ | Sampler / Mosaic views | DeviceCards/Sampler/SamplerViews.cs, Mosaic/MosaicViews.cs | H wave markers (Sampler), env A · D→S · R, `AttachAll` filter cutoff → reso (both cards), A Mosaic curves; Mosaic zone markers and zone number boxes **exempt** (zone data in the program, not parameters) |
| ☑ | `ChamberFader`, `ChamberIrView`, `ChamberDecayGraph`, `ChamberEqCurve` | Controls/ChamberViews.cs | A blend / dry-wet · H IR start / decay trims · H band RT60 markers · H low cut → shelf, high cut → shelf; LFO view **exempt** (display) |
| ☑ | `PrismSpectrum`, `PrismTransfer`, Prism value boxes | Controls/PrismViews.cs | H crossovers · H threshold → ratio per dot · A |
| ☑ | Output / value boxes: EQ-8, Dynamic EQ, EQ-3, Auto Gain ceiling, Forge stage outputs, Chord velocity offsets, Scale range, Arp steppers, Consort cable depth, drum-pad pan | bodies | A over their read-outs |

## 4. Envelopes and matrices

| ☑ | Control | Plan |
|---|---|---|
| ☑ | `VoltEnv` — Volt (amp, filter), Bass (amp, filter), Aurora (×2), Operator, Physical noise | host — A · D→S · R nodes and segments, formatter per stage from the card; the cards' A/D/S/R read-out cells type too (Operator has knobs) — no extra `EnvReadouts` row needed |
| ☑ | `SynthViz` Env tab | host — A · D→S · R breakpoints |
| ☑ | Clip ADSR handles (`ClipAdsrKit`, arrangement lane) | host on the lane — each value one `CommitAdsr`; DC still resets the stage (gestures per #274) |
| ☑ | Audio clip editor ADSR rows | K — already typed |
| ☑ | Other envelope strips (Monolith / Pentad / Consort inline-knob ADSR) | covered by `Knob` |
| ☑ | `VoltMatrix` | host per cell, % −100…+100; ⌥ types, DC / menu **Clear** (one gesture, undoable); RC → menu; Tab walks the routed cells |
| ☑ | `ConsortPatchMatrix` | host per cell; ⌥ types the depth (creates the cable); DC / menu **Clear** pulls it |

## 5. Out of scope (recorded, not converted)

| Area | Reason |
|---|---|
| Piano-roll note velocity / expression lanes, MPE lanes | Note data, not parameters; own gesture set (DC adds / deletes points) |
| Arrangement automation breakpoints | Own editor; DC adds a point |
| Step sequencer cells (Consort steps / pitch, Rhythm steps, Arp grid) | Step editors with ⌥ paint |
| Plug-ins' own editor windows | Not Nota controls |
| Nota Remote (phone) | Separate UI |
| Tempo / time-signature / inspector `DragNumber` fields | Already typed (they keep their gesture; only the box look and parser are shared) |

## 6. Interim (PR 1)

Graph controls and custom value boxes that still reset on double-click until PR 3 also
reset on ⌥-click now, so ⌥ is the reset gesture everywhere from PR 1 on: AutoFilter /
Delay / Comp / Chamber / Eq3 / Level / Lens faders, the Reverb / Phaser / Chorus / Orbit /
Crush / Flanger / Vocoder / Prism views, `ShDragView` (Shutter, Ceiling, Crush, Beat Repeat,
Forge views), the output-gain boxes in EQ-8 / Dynamic EQ / EQ-3 / AutoGain / Forge /
Prism / Shutter, the Chord / Scale / Arp value boxes, Consort mod amount, Mosaic value box,
the Drum-rack pad pan, and the Sampler handles. Not touched (double-click means something
else there): EQ-8 / Dynamic EQ add-band and band on/off, Rhythm sample load, Consort
painting, Flux pad / Grain view (`DoubleTapped`), `StereoMapView`, `VoltMatrix` (PR 2).
The rack macro knobs type and reset but keep their own right-click menu (no Type value…
item there yet).

## 7. Gestures after #274 (PR 2 / PR 3)

Part 1 swapped the gestures after review: **⌥-click types** (and a double-click on a read-out),
**double-click resets** (or clears a route). The graph, envelope and matrix hosts follow the
same scheme, so the "DC types" wording in older rows reads as "⌥ types" now. A host's fields
without a default let a double-click through to the control (EQ band on / off, Rhythm load,
Flux / Grain `DoubleTapped`).
