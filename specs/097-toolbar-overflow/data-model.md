# Data model: Toolbar overflow

All in memory, no persistence beyond the existing `InterfaceScale` setting.

## ToolbarPart (enum, `Nota.Presentation`)

The transport-bar parts that can shed: `CpuBar`, `RemoteLabel`, `Tap`, `Key`, `Remote`,
`Follow`, `Automation`, `Mixer`, `TimeSignature`, `MidiLearn`, `Grid`, `Snap`, `LoopRecord`, `Loop`,
`Master`, `DspMeter`.

## ToolbarOverflow (static, `Nota.Presentation`)

| Member | Meaning |
|--------|---------|
| `ShedOrder : IReadOnlyList<ToolbarPart>` | today's #303 order, one part per step |
| `BarOrder : IReadOnlyList<ToolbarPart>` | left-to-right position in the bar: Loop, LoopRecord, Tap, TimeSignature, Key, Grid, Follow, Snap, Automation, Mixer, MidiLearn, Remote, DspMeter, Master (+ info-only parts) |
| `IsInfoOnly(part)` | true for `CpuBar`, `RemoteLabel` |
| `Entries(int shedSteps) : IReadOnlyList<ToolbarPart>` | the parts in the first `shedSteps` steps, minus info-only ones, sorted by `BarOrder` |

Rules:
- `shedSteps` is clamped to `0 … ShedOrder.Count`.
- `Entries` never contains duplicates; `Entries(n)` ⊆ `Entries(n + 1)`.
- `Entries(0)` is empty (the menu then holds only the quick functions).

## Shed state (App, `MainWindow`)

- `_shedSteps : int` — set by `FitTransport`; drives the button's cue and the menu.
- The App maps each `ToolbarPart` to the controls it hides (`TimeSignature` → `TimeSigCell`,
  `MidiLearn` → `RemotePanel`, `Loop` → `LoopSep` + `LoopCell`, `DspMeter` → `CpuMeter` + `CpuSep`, …).
