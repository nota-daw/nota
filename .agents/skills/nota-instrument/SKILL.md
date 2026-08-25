---
name: nota-instrument
description: How to build a Nota built-in instrument, audio effect or MIDI effect end-to-end — the DSP class, the kind-registration checklist across the C++ engine and C# managed layers, and the device-card UI design approach (signal-chain panels, tabs, the shared gauge knob, interactive graphs, using the card space well). Use whenever adding or reworking a built-in synth / audio effect / MIDI effect or its editor. Reference implementations: Nota Volt (instrument), Nota Vintage / Auto Filter (audio effect), Nota Arp (MIDI effect).
user-invocable: true
---

# Building a Nota built-in instrument, effect or MIDI effect

This skill covers **all three built-in device families** — an **instrument**
(the track's sound source), an **audio effect** (an insert `Device`), and a
**MIDI effect** (a `MidiDevice` that transforms notes before the instrument).
The DSP conventions (§1) and card design (§3) are shared; only the base class,
the **registration path (§2)** and the **card interface (§3)** differ — effects
and MIDI effects register through a much lighter path than instruments.

The canonical example is **Nota Volt** (virtual-analog synth, kind 6):
`src/native/nota.engine/src/VoltSynth.h` (DSP) + `DeviceCards/Instruments/VoltInstrumentCard.cs` (UI).
Its editor is the reference build of the card layout — **read it first when designing
a synth UI, together with `DESIGN.md` § Device cards and the rendered specimens in
`DESIGN.html`.** Good
effect exemplars: **Auto Filter** and **Nota Vintage** (audio, `: public Device`),
**Nota Arp** (MIDI, `: public MidiDevice`). Also load the `nota-design` skill.

Built-in **instruments** (`kind()` ≥ 0): 0 Synth, 1 Sampler, 2 Physical (modal),
3 Instrument Rack, 4 Drum Rack, 5 Aurora (wavetable), 6 Volt, 7 Bass. **Audio
effects** (`builtinKind()`): 0 EQ, 1 Compressor, 2 Reverb, 3 Delay, 4 Utility,
5 Effect Rack, 6 Amp, 7 Auto Filter, 8 Vintage. **MIDI effects** (`midiKind()`):
0 Arp, + Chord / Scale / Note Length / Velocity / Random. **Grep the current
registration for the next free kind before picking one** — this list drifts.

## 1. DSP class (header-only)

Put the whole device in one header `src/native/nota.engine/src/<Name>.h` —
`final : public Instrument`, `: public Device` (audio effect, in-place stereo
`process(buf, frames)`), or `: public MidiDevice` (MIDI effect). No new .cpp, no
CMakeLists change. **Allocation-free after construction** (pre-size buffers). Rules:

- **Parameters via the plugin-param interface** (`pluginParamCount/Id/Name/Get/Set/
  IndexOfId`). Values are **normalized 0..1** in `std::atomic<float> pn_[N]`
  (audio-thread read). Denormalise inside `render()` to musical units
  (`expMap(v, lo, hi)` for time/cutoff/rate; bipolar = `(v-0.5)*2`; discrete =
  `round(v*(n-1))`). This gives **automation, persist and clone for free** — the
  engine already routes `PluginParam` + `deviceIndex<0` to `instrument->pluginParamSet`.
- **Param order = the persisted state layout — APPEND ONLY.** `getState`/`setState`
  serialize `N` little-endian floats; `setState` tolerates a shorter/longer blob.
  `clone()` copies `pn_` + sample rate. Ids are stable strings; names are display.
- **DSP conventions:** 16 voices, per-sample outer loop when modulation must stay
  smooth; recompute expensive filter coefficients at control rate (every ~16
  samples), not per sample. Steal the quietest voice. polyBLEP saw/square if you
  care about aliasing (Volt/Aurora do); a TPT/Cytomic SVF gives LP/HP/BP/Notch
  from one filter. `kind()` returns the new integer; `displayName()` the label.

## 2. Registration checklist (mirror an existing kind)

Grep the exemplar and copy every site. **Instruments** carry the most sites
(they need their own track + C ABI); **audio/MIDI effects register through a much
lighter path — see the end of this section.**

### Instruments (`grep -rn Volt` / `grep -rn Aurora`)

**Native:** `Engine.h` decl `add<Name>Track`; `Engine_Devices.cpp` (the method +
`#include` + `cloneInstrument` switch + `makeBuiltinInstrument` switch);
`RackCore.h` `makeInstrument` switch + include; C ABI
`nota_engine_add_<name>_track` in `nota_engine_track.cpp` **and** the decl in
`include/nota/nota_engine.h` (the `_nota_*` export list is a wildcard — no change).

**Managed:** `NativeMethods.Tracks.cs` (`[LibraryImport]`), `NotaEngine.Tracks.cs`
(wrapper), `IAudioEngine.cs` (interface); `BrowserViewModel.cs` (instrument list,
`BuiltinKind=N`); `MainWindow.Browser.cs` + `MainWindow.Dnd.cs` (`N => Engine.Add…`);
`ProjectService.cs` (`{ Kind: N }`) + `PresetService.cs` (`N =>`); rack/drum
"add chain / add pad" menus in `DeviceCards/Rack/RackCardView.cs`
(`AddChainButton`, `ShowAddPadMenu`).

**Automation menu:** built-in instruments (`TrackInstrumentKind >= 0`) must list
their params *directly, grouped* via `BuildBuiltinInstrumentAutoMenu`
(`ArrangementView.Automation.cs`) — NOT the hosted-plugin "Choose parameter…/Learn"
picker (that path made empty-id lanes → "drew a curve, no effect").

### Audio effects (`grep -rn AutoFilter` / `grep -rn Vintage`) — much lighter

No track, no C ABI, no `Engine.h` method: effects are added generically by
`addTrackBuiltinDevice(trackId, kind)`. Only:
- **Native:** the header `: public Device` with `builtinKind()` returning the kind,
  then register that kind in the **three switches** in `Engine_Devices.cpp` —
  `addTrackBuiltinDevice` **+** `cloneDevice` **+** `makeBuiltinDevice` — plus the
  `#include`. Params use the plain `Device` interface (`paramCount/paramName/
  paramMin/paramMax/getParam/setParam`); **prefer normalized 0..1** (mirror
  AutoFilter/Vintage, not the older raw-unit Amp/EQ). Persist / clone / automation
  are **generic** through the base `Device` (by `builtinKind()` + params) — no
  `getState`/`setState`, no per-kind managed persist code.
- **Managed:** `DeviceCardFactory` `[kind] = new NameDeviceBody()`; a
  `BrowserViewModel.cs` FX entry (`Kind = BuiltinEffect, BuiltinKind = N`). The
  browser add/drop path (`MainWindow.Browser.cs`/`.Dnd.cs`) is already generic.

### MIDI effects (`grep -rn Arpeggiator` / `grep -rn MidiScale`)

Mirror an existing `MidiDevice` (e.g. `Arpeggiator.h`) with `midiKind()`; register
in the engine's MIDI-device factory + `MidiDeviceCardFactory` `[kind]` + the MIDI
browser list. Params/persist/clone are generic like audio effects. Cards sit
**before** the instrument in the chain.

## 3. Editor card architecture

Cards are **strategy classes under `src/managed/Nota.App/DeviceCards/`**, resolved by
a factory and handed a `DeviceCardContext` — `DeviceChainView` is a thin orchestrator
(row + registries + the 60 Hz `RefreshSynthLive` tick) and builds nothing itself.
To add an editor, write a class and register it — no `switch` to edit (open/closed):

- **Instrument** (kind ≥ 0, the track's instrument): `class NameInstrumentCard :
  IInstrumentCard` in `DeviceCards/Instruments/`, `Control Build(DeviceCardContext ctx)`;
  register `[kind] = new NameInstrumentCard()` in `InstrumentCardFactory`.
- **Audio effect** (device): `class NameDeviceBody : IDeviceBody` (`double Width`,
  optional `bool AutoWidth => true` to size the card to its content,
  `Control Build(ctx, deviceIndex)`) in `DeviceCards/Bodies/`; register in
  `DeviceCardFactory`. The shared device shell (bypass/reorder/remove) wraps it —
  the body has NO instrument live-follow; use `ctx.AddDeviceRefresher(a)` for the
  60 Hz visual and read/write params with `engine.DeviceGetParam/DeviceSetParam` +
  `BeginAutomationWrite/EndAutomationWrite` (see `AutoFilterDeviceBody`/`VintageDeviceBody`).
- **MIDI effect**: `class NameMidiBody : IMidiDeviceBody` in `DeviceCards/Midi/`;
  register in `MidiDeviceCardFactory`. The racks live in `DeviceCards/Rack/RackCardView.cs`.

Everything a card needs comes through **`ctx`** (never reach into the view): `ctx.Engine`,
`ctx.TrackId`; `ctx.AddDeviceRefresher(a)` for a 60 Hz visual; `ctx.AddInstFader(i,knob,val)`
+ `ctx.SetInstLiveViz(refresh)` for instrument live-follow; `ctx.NotifyChanged()` /
`ctx.RequestRebuild()` / `ctx.RequestPresetSave(i)`. Stateless strategies are singletons —
all per-rebuild state stays behind `ctx`. Shared builders: `DeviceParamControls` (param
faders + sidechain), `InstrumentControls.InstKnob`, `SamplerEditor`.

Follow the mockup language:

- **A device card is a fixed rectangle**, height `CardH` (260, shared by all cards)
  — content taller than that is clipped (`ClipToBounds`). **Gotcha:
  `SimpleCard(title, width, body)` — the 2nd arg is WIDTH; height is always CardH.**
- **Use the working space deliberately — no dead space, nothing overflowing.**
  Fill the card's width with content and don't leave a half-empty card or let
  panels spill past the edge. Size each panel to its own controls, then make the
  card track that: either set `Width` to the sum of the panels, or (audio effects)
  set `AutoWidth => true` so the card sizes to content exactly — the safest way to
  guarantee "it all fits". Lay bordered **islands** side by side and give a large
  interactive graph its **own** framed island next to the knob panels (Nota Vintage:
  CHARACTER / WEAR / SHAPE). If the content genuinely can't fit 260px tall at a sane
  width, **split into tabs** (below) rather than shrinking every control. Always
  do the visual check and trim leftover margins.
- **If there's more than one logical section, use tabs** inside the card (e.g.
  Signal Path / Modulation) with a segmented toggle styled like the Devices/Clip
  segment, swapping a `ContentControl`. One section = one horizontal row that fits
  260px tall.
- **Lay panels as a signal chain**: bordered titled sections left→right with `▸`
  arrows (Osc → Noise → Filter → Amp). Each panel = `Border` (SurfaceCard fill,
  1px border, radius 7) with a bold section title.
- **Controls, in order of preference for space:**
  - The shared **gauge `Knob`** via `InstrumentControls.InstKnob(ctx, idx, id, name, refresh, size, cellW, arc)`
    — 270° groove + value arc + pointer. Pass a teal `arc` for **modulation-depth**
    knobs (env/lfo/key amounts, velocity, vibrato); amber (default) for everything
    else. Double-click resets to the param default.
  - **`KnobGrid(cols, …)`** packs knobs into centered rows (2×2, or vertical for
    1 col) to shrink a panel; a partial last row stays centred.
  - **Interactive graphs are controls, not decoration**: a drag ADSR editor
    (`VoltEnv`) and a drag filter response (`VoltFilter`, X=cutoff / Y=reso, curve
    follows the type, with grid + fill + freq/dB axes + cutoff handle). They replace
    ~a dozen knobs and should FILL their panel via a `DockPanel` (header docked
    top, graph stretches).
  - **Selectors:** waveform = icon chips (`WaveIcon`), filter type = text chips,
    small A/B or Fil1/Fil2 = segmented `Seg` toggles. Not combo-boxes unless a list
    is long.
  - Live values in `Font.Mono`; tiny UPPERCASE section/knob labels.
- **Live follow:** call `ctx.SetInstLiveViz(Refresh)` and register knobs via
  `ctx.AddInstFader(...)` (InstKnob does this for you); `RefreshSynthLive()` (driven by
  the playhead tick) re-reads params so automation playback moves the UI. Keep a
  `readouts` list of `Action` for numeric captions / graphs and call them in `Refresh`.

Colours/tokens: never hardcode — reuse the `DeviceCardKit` brushes (`Card2`, `BorderDef`,
`Brass`, `Teal`, `Sunken`, `TextPrimary/Secondary/Tertiary`, `AccentBright`), in scope via
`using static Nota.App.DeviceCardKit;` — along with `SimpleCard`, `KnobCell`, `ChipRow`,
`Labeled`, `Glyph`, `Pct`, `NoteName`. See `nota-design`.

## 4. Presets & verification

- **Factory presets:** instruments use `Inst("<group>", N, "Name", ("paramid", v), …)`;
  audio/MIDI effects use `Fx(…)` / `Midi(…)` with **param NAMES** (not ids) in
  `FactoryPresetCatalog.cs` (values normalized 0..1 for the newer devices; bipolar
  knobs neutral at 0.5). ~6 covering the useful range.
- **Smoke test** (`tests/Nota.SmokeTest/Program.cs`): assert kind/`builtinKind`,
  name, param count, param round-trip, clone (duplicate the track), and that each
  mode renders audible + finite (RMS>0). Instruments add a track + note; effects
  do `AddBuiltinDevice(track, kind)` over an audio clip and check `DeviceName`/
  `DeviceGetParam`. Run `dotnet run --project tests/Nota.SmokeTest`.
- **Visual check** (no click automation on macOS — see `nota-ui-verify`): add a
  temporary `NOTA_DEBUG_SESSION`-guarded block at the end of
  `MainWindow.OnDataContextChanged` (a `DispatcherTimer` firing ~2.5 s). For an
  instrument add its track; for an effect add an **audio** track +
  `Engine.AddBuiltinDevice(t, kind)` (an audio track has no instrument card, so the
  effect card is first and not pushed off-screen); then `ShowDevices(t)`. Launch
  with the env var, `screencapture`, crop with `sips`, then **revert the hook and
  rebuild.** To shoot a non-default tab, briefly flip the tab's default `Content`.

Build native (`cmake --build build --target nota_engine`) and managed
(`dotnet build src/managed/Nota.App`) after each layer; the C ABI is wildcard-
exported so a new `_nota_*` function needs no export-list edit.
