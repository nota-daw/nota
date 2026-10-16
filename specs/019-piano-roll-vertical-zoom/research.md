# Research: Piano roll vertical zoom

Findings from reading the current piano roll (`src/managed/Nota.App/PianoRollView.cs`,
`ClipEditorView.cs`, `MainWindow.Input.cs`, `MainWindow.Commands.cs`,
`Nota.Application/Palette/CommandCatalog.cs`, `PreferencesWindow.cs`) and the Avalonia
12.0.3 API.

## R1 — Where the row height lives

- **Decision**: Replace `private const double RowH = 12` with an instance property
  `RowH` backed by `_rowH`, plus `MinRowH = 6`, `DefaultRowH = 12`, `MaxRowH = 36`.
  `RangeH`, `PitchToY` and `YToPitch` (static today) become instance members; the nested
  controls (`KeysColumn`, `NoteGrid`) read `_o.RowH`.
- **Rationale**: Every row-dependent draw and hit-test already goes through `RowH`
  (~40 uses, all in `PianoRollView.cs`; `PianoRollView.Expression.cs` uses none), so one
  source of truth covers FR-008 without touching the drawing logic.
- **Alternatives considered**: A `ScaleTransform` on the body — rejected: it would scale
  note text, pens and the keys column width too, and hit-testing would still be in
  unscaled rows.

## R2 — Anchored zoom math

- **Decision**: One method `ZoomRowsAt(double factor, double viewportY)`. With
  `off` = current vertical offset and `h0`/`h1` the old/new row heights, the content
  position under the pointer is `c = off + viewportY`; the new offset is
  `c * h1 / h0 - viewportY`, clamped to `[0, max(0, 88·h1 − viewportHeight)]`. The new
  `Height` is set on `_keys` / `_gridControl`, and the offset goes through the existing
  `_pendingVOffset` → `ApplyPendingVOffset` path, because the ScrollViewer's extent only
  grows after the next layout pass.
- **Rationale**: The same anchor formula `ZoomAt` uses horizontally; reusing the pending
  offset path avoids a clamp against the stale extent (which would make zoom-in near the
  bottom jump).
- **Alternatives considered**: Setting `Offset` synchronously after `UpdateLayout()` —
  works but forces a layout per wheel event; the pending path is already there.

## R3 — Wheel gesture

- **Decision**: In `NoteGrid.OnPointerWheelChanged`, test **(⌘ or Ctrl) and ⌥** first →
  `ZoomRowsAt(WheelInput.ZoomFactor(e.Delta.Y, 1.2), y-in-viewport)`; the existing
  branches (⌘/Ctrl = horizontal zoom, Shift, horizontal swipe) follow unchanged. Add the
  same ⌘⌥ branch to a new `KeysColumn.OnPointerWheelChanged` (plain wheel there stays with
  the ScrollViewer).
- **Rationale**: FR-002/FR-003. Today ⌘⌥+wheel falls into the ⌘ branch (horizontal zoom),
  so checking ⌥ first is the only change to existing meaning — a combination nobody
  relies on.
- **Note**: `ClipEditorView`'s `RouteWheel` (ruler / lane rows) keeps horizontal only —
  vertical zoom is about rows, which those rows don't show.

## R4 — Keys-column drag

- **Decision**: `KeysColumn` gets `OnPointerPressed` (left button → capture, remember the
  press Y in `_bodyScroll` viewport coordinates and the row height at press),
  `OnPointerMoved` while captured (`h = clamp(h0 · exp(dy · 0.01))`, applied via a
  `SetRowHeightAt(h, pressViewportY)` sibling of R2), `OnPointerReleased` /
  `OnPointerCaptureLost` end it. Down (positive dy) = taller. X is ignored.
- **Rationale**: The keys column has no press handling today (only hover), so there is no
  conflict and nothing auditions (FR-004). Exponential mapping makes equal drags equal
  zoom ratios; 0.01/px gives ~2.7× per 100 px, which covers 6→36 px in ~180 px.
- **Alternatives considered**: Linear px→px mapping — feels sluggish at small heights and
  jumpy at large ones.

## R5 — Trackpad pinch

- **Decision**: `AddHandler(InputElement.PointerTouchPadGestureMagnifyEvent, …)` on the
  note grid; when ⌥ is held, `ZoomRowsAt(1 + e.Delta.X, y)` and mark handled.
- **Rationale**: Avalonia 12 exposes `PointerTouchPadGestureMagnify`
  (`PointerDeltaEventArgs`, "two contact points moving apart on a touchpad"); it is raised
  by the macOS backend. On platforms that never raise it nothing changes (US4 scenario 2).
- **Alternatives considered**: `Gestures.PinchEvent` — that is the touchscreen pinch
  recogniser, not the touchpad.

## R6 — Zoom during a note gesture

- **Decision**: Ignore vertical-zoom input while `NoteGrid._drag != Mode.None` (move,
  resize, marquee in progress).
- **Rationale**: Simplest way to guarantee SC-004; the drag state stores grab offsets in
  pixels, and re-deriving them mid-gesture is risk with no user value.

## R7 — Fit to notes and ⇧F

- **Decision**: `PianoRollView.FitToNotes()`: empty clip → `DefaultRowH` and the existing
  `CenterOffsetForNotes()` C5 fallback; otherwise `span = maxP − minP + 1 + 2` (one margin
  row each side), `h = clamp(viewportHeight / span, MinRowH, MaxRowH)`, then centre on the
  notes (the existing centring helper, which now uses the instance `RowH`). Uses all notes
  regardless of selection.
- **Shortcut**: handle `Key.F` with exactly Shift in `NoteGrid.OnKeyDown`, set `Handled`.
  Because the grid is the focused source, the event never reaches `MainWindow.OnKeyDown`
  (a class handler that skips handled events), so the note map's Shift-agnostic `F`
  (`mod` excludes Shift) does not sound a note. Outside the grid, ⇧F is untouched.
- **Palette**: `C("roll.fitNotes", "Fit to notes", "Piano roll", "⇧F", "zoom vertical rows
  height")` in `CommandCatalog`, bound with the existing `Roll` availability in
  `MainWindow.Commands`.
- **Header**: a "Fit" chip (the header's existing `chip` style, like "Tools") in
  `ClipEditorView`'s right header group, before the snap chip, tooltip "Fit to notes (⇧F)" —
  per DESIGN.md, neutral, not accent.

## R8 — Persistence

- **Decision**: Add `RowH` to the per-clip `ViewState` record; `SaveView` writes it,
  `SetNotes` restores it (clamped); a clip without saved state gets `DefaultRowH`
  (clarification 1 and 2). Not undoable, not saved to the project (FR-009/FR-010).
- **Rationale**: Same scope and lifetime as horizontal zoom and scroll today.

## R9 — Key labels at small/large rows

- **Decision**: Keep labelling only C rows (plus the hovered/held key, as today). The
  label is centred on its row and may overflow a short row; with C rows ≥ 72 px apart at
  the 6 px minimum, labels never collide.
- **Rationale**: FR-011 is already met by the sparse labelling; no thinning logic needed.

## R10 — Verification

- **Decision**: No managed unit-test project exists; the smoke test covers the engine and
  is unaffected (no engine change). Verify with the headless Avalonia harness (agent memory
  `nota-ui-verify`): host `PianoRollView` in a `Window`, seed notes, drive zoom through
  synthetic wheel/pointer events and capture frames at 6 / 12 / 36 px and after Fit.
