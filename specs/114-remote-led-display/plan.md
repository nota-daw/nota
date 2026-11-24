# Implementation Plan: Remote Macros as a controller surface with an LED display

**Branch**: `feat/remote-led-display` | **Date**: 2026-11-24 | **Spec**: [spec.md](spec.md)

## Summary

The phone's Macros screen walks the track's devices in chain order and each device's controller
pages (the same pages the hardware's knobs walk, specs/051), with Track / Device / Page buttons
that follow the controller focus (specs/107) both ways. Above the knobs a 128 × 48 × 16-level dot
matrix shows a picture of the page. Nota renders the matrix in shared C# (`Nota.Application.Displays`,
no Avalonia): a `LedFrame` with drawing primitives, a library of display widgets (filter curve,
envelope, EQ, transfer curve, tail, taps, sweep, keyboard, bars …) and a registry that gives every
page of every built-in device its picture. `RemoteHub` renders one frame per watched device page on
its UI tick, RLE-encodes it and sends a `led` message only when it changed; the web Macros screen
draws the dots.

## Nota: Read Before Planning

- No engine change: every input already crosses the C ABI (params, `InstrumentOutput`,
  `DeviceScope`, `DeviceGainReduction`, `TryGetTrackMeter`, `MidiEffectLastIn/Out`).
- UI: the Remote web app follows `DESIGN.md` via its theme tokens (specs/090).

## Technical Context

**Language/Version**: C# / .NET 10 (Nota.Application, Nota.Remote, Nota.App), TypeScript + Svelte 5 (`src/web/remote`)

**Primary Dependencies**: none new

**Storage**: N/A (pages come from the existing `DevicePagesStore`)

**Audio-Thread Impact**: None. Frames render on the UI tick that already drives `RemoteHub.Tick`; the
only engine reads are the existing lock-free telemetry calls the device cards make. Reading
`InstrumentOutput` keeps the instrument's output tap on (as the desktop filter graph does).

**Testing**: `tests/Nota.Tests` unit tests for `LedFrame` (primitives, RLE round trip), the registry
(every catalog type key and page has a display; rendering each on a fresh device does not throw and
lights dots), and the hub's `led` / `nav` handling; the smoke test renders every built-in device's
pages once through the C ABI; the web app's `npm run check` / build.

**Target Platform**: Nota desktop (macOS, Windows, Linux) + the Remote web app on phones

**Performance Goals**: ≤ 2 ms per rendered frame on the UI thread; ≤ 30 frames/s per watched page;
idle pages send nothing; a busy page ≤ 100 KB/s per phone.

**Scale/Scope**: 50 built-in devices (Nota Color joined from develop), 96 pages.

## Constitution Check

- 0 Approved issue: **deviation** — the maintainer asked to open and approve it with the PR (spec header).
- I Real-time safety: pass (no audio-thread work).
- II Boundary: pass (no C ABI change).
- III Licensing: pass (new files carry SPDX headers, no dependency).
- IV Verification: smoke test extended to render every display.
- V One change: one feature (Remote Macros surface + display); MCP and hardware screens are later.
- VI Docs: CHANGELOG, FEATURES.md, Remote user doc.
- VII Spec-first: this spec / plan / tasks.

## Design

### Shared rendering — `src/managed/Nota.Application/Displays/`

- `LedFrame` — `W = 128`, `H = 48`, `byte[] Dots` (0–15). Primitives: `Dot` (max-blend), `Put`
  (overwrite), `Line` (Bresenham, optional dotted), `HLine` / `VLine`, `Rect`, `Curve` (a y(x)
  function joined column to column with an optional fill below), `Text` (3 × 5 font, upper case,
  returns width), `TextWidth`, `Clear`. `Rle4()` encodes runs as bytes `(n−1) << 4 | level`;
  `FromRle4` decodes (tests, MCP later). Equality by content for the hub's "changed?" check.
- `DisplayParams` — the device's parameters by name for one `DeviceAddress` (track-level
  instrument, audio effect, MIDI effect): `N(name)` normalized 0..1, `Raw(name)`, `Text(name)`,
  and unit helpers that work for both kinds of parameter — `Hz`, `Sec`, `Db`, `Frac`, `Int` —
  reading the engine's own value text for plug-in-style (instrument) params and min/max/raw for
  built-in effect and MIDI-effect params. Unknown names return the given fallback, so a display
  never throws on a renamed parameter.
- `DisplayContext` — engine, address, track id, params, time (s), the page's eight knob names and
  normalized values, the knob being turned (index or −1), and live taps: `Spectrum()` (the
  instrument output through a small FFT into 64 log bands, smoothed), `Scope(n)` (the device's
  `DeviceScope` floats), `GainReduction()`, `TrackLevel()` (dB peak), `MidiLast()`.
- `Widgets` — static drawing routines in a fixed graph area (y 9–47 under the header):
  `FilterCurve`, `Adsr` (one or two envelopes, playhead), `Waves` (oscillators), `Lfos`, `EqCurve`
  (peaks / shelves summed), `Transfer` (compressor / limiter / gate / saturation curves with a
  live input dot), `Tail` (reverb decay in dB), `Taps` (delay echoes with feedback decay),
  `Sweep` (modulated delay / phaser notches / tremolo over time), `Keyboard` (a one-octave strip
  with lit keys), `Steps` (an arp / repeat pattern), `Grid`, `Bars`, `Meter`, `Spectrum`, `Xy`.
- `DeviceDisplays` — the registry: type key → page name → `Action<DisplayContext, LedFrame>`;
  pages not listed (and racks, plug-ins, unknown kinds) fall back to `Bars`. Split by family into
  `DeviceDisplays.Instruments.cs`, `.Effects.cs`, `.Midi.cs`.
- `DisplayRenderer.Render(ctx, frame, overlay)` — clears, draws the page, then the header row:
  "DEVICE PAGE" left and a short readout right, or the overlay (inverted bar: knob name + value)
  while a knob turns.

### Remote — `src/managed/Nota.Remote/`

- `RemoteMacros` (new) — the phone's device list for a track in chain order (MIDI effects,
  instrument, audio effects; one entry per device), each with its controller pages
  (`FocusedDevice.Build` with the host's edited pages) mapped to `RemoteParam`s (new kind
  `MidiParam` for MIDI effects). Replaces `RemoteParams.MacroDevices` in the hub; Twin and racks
  come through the catalog pages like everything else.
- `RemoteClient` gains `MacroPage`, `MacroAddress`, `SentLed` (last frame bytes), `LedSeq`,
  `TurnKnob` / `TurnUntil` (the overlay).
- `mac` message gains `pg` (page), `pn` (page names), `dk` (device kinds / names list unchanged as
  `devs`), and per knob what it had; `led` is new; phone → Nota `nav` is new
  ([contracts/remote-led.md](contracts/remote-led.md)).
- `RemoteHub.Led` (new partial) — on each tick for phones on Macros: group them by (address,
  page, overlay), render once per group into a `LedFrame`, encode, send to each phone whose last
  frame differs. Rendering runs at the hub's ~30 Hz tick; a frame that equals the last one sent is
  not sent.
- Navigation: `nav` applies on the phone's own state; with Follow on it also asks the host
  (`IRemoteHost.StepTrack`, `StepDevice`, `SetDevicePage`, default no-ops) so Nota's selection and
  the controller follow; the existing `foc` follow (specs/107) moves the phone back when the
  controller or Nota moves. `RemoteControllerFocus` gains `Address` (the focused `DeviceAddress`)
  so a MIDI effect focus maps to the phone too.

### App — `src/managed/Nota.App/`

- `MainWindow.Remote.cs` implements the new host members: step the selected track as the
  controller's Track ◀ ▶ does (`ControllerService.StepSelectedTrack`), step the device
  (`ControllerService.StepDevice`), set the lead controller's page (`ControllerService.SetPage`),
  and hand the `DevicePagesStore` as the edited pages.

### Web — `src/web/remote/`

- `screens/Macros.svelte` — header row of Track ◀ ▶ and Device ◀ ▶ steppers, the LED display, a
  row of page buttons (scrolls when long), the eight knobs (4 × 2 on phones). The device sheet
  stays for a direct pick.
- `components/Led.svelte` — decodes RLE4 and paints round dots on a canvas (unlit dots faint,
  lit by level, a soft glow), in the track colour; keeps the last frame dimmed while
  disconnected.
- `lib/types.ts`, `lib/app.svelte.ts` — the `led` message and `nav` sends; `mock/nota-mock.mjs`
  serves frames so the screen works in the mock.

## Project Structure

```text
specs/114-remote-led-display/
├── spec.md, plan.md, research.md, data-model.md, quickstart.md, tasks.md
└── contracts/remote-led.md

src/managed/Nota.Application/Displays/
├── LedFrame.cs
├── DisplayParams.cs
├── DisplayContext.cs
├── DisplaySpectrum.cs
├── Widgets.cs
├── DisplayRenderer.cs
├── DeviceDisplays.cs
├── DeviceDisplays.Instruments.cs
├── DeviceDisplays.Effects.cs
└── DeviceDisplays.Midi.cs

src/managed/Nota.Remote/
├── RemoteMacros.cs            (new)
├── RemoteHub.Led.cs           (new)
├── RemoteHub.State.cs, RemoteHub.Apply.cs, RemoteClient.cs, RemoteContracts.cs, RemoteParams.cs

src/managed/Nota.App/MainWindow.Remote.cs, Controllers/ControllerService.cs

src/web/remote/src/screens/Macros.svelte, components/Led.svelte, lib/types.ts, lib/app.svelte.ts
src/web/remote/mock/nota-mock.mjs

tests/Nota.Tests/Displays/*, tests/Nota.SmokeTest (display pass)
```
