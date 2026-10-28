// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Arrangement · automation lane editor (specs/045-return-master-lanes). One editor for every
// row that shows an automation lane — the scrolling track lanes and the pinned return rows in
// the footer — so a return's lane edits exactly like a track's: target pill, points, snap,
// fine drag, curve bends, range selection and its clipboard. The editor owns the gesture state
// and drawing; the control hosting it supplies the rows through IAutoRows.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Presentation;

namespace Nota.App;

public sealed partial class ArrangementView
{
    /// <summary>The rows an <see cref="AutoEditor"/> edits, in its owner's coordinates.</summary>
    private interface IAutoRows
    {
        Control Owner { get; }
        int RowAtY(double y);               // row under y, or -1
        TrackVM TrackAt(int i);
        int IndexOf(TrackVM t);             // -1 when the track is no longer a row here
        double Top(int i);
        double Height(int i);
        bool Editable(int i);               // false for a row with no room for an envelope
        (int lo, int hi) VisibleRange { get; }
    }

    private sealed class AutoEditor
    {
        private readonly ArrangementView _o;
        private readonly IAutoRows _rows;
        private readonly Control _owner;
        public AutoEditor(ArrangementView o, IAutoRows rows) { _o = o; _rows = rows; _owner = rows.Owner; }

        /// <summary>A point drag, curve bend or range selection is live.</summary>
        internal bool IsActive => _autoDrag is not null || _bendLeft is not null || _rangeTrack is not null;

        /// <summary>Drop a live gesture (capture lost / cancel). False when nothing was live.</summary>
        internal bool Cancel()
        {
            if (!IsActive) return false;
            _autoDrag = null; _autoDragTrack = null; _bendLeft = _bendRight = null; _bendTrack = null;
            _rangeTrack = null;
            return true;
        }

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
        internal bool IsEditingPoint => _autoDrag != null || _bendLeft != null;   // M9-C/D: don't clobber a hand-edit

        // Target-selector pill: width fits its label so nothing clips.
        private const double PillH = 16;
        private static double PillWidth(string label)
        {
            var ft = new FormattedText(label, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                Typeface.Default, 9, NotaPalette.TextPrimary);
            return Math.Clamp(Math.Ceiling(ft.Width) + 12, 34, 180);
        }
        private Rect PillRect(int i, double w) => new(4, _rows.Top(i) + 3, w, PillH);
        private double ValueToY(TrackVM t, int row, float v)
        {
            var (min, max) = _o.AutoRange(t);
            double top = _rows.Top(row) + 8, bot = _rows.Top(row) + _rows.Height(row) - 8;
            double frac = max > min ? Math.Clamp((v - min) / (max - min), 0, 1) : 0.5;
            return bot - frac * (bot - top);
        }
        private float YToValue(TrackVM t, int row, double py)
        {
            var (min, max) = _o.AutoRange(t);
            double top = _rows.Top(row) + 8, bot = _rows.Top(row) + _rows.Height(row) - 8;
            double frac = Math.Clamp((bot - py) / (bot - top), 0, 1);
            return (float)(min + frac * (max - min));
        }

        // --- automation editing (M9-A3) ------------------------------------
        internal void AutoPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(_owner);
            var pos = pt.Position;
            int i = _rows.RowAtY(pos.Y);
            if (i < 0) return;
            var t = _rows.TrackAt(i);

            double pillW = PillWidth(t.AutoLabel);
            if (PillRect(i, pillW).Contains(pos)) { _o.ShowAutoTargetMenu(_owner, t); return; }

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
                e.Pointer.Capture(_owner);
                return;
            }

            if (hit is not null)
            {
                // On a point: right-click or double-click removes it; a single press grabs it to drag.
                if (right || dbl) { t.AutoPoints.Remove(hit); _o.CommitAuto(t); _owner.InvalidateVisual(); return; }
                // else: grab the existing point (drag logic below).
            }
            else
            {
                var seg = HitSegment(t, i, pos);
                // Right-click resets a segment's curve, else opens the range menu.
                if (right)
                {
                    if (seg is { } sr) { sr.left.Curve = 0f; _o.CommitAuto(t); _owner.InvalidateVisual(); }
                    else ShowAutoMenu(t, curBeat);
                    return;
                }
                // Alt+drag on a segment bends it (M9-D).
                if (seg is { } sg && (e.KeyModifiers & KeyModifiers.Alt) != 0)
                {
                    _bendLeft = sg.left; _bendRight = sg.right; _bendTrack = t;
                    e.Pointer.Capture(_owner);
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
            e.Pointer.Capture(_owner);
            _owner.InvalidateVisual();
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
            int i = _rows.IndexOf(_bendTrack);
            if (i < 0) return;
            float v0 = _bendLeft.Value, v1 = _bendRight.Value;
            if (Math.Abs(v1 - v0) < 1e-4f) { _bendLeft.Curve = 0f; _owner.InvalidateVisual(); return; }
            double val = YToValue(_bendTrack, i, pos.Y);
            double frac = Math.Clamp((val - v0) / (v1 - v0), 0.02, 0.98);
            // Solve Shape(0.5, curve) == frac  ->  0.5^e = frac  ->  e = ln(frac)/ln(0.5).
            double e = Math.Log(frac) / Math.Log(0.5);
            _bendLeft.Curve = (float)Math.Clamp(-Math.Log2(e) / 4.0, -1.0, 1.0);
            CommitAutoDuringDrag(_bendTrack);   // engine follows the curve bend live
            _owner.InvalidateVisual();
        }

        // Hover feedback (M9-D): highlight the point or segment under the cursor.
        internal void UpdateHover(Point pos)
        {
            AutoPt? pt = null, seg = null; TrackVM? tr = null;
            int i = _rows.RowAtY(pos.Y);
            if (i >= 0)
            {
                var t = _rows.TrackAt(i);
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
                _owner.InvalidateVisual();
            }
        }

        internal void ClearHover()
        {
            if (_hoverPoint is null && _hoverSegLeft is null) return;
            _hoverPoint = null; _hoverSegLeft = null; _hoverTrack = null;
            _owner.InvalidateVisual();
        }

        internal void AutoPointerMoved(PointerEventArgs e)
        {
            if (_rangeTrack is not null)
            {
                double beat = Math.Max(0, _o.Snap(_o._scrollBeats + e.GetPosition(_owner).X / _o._pixelsPerBeat));
                _o.SetAutoSelection(_rangeTrack, _rangeAnchor, beat);
                return;
            }
            if (_bendLeft is not null) { AutoBendMoved(e.GetPosition(_owner)); return; }
            if (_autoDrag is null || _autoDragTrack is null) { UpdateHover(e.GetPosition(_owner)); return; }
            int i = _rows.IndexOf(_autoDragTrack);
            if (i < 0) return;
            var pos = e.GetPosition(_owner);
            // Time: snapped (Alt bypasses), clamped between neighbour points — no reordering (8.2.3).
            double cand = Math.Max(0, _o.SnapMaybe(_o._scrollBeats + pos.X / _o._pixelsPerBeat, e.KeyModifiers));
            _autoDrag.Beat = Math.Clamp(cand, _autoDragLo, _autoDragHi);
            // Value: free, but Shift = fine-mode (quarter sensitivity, anchored at the grab value, 8.2.2).
            float full = YToValue(_autoDragTrack, i, pos.Y);
            _autoDrag.Value = (e.KeyModifiers & KeyModifiers.Shift) != 0
                ? _autoDragStartVal + (full - _autoDragStartVal) * 0.25f
                : full;
            CommitAutoDuringDrag(_autoDragTrack);   // engine follows live (device updates at the playhead)
            _owner.InvalidateVisual();
        }

        // First push of a drag checkpoints undo (CommitAuto); the rest are raw (CommitAutoLive),
        // so a whole drag is one undo step while the device follows in real time.
        private void CommitAutoDuringDrag(TrackVM t)
        {
            if (!_autoLiveStarted) { _o.CommitAuto(t); _autoLiveStarted = true; }
            else _o.CommitAutoLive(t);
        }

        internal void AutoPointerReleased(PointerReleasedEventArgs e)
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
                _owner.InvalidateVisual();
                return;
            }
            if (_autoDrag is null || _autoDragTrack is null) return;
            if (_autoLiveStarted) _o.CommitAutoLive(_autoDragTrack); else _o.CommitAuto(_autoDragTrack);
            _autoLiveStarted = false;
            _autoDrag = null;
            _autoDragTrack = null;
            e.Pointer.Capture(null);
            _owner.InvalidateVisual();
        }

        // Automation-mode right-click: copy/cut/delete the selected time range and paste the
        // range clipboard at the cursor (Phase 2). Copy/Cut/Delete need a selection on this
        // track; Paste needs a filled clipboard.
        private void ShowAutoMenu(TrackVM t, double beat)
        {
            bool hasSel = _o.HasAutoSelection && ReferenceEquals(_o._autoSelTrack, t);
            var flyout = new MenuFlyout();
            var copy = MenuKit.Item("Copy automation", GlyphKind.Copy, () => _o.CopyAutoSelection(), MenuKit.CopyKey, hasSel);
            var cut = MenuKit.Item("Cut automation", GlyphKind.Cut, () => _o.CutAutoSelection(), MenuKit.CutKey, hasSel);
            var paste = MenuKit.Item("Paste automation", GlyphKind.Paste, () => _o.PasteAutoAt(t, _o.Snap(beat)), MenuKit.PasteKey, _o.HasAutoClip);
            var del = MenuKit.Danger("Delete automation", GlyphKind.Trash, () => _o.DeleteAutoSelection(), MenuKit.DeleteKey, hasSel);
            MenuKit.AddGroups(flyout.Items, new Control[] { copy, cut, paste }, new Control[] { del });
            flyout.ShowAt(_owner, showAtPointer: true);
        }

        private static readonly IPen AutoCurvePen = new Pen(NotaPalette.VizInk, 1.6);

        internal void DrawAutomation(DrawingContext ctx, double w)
        {
            var (rLo, rHi) = _rows.VisibleRange;
            for (int i = rLo; i <= rHi; i++)
            {
                var t = _rows.TrackAt(i);
                double y = _rows.Top(i);
                ctx.FillRectangle(AutoScrim, new Rect(0, y, w, _rows.Height(i)));
                if (!_rows.Editable(i)) continue;   // a slim group bar has no room for an envelope

                // Automation: the curve and its points in neutral viz ink; only the
                // point or segment under the pointer takes the accent.
                var content = NotaPalette.VizInk;
                var pen = AutoCurvePen;

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
                        double arh = _rows.Height(i);
                        ctx.FillRectangle(AutoSelBand, new Rect(cx0, y, cx1 - cx0, arh));
                        if (sx >= 0) ctx.DrawLine(AutoSelEdge, new Point(sx, y), new Point(sx, y + arh));
                        if (ex <= w) ctx.DrawLine(AutoSelEdge, new Point(ex, y), new Point(ex, y + arh));
                    }
                }

                // Target-selector pill (top-left) — width fits the label, text centred.
                double pillW = PillWidth(t.AutoLabel);
                var pill = PillRect(i, pillW);
                ctx.DrawRectangle(NotaPalette.SurfaceRaised, null, pill, NotaRadius.ControlValue, NotaRadius.ControlValue);
                var ft = new FormattedText(t.AutoLabel, CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight, NotaFonts.SansMedium, 9, NotaPalette.TextPrimary);
                using (ctx.PushClip(pill))
                    ctx.DrawText(ft, new Point(pill.X + 6, pill.Y + (PillH - ft.Height) / 2));
            }
        }
    }
}
