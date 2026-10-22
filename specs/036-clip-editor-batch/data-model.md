# Data model: Clip editor batch

No persisted data changes: no project-file, preset or settings field is added. The
entities below are in-memory value types in `src/managed/Nota.App`, pure (no Avalonia, no
engine) so the smoke test can exercise them as linked source, like `ValueText`.

## ClipTimeMap — editor position ↔ arrangement position

Built by the audio clip editor on every `Reload` from the clip's geometry.

| Field | Meaning |
|---|---|
| `StartBeat` | the clip's arrangement start (`NotaClipInfo.start_beat`) |
| `LengthBeats` | the clip's arrangement length |
| `Domain` | `WarpBeats` (warped) or `SourceFrames` (unwarped) |
| `ContentTotal` | `warp_beats` (warped) or the sample's total frames (unwarped) |
| `PlayStart`, `PlayEnd` | played window in the domain: trim brackets (warped) or `[offset, offset + length]` frames (unwarped) |
| `Reversed` | the region plays back-to-front |
| `IsSlot` | a Session-slot clip — no arrangement position |

Operations (all total; NaN-free for `ContentTotal > 0`):

- `PlayFracAtContent(c)` — content frac `c ∈ [0,1]` → played frac; `(c·Total − PlayStart) /
  (PlayEnd − PlayStart)`, then `1 − f` when reversed. Not clamped (seeks may fall outside).
- `ContentAtPlayFrac(f)` — the inverse; used to draw the playhead (replaces the inline
  formula in `Render`).
- `ArrangementBeatAtContent(c) = StartBeat + PlayFracAtContent(c) × LengthBeats`.
- `ContentAtArrangementBeat(b)` — the inverse; places ruler ticks at arrangement steps.

Rules: `IsSlot` → the editor uses clip-relative labels and no seeking; the map still serves
the playhead (the slot position is clip-relative already).

## ClipTempo — the clip's warp tempo

| Input | Meaning |
|---|---|
| markers `(src, beat)[]` | warp markers, sorted by `src`, ≥ 2 |
| `PlayStart`, `PlayEnd` | trim window in warp beats (`PlayEnd = 0` → `warp_beats`) |
| `SampleRate` | source sample rate |

- `SrcAt(beat)` — piecewise-linear source frame at a warp beat (extrapolates past the ends
  with the end segments' slope).
- `Bpm = (PlayEnd − PlayStart) × SampleRate × 60 / (SrcAt(PlayEnd) − SrcAt(PlayStart))`.
- `Scale(newBpm)` → `(markers', PlayStart', PlayEnd')` with every beat × `k = newBpm / Bpm`;
  `PlayEnd = 0` stays 0.
- Valid range 20–999 BPM; `Half()` / `Double()` return null if the result leaves it.

Unwarped clip: `Bpm` is the last detected tempo or null ("—"). With null, typing still sets a
tempo; ×2 / ÷2 are disabled.

## WarpRemap — live marker-drag preview

| Input | Meaning |
|---|---|
| `committed` `(src, beat)[]` | markers as the engine laid out the current peaks |
| `live` `(src, beat)[]` | markers during the drag (same count, same sources) |

- `OldBeatAt(newBeat) = BeatAt(committed, SrcAt(live, newBeat))` — piecewise linear through
  both lists; monotone because both lists are.
- Identity when `live == committed` (no visual change before the pointer moves).

State: `Idle` → (press on marker) `Dragging{committed snapshot}` → release ⇒ commit ⇒ `Idle`;
Esc / capture lost ⇒ restore `live = committed` ⇒ `Idle`.

## RulerLabels — shared ruler text

- Input: arrangement beat, label step (beats or seconds), mode `Bars | Time`, beats per bar,
  BPM.
- Bars: `N` on a bar, `N.b` on a beat, `N.b.s` on a 16th (1-based, like the existing
  labels).
- Time: seconds = `beat × 60 / BPM`; `m:ss` for steps ≥ 1 s, `m:ss.f`, `m:ss.ff`, `m:ss.fff`
  as the step shrinks. Negative positions never occur (clamped at 0 before formatting).

## RulerDisplay — the shared mode

A static observable in `Nota.App`: `bool ShowTime`, `event Action Changed`. Set from the
transport view model's `ShowTimeDisplay`; read by the arrangement ruler, the piano roll
ruler and the audio editor ruler.
