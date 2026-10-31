// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// The piano roll's controller lane (specs/063; designed in specs/020 contracts/ui-lanes.md):
// one instrument-wide controller of the active clip — any CC, the pitch wheel or channel
// pressure — over the whole clip, picked from the gutter's fifth tab. Switch controllers
// (CC64–69, the sustain pedal) are blocks, on where the value is at least half:
//   drag on empty lane      draws a block on the grid (⌘/Ctrl: off the grid);
//   drag a block's edge     resizes it · drag its body moves it · double-click or Delete removes it.
// The rest are a curve:
//   drag across the lane    draws over that stretch (⌥ draws a straight line);
//   drag a point            moves it (in time between its neighbours, and in value);
//   double-click            adds a point, or deletes the one under the pointer.
// Right-click clears the controller. A gesture is one write, one undo step in the engine.

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
    /// <summary>The controllers (<see cref="MidiController"/> ids) the active clip holds data
    /// for. Set by the host with <see cref="GetController"/> / <see cref="SetController"/>.</summary>
    public Func<int[]>? ControllersInClip { get; set; }
    /// <summary>The active clip's curve for a controller, in clip-local beats.</summary>
    public Func<int, AutomationPoint[]>? GetController { get; set; }
    /// <summary>Replaces the active clip's curve for a controller (empty clears it); one undo
    /// step. Without it the lane is view-only.</summary>
    public Action<int, AutomationPoint[]>? SetController { get; set; }

    private readonly ControllerLane _ctlLane;
    private int _laneCtl = -1;   // −1 until the lane is first shown
    private readonly Dictionary<int, AutomationPoint[]> _ctlCache = new();
    private int[]? _ctlInClip;

    private static readonly int[] CommonControllers =
        { MidiController.Sustain, MidiController.ModWheel, MidiController.PitchWheel, MidiController.ChannelPressure, 11, 2 };

    // Every reload (undo, recording, another clip) and active-clip change re-reads the engine.
    private void DropControllerCache()
    {
        _ctlCache.Clear();
        _ctlInClip = null;
    }

    private int[] ControllersWithData()
        => _ctlInClip ??= (ControllersInClip?.Invoke() ?? Array.Empty<int>()).Where(c => !MidiController.IsReserved(c)).ToArray();

    private int DefaultController()
    {
        var d = ControllersWithData();
        return d.Length > 0 ? d[0] : MidiController.Sustain;
    }

    private AutomationPoint[] ControllerPoints(int ctl)
    {
        if (!_ctlCache.TryGetValue(ctl, out var p))
            _ctlCache[ctl] = p = GetController?.Invoke(ctl) ?? Array.Empty<AutomationPoint>();
        return p;
    }

    private void StoreController(int ctl, List<AutomationPoint> pts)
    {
        if (SetController is null) return;
        SetController(ctl, pts.ToArray());
        DropControllerCache();
        _ctlLane.InvalidateVisual();
        _laneTabs.InvalidateVisual();
    }

    /// <summary>"Mod wheel · CC 1", "CC 3", "Pitch wheel".</summary>
    private static string ControllerLabel(int ctl)
    {
        string n = MidiController.Name(ctl);
        return ctl >= 128 || n == $"CC {ctl}" ? n : $"{n} · CC {ctl}";
    }

    private void SelectController(int ctl)
    {
        _laneCtl = ctl;
        _ctlLane.ResetGesture();
        if (_laneMode != LaneMode.Controller) SetLaneMode(LaneMode.Controller);
        _ctlLane.InvalidateVisual();
        _laneTabs.InvalidateVisual();
    }

    /// <summary>The controller picker: what the clip holds first (marked), the common
    /// controllers, then every other CC in groups of 16.</summary>
    internal void ShowControllerPicker(Control anchor)
    {
        var f = new MenuFlyout();
        var data = ControllersWithData();
        MenuItem Item(int ctl, string header)
        {
            var it = new MenuItem { Header = header };
            if (Array.IndexOf(data, ctl) >= 0) it.Icon = MenuKit.Icon(GlyphKind.Automation);
            it.Click += (_, _) => SelectController(ctl);
            return it;
        }
        foreach (int c in data) f.Items.Add(Item(c, ControllerLabel(c)));
        if (data.Length > 0) f.Items.Add(new Separator());
        foreach (int c in CommonControllers)
            if (Array.IndexOf(data, c) < 0) f.Items.Add(Item(c, MidiController.Name(c)));
        f.Items.Add(new Separator());
        var other = new MenuItem { Header = "Other CC" };
        for (int g = 0; g < 128; g += 16)
        {
            var ctls = Enumerable.Range(g, 16).Where(c => !MidiController.IsReserved(c)).ToList();
            if (ctls.Count == 0) continue;
            var grp = new MenuItem { Header = $"CC {ctls[0]}–{ctls[^1]}" };
            foreach (int c in ctls)
            {
                string n = MidiController.Name(c);
                grp.Items.Add(Item(c, n == $"CC {c}" ? n : $"CC {c} — {n}"));
            }
            other.Items.Add(grp);
        }
        f.Items.Add(other);
        f.ShowAt(anchor);
    }

    // ---- the lane ------------------------------------------------------------------------

    private sealed class ControllerLane : Control
    {
        private readonly PianoRollView _o;
        public ControllerLane(PianoRollView o) { _o = o; }

        private const double Pad = 6, EdgePx = 5, PointPx = 7;
        private const double Eps = 1.0 / 1024;   // beats: where a drawn stretch meets the old curve
        private static readonly IBrush BlockFill = NotaPalette.Wash(NotaPalette.VizInk, 0x40);
        private static readonly IBrush BlockFillLow = NotaPalette.Wash(NotaPalette.VizInk, 0x1C);
        private static readonly IBrush BlockHot = NotaPalette.Wash(NotaPalette.Accent, 0x70);
        private static readonly IPen BlockEdge = new Pen(NotaPalette.VizInk, 1.5);
        private static readonly IPen BlockEdgeHot = new Pen(NotaPalette.Accent, 1.5);

        private int Ctl => _o._laneCtl < 0 ? MidiController.Sustain : _o._laneCtl;
        private bool IsSwitch => MidiController.IsSwitch(Ctl);
        private bool Editable => _o.SetController is not null;

        private enum Drag { None, Draw, Point, NewBlock, MoveBlock, BlockStart, BlockEnd }
        private Drag _drag;
        private List<AutomationPoint>? _work;             // the curve while a gesture reshapes it
        private List<AutomationPoint> _orig = new();      // ... and what it started from
        private readonly List<(double Beat, float Value)> _stroke = new();
        private bool _straight, _free;
        private int _pt = -1, _hoverPt = -1;
        private Block _grab;
        private double _anchor, _grabAt, _blockStart;
        private static readonly Cursor ResizeCursor = new(StandardCursorType.SizeWestEast);
        private static readonly Cursor MoveCursor = new(StandardCursorType.SizeAll);
        private int _hoverBlock = -1, _selBlock = -1;
        private double _hoverBeat = double.NaN;

        internal void ResetGesture()
        {
            _drag = Drag.None; _work = null; _stroke.Clear();
            _pt = _hoverPt = _hoverBlock = _selBlock = -1;
        }

        private IReadOnlyList<AutomationPoint> Cur => (IReadOnlyList<AutomationPoint>?)_work ?? _o.ControllerPoints(Ctl);

        // ---- time and value ----
        private double ClipLen => _o._set.ActiveClip.LengthBeats;
        private double Ed(double local) => _o._set.ToEditor(_o._set.Active, local);
        private double XOf(double local) => _o.BeatToX(Ed(local));
        private double LocalAt(double x) => _o._set.ToLocal(_o._set.Active, _o.XToBeat(x));
        private double SnapLocal(double local)
            => _free ? local : _o._set.ToLocal(_o._set.Active, _o.SnapBeat(Ed(local)));
        private double MinBlock => _free ? 1.0 / 32 : Math.Max(1.0 / 32, _o.SnapStep);

        private double ValueToY(float v)
        {
            var (lo, hi) = MidiController.Range(Ctl);
            double h = Bounds.Height;
            return h - Pad - (Math.Clamp(v, lo, hi) - lo) / (hi - lo) * (h - 2 * Pad);
        }

        private float YToValue(double y)
        {
            var (lo, hi) = MidiController.Range(Ctl);
            double h = Bounds.Height;
            float v = (float)Math.Clamp(lo + (h - Pad - y) / (h - 2 * Pad) * (hi - lo), lo, hi);
            if (Ctl == MidiController.PitchWheel && Math.Abs(v) < 0.02f) v = 0;   // the wheel's centre
            return v;
        }

        private string Readout(float v)
        {
            if (IsSwitch) return v >= 0.5f ? "on" : "off";
            if (Ctl == MidiController.PitchWheel) return v == 0 ? "0" : v.ToString("+0.00;−0.00", NotaNum.Culture);
            return ((int)Math.Round(v * 127)).ToString(NotaNum.Culture);
        }

        // The engine's reading of a curve (ControllerCurve::valueAt): held before the first and
        // after the last point; a switch steps, the rest follow each point's shaping.
        private static float ValueAt(IReadOnlyList<AutomationPoint> p, double beat, bool sw)
        {
            if (p.Count == 0) return 0;
            if (beat <= p[0].Beat) return p[0].Value;
            if (beat >= p[^1].Beat) return p[^1].Value;
            int i = 1;
            while (p[i].Beat <= beat) i++;
            var a = p[i - 1]; var b = p[i];
            if (sw) return a.Value;
            double span = b.Beat - a.Beat;
            if (span <= 0) return b.Value;
            return (float)(a.Value + (b.Value - a.Value) * Shape((beat - a.Beat) / span, a.Curve));
        }

        private static double Shape(double t, float curve) => curve == 0f ? t : Math.Pow(t, Math.Pow(2.0, -curve * 4.0));

        // Sorted, the last point given for any one beat kept; a switch keeps only its changes.
        private List<AutomationPoint> Normalize(IEnumerable<AutomationPoint> pts)
        {
            var sorted = pts.Select((p, i) => (p, i)).OrderBy(t => t.p.Beat).ThenBy(t => t.i).Select(t => t.p).ToList();
            var outp = new List<AutomationPoint>(sorted.Count);
            foreach (var p in sorted)
            {
                if (outp.Count > 0 && Math.Abs(outp[^1].Beat - p.Beat) < 1e-9) { outp[^1] = p; continue; }
                if (IsSwitch && outp.Count > 0 && outp[^1].Value == p.Value) continue;
                outp.Add(p);
            }
            return outp;
        }

        // ---- switch blocks ----
        // A span where the value is at least half: its points [First, Off), ended by point Off
        // (−1: it holds to the clip's end).
        private readonly record struct Block(double Start, double End, int First, int Off);

        private List<Block> Blocks(IReadOnlyList<AutomationPoint> p)
        {
            var list = new List<Block>();
            int i = 0;
            while (i < p.Count)
            {
                if (p[i].Value < 0.5f) { i++; continue; }
                int first = i;
                while (i < p.Count && p[i].Value >= 0.5f) i++;
                bool ended = i < p.Count;
                list.Add(new Block(p[first].Beat, ended ? p[i].Beat : Math.Max(ClipLen, p[first].Beat), first, ended ? i : -1));
            }
            return list;
        }

        private static List<AutomationPoint> Without(IReadOnlyList<AutomationPoint> p, Block b)
        {
            int end = b.Off < 0 ? p.Count : b.Off;
            var outp = new List<AutomationPoint>(p.Count);
            for (int i = 0; i < p.Count; i++) if (i < b.First || i >= end) outp.Add(p[i]);
            return outp;
        }

        private static List<AutomationPoint> PointsOf(IReadOnlyList<AutomationPoint> p, Block b)
        {
            int end = b.Off < 0 ? p.Count : b.Off;
            var outp = new List<AutomationPoint>();
            for (int i = b.First; i < end; i++) outp.Add(p[i]);
            return outp;
        }

        // `rest` with [from, to) replaced by the points of `seg` inside it; from `to` on, the
        // value that was there before resumes (a block drawn over another merges with it).
        private List<AutomationPoint> Overwrite(List<AutomationPoint> rest, double from, double to, List<AutomationPoint> seg)
        {
            float tail = 0;
            foreach (var q in rest) if (q.Beat <= to) tail = q.Value;
            var outp = rest.Where(q => q.Beat < from).ToList();
            outp.AddRange(seg.Where(q => q.Beat >= from && q.Beat < to));
            if (to < ClipLen - 1e-9 || rest.Any(q => q.Beat > to)) outp.Add(new AutomationPoint(to, tail));
            outp.AddRange(rest.Where(q => q.Beat > to));
            return Normalize(outp);
        }

        private (int Block, int Edge) BlockAt(Point p)
        {
            var blocks = Blocks(Cur);
            for (int k = blocks.Count - 1; k >= 0; k--)
            {
                double x0 = XOf(blocks[k].Start), x1 = XOf(blocks[k].End);
                if (p.X < x0 - 2 || p.X > x1 + 2) continue;
                double zone = Math.Min(EdgePx, (x1 - x0) / 3);
                return (k, p.X <= x0 + zone ? -1 : p.X >= x1 - zone ? 1 : 0);
            }
            return (-1, 0);
        }

        // ---- curve points ----
        private int PointAt(Point p)
        {
            var c = Cur;
            double best = PointPx; int hit = -1;
            for (int k = 0; k < c.Count; k++)
            {
                double d = Math.Max(Math.Abs(XOf(c[k].Beat) - p.X), Math.Abs(ValueToY(c[k].Value) - p.Y));
                if (d < best) { best = d; hit = k; }
            }
            return hit;
        }

        // The stroke replaces the curve over the stretch it covers; outside it the curve keeps its
        // values (pinned at the stroke's edges); with nothing after it, the drawn value holds.
        private void ApplyStroke(List<(double Beat, float Value)> samples)
        {
            if (samples.Count == 0) return;
            samples.Sort((a, b) => a.Beat.CompareTo(b.Beat));
            double a = samples[0].Beat, b = samples[^1].Beat;
            var outp = _orig.Where(q => q.Beat < a - Eps).ToList();
            if (outp.Count > 0) outp.Add(new AutomationPoint(a - Eps, ValueAt(_orig, a - Eps, false)));
            foreach (var (sb, sv) in samples) outp.Add(new AutomationPoint(sb, sv));
            if (_orig.Any(q => q.Beat > b + Eps))
            {
                outp.Add(new AutomationPoint(b + Eps, ValueAt(_orig, b + Eps, false)));
                outp.AddRange(_orig.Where(q => q.Beat > b + Eps));
            }
            _work = Normalize(outp);
        }

        private List<(double Beat, float Value)> StrokeSamples()
            => _straight && _stroke.Count >= 2 ? new() { _stroke[0], _stroke[^1] } : new(_stroke);

        // Ramer–Douglas–Peucker over the drawn stroke only: recorded data around it is left as is.
        private static List<(double Beat, float Value)> Thin(List<(double Beat, float Value)> p, float tol)
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
                double worst = -1; int at = a;
                for (int i = a + 1; i < b; i++)
                {
                    double span = p[b].Beat - p[a].Beat;
                    double t = span > 0 ? (p[i].Beat - p[a].Beat) / span : 0;
                    double dev = Math.Abs(p[i].Value - (p[a].Value + (p[b].Value - p[a].Value) * t));
                    if (dev > worst) { worst = dev; at = i; }
                }
                if (worst > tol) { keep[at] = true; stack.Push((a, at)); stack.Push((at, b)); }
            }
            var outp = new List<(double, float)>();
            for (int i = 0; i < p.Count; i++) if (keep[i]) outp.Add(p[i]);
            return outp;
        }

        // ---- drawing ----
        public override void Render(DrawingContext ctx)
        {
            double w = Bounds.Width, h = Bounds.Height;
            if (w <= 0) return;
            _o.EnsurePpb(w);
            ctx.FillRectangle(VelBg, new Rect(0, 0, w, h));
            _o.DrawGridLines(ctx, w, h);
            var c = Cur;

            if (!IsSwitch)
            {
                double yr = Math.Round(ValueToY(Ctl == MidiController.PitchWheel ? 0f : 0.5f)) + 0.5;
                ctx.DrawLine(Ctl == MidiController.PitchWheel ? ZeroPen : NeutralPen, new Point(0, yr), new Point(w, yr));
            }

            if (IsSwitch) DrawBlocks(ctx, c, h);
            else DrawCurve(ctx, c, w);

            _o.DrawOutsideClip(ctx, w, h);

            var name = new FormattedText(ControllerLabel(Ctl), NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.Sans, 9, LabelText);
            ctx.DrawText(name, new Point(4, 3));
            if (c.Count == 0 && Editable && _drag == Drag.None)
            {
                var hint = new FormattedText(IsSwitch ? "Drag to draw a block" : "Drag to draw", NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.Sans, 9, LabelText);
                ctx.DrawText(hint, new Point((w - hint.Width) / 2, (h - hint.Height) / 2));
            }
            if (!double.IsNaN(_hoverBeat) || _drag != Drag.None)
            {
                double beat = _drag == Drag.Point && _pt >= 0 && _pt < c.Count ? c[_pt].Beat : _hoverBeat;
                if (!double.IsNaN(beat) && (c.Count > 0 || _drag != Drag.None))
                {
                    var ft = new FormattedText(Readout(ValueAt(c, beat, IsSwitch)), NotaNum.Culture, FlowDirection.LeftToRight, Mono, 9, KeyLabelHot);
                    ctx.DrawText(ft, new Point(w - ft.Width - 4, 3));
                }
            }
        }

        private void DrawBlocks(DrawingContext ctx, IReadOnlyList<AutomationPoint> c, double h)
        {
            // Every stretch at its value: a half pedal is a half-height block, a value under
            // half a faint shade (the pedal is up there).
            for (int i = 0; i < c.Count; i++)
            {
                if (c[i].Value <= 0) continue;
                double end = i + 1 < c.Count ? c[i + 1].Beat : Math.Max(ClipLen, c[i].Beat);
                double x0 = XOf(c[i].Beat), x1 = XOf(end);
                double y = ValueToY(c[i].Value);
                ctx.FillRectangle(c[i].Value >= 0.5f ? BlockFill : BlockFillLow, new Rect(x0, y, Math.Max(1, x1 - x0), h - Pad - y));
            }
            var blocks = Blocks(c);
            for (int k = 0; k < blocks.Count; k++)
            {
                var b = blocks[k];
                double x0 = XOf(b.Start), x1 = XOf(b.End);
                bool hot = k == _hoverBlock || k == _selBlock;
                if (hot) ctx.FillRectangle(BlockHot, new Rect(x0, Pad, Math.Max(1, x1 - x0), h - 2 * Pad));
                var pen = hot ? BlockEdgeHot : BlockEdge;
                ctx.DrawLine(pen, new Point(x0 + 0.75, Pad), new Point(x0 + 0.75, h - Pad));
                ctx.DrawLine(pen, new Point(x1 - 0.75, Pad), new Point(x1 - 0.75, h - Pad));
            }
        }

        private void DrawCurve(DrawingContext ctx, IReadOnlyList<AutomationPoint> c, double w)
        {
            if (c.Count == 0) return;
            var line = new StreamGeometry();
            using (var g = line.Open())
            {
                g.BeginFigure(new Point(XOf(c[0].Beat), ValueToY(c[0].Value)), false);
                for (int i = 1; i < c.Count; i++)
                {
                    var a = c[i - 1]; var b = c[i];
                    if (a.Curve != 0)
                        for (int s = 1; s < 12; s++)
                        {
                            double t = s / 12.0;
                            g.LineTo(new Point(XOf(a.Beat + (b.Beat - a.Beat) * t), ValueToY((float)(a.Value + (b.Value - a.Value) * Shape(t, a.Curve)))));
                        }
                    g.LineTo(new Point(XOf(b.Beat), ValueToY(b.Value)));
                }
                g.LineTo(new Point(Math.Max(XOf(c[^1].Beat), XOf(ClipLen)), ValueToY(c[^1].Value)));
                g.EndFigure(false);
            }
            ctx.DrawGeometry(null, CurvePen, line);
            for (int k = 0; k < c.Count; k++)
            {
                double px = XOf(c[k].Beat);
                if (px < -4 || px > w + 4) continue;
                bool hot = k == _hoverPt || (_drag == Drag.Point && k == _pt);
                const double r = 2.5;   // hover changes colour, not size
                ctx.FillRectangle(hot ? PointHot : PointFill, new Rect(px - r, ValueToY(c[k].Value) - r, r * 2, r * 2), 1);
            }
        }

        // ---- gestures ----
        protected override void OnPointerPressed(PointerPressedEventArgs e)
        {
            var pt = e.GetCurrentPoint(this);
            var p = e.GetPosition(this);
            if (pt.Properties.IsRightButtonPressed)
            {
                if (Editable && Cur.Count > 0) ShowClearMenu();
                e.Handled = true;
                return;
            }
            if (!pt.Properties.IsLeftButtonPressed || !Editable) return;
            Focus();
            _free = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta)) != 0;
            double local = Math.Clamp(LocalAt(p.X), 0, ClipLen);
            _orig = Cur.ToList();

            if (IsSwitch)
            {
                var (k, edge) = BlockAt(p);
                if (e.ClickCount == 2)
                {
                    if (k >= 0) DeleteBlock(k);
                    e.Handled = true;
                    return;
                }
                _selBlock = k;
                if (k >= 0)
                {
                    _grab = Blocks(_orig)[k];
                    _grabAt = local;
                    _drag = edge < 0 ? Drag.BlockStart : edge > 0 ? Drag.BlockEnd : Drag.MoveBlock;
                }
                else
                {
                    _anchor = SnapLocal(local);
                    _drag = Drag.NewBlock;
                    UpdateBlock(local);
                }
            }
            else
            {
                int hit = PointAt(p);
                if (e.ClickCount == 2)
                {
                    var c = _orig;
                    if (hit >= 0) c.RemoveAt(hit);
                    else c.Add(new AutomationPoint(local, YToValue(p.Y)));
                    _o.StoreController(Ctl, Normalize(c));
                    e.Handled = true;
                    return;
                }
                _stroke.Clear();
                if (hit >= 0) { _drag = Drag.Point; _pt = hit; _work = _orig.ToList(); }
                else
                {
                    _drag = Drag.Draw;
                    _straight = (e.KeyModifiers & KeyModifiers.Alt) != 0;
                    _stroke.Add((local, YToValue(p.Y)));
                    ApplyStroke(StrokeSamples());
                }
            }
            e.Pointer.Capture(this);
            InvalidateVisual();
            e.Handled = true;
        }

        protected override void OnPointerMoved(PointerEventArgs e)
        {
            var p = e.GetPosition(this);
            double local = Math.Clamp(LocalAt(p.X), 0, ClipLen);
            _hoverBeat = local;
            switch (_drag)
            {
                case Drag.Point:
                {
                    var c = _work!;
                    double lo = _pt > 0 ? c[_pt - 1].Beat + Eps : 0, hi = _pt < c.Count - 1 ? c[_pt + 1].Beat - Eps : ClipLen;
                    c[_pt] = new AutomationPoint(Math.Clamp(local, Math.Min(lo, hi), hi), YToValue(p.Y), c[_pt].Curve);
                    break;
                }
                case Drag.Draw:
                {
                    var s = (local, YToValue(p.Y));
                    if (_straight) { if (_stroke.Count > 1) _stroke[1] = s; else _stroke.Add(s); }
                    else if (Math.Abs(XOf(_stroke[^1].Beat) - p.X) >= 1) _stroke.Add(s);
                    ApplyStroke(StrokeSamples());
                    break;
                }
                case Drag.NewBlock or Drag.MoveBlock or Drag.BlockStart or Drag.BlockEnd:
                    UpdateBlock(local);
                    break;
                default:
                {
                    int hp = IsSwitch ? -1 : PointAt(p);
                    var (hb, edge) = IsSwitch ? BlockAt(p) : (-1, 0);
                    Cursor = hb < 0 ? Cursor.Default : edge != 0 ? ResizeCursor : MoveCursor;
                    _hoverPt = hp; _hoverBlock = hb;
                    break;
                }
            }
            InvalidateVisual();
        }

        // The block being drawn, moved or resized, laid over the curve it started from.
        private void UpdateBlock(double local)
        {
            double len = ClipLen;
            switch (_drag)
            {
                case Drag.NewBlock:
                {
                    double at = SnapLocal(local);
                    double s = Math.Min(_anchor, at), e = Math.Max(_anchor, at);
                    if (e - s < MinBlock) e = s + MinBlock;
                    _blockStart = s;
                    _work = Overwrite(_orig, s, Math.Min(e, len), new List<AutomationPoint> { new(s, 1f) });
                    break;
                }
                case Drag.MoveBlock:
                {
                    double s = Math.Clamp(SnapLocal(_grab.Start + local - _grabAt), 0, Math.Max(0, len - MinBlock));
                    double d = s - _grab.Start;
                    _blockStart = s;
                    var seg = PointsOf(_orig, _grab).Select(q => new AutomationPoint(q.Beat + d, q.Value, q.Curve)).ToList();
                    _work = Overwrite(Without(_orig, _grab), s, Math.Min(_grab.End + d, len), seg);
                    break;
                }
                case Drag.BlockStart:
                {
                    // A recorded tap can be shorter than the grid, so the min-length bound can
                    // sit left of 0. Clamping onto an inverted range throws; stay inside the block.
                    double cap = _grab.End - MinBlock;
                    double s = cap >= 0
                        ? Math.Clamp(SnapLocal(local), 0, cap)
                        : Math.Clamp(SnapLocal(local), 0, Math.Max(0, _grab.End));
                    _blockStart = s;
                    var pts = PointsOf(_orig, _grab);
                    var seg = new List<AutomationPoint> { new(s, ValueAt(pts, s, true)) };
                    seg.AddRange(pts.Where(q => q.Beat > s));
                    _work = Overwrite(Without(_orig, _grab), s, _grab.End, seg);
                    break;
                }
                case Drag.BlockEnd:
                {
                    double lo = _grab.Start + MinBlock;
                    double e = lo <= len
                        ? Math.Clamp(SnapLocal(local), lo, len)
                        : Math.Clamp(SnapLocal(local), _grab.Start, Math.Max(_grab.Start, len));
                    _blockStart = _grab.Start;
                    _work = Overwrite(Without(_orig, _grab), _grab.Start, e, PointsOf(_orig, _grab));
                    break;
                }
            }
        }

        protected override void OnPointerReleased(PointerReleasedEventArgs e)
        {
            if (_drag == Drag.None) return;
            var was = _drag;
            _drag = Drag.None;
            e.Pointer.Capture(null);
            if (was == Drag.Draw)
            {
                var samples = StrokeSamples().OrderBy(s => s.Beat).ToList();
                // A click without a drag is not a stroke. Committing it would write a point
                // that the second press of a double-click then deletes.
                if (samples.Count < 2)
                    _work = _orig.ToList();
                else if (!_straight)
                {
                    // Thin to what the eye can tell apart: a pixel and a half of the lane's scale.
                    float tol = Math.Abs(YToValue(Bounds.Height / 2) - YToValue(Bounds.Height / 2 + 1.5));
                    ApplyStroke(Thin(samples, tol));
                }
            }
            var result = _work;
            _work = null; _stroke.Clear(); _pt = -1;
            if (result is null) return;
            if (IsSwitch) _selBlock = Blocks(result).FindIndex(b => b.Start <= _blockStart + 1e-9 && b.End > _blockStart);
            if (!result.SequenceEqual(_orig)) _o.StoreController(Ctl, result);
            InvalidateVisual();
        }

        protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
        {
            if (_drag != Drag.None) { _drag = Drag.None; _work = null; _stroke.Clear(); InvalidateVisual(); }
            base.OnPointerCaptureLost(e);
        }

        protected override void OnPointerExited(PointerEventArgs e)
        {
            _hoverBeat = double.NaN;
            _hoverPt = _hoverBlock = -1;
            InvalidateVisual();
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (IsSwitch && _selBlock >= 0 && (e.Key == Key.Delete || e.Key == Key.Back)) { DeleteBlock(_selBlock); e.Handled = true; return; }
            if (e.Key == Key.Escape && _selBlock >= 0) { _selBlock = -1; InvalidateVisual(); e.Handled = true; return; }
            base.OnKeyDown(e);
        }

        private void DeleteBlock(int k)
        {
            var c = Cur.ToList();
            var blocks = Blocks(c);
            if (k < 0 || k >= blocks.Count) return;
            _selBlock = -1;
            _o.StoreController(Ctl, Normalize(Without(c, blocks[k])));
        }

        private void ShowClearMenu()
        {
            int ctl = Ctl;
            ClipEditorKit.ShowMenu(this, new[] { $"Clear {MidiController.Name(ctl)}" }, -1, _ =>
            {
                _selBlock = -1;
                _o.StoreController(ctl, new List<AutomationPoint>());
            });
        }
    }
}
