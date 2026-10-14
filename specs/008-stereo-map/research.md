# Research: Stereo Map (MVP)

## R1. What is measured, and where

**Decision**: the engine already accumulates, per segment, `ΣL²` and `ΣR²` of every track's
and return's post-fader, post-pan signal (the meter pass in `Engine_Render.cpp`) and of the
master (after master volume). Add one more sum, `ΣL·R`, in the same three loops — one
multiply-add per sample, no extra pass (FR-007). Per segment, fold the three block means
into exponential averages on the track (`Track`: three `std::atomic<float>`) and on the
engine for the master:

```text
a = 1 − exp(−frames / (τ · sampleRate)),  τ = 0.3 s
eL += a (ΣL²/n − eL);  eR += a (ΣR²/n − eR);  eLR += a (ΣLR/n − eLR)
```

A muted / solo-gated track feeds zeros, so its averages decay (FR-010). Groups are not
measured (not drawn). Torn reads across the three atomics are harmless for a display.

**Rationale**: three running energies are all the map needs — every displayed value is
derived from them on the message thread (R2), so the audio-thread cost is the minimum.
τ = 0.3 s matches the requested ~300 ms window and reacts within SC-005 (63 % in 0.3 s,
95 % in 0.9 s; a cloud appears/disappears within 0.5 s of a change at its threshold).

**Alternatives**: ring buffers of samples per track for the UI to analyse — memory per track
and a copy per block; needed only for V2's spectrum. Rejected for the MVP.

## R2. The displayed values

Computed in the C ABI getter from `eL`, `eR`, `eLR` (message thread):

| Value | Formula | Why |
|---|---|---|
| **level** (dBFS) | `10·log10((eL + eR) / 2)` | RMS of the pair |
| **silent** | level < −60 dBFS | FR-006 |
| **balance** (−1…+1, on the pan scale) | `(4/π) · atan2(√eR, √eL) − 1` | the pan that would put a mono source at this energy balance — so a mono track panned L50 measures exactly −0.5 and its cloud sits on its handle |
| **correlation** (−1…+1) | `eLR / √(eL · eR)` (0 if either side is silent) | |
| **width** (0…1) | `clamp(1 − correlation, 0, 1)` | mono (panned or not) → 0; unrelated channels → 1; anti-phase → 1 (and a red outline) |

**Decisions that differ from the feature description** (recorded for review):

- *Balance as `(R²−L²)/(L²+R²)`* (the description's formula) puts a mono source panned L50
  at −0.71 under the equal-power law, i.e. its cloud would sit visibly left of its own handle
  and trigger the "Image is off the pan" note for every panned mono track. The inverse-pan
  form keeps the same information (energy left vs right) on the X axis's own scale.
- *Width as `S/(M+S)`* gives 0.5 for unrelated left/right material (not "nearly the full
  width", US1 scenario 2 / FR-021) and a non-zero width for a panned mono source (a point
  source drawn as a smear). `1 − correlation` is 0 for any mono source, wherever it is
  panned, and 1 for unrelated or anti-phase material — what the cloud is meant to show.

## R3. Y axis = the Mixer fader's scale

**Decision**: Y is the Mixer `VFader`'s mapping — **linear in gain**, 0 … 1.5 (≈ +3.5 dB) —
so a handle and its strip fader always sit at the same height (spec FR-003, assumption).
Grid lines are drawn at the linear positions of +3.5 (top), 0, −6, −12, −24 dB.

**Consequence (flag for the user)**: on a linear-gain scale 0 dB sits at 67 % of the height
and −24 dB at 4 %, so quiet tracks bunch near the bottom; the mock's dB-curved axis spreads
them out. Matching the fader was the spec's choice; a dB taper for *both* the Mixer fader and
the map is a separate, small change worth considering later.

## R4. Rendering at 60 fps

**Decision**: `StereoMapView` is one custom-drawn control (`Render` override: grid, clouds,
handles, labels, tooltip, master correlation bar) — no control per track. While it is visible
it requests animation frames (`TopLevel.RequestAnimationFrame`), and each frame pulls all
measurements in one C ABI call and the track list from the engine. The app's 33 ms UI tick
is too slow for SC-003.

**Rationale**: 128 tracks × (ellipse + circle + text) per frame is a few hundred draw calls —
cheap for Skia; one native call per frame instead of 128.

## R5. Mixer window: Strips | Stereo Map

**Decision**: `MixerWindow` switches to its own 36 px title bar (as `DeviceWindow` does):
"Mixer" left, the shell segmented control **Strips | Stereo Map** centred (FR-001). On
Linux (native frame) the switch sits at the top of the body. `MixerView` gets a `View`
property: in **Stereo Map** the map fills the left (`*`) and the strips scroll in a right
column capped at 440 px (FR-002); the footer stays. The choice is a user preference,
`Settings.MixerView` (`"strips"` default | `"map"`), remembered across openings.

## R6. Interaction

- Drag a handle: one `MixGesture(engine, Volume(id), Pan(id))` (spec 009) — pan from the
  X delta (`2 / plotWidth` per px), volume from the Y delta on the fader scale, clamped;
  Shift locks to the axis with the larger movement so far (FR-014); one undo step (FR-017).
- Automation: the drag opens the volume and pan automation writes like the strip controls
  (`BeginAutomationWrite` after the mix gesture opens, `EndAutomationWrite` before it ends),
  so recording and read/override rules are the same (FR-016).
- Double-click → pan C; ⌥ + double-click → volume 1.0; each a one-shot step (FR-015).
- The master handle is fixed (no drag, no reset).
- Click → select the track app-wide: a new `ArrangementView.SelectTrackFromOutside(id)`
  (selects and raises `TrackSelected`); the map draws the ring from `Timeline.SelectedTrackId`
  each frame, so selections made elsewhere follow (FR-018).

## R7. Labels, order, tooltip

Draw order: by level ascending, selected last (on top). Labels: greedy — try each handle's
label rect (right of the dot, ≤ 84 px, truncated) from the top of the draw order down; skip
any that overlaps an already placed one (FR-011). Tooltip after 300 ms hover, not while
dragging; rows and notes per FR-019; "Image is off the pan" when `|balance − pan| > 0.15`.

## R8. Tests

- **Smoke** (`StereoMeasureTests`): WAV clips (deterministic): mono 440 Hz panned L50 under
  Stereo → balance −0.50 ± 0.05, width < 0.05; unrelated L/R tones at C → width > 0.95,
  |correlation| < 0.05; L = −R → correlation < −0.95; silence → level < −60; master
  correlation of the unrelated pair ≈ 0; measuring cost: render time with 64 tracks within
  1 % of the same render with measurement disabled via a test-only switch, or (if a switch
  is not worth adding) within the run-to-run noise of two plain renders (SC-004).
- **SC-004, measured (2026-10-14, implementation)**: no test-only switch was added to the
  engine, so the cost was measured once as an A/B of two engine builds — the committed tree
  before the Stereo Map sums (built in a temporary `git worktree`, `vendor/` linked from the main
  checkout) against the tree with them — on the same render: 64 stereo audio tracks (unrelated
  440 Hz / 1 kHz WAV), Stereo law, 8 s in 512-frame blocks at 48 kHz, best of 6 runs, two rounds:
  **169.2 / 170.1 µs per block with the sums vs 171.0 / 171.8 µs without** — a difference inside
  the run-to-run noise, far below 1 % of the 10.7 ms block budget (SC-004). To repeat: build the
  engine at the two commits, run the same render through `NotaEngine.RenderOffline` with each
  `libnota_engine.dylib`, compare the best times.
- **Headless UI** (harness): handles at the right positions, drag = one undo step moving pan
  and volume, Shift lock, resets, master fixed, selection both ways, labels never overlap,
  screenshots in both themes vs the mock.
