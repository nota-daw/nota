# Research: Mixer Groups and Compact Strips

Phase 0 of [plan.md](plan.md). Each entry: decision, rationale, alternatives.

## R1 — Strip order: the Arrangement's hierarchy, not the flat engine order

**Finding**: `MixerView.Refresh` walks `ArrangementView.DisplayOrder(engine)`, which is the
flat engine order with returns moved last (#374/#389). The Arrangement reorders its rows
with `ApplyHierarchy` (DFS over the `GroupId` forest: group row, then its children; orphans
become roots). So a group's members are only next to it in the Mixer when the engine order
happens to agree.

**Decision**: Add a static `ArrangementView.HierarchyOrder(IAudioEngine)` in
`ArrangementView.Groups.cs` that yields `(index, id, depth, parentGroupId)` in the same DFS
rule as `ApplyHierarchy`, ignoring collapse (the Mixer needs the hidden members too, to
count and flag them). `MixerView.Refresh` uses it for non-return tracks; returns and master
follow as today. `ApplyHierarchy` keeps its own walk over `TrackVM`s, but both share one
private child-map builder, so the two orders cannot drift.

**Alternatives**: reuse `ApplyHierarchy` output directly — it lives on view models of the
Arrangement instance and drops collapsed descendants; reordering in the engine — touches
the project model and undo for a view concern.

## R2 — One fold state for both views

**Finding**: the collapse set is `ArrangementView._collapsed` (`HashSet<int>` of group ids).
Its comment says "persisted per project", but `SetCollapsedGroups` has no caller: the state
lives for the session only and is not cleared on New/Open either.

**Decision**: Move the set into a small shared class `GroupFold` (new file
`src/managed/Nota.App/GroupFold.cs`): `IsFolded(id)`, `Toggle(id)`, `Set(ids)`, `Clear()`,
`event Action? Changed`. `MainWindow` owns one instance and hands it to `ArrangementView`
and `MixerView`. The Arrangement's triangle calls `Toggle`; both views refresh on `Changed`.
Persist it in the existing `track-view.json` sidecar (`ArrangementView.Sizing.cs`) as
`collapsedGroups: [trackIndex…]` — indices, like the heights, because ids last a session
only. The field is additive, so the sidecar stays `version: 1` and older builds ignore it.
`ResetTrackView` clears it (New project / before load).

**Alternatives**: a second set in `MixerView` synced by events — two sources of truth;
engine-side flag via the C ABI — a pure view concern would cross the boundary and land in
undo history.

## R3 — Group bands: an overlay over a flat strip row

**Decision**: Keep `_row` a flat horizontal `StackPanel` of strips (height fitting, meters and
sync code stay as they are). Wrap it in a `Panel` with a new `GroupBandsLayer` control
behind it (`src/managed/Nota.App/Controls/GroupBandsLayer.cs`). After layout the layer reads
each strip's bounds and, per group, draws:

- a bar row `BandBarH = 14` high at `depth × BandBarH` from the top, spanning the group strip
  through its last visible member, filled with the group colour at low alpha, the group name
  in 9 px semibold, ellipsised;
- a faint tint (group colour ≈ 6–8 % alpha) behind the same strips.

`_row` gets a top margin of `maxDepth × BandBarH`; the bar rows count in `CoreH`, so
`MinContentHeight` grows by them (spec edge case). Bars re-draw on `LayoutUpdated` of the
row, without rebuilding strips. Group colour = `ArrangementView.EffectiveColorIndex`, as the
strip's own top bar.

**Alternatives**: nested `StackPanel`s per group — strips would need per-depth spacers to
stay aligned and the flat lists (`_faderGrids`, `_ioSections`) would get harder to keep;
per-strip coloured borders — no place for the group's name, nesting unreadable.

## R4 — Folding hides strips, never rebuilds them

**Decision**: Every strip is built once per `Refresh`; folding toggles `IsVisible` on the
strips under a folded group (any folded ancestor hides). `UpdateMeters` and `FollowStrips`
skip invisible strips. The group strip's header gets a disclosure triangle (the Arrangement's
glyph) and, when folded, a `+N` count tag and small solo / arm / clip dots aggregated from
the hidden members on the UI tick (`TrackInfo.Soloed/Armed`, member meter peak ≥ 0 dBFS
since the last unfold or a click on the dot). Meets SC-002 (no rebuild, < 100 ms).

**Alternatives**: rebuild on toggle — simpler but loses scroll and costs ~1 ms per strip.

## R5 — Following the selected track

**Finding**: the Mixer ignores selection today. `Timeline.TrackSelected` and
`_modular.TrackActivated` already feed `MainWindow.OnTrackSelected`.

**Decision**: `MainWindow.Mixer.cs` subscribes to both events itself (not inside
`OnTrackSelected`, keeping the Mixer wiring in one file) and calls
`MixerView.FollowSelection(trackId)`: the strip — or, when hidden, its outermost folded
ancestor — gets the selected outline (`NotaPalette.Accent`, 1 px, the "chosen" accent from
`DESIGN.md`) and is brought into view (`BringIntoView`). Selection never unfolds.

## R6 — Compact strip: what goes where

Target width **72 px** (FR-018: ≤ 77). Top to bottom:

| Section | Compact form |
|---|---|
| Header | 3 px colour bar, name 9 px ellipsised + full name tooltip; group: triangle + `+N` |
| I/O | one 18 px line (`In 1 → Monitor in` shortened), full text in tooltip; instrument tracks keep the MIDI-in combo at 16 px |
| Sends | per return a thin `MiniFader` bar (6 px) with its letter, full width (clarified) |
| Pan | the existing `PanBar` (`Controls/PanBar.cs`, already in Arrangement track headers and Session): full-width centre-out bar; BAL tag beside on groups (clarified) |
| Level | new `MeterFader`: 14 px scale, two 18 px channels, an 11 px gutter with the triangular handle |
| dB | the same `DragNumber` |
| Buttons | M, S and Arm as full-width buttons stacked, 18 px each |

Height adaptation keeps the regular priorities (FR-017): `FitHeight` gets a layout-specific
set of section heights (`StripMetrics` record: header, I/O, sends, pan, buttons, fader
min/max/comfort), so the same algorithm runs for both layouts.

## R7 — `MeterFader`: meter and handle as one control

**Finding**: `MeterScale.Channels` never widens a channel past 11 px (DESIGN.md § Controls:
"Meter · 11 wide, 5 between"); a wider host only centres the pair.

**Decision**: A new control (`Controls/MeterFader.cs`) draws two 18 px channels 3 px apart
with `MeterScale.DrawChannel` (zones, hold mark and scale unchanged; only the width
differs), a hairline across them at the handle's height, and a 9 × 10 px
triangle pointing left at the volume's position on the **meter's dB scale**
(`MeterScale.NormDb(LinToDb(v))`), so 0 dB on the handle lines up with 0 dB on the meter.
Interaction follows `VFader` (relative drag, Shift/Ctrl/⌘ fine, `GestureBegin/End`,
`SetValueExternal`, `EntryField` for typed values / double-click reset) but in dB space over
the meter's height. The drag target is the whole control — a bigger target than the
handle. A click without movement still clears the peak hold, as `MeterBar` does today. MIDI
learn binds the same `MidiTarget.TrackVolume(id)`. The handle never covers the clip zone:
it sits outside the channels, in the right 10 px gutter.

**Alternatives**: overlay a `VFader` on a `MeterBar` — two controls competing for the same
pointer and peak-reset click; linear handle position — 0 dB on the handle would not line up
with 0 dB on the meter.

## R8 — The Compact toggle and its setting

**Decision**: `Settings.MixerCompact` (bool, default false) in `Nota.Application/Settings.cs`.
The footer gets a `Compact` toggle after `Sends` / `I/O`, painted as those (accent when on,
no auto-hidden state). Toggling rebuilds the strips (`Refresh`) — a layout change, rare,
and the only place where a rebuild is fine. The Stereo Map's strip column uses the same
layout; its `MaxWidth = 440` then shows six compact strips instead of two-and-a-bit.

## R9 — Design check before code (done 2026-11-02)

Mockup in [`mockups/`](mockups/): four scenes (regular unfolded / folded, compact 1440 px
with 24 tracks, compact with two groups folded) in Studio Dark and Studio Light, colours
read from the theme files by `build.py`; `shoot.sh` renders them (the PNGs are not committed —
the agreed set is on the `pr-assets` branch, `issue-386/mockup/`). Agreed with these outcomes:

- the hidden count sits in the group's bar (`Drums +3`), not as a header tag — a 72 px
  header has no room; hidden solo / arm / clip are 6 px dots at the right of the header;
- the compact meter's 18 px channels are a deliberate exception to DESIGN.md's 11 px
  meter: it is the issue's core ask (readable levels). It is recorded under DESIGN.md
  § Accepted exceptions in the same change;
- in compact, returns and master keep empty send / pan slots so all meters line up;
- 19 compact strips fit a 1440 px window against 9 regular ones (SC-003).

## R10 — Verification

No engine change, so the smoke test is unaffected but must stay green. Behaviour is checked
with a headless Avalonia harness (memory recipe: `new MixerView(engine)` in a `Window`,
engine tracks built through the API, synthetic pointer events on the controls,
`CaptureRenderedFrame` screenshots). SC-005 (folding never changes audio) is shown by
rendering the same project folded and unfolded offline and comparing the WAVs byte for byte.
