// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The piano roll's expression lanes: the lane under the notes switches between velocity and a
// note's recorded MPE — Bend (semitones), Pressure and Slide (0..1). In an MPE lane each note's
// curve is drawn over its own time span; the selected notes' curves are editable:
//   drag on a note's span   draws the curve freehand (⌥ draws a straight line), replacing the
//                           stretch you drew over — on every selected note under the pointer;
//   drag a point            moves it (in time between its neighbours, and in value);
//   double-click            adds a point, or deletes the one under the pointer;
//   right-click             Clear <lane> / Clear All Expression on the selected notes.
// A click on a note's span with nothing selected there selects it (the highest under the
// pointer; ⇧ adds). Curves are immutable in the engine's store, so an edit stores a new curve
// and repoints the note (one undo step per gesture, like the velocity lane).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed partial class PianoRollView
{
    /// <summary>Stores an edited expression curve and returns its id (0 for none). Set by the
    /// host; without it the MPE lanes are view-only.</summary>
    public Func<NotaExprPoint[], int>? CreateExpression { get; set; }

    internal enum LaneMode { Velocity, Bend, Pressure, Slide }

    private const double ExprH = 112;   // the MPE lanes are taller: a curve needs the room
    private readonly ExpressionLane _expr;
    private readonly LaneTabs _laneTabs;
    private readonly Grid _laneRow;
    private LaneMode _laneMode;

    private static readonly IPen CurvePen = new Pen(NotaPalette.AccentBright, 1.5, lineCap: PenLineCap.Round, lineJoin: PenLineJoin.Round);
    private static readonly IPen NeutralPen = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x70), 1, new DashStyle(new double[] { 3, 3 }, 0));
    private static readonly IPen ZeroPen = new Pen(NotaPalette.BorderStrong, 1);
    private static readonly IBrush SpanWash = NotaPalette.Wash(NotaPalette.AccentBright, 0x14);
    private static readonly IBrush PointFill = NotaPalette.AccentBright;
    private static readonly IBrush PointHot = NotaPalette.AccentPale;
    private static readonly IBrush TabActive = NotaPalette.AccentBright;

    internal LaneMode Lane => _laneMode;

    internal void SetLaneMode(LaneMode m)
    {
        if (m == _laneMode) return;
        _laneMode = m;
        _velHost.Child = m == LaneMode.Velocity ? _vel : _expr;
        _laneRow.Height = m == LaneMode.Velocity ? VelH : ExprH;
        Invalidate();
        _laneTabs.InvalidateVisual();
    }

    private static NoteExpressionDim DimOf(LaneMode m) => m switch
    {
        LaneMode.Bend => NoteExpressionDim.Bend,
        LaneMode.Pressure => NoteExpressionDim.Pressure,
        _ => NoteExpressionDim.Slide,
    };

    private static float Neutral(NoteExpressionDim d) => d == NoteExpressionDim.Slide ? 0.5f : 0f;

    /// <summary>One dimension of a note's curve, (note-relative beat, value), sorted.</summary>
    private List<(float Beat, float Value)> CurveOf(NotaNote n, NoteExpressionDim d)
    {
        var list = new List<(float, float)>();
        foreach (var p in ExprOf(n.ExprId)) if (p.Dim == d) list.Add((p.Beat, p.Value));
        return list;
    }

    /// <summary>The note with dimension <paramref name="d"/> replaced by <paramref name="curve"/>
    /// (the other dimensions kept): a new curve in the store, the note repointed at it.</summary>
    private NotaNote WithCurve(NotaNote n, NoteExpressionDim d, IReadOnlyList<(float Beat, float Value)>? curve)
    {
        if (CreateExpression is null) return n;
        var pts = ExprOf(n.ExprId).Where(p => p.Dim != d).ToList();
        if (curve is not null) foreach (var (b, v) in curve) pts.Add(new NotaExprPoint(d, b, v));
        var arr = pts.ToArray();
        int id = arr.Length == 0 ? 0 : CreateExpression(arr);
        if (id > 0) _exprCache[id] = arr;   // known already: skip the round trip
        n.ExprId = id;
        return n;
    }

    // Ramer–Douglas–Peucker: a freehand stroke keeps the points that shape it.
    private static List<(float Beat, float Value)> Thin(List<(float Beat, float Value)> p, float tol)
    {
        if (p.Count <= 2) return p;
        var keep = new bool[p.Count];
        keep[0] = keep[^1] = true;
        var stack = new Stack<(int, int)>();
        stack.Push((0, p.Count - 1));
        while (stack.Count > 0)
        {
            var (a, b) = stack.Pop();
            if (b <= a + 1) continue;
            float worst = -1; int at = a;
            for (int i = a + 1; i < b; i++)
            {
                float span = p[b].Beat - p[a].Beat;
                float t = span > 0 ? (p[i].Beat - p[a].Beat) / span : 0;
                float dev = Math.Abs(p[i].Value - (p[a].Value + (p[b].Value - p[a].Value) * t));
                if (dev > worst) { worst = dev; at = i; }
            }
            if (worst > tol) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
        }
        var outp = new List<(float, float)>();
        for (int i = 0; i < p.Count; i++) if (keep[i]) outp.Add(p[i]);
        return outp;
    }

    private static float ValueAt(List<(float Beat, float Value)> c, float beat, float neutral)
    {
        if (c.Count == 0) return neutral;
        if (beat <= c[0].Beat) return c[0].Value;
        if (beat >= c[^1].Beat) return c[^1].Value;
        for (int i = 1; i < c.Count; i++)
            if (beat <= c[i].Beat)
            {
                var (b0, v0) = c[i - 1]; var (b1, v1) = c[i];
                return b1 > b0 ? v0 + (v1 - v0) * (beat - b0) / (b1 - b0) : v1;
            }
        return c[^1].Value;
    }

    // ---- the gutter: VEL · BEND · PRES · SLIDE -----------------------------------------------

    private sealed class LaneTabs : Control
    {
        private static readonly (LaneMode Mode, string Label)[] Tabs =
            { (LaneMode.Velocity, "VEL"), (LaneMode.Bend, "BEND"), (LaneMode.Pressure, "PRES"), (LaneMode.Slide, "SLIDE") };
        private const double Top = 6, RowStep = 15;
        private readonly PianoRollView _o;
        private int _hover = -1;
        public LaneTabs(PianoRollView o) { _o = o; Cursor = new Cursor(StandardCursorType.Hand); }

        public override void Render(DrawingContext ctx)
        {
            ctx.FillRectangle(Brushes.Transparent, new Rect(Bounds.Size));
            for (int i = 0; i < Tabs.Length; i++)
            {
                bool on = Tabs[i].Mode == _o._laneMode;
                var ink = on ? TabActive : i == _hover ? KeyLabelHot : LabelText;
                var ft = new FormattedText(Tabs[i].Label, NotaNum.Culture, FlowDirection.LeftToRight,
                    new Typeface(NotaFonts.Mono.FontFamily, FontStyle.Normal, FontWeight.Bold), 9, ink);
                double y = Top + i * RowStep;
                ctx.DrawText(ft, new Point((Bounds.Width - ft.Width) / 2, y));
                if (on) ctx.FillRectangle(TabActive, new Rect(0, y + 1, 2, ft.Height - 2));
            }
        }

        private int RowAt(Point p)
        {
            int i = (int)Math.Floor((p.Y - Top + 2) / RowStep);
            return i >= 0 && i < Tabs.Length ? i : -1;
        }

        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            int i = RowAt(e.GetPosition(this));
            if (i < 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
            _o.SetLaneMode(Tabs[i].Mode);
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            int i = RowAt(e.GetPosition(this));
            if (i != _hover) { _hover = i; InvalidateVisual(); }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hover != -1) { _hover = -1; InvalidateVisual(); }
        }
    }

    // ---- an MPE lane -----------------------------------------------------------------------

    private sealed class ExpressionLane : Control
    {
        private readonly PianoRollView _o;
        public ExpressionLane(PianoRollView o) { _o = o; }

        private enum Drag { None, Point, Draw }
        private Drag _drag;
        // The notes a gesture edits, and the curve each started with (of this lane's dim).
        private readonly Dictionary<int, List<(float Beat, float Value)>> _orig = new();
        private readonly Dictionary<int, List<(float Beat, float Value)>> _work = new();
        private readonly List<(double Beat, float Value)> _stroke = new();   // absolute clip beats
        private bool _straight;
        private int _ptNote = -1, _ptIdx = -1;
        private (int Note, int Idx) _hoverPt = (-1, -1);
        private double _range = 2;   // bend lane: ± semitones shown

        private NoteExpressionDim Dim => DimOf(_o._laneMode);
        private const double Pad = 6;

        /// <summary>The curve a gesture is reshaping right now (for the note grid's live view).</summary>
        internal List<(float Beat, float Value)>? EditingCurve(int i, NoteExpressionDim d)
            => _drag != Drag.None && d == Dim && _work.TryGetValue(i, out var w) ? w : null;
        internal bool Editing(int i) => _drag != Drag.None && _work.ContainsKey(i);

        private List<(float Beat, float Value)> Curve(int i)
            => _work.TryGetValue(i, out var w) ? w : _o.CurveOf(_o._notes[i], Dim);

        // ---- value ↔ y ----
        private double ValueToY(float v)
        {
            double h = Bounds.Height;
            if (Dim == NoteExpressionDim.Bend) return h / 2 - v / _range * (h / 2 - Pad);
            return h - Pad - Math.Clamp(v, 0, 1) * (h - 2 * Pad);
        }

        private float YToValue(double y)
        {
            double h = Bounds.Height;
            if (Dim == NoteExpressionDim.Bend)
            {
                double v = (h / 2 - y) / (h / 2 - Pad) * _range;
                return (float)Math.Clamp(v, -_range, _range);
            }
            return (float)Math.Clamp((h - Pad - y) / (h - 2 * Pad), 0, 1);
        }

        // The bend scale follows what is there: ±2, 12, 24, 48 or 96 semitones.
        private void UpdateRange()
        {
            if (Dim != NoteExpressionDim.Bend || _drag != Drag.None) return;
            double m = 0;
            for (int i = 0; i < _o._notes.Count; i++)
                foreach (var (_, v) in Curve(i)) m = Math.Max(m, Math.Abs(v));
            _range = new[] { 2.0, 12, 24, 48, 96 }.FirstOrDefault(r => m <= r + 1e-3, 96);
        }

        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            _o.EnsurePpb(w);
            UpdateRange();
            ctx.FillRectangle(VelBg, new Rect(0, 0, w, h));
            double ppb = _o.PixelsPerBeat;
            double step = _o.GridStepBeats();
            double viewEnd = Math.Min(_o._lengthBeats, _o.ScrollBeats + w / Math.Max(1e-6, ppb));
            double startB = Math.Max(0, Math.Floor(_o.ScrollBeats / step) * step);
            for (int k = 0; ; k++)
            {
                double b = startB + k * step;
                if (b > viewEnd + 1e-9) break;
                ctx.DrawLine(_o.GridPen(b), new Point(_o.BeatToX(b) + 0.5, 0), new Point(_o.BeatToX(b) + 0.5, h));
            }
            // Rest line: no bend / centred slide.
            if (Dim != NoteExpressionDim.Pressure)
            {
                double y0 = Math.Round(ValueToY(Neutral(Dim))) + 0.5;
                ctx.DrawLine(ZeroPen, new Point(0, y0), new Point(w, y0));
            }

            var notes = _o._notes;
            // Unselected first, so the curves you edit sit on top.
            foreach (bool selPass in new[] { false, true })
                for (int i = 0; i < notes.Count; i++)
                {
                    bool sel = _o._selection.Contains(i);
                    if (sel != selPass || _o.IsGhost(i)) continue;
                    var n = notes[i];
                    double x0 = _o.BeatToX(n.StartBeat), x1 = _o.BeatToX(n.StartBeat + n.LengthBeats);
                    if (x1 < 0 || x0 > w) continue;
                    if (sel) ctx.FillRectangle(SpanWash, new Rect(x0, 0, Math.Max(1, x1 - x0), h));
                    var c = Curve(i);
                    if (c.Count == 0)
                    {
                        if (sel)
                        {
                            double yn = ValueToY(Neutral(Dim));
                            ctx.DrawLine(NeutralPen, new Point(x0, yn), new Point(x1, yn));
                        }
                        continue;
                    }
                    var line = new StreamGeometry();
                    using (var g = line.Open())
                    {
                        g.BeginFigure(new Point(x0, ValueToY(c[0].Value)), false);
                        foreach (var (b, v) in c)
                            g.LineTo(new Point(Math.Clamp(x0 + b * ppb, x0, x1), ValueToY(v)));
                        g.LineTo(new Point(x1, ValueToY(c[^1].Value)));
                        g.EndFigure(false);
                    }
                    IPen pen = sel ? CurvePen : TrackPen(n);
                    using (ctx.PushClip(new Rect(x0, 0, Math.Max(1, x1 - x0), h))) ctx.DrawGeometry(null, pen, line);
                    if (!sel) continue;
                    for (int k = 0; k < c.Count; k++)
                    {
                        double px = x0 + c[k].Beat * ppb;
                        if (px > x1 + 0.5) continue;
                        bool hot = _hoverPt == (i, k) || (_drag == Drag.Point && _ptNote == i && _ptIdx == k);
                        double r = hot ? 3.5 : 2.5;
                        ctx.FillRectangle(hot ? PointHot : PointFill, new Rect(px - r, ValueToY(c[k].Value) - r, r * 2, r * 2), 1);
                    }
                }

            if (Dim == NoteExpressionDim.Bend)
            {
                var ft = new FormattedText("±" + NotaNum.Unit(_range, "0", "st"), NotaNum.Culture, FlowDirection.LeftToRight, Mono, 9, LabelText);
                ctx.DrawText(ft, new Point(4, 3));
            }
            _o.DrawOutsideClip(ctx, w, h);
        }

        private IPen TrackPen(NotaNote n)
        {
            var c = _o._trackColor;
            return new Pen(new SolidColorBrush(Color.FromArgb(0x99, c.R, c.G, c.B)), 1.25);
        }

        // ---- hit testing ----
        private bool Spans(int i, double beat)
        {
            var n = _o._notes[i];
            return beat >= n.StartBeat - 1e-9 && beat <= n.StartBeat + n.LengthBeats + 1e-9;
        }

        private (int Note, int Idx) PointAt(Point p)
        {
            double best = 7; var hit = (-1, -1);
            foreach (int i in _o._selection)
            {
                if (i < 0 || i >= _o._notes.Count) continue;
                var n = _o._notes[i];
                var c = Curve(i);
                for (int k = 0; k < c.Count; k++)
                {
                    double d = Math.Max(Math.Abs(_o.BeatToX(n.StartBeat + c[k].Beat) - p.X), Math.Abs(ValueToY(c[k].Value) - p.Y));
                    if (d < best) { best = d; hit = (i, k); }
                }
            }
            return hit;
        }

        // The selected notes under a beat; with none, the highest note there becomes the
        // selection (⇧ adds it). Empty when nothing sounds there.
        private List<int> TargetsAt(double beat, bool shift)
        {
            var sel = _o._selection.Where(i => i >= 0 && i < _o._notes.Count && Spans(i, beat)).ToList();
            if (sel.Count > 0) return sel;
            int pick = -1;
            for (int i = 0; i < _o._notes.Count; i++)
                if (Spans(i, beat) && (pick < 0 || _o._notes[i].Pitch > _o._notes[pick].Pitch)) pick = i;
            if (pick < 0) return sel;
            if (!shift) _o._selection.Clear();
            _o._selection.Add(pick);
            _o.Invalidate(); _o.Changed?.Invoke();
            return new List<int> { pick };
        }

        private float Rel(int i, double absBeat)
        {
            var n = _o._notes[i];
            return (float)Math.Clamp(absBeat - n.StartBeat, 0, n.LengthBeats);
        }

        // ---- gestures ----
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            var p = e.GetPosition(this);
            double beat = _o.XToBeat(p.X);
            bool shift = (e.KeyModifiers & KeyModifiers.Shift) != 0;

            if (pt.Properties.IsRightButtonPressed)
            {
                if (TargetsAt(beat, shift).Count == 0 && _o._selection.Count == 0) return;
                ShowClearMenu();
                e.Handled = true;
                return;
            }
            if (!pt.Properties.IsLeftButtonPressed || _o.CreateExpression is null) return;

            var hit = PointAt(p);
            if (e.ClickCount == 2)
            {
                if (hit.Note >= 0) { EditOne(hit.Note, c => c.RemoveAt(hit.Idx)); }
                else
                {
                    var targets = TargetsAt(beat, shift);
                    if (targets.Count == 0) return;
                    int i = targets[0];
                    float v = YToValue(p.Y), rb = Rel(i, beat);
                    EditOne(i, c =>
                    {
                        int at = c.FindIndex(q => q.Beat > rb);
                        c.Insert(at < 0 ? c.Count : at, (rb, v));
                    });
                }
                e.Handled = true;
                return;
            }

            _orig.Clear(); _work.Clear(); _stroke.Clear();
            if (hit.Note >= 0)
            {
                _drag = Drag.Point;
                _ptNote = hit.Note; _ptIdx = hit.Idx;
                _orig[hit.Note] = Curve(hit.Note);
                _work[hit.Note] = new List<(float, float)>(_orig[hit.Note]);
            }
            else
            {
                var targets = TargetsAt(beat, shift);
                if (targets.Count == 0) return;
                _drag = Drag.Draw;
                _straight = (e.KeyModifiers & KeyModifiers.Alt) != 0;
                foreach (int i in targets) { _orig[i] = Curve(i); _work[i] = new List<(float, float)>(_orig[i]); }
                _stroke.Add((beat, YToValue(p.Y)));
                ApplyStroke();
            }
            e.Pointer.Capture(this);
            InvalidateVisual(); _o._gridControl.InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            if (_drag == Drag.Point)
            {
                var n = _o._notes[_ptNote];
                var c = _work[_ptNote];
                float lo = _ptIdx > 0 ? c[_ptIdx - 1].Beat : 0f;
                float hi = _ptIdx < c.Count - 1 ? c[_ptIdx + 1].Beat : (float)n.LengthBeats;
                c[_ptIdx] = ((float)Math.Clamp(_o.XToBeat(p.X) - n.StartBeat, lo, hi), YToValue(p.Y));
                Refresh();
                return;
            }
            if (_drag == Drag.Draw)
            {
                var s = (_o.XToBeat(p.X), YToValue(p.Y));
                if (_straight) { if (_stroke.Count > 1) _stroke[1] = s; else _stroke.Add(s); }
                else if (Math.Abs(_o.BeatToX(_stroke[^1].Beat) - p.X) >= 1) _stroke.Add(s);
                ApplyStroke();
                Refresh();
                return;
            }
            var hv = PointAt(p);
            if (hv != _hoverPt) { _hoverPt = hv; InvalidateVisual(); }
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            if (_hoverPt.Note >= 0) { _hoverPt = (-1, -1); InvalidateVisual(); }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_drag == Drag.None) return;
            bool draw = _drag == Drag.Draw;
            _drag = Drag.None;
            e.Pointer.Capture(null);
            // Thin to what the eye can tell apart: a pixel and a half of the lane's scale.
            float tol = (float)Math.Abs(YToValue(Bounds.Height / 2) - YToValue(Bounds.Height / 2 + 1.5));
            foreach (var (i, c) in _work)
                if (i < _o._notes.Count) _o._notes[i] = _o.WithCurve(_o._notes[i], Dim, draw && !_straight ? Thin(c, tol) : c);
            _orig.Clear(); _work.Clear(); _stroke.Clear(); _ptNote = _ptIdx = -1;
            _o.Invalidate();
            _o.CommitNotes(); _o.Changed?.Invoke();
        }

        private void Refresh() { InvalidateVisual(); _o._gridControl.InvalidateVisual(); }

        // The stroke replaces each target's curve over the stretch it covers; outside it the
        // curve keeps its old values (pinned at the stroke's edges, so nothing else moves); with nothing
        // of the old curve after it, the drawn value holds to the note's end.
        private void ApplyStroke()
        {
            if (_stroke.Count == 0) return;
            var samples = _straight && _stroke.Count == 2
                ? Enumerable.Range(0, 2).Select(k => _stroke[k]).ToList()
                : new List<(double Beat, float Value)>(_stroke);
            samples.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            float neutral = Neutral(Dim);
            foreach (var (i, orig) in _orig)
            {
                var n = _o._notes[i];
                float a = Rel(i, samples[0].Beat), b = Rel(i, samples[^1].Beat);
                const float eps = 1e-3f;
                var outp = new List<(float Beat, float Value)>();
                foreach (var q in orig) if (q.Beat < a - eps) outp.Add(q);
                if (a > eps) outp.Add((a - eps, ValueAt(orig, a - eps, neutral)));
                float last = float.NegativeInfinity;
                foreach (var (sb, sv) in samples)
                {
                    float r = Rel(i, sb);
                    if (r <= last) { outp[^1] = (outp[^1].Beat, sv); continue; }
                    outp.Add((r, sv)); last = r;
                }
                // Past the stroke: the old curve resumes if there is one; otherwise the drawn
                // value holds to the note's end (a bend drawn up stays up).
                if (b < n.LengthBeats - eps && orig.Any(q => q.Beat > b + eps))
                {
                    outp.Add((b + eps, ValueAt(orig, b + eps, neutral)));
                    foreach (var q in orig) if (q.Beat > b + eps) outp.Add(q);
                }
                _work[i] = outp;
            }
        }

        // A one-shot edit of one note's curve (double-click add / delete): one undo step.
        private void EditOne(int i, Action<List<(float Beat, float Value)>> change)
        {
            var c = Curve(i);
            change(c);
            _o._notes[i] = _o.WithCurve(_o._notes[i], Dim, c);
            _o.Invalidate();
            _o.CommitNotes(); _o.Changed?.Invoke();
        }

        private void ShowClearMenu()
        {
            string lane = Dim switch { NoteExpressionDim.Bend => "Bend", NoteExpressionDim.Pressure => "Pressure", _ => "Slide" };
            var items = new[] { $"Clear {lane}", "Clear All Expression" };
            ClipEditorKit.ShowMenu(this, items, -1, k =>
            {
                bool changed = false;
                foreach (int i in _o._selection.ToList())
                {
                    if (i < 0 || i >= _o._notes.Count || _o._notes[i].ExprId == 0) continue;
                    var n = _o._notes[i];
                    if (k == 0) n = _o.WithCurve(n, Dim, null);
                    else n.ExprId = 0;
                    _o._notes[i] = n;
                    changed = true;
                }
                if (!changed) return;
                _o.Invalidate();
                _o.CommitNotes(); _o.Changed?.Invoke();
            });
        }
    }
}
