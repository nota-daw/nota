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
    private sealed partial class LaneControl : Control, IAutoRows, IValueEntryHost
    {
        private readonly ArrangementView _o;
        private readonly AutoEditor _auto;   // automation mode (M9-A3), shared with the footer's return rows
        public LaneControl(ArrangementView o)
        {
            _o = o;
            ClipToBounds = true;
            _auto = new AutoEditor(o, this);
            ValueEntry.AttachHost(this, menu: false);   // the right-press below shows the menu
        }

        // ⌥-click an audio clip's ADSR handle to type its stage (specs/035); one engine edit each.
        // The clip is found again by id on every read: a commit reloads the lane's clips.
        public IReadOnlyList<ValueField>? FieldsAt(Point p)
        {
            if (_o._automationMode || HitAdsr(p) is not { } ah) return null;
            int trackId = ah.track.Id, index = ah.clip.ClipIndex;
            ClipVM? Clip() => _o._tracks.FirstOrDefault(t => t.Id == trackId)?.Clips.FirstOrDefault(c => c.ClipIndex == index);
            var fs = ClipAdsrKit.Fields(ah.handle, () => Clip()?.Adsr ?? ah.clip.Adsr,
                a => { if (Clip() is { } c) CommitAdsr(trackId, c, a); }, ah.clip.LengthBeats, () => _o._engine?.Bpm ?? 120);
            _adsrFields = (trackId, index, ah.handle, fs);
            return fs;
        }
        private (int track, int clip, AdsrHandle handle, IReadOnlyList<ValueField> fields)? _adsrFields;   // the handle last asked about

        public Rect AnchorOf(ValueField field)
        {
            if (_adsrFields is not { } af) return default;
            int row = _o._tracks.FindIndex(t => t.Id == af.track);
            if (row < 0 || _o._tracks[row].Clips.FirstOrDefault(c => c.ClipIndex == af.clip) is not { } clip) return default;
            var f = AdsrFrameOf(clip, row); var a = clip.Adsr.Fit(clip.LengthBeats);
            var at = af.handle switch
            {
                AdsrHandle.Attack => ClipAdsrKit.AttackPt(f, a),
                AdsrHandle.Release => ClipAdsrKit.ReleasePt(f, a),
                _ => ClipAdsrKit.DecayPt(f, a),
            };
            return ValueEntry.Near(at, Bounds.Size);
        }

        public IReadOnlyList<ValueField> AllFields => _adsrFields?.fields ?? [];

        // The scrolling track rows, as the automation editor sees them.
        Control IAutoRows.Owner => this;
        int IAutoRows.RowAtY(double y) => _o.RowAtY(y);
        TrackVM IAutoRows.TrackAt(int i) => _o._tracks[i];
        int IAutoRows.IndexOf(TrackVM t) => _o._tracks.IndexOf(t);
        double IAutoRows.Top(int i) => _o.RowTop(i);
        double IAutoRows.Height(int i) => _o.ClipAreaHeightAt(i);
        bool IAutoRows.Editable(int i) => !_o.IsSlimRowAt(i);   // a slim group bar has no room for an envelope
        (int lo, int hi) IAutoRows.VisibleRange => _o.VisibleRowRange();

        public bool IsEditingPoint => _auto.IsEditingPoint;   // M9-C/D: don't clobber a hand-edit

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
        private bool _rpWarped;
        private int _rpTrack, _rpClip;
        private float[]? _rpDetail;                // the lane's visible material at one bucket per pixel (#299)
        private int _rpDetailCount;
        private double _rpDetailM0, _rpDetailM1;
        private const double EdgePx = 6;

        // ADSR handles on audio clips (shown on hover / selection). A drag previews on the
        // ClipVM and commits one engine edit (one undo step) on release.
        private AdsrHandle _adsrDrag;
        private ClipVM? _adsrClip;
        private int _adsrTrackId;
        private ClipAdsr _adsrStart;
        private double _adsrAnchorBeat, _adsrAnchorLevel;
        private Point _adsrPress;
        private int _adsrHoverTrack = -1, _adsrHoverClip = -1;
        private AdsrHandle _adsrHoverHandle;
        private static readonly Cursor HandCursor = new(StandardCursorType.Hand);

        // The clip's ADSR frame: its span sideways, the content area (under the colour band) for level.
        private AdsrFrame AdsrFrameOf(ClipVM c, int row)
        {
            double top = RowTop(row) + 2 + BandH, bottom = RowTop(row) + RowH(row) - 2;
            return new AdsrFrame(_o.BeatToX(c.StartBeat), _o.BeatToX(c.StartBeat + c.LengthBeats),
                                 top + 4, bottom - 3, c.LengthBeats);
        }

        // The ADSR handle under `p` on an audio clip (lanes view, not automation).
        private (TrackVM track, ClipVM clip, int row, AdsrHandle handle)? HitAdsr(Point p)
        {
            int row = _o.RowAtY(p.Y);
            if (row < 0 || _o._tracks[row].IsInstrument || _o._tracks[row].IsGroup) return null;
            var t = _o._tracks[row];
            foreach (var c in t.Clips)
            {
                if (c.IsMidi) continue;
                var h = ClipAdsrKit.Hit(p, AdsrFrameOf(c, row), c.Adsr);
                if (h != AdsrHandle.None) return (t, c, row, h);
            }
            return null;
        }

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
        // Held below the last track (#301): each source track gets a new track like it, shown
        // as ghost rows under the lanes; the ids are the sources, top to bottom.
        private List<int>? _newTrackSources;

        // Audio clip → instrument (see ArrangementView.ClipDrag): the grabbed clip, the
        // sample-taking instrument row it's held over (-1 none: a plain move), and the press
        // a drag out of the arrangement starts its sample drag from.
        private ClipVM? _grabClip;
        private int _grabTrackId;
        private int _loadRow = -1;
        private PointerPressedEventArgs? _pressArgs;
        private bool _dragOutTried;
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

        /// <summary>A clip drag / trim, marquee, time-range or envelope edit is in progress:
        /// Follow must not scroll the lanes out from under it (#105).</summary>
        public bool IsGesturing => _drag != Drag.None || _marquee is not null || _auto.IsActive || _takeSwipe is not null;
        private double RowTop(int i) => _o.RowTop(i);
        private double RowH(int i) => _o.ClipAreaHeightAt(i);
        protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
            => _o.HandleLaneWheel(e, e.GetPosition(this).X);

        private (TrackVM track, ClipVM clip)? HitTest(Point p, out double beat)
        {
            beat = _o._scrollBeats + p.X / _o._pixelsPerBeat;
            int ti = _o.RowAtY(p.Y);
            if (ti < 0 || p.Y >= RowTop(ti) + RowH(ti)) return null;   // below the clip area: take lanes
            var t = _o._tracks[ti];
            foreach (var c in t.Clips)
                if (beat >= c.StartBeat && beat <= c.StartBeat + c.LengthBeats)
                    return (t, c);
            return null;
        }

        // Row seams (specs/015): the thin band on a track row's bottom edge resizes the row —
        // checked before clips, trims and automation so the band always means "resize".
        private static readonly Cursor RowResizeCursor = new(StandardCursorType.SizeNorthSouth);
        private bool _rowSeamCursorOn;

        private bool RowSeamPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            if (!pt.Properties.IsLeftButtonPressed) return false;
            int row = _o.RowSeamAtY(pt.Position.Y);
            if (row < 0) return false;
            int trackId = _o._tracks[row].Id;
            e.Handled = true;
            if (e.ClickCount == 2) { _o.ResetRowHeight(trackId); return true; }
            _o.BeginRowResize(trackId, pt.Position.Y, e.KeyModifiers);
            e.Pointer.Capture(this);
            return true;
        }

        // Hover over a row seam owns the cursor; leaving it hands the cursor back.
        private bool RowSeamHover(Point pos)
        {
            bool on = _o.RowSeamAtY(pos.Y) >= 0;
            if (on == _rowSeamCursorOn) return on;
            _rowSeamCursorOn = on;
            _resizeCursorOn = false;   // the clip-edge cursor state restarts from Arrow
            Cursor = on ? RowResizeCursor : ArrowCursor;
            return on;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (RowSeamPressed(e)) return;
            if (TakeLanePressed(e)) return;
            if (_o._automationMode) { _auto.AutoPointerPressed(e); return; }
            var pt = e.GetCurrentPoint(this);
            var hit = HitTest(pt.Position, out double beat);

            if (pt.Properties.IsRightButtonPressed)
            {
                if (FieldsAt(pt.Position) is { Count: > 0 } adsr) ValueEntry.ShowMenu(this, adsr);
                else if (hit is { } h) ShowClipMenu(h.track.Id, h.clip, beat);
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

            // ADSR handle: double-click resets its stage, a press starts a handle drag. Checked
            // before the edge trims so the top corners belong to attack/release (as in Ableton).
            if (HitAdsr(pt.Position) is { } ah)
            {
                if (e.ClickCount == 2)
                {
                    CommitAdsr(ah.track.Id, ah.clip, ClipAdsrKit.Reset(ah.handle, ah.clip.Adsr.Fit(ah.clip.LengthBeats)));
                    return;
                }
                _o.Select(ah.track.Id, ah.clip.ClipIndex);
                _adsrDrag = ah.handle;
                _adsrClip = ah.clip;
                _adsrTrackId = ah.track.Id;
                _adsrStart = ah.clip.Adsr.Fit(ah.clip.LengthBeats);
                (_adsrAnchorBeat, _adsrAnchorLevel) = ClipAdsrKit.Anchor(ah.handle, _adsrStart, ah.clip.LengthBeats);
                _adsrPress = pt.Position;
                _lastPos = pt.Position;
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
                _o.RestoreNarrowed(hh.track.Id, hh.clip.ClipIndex);
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
            _pressArgs = e;
            _dragOutTried = false;
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
            if (_o.IsRowResizing) { _o.UpdateRowResize(e.GetPosition(this).Y); return; }
            if (_takeSwipe is not null) { TakeSwipeMoved(e); return; }
            if (e.GetCurrentPoint(this).Properties is { IsLeftButtonPressed: false, IsRightButtonPressed: false }
                && RowSeamHover(e.GetPosition(this))) return;
            if (_o._automationMode) { _auto.AutoPointerMoved(e); return; }
            var pos = e.GetPosition(this);
            _lastPos = pos;
            _lastMods = e.KeyModifiers;

            if (_adsrDrag != AdsrHandle.None && _adsrClip is { } ac)
            {
                double dx = pos.X - _adsrPress.X, dy = pos.Y - _adsrPress.Y;
                if (_adsrDrag == AdsrHandle.AttackOrSustain)
                {
                    if (Math.Abs(dx) < 3 && Math.Abs(dy) < 3) return;
                    _adsrDrag = ClipAdsrKit.Resolve(_adsrDrag, dx, dy);
                    (_adsrAnchorBeat, _adsrAnchorLevel) = ClipAdsrKit.Anchor(_adsrDrag, _adsrStart, ac.LengthBeats);
                }
                int row = _o._tracks.FindIndex(t => t.Id == _adsrTrackId);
                if (row < 0) return;
                var f = AdsrFrameOf(ac, row);
                double beat = _adsrAnchorBeat + dx / Math.Max(1e-9, _o._pixelsPerBeat);
                double level = _adsrAnchorLevel - dy / Math.Max(1, f.Bottom - f.Top);
                ac.Adsr = ClipAdsrKit.Drag(_adsrDrag, _adsrStart, beat, level, ac.LengthBeats);
                InvalidateVisual();
                return;
            }

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
                    _grabClip = _pendingClip; _grabTrackId = _pendingTrackId;
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

            if (_drag == Drag.None) { if (!UpdateAdsrHover(pos)) UpdateEdgeHover(pos); return; }
            // An audio clip pulled out of the arrangement becomes a sample drag.
            if (_drag == Drag.Move && _groupMove is not null && !_dragOutTried && _grabClip is { IsMidi: false }
                && e.GetPosition(_o) is var op && (op.X < 0 || op.Y < 0 || op.X > _o.Bounds.Width || op.Y > _o.Bounds.Height))
            {
                _ = DragOutAsync();
                return;
            }
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
                // Over a sample-taking instrument row an audio clip loads there on release
                // instead of moving, so the group rests at home meanwhile.
                int hoverRow = _o.RowAtY(pos.Y);
                SetLoadRow(_grabClip is { IsMidi: false } && hoverRow != _grabRow && _o.TakesSample(hoverRow) ? hoverRow : -1);
                if (_loadRow >= 0)
                {
                    foreach (var m in _groupMove) m.vm.StartBeat = m.origStart;
                    _moveRowDelta = 0;
                }
                SetNewTrackRows(_loadRow < 0 && _o._tracks.Count > 0 && pos.Y >= _o.RowsHeight);
                if (_newTrackSources is not null) _moveRowDelta = 0;
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
            if (_o.IsRowResizing) { e.Pointer.Capture(null); _o.EndRowResize(); return; }
            if (_takeSwipe is not null) { TakeSwipeReleased(e); return; }
            if (_o._automationMode) { _auto.AutoPointerReleased(e); return; }
            var eng = _o._engine;

            if (_adsrDrag != AdsrHandle.None)
            {
                var clip = _adsrClip;
                var start = _adsrStart;
                _adsrDrag = AdsrHandle.None; _adsrClip = null;
                e.Pointer.Capture(null);
                if (clip is not null && !clip.Adsr.Equals(start)) CommitAdsr(_adsrTrackId, clip, clip.Adsr);
                else InvalidateVisual();
                return;
            }

            // A press that never crossed the drag threshold is a click on a clip: select it,
            // or narrow a multi-selection down to just it (req 2.2/2.12). No model change.
            if (_pending)
            {
                _pending = false;
                e.Pointer.Capture(null);
                if (!_pendingWasSelected || _o.SelectionCount > 1)
                {
                    // The first click of a double-click narrows too; keep what it narrowed so the
                    // double-click can open the whole selection together (specs/029).
                    _o.RememberNarrowed(_pendingTrackId, _pendingClipIndex, _pendingWasSelected);
                    _o.Select(_pendingTrackId, _pendingClipIndex);
                }
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
                        int row = _o.RowAtY(_marqueePress.Y);
                        if (row >= 0 && row < _o._tracks.Count) _o.FocusTrackId = _o._tracks[row].Id;
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

            if (_drag == Drag.Move && _groupMove is not null && _loadRow >= 0)
            {
                int trackId = _o._tracks[_loadRow].Id;
                var grabbed = _grabClip;
                SetLoadRow(-1);
                _groupMove = null; _drag = Drag.None; _moveRowDelta = 0;
                e.Pointer.Capture(null);
                InvalidateVisual();
                if (eng is not null && grabbed is not null && ClipSample(eng, grabbed) is { } item)
                    _o.LoadClipIntoTrack(item, trackId);
                return;
            }

            if (_drag == Drag.Move && _groupMove is not null && _newTrackSources is { } sources)
            {
                var clips = _groupMove.Select(m => (m.track, m.clip, m.vm.StartBeat)).ToList();
                SetNewTrackRows(false);
                _groupMove = null; _drag = Drag.None; _moveRowDelta = 0;
                e.Pointer.Capture(null);
                _o.MoveClipsToNewTracks(sources, clips);
                return;
            }

            if (_drag == Drag.Move && _groupMove is not null)
            {
                int rowDelta = _moveRowDelta;
                // Nothing actually moved (snap kept every start, no row change) → don't touch
                // the model, so a jiggle-and-release doesn't create a no-op undo step.
                bool moved = rowDelta != 0 || _groupMove.Any(m => Math.Abs(m.vm.StartBeat - m.origStart) > 1e-9);
                bool committed = false;
                Nota.Application.Editing.ClipBlockMove? move = null;
                if (eng is not null && moved)
                {
                    // One atomic group move: moving the clips one by one let an early clip
                    // carve a not-yet-moved member of the group (clips vanished / got cut).
                    var moves = _groupMove.Select(m =>
                    {
                        int destRow = m.origRow + rowDelta;
                        int destTrack = (rowDelta != 0 && destRow >= 0 && destRow < _o._tracks.Count)
                            ? _o._tracks[destRow].Id : m.track;
                        return (m.track, m.clip, destTrack, m.vm.StartBeat);
                    }).ToArray();
                    move = _o.MoveBlock(moves);
                    committed = move is not null;
                    if (committed && eng.LastMoveKeptDeviceAutomation())   // explicit hint: only Volume/Pan followed across tracks (req 8.3.4)
                        _o.StatusMessage?.Invoke("Moved across tracks — device automation stayed on the source");
                }
                _groupMove = null; _drag = Drag.None; _moveRowDelta = 0;
                e.Pointer.Capture(null);
                if (moved)
                {
                    _o.Refresh();   // also restores the preview if the engine rejected the move
                    if (committed) _o.ReselectPlaced();   // a move can change clip indices
                    if (move is not null) _o.RaiseClipsMoved(move);   // so can open editors' clips
                    if (committed && rowDelta != 0) _o.AnnounceSelectedTrack();   // the destination track is now the active one
                }
                else InvalidateVisual();
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
                          || _auto.IsActive
                          || _adsrDrag != AdsrHandle.None
                          || _takeSwipe is not null;
            if (!active) return false;
            _pending = false; _drag = Drag.None; _groupMove = null; _moveRowDelta = 0; _dragClip = null; _rpPeaks = null;
            _takeSwipe = null; _takeMoved = false;   // an abandoned take swipe never comps
            _marqueeArmed = false; _rangeActive = false; _marquee = null; _shiftClipTrack = -1; _shiftClipIndex = -1;
            _auto.Cancel();
            _adsrDrag = AdsrHandle.None; _adsrClip = null;
            SetLoadRow(-1);
            SetNewTrackRows(false);
            _autoScroll?.Stop();
            SetResizeCursor(false);
            _o.Refresh();   // reload authoritative clip/automation positions, discarding the preview
            return true;
        }

        private void SetLoadRow(int row)
        {
            if (row == _loadRow) return;
            _loadRow = row;
            _o.SetDropTrack(row);
            _o.DwellOverTrack(row >= 0 ? _o._tracks[row].Id : -1);
            if (row >= 0) _o.StatusMessage?.Invoke($"Release to load the clip into {_o._tracks[row].Name}");
        }

        // The grabbed clip's audio as a sample file (null + a status line when it can't be).
        private BrowserItem? ClipSample(IAudioEngine eng, ClipVM clip)
        {
            string name = clip.Name.Length > 0 ? clip.Name : _o._tracks.Find(t => t.Id == _grabTrackId)?.Name ?? "";
            try { return ClipAudioExport.AsSample(eng, _grabTrackId, clip.ClipIndex, name); }
            catch (Exception ex) { _o.StatusMessage?.Invoke($"Couldn't take the clip's audio: {ex.Message}"); return null; }
        }

        // Hands the move over to a sample drag: the clip snaps back home and its audio rides
        // the browser payload, so any sample drop target takes it.
        private async System.Threading.Tasks.Task DragOutAsync()
        {
            _dragOutTried = true;
            if (_pressArgs is not { } press || _grabClip is not { } clip || _o._engine is not { } eng) return;
            if (ClipSample(eng, clip) is not { } item) return;
            CancelGesture();
            press.Pointer.Capture(null);
            try { await BrowserView.DragItemAsync(press, item, fromClip: true); }
            finally
            {
                _pressArgs = null;
                _o.SetDropTrack(-1);
                _o.DwellOverTrack(-1);
            }
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

        protected override void OnPointerExited(PointerEventArgs e) { _auto.ClearHover(); ClearEdgeHover(); ClearAdsrHover(); base.OnPointerExited(e); }

        // One engine edit (one undo step); an open clip editor re-reads it, the lane reloads.
        private void CommitAdsr(int trackId, ClipVM clip, ClipAdsr adsr)
        {
            if (_o._engine is not { } eng) return;
            eng.SetClipAdsr(trackId, clip.ClipIndex, adsr);
            _o.RaiseClipGeometryChanged(trackId, clip.ClipIndex);
            _o.Refresh();
        }

        // Hover over audio clips: which clip shows its ADSR handles, and which handle is hot.
        // Returns true while a handle is under the pointer (it then owns the cursor).
        private bool UpdateAdsrHover(Point pos)
        {
            int ht = -1, hc = -1; var hh = AdsrHandle.None;
            if (HitAdsr(pos) is { } a) { ht = a.track.Id; hc = a.clip.ClipIndex; hh = a.handle; }
            else if (HitTest(pos, out _) is { } c && !c.clip.IsMidi) { ht = c.track.Id; hc = c.clip.ClipIndex; }
            if (ht != _adsrHoverTrack || hc != _adsrHoverClip || hh != _adsrHoverHandle)
            {
                _adsrHoverTrack = ht; _adsrHoverClip = hc; _adsrHoverHandle = hh;
                InvalidateVisual();
            }
            if (hh == AdsrHandle.None)
            {
                if (ReferenceEquals(Cursor, HandCursor)) Cursor = ArrowCursor;
                return false;
            }
            ClearEdgeHover();
            Cursor = HandCursor;
            return true;
        }

        private void ClearAdsrHover()
        {
            if (ReferenceEquals(Cursor, HandCursor)) Cursor = ArrowCursor;
            if (_adsrHoverTrack == -1 && _adsrHoverHandle == AdsrHandle.None) return;
            _adsrHoverTrack = -1; _adsrHoverClip = -1; _adsrHoverHandle = AdsrHandle.None;
            InvalidateVisual();
        }

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

        // "Paste bounced audio" onto this track at `at`, or null when there's nothing to paste
        // or the track can't take it (only audio tracks can).
        private MenuItem? PasteBouncedItem(int trackId, double at)
        {
            if (!_o.CanPasteBouncedOnto(trackId)) return null;
            var mi = new MenuItem { Header = "Paste bounced audio", Icon = MenuKit.Icon(GlyphKind.Paste), InputGesture = MenuKit.PasteBouncedKey };
            mi.Click += (_, _) => _o.RequestPasteBounced(trackId, at);
            return mi;
        }

        // Right-click on empty lane space: paste the clipboard clip (or the bounced range) here,
        // insert a MIDI clip on an instrument track, and — when the click lands inside the time
        // selection — copy / cut / duplicate / consolidate it. Always opens, so the menu never
        // silently fails to appear.
        private void ShowLaneMenu(Point pos, double beat)
        {
            int ti = _o.RowAtY(pos.Y);
            if (ti < 0) return;
            var track = _o._tracks[ti];
            int trackId = track.Id;
            bool inRange = _o.TimeSelectionCovers(trackId, beat);
            double at = _o.Snap(beat);
            var flyout = new MenuFlyout();
            var clipboard = MenuKit.ClipboardGroup(
                inRange ? () => _o.CopyTimeSelection() : null,
                inRange ? () => _o.CutTimeSelection() : null,
                () => _o.PasteClipboardAt(trackId, at),
                inRange ? () => _o.DuplicateTimeSelection() : null,
                _o.HasClipClipboard).ToList();
            if (PasteBouncedItem(trackId, at) is { } bounced) clipboard.Insert(inRange ? 3 : 1, bounced);   // right after Paste
            foreach (var mi in clipboard) flyout.Items.Add(mi);
            if (track.IsInstrument)
            {
                var insert = new MenuItem { Header = "Insert MIDI clip", Icon = MenuKit.Icon(GlyphKind.Plus) };
                insert.Click += (_, _) => _o.AddMidiClipAt(trackId, beat);
                flyout.Items.Add(new Separator());
                flyout.Items.Add(insert);
            }
            if (inRange)
            {
                var consolidate = new MenuItem { Header = "Consolidate selection", Icon = MenuKit.Icon(GlyphKind.Consolidate), InputGesture = MenuKit.ConsolidateKey };
                consolidate.Click += (_, _) => _o.ConsolidateSelection();
                var dupTime = new MenuItem { Header = "Duplicate time", Icon = MenuKit.Icon(GlyphKind.Duplicate), InputGesture = MenuKit.DuplicateTimeKey };
                dupTime.Click += (_, _) => _o.DuplicateTimeInsert();
                var silence = new MenuItem { Header = "Insert silence", Icon = MenuKit.Icon(GlyphKind.Plus), InputGesture = MenuKit.InsertSilenceKey };
                silence.Click += (_, _) => _o.InsertSilenceAtTimeSelection();
                flyout.Items.Add(new Separator());
                flyout.Items.Add(consolidate);
                flyout.Items.Add(dupTime);
                flyout.Items.Add(silence);
            }
            flyout.ShowAt(this, showAtPointer: true);
        }

        private void ShowClipMenu(int trackId, ClipVM clip, double beat)
            => BuildClipMenu(trackId, clip, beat).ShowAt(this, showAtPointer: true);

        // The clip menu (spec 034): header (the clip, or the count of a multi-selection) →
        // rename → edits → audio conversions → Session → clipboard → delete.
        internal MenuFlyout BuildClipMenu(int trackId, ClipVM clip, double beat)
        {
            double at = _o.Snap(beat);
            int idx = clip.ClipIndex;
            // Copy/Cut/Duplicate/Delete act on the whole selection when the clicked clip is
            // part of a multi-selection; otherwise they collapse to just this clip.
            void EnsureSelected() { if (!_o.IsSelected(trackId, idx)) _o.Select(trackId, idx); }
            bool inGroup = _o.IsSelected(trackId, idx) && _o.Selection.Count > 1;
            var flyout = new MenuFlyout();

            var track = _o._tracks.FirstOrDefault(v => v.Id == trackId);
            var header = inGroup
                ? MenuKit.Header($"{_o.Selection.Count} clips", null, "")
                : MenuKit.Header(clip.Name.Length > 0 ? clip.Name : "Clip",
                                 track is null ? null : TrackBrush(track.ColorIndex), clip.IsMidi ? "MIDI clip" : "Audio clip");

            var rename = MenuKit.Item("Rename…", GlyphKind.Edit,
                () => PromptRename(clip.Name, s => { _o._engine?.SetClipName(trackId, idx, s); _o.Refresh(); }));

            var edits = new List<Control>
            {
                MenuKit.Item("Split here", GlyphKind.Split, () => { _o._engine?.SplitClip(trackId, idx, at); _o.Refresh(); }, MenuKit.SplitKey),
            };
            // Reverse (audio only): non-destructive, so the label reflects the clicked clip.
            if (!clip.IsMidi && _o._engine is { } rev && rev.TryGetAudioClipInfo(trackId, idx, out var rai))
                edits.Add(MenuKit.Item(rai.Reversed != 0 ? "Un-reverse" : "Reverse", GlyphKind.Reverse,
                    () => { EnsureSelected(); _o.ToggleSelectedClipsReverse(); }));
            // Consolidate: a right-click inside the time selection acts on that range; otherwise
            // on the multi-selection's span, or just this clip (bakes its edits into one clip).
            bool inRange = _o.TimeSelectionCovers(trackId, beat);
            edits.Add(MenuKit.Item(inRange ? "Consolidate selection" : "Consolidate", GlyphKind.Consolidate,
                () => { if (!inRange) EnsureSelected(); _o.ConsolidateSelection(); }, MenuKit.ConsolidateKey));
            // Clip deactivate (key 0): the label reflects the clicked clip's current state.
            edits.Add(MenuKit.Item(clip.Active ? "Deactivate" : "Activate", GlyphKind.Bypass,
                () => { EnsureSelected(); _o.ToggleSelectedClipsActive(); }, MenuKit.ActivateKey));
            // Loop the current selection (or just this clip if nothing is selected).
            edits.Add(MenuKit.Item("Loop selection", GlyphKind.Cycle, () =>
            {
                if (!_o.LoopSelection())
                    _o.SetLoopRegion(clip.StartBeat, clip.StartBeat + clip.LengthBeats);
            }, MenuKit.LoopKey));
            // Several MIDI clips selected: open them together in the Clip editor (specs/029).
            if (clip.IsMidi && inGroup && _o.SelectedMidiClips().Count >= 2)
                edits.Add(MenuKit.Item("Edit selected clips together", GlyphKind.Edit, () => _o.EditSelectedClipsRequested?.Invoke()));

            // Audio clip → stems (AI model) and → MIDI: Convert / Slice to a new MIDI track.
            var audio = new List<Control>();
            if (!clip.IsMidi)
            {
                audio.Add(MenuKit.Item("Separate stems…", GlyphKind.Stems, () => _o.SeparateStemsRequested?.Invoke(trackId, idx)));
                var convert = MenuKit.Item("Convert", GlyphKind.Arrow);
                MenuItem ConvItem(string header, ClipConvertMode mode)
                    => MenuKit.Item(header, null, () => _o.ConvertClipRequested?.Invoke(trackId, idx, mode));
                // Melody / Harmony are only offered on the AI model: the DSP fallback's results
                // are much worse and the user couldn't tell which one ran. Without the model the
                // menu offers to download it instead (the menu is built on open, so installing
                // it brings the items back without a restart).
                bool canTranscribe = _o.CanTranscribe?.Invoke() != false;
                if (canTranscribe)
                {
                    convert.Items.Add(ConvItem("Melody to new MIDI track", ClipConvertMode.Melody));
                    convert.Items.Add(ConvItem("Harmony to new MIDI track", ClipConvertMode.Harmony));
                }
                convert.Items.Add(ConvItem("Drums to new MIDI track", ClipConvertMode.Drums));
                convert.Items.Add(ConvItem("Slice to new MIDI track", ClipConvertMode.Slice));
                if (!canTranscribe)
                {
                    convert.Items.Add(new Separator());
                    convert.Items.Add(MenuKit.Item("Get the AI model for melody and harmony…", null, () => _o.TranscriptionModelRequested?.Invoke()));
                }
                audio.Add(convert);
            }

            // M5-6: copy the clip selection into session slots — the first scene row where it
            // fits (⌘⇧C), or from a chosen scene down.
            var session = new List<Control>();
            if (_o._engine is { } eng)
            {
                session.Add(MenuKit.Item("Copy to Session", GlyphKind.Grid, () => { EnsureSelected(); _o.CopySelectionToSession(); }, MenuKit.CopyToOtherViewKey));
                var toScene = MenuKit.Item("Copy to scene", GlyphKind.Grid);
                for (int s = 0; s < eng.SceneCount; s++)
                {
                    int sc = s;
                    string name = eng.GetSceneName(sc);
                    toScene.Items.Add(MenuKit.Item(name.Length > 0 ? $"{sc + 1} · {name}" : $"Scene {sc + 1}", null,
                        () => { EnsureSelected(); _o.CopySelectionToSession(sc); }));
                }
                session.Add(toScene);
            }

            // Inside the time selection, Copy / Cut / Duplicate act on the range (like ⌘C / ⌘X / ⌘D).
            var clipboard = MenuKit.ClipboardGroup(
                inRange ? () => _o.CopyTimeSelection() : () => { EnsureSelected(); _o.CopySelectedClip(); },
                inRange ? () => _o.CutTimeSelection() : () => { EnsureSelected(); _o.CutSelectedClip(); },
                () => _o.PasteClipboardAt(trackId, at),
                inRange ? () => _o.DuplicateTimeSelection() : () => { EnsureSelected(); _o.DuplicateSelectedClip(); },
                _o.HasClipClipboard).Cast<Control>().ToList();
            if (PasteBouncedItem(trackId, at) is { } bounced) clipboard.Insert(3, bounced);

            var del = MenuKit.Danger("Delete", GlyphKind.Trash, () =>
            {
                if (inGroup) _o.DeleteSelectedClips();
                else { _o._engine?.DeleteClip(trackId, idx); _o.Select(-1, -1); _o.Refresh(); }
            }, MenuKit.DeleteKey);

            MenuKit.AddGroups(flyout.Items, new Control[] { header }, new Control[] { rename }, edits, audio, session,
                              clipboard, new Control[] { del });
            return flyout;
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

            // Browser drag-over: wash the lane the drop would land on (its edge is drawn
            // last, over the clips, so a full lane still reads as the target).
            if (_o.DropTrackIndex >= 0 && _o.DropTrackIndex < _o._tracks.Count)
                ctx.FillRectangle(DropWash, new Rect(0, RowTop(_o.DropTrackIndex), w, RowH(_o.DropTrackIndex)));

            // Vertical grid: the snap grid's lines at this zoom — what you see is what you snap to.
            _o.DrawGrid(ctx, w, h);

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
                // One pitch scale for the whole track, so a clip an octave up reads higher
                // than its neighbour instead of every clip auto-fitting to its own notes.
                var pitches = TrackPitchRange(_o._tracks[i].Clips);
                // The recording take's row (#56): the scale includes the keys being held, so a
                // stored note doesn't jump when the held one lands; new notes animate in.
                var rp = _o.RecordPreview;
                bool take = rp is { Active: true, SessionScene: < 0 } && rp.TrackId == tid;
                if (take) pitches = WithHeld(pitches, rp!.Held);
                // A track still being filled by a background import reads as translucent.
                using (ctx.PushOpacity(_o.IsProcessingTrack(tid) ? 0.5 : 1.0))
                    foreach (var c in _o._tracks[i].Clips)
                    {
                        if (IsDraggingCrossTrack(tid, c.ClipIndex)) continue;
                        // Highlight the edge a resize would grab (hover), or the edge actively being trimmed.
                        var edgeHi = Drag.None;
                        if (_hoverEdge != Drag.None && _hoverEdgeTrack == tid && _hoverEdgeClip == c.ClipIndex) edgeHi = _hoverEdge;
                        else if ((_drag == Drag.TrimL || _drag == Drag.TrimR) && _dragTrackId == tid && _dragClipIndex == c.ClipIndex) edgeHi = _drag;
                        DrawClipBody(ctx, _o._tracks[i].ColorIndex, _o._tracks[i].Name, c, y, rh, _o.IsSelected(tid, c.ClipIndex), edgeHi, pitches, tid, i,
                                     take ? rp : null);
                    }
                if (take) DrawHeldNotes(ctx, rp!, _o._tracks[i].ColorIndex, y, rh, w, pitches);
                if (_o.TakesOpen(_o._tracks[i])) DrawTakeLanes(ctx, _o._tracks[i], y + rh, w);
                // Imports still decoding: a translucent placeholder whose waveform fills in.
                using (ctx.PushOpacity(0.5))
                    foreach (var c in _o.PendingClipsFor(tid))
                        DrawClipBody(ctx, _o._tracks[i].ColorIndex, _o._tracks[i].Name, c, y, rh, false, pitches: pitches);
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
                            var ft = new FormattedText("Rec", CultureInfo.InvariantCulture,
                                FlowDirection.LeftToRight, NotaFonts.SansBold, 9, RecText);
                            Glyph.Draw(ctx, GlyphKind.Record, new Rect(rr.X + 5, rr.Y + 3 + (ft.Height - 7) / 2, 7, 7), RecText);
                            ctx.DrawText(ft, new Point(rr.X + 16, rr.Y + 3));
                        }
                        break;
                    }
            }

            // Automation overlay (M9-A3): scrim + envelope + target pill per row.
            if (_o._automationMode) _auto.DrawAutomation(ctx, w);

            // Cross-track drag: the dragged clips are hidden on their source lanes
            // (above) and previewed translucently on the destination lane here.
            if (_groupMove is not null && _moveRowDelta != 0)
                using (ctx.PushOpacity(0.5))
                    foreach (var m in _groupMove)
                    {
                        int destRow = m.origRow + _moveRowDelta;
                        if (destRow < 0 || destRow >= _o._tracks.Count) continue;
                        var dest = _o._tracks[destRow];
                        var pitches = TrackPitchRange(dest.Clips.Append(m.vm));
                        DrawClipBody(ctx, dest.ColorIndex, dest.Name, m.vm, RowTop(destRow), RowH(destRow), true, pitches: pitches);
                    }

            if (_groupMove is not null && _newTrackSources is { } srcs) DrawNewTrackGhosts(ctx, w, srcs);

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

            if (_o.DropTrackIndex >= 0 && _o.DropTrackIndex < _o._tracks.Count)
                ctx.DrawRectangle(null, DropEdgePen, new Rect(1, RowTop(_o.DropTrackIndex) + 1, w - 2, RowH(_o.DropTrackIndex) - 2));

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

            // ADSR drag readout: the stage being edited, in time and dB.
            if (_adsrDrag is not (AdsrHandle.None or AdsrHandle.AttackOrSustain) && _adsrClip is { } adc)
            {
                var ft = new FormattedText(ClipAdsrKit.Readout(_adsrDrag, adc.Adsr, _o._engine?.Bpm ?? 120), CultureInfo.InvariantCulture,
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

        // Lowest/highest pitch across a track's MIDI clips, or null when there are no notes.
        private static (int lo, int hi)? TrackPitchRange(IEnumerable<ClipVM> clips)
        {
            int lo = int.MaxValue, hi = int.MinValue;
            foreach (var c in clips)
                if (c.IsMidi && c.Notes is { } notes)
                    foreach (var n in notes) { lo = Math.Min(lo, n.Pitch); hi = Math.Max(hi, n.Pitch); }
            return lo <= hi ? (lo, hi) : null;
        }

        // A track's pitch range widened to the keys held into its recording take.
        private static (int lo, int hi)? WithHeld((int lo, int hi)? range, ReadOnlySpan<NotaRecordHeldNote> held)
        {
            if (held.IsEmpty) return range;
            int lo = range?.lo ?? int.MaxValue, hi = range?.hi ?? int.MinValue;
            foreach (var h in held) { lo = Math.Min(lo, h.Pitch); hi = Math.Max(hi, h.Pitch); }
            return (lo, hi);
        }

        // A note settling in (#56): the accent fading into the note's own ink, ease-out.
        private static IBrush AppearBrush(IBrush ink, double p)
            => RecordPreview.AppearBrush((ink as ISolidColorBrush)?.Color ?? NotaPalette.Accent.Color, p);

        // Notes still animating in, drawn one by one after the clip's batched geometry.
        private readonly List<(Rect r, double p)> _appearing = new();

        // The recording take's held notes (#56): from where each key went down to the capture
        // clock, on the take row's content area and pitch scale. Keys held since before the
        // take's clip (the count-in) sit on a faint extension of the take, not on bare lane.
        private void DrawHeldNotes(DrawingContext ctx, RecordPreview rp, int colorIndex, double y, double rowH,
                                   double w, (int lo, int hi)? pitches)
        {
            var held = rp.Held;
            if (held.IsEmpty || pitches is not { } pr) return;
            var content = new Rect(0, y + 2 + BandH, w, Math.Max(0, rowH - 4 - BandH));
            if (content.Height <= 0) return;
            double first = double.MaxValue;
            foreach (var h in held) first = Math.Min(first, h.StartBeat);
            double clipStart = rp.TakeClipIndex >= 0 ? rp.TakeClipStart : rp.NowBeat;
            if (first < clipStart)
            {
                double ex0 = _o.BeatToX(first), ex1 = _o.BeatToX(clipStart);
                var (_, _, _, body) = ClipColors(colorIndex);
                using (ctx.PushClip(new Rect(0, y + 2, w, Math.Max(0, rowH - 4))))
                using (ctx.PushOpacity(0.45))
                    ctx.DrawRectangle(body, null, new Rect(ex0, y + 2, Math.Max(2, ex1 - ex0), rowH - 4), NotaRadius.ClipValue, NotaRadius.ClipValue);
            }
            // Under the take's title band, as the stored notes are (the band covers them there).
            double top = y + 2 + (rowH - 4 > TitleBandH * 2.5 ? TitleBandH : BandH);
            using var _ = ctx.PushClip(new Rect(0, top, w, Math.Max(0, y + rowH - 2 - top)));
            int span = Math.Max(1, pr.hi - pr.lo + 1);
            double step = Math.Min(content.Height / span, 4), noteH = Math.Max(1, step);
            var ink = ClipInk(colorIndex);
            foreach (var h in held)
            {
                double x0 = _o.BeatToX(h.StartBeat), x1 = _o.BeatToX(Math.Max(h.StartBeat, rp.NowBeat));
                if (x1 < 0 || x0 > w) continue;
                double ny = Math.Max(content.Y, content.Bottom - (h.Pitch - pr.lo + 1) * step);
                var nr = new Rect(x0, ny, Math.Max(2, x1 - x0), noteH);
                double p = rp.Progress(h.Pitch, h.StartBeat);
                ctx.FillRectangle(p < 1 ? AppearBrush(ink, p) : ink, nr);
            }
        }

        private void DrawNotes(DrawingContext ctx, ClipVM c, Rect r, IBrush brush, (int lo, int hi)? pitches = null,
                               RecordPreview? appear = null)
        {
            if (c.Notes is null || c.Notes.Length == 0 || c.LengthBeats <= 0 || r.Height <= 0) return;
            // Auto-range to the pitches actually present on the track (all its clips share
            // one scale, so relative register reads across clips): a fixed 5-octave range
            // needs far more height than one lane row, so mapping it with a min
            // bar height pushes high notes above the clip and into the row above.
            var (lo, hi) = pitches ?? TrackPitchRange(new[] { c })!.Value;
            int span = Math.Max(1, hi - lo + 1);
            // Row step may drop below a pixel on a wide track range; the bar keeps 1px so
            // it stays visible, and the top note still lands inside the clip.
            double step = Math.Min(r.Height / span, 4), noteH = Math.Max(1, step);
            using var _ = ctx.PushClip(r);   // never bleed into adjacent lanes
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                foreach (var n in c.Notes)
                {
                    double nx = r.X + (n.StartBeat / c.LengthBeats) * r.Width;
                    double nw = Math.Max(2, (n.LengthBeats / c.LengthBeats) * r.Width);
                    double ny = Math.Max(r.Y, r.Bottom - (n.Pitch - lo + 1) * step);
                    double ww = Math.Min(nw, r.Right - nx);
                    if (ww <= 0) continue;
                    double p = appear?.Progress(n.Pitch, c.StartBeat + n.StartBeat) ?? 1;
                    if (p < 1) _appearing.Add((new Rect(nx, ny, ww, noteH), p));
                    else AddRect(g, nx, ny, ww, noteH);
                }
            ctx.DrawGeometry(brush, null, geo);
            foreach (var (ar, p) in _appearing) ctx.FillRectangle(AppearBrush(brush, p), ar);
            _appearing.Clear();
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
                    // Input over 0 dBFS reads past ±1: pin it to the box's edges, as the take's
                    // own waveform is once it lands (#208).
                    float min = Math.Clamp(_recPeaks[i * 2], -1f, 1f);
                    float max = Math.Clamp(_recPeaks[i * 2 + 1], -1f, 1f);
                    AddRect(g, fx, mid - max * amp, 1, Math.Max(1, (max - min) * amp));
                }
            using (ctx.PushClip(r))   // and never paint over the tracks around it
                ctx.DrawGeometry(RecWave, null, geo);
        }

        private bool IsDraggingCrossTrack(int track, int clip)
            => _groupMove is not null && (_moveRowDelta != 0 || _newTrackSources is not null)
               && _groupMove.Any(m => m.track == track && m.clip == clip);

        // Shows or drops the new-track ghost rows below the lanes (#301), growing the lanes
        // so the rows can be scrolled to and drawn.
        private void SetNewTrackRows(bool on)
        {
            List<int>? sources = on && _groupMove is not null
                ? _groupMove.OrderBy(m => m.origRow).Select(m => m.track).Distinct().ToList()
                : null;
            if (sources is { Count: 0 }) sources = null;
            if ((sources?.Count ?? 0) == (_newTrackSources?.Count ?? 0)) { _newTrackSources = sources; return; }
            _newTrackSources = sources;
            Height = Math.Max(DefaultRowHeight, _o.RowsHeight) + (sources?.Count ?? 0) * DefaultRowHeight;
            _o._overlay.Height = Height;
        }

        // The rows a drop below the last track creates: a drop ring per new track, with the
        // dragged clips previewed in it at their new beats.
        private void DrawNewTrackGhosts(DrawingContext ctx, double w, List<int> sources)
        {
            double top = _o.RowsHeight;
            for (int k = 0; k < sources.Count; k++)
            {
                var r = new Rect(0, top + k * DefaultRowHeight, w, DefaultRowHeight);
                ctx.FillRectangle(DropWash, r);
                ctx.DrawRectangle(null, DropEdgePen, r.Deflate(1));
                var ft = new FormattedText("New track", CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                    Typeface.Default, 11, RulerText);
                ctx.DrawText(ft, new Point(Math.Max(8, w - ft.Width - 12), r.Y + 4));   // clear of clips at the song start
            }
            using (ctx.PushOpacity(0.5))
                foreach (var m in _groupMove!)
                {
                    int k = sources.IndexOf(m.track);
                    var src = _o._tracks.FirstOrDefault(t => t.Id == m.track);
                    if (k < 0 || src is null) continue;
                    DrawClipBody(ctx, src.ColorIndex, src.Name, m.vm, top + k * DefaultRowHeight, DefaultRowHeight, true,
                                 pitches: TrackPitchRange(new[] { m.vm }));
                }
        }

        // Draws one clip (fill + border + name strip + notes/waveform) at row-y with
        // the given track colour. Reused for the live cross-track drag preview.
        // Group lane preview: collapsed → each descendant clip as a mini-clip stacked into its
        // own sub-lane (child colour), so starts/ends read at a glance; expanded → nothing (the
        // children are right below and show their own clips).
        private void DrawGroupLane(DrawingContext ctx, ArrangementView.TrackVM g, double y, double rowH, double w, bool collapsed)
        {
            if (!collapsed || g.GroupMini.Count == 0) return;
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

        private void DrawClipBody(DrawingContext ctx, int colorIndex, string label, ClipVM c, double y,
                                  double rowH, bool selected, Drag edgeHi = Drag.None, (int lo, int hi)? pitches = null,
                                  int trackId = -1, int row = -1, RecordPreview? appear = null)
        {
            double w = Bounds.Width;
            double x0 = _o.BeatToX(c.StartBeat);
            double x1 = _o.BeatToX(c.StartBeat + c.LengthBeats);
            if (x1 < 0 || x0 > w || c.LengthBeats <= 0) return;
            var (_, _, _, body) = ClipColors(colorIndex);
            // The clip: a flat body in the track colour, radius 2, no outline; its notes and
            // waveform in a deeper shade of it; selection adds a 1px accent edge.
            var content = ClipInk(colorIndex);
            var rect = new Rect(x0, y + 2, Math.Max(2, x1 - x0), rowH - 4);
            ctx.DrawRectangle(body, selected ? ClipSelBorder : null, rect, NotaRadius.ClipValue, NotaRadius.ClipValue);

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
            if (c.IsMidi) DrawNotes(ctx, c, contentRect, content, pitches, appear);
            else DrawWaveform(ctx, c, contentRect, content, trackId);

            // The title band: the track colour mixed 30% toward black across the top, the
            // accent when the clip is selected; the name sits on it in derived ink. It is drawn
            // over the content — the content area and the ADSR frame keep their geometry.
            // A row too short for a band keeps the 2px family marker.
            bool tall = rect.Height > TitleBandH * 2.5;
            var band = selected ? NotaPalette.Accent : ClipBand(colorIndex);
            if (rect.Width > 3)
            {
                using var bc = ctx.PushClip(new RoundedRect(rect, NotaRadius.ClipValue));
                ctx.FillRectangle(band, new Rect(rect.X, rect.Y, rect.Width, tall ? TitleBandH : BandH + 1));
            }

            if (tall && rect.Width > 24 && ShowsLabel(c))
            {
                // The clip's own name (set via its menu) wins; otherwise the track name.
                string text = string.IsNullOrEmpty(c.Name) ? label : c.Name;
                var ft = new FormattedText(text, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, NotaFonts.SansSemiBold, 9.5, NotaPalette.InkOn(band))
                { MaxTextWidth = Math.Max(8, rect.Width - 10), Trimming = TextTrimming.CharacterEllipsis };
                using var _ = ctx.PushClip(rect);
                ctx.DrawText(ft, new Point(rect.X + 5, rect.Y + (TitleBandH - ft.Height) / 2));
            }

            if (!c.IsMidi && row >= 0) DrawAdsr(ctx, c, trackId, row, selected, content, NotaPalette.BgSunken);

            // Deactivated clip (key 0): grey it out with a dark scrim so it reads as "off"
            // while still showing its content/geometry. Inset so the (selection) border stays.
            if (!c.Active)
                ctx.DrawRectangle(InactiveVeil, null, rect.Deflate(1), NotaRadius.ClipValue, NotaRadius.ClipValue);
        }

        private const double BandH = 2;         // the content area starts under this (ADSR frame, notes)
        private const double TitleBandH = 12;   // the drawn title band over the top of the content

        // The clip's ADSR: the curve whenever it shapes the clip, the handles while the clip is
        // hovered, selected or being edited. Drawn in the clip's own colour; the hot handle is brass.
        private void DrawAdsr(DrawingContext ctx, ClipVM c, int trackId, int row, bool selected, IBrush ink, IBrush body)
        {
            bool editing = _adsrDrag != AdsrHandle.None && ReferenceEquals(c, _adsrClip);
            bool hovered = _adsrHoverTrack == trackId && _adsrHoverClip == c.ClipIndex;
            bool handles = editing || hovered || selected;
            if (c.Adsr.IsIdentity && !handles) return;
            var f = AdsrFrameOf(c, row);
            if (f.Right - f.Left < 12) return;
            using var _ = ctx.PushClip(new Rect(f.Left, RowTop(row) + 2, f.Right - f.Left, RowH(row) - 4));
            var a = c.Adsr.Fit(c.LengthBeats);
            if (!a.IsIdentity) ClipAdsrKit.DrawCurve(ctx, f, a, new Pen(ink, 1.2));
            if (!handles) return;
            var hot = editing ? _adsrDrag : hovered ? _adsrHoverHandle : AdsrHandle.None;
            ClipAdsrKit.DrawHandles(ctx, f, a, hot, ink, NotaPalette.Accent, new Pen(body, 1));
        }

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
            _rpPeaks = null; _rpCount = 0; _rpDetail = null;
            (_rpTrack, _rpClip) = (trackId, clipIndex);
            var eng = _o._engine;
            if (eng is null || !eng.TryGetAudioClipInfo(trackId, clipIndex, out var ai)) return;
            const int buckets = 2048;
            var peaks = new float[buckets * 2];
            if (ai.WarpEnabled != 0 && ai.WarpBeats > 0)
            {
                int n = eng.GetClipWarpFullPeaks(trackId, clipIndex, peaks, buckets);
                if (n <= 0) return;
                _rpCount = n; _rpPeaks = peaks; _rpWarped = true;
                _rpMTotal = ai.WarpBeats;
                _rpS0 = ai.WarpPlayStart;
                _rpS1 = ai.WarpPlayEnd > 0 ? ai.WarpPlayEnd : ai.WarpBeats;
            }
            else
            {
                if (ai.SampleId == 0 || !eng.TryGetSampleInfo(ai.SampleId, out var si) || si.Frames <= 0) return;
                int n = eng.GetClipSourcePeaks(trackId, clipIndex, peaks, buckets);
                if (n <= 0) return;
                _rpCount = n; _rpPeaks = peaks; _rpWarped = false;
                _rpMTotal = si.Frames;
                _rpS0 = ai.SourceOffsetFrames;
                double len = ai.LengthFrames > 0 ? ai.LengthFrames : si.Frames - ai.SourceOffsetFrames;
                _rpS1 = ai.SourceOffsetFrames + len;
            }
        }

        // Draw the clip being resized from its full-material peaks, mapping each timeline
        // beat through the committed window so the SAME audio stays anchored and dragging
        // reveals/hides real content (point 2) rather than squashing the window peaks.
        private void DrawResizeWaveform(DrawingContext ctx, Rect r, IBrush brush, float gain)
        {
            double mid = r.Y + r.Height / 2, amp = r.Height / 2 - 1;
            double ppb = _o._pixelsPerBeat, scroll = _o._scrollBeats;
            double rate = (_rpS1 - _rpS0) / _origLen;   // material units per timeline beat (committed)
            double vx0 = Math.Max(r.X, 0), vx1 = Math.Min(r.Right, Bounds.Width);
            double MaterialAt(double px) => _rpS0 + (scroll + px / ppb - _origStart) * rate;
            // The whole-material overview, or — when the lane shows it wider than it has buckets —
            // the material under the whole lane at one bucket per pixel, fetched once per drag (#299).
            float[] pk = _rpPeaks!;
            int count = _rpCount;
            double m0 = 0, m1 = _rpMTotal;
            double lm0 = Math.Clamp(MaterialAt(0), 0, _rpMTotal), lm1 = Math.Clamp(MaterialAt(Bounds.Width), 0, _rpMTotal);
            if (lm1 > lm0 && (lm1 - lm0) / _rpMTotal * _rpCount * 1.5 < Bounds.Width && _o._engine is { } eng)
            {
                int n = Math.Clamp((int)Math.Ceiling(Bounds.Width), 1, 8192);
                if (_rpDetail is null || _rpDetailCount <= 0 || _rpDetailM0 != lm0 || _rpDetailM1 != lm1)
                {
                    _rpDetail ??= new float[8192 * 2];
                    _rpDetailCount = _rpWarped
                        ? eng.GetClipWarpPeaksRange(_rpTrack, _rpClip, lm0, lm1, _rpDetail, n)
                        : eng.GetClipSourcePeaksRange(_rpTrack, _rpClip, lm0, lm1, _rpDetail, n);
                    (_rpDetailM0, _rpDetailM1) = (lm0, lm1);
                }
                if (_rpDetailCount > 0) (pk, count, m0, m1) = (_rpDetail, _rpDetailCount, lm0, lm1);
            }
            var geo = new StreamGeometry();
            using (var g = geo.Open())
                ForEachWaveColumn(r.X, vx0, vx1, (cx0, cx1, x) =>
                {
                    // Material span under this column, anchored to the committed window.
                    double s0 = MaterialAt(cx0), s1 = MaterialAt(cx1);
                    double f0 = Math.Clamp((s0 - m0) / (m1 - m0), 0, 1), f1 = Math.Clamp((s1 - m0) / (m1 - m0), 0, 1);
                    if (s1 <= 0 || s0 >= _rpMTotal) return;   // past the material's ends: nothing to draw
                    int b0 = Math.Clamp((int)(f0 * count), 0, count - 1);
                    int b1 = Math.Clamp((int)Math.Ceiling(f1 * count), b0 + 1, count);
                    float min = 1f, max = -1f;
                    for (int b = b0; b < b1; b++) { min = Math.Min(min, pk[b * 2]); max = Math.Max(max, pk[b * 2 + 1]); }
                    if (min > max) return;
                    min = Math.Clamp(min * gain, -1f, 1f); max = Math.Clamp(max * gain, -1f, 1f);
                    AddWaveColumn(g, x, mid, amp, min, max);
                });
            ctx.DrawGeometry(brush, null, geo);
        }

        // Bars: one 1px stroke every BarPitch px, laid on a grid anchored to the clip's left
        // edge (so the strokes travel with the clip instead of shimmering while it scrolls),
        // each summarising the whole pitch it stands for. Solid: one column per pixel.
        private const double BarPitch = 3;

        private void ForEachWaveColumn(double clipX, double vx0, double vx1, Action<double, double, double> column)
        {
            if (_o.Waveform == WaveformStyle.Solid)
            {
                for (double px = vx0; px < vx1; px += 1) column(px, px + 1, px);
                return;
            }
            double k0 = Math.Floor((vx0 - clipX) / BarPitch);
            for (double cx = clipX + k0 * BarPitch; cx < vx1; cx += BarPitch)
            {
                double c0 = Math.Max(cx, clipX), c1 = cx + BarPitch;
                double x = cx + 1;   // the stroke sits in the middle pixel of its pitch
                if (x < vx0 || x >= vx1) continue;
                column(c0, c1, x);
            }
        }

        // Solid keeps the true min/max outline; Bars mirror the larger excursion about the
        // centre line, so each stroke reads as one level.
        private void AddWaveColumn(StreamGeometryContext g, double x, double mid, double amp, float min, float max)
        {
            if (_o.Waveform == WaveformStyle.Solid)
            {
                AddRect(g, x, mid - max * amp, 1, Math.Max(1, (max - min) * amp));
                return;
            }
            double a = Math.Max(Math.Abs(min), Math.Abs(max)) * amp;
            AddRect(g, x, mid - a, 1, Math.Max(1, 2 * a));
        }

        private void DrawWaveform(DrawingContext ctx, ClipVM c, Rect r, IBrush brush, int trackId = -1)
        {
            // While resizing THIS clip, show the real audio revealed/hidden by the drag.
            if ((_drag == Drag.TrimL || _drag == Drag.TrimR) && ReferenceEquals(c, _dragClip)
                && _rpPeaks is not null && _rpCount > 0 && _rpMTotal > 0 && _origLen > 0 && r.Height > 0 && r.Width > 0)
            {
                DrawResizeWaveform(ctx, r, brush, c.Gain);
                return;
            }
            if (c.Peaks is null || c.PeakCount <= 0 || r.Height <= 0 || r.Width <= 0) return;
            double mid = r.Y + r.Height / 2;
            double amp = r.Height / 2 - 1;
            // One column per stroke (see ForEachWaveColumn), aggregating every peak bucket that
            // falls in the span it stands for, so nothing between strokes is skipped.
            double vx0 = Math.Max(r.X, 0), vx1 = Math.Min(r.Right, Bounds.Width);
            if (vx1 <= vx0) return;
            // The whole-clip overview, or — when the clip is drawn wider than it has buckets —
            // peaks for just the visible slice at one bucket per pixel (#299).
            float[] pk = c.Peaks;
            int count = c.PeakCount;
            double fa = 0, fb = 1;
            if (r.Width > c.PeakCount * 1.5 && EnsureDetailPeaks(c, trackId, (vx0 - r.X) / r.Width, (vx1 - r.X) / r.Width,
                    (int)Math.Ceiling(vx1 - vx0), r.Width))
            {
                (pk, count, fa, fb) = (c.DetailPeaks!, c.DetailCount, c.DetailF0, c.DetailF1);
            }
            double span = (fb - fa) * r.Width;
            var adsr = c.Adsr.Fit(c.LengthBeats);
            bool shaped = !adsr.IsIdentity;
            var geo = new StreamGeometry();
            using (var gc = geo.Open())
                ForEachWaveColumn(r.X, vx0, vx1, (cx0, cx1, x) =>
                {
                    double ox = r.X + fa * r.Width;
                    int b0 = Math.Clamp((int)((cx0 - ox) / span * count), 0, count - 1);
                    int b1 = Math.Clamp((int)Math.Ceiling((cx1 - ox) / span * count), b0 + 1, count);
                    float min = 1f, max = -1f;
                    for (int b = b0; b < b1; b++) { min = Math.Min(min, pk[b * 2]); max = Math.Max(max, pk[b * 2 + 1]); }
                    if (min > max) return;
                    // Clip gain (and the ADSR shape) scales the drawn waveform, matching the clip editor.
                    float g = c.Gain;
                    if (shaped) g *= (float)adsr.GainAt((x + 0.5 - r.X) / r.Width * c.LengthBeats, c.LengthBeats);
                    min = Math.Clamp(min * g, -1f, 1f); max = Math.Clamp(max * g, -1f, 1f);
                    AddWaveColumn(gc, x, mid, amp, min, max);
                });
            ctx.DrawGeometry(brush, null, geo);
        }

        // Fill c.DetailPeaks for the played-length fractions [f0, f1] at n buckets, unless what
        // it holds already matches to within half a pixel. Played order, like Peaks: a reversed
        // clip reads its window back to front. False when the engine has nothing for it.
        private bool EnsureDetailPeaks(ClipVM c, int trackId, double f0, double f1, int n, double widthPx)
        {
            n = Math.Clamp(n, 1, 8192);
            if (c.DetailPeaks is not null && c.DetailCount == n
                && Math.Abs(c.DetailF0 - f0) * widthPx < 0.5 && Math.Abs(c.DetailF1 - f1) * widthPx < 0.5)
                return true;
            var eng = _o._engine;
            if (eng is null || trackId <= 0 || !eng.TryGetAudioClipInfo(trackId, c.ClipIndex, out var ai)) return false;
            bool rev = ai.Reversed != 0;
            double a = rev ? 1 - f1 : f0, b = rev ? 1 - f0 : f1;   // file-order fractions of the window
            var buf = c.DetailPeaks is { } old && old.Length >= n * 2 ? old : new float[n * 2];
            int got;
            if (ai.WarpEnabled != 0 && ai.WarpBeats > 0)
            {
                double ps = ai.WarpPlayStart, pe = ai.WarpPlayEnd > 0 ? ai.WarpPlayEnd : ai.WarpBeats;
                got = eng.GetClipWarpPeaksRange(trackId, c.ClipIndex, ps + a * (pe - ps), ps + b * (pe - ps), buf, n);
            }
            else
            {
                double len = ai.LengthFrames;
                if (len <= 0 && eng.TryGetSampleInfo(ai.SampleId, out var si)) len = si.Frames - ai.SourceOffsetFrames;
                if (len <= 0) return false;
                got = eng.GetClipSourcePeaksRange(trackId, c.ClipIndex, ai.SourceOffsetFrames + a * len, ai.SourceOffsetFrames + b * len, buf, n);
            }
            if (got <= 0) return false;
            if (rev)
                for (int i = 0, j = got - 1; i < j; i++, j--)
                {
                    (buf[i * 2], buf[j * 2]) = (buf[j * 2], buf[i * 2]);
                    (buf[i * 2 + 1], buf[j * 2 + 1]) = (buf[j * 2 + 1], buf[i * 2 + 1]);
                }
            (c.DetailPeaks, c.DetailCount, c.DetailF0, c.DetailF1) = (buf, got, f0, f1);
            return true;
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
                ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
            }
        }
    }
}
