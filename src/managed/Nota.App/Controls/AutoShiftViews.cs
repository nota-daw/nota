// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Auto Shift (device kind 10) card windows, all read from the engine's telemetry
// (AutoShift.h scopeRead), so the picture is what the corrector does:
//  · AsPill — the PITCH column: IN (where the voice sits in the detection range, brass,
//    rising from the bottom) or CORR (the correction now, teal, bipolar about the centre).
//  · AsTraceView — the Trace and MIDI tabs: the detected pitch (teal dots) and the output
//    (brass line) over the last ~2 s on note lanes of the active scale, the target lane lit;
//    in MIDI mode the source's notes are drawn as blocks on their lanes.
//  · AsHistView — the Scale tab: how long each pitch class was sung (decaying over ~16
//    bars), in-scale columns brass, sung out-of-scale ones teal. Clicking a column toggles
//    that note in the scale.

using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class AsPill : Control
{
    private double _v = double.NaN;
    /// <summary>Bipolar: the fill hangs from the centre (the correction); else it rises from the bottom.</summary>
    public bool Bipolar { get; init; }
    public IBrush Ink { get; init; } = NotaPalette.Accent;

    public AsPill() { Width = 16; }

    /// <summary>0..1 (bipolar: −1..1), NaN = nothing to show.</summary>
    public void Set(double v)
    {
        if (!double.IsNaN(v)) v = Bipolar ? Math.Clamp(v, -1, 1) : Math.Clamp(v, 0, 1);
        if (v.Equals(_v) || (!double.IsNaN(v) && !double.IsNaN(_v) && Math.Abs(v - _v) < 1e-3)) return;
        _v = v; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 8) return;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        double r = Math.Min(8, w / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.BorderDefault, 1), rect, r, r);
        using (ctx.PushClip(new RoundedRect(rect, r)))
        {
            var wash = NotaPalette.Wash((SolidColorBrush)Ink, 0x4C);
            if (Bipolar)
            {
                double mid = h / 2;
                ctx.FillRectangle(NotaPalette.BorderStrong, new Rect(2, mid - 0.5, w - 4, 1));
                if (double.IsNaN(_v)) return;
                double y = mid - _v * (h / 2 - 2);
                ctx.FillRectangle(wash, new Rect(0, Math.Min(mid, y), w, Math.Abs(y - mid)));
                ctx.FillRectangle(Ink, new Rect(1.5, Math.Clamp(y - 1, 0, h - 2), w - 3, 2));
            }
            else
            {
                if (double.IsNaN(_v)) return;
                double y = (1 - _v) * h;
                ctx.FillRectangle(wash, new Rect(0, y, w, h - y));
                ctx.FillRectangle(Ink, new Rect(1.5, Math.Clamp(y - 1, 0, h - 2), w - 3, 2));
            }
        }
    }
}

internal sealed class AsTraceView : Control
{
    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private float[] _det = Array.Empty<float>(), _out = Array.Empty<float>(), _midi = Array.Empty<float>();
    private int _n;
    private int _mask = 0x0AB5;
    private double _target, _center = 60;
    private bool _midiMode;
    private string _window = "2 s";

    public AsTraceView() { ClipToBounds = true; MinHeight = 40; }

    /// <summary>Histories (MIDI note numbers, 0 = none) from <paramref name="src"/>, the active
    /// pitch-class mask (absolute), the target note now (0 = none), MIDI mode, the window text.</summary>
    public void Set(float[] src, int detAt, int outAt, int midiAt, int n, int mask, double target, bool midiMode, string window)
    {
        if (_det.Length != n) { _det = new float[n]; _out = new float[n]; _midi = new float[n]; }
        if (n > 0 && midiAt + n <= src.Length)
        {
            Array.Copy(src, detAt, _det, 0, n); Array.Copy(src, outAt, _out, 0, n); Array.Copy(src, midiAt, _midi, 0, n);
        }
        _n = n; _mask = mask; _target = target; _midiMode = midiMode; _window = window;
        // The lanes follow the latest pitch (target, else output, else MIDI) smoothly.
        double follow = target > 0 ? target : 0;
        for (int k = n - 1; k >= 0 && follow <= 0; k--) { if (_out[k] > 0) follow = _out[k]; else if (_midi[k] > 0) follow = _midi[k]; }
        if (follow > 0) _center += (follow - _center) * (Math.Abs(follow - _center) > 7 ? 1 : 0.2);
        InvalidateVisual();
    }

    // Seven lanes: the scale notes around the centre (chromatic when the scale is too thin).
    private int[] Lanes()
    {
        int mask = BitCount(_mask) >= 5 ? _mask : 0x0FFF;
        int c = (int)Math.Round(_center);
        var below = new List<int>(); var above = new List<int>();
        int anchor = c;
        for (int d = 0; d < 12; d++) { if (In(mask, c - d)) { anchor = c - d; break; } if (In(mask, c + d)) { anchor = c + d; break; } }
        for (int m = anchor - 1; below.Count < 3 && m > anchor - 24; m--) if (In(mask, m)) below.Add(m);
        for (int m = anchor + 1; above.Count < 3 && m < anchor + 24; m++) if (In(mask, m)) above.Add(m);
        var lanes = new List<int>();
        for (int i = above.Count - 1; i >= 0; i--) lanes.Add(above[i]);
        lanes.Add(anchor);
        lanes.AddRange(below);
        return lanes.ToArray();   // top → bottom
    }
    private static bool In(int mask, int m) => (mask >> (((m % 12) + 12) % 12) & 1) != 0;
    private static int BitCount(int m) { int c = 0; for (; m != 0; m &= m - 1) c++; return c; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        var lanes = Lanes();
        int nl = lanes.Length;
        double laneH = (h - 2) / nl;
        double LaneY(int i) => 1 + (i + 0.5) * laneH;
        // Pitch → y, piecewise linear between the lanes (so every lane is the same height).
        double Y(double m)
        {
            if (m >= lanes[0]) return LaneY(0) - (m - lanes[0]) * laneH / 2;
            for (int i = 0; i < nl - 1; i++)
                if (m >= lanes[i + 1]) { double t = (lanes[i] - m) / Math.Max(1e-6, lanes[i] - lanes[i + 1]); return LaneY(i) + t * laneH; }
            return LaneY(nl - 1) + (lanes[nl - 1] - m) * laneH / 2;
        }

        int tgtNote = _target > 0 ? (int)Math.Round(_target) : int.MinValue;
        for (int i = 0; i < nl; i++)
        {
            var band = new Rect(0, 1 + i * laneH, w, laneH);
            if (lanes[i] == tgtNote) ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x14), band);
            else if (i % 2 == 0) ctx.FillRectangle(NotaPalette.Wash(NotaPalette.BorderDefault, 0x30), band);
        }
        for (int i = 1; i < 4; i++) ctx.DrawLine(NotaGraph.GridPen, new Point(w * i / 4, 0), new Point(w * i / 4, h));
        if (tgtNote != int.MinValue)
        {
            int ti = Array.IndexOf(lanes, tgtNote);
            if (ti >= 0) ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x40), 1), new Point(0, LaneY(ti)), new Point(w, LaneY(ti)));
        }

        double gx = 20, gw = w - gx - 4;
        double X(int k) => gx + (_n <= 1 ? 0 : (double)k / (_n - 1) * gw);

        // MIDI blocks: runs of one target note.
        if (_midiMode && _n > 1)
        {
            int k = 0;
            while (k < _n)
            {
                if (_midi[k] <= 0) { k++; continue; }
                int note = (int)Math.Round(_midi[k]), s = k;
                while (k < _n && _midi[k] > 0 && (int)Math.Round(_midi[k]) == note) k++;
                double y = Y(note), bh = Math.Max(4, laneH * 0.7);
                bool now = k >= _n;
                var r = new Rect(X(s), y - bh / 2, Math.Max(2, X(k - 1) - X(s)), bh);
                ctx.DrawRectangle(NotaPalette.Wash(NotaPalette.Accent, now ? (byte)0x4C : (byte)0x2E),
                    new Pen(now ? NotaPalette.AccentBright : NotaPalette.Accent, now ? 1.2 : 1), new RoundedRect(r, 2));
            }
        }

        // Detected: teal dots.
        var dot = NotaPalette.Wash(NotaPalette.Teal, 0xB0);
        for (int k = 0; k < _n; k += 2)
            if (_det[k] > 0) ctx.DrawEllipse(dot, null, new Point(X(k), Y(_det[k])), 0.9, 0.9);

        // Output: brass line, broken where unvoiced.
        StreamGeometry? geo = null; StreamGeometryContext? gc = null;
        Point last = default; bool any = false;
        void Flush() { if (gc != null) { gc.Dispose(); ctx.DrawGeometry(null, NotaGraph.PrimaryPen, geo!); gc = null; geo = null; } }
        for (int k = 0; k < _n; k++)
        {
            if (_out[k] <= 0) { Flush(); continue; }
            var p = new Point(X(k), Y(_out[k]));
            if (gc == null) { geo = new StreamGeometry(); gc = geo.Open(); gc.BeginFigure(p, false); }
            else gc.LineTo(p);
            last = p; any = k == _n - 1;
        }
        Flush();
        if (any) ctx.DrawEllipse(NotaPalette.Accent, new Pen(NotaGraph.Ground, 1), last, 2.6, 2.6);

        // Lane labels, the target lit.
        for (int i = 0; i < nl; i++)
        {
            int m = lanes[i];
            var ft = NotaGraph.AxisText(Names[((m % 12) + 12) % 12] + (m / 12 - 1), m == tgtNote ? NotaPalette.AccentBright : null, 6);
            ctx.DrawText(ft, new Point(4, LaneY(i) - ft.Height / 2));
        }
        if (_midiMode) NotaGraph.Legend(ctx, w - 5, 2, ("MIDI", NotaPalette.AccentBright, NotaGraph.Mark.Area), ("input", NotaPalette.TealBright, NotaGraph.Mark.Dashed));
        else NotaGraph.Legend(ctx, w - 5, 2, ("input", NotaPalette.TealBright, NotaGraph.Mark.Dashed), ("corrected", NotaPalette.AccentBright, NotaGraph.Mark.Line));
        var t0 = NotaGraph.AxisText("−" + _window, null, 6);
        ctx.DrawText(t0, new Point(gx, h - t0.Height - 1));
        var t1 = NotaGraph.AxisText("now", null, 6);
        ctx.DrawText(t1, new Point(w - 5 - t1.Width, h - t1.Height - 1));
    }
}

internal sealed class AsHistView : Control
{
    private static readonly string[] Names = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };
    private readonly float[] _h = new float[12];
    private int _mask, _key;
    private string _outName = "";
    private int _hover = -1;

    /// <summary>A pitch class was clicked (toggle it in the scale).</summary>
    public event Action<int>? NoteClicked;

    public AsHistView() { ClipToBounds = true; MinHeight = 30; Cursor = new Cursor(StandardCursorType.Hand); }

    public void Set(float[] src, int at, int mask, int key)
    {
        if (at + 12 <= src.Length) Array.Copy(src, at, _h, 0, 12);
        _mask = mask; _key = key;
        // The most-sung note outside the scale, for the legend.
        int best = -1; float bv = 0.08f;
        for (int i = 0; i < 12; i++) if ((mask >> i & 1) == 0 && _h[i] > bv) { bv = _h[i]; best = i; }
        _outName = best >= 0 ? ", " + Names[best] : "";
        InvalidateVisual();
    }

    private int Col(Point p)
    {
        double gw = (Bounds.Width - 12) / 12;
        int i = (int)Math.Floor((p.X - 6) / gw);
        return i is >= 0 and < 12 ? i : -1;
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int i = Col(e.GetPosition(this));
        if (i >= 0) { NoteClicked?.Invoke(i); e.Handled = true; }
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        int i = Col(e.GetPosition(this));
        if (i != _hover) { _hover = i; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = -1; InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double top = 15, bot = h - 4, gw = (w - 12) / 12, gap = 4;
        for (int i = 0; i < 12; i++)
        {
            bool inScale = (_mask >> i & 1) != 0;
            double v = Math.Clamp(_h[i], 0, 1);
            double bh = Math.Max(1.5, v * (bot - top));
            IBrush fill = inScale ? (i == _key ? NotaPalette.Accent : NotaPalette.Wash(NotaPalette.Accent, 0x8C))
                        : v > 0.08 ? NotaPalette.Wash(NotaPalette.Teal, 0x99) : NotaPalette.BorderDefault;
            var r = new Rect(6 + i * gw + gap / 2, bot - bh, gw - gap, bh);
            if (i == _hover) ctx.FillRectangle(NotaPalette.Wash(NotaPalette.BorderStrong, 0x30), new Rect(6 + i * gw + 1, top - 2, gw - 2, bot - top + 2));
            ctx.DrawRectangle(fill, null, new RoundedRect(r, 2, 2, 0, 0));
        }
        var title = new FormattedText("SUNG NOTES", System.Globalization.CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.SansBold, 7, NotaGraph.TitleInk);
        ctx.DrawText(title, new Point(5, 3));
        NotaGraph.Legend(ctx, w - 5, 2, ("in scale", NotaPalette.AccentBright, NotaGraph.Mark.Area), ("out of scale" + _outName, NotaPalette.TealBright, NotaGraph.Mark.Area));
    }
}
