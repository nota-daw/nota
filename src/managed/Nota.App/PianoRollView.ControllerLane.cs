// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;
using Nota.Application.Editing;

namespace Nota.App;

public sealed partial class PianoRollView
{
    private sealed class ControllerLane : Control
    {
        private readonly PianoRollView _o;
        private List<AutomationPoint>? _working;
        private int _dragIndex = -1;
        private bool _pendingAdd;
        private bool _gestureMoved;
        private bool _isRightDeleting;
        private Point _pressPoint;
        private static readonly IPen LinePen = new Pen(NotaPalette.VizInk, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
        private static readonly IPen NodePen = new Pen(NotaPalette.BgSunken, 1);
        private const double Pad = 8;

        public ControllerLane(PianoRollView o) { _o = o; Cursor = new Cursor(StandardCursorType.Cross); }

        private List<AutomationPoint> Points()
            => (_working ?? (_o.GetMidiControllerPoints?.Invoke(_o.SelectedMidiController) ?? Array.Empty<AutomationPoint>())
                .ToList()).OrderBy(p => p.Beat).ToList();

        private double ValueToY(float value)
        {
            var range = MidiController.Range(_o.SelectedMidiController);
            double unit = (value - range.Min) / Math.Max(1e-6, range.Max - range.Min);
            return Bounds.Height - Pad - Math.Clamp(unit, 0, 1) * Math.Max(1, Bounds.Height - 2 * Pad);
        }

        private float YToValue(double y)
        {
            var range = MidiController.Range(_o.SelectedMidiController);
            double unit = Math.Clamp((Bounds.Height - Pad - y) / Math.Max(1, Bounds.Height - 2 * Pad), 0, 1);
            return (float)(range.Min + unit * (range.Max - range.Min));
        }

        private double EditBeat(double beat)
        {
            beat = Math.Clamp(beat, 0, _o._lengthBeats);
            if (!_o.ParameterSnapOn) return beat;
            var snap = _o.SnapGridState.Snap;
            return Math.Clamp(SnapGrid.SnapBeat(beat, snap.Beats(_o._beatsPerBar), _o._beatsPerBar), 0, _o._lengthBeats);
        }

        private int PointAt(Point p, IReadOnlyList<AutomationPoint> points)
        {
            int found = -1;
            double best = 12; // Increased hit radius for easier point deletion
            for (int i = 0; i < points.Count; i++)
            {
                double dx = Math.Abs(_o.BeatToX(points[i].Beat) - p.X);
                double dy = Math.Abs(ValueToY(points[i].Value) - p.Y);
                double distance = Math.Max(dx, dy);
                if (distance < best) { found = i; best = distance; }
            }
            return found;
        }

        private void Commit(IEnumerable<AutomationPoint> points)
        {
            var sorted = points.OrderBy(p => p.Beat).ToArray();
            _o.SetMidiControllerPoints?.Invoke(_o.SelectedMidiController, sorted);
            _working = null;
            _dragIndex = -1;
            _pendingAdd = false;
            _gestureMoved = false;
            InvalidateVisual();
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0 || h < 12) return;
            _o.EnsurePpb(w);
            ctx.FillRectangle(VelBg, new Rect(0, 0, w, h));
            _o.DrawGridLines(ctx, w, h);

            var gridPen = new Pen(NotaPalette.Wash(NotaPalette.BorderDefault, 0x44), 1);
            double stepVal = _o.SnapGridState.Snap.Beats(_o._beatsPerBar);
            int steps = Math.Clamp((int)Math.Round(4.0 / stepVal), 2, 32);
            double usableH = Math.Max(1, h - 2 * Pad);
            for (int s = 1; s < steps; s++)
            {
                double y = h - Pad - s * (usableH / steps);
                ctx.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            }

            var pts = Points();
            bool isSwitch = MidiController.IsSwitch(_o.SelectedMidiController);
            if (pts.Count == 0)
            {
                var empty = new FormattedText("Double-click to add a controller point", NotaNum.Culture,
                    FlowDirection.LeftToRight, Mono, 10, LabelText);
                ctx.DrawText(empty, new Point(12, Math.Max(22, h * 0.5)));
            }
            else if (isSwitch)
            {
                for (int i = 0; i < pts.Count; i++)
                {
                    double x0 = Math.Clamp(_o.BeatToX(pts[i].Beat), 0, w);
                    double x1 = i + 1 < pts.Count ? Math.Clamp(_o.BeatToX(pts[i + 1].Beat), 0, w) : Math.Clamp(_o.BeatToX(_o._lengthBeats), 0, w);
                    if (x1 <= x0) continue;
                    double y = ValueToY(pts[i].Value);
                    if (pts[i].Value > 0f) ctx.FillRectangle(SpanWash, new Rect(x0, y, x1 - x0, Math.Max(1, h - y)));
                    ctx.DrawLine(LinePen, new Point(x0, y), new Point(x1, y));
                    if (i + 1 < pts.Count)
                        ctx.DrawLine(LinePen, new Point(x1, y), new Point(x1, ValueToY(pts[i + 1].Value)));
                }
            }
            else
            {
                // For continuous controllers, draw the curve connecting all points.
                // With a single point, draw a horizontal line across the clip (constant value).
                var geometry = new StreamGeometry();
                using (var g = geometry.Open())
                {
                    double x0 = _o.BeatToX(pts[0].Beat);
                    double y0 = ValueToY(pts[0].Value);
                    g.BeginFigure(new Point(x0, y0), false);
                    if (pts.Count == 1)
                    {
                        // Single point: constant value across the clip
                        g.LineTo(new Point(_o.BeatToX(_o._lengthBeats), y0));
                    }
                    else
                    {
                        foreach (var p in pts.Skip(1)) g.LineTo(new Point(_o.BeatToX(p.Beat), ValueToY(p.Value)));
                    }
                    g.EndFigure(false);
                }
                using (ctx.PushClip(new Rect(0, 0, w, h))) ctx.DrawGeometry(null, LinePen, geometry);
            }
            for (int i = 0; i < pts.Count; i++)
            {
                double x = _o.BeatToX(pts[i].Beat), y = ValueToY(pts[i].Value);
                if (x < -6 || x > w + 6) continue;
                double r = i == _dragIndex ? 4 : 3;
                ctx.FillRectangle(PointHot, new Rect(x - r, y - r, r * 2, r * 2), 1.5f);
                ctx.DrawRectangle(null, NodePen, new Rect(x - r, y - r, r * 2, r * 2), 1.5);
            }
            string snapLabel = _o.ParameterSnapOn ? "Snap on" : "Snap off";
            var header = new FormattedText($"{MidiController.Name(_o.SelectedMidiController)} · {snapLabel}",
                NotaNum.Culture, FlowDirection.LeftToRight, Mono, 9, LabelText);
            ctx.FillRectangle(VelBg, new Rect(0, 0, header.Width + 10, header.Height + 4));
            ctx.DrawText(header, new Point(5, 2));
            _o.DrawOutsideClip(ctx, w, h);
            if (_o._playheadBeat >= 0)
            {
                double px = _o.BeatToX(_o._playheadBeat);
                if (px >= 0 && px <= w) ctx.DrawLine(PlayheadPen, new Point(px, 0), new Point(px, h));
            }
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var point = e.GetPosition(this);
            if (e.GetCurrentPoint(this).Properties.IsRightButtonPressed)
            {
                _isRightDeleting = true;
                _working = Points();
                int hitIdxRight = PointAt(point, _working);
                if (hitIdxRight >= 0)
                {
                    _working.RemoveAt(hitIdxRight);
                    Commit(_working);
                    _working = Points();
                }
                e.Pointer.Capture(this);
                e.Handled = true;
                return;
            }
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            var ptsLeft = Points();
            int hitIdxLeft = PointAt(point, ptsLeft);
            double beat = EditBeat(_o.XToBeat(point.X));
            float value = YToValue(point.Y);
            if (e.ClickCount >= 2)
            {
                if (hitIdxLeft >= 0) ptsLeft.RemoveAt(hitIdxLeft);
                else ptsLeft.Add(new AutomationPoint(beat, value));
                _pendingAdd = false;
                Commit(ptsLeft);
                e.Handled = true;
                return;
            }
            _working = ptsLeft;
            _pendingAdd = hitIdxLeft < 0;
            _gestureMoved = false;
            _pressPoint = point;
            _dragIndex = hitIdxLeft;
            e.Pointer.Capture(this);
            e.Handled = true;
            InvalidateVisual();
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_isRightDeleting)
            {
                var pts = _working ?? Points();
                int hitIdx = PointAt(p, pts);
                if (hitIdx >= 0)
                {
                    pts.RemoveAt(hitIdx);
                    Commit(pts);
                    _working = Points();
                }
                e.Handled = true;
                return;
            }
            if (_working is null) return;
            if (_pendingAdd && (Math.Abs(p.X - _pressPoint.X) > 3 || Math.Abs(p.Y - _pressPoint.Y) > 3))
            {
                double beat = EditBeat(_o.XToBeat(_pressPoint.X));
                _working.Add(new AutomationPoint(beat, YToValue(_pressPoint.Y)));
                _working = _working.OrderBy(point => point.Beat).ToList();
                _dragIndex = PointAt(_pressPoint, _working);
                _pendingAdd = false;
                _gestureMoved = true;
            }
            if (_dragIndex < 0 || _dragIndex >= _working.Count) return;
            if (Math.Abs(p.X - _pressPoint.X) > 3 || Math.Abs(p.Y - _pressPoint.Y) > 3)
                _gestureMoved = true;
            if (!_gestureMoved) return;
            var item = _working[_dragIndex];
            double editedBeat = EditBeat(_o.XToBeat(p.X));
            double left = _dragIndex > 0 ? _working[_dragIndex - 1].Beat : 0;
            double right = _dragIndex + 1 < _working.Count ? _working[_dragIndex + 1].Beat : _o._lengthBeats;
            item.Beat = Math.Clamp(editedBeat, left, right);
            item.Value = YToValue(p.Y);
            _working[_dragIndex] = item;
            InvalidateVisual();
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_isRightDeleting)
            {
                _isRightDeleting = false;
                _working = null;
                e.Pointer.Capture(null);
                e.Handled = true;
                InvalidateVisual();
                return;
            }
            if (_working is null) return;
            var values = _working;
            bool changed = _gestureMoved && !_pendingAdd && _dragIndex >= 0;
            e.Pointer.Capture(null);
            _pendingAdd = false;
            _gestureMoved = false;
            if (changed) Commit(values);
            else
            {
                _working = null;
                _dragIndex = -1;
                InvalidateVisual();
            }
            e.Handled = true;
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            _isRightDeleting = false;
            _working = null;
            base.OnPointerCaptureLost(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_working is null && !_isRightDeleting) Cursor = new Cursor(StandardCursorType.Cross);
        }
    }

    private sealed class LaneResizeHandle : Control
    {
        private readonly PianoRollView _o;
        private double _originY, _originHeight;
        private bool _dragging;
        public LaneResizeHandle(PianoRollView o) { _o = o; Height = 5; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(NotaPalette.BorderDefault, new Rect(Bounds.Size));
            double cx = Bounds.Width / 2;
            var pen = new Pen(NotaPalette.BorderStrong, 1.2);
            ctx.DrawLine(pen, new Point(cx - 12, Bounds.Height / 2), new Point(cx + 12, Bounds.Height / 2));
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            if (e.ClickCount >= 2)
            {
                _o.ToggleLaneCollapsed();
                e.Handled = true;
                return;
            }
            _dragging = true;
            _originY = e.GetPosition(_o).Y;
            _originHeight = _o._laneCollapsed ? 0 : _o._laneHeight;
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            if (!_dragging) return;
            double currentY = e.GetPosition(_o).Y;
            _o.SetLaneHeight(_originHeight + _originY - currentY);
            e.Handled = true;
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (!_dragging) return;
            _dragging = false;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

}
