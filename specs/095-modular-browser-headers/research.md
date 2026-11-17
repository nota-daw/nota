# Research: Modular tab in the browser, arrangement track headers in Modular

Spec: [spec.md](spec.md) · Issue: nota-daw/nota#592

## R1 — How Modular gets "the same track headers as the arrangement"

**Decision**: Show the **same `ArrangementView` instance** (`MainWindow.Timeline`) in Modular,
switched into a *headers-only* mode, beside the Modular canvas. In Modular view `MainContent`
shows `Timeline` (headers-only, as wide as its header column, docked on the header side) and
`ModularView` (its canvas, inset by the column's width on that side). ModularView loses its own
"Tracks" rail.

**Rationale**:

- The header cards are ~2 000 lines spread over `ArrangementView` partials and lean on its
  private state (`_tracks`, `RowTop`, fold, row heights, tray slices, meters, selection,
  drag-reorder, menus, looks). Extracting them into a reusable control is a large refactor
  with a high regression risk for the arrangement itself.
- A second `ArrangementView` instance would need every MainWindow wiring duplicated (≈60
  events) and double the per-tick meter work.
- The same instance gives FR-009–FR-016 by construction: identical cards, every gesture and
  menu, live meters (`Timeline.UpdateMeters()` already runs every tick regardless of view),
  shared fold / row heights / header width / multi-selection, and the header side
  (`HeadersOnRight`) moving live — `ApplyHeadersRight` already sets it on the same object.
- Selection already routes to Modular: a header click raises the selection that ends in
  `MainWindow.OnTrackSelected`, which calls `_modular.Show(trackId)` when Modular is visible
  (`MainWindow.Detail.cs`). So FR-012 needs no new path.

**What headers-only mode hides**: the overview row, the sections row, the ruler and zoom bar,
the lanes and their overlay, the footer lanes and the horizontal scrollbar. It keeps the
header canvas (with automation / take sub-rows, since they are part of the shared row
geometry), the pinned return / master footer headers, the column backdrop and the column-width
seam. The top band shrinks to a header-width strip as tall as the Modular toolbar (34 px) with
the "Tracks" caption, and the bottom band to a strip as tall as the Modular status line (22 px),
so the column lines up with the canvas chrome.

**Consequences, handled**:

- Code that reads `Timeline.IsVisible` as "the arrangement is the shown view" (Copy to
  Session in `MainWindow.Menu.cs`, clip nudge arrows in `MainWindow.Input.cs`) switches to a
  new `ArrangementShown` (`Timeline.IsVisible && !Timeline.HeadersOnly`). Track-header keys
  (`_trackHeadersFocused && …`, ⌘A / ⌘C / Delete on tracks) keep working in Modular — that is
  wanted (FR-010).
- Vertical scroll is shared with the arrangement (one scroller). The spec's assumption of a
  per-view scroll position is revised: FR-016 (the column scrolls on its own and keeps its
  position while working on the canvas) still holds.
- The arrangement's automation / take sub-rows show in Modular's column as they do in the
  arrangement (they are header rows, and hiding them would change the shared row geometry).
  Spec assumption revised accordingly.

**Alternatives considered**: extract a `TrackHeaderColumn` control (rejected: size and risk,
see above); a second `ArrangementView` (rejected: wiring duplication, double work); keep the
rail and copy controls into it (rejected: not "the same headers", drifts immediately).

## R2 — Cross-track CV drop onto the track column

The old rail doubled as a drop target: a CV cable dragged from a modulator and released on
another track's row opens `ShowCrossTrackParamMenu` for that track
(`ModularView.FinishConnect`). Removing the rail must not lose it.

**Decision**: `ModularView` asks the host which track is under the release point:
`Func<Point, (int TrackId, Control Anchor)?> TrackAtPoint` (point in top-level coordinates),
answered by `ArrangementView.TrackAtHeaderPoint(Point)` (any track or return row; the master
and the shown track excluded). While a cable is being dragged, ModularView raises
`ConnectHintChanged(bool)` and the host lights the header column's divider in brass
(`ArrangementView.SetColumnHint`), as the rail's border did.

## R3 — Group graphs (US3)

**Finding**: in the engine a group is a regular `Track` in `Graph::tracks` with its own device
chain (`Engine.h` § track groups). `updateModulators` / `applyModulation`
(`Engine_Modulation.cpp`) iterate every `g->tracks` entry, and the modulator / CV-link calls
look tracks up with `findTrackAuthoring(trackId)` with no group exclusion. The Envelope
Follower reads the track's own post-fader meter, which groups have.

**Decision**: no engine change (FR-021 holds). `ModularView.Rebuild` already builds the
audio-track shape (input → effects → output) for any non-instrument track; groups take that
path with the source node labelled **Tracks in** (badge `GROUP`) instead of **Audio in /
CLIPS**. What changes in ModularView: groups are no longer filtered out of the Track view
(`Show` accepts them), Global view keeps skipping them (out of scope). Verified at
implementation with an LFO on a group's Auto Filter (quickstart Q4).

**Master**: `Show(masterId)` is ignored (the graph stays), so a click on the master footer row
selects it without touching the graph; Modular never patches the master (spec Edge Cases).

## R4 — The Modular browser tab

**Findings**: tabs are addressed by index (`TabCount = 9`; 0–4 are VM-driven trees, 5 Projects
is a flat `_sources` list filtered in `ApplyFilter`, 6–8 host custom views). The rail order is
the order of `_tabs` in the `tabStack`. The last tab is not persisted across restarts
(`SelectTab(0)` at start-up). Browser drags carry the item in the static
`BrowserView.CurrentDrag`; every drop target accepts via `BrowserView.IsAcceptableDrag(e)`.

**Decision**:

- New tab index `ModularTab = 9` (`TabCount = 10`), a flat `_sources` list like Projects;
  placed on the rail after MIDI effects (rail order ≠ index). New `GlyphKind.PatchCable`
  (16-unit grid, the rail's stroke) — distinct from `GlyphKind.Modular` (the view switch).
- Items come from `ModularCatalog.All` through a pure mapper
  `ModularBrowserItems.Build(IReadOnlyList<ModularNode>)` in `Nota.Presentation`:
  `Kind = BrowserItemKind.ModularNode`, `BuiltinKind = node.Kind`, `Sub = node.Sub` (category),
  `Tip = description`, `Keywords = aliases` (new `BrowserItem.Keywords`). Search
  (`ModularBrowserItems.Matches`) matches name, category and keywords, case-insensitive —
  `ApplyFilter` uses it for this tab. FR-002–FR-004.
- Visibility: `BrowserView.SetModularContext(bool)`; MainWindow calls it from `SetView`.
  Per-view tab memory is a small pure class `BrowserTabMemory` (Nota.Presentation): it keeps
  the last tab picked outside Modular and the last tab picked in Modular (default: Modular
  tab). Entering Modular → show Modular's remembered tab; leaving → the other one. Runtime
  only, like today's last tab (FR-007a, FR-007b).
- Drops: `IsAcceptableDrag` refuses a `ModularNode` item (so the arrangement, session, device
  panel, mixer and the header column refuse it — FR-007); ModularView accepts it only in Track
  view with a track shown, and adds it via `AddModulatorAt(kind, viewportPoint)` (a new
  overload; the palette's pointer/centre placement stays). Double-click / Enter route through
  `ItemActivated` → MainWindow → `_modular.AddModulatorAt(kind)` (centre). Global view / no
  track → status line reason, nothing added. One engine call = one undo step, as the palette.

## R5 — Show in Modular

**Findings**: track context menus exist in the arrangement (`BuildTrackMenu` for tracks and
groups, `ShowFooterMenu` for returns and the master) and the session (`ShowHeaderMenu`). The
mixer has **no** track context menu — FR-022 applies to the menus that exist.

**Decision**: `ArrangementView` and `SessionView` raise `ShowInModularRequested(int trackId)`;
the item sits in the naming / inputs group, not offered for the master, and disabled in
headers-only mode for the track Modular already shows. MainWindow handles it:
`SetView(Modular)`, select the track, `_modular.ShowTrack(trackId)` (leaves Global view if
needed). Not an undo step (FR-023).

## R6 — Removing + Add

The toolbar's **+ Add** flyout goes (FR-019). The Track/Global segment and the zoom controls
stay. The palette keeps its Modular entries (`PaletteKind.Modular`). The private
`AddModulator(int)` becomes unused and is removed.

## R7 — Tests

`tests/Nota.SmokeTest` gets pure tests (no UI): `ModularBrowserItems` (every catalogue node
mapped, name / category / alias search) and `BrowserTabMemory` (first entry → Modular tab,
memory per context, leaving returns the previous tab). The view wiring is verified with the
headless screenshot harness and by hand (quickstart).
