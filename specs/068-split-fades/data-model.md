# Data model: Fade audio clips when they are split

## ClipAdsr (existing)

| Field | Role in this feature |
|---|---|
| `attack` | Fade-in on the right side of a cut, in beats. Written only when it is shorter than the default and there is room beside `release`. |
| `decay` | Untouched. Counts against the room left for a new release. |
| `sustain` | Untouched. |
| `release` | Fade-out on the left side of a cut, in beats. Written only when it is shorter than the default and there is room beside `attack + decay`. |

Identity `(0, 0, 1, 0)` is a hard edge. The render multiplies by `gainAt`, which already ramps attack from 0 to 1 and release to 0 over the last `release` beats.

## Engine flag

`Engine::splitFades_`, default `true`. Not project data. The app pushes `Settings.SplitFades` at startup and when the switch changes. A missing JSON key stays true because the property initializer is true.

## Fade length

`splitFadeBeats() = 0.004 * authoringBpm_ / 60`, with 120 used when the tempo is not positive.

## Join

Two audio clips on the same track whose end and start differ by at most 1e-4 beats. The command fades that pair when either clip is in the selection. A gap, an overlap, or a MIDI clip is not a join.
