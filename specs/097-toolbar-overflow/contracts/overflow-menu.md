# Contract: the overflow menu

Opened by the ⋯ button at the right end of the transport bar. Built at open time.

## Section 1 — shed controls (only when at least one is shed), in bar order

| Part | Entry | Kind | Does |
|------|-------|------|------|
| Loop | Loop · *range* | check | `Transport.LoopOn` |
| LoopRecord | Loop record: Replace | check | `Transport.LoopRecordReplace` |
| Tap | Tap tempo · *bpm* | stays open | `TapTempoBeat()` |
| TimeSignature | Time signature ▸ | radio submenu | numerator / denominator |
| Key | Key… · *current key* | item | opens the key picker under the ⋯ button |
| Grid | Grid ▸ | submenu | the Grid chip's own items |
| Follow | Follow playhead | check | `SetFollowPlayhead` |
| Snap | Snap to grid | check (⌘4) | same as the magnet |
| Automation | Automation | check | `Timeline.AutomationMode`; + "Re-enable automation" when the chip shows |
| Mixer | Mixer | check (⌘⇧M) | `view.mixer` |
| MidiLearn | MIDI learn | check | `ToggleMidiLearn()` |
| Remote | Connect Phone… · *count* | item | `ShowConnectPhone()` |
| DspMeter | DSP *n* % | disabled, live | — |
| Master | Master · fader | stays open | master volume, one undo step per drag |

A separator follows the section.

## Section 2 — quick functions (always)

| Entry | Kind | Does |
|-------|------|------|
| Interface scale ▸ 75 / 100 / 125 / 150 / 200 % | radio submenu | `InterfaceScale` setting, saved, applied live |

## Behaviour

- Closes on: choosing an item (except Tap and Master), Escape, a second click of ⋯, a click
  outside (which does nothing else).
- Keyboard: ⋯ is in the tab order; Space / Return opens with focus on the first item.
- Shortcuts bound to an entry's action are shown beside it (`MenuKit`).
