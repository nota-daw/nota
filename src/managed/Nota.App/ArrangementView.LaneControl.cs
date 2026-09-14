// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Nota.Application;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    // ---- lanes ------------------------------------------------------------
    private sealed class LaneControl : Control
    {
        private readonly ArrangementView _o;
        public LaneControl(ArrangementView o)
        {
            _o = o;
            ClipToBounds = true;
        }

        private enum Drag { None, Move, TrimL, TrimR }
        private Drag _drag;
        private int _dragTrackId, _dragClipIndex;
        private ClipVM? _dragClip;
        private double _grabBeat, _origStart, _origLen;

        // Live-resize waveform preview (point 2): full-material peaks + the committed play
        // window [S0,S1] in that material's domain, captured at drag start. Drawing maps
        // each timeline beat through the committed anchor to a material point, so a resize
        // reveals/hides the REAL audio instead of squashing the fixed window peaks.
        private float[]? _rpPeaks;
        private int _rpCount;
        private double _rpMTotal, _rpS0, _rpS1;   // material total + committed window (frames for unwarped, beats for warped)
        private const double EdgePx = 6;

        // Press-pending: a clip was pressed but the 4px drag threshold isn't crossed yet, so
        // it's still a potential click (select / narrow selection), not a drag (req 2.3/2.12).
        private bool _pending;
        private Point _pressPos;
        private Drag _pendingMode;             // Move / TrimL / TrimR chosen at press
        private ClipVM? _pendingClip;
        private int _pendingTrackId, _pendingClipIndex;
        private double _pendingGrabBeat;
        private bool _pendingWasSelected;      // grabbed clip already in the multi-selection
        private Point _lastPos;                // last pointer position (drag-tooltip anchor)

        // Clip-edge resize hover: which edge (if any) a resize would grab, so it's
        // clear whether a drag will move or resize (and which side).
        private int _hoverEdgeTrack = -1, _hoverEdgeClip = -1;
        private Drag _hoverEdge = Drag.None;
        private static readonly Cursor ArrowCursor = new(StandardCursorType.Arrow);
        private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
        private bool _resizeCursorOn;

        // Group move (drag a clip that's part of the multi-selection → move all).
        private List<(int track, int clip, ClipVM vm, double origStart, int origRow)>? _groupMove;
        private double _grabOrigStart;   // grabbed clip's original start (delta reference)
        private int _grabRow;            // grabbed clip's row (vertical delta reference)
        private int _moveRowDelta;       // rows to shift the group (instrument→instrument only)
        private List<double> _snapTargets = new();   // neighbour clip edges + markers (magnetic snap, 2.4)

        // Rubber-band on empty lane space. Plain drag = clip marquee (select whole clips);
        // Shift+drag = time-range selection (req 1.2). Decided at press by _emptyTimeMode.
        private bool _marqueeArmed;      // pressed on empty space; may become a drag
        private Point _marqueePress;     // press point (control coords)
        private bool _rangeActive;       // the drag crossed the threshold → a band is live
        private bool _emptyTimeMode;     // Shift held at press → author a time range, not a marquee
        private Rect? _marquee;          // live clip-marquee rect (null unless a marquee drag)
        private int _shiftClipTrack = -1, _shiftClipIndex = -1;   // Shift-armed over a clip → click falls back to rectangular clip-select
        private const double DragThreshold = 4;

        // Automation editing (M9-A3).
        private AutoPt? _autoDrag;
        private TrackVM? _autoDragTrack;
        private double _autoDragLo, _autoDragHi;   // neighbour beats — a point can't be dragged past them (req 8.2.3)
        private float _autoDragStartVal;           // value at grab (for Shift fine-mode, req 8.2.2)
        private AutoPt? _bendLeft, _bendRight;   // segment being bent (M9-D)
        private TrackVM? _bendTrack;
        // While dragging/bending, the engine lane is updated live (no undo) so the device
        // follows in real time; the first update checkpoints undo once, the rest are raw.
        private bool _autoLiveStarted;
        private AutoPt? _hoverPoint;             // point / segment-left under the cursor (hover feedback)
        private AutoPt? _hoverSegLeft;
        private TrackVM? _hoverTrack;
        private TrackVM? _rangeTrack;            // Shift-drag time-range selection in progress (Phase 2)
        private double _rangeAnchor;
        public bool IsEditingPoint => _autoDrag != null || _bendLeft != null;   // M9-C/D: don't clobber a hand-edit
        private double RowTop(int i) => _o.RowTop(i);
        private double RowH(int i) => _o.RowHeightAt(i);
        // Target-selector pill: width fits its label so nothing clips.
        private const double PillH = 16;
        private static double PillWidth(string label)
        {
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 9, Brushes.White);
            return Math.Clamp(Math.Ceiling(ft.Width) + 12, 34, 180);
        }
        private Rect PillRect(int i, double w) => new(4, RowTop(i) + 3, w, PillH);
        private double ValueToY(TrackVM t, int row, float v)
        {
            var (min, max) = _o.AutoRange(t);
            double top = RowTop(row) + 8, bot = RowTop(row) + RowH(row) - 8;
            double frac = max > min ? Math.Clamp((v - min) / (max - min), 0, 1) : 0.5;
            return bot - frac * (bot - top);
        }
        private float YToValue(TrackVM t, int row, double py)
        {
            var (min, max) = _o.AutoRange(t);
            double top = RowTop(row) + 8, bot = RowTop(row) + RowH(row) - 8;
            double frac = Math.Clamp((bot - py) / (bot - top), 0, 1);
            return (float)(min + frac * (max - min));
        }

        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
            => _o.HandleLaneWheel(e, e.GetPosition(this).X);

        private (TrackVM track, ClipVM clip)? HitTest(Point p, out double beat)
        {
            beat = _o._scrollBeats + p.X / _o._pixelsPerBeat;
            int ti = _o.RowAtY(p.Y);
            if (ti < 0) return null;
            var t = _o._tracks[ti];
            foreach (var c in t.Clips)
                if (beat >= c.StartBeat && beat <= c.StartBeat + c.LengthBeats)
                    return (t, c);
            return null;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (_o._automationMode) { AutoPointerPressed(e); return; }
            var pt = e.GetCurrentPoint(this);
            var hit = HitTest(pt.Position, out double beat);

            if (pt.Properties.IsRightButtonPressed)
            {
                if (hit is { } h) ShowClipMenu(h.track.Id, h.clip, beat);
                else ShowLaneMenu(pt.Position, beat);
                return;
            }

            if (hit is not { } hh)
            {
                // Double-click empty space on an instrument track's lane → new MIDI clip there.
                if (e.ClickCount == 2)
                {
                    int ti = _o.RowAtY(pt.Position.Y);
                    if (ti >= 0 && _o._tracks[ti].IsInstrument)
                    {
                        _o.AddMidiClipAt(_o._tracks[ti].Id, beat);
                        return;
                    }
                }
                // Empty space: arm a rubber-band. Plain drag selects clips (marquee); Shift+drag
                // authors a time range; a plain click (no drag) moves the playhead (on release).
                _marqueeArmed = true;
                _marqueePress = pt.Position;
                _rangeActive = false;
                _emptyTimeMode = (e.KeyModifiers & KeyModifiers.Shift) != 0;
                _marquee = null;
                _shiftClipTrack = -1; _shiftClipIndex = -1;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }

            if (e.ClickCount == 2)
            {
                // Keep the selection in sync with the opened editor (without firing
                // the select→Devices path), so the Clip tab targets this clip.
                _o.SelTrackId = hh.track.Id;
                _o.SelClipIndex = hh.clip.ClipIndex;
                _o.OnClipDoubleClicked(hh.track.Id, hh.clip.ClipIndex, hh.clip.IsMidi);
                return;
            }

            // Shift over a clip: arm a time-range rubber-band (like empty space) so a drag
            // authors a range even when it starts on top of a clip. A plain click (no drag)
            // falls back to the rectangular clip selection from the anchor (req 1.1.3).
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0)
            {
                _marqueeArmed = true;
                _marqueePress = pt.Position;
                _rangeActive = false;
                _emptyTimeMode = true;
                _marquee = null;
                _shiftClipTrack = hh.track.Id; _shiftClipIndex = hh.clip.ClipIndex;
                e.Pointer.Capture(this);
                InvalidateVisual();
                return;
            }

            // Cmd/Ctrl+click toggles this clip in the selection — never starts a drag (req 1.1.2).
            if (ArrangementView.IsPrimaryDown(e.KeyModifiers))
            {
                _o.ToggleSelect(hh.track.Id, hh.clip.ClipIndex);
                return;
            }

            double x0 = _o.BeatToX(hh.clip.StartBeat);
            double x1 = _o.BeatToX(hh.clip.StartBeat + hh.clip.LengthBeats);
            double px = pt.Position.X;

            // Arm a press-pending: whether this becomes a drag (move/trim) or a click
            // (select / narrow selection) is decided once the 4px threshold is crossed.
            _pending = true;
            _pressPos = pt.Position;
            _pendingMode = (px - x0 <= EdgePx) ? Drag.TrimL : (x1 - px <= EdgePx) ? Drag.TrimR : Drag.Move;
            _pendingClip = hh.clip;
            _pendingTrackId = hh.track.Id;
            _pendingClipIndex = hh.clip.ClipIndex;
            _pendingGrabBeat = beat;
            _pendingWasSelected = _o.IsSelected(hh.track.Id, hh.clip.ClipIndex);
            e.Pointer.Capture(this);
            InvalidateVisual();
        }

        // Snapshot start + row of every selected clip so the group moves rigidly.
        private void BeginGroupMove(int grabTrackId, double grabbedStart, double grabBeat)
        {
            _drag = Drag.Move;
            _grabBeat = grabBeat;
            _grabOrigStart = grabbedStart;
            _moveRowDelta = 0;
            _groupMove = new List<(int, int, ClipVM, double, int)>();
            for (int r = 0; r < _o._tracks.Count; r++)
            {
                var t = _o._tracks[r];
                if (t.Id == grabTrackId) _grabRow = r;
                foreach (var c in t.Clips)
                    if (_o.IsSelected(t.Id, c.ClipIndex))
                        _groupMove.Add((t.Id, c.ClipIndex, c, c.StartBeat, r));
            }
            // Magnetic-snap targets: every other clip's edges + markers (exclude the group).
            _snapTargets = _o.SnapTargets(new HashSet<(int, int)>(_groupMove.Select(m => (m.track, m.clip))));
        }

        // Vertical shift is allowed only when every dragged clip lands on a track of
        // the same type (instrument→instrument or audio→audio); else stay horizontal.
        private int ClampRowDelta(int delta)
        {
            if (delta == 0 || _groupMove is null) return 0;
            foreach (var m in _groupMove)
            {
                int tr = m.origRow + delta;
                if (tr < 0 || tr >= _o._tracks.Count) return 0;
                if (_o._tracks[tr].IsInstrument != _o._tracks[m.origRow].IsInstrument) return 0;
            }
            return delta;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (_o._automationMode) { AutoPointerMoved(e); return; }
            var pos = e.GetPosition(this);
            _lastPos = pos;
            _lastMods = e.KeyModifiers;

            if (_marqueeArmed)
            {
                if (_rangeActive || Math.Abs(pos.X - _marqueePress.X) >= DragThreshold
                                 || Math.Abs(pos.Y - _marqueePress.Y) >= DragThreshold)
                {
                    _rangeActive = true;
                    if (_emptyTimeMode)
                    {
                        // Shift+drag authors a time-range live: snap both edges (Alt bypasses),
                        // extend across rows vertically (req 1.2.1/1.2.2).
                        double a = _o.SnapMaybe(_o._scrollBeats + _marqueePress.X / _o._pixelsPerBeat, e.KeyModifiers);
                        double b = _o.SnapMaybe(_o._scrollBeats + pos.X / _o._pixelsPerBeat, e.KeyModifiers);
                        int rowA = Math.Max(0, _o.RowAtYClamped(_marqueePress.Y));
                        int rowB = Math.Max(0, _o.RowAtYClamped(pos.Y));
                        _o.SetTimeSelection(a, b, rowA, rowB);
                    }
                    else
                    {
                        // Plain drag = clip marquee (normalised; drag can go up/left).
                        _marquee = new Rect(
                            Math.Min(_marqueePress.X, pos.X), Math.Min(_marqueePress.Y, pos.Y),
                            Math.Abs(pos.X - _marqueePress.X), Math.Abs(pos.Y - _marqueePress.Y));
                    }
                }
                InvalidateVisual();
                return;
            }

            // Promote a press-pending to a real drag once the threshold is crossed (req 2.3);
            // below the threshold it stays a click and the model is never touched.
            if (_pending)
            {
                if (Math.Abs(pos.X - _pressPos.X) < DragThreshold && Math.Abs(pos.Y - _pressPos.Y) < DragThreshold)
                    return;
                _pending = false;
                if (_pendingMode == Drag.Move)
                {
                    // Grabbing an already-selected clip moves the whole group; otherwise the
                    // clip becomes the sole selection first (req 2.1/2.2).
                    if (!_pendingWasSelected) _o.Select(_pendingTrackId, _pendingClipIndex);
                    BeginGroupMove(_pendingTrackId, _pendingClip!.StartBeat, _pendingGrabBeat);
                }
                else
                {
                    _o.Select(_pendingTrackId, _pendingClipIndex);   // trims act on a single clip
                    _drag = _pendingMode;
                    _dragTrackId = _pendingTrackId; _dragClipIndex = _pendingClipIndex;
                    _dragClip = _pendingClip; _origStart = _pendingClip!.StartBeat; _origLen = _pendingClip.LengthBeats;
                    _grabBeat = _pendingGrabBeat;
                    _snapTargets = _o.SnapTargets(new HashSet<(int, int)> { (_pendingTrackId, _pendingClipIndex) });
                    if (!_dragClip.IsMidi) CaptureResizePreview(_dragTrackId, _dragClipIndex);   // real-audio resize preview (point 2)
                }
            }

            if (_drag == Drag.None) { UpdateEdgeHover(pos); return; }
            EnsureAutoScroll();
            UpdateActiveDrag(pos, e.KeyModifiers);
        }

        // Apply a move/resize drag to the preview at the given pointer position + modifiers.
        // Split out so the autoscroll timer can re-run it after nudging the view (req 2.10).
        private void UpdateActiveDrag(Point pos, KeyModifiers mods)
        {
            double beat = _o._scrollBeats + pos.X / _o._pixelsPerBeat;

            if (_drag == Drag.Move && _groupMove is not null)
            {
                // Snap by the grabbed clip (Alt bypasses; magnetic to neighbours), then shift
                // the whole group by the same delta.
                double snappedStart = Math.Max(0, _o.SnapMagnetic(_grabOrigStart + (beat - _grabBeat), mods, _snapTargets));
                double delta = snappedStart - _grabOrigStart;
                foreach (var m in _groupMove) m.vm.StartBeat = Math.Max(0, m.origStart + delta);
                // Vertical: how many rows to shift (instrument→instrument only).
                int targetRow = Math.Max(0, _o.RowAtYClamped(pos.Y));
                _moveRowDelta = ClampRowDelta(targetRow - _grabRow);
                InvalidateVisual();
                return;
            }

            if (_dragClip is null) return;
            double d = beat - _grabBeat;
            switch (_drag)
            {
                case Drag.TrimR:
                    double newEnd = _o.SnapMagnetic(_origStart + _origLen + d, mods, _snapTargets);
                    _dragClip.LengthBeats = Math.Max(0.25, newEnd - _dragClip.StartBeat);
                    break;
                case Drag.TrimL:
                    double ns = Math.Clamp(_o.SnapMagnetic(_origStart + d, mods, _snapTargets), 0, _origStart + _origLen - 0.25);
                    _dragClip.StartBeat = ns;
                    _dragClip.LengthBeats = (_origStart + _origLen) - ns;
                    break;
            }
            InvalidateVisual();
        }

        // --- autoscroll near the viewport edges during a drag (req 2.10) --------
        private DispatcherTimer? _autoScroll;
        private KeyModifiers _lastMods;
        private void EnsureAutoScroll()
        {
            if (_autoScroll is not null) { if (!_autoScroll.IsEnabled) _autoScroll.Start(); return; }
            _autoScroll = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(16) };
            _autoScroll.Tick += (_, _) => AutoScrollTick();
            _autoScroll.Start();
        }
        private void AutoScrollTick()
        {
            // Only clip move/resize drags autoscroll; anything else stops the timer.
            if (_drag != Drag.Move && _drag != Drag.TrimL && _drag != Drag.TrimR) { _autoScroll?.Stop(); return; }
            const double zone = 40;                 // logical px from an edge where scrolling kicks in
            double w = Bounds.Width;
            double depth = _lastPos.X < zone ? -(zone - _lastPos.X)
                         : _lastPos.X > w - zone ? (_lastPos.X - (w - zone))
                         : 0;
            if (depth == 0) return;
            // Beats/tick scales with how deep into the edge the cursor is (0.3..~2 beats).
            double beats = Math.Sign(depth) * Math.Clamp(Math.Abs(depth) / zone, 0.15, 1.0) * 2.0;
            _o.ScrollByBeats(beats);
            UpdateActiveDrag(_lastPos, _lastMods);   // the clip follows the newly-scrolled view
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_o._automationMode) { AutoPointerReleased(e); return; }
            var eng = _o._engine;

            // A press that never crossed the drag threshold is a click on a clip: select it,
            // or narrow a multi-selection down to just it (req 2.2/2.12). No model change.
            if (_pending)
            {
                _pending = false;
                e.Pointer.Capture(null);
                if (!_pendingWasSelected || _o.SelectionCount > 1)
                    _o.Select(_pendingTrackId, _pendingClipIndex);
                // Also drop the playhead where the user clicked (same as clicking empty lane) —
                // but not during playback: scrubbing off the grid is disruptive, use the ruler.
                if (_o._engine is not { IsPlaying: true })
                    _o.SeekTo(_o._scrollBeats + _pressPos.X / _o._pixelsPerBeat);
                InvalidateVisual();
                return;
            }

            if (_marqueeArmed)
            {
                _marqueeArmed = false;
                e.Pointer.Capture(null);
                if (!_rangeActive)
                {
                    if (_shiftClipIndex >= 0)
                        _o.ShiftSelectTo(_shiftClipTrack, _shiftClipIndex);   // Shift-click on a clip: rectangular clip-select (req 1.1.3)
                    else
                    {
                        // Plain click on empty space: clear everything and move the playhead
                        // (except during playback — scrub from the ruler, not the grid).
                        _o.Select(-1, -1);
                        _o.ClearTimeSelection();
                        if (_o._engine is not { IsPlaying: true })
                            _o.SeekTo(_o._scrollBeats + _marqueePress.X / _o._pixelsPerBeat);
                    }
                }
                else if (!_emptyTimeMode && _marquee is { } r)
                {
                    SelectInMarquee(r);   // plain drag selected whole clips
                }
                // Shift+drag already authored the live time range; nothing to commit.
                _rangeActive = false; _marquee = null;
                _shiftClipTrack = -1; _shiftClipIndex = -1;
                InvalidateVisual();
                return;
            }

            if (_drag == Drag.Move && _groupMove is not null)
            {
                int rowDelta = _moveRowDelta;
                // Nothing actually moved (snap kept every start, no row change) → don't touch
                // the model, so a jiggle-and-release doesn't create a no-op undo step.
                bool moved = rowDelta != 0 || _groupMove.Any(m => Math.Abs(m.vm.StartBeat - m.origStart) > 1e-9);
                bool keptDeviceAuto = false;
                if (eng is not null && moved)
                    // Descending clip index: cross-track moves erase from the source and
                    // would otherwise invalidate lower indices on the same track.
                    foreach (var m in _groupMove.OrderByDescending(m => m.clip))
                    {
                        int destRow = m.origRow + rowDelta;
                        int destTrack = (rowDelta != 0 && destRow >= 0 && destRow < _o._tracks.Count)
                            ? _o._tracks[destRow].Id : m.track;
                        if (destTrack != m.track)
                        {
                            eng.MoveClipToTrack(m.track, m.clip, destTrack, m.vm.StartBeat);
                            keptDeviceAuto |= eng.LastMoveKeptDeviceAutomation();
                        }
                        else eng.MoveClip(m.track, m.clip, m.vm.StartBeat);
                        // Same-track moves keep their clip index, so an open editor on one of
                        // them stays valid — but its START read-out is now off.
                        if (rowDelta == 0) _o.RaiseClipGeometryChanged(m.track, m.clip);
                    }
                if (keptDeviceAuto)   // explicit hint: only Volume/Pan followed across tracks (req 8.3.4)
                    _o.StatusMessage?.Invoke("Moved across tracks — device automation stayed on the source");
                _groupMove = null; _drag = Drag.None; _moveRowDelta = 0;
                e.Pointer.Capture(null);
                if (moved && rowDelta != 0) _o.Select(-1, -1);   // indices changed → drop stale selection
                if (moved) _o.Refresh(); else InvalidateVisual();
                return;
            }

            if (_drag == Drag.None || _dragClip is null) { _drag = Drag.None; return; }
            // Skip a resize that didn't change anything (a click on the edge) — no undo step.
            bool changed = Math.Abs(_dragClip.StartBeat - _origStart) > 1e-9
                        || Math.Abs(_dragClip.LengthBeats - _origLen) > 1e-9;
            if (eng is not null && changed)
            {
                // Audio clips resize the grid-relative way: warped clips (or unwarped clips
                // dragged past their source) stretch, otherwise trim — the engine picks.
                // MIDI clips trim as before.
                if (_dragClip.IsMidi)
                    eng.TrimClip(_dragTrackId, _dragClipIndex, _dragClip.StartBeat, _dragClip.LengthBeats);
                else
                    eng.ResizeAudioClip(_dragTrackId, _dragClipIndex, _dragClip.StartBeat, _dragClip.LengthBeats);
                // An editor open on this clip still holds the pre-drag geometry.
                _o.RaiseClipGeometryChanged(_dragTrackId, _dragClipIndex);
            }
            _drag = Drag.None;
            _dragClip = null;
            _rpPeaks = null;   // drop the resize-preview peaks
            e.Pointer.Capture(null);
            if (changed) _o.Refresh(); else InvalidateVisual();
        }

        // Esc / lost pointer capture / lost window focus: abandon any in-progress gesture with
        // NO model change (the model is only touched on release), restoring the live preview
        // from the engine snapshot (req 1.2.6 / 2.11 / 7.9).
        internal bool CancelGesture()
        {
            // Only a *committed* gesture (drag/resize/range/point edit) is cancellable. A mere
            // press-pending or armed rubber-band is a click-in-progress: leave it untouched so
            // that a PointerCaptureLost fired around button-up doesn't wipe the latch before
            // OnPointerReleased turns it into a selection.
            bool active = _drag != Drag.None || _groupMove is not null || _rangeActive
                          || _autoDrag is not null || _bendLeft is not null || _rangeTrack is not null;
            if (!active) return false;
            _pending = false; _drag = Drag.None; _groupMove = null; _moveRowDelta = 0; _dragClip = null; _rpPeaks = null;
            _marqueeArmed = false; _rangeActive = false; _marquee = null; _shiftClipTrack = -1; _shiftClipIndex = -1;
            _autoDrag = null; _autoDragTrack = null; _bendLeft = _bendRight = null; _bendTrack = null;
            _rangeTrack = null;
            _autoScroll?.Stop();
            SetResizeCursor(false);
            _o.Refresh();   // reload authoritative clip/automation positions, discarding the preview
            return true;
        }

        // NB: we deliberately do NOT cancel on OnPointerCaptureLost. On macOS that event fires
        // *before* OnPointerReleased on a normal button-up, so cancelling there would abort every
        // drag/resize/marquee right before it commits. External loss (alt-tab) is handled via the
        // window's Deactivated event instead (see MainWindow), and Esc cancels explicitly.

        // Select every clip whose rect intersects the marquee (across all tracks), replacing
        // the current selection.
        private void SelectInMarquee(Rect r)
        {
            var hits = new List<(int, int)>();
            for (int i = 0; i < _o._tracks.Count; i++)
            {
                double y = RowTop(i);
                if (!r.Intersects(new Rect(0, y, double.MaxValue, RowH(i)))) continue;
                foreach (var c in _o._tracks[i].Clips)
                {
                    double cx0 = _o.BeatToX(c.StartBeat);
                    double cx1 = _o.BeatToX(c.StartBeat + c.LengthBeats);
                    var clipRect = new Rect(cx0, y, Math.Max(1, cx1 - cx0), RowH(i));
                    if (r.Intersects(clipRect)) hits.Add((_o._tracks[i].Id, c.ClipIndex));
                }
            }
            _o.SetSelection(hits);
        }

        // --- automation editing (M9-A3) ------------------------------------
        private void AutoPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            var pos = pt.Position;
            int i = _o.RowAtY(pos.Y);
            if (i < 0) return;
            var t = _o._tracks[i];

            double pillW = PillWidth(t.AutoLabel);
            if (PillRect(i, pillW).Contains(pos)) { _o.ShowAutoTargetMenu(this, t); return; }

            double curBeat = Math.Max(0, _o.SnapMaybe(_o._scrollBeats + pos.X / _o._pixelsPerBeat, e.KeyModifiers));

            // Point under the cursor? (7px grab radius). Detected first so Shift can mean
            // "fine-drag this point" (req 8.2.2) rather than always starting a range select.
            AutoPt? hit = null;
            foreach (var p in t.AutoPoints)
            {
                double dx = _o.BeatToX(p.Beat) - pos.X;
                double dy = ValueToY(t, i, p.Value) - pos.Y;
                if (dx * dx + dy * dy <= 49) { hit = p; break; }
            }

            bool right = pt.Properties.IsRightButtonPressed;
            bool dbl = e.ClickCount == 2;

            // Shift + left-drag authors a time-range selection (Phase 2) — including when the
            // drag starts on a point, so a range bounded by points is selectable. Fine-drag of a
            // point is still available by holding Shift *during* an already-started drag.
            if ((e.KeyModifiers & KeyModifiers.Shift) != 0 && !right)
            {
                _rangeTrack = t; _rangeAnchor = curBeat;
                _o.SetAutoSelection(t, curBeat, curBeat);
                e.Pointer.Capture(this);
                return;
            }

            if (hit is not null)
            {
                // On a point: right-click or double-click removes it; a single press grabs it to drag.
                if (right || dbl) { t.AutoPoints.Remove(hit); _o.CommitAuto(t); InvalidateVisual(); return; }
                // else: grab the existing point (drag logic below).
            }
            else
            {
                var seg = HitSegment(t, i, pos);
                // Right-click resets a segment's curve, else opens the range menu.
                if (right)
                {
                    if (seg is { } sr) { sr.left.Curve = 0f; _o.CommitAuto(t); InvalidateVisual(); }
                    else ShowAutoMenu(t, curBeat);
                    return;
                }
                // Alt+drag on a segment bends it (M9-D).
                if (seg is { } sg && (e.KeyModifiers & KeyModifiers.Alt) != 0)
                {
                    _bendLeft = sg.left; _bendRight = sg.right; _bendTrack = t;
                    e.Pointer.Capture(this);
                    return;
                }
                if (dbl)
                {
                    // Double-click adds a breakpoint at the cursor, then grabs it for immediate drag.
                    _o.ClearAutoSelection();
                    hit = new AutoPt { Beat = curBeat, Value = YToValue(t, i, pos.Y) };
                    t.AutoPoints.Add(hit);
                }
                else
                {
                    // Single click: behave like the arrangement — move the playhead when stopped, do
                    // nothing while playing. Points come from double-clicking, never a single click.
                    _o.ClearAutoSelection();
                    if (_o._engine is not { IsPlaying: true })
                        _o.SeekTo(_o._scrollBeats + pos.X / _o._pixelsPerBeat);
                    return;
                }
            }

            // Grab the point (existing on a single press, or the one just double-click-added) to drag.
            _autoDrag = hit;
            _autoDragTrack = t;
            _autoDragStartVal = hit!.Value;
            // Neighbour bounds: the point can't be dragged past its neighbours in time (req 8.2.3).
            _autoDragLo = 0; _autoDragHi = double.MaxValue;
            foreach (var p in t.AutoPoints)
            {
                if (ReferenceEquals(p, hit)) continue;
                if (p.Beat <= hit.Beat) _autoDragLo = Math.Max(_autoDragLo, p.Beat);
                else _autoDragHi = Math.Min(_autoDragHi, p.Beat);
            }
            e.Pointer.Capture(this);
            InvalidateVisual();
        }

        // The (left,right) points of the segment whose curve line is under `pos`, or null.
        private (AutoPt left, AutoPt right)? HitSegment(TrackVM t, int row, Point pos)
        {
            var ordered = t.AutoPoints.OrderBy(p => p.Beat).ToList();
            double beat = _o._scrollBeats + pos.X / _o._pixelsPerBeat;
            for (int k = 1; k < ordered.Count; k++)
            {
                var a = ordered[k - 1]; var b = ordered[k];
                if (beat < a.Beat || beat > b.Beat) continue;
                double span = b.Beat - a.Beat;
                if (span <= 0) return null;
                double tt = (beat - a.Beat) / span;
                double vy = a.Value + (b.Value - a.Value) * Shape(tt, a.Curve);
                double cy = ValueToY(t, row, (float)vy);
                return Math.Abs(cy - pos.Y) <= 6 ? (a, b) : null;
            }
            return null;
        }

        private void AutoBendMoved(Point pos)
        {
            if (_bendLeft is null || _bendRight is null || _bendTrack is null) return;
            int i = _o._tracks.IndexOf(_bendTrack);
            if (i < 0) return;
            float v0 = _bendLeft.Value, v1 = _bendRight.Value;
            if (Math.Abs(v1 - v0) < 1e-4f) { _bendLeft.Curve = 0f; InvalidateVisual(); return; }
            double val = YToValue(_bendTrack, i, pos.Y);
            double frac = Math.Clamp((val - v0) / (v1 - v0), 0.02, 0.98);
            // Solve Shape(0.5, curve) == frac  ->  0.5^e = frac  ->  e = ln(frac)/ln(0.5).
            double e = Math.Log(frac) / Math.Log(0.5);
            _bendLeft.Curve = (float)Math.Clamp(-Math.Log2(e) / 4.0, -1.0, 1.0);
            CommitAutoDuringDrag(_bendTrack);   // engine follows the curve bend live
            InvalidateVisual();
        }

        // Hover feedback (M9-D): highlight the point or segment under the cursor.
        private void UpdateHover(Point pos)
        {
            AutoPt? pt = null, seg = null; TrackVM? tr = null;
            int i = _o.RowAtY(pos.Y);
            if (i >= 0)
            {
                var t = _o._tracks[i];
                foreach (var p in t.AutoPoints)
                {
                    double dx = _o.BeatToX(p.Beat) - pos.X, dy = ValueToY(t, i, p.Value) - pos.Y;
                    if (dx * dx + dy * dy <= 49) { pt = p; break; }   // 7px, matches grab radius
                }
                if (pt is null && HitSegment(t, i, pos) is { } sg) seg = sg.left;
                if (pt is not null || seg is not null) tr = t;
            }
            if (!ReferenceEquals(pt, _hoverPoint) || !ReferenceEquals(seg, _hoverSegLeft) || !ReferenceEquals(tr, _hoverTrack))
            {
                _hoverPoint = pt; _hoverSegLeft = seg; _hoverTrack = tr;
                InvalidateVisual();
            }
        }

        private void ClearHover()
        {
            if (_hoverPoint is null && _hoverSegLeft is null) return;
            _hoverPoint = null; _hoverSegLeft = null; _hoverTrack = null;
            InvalidateVisual();
        }

        protected override void OnPointerExited(PointerEventArgs e) { ClearHover(); ClearEdgeHover(); base.OnPointerExited(e); }

        // Which edge (if any) a resize would grab at `pos` — mirrors the press logic so
        // the highlight matches what a drag would do. Updates the resize cursor.
        private void UpdateEdgeHover(Point pos)
        {
            var hit = HitTest(pos, out _);
            var edge = Drag.None; int et = -1, ec = -1;
            if (hit is { } h)
            {
                double x0 = _o.BeatToX(h.clip.StartBeat);
                double x1 = _o.BeatToX(h.clip.StartBeat + h.clip.LengthBeats);
                if (pos.X - x0 <= EdgePx) edge = Drag.TrimL;
                else if (x1 - pos.X <= EdgePx) edge = Drag.TrimR;
                if (edge != Drag.None) { et = h.track.Id; ec = h.clip.ClipIndex; }
            }
            SetResizeCursor(edge != Drag.None);
            if (edge == _hoverEdge && et == _hoverEdgeTrack && ec == _hoverEdgeClip) return;
            _hoverEdge = edge; _hoverEdgeTrack = et; _hoverEdgeClip = ec;
            InvalidateVisual();
        }

        private void ClearEdgeHover()
        {
            SetResizeCursor(false);
            if (_hoverEdge == Drag.None && _hoverEdgeTrack == -1) return;
            _hoverEdge = Drag.None; _hoverEdgeTrack = -1; _hoverEdgeClip = -1;
            InvalidateVisual();
        }

        private void SetResizeCursor(bool on)
        {
            if (on == _resizeCursorOn) return;
            _resizeCursorOn = on;
            Cursor = on ? ResizeCursor : ArrowCursor;
        }

        private void AutoPointerMoved(PointerEventArgs e)
        {
            if (_rangeTrack is not null)
            {
                double beat = Math.Max(0, _o.Snap(_o._scrollBeats + e.GetPosition(this).X / _o._pixelsPerBeat));
                _o.SetAutoSelection(_rangeTrack, _rangeAnchor, beat);
                return;
            }
            if (_bendLeft is not null) { AutoBendMoved(e.GetPosition(this)); return; }
            if (_autoDrag is null || _autoDragTrack is null) { UpdateHover(e.GetPosition(this)); return; }
            int i = _o._tracks.IndexOf(_autoDragTrack);
            if (i < 0) return;
            var pos = e.GetPosition(this);
            // Time: snapped (Alt bypasses), clamped between neighbour points — no reordering (8.2.3).
            double cand = Math.Max(0, _o.SnapMaybe(_o._scrollBeats + pos.X / _o._pixelsPerBeat, e.KeyModifiers));
            _autoDrag.Beat = Math.Clamp(cand, _autoDragLo, _autoDragHi);
            // Value: free, but Shift = fine-mode (quarter sensitivity, anchored at the grab value, 8.2.2).
            float full = YToValue(_autoDragTrack, i, pos.Y);
            _autoDrag.Value = (e.KeyModifiers & KeyModifiers.Shift) != 0
                ? _autoDragStartVal + (full - _autoDragStartVal) * 0.25f
                : full;
            CommitAutoDuringDrag(_autoDragTrack);   // engine follows live (device updates at the playhead)
            InvalidateVisual();
        }

        // First push of a drag checkpoints undo (CommitAuto); the rest are raw (CommitAutoLive),
        // so a whole drag is one undo step while the device follows in real time.
        private void CommitAutoDuringDrag(TrackVM t)
        {
            if (!_autoLiveStarted) { _o.CommitAuto(t); _autoLiveStarted = true; }
            else _o.CommitAutoLive(t);
        }

        private void AutoPointerReleased(PointerReleasedEventArgs e)
        {
            if (_rangeTrack is not null)
            {
                // A zero-width drag (Shift-click, no move) is a deselect, not a selection.
                if (_o._autoSelEnd - _o._autoSelStart <= 1e-6) _o.ClearAutoSelection();
                _rangeTrack = null;
                e.Pointer.Capture(null);
                return;
            }
            if (_bendLeft is not null && _bendTrack is not null)
            {
                // Final state: a live drag already checkpointed at its start, so commit raw;
                // a no-move press (shouldn't happen for bend) still gets a normal undo commit.
                if (_autoLiveStarted) _o.CommitAutoLive(_bendTrack); else _o.CommitAuto(_bendTrack);
                _autoLiveStarted = false;
                _bendLeft = _bendRight = null; _bendTrack = null;
                e.Pointer.Capture(null);
                InvalidateVisual();
                return;
            }
            if (_autoDrag is null || _autoDragTrack is null) return;
            if (_autoLiveStarted) _o.CommitAutoLive(_autoDragTrack); else _o.CommitAuto(_autoDragTrack);
            _autoLiveStarted = false;
            _autoDrag = null;
            _autoDragTrack = null;
            e.Pointer.Capture(null);
            InvalidateVisual();
        }

        // A small inline rename popup (Enter commits, Esc cancels) anchored at the pointer.
        private void PromptRename(string current, Action<string> commit)
        {
            var box = new TextBox { Text = current, Width = 170, FontSize = 12 };
            var flyout = new Flyout { Content = box, Placement = PlacementMode.Pointer };
            box.KeyDown += (_, ke) =>
            {
                if (ke.Key == Key.Enter) { commit(box.Text ?? ""); flyout.Hide(); ke.Handled = true; }
                else if (ke.Key == Key.Escape) { flyout.Hide(); ke.Handled = true; }
            };
            flyout.ShowAt(this);
            Dispatcher.UIThread.Post(() => { box.SelectAll(); box.Focus(); }, DispatcherPriority.Input);
        }

        // Automation-mode right-click: copy/cut/delete the selected time range and paste the
        // range clipboard at the cursor (Phase 2). Copy/Cut/Delete need a selection on this
        // track; Paste needs a filled clipboard.
        private void ShowAutoMenu(TrackVM t, double beat)
        {
            bool hasSel = _o.HasAutoSelection && ReferenceEquals(_o._autoSelTrack, t);
            var flyout = new MenuFlyout();
            var copy = new MenuItem { Header = "Copy automation", IsEnabled = hasSel };
            copy.Click += (_, _) => _o.CopyAutoSelection();
            var cut = new MenuItem { Header = "Cut automation", IsEnabled = hasSel };
            cut.Click += (_, _) => _o.CutAutoSelection();
            var del = new MenuItem { Header = "Delete automation", IsEnabled = hasSel };
            del.Click += (_, _) => _o.DeleteAutoSelection();
            var paste = new MenuItem { Header = "Paste automation", IsEnabled = _o.HasAutoClip };
            paste.Click += (_, _) => _o.PasteAutoAt(t, _o.Snap(beat));
            flyout.Items.Add(copy);
            flyout.Items.Add(cut);
            flyout.Items.Add(del);
            flyout.Items.Add(new Separator());
            flyout.Items.Add(paste);
            flyout.ShowAt(this, showAtPointer: true);
        }

        // ⌘J / Ctrl+J, shown next to the context-menu Consolidate items.
        private static readonly KeyGesture ConsolidateGesture =
            new(Key.J, OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control);

        // Right-click on empty lane space: paste the clipboard clip here (type permitting), and
        // consolidate the time selection when the click lands inside it.
        private void ShowLaneMenu(Point pos, double beat)
        {
            int ti = _o.RowAtY(pos.Y);
            if (ti < 0) return;
            int trackId = _o._tracks[ti].Id;
            bool inRange = _o.TimeSelectionCovers(trackId, beat);
            if (!_o.HasClipClipboard && !inRange) return;
            double at = _o.Snap(beat);
            var flyout = new MenuFlyout();
            if (_o.HasClipClipboard)
            {
                var paste = new MenuItem { Header = "Paste" };
                paste.Click += (_, _) => _o.PasteClipboardAt(trackId, at);
                flyout.Items.Add(paste);
            }
            if (inRange)
            {
                var consolidate = new MenuItem { Header = "Consolidate selection", InputGesture = ConsolidateGesture };
                consolidate.Click += (_, _) => _o.ConsolidateSelection();
                flyout.Items.Add(consolidate);
            }
            flyout.ShowAt(this, showAtPointer: true);
        }

        private void ShowClipMenu(int trackId, ClipVM clip, double beat)
        {
            double at = _o.Snap(beat);
            int idx = clip.ClipIndex;
            // Copy/Cut/Duplicate/Delete act on the whole selection when the clicked clip is
            // part of a multi-selection; otherwise they collapse to just this clip.
            void EnsureSelected() { if (!_o.IsSelected(trackId, idx)) _o.Select(trackId, idx); }
            bool inGroup = _o.IsSelected(trackId, idx) && _o.Selection.Count > 1;

            var flyout = new MenuFlyout();
            var split = new MenuItem { Header = "Split here" };
            split.Click += (_, _) => { _o._engine?.SplitClip(trackId, idx, at); _o.Refresh(); };
            var dup = new MenuItem { Header = inGroup ? "Duplicate selection" : "Duplicate" };
            dup.Click += (_, _) => { EnsureSelected(); _o.DuplicateSelectedClip(); };
            var del = new MenuItem { Header = inGroup ? "Delete selection" : "Delete" };
            del.Click += (_, _) =>
            {
                if (inGroup) _o.DeleteSelectedClips();
                else { _o._engine?.DeleteClip(trackId, idx); _o.Select(-1, -1); _o.Refresh(); }
            };
            // Clip deactivate (key 0): header reflects the clicked clip's current state.
            var deact = new MenuItem
            {
                Header = clip.Active ? (inGroup ? "Deactivate selection" : "Deactivate clip")
                                     : (inGroup ? "Activate selection" : "Activate clip"),
                InputGesture = new KeyGesture(Key.D0),
            };
            deact.Click += (_, _) => { EnsureSelected(); _o.ToggleSelectedClipsActive(); };
            // Reverse (audio only): non-destructive, so the header reflects the clicked clip.
            MenuItem? reverse = null;
            if (!clip.IsMidi && _o._engine is { } rev && rev.TryGetAudioClipInfo(trackId, idx, out var rai))
            {
                bool on = rai.Reversed != 0;
                reverse = new MenuItem
                {
                    Header = on ? (inGroup ? "Un-reverse selection" : "Un-reverse")
                                : (inGroup ? "Reverse selection" : "Reverse"),
                };
                reverse.Click += (_, _) => { EnsureSelected(); _o.ToggleSelectedClipsReverse(); };
            }
            // Consolidate: a right-click inside the time selection acts on that range; otherwise
            // on the multi-selection's span, or just this clip (bakes its edits into one clip).
            bool inRange = _o.TimeSelectionCovers(trackId, beat);
            var consolidate = new MenuItem
            {
                Header = inRange || inGroup ? "Consolidate selection" : "Consolidate",
                InputGesture = ConsolidateGesture,
            };
            consolidate.Click += (_, _) =>
            {
                if (!inRange) EnsureSelected();
                _o.ConsolidateSelection();
            };
            var copy = new MenuItem { Header = inGroup ? "Copy selection" : "Copy" };
            copy.Click += (_, _) => { EnsureSelected(); _o.CopySelectedClip(); };
            var cut = new MenuItem { Header = inGroup ? "Cut selection" : "Cut" };
            cut.Click += (_, _) => { EnsureSelected(); _o.CutSelectedClip(); };
            var paste = new MenuItem { Header = "Paste", IsEnabled = _o.HasClipClipboard };
            paste.Click += (_, _) => _o.PasteClipboardAt(trackId, at);
            var rename = new MenuItem { Header = "Rename…" };
            rename.Click += (_, _) => PromptRename(clip.Name, s => { _o._engine?.SetClipName(trackId, idx, s); _o.Refresh(); });
            flyout.Items.Add(rename);
            flyout.Items.Add(copy);
            flyout.Items.Add(cut);
            flyout.Items.Add(paste);
            flyout.Items.Add(new Separator());
            flyout.Items.Add(split);
            flyout.Items.Add(dup);
            if (reverse is not null) flyout.Items.Add(reverse);
            flyout.Items.Add(consolidate);
            flyout.Items.Add(deact);
            flyout.Items.Add(del);

            // Loop the current selection (or just this clip if nothing is selected).
            var loop = new MenuItem { Header = "Loop selection" };
            loop.Click += (_, _) =>
            {
                if (!_o.LoopSelection())
                    _o.SetLoopRegion(clip.StartBeat, clip.StartBeat + clip.LengthBeats);
            };
            flyout.Items.Add(new Separator());
            flyout.Items.Add(loop);

            // Audio clip → MIDI: Convert / Slice to New MIDI Track. Drums + Slice
            // ship now; Melody + Harmony (pitch detection) are disabled until their DSP lands.
            if (!clip.IsMidi)
            {
                var convert = new MenuItem { Header = "Convert" };
                MenuItem ConvItem(string header, ClipConvertMode mode, bool enabled)
                {
                    var mi = new MenuItem { Header = header, IsEnabled = enabled };
                    if (enabled) mi.Click += (_, _) => _o.ConvertClipRequested?.Invoke(trackId, idx, mode);
                    return mi;
                }
                convert.Items.Add(ConvItem("Convert Melody to New MIDI Track", ClipConvertMode.Melody, true));
                convert.Items.Add(ConvItem("Convert Harmony to New MIDI Track", ClipConvertMode.Harmony, true));
                convert.Items.Add(ConvItem("Convert Drums to New MIDI Track", ClipConvertMode.Drums, true));
                convert.Items.Add(ConvItem("Slice to New MIDI Track", ClipConvertMode.Slice, true));
                flyout.Items.Add(new Separator());
                flyout.Items.Add(convert);
            }

            // M5-6: copy a clip (MIDI or audio) into a session slot (choose the scene).
            if (_o._engine is { } eng)
            {
                bool midi = clip.IsMidi;
                var toSession = new MenuItem { Header = "Copy to session" };
                for (int s = 0; s < eng.SceneCount; s++)
                {
                    int sc = s;
                    var it = new MenuItem { Header = $"Scene {sc + 1}" };
                    if (midi) it.Click += (_, _) => { eng.ArrangementClipToSession(trackId, idx, sc); _o.SessionChanged?.Invoke(); };
                    else      it.Click += (_, _) => { eng.ArrangementAudioClipToSession(trackId, idx, sc); _o.SessionChanged?.Invoke(); };
                    toSession.Items.Add(it);
                }
                flyout.Items.Add(new Separator());
                flyout.Items.Add(toSession);
            }
            flyout.ShowAt(this, showAtPointer: true);
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;

            // Only paint the rows the viewport actually shows (vertical culling) — the render
            // re-runs on scroll, so off-screen tracks skip their fill/clip/waveform work.
            var (rLo, rHi) = _o.VisibleRowRange();

            // Lane backgrounds + separators.
            for (int i = rLo; i <= rHi; i++)
            {
                double y = RowTop(i), rh = RowH(i);
                // A group row reads as chrome between its children, not as another lane.
                ctx.FillRectangle(_o.IsSlimRowAt(i) ? GroupLaneBg : (i & 1) == 0 ? LaneBgA : LaneBgB,
                    new Rect(0, y, w, rh));
                ctx.DrawLine(BeatPen, new Point(0, y + rh), new Point(w, y + rh));
            }
            if (_o._tracks.Count == 0)
                ctx.FillRectangle(LaneBgA, new Rect(0, 0, w, h));

            // Selected-track highlight: a soft brass wash + bright top/bottom edges
            // so the selection reads across the whole grid, not just the header.
            for (int i = rLo; i <= rHi; i++)
            {
                if (_o._tracks[i].Id != _o.SelTrackId) continue;
                double sy = RowTop(i);
                ctx.FillRectangle(SelWash, new Rect(0, sy, w, RowH(i)));
            }

            // Browser drag-over: glow the lane the drop would land on (future state).
            if (_o.DropTrackIndex >= 0 && _o.DropTrackIndex < _o._tracks.Count)
            {
                double dy = RowTop(_o.DropTrackIndex);
                var r = new Rect(0, dy, w, RowH(_o.DropTrackIndex));
                ctx.FillRectangle(DropWash, r);
                ctx.DrawRectangle(null, new Pen(DropEdge, 1.5), r);
            }

            // Vertical grid: bars always, beats when zoomed in enough.
            bool showBeats = _o._pixelsPerBeat >= 12;
            int firstBeat = (int)Math.Floor(_o._scrollBeats);
            for (int beat = firstBeat; ; beat++)
            {
                double x = _o.BeatToX(beat);
                if (x > w) break;
                if (x < 0) continue;
                bool isBar = beat % _o._beatsPerBar == 0;
                if (isBar) ctx.DrawLine(BarPen, new Point(x, 0), new Point(x, h));
                else if (showBeats) ctx.DrawLine(BeatPen, new Point(x, 0), new Point(x, h));
            }

            // Clips: a tinted body under a 2px colour band, with the name drawn only where a
            // run of clips starts (design 1a) so a repeating pattern reads as one block instead
            // of the same word printed twenty times. A clip being dragged to another track is
            // hidden here and shown as a translucent preview on the destination lane (below).
            for (int i = rLo; i <= rHi; i++)
            {
                double y = RowTop(i), rh = RowH(i);
                int tid = _o._tracks[i].Id;
                // Group rows carry no clips of their own: preview their descendants instead
                // (stacked mini-clips collapsed, a thin colour strip expanded).
                if (_o._tracks[i].IsGroup)
                {
                    DrawGroupLane(ctx, _o._tracks[i], y, rh, w, _o.IsGroupCollapsed(tid));
                    continue;
                }
                foreach (var c in _o._tracks[i].Clips)
                {
                    if (IsDraggingCrossTrack(tid, c.ClipIndex)) continue;
                    // Highlight the edge a resize would grab (hover), or the edge actively being trimmed.
                    var edgeHi = Drag.None;
                    if (_hoverEdge != Drag.None && _hoverEdgeTrack == tid && _hoverEdgeClip == c.ClipIndex) edgeHi = _hoverEdge;
                    else if ((_drag == Drag.TrimL || _drag == Drag.TrimR) && _dragTrackId == tid && _dragClipIndex == c.ClipIndex) edgeHi = _drag;
                    DrawClipBody(ctx, _o._tracks[i].ColorIndex, _o._tracks[i].Name, c, y, rh, _o.IsSelected(tid, c.ClipIndex), edgeHi);
                }
            }

            // In-progress audio take (M-fix): audio clips only materialise on stop,
            // so draw a growing red region on the armed track from the take start to
            // the playhead — otherwise recording gives no feedback on the timeline.
            if (_o._engine is { } re)
            {
                int rt = re.AudioRecordTrackId;
                if (rt > 0)
                    for (int i = rLo; i <= rHi; i++)
                    {
                        if (_o._tracks[i].Id != rt) continue;
                        double ry = RowTop(i), rh = RowH(i);
                        // Loop recording punches the loop region → draw a stable box over it.
                        // Otherwise the box grows by the actual captured length (monotonic),
                        // not the playhead.
                        double rx0, rx1;
                        if (re.LoopEnabled)
                        {
                            rx0 = _o.BeatToX(re.LoopStart);
                            rx1 = _o.BeatToX(re.LoopEnd);
                        }
                        else
                        {
                            rx0 = _o.BeatToX(re.AudioRecordStartBeat);
                            rx1 = Math.Max(rx0, _o.BeatToX(re.AudioRecordStartBeat + re.AudioRecordLengthBeats));
                        }
                        var rr = new Rect(rx0, ry + 2, Math.Max(2, rx1 - rx0), rh - 4);
                        ctx.DrawRectangle(RecFill, RecBorder, rr, 4, 4);
                        // Live waveform of what's being captured, so the take shows real
                        // signal rather than an empty box (clips only materialise on stop).
                        DrawRecPeaks(ctx, re, rr);
                        if (rr.Width > 34)
                        {
                            var ft = new FormattedText("● REC", CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, Typeface.Default, 9, RecText);
                            ctx.DrawText(ft, new Point(rr.X + 5, rr.Y + 3));
                        }
                        break;
                    }
            }

            // Automation overlay (M9-A3): scrim + envelope + target pill per row.
            if (_o._automationMode) DrawAutomation(ctx, w);

            // Cross-track drag: the dragged clips are hidden on their source lanes
            // (above) and previewed translucently on the destination lane here.
            if (_groupMove is not null && _moveRowDelta != 0)
                using (ctx.PushOpacity(0.5))
                    foreach (var m in _groupMove)
                    {
                        int destRow = m.origRow + _moveRowDelta;
                        if (destRow < 0 || destRow >= _o._tracks.Count) continue;
                        var dest = _o._tracks[destRow];
                        DrawClipBody(ctx, dest.ColorIndex, dest.Name, m.vm, RowTop(destRow), RowH(destRow), true);
                    }

            // Clip marquee (plain drag on empty space).
            if (_marquee is { } mq) ctx.DrawRectangle(MarqueeFill, MarqueePen, mq);

            // Time-range selection: a band across its covered rows (req 1.2).
            if (_o.HasTimeSelection)
            {
                double sx = _o.BeatToX(_o._timeSelStart), ex = _o.BeatToX(_o._timeSelEnd);
                double ty = RowTop(_o._timeSelRowLo);
                double th = RowTop(_o._timeSelRowHi + 1) - ty;
                double cx0 = Math.Max(0, sx), cx1 = Math.Min(w, ex);
                if (cx1 > cx0) ctx.FillRectangle(MarqueeFill, new Rect(cx0, ty, cx1 - cx0, th));
                if (sx >= 0 && sx <= w) ctx.DrawLine(MarqueePen, new Point(sx, ty), new Point(sx, ty + th));
                if (ex >= 0 && ex <= w) ctx.DrawLine(MarqueePen, new Point(ex, ty), new Point(ex, ty + th));
            }

            // Drag position tooltip (req 2.9): bar.beat of the grabbed clip's start, or the
            // edge being trimmed, in a small pill near the cursor.
            if (_drag != Drag.None)
            {
                double tb = _drag switch
                {
                    Drag.TrimR => (_dragClip?.StartBeat ?? 0) + (_dragClip?.LengthBeats ?? 0),
                    Drag.TrimL => _dragClip?.StartBeat ?? 0,
                    _          => _pendingClip?.StartBeat ?? 0,
                };
                var ft = new FormattedText(_o.FormatBarBeat(tb), CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 10, TooltipText);
                double tx = Math.Clamp(_lastPos.X + 12, 6, Math.Max(6, w - ft.Width - 10));
                double ty = Math.Max(4, _lastPos.Y - 24);
                ctx.DrawRectangle(TooltipBg, null, new Rect(tx - 5, ty - 3, ft.Width + 10, ft.Height + 6), 3, 3);
                ctx.DrawText(ft, new Point(tx, ty));
            }

            // NB: the loop band + playhead are drawn on LaneOverlayControl (a hit-transparent
            // layer above the lanes) so the 30 Hz transport tick doesn't repaint this whole
            // clip/waveform surface — only the overlay's few strokes.

            // Empty-state hint.
            if (_o._tracks.Count == 0)
            {
                var ft = new FormattedText("Add an instrument from the browser",
                    CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Typeface.Default, 12, RulerText);
                ctx.DrawText(ft, new Point(Math.Max(12, (w - ft.Width) / 2), h / 2 - 10));
            }
        }

        // Adds one axis-aligned rectangle as its own closed figure. Batching a clip's many
        // columns/notes into a single StreamGeometry drawn once cuts thousands of per-rect
        // draw commands per clip down to one DrawGeometry call.
        private static void AddRect(StreamGeometryContext g, double x, double y, double w, double h)
        {
            g.BeginFigure(new Point(x, y), true);
            g.LineTo(new Point(x + w, y));
            g.LineTo(new Point(x + w, y + h));
            g.LineTo(new Point(x, y + h));
            g.EndFigure(true);
        }

        private void DrawNotes(DrawingContext ctx, ClipVM c, Rect r, IBrush brush)
        {
            if (c.Notes is null || c.Notes.Length == 0 || c.LengthBeats <= 0 || r.Height <= 0) return;
            // Auto-range to the pitches actually present: a fixed 5-octave range
            // needs far more height than one lane row, so mapping it with a min
            // bar height pushes high notes above the clip and into the row above.
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var n in c.Notes) { lo = Math.Min(lo, n.Pitch); hi = Math.Max(hi, n.Pitch); }
            int span = Math.Max(1, hi - lo + 1);
            double noteH = Math.Clamp(r.Height / span, 1, 4);
            using var _ = ctx.PushClip(r);   // never bleed into adjacent lanes
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                foreach (var n in c.Notes)
                {
                    double nx = r.X + (n.StartBeat / c.LengthBeats) * r.Width;
                    double nw = Math.Max(2, (n.LengthBeats / c.LengthBeats) * r.Width);
                    double ny = r.Bottom - (n.Pitch - lo + 1) * noteH;
                    double ww = Math.Min(nw, r.Right - nx);
                    if (ww > 0) AddRect(g, nx, ny, ww, noteH);
                }
            ctx.DrawGeometry(brush, null, geo);
        }

        // Scratch buffer for live capture peaks — reused each frame (no per-render alloc).
        private readonly float[] _recPeaks = new float[512 * 2];

        // Live waveform for the in-progress take: fetch min/max peaks over the capture
        // buffer sized to the region width, and draw them like a normal clip waveform.
        private void DrawRecPeaks(DrawingContext ctx, IAudioEngine eng, Rect r)
        {
            if (r.Width < 2 || r.Height <= 4) return;
            int want = Math.Clamp((int)r.Width, 1, 512);
            int n = eng.AudioRecordPeaks(_recPeaks, want);
            if (n <= 0) return;
            double mid = r.Y + r.Height / 2;
            double amp = r.Height / 2 - 2;
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                for (int i = 0; i < n; i++)
                {
                    double fx = r.X + (i / (double)n) * r.Width;
                    if (fx < 0 || fx > Bounds.Width) continue;
                    float min = _recPeaks[i * 2];
                    float max = _recPeaks[i * 2 + 1];
                    AddRect(g, fx, mid - max * amp, 1, Math.Max(1, (max - min) * amp));
                }
            ctx.DrawGeometry(RecWave, null, geo);
        }

        private bool IsDraggingCrossTrack(int track, int clip)
            => _groupMove is not null && _moveRowDelta != 0 && _groupMove.Any(m => m.track == track && m.clip == clip);

        // Draws one clip (fill + border + name strip + notes/waveform) at row-y with
        // the given track colour. Reused for the live cross-track drag preview.
        // Group lane preview: collapsed → each descendant clip as a mini-clip stacked into its
        // own sub-lane (child colour), so starts/ends read at a glance; expanded → a thin strip
        // in the group colour over each descendant clip's span, marking content boundaries.
        private void DrawGroupLane(DrawingContext ctx, ArrangementView.TrackVM g, double y, double rowH, double w, bool collapsed)
        {
            if (g.GroupMini.Count == 0) return;
            if (collapsed)
            {
                // Collapsed: the hidden children still have to read, so each takes a sub-lane
                // of the (slim) row. Below ~3px a sub-lane stops being legible — past that the
                // stack degrades to one merged span strip.
                int slots = Math.Max(1, g.GroupSlotCount);
                const double padTop = 5, padBot = 5;
                double innerH = rowH - padTop - padBot;
                double laneH = innerH / slots;
                if (laneH >= 2.5)
                {
                    double barH = Math.Max(2, laneH - 1);
                    foreach (var m in g.GroupMini)
                    {
                        double x0 = _o.BeatToX(m.Start), x1 = _o.BeatToX(m.Start + m.Length);
                        if (x1 < 0 || x0 > w || m.Length <= 0) continue;
                        var (fill, border, _, _) = ClipColors(m.ColorIndex);
                        var r = new Rect(x0, y + padTop + m.Slot * laneH, Math.Max(2, x1 - x0), barH);
                        ctx.DrawRectangle(fill, border, r, 1.5, 1.5);
                    }
                    return;
                }
                foreach (var m in g.GroupMini)
                {
                    double x0 = _o.BeatToX(m.Start), x1 = _o.BeatToX(m.Start + m.Length);
                    if (x1 < 0 || x0 > w || m.Length <= 0) continue;
                    var (fill, border, _, _) = ClipColors(m.ColorIndex);
                    ctx.DrawRectangle(fill, border, new Rect(x0, y + padTop, Math.Max(2, x1 - x0), innerH), 1.5, 1.5);
                }
            }
            else
            {
                var col = TrackColorForIndex(g.ColorIndex);
                var fill = Alpha(col, 0.55);
                var border = new Pen(Alpha(col, 0.85), 1);
                double stripH = Math.Min(6, Math.Max(3, rowH - 12));
                double sy = y + (rowH - stripH) / 2;
                foreach (var m in g.GroupMini)
                {
                    double x0 = _o.BeatToX(m.Start), x1 = _o.BeatToX(m.Start + m.Length);
                    if (x1 < 0 || x0 > w || m.Length <= 0) continue;
                    var r = new Rect(x0, sy, Math.Max(2, x1 - x0), stripH);
                    ctx.DrawRectangle(fill, border, r, 3, 3);
                }
            }
        }

        private void DrawClipBody(DrawingContext ctx, int colorIndex, string label, ClipVM c, double y,
                                  double rowH, bool selected, Drag edgeHi = Drag.None)
        {
            double w = Bounds.Width;
            double x0 = _o.BeatToX(c.StartBeat);
            double x1 = _o.BeatToX(c.StartBeat + c.LengthBeats);
            if (x1 < 0 || x0 > w || c.LengthBeats <= 0) return;
            var (fill, border, _, content) = ClipColors(colorIndex);
            var rect = new Rect(x0, y + 2, Math.Max(2, x1 - x0), rowH - 4);
            ctx.DrawRectangle(fill, selected ? ClipSelBorder : border, rect, 4, 4);

            // Resize-edge affordance: a bright bar on the edge a drag would trim.
            if (edgeHi != Drag.None)
            {
                double ex = edgeHi == Drag.TrimL ? rect.X : rect.Right - 2.5;
                ctx.FillRectangle(EdgeHighlight, new Rect(ex, rect.Y, 2.5, rect.Height));
            }

            // The name is a label on the clip, not a strip that eats a fifth of the row's
            // height. Content first, then the band and the name over it. Both DrawNotes and
            // DrawWaveform already clip themselves to the rect they are handed.
            var contentRect = new Rect(rect.X, rect.Y + BandH, rect.Width, Math.Max(0, rect.Height - BandH));
            if (c.IsMidi) DrawNotes(ctx, c, contentRect, content);
            else DrawWaveform(ctx, c, contentRect, content);

            // 2px band in the track colour along the top edge: the clip's family marker.
            if (rect.Width > 3)
                ctx.FillRectangle(content, new Rect(rect.X + 1, rect.Y + 1, rect.Width - 2, BandH));

            if (rect.Width > 24 && rect.Height > 14 && ShowsLabel(c))
            {
                // The clip's own name (set via its menu) wins; otherwise the track name.
                string text = string.IsNullOrEmpty(c.Name) ? label : c.Name;
                var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, content)
                { MaxTextWidth = Math.Max(8, rect.Width - 10), Trimming = TextTrimming.CharacterEllipsis };
                var plate = new Rect(rect.X + 1, rect.Y + BandH + 1,
                                     Math.Min(rect.Width - 2, ft.Width + 8), ft.Height + 2);
                using var _ = ctx.PushClip(rect);
                // A soft plate keeps the name readable over a dense waveform without
                // reintroducing a full-width strip.
                ctx.FillRectangle(LabelPlate, plate);
                ctx.DrawText(ft, new Point(rect.X + 5, rect.Y + BandH + 2));
            }

            // Deactivated clip (key 0): grey it out with a dark scrim so it reads as "off"
            // while still showing its content/geometry. Inset so the (selection) border stays.
            if (!c.Active)
                ctx.DrawRectangle(InactiveVeil, null, rect.Deflate(1), 3, 3);
        }

        private const double BandH = 2;   // clip colour band along the top edge

        // Which clips carry their name (design 1a's "labels" choice). A clip that was named by
        // hand always shows it — that name was authored to be read.
        private bool ShowsLabel(ClipVM c) => _o.ClipLabels switch
        {
            ClipLabelMode.Every => true,
            ClipLabelMode.None => !string.IsNullOrEmpty(c.Name),
            _ => c.RunStart || !string.IsNullOrEmpty(c.Name),
        };

        // Capture full-material peaks + the committed play window for a live resize preview
        // (point 2). Unwarped: peaks over the whole sample, window = source frames. Warped:
        // peaks over the whole warp, window = played beats. Cleared on release/cancel.
        private void CaptureResizePreview(int trackId, int clipIndex)
        {
            _rpPeaks = null; _rpCount = 0;
            var eng = _o._engine;
            if (eng is null || !eng.TryGetAudioClipInfo(trackId, clipIndex, out var ai)) return;
            const int buckets = 2048;
            var peaks = new float[buckets * 2];
            if (ai.WarpEnabled != 0 && ai.WarpBeats > 0)
            {
                int n = eng.GetClipWarpFullPeaks(trackId, clipIndex, peaks, buckets);
                if (n <= 0) return;
                _rpCount = n; _rpPeaks = peaks;
                _rpMTotal = ai.WarpBeats;
                _rpS0 = ai.WarpPlayStart;
                _rpS1 = ai.WarpPlayEnd > 0 ? ai.WarpPlayEnd : ai.WarpBeats;
            }
            else
            {
                if (ai.SampleId == 0 || !eng.TryGetSampleInfo(ai.SampleId, out var si) || si.Frames <= 0) return;
                int n = eng.GetClipSourcePeaks(trackId, clipIndex, peaks, buckets);
                if (n <= 0) return;
                _rpCount = n; _rpPeaks = peaks;
                _rpMTotal = si.Frames;
                _rpS0 = ai.SourceOffsetFrames;
                double len = ai.LengthFrames > 0 ? ai.LengthFrames : si.Frames - ai.SourceOffsetFrames;
                _rpS1 = ai.SourceOffsetFrames + len;
            }
        }

        // Draw the clip being resized from its full-material peaks, mapping each timeline
        // beat through the committed window so the SAME audio stays anchored and dragging
        // reveals/hides real content (point 2) rather than squashing the window peaks.
        private void DrawResizeWaveform(DrawingContext ctx, Rect r, IBrush brush)
        {
            double mid = r.Y + r.Height / 2, amp = r.Height / 2 - 1;
            double ppb = _o._pixelsPerBeat, scroll = _o._scrollBeats;
            double rate = (_rpS1 - _rpS0) / _origLen;   // material units per timeline beat (committed)
            double vx0 = Math.Max(r.X, 0), vx1 = Math.Min(r.Right, Bounds.Width);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                for (double px = vx0; px < vx1; px += 1)
                {
                    // Material point under this column, anchored to the committed window.
                    double s0 = _rpS0 + (scroll + px / ppb - _origStart) * rate;
                    double s1 = _rpS0 + (scroll + (px + 1) / ppb - _origStart) * rate;
                    double f0 = Math.Clamp(s0 / _rpMTotal, 0, 1), f1 = Math.Clamp(s1 / _rpMTotal, 0, 1);
                    int b0 = Math.Clamp((int)(f0 * _rpCount), 0, _rpCount - 1);
                    int b1 = Math.Clamp((int)Math.Ceiling(f1 * _rpCount), b0 + 1, _rpCount);
                    float min = 1f, max = -1f;
                    for (int b = b0; b < b1; b++) { min = Math.Min(min, _rpPeaks![b * 2]); max = Math.Max(max, _rpPeaks[b * 2 + 1]); }
                    if (min > max) continue;
                    AddRect(g, px, mid - max * amp, 1, Math.Max(1, (max - min) * amp));
                }
            ctx.DrawGeometry(brush, null, geo);
        }

        private void DrawWaveform(DrawingContext ctx, ClipVM c, Rect r, IBrush brush)
        {
            // While resizing THIS clip, show the real audio revealed/hidden by the drag.
            if ((_drag == Drag.TrimL || _drag == Drag.TrimR) && ReferenceEquals(c, _dragClip)
                && _rpPeaks is not null && _rpCount > 0 && _rpMTotal > 0 && _origLen > 0 && r.Height > 0 && r.Width > 0)
            {
                DrawResizeWaveform(ctx, r, brush);
                return;
            }
            if (c.Peaks is null || c.PeakCount <= 0 || r.Height <= 0 || r.Width <= 0) return;
            double mid = r.Y + r.Height / 2;
            double amp = r.Height / 2 - 1;
            // Draw one contiguous column per visible pixel, aggregating the peak buckets
            // that fall in it. This keeps the waveform solid on wide/zoomed clips instead
            // of leaving gaps between sparse fixed-position bars.
            double vx0 = Math.Max(r.X, 0), vx1 = Math.Min(r.Right, Bounds.Width);
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                for (double px = vx0; px < vx1; px += 1)
                {
                    int b0 = Math.Clamp((int)((px - r.X) / r.Width * c.PeakCount), 0, c.PeakCount - 1);
                    int b1 = Math.Clamp((int)Math.Ceiling((px + 1 - r.X) / r.Width * c.PeakCount), b0 + 1, c.PeakCount);
                    float min = 1f, max = -1f;
                    for (int b = b0; b < b1; b++) { min = Math.Min(min, c.Peaks[b * 2]); max = Math.Max(max, c.Peaks[b * 2 + 1]); }
                    if (min > max) continue;
                    AddRect(g, px, mid - max * amp, 1, Math.Max(1, (max - min) * amp));
                }
            ctx.DrawGeometry(brush, null, geo);
        }

        private void DrawAutomation(DrawingContext ctx, double w)
        {
            var (rLo, rHi) = _o.VisibleRowRange();
            for (int i = rLo; i <= rHi; i++)
            {
                var t = _o._tracks[i];
                double y = RowTop(i);
                ctx.FillRectangle(AutoScrim, new Rect(0, y, w, RowH(i)));
                if (_o.IsSlimRowAt(i)) continue;   // a slim group bar has no room for an envelope

                var (fill, _, strip, content) = ClipColors(t.ColorIndex);
                var pen = new Pen(content, 1.6);

                var ordered = t.AutoPoints.OrderBy(p => p.Beat).ToList();
                if (ordered.Count == 0)
                {
                    // Empty lane: dashed baseline at the current value (grab to start).
                    double by = ValueToY(t, i, _o.AutoCurrent(t));
                    ctx.DrawLine(AutoBasePen, new Point(0, by), new Point(w, by));
                }
                else
                {
                    double firstX = _o.BeatToX(ordered[0].Beat), firstY = ValueToY(t, i, ordered[0].Value);
                    ctx.DrawLine(pen, new Point(0, firstY), new Point(firstX, firstY));   // left end-hold
                    for (int k = 1; k < ordered.Count; k++)
                    {
                        var a = ordered[k - 1]; var b = ordered[k];
                        var segPen = ReferenceEquals(t, _hoverTrack) && ReferenceEquals(a, _hoverSegLeft) ? AutoHoverPen : pen;
                        double ax = _o.BeatToX(a.Beat), ay = ValueToY(t, i, a.Value);
                        double bx = _o.BeatToX(b.Beat), by2 = ValueToY(t, i, b.Value);
                        if (a.Curve == 0f) { ctx.DrawLine(segPen, new Point(ax, ay), new Point(bx, by2)); continue; }
                        // Curved segment: polyline of shaped sub-samples (M9-D).
                        const int steps = 16;
                        var prev = new Point(ax, ay);
                        for (int s = 1; s <= steps; s++)
                        {
                            double tt = s / (double)steps;
                            double vy = a.Value + (b.Value - a.Value) * Shape(tt, a.Curve);
                            var cur = new Point(ax + (bx - ax) * tt, ValueToY(t, i, (float)vy));
                            ctx.DrawLine(segPen, prev, cur);
                            prev = cur;
                        }
                    }
                    double lastX = _o.BeatToX(ordered[^1].Beat), lastY = ValueToY(t, i, ordered[^1].Value);
                    ctx.DrawLine(pen, new Point(lastX, lastY), new Point(w, lastY));       // right end-hold
                    foreach (var p in ordered)
                    {
                        double dx = _o.BeatToX(p.Beat), dy = ValueToY(t, i, p.Value);
                        if (dx < -4 || dx > w + 4) continue;
                        bool ptHover = ReferenceEquals(t, _hoverTrack) && ReferenceEquals(p, _hoverPoint);
                        ctx.DrawEllipse(ptHover ? PlayheadBrush : content, null, new Point(dx, dy), ptHover ? 4.5 : 3.5, ptHover ? 4.5 : 3.5);
                        if (ptHover) ctx.DrawEllipse(null, AutoHoverRing, new Point(dx, dy), 7, 7);
                    }
                }

                // Range selection band (Phase 2): teal wash + edges over the picked span.
                if (ReferenceEquals(_o._autoSelTrack, t) && _o._autoSelEnd - _o._autoSelStart > 1e-6)
                {
                    double sx = _o.BeatToX(_o._autoSelStart), ex = _o.BeatToX(_o._autoSelEnd);
                    double cx0 = Math.Max(0, sx), cx1 = Math.Min(w, ex);
                    if (cx1 > cx0)
                    {
                        double arh = RowH(i);
                        ctx.FillRectangle(AutoSelBand, new Rect(cx0, y, cx1 - cx0, arh));
                        if (sx >= 0) ctx.DrawLine(AutoSelEdge, new Point(sx, y), new Point(sx, y + arh));
                        if (ex <= w) ctx.DrawLine(AutoSelEdge, new Point(ex, y), new Point(ex, y + arh));
                    }
                }

                // Target-selector pill (top-left) — width fits the label, text centred.
                double pillW = PillWidth(t.AutoLabel);
                var pill = PillRect(i, pillW);
                ctx.DrawRectangle(strip, null, pill, 3, 3);
                var ft = new FormattedText(t.AutoLabel, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, Typeface.Default, 9, content);
                using (ctx.PushClip(pill))
                    ctx.DrawText(ft, new Point(pill.X + 6, pill.Y + (PillH - ft.Height) / 2));
            }
        }
    }

    // ---- playhead + loop overlay -----------------------------------------
    // A hit-transparent layer stacked over LaneControl. Only the moving/transport-driven
    // decorations live here so the 30 Hz tick repaints these few strokes instead of the
    // whole clip/waveform surface. It shares the lanes' width/height and scroll/zoom mapping.
    private sealed class LaneOverlayControl : Control
    {
        private readonly ArrangementView _o;
        public LaneOverlayControl(ArrangementView o) { _o = o; }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;

            // Loop region: a faint wash + edge lines spanning all lanes.
            if (_o._loopActive && _o._loopE > _o._loopS)
            {
                double lx0 = _o.BeatToX(_o._loopS), lx1 = _o.BeatToX(_o._loopE);
                if (lx1 > 0 && lx0 < w)
                {
                    double cx0 = Math.Max(0, lx0), cx1 = Math.Min(w, lx1);
                    ctx.FillRectangle(LoopBand, new Rect(cx0, 0, cx1 - cx0, h));
                    if (lx0 >= 0) ctx.DrawLine(LoopEdge, new Point(lx0, 0), new Point(lx0, h));
                    if (lx1 <= w) ctx.DrawLine(LoopEdge, new Point(lx1, 0), new Point(lx1, h));
                }
            }

            // Playhead across all lanes: brass line with a soft glow.
            double px = _o.BeatToX(_o._playheadBeats);
            if (px >= 0 && px <= w)
            {
                ctx.DrawLine(PlayheadGlow, new Point(px, 0), new Point(px, h));
                ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
            }
        }
    }
}
