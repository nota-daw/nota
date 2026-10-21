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

| ☐ | Control | File | Gesture today | Plan |
|---|---|---|---|---|
| ☑ | `Knob` | Controls/Knob.cs | DC reset | A — `Format` property; `KnobCell` attaches when set; DC branch removed, ⌥ reset |
| ☑ | `PanKnob` | Controls/PanKnob.cs | — | A — L/R/C read-out, default C |
| ☑ | `MiniFader` | Controls/MiniFader.cs | DC reset | A — dB read-out; DC branch removed |
| ☑ | `VFader` | Controls/VFader.cs | — (a click jumped the value) | A — dB read-out, default 0 dB; the drag is now relative, so the first click of a double-click moves nothing |
| ☑ | `SliderTrack` | Controls/SliderTrack.cs | DC `Reset` | A via its row; `Reset` moves to ⌥ |
| ☐ | `RangeSliderTrack` | Controls/RangeSliderTrack.cs | DC `Reset` | H — low / high fields; `Reset` on ⌥ |
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

| ☐ | Site | Plan |
|---|---|---|
| ☑ | DeviceCards/DeviceParamControls.cs (built-in device params; generic knob) | set `Format` |
| ☑ | DeviceCards/InstrumentControls.cs | set `Format` |
| ☑ | DeviceCards/PluginCard.cs (VST3/AU generic params) | `Format` from `PluginParamText` via probe — exempt from pure `TextAt` (plug-in text is only readable at the current value); probe path allowed here, documented |
| ☑ | DeviceCards/Rack/RackCardView.cs (×5), Rack/MacroPanel.cs | set `Format`; merge menu into `MacroPanel.ShowMenu` |
| ☑ | ModularView.cs (×5) | set `Format`; merge into `ShowModulateMenu` |
| ☐ | DeviceChainView.cs | set `Format` |
| ☑ | Device bodies: AutoFilter, AutoShift, BeatRepeat, Ceiling, Chamber, Compressor, Crush, Delay, DynamicEq, Eq, Forge, Lens, Prism, Reverb, Shutter, Utility, Valve, Vintage, Vocoder | set `Format` in each body's knob helper |
| ☑ | MIDI bodies: Arp, Chord, Generic, Length, Random, Velocity | set `Format` |
| ☑ | Instrument cards: Aurora (×2), Bass, Consort, Flux, Grain, Monolith, Mosaic (×2), Pendulum, Physical, Rhythm, Sampler, Volt | set `Format` |
| ☑ | `SliderTrack` outside kits: AutoGain, DynamicEq, Eq, Forge, Strata, Physical, Arp (×2), Chord, Length (×2), Random, Velocity (×2), Rack drum | already `ValueEntry.Attach` on read-out in most — verify each; DC `Reset` → ⌥ |
| ☑ | `MiniFader` sites: ArrangementView (×2), RackCardView (×2), MainWindow, MixerView, SessionView.Mixer (×2) | A with the track's dB / pan text |
| ☑ | `VFader`, `PanKnob`: MixerView | A |
| ☑ | Preview player volume (MainWindow / PreviewPlayer, "Double-click volume = reset to 0 dB" in Shortcuts) | A; reset → ⌥ |

## 3. Graph controls (handles that set numbers)

| ☐ | Control | File | Plan |
|---|---|---|---|
| ☐ | `AutoFilterCurve` | Controls/AutoFilterCurve.cs | H — cutoff → resonance |
| ☐ | `AfFader`, `AfEnvView`, `AfLfoView` | Controls/AutoFilterViews.cs | A (fader) · H (env / LFO handles) |
| ☐ | `ChorusVoicesView`, `ChorusFieldView` | Controls/ChorusViews.cs | H |
| ☐ | `CompFader`, `CompTransferView` (threshold node), `CompMotionView`, `CompKeyView` | Controls/CompViews.cs | A · H; history view exempt (display only) |
| ☐ | `ConsortFilterCurve`, `ConsortDelayView`, `ConsortPitchLane`, `ConsortStepGrid` | Controls/ConsortViews.cs | H for curve / delay; step and pitch lanes keep ⌥ paint — **exempt** (step editors, out of scope) unless they set a card parameter |
| ☐ | `ConsortPatchMatrix` | Controls/ConsortViews.cs | H per cell — see §4 |
| ☐ | `CrSpectrumView`, `CrTransferView` | Controls/CrushViews.cs | H |
| ☐ | `DelayTaps`, `DelayFader`, `DelayFilterCurve` | Controls/DelayViews.cs | H · A · H (low cut → high cut) |
| ☐ | `DynamicEqCurve` | Controls/DynamicEqCurve.cs | H — band freq → gain → Q; keep empty-space DC |
| ☐ | `Eq3Fader`, `Eq3Graph` | Controls/Eq3Views.cs | A · H |
| ☐ | `Eq8Curve` | Controls/Eq8Views.cs | H — band freq → gain → Q; keep empty-space DC |
| ☐ | `FlangerSweepView`, `FlangerBar` | Controls/FlangerViews.cs | H · A; comb view exempt (display) |
| ☐ | `FluxVectorPad` | Controls/FluxVectorPad.cs | H — X → Y |
| ☐ | `GrainWaveViz` | Controls/GrainWaveViz.cs | H — position → spray |
| ☐ | `KeysPickupView`, `KeysTremView` | Controls/KeysViews.cs | H — symmetry → drive (per its handle) |
| ☐ | `LensVSlider` | Controls/LensViews.cs | A; spectrum / scope / waterfall exempt (display) |
| ☐ | `LvHistoryView`, `LvGainFader` | Controls/LevelViews.cs | H · A |
| ☐ | `OrbitGainView`, `OrbitPanBar` | Controls/OrbitViews.cs | H — amount → rate · A |
| ☐ | `PhaserSweepView` | Controls/PhaserViews.cs | H; response view exempt (display) |
| ☐ | `ReverbTailView` | Controls/ReverbViews.cs | H if it drags (verify) |
| ☐ | `RhythmHitView`, `RhythmSampleView` | Controls/RhythmViews.cs | H; `RhythmStepStrip` exempt (step editor) |
| ☐ | `ShSignalView`, `ShEnvelopeView`, `ShKeyView` | Controls/ShutterViews.cs | H; `ShPill`, `ShPatternView` verify |
| ☐ | `VtCurveView`, `VtWearView`, `VtHarmView` | Controls/VintageViews.cs | H if they drag (verify) |
| ☐ | `VocoderGraph` | Controls/VocoderGraph.cs | H — low edge → high edge |
| ☐ | `VoltFilter` | Controls/VoltFilter.cs | H — cutoff → resonance |
| ☐ | `SynthViz` Filter pad | Controls/SynthViz.cs | H — cutoff (X) → reso (Y) |
| ☐ | `AuroraLfoStrip`, `AuroraStack`, `StrataWave`, `PadWave`, `ChordKeysViz` | Controls/ | verify: display only → exempt |
| ☐ | `ArpGrid` | Controls/ArpGrid.cs | exempt (pattern editor, ⌥ already used) unless a handle sets a card parameter |
| ☐ | `StereoMapView` handles | StereoMapView.cs | DC reset (pan → C, ⌥ volume → 0 dB) | H — pan → volume per track dot; ⌥ already resets volume, keep; reset pan via menu |
| ☐ | Sampler / Mosaic views | DeviceCards/Sampler/SamplerViews.cs, Mosaic/MosaicViews.cs | H for start / end / loop / zone handles (verify) |

## 4. Envelopes and matrices

| ☐ | Control | Plan |
|---|---|---|
| ☐ | `VoltEnv` — Volt (amp, filter), Bass (amp, filter), Aurora (×2), Operator, Physical noise | H — A · D→S · R nodes and segments; R where the card has no A/D/S/R controls beside the graph |
| ☐ | `SynthViz` Env tab | H — breakpoints; read-outs already in the graph |
| ☐ | Clip ADSR handles (`ClipAdsrKit`, arrangement lane) | H — commit via `CommitAdsr`; DC reset → ⌥ (Shortcuts row "double-click resets the stage" changes) |
| ☐ | Audio clip editor ADSR rows | K — already typed; reset → ⌥ |
| ☐ | Other envelope strips (Monolith / Pentad / Consort inline-knob ADSR) | covered by `Knob` |
| ☐ | `VoltMatrix` | H per cell, % −100…+100; DC types, ⌥ / menu **Clear**; RC → menu; clear wrapped in a gesture (undoable) |
| ☐ | `ConsortPatchMatrix` | H per cell; DC types depth (creates the cable); ⌥ / menu **Clear** removes |

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
