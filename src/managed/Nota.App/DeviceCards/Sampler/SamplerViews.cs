// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Sampler's windows — one per tab, each a control, not a picture:
//
//   SamplerWaveView    the sample: brass peaks (the lower half a deeper brass), the played
//                      window washed, Start / End as bright brass lines, the loop and its
//                      crossfade when looping, the live playhead. Drag a line to move it.
//   SamplerKeysView    the keyboard C2 … C6 (it follows the root): the root key in brass,
//                      the key that plays the sample at its own pitch outlined, the note
//                      sounding now in teal. Click a key to make it the root.
//   SamplerEnvView     the amp envelope: drag Attack / Decay (and Sustain vertically) /
//                      Release; the loudest voice rides it as a teal node.
//   SamplerFilterView  the filter's real response (the engine's SVF): drag for cutoff (x)
//                      and resonance (y); the cutoff the loudest voice hears after key
//                      tracking and the envelope shows as a teal line.
//
// All in the NotaGraph window (well, hairline, radius 4, corner labels in mono 7).

using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal static class SamplerInk
{
    public static FormattedText Mono(string t, IBrush ink, double size = 7)
        => new(t, NotaNum.Culture, FlowDirection.LeftToRight, NotaFonts.Mono, size, ink);

    public static void Corner(DrawingContext ctx, Rect r, NotaGraph.Corner c, string text, IBrush ink)
    {
        if (text.Length == 0) return;
        var ft = Mono(text, ink);
        const double padX = 6, padY = 3;
        double x = c is NotaGraph.Corner.TopLeft or NotaGraph.Corner.BottomLeft ? r.X + padX : r.Right - padX - ft.Width;
        double y = c is NotaGraph.Corner.TopLeft or NotaGraph.Corner.TopRight ? r.Y + padY : r.Bottom - padY - ft.Height + 1;
        ctx.DrawText(ft, new Point(x, y));
    }
}

// ---- the sample -------------------------------------------------------------------------
internal sealed class SamplerWaveView : Control
{
    private float[] _peaks = Array.Empty<float>();
    private double _start, _end = 1, _ls, _le = 1, _xf, _dur, _play = -1;
    private int _loop;   // 0 Off, 1 Fwd, 2 Ping, 3 Rev
    private int _drag = -1;
    private string _info = "", _loopText = "", _empty = "";

    /// <summary>A handle moved: 0 start, 1 end, 2 loop start, 3 loop end → the new position.</summary>
    public event Action<int, double>? Moved;
    public event Action<int>? DragBegin, DragEnd, ResetHandle;

    private static readonly string[] Ids = { "start", "end", "loopstart", "loopend" };
    public static string HandleId(int h) => Ids[h];

    public SamplerWaveView() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeWestEast); }

    public void SetPeaks(float[] p, double durationSec) { _peaks = p; _dur = durationSec; InvalidateVisual(); }
    public void SetEmpty(string text) { _empty = text; InvalidateVisual(); }
    public void SetPlayhead(double frac) { if (Math.Abs(frac - _play) > 1e-4) { _play = frac; InvalidateVisual(); } }
    public void Set(double s, double e, double ls, double le, int loop, double xfadeSec, string info, string loopText)
    {
        _start = s; _end = e; _ls = ls; _le = le; _loop = loop; _xf = xfadeSec; _info = info; _loopText = loopText;
        InvalidateVisual();
    }

    private double[] Handles() => _loop > 0 ? new[] { _start, _end, _ls, _le } : new[] { _start, _end };

    private int Hit(double xFrac, double w)
    {
        var hs = Handles();
        int best = -1; double bestD = 7 / Math.Max(1, w);   // 7 px grab radius
        for (int i = 0; i < hs.Length; i++) { double d = Math.Abs(xFrac - hs[i]); if (d < bestD) { bestD = d; best = i; } }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        double w = Bounds.Width;
        if (w <= 0 || _peaks.Length == 0 || !e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        double x = e.GetPosition(this).X / w;
        int h = Hit(x, w);
        if (h < 0) return;
        if (e.ClickCount == 2) { ResetHandle?.Invoke(h); e.Handled = true; return; }
        _drag = h;
        DragBegin?.Invoke(h);
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag < 0 || Bounds.Width <= 0) return;
        double f = Math.Clamp(e.GetPosition(this).X / Bounds.Width, 0, 1);
        f = _drag switch
        {
            0 => Math.Min(f, _end - 0.001),
            1 => Math.Max(f, _start + 0.001),
            2 => Math.Clamp(Math.Min(f, _le - 0.001), _start, _end),
            _ => Math.Clamp(Math.Max(f, _ls + 0.001), _start, _end),
        };
        Moved?.Invoke(_drag, f);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag < 0) return;
        int h = _drag; _drag = -1;
        e.Pointer.Capture(null);
        DragEnd?.Invoke(h);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, r);
        if (_peaks.Length == 0)
        {
            var ft = SamplerInk.Mono(_empty, NotaPalette.TextTertiary, 9);
            ctx.DrawText(ft, new Point((w - ft.Width) / 2, (h - ft.Height) / 2));
            return;
        }
        double top = 2, bot = h - 2, cy = (top + bot) / 2, amp = (bot - top) / 2 - 1;

        // The played window, washed; outside it the peaks go quiet.
        ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x14), new Rect(_start * w, 0, (_end - _start) * w, h));
        var grid = NotaGraph.GridPen;
        ctx.DrawLine(grid, new Point(0, cy), new Point(w, cy));
        for (int i = 1; i < 4; i++) ctx.DrawLine(grid, new Point(w * i / 4, 0), new Point(w * i / 4, h));

        // Loop + its crossfade zone (the engine crossfades a forward loop only) — a deeper wash between the markers.
        if (_loop > 0)
        {
            ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x1C), new Rect(_ls * w, 0, Math.Max(1, (_le - _ls) * w), h));
            if (_loop == 1 && _xf > 0 && _dur > 0)
            {
                double xw = Math.Min(_xf / _dur, (_le - _ls) / 2) * w;
                ctx.FillRectangle(NotaPalette.Wash(NotaPalette.Accent, 0x30), new Rect(_le * w - xw, 0, xw, h));
            }
        }

        int n = _peaks.Length / 2;
        var upIn = new Pen(NotaPalette.Accent, 1.2); var downIn = new Pen(NotaPalette.AccentDim, 1.2);
        var outPen = new Pen(NotaPalette.Wash(NotaPalette.Accent, 0x40), 1);
        int step = Math.Max(1, (int)Math.Floor(n / Math.Max(1.0, w / 1.5)));
        for (int i = 0; i < n; i += step)
        {
            double f = (double)i / n, x = f * w;
            float mn = 0, mx = 0;
            for (int j = i; j < Math.Min(n, i + step); j++) { mn = Math.Min(mn, _peaks[j * 2]); mx = Math.Max(mx, _peaks[j * 2 + 1]); }
            bool inWin = f >= _start && f <= _end;
            ctx.DrawLine(inWin ? upIn : outPen, new Point(x, cy), new Point(x, cy - mx * amp));
            ctx.DrawLine(inWin ? downIn : outPen, new Point(x, cy), new Point(x, cy - mn * amp));
        }

        if (_loop > 0)
        {
            var lp = new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0xB0), 1, new DashStyle(new double[] { 3, 2 }, 0));
            ctx.DrawLine(lp, new Point(_ls * w, 0), new Point(_ls * w, h));
            ctx.DrawLine(lp, new Point(_le * w, 0), new Point(_le * w, h));
        }
        var edge = new Pen(NotaPalette.AccentBright, 1.5);
        ctx.DrawLine(edge, new Point(_start * w, 0), new Point(_start * w, h));
        ctx.DrawLine(edge, new Point(_end * w, 0), new Point(_end * w, h));
        if (_play >= 0 && _play <= 1) ctx.DrawLine(new Pen(NotaPalette.TextPrimary, 1), new Point(_play * w, 0), new Point(_play * w, h));

        SamplerInk.Corner(ctx, r, NotaGraph.Corner.TopLeft, _info, NotaPalette.TextTertiary);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.TopRight, _loopText, _loop > 0 ? NotaPalette.AccentBright : NotaPalette.TextTertiary);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.BottomLeft, "start " + NotaNum.Str(_start * _dur, "0.00"), NotaPalette.AccentBright);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.BottomRight, "end " + NotaNum.Str(_end * _dur, "0.00"), NotaPalette.AccentBright);
    }
}

// ---- the keyboard -----------------------------------------------------------------------
internal sealed class SamplerKeysView : Control
{
    private int _root = 60, _lo = 36, _natural = 60;
    private double _playing = -1, _track = 1;
    private string _hint = "";

    public event Action<int>? RootPicked;

    public SamplerKeysView() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    /// <param name="natural">The key that plays the sample at its own pitch (−1 none).</param>
    public void Set(int root, int natural, double playingNote, double keytrack, string hint)
    {
        _root = root; _natural = natural; _playing = playingNote; _track = keytrack; _hint = hint;
        // Four octaves from a C, C2 … C6 by default, shifted so the root stays in view.
        int lo = 36;
        while (_root < lo + 3 && lo > 0) lo -= 12;
        while (_root > lo + 45 && lo < 79) lo += 12;
        _lo = lo;
        InvalidateVisual();
    }

    private const double LabelH = 14;
    private int Keys => 49;

    private int KeyAt(Point p)
    {
        double pad = 4, kw = (Bounds.Width - pad * 2) / Keys;
        if (kw <= 0) return -1;
        int i = (int)Math.Floor((p.X - pad) / kw);
        return i < 0 || i >= Keys ? -1 : Math.Clamp(_lo + i, 0, 127);
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int k = KeyAt(e.GetPosition(this));
        if (k < 0) return;
        RootPicked?.Invoke(k);
        e.Handled = true;
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, r);
        double pad = 4, kw = (w - pad * 2) / Keys, top = LabelH + 4, bot = h - pad;
        double keyH = Math.Max(8, bot - top);
        int playing = _playing >= 0 ? (int)Math.Round(_playing) : -1;
        var hair = new Pen(NotaPalette.GraphBorder, 1);
        for (int i = 0; i < Keys; i++)
        {
            int note = _lo + i;
            int pc = ((note % 12) + 12) % 12;
            bool black = pc is 1 or 3 or 6 or 8 or 10;
            double kh = black ? keyH * 0.62 : keyH;
            var rect = new Rect(pad + i * kw + 1, bot - kh, Math.Max(1, kw - 2), kh);   // resting on the bottom edge
            IBrush fill = note == _root ? NotaPalette.Accent
                : note == playing ? NotaPalette.Teal
                : black ? NotaPalette.SurfaceAbyss : NotaPalette.SurfaceRaised;
            ctx.DrawRectangle(fill, note == _root ? null : hair, new RoundedRect(rect, 0, 0, 2, 2));
            if (note == _natural && note != _root)
                ctx.DrawRectangle(null, new Pen(NotaPalette.AccentBright, 1), new RoundedRect(rect.Deflate(0.5), 0, 0, 2, 2));
            if (pc == 0 && note != _root)
            {
                var c = SamplerInk.Mono(SamplerModel.NoteName(note), NotaPalette.TextAxis);
                if (!black && kh > c.Height + 4) ctx.DrawText(c, new Point(rect.X + (rect.Width - c.Width) / 2, rect.Bottom - c.Height - 2));
            }
            if (note == _root)
            {
                var t = SamplerInk.Mono(SamplerModel.NoteName(note), NotaPalette.TextOnAccent);
                ctx.DrawText(t, new Point(rect.X + (rect.Width - t.Width) / 2, rect.Bottom - t.Height - 2));
            }
        }
        string range = $"range {SamplerModel.NoteName(_lo)} — {SamplerModel.NoteName(_lo + 48)} · root {SamplerModel.NoteName(_root)} · keytrack {NotaNum.Pct(_track)}";
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.TopLeft, range, NotaPalette.TextTertiary);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.TopRight, _hint, NotaPalette.TextAxis);
    }
}

// ---- the amp envelope -------------------------------------------------------------------
internal sealed class SamplerEnvView : Control
{
    private double _a, _d = 0.3, _s = 1, _r = 0.06;
    private SamplerModel.Snapshot? _live;
    private string _tl = "", _br = "";
    private int _drag = -1;   // 0 attack, 1 decay+sustain, 2 release
    private Point _last;

    /// <summary>A node moved: (param id, new normalized value).</summary>
    public event Action<string, double>? Changed;
    public event Action<string>? DragBegin, DragEnd, Reset;

    public SamplerEnvView() { ClipToBounds = true; }

    public void Set(double a, double d, double s, double r, SamplerModel.Snapshot? live, string tl, string br)
    {
        _a = a; _d = d; _s = s; _r = r; _live = live; _tl = tl; _br = br;
        InvalidateVisual();
    }

    // Each timed segment takes 4 … 30 % of the width by its (normalized) value; the rest is
    // the held note.
    private const double SegMin = 0.04, SegSpan = 0.26, Pad = 4;
    private (Point A, Point D, Point S, Point R, Point O) Nodes()
    {
        double w = Bounds.Width, h = Bounds.Height;
        double x0 = Pad, x1 = w - Pad, span = x1 - x0, top = 14, bot = h - 12;
        double aw = (SegMin + SegSpan * _a) * span, dw = (SegMin + SegSpan * _d) * span, rw = (SegMin + SegSpan * _r) * span;
        double sy = top + (1 - _s) * (bot - top);
        var o = new Point(x0, bot);
        var a = new Point(x0 + aw, top);
        var d = new Point(a.X + dw, sy);
        var rEnd = new Point(Math.Max(d.X + 8, x1 - span * 0.06), bot);
        var s = new Point(Math.Max(d.X, rEnd.X - rw), sy);
        return (a, d, s, rEnd, o);
    }

    private static readonly string[] Ids = { "attack", "decay", "release" };

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        var n = Nodes();
        var pts = new[] { n.A, n.D, n.R };
        int best = -1; double bd = 10;
        for (int i = 0; i < 3; i++) { double dd = Math.Abs(p.X - pts[i].X) + Math.Abs(p.Y - pts[i].Y) * 0.5; if (dd < bd) { bd = dd; best = i; } }
        if (best < 0) return;
        if (e.ClickCount == 2) { Reset?.Invoke(Ids[best]); if (best == 1) Reset?.Invoke("sustain"); e.Handled = true; return; }
        _drag = best; _last = p;
        DragBegin?.Invoke(Ids[best]);
        if (best == 1) DragBegin?.Invoke("sustain");
        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        var p = e.GetPosition(this);
        if (_drag < 0)
        {
            var n = Nodes();
            bool near = new[] { n.A, n.D, n.R }.Any(q => Math.Abs(p.X - q.X) + Math.Abs(p.Y - q.Y) * 0.5 < 10);
            Cursor = near ? new Cursor(StandardCursorType.SizeAll) : Cursor.Default;
            return;
        }
        double span = Math.Max(1, Bounds.Width - Pad * 2) * SegSpan;
        double dx = (p.X - _last.X) / span;
        bool fine = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (fine) dx *= 0.2;
        switch (_drag)
        {
            case 0: _a = Math.Clamp(_a + dx, 0, 1); Changed?.Invoke("attack", _a); break;
            case 1:
            {
                _d = Math.Clamp(_d + dx, 0, 1); Changed?.Invoke("decay", _d);
                double top = 14, bot = Bounds.Height - 12;
                double dy = (p.Y - _last.Y) / Math.Max(1, bot - top) * (fine ? 0.2 : 1);
                _s = Math.Clamp(_s - dy, 0, 1); Changed?.Invoke("sustain", _s);
                break;
            }
            default: _r = Math.Clamp(_r - dx, 0, 1); Changed?.Invoke("release", _r); break;   // the release node moves left as it grows
        }
        _last = p;
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag < 0) return;
        DragEnd?.Invoke(Ids[_drag]);
        if (_drag == 1) DragEnd?.Invoke("sustain");
        _drag = -1;
        e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, r);
        var grid = NotaGraph.GridPen;
        for (int i = 1; i < 4; i++) ctx.DrawLine(grid, new Point(w * i / 4, 0), new Point(w * i / 4, h));
        var n = Nodes();
        ctx.DrawLine(grid, new Point(0, n.A.Y), new Point(w, n.A.Y));
        ctx.DrawLine(grid, new Point(0, n.D.Y), new Point(w, n.D.Y));

        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(n.O, true);
            g.LineTo(n.A); g.LineTo(n.D); g.LineTo(n.S); g.LineTo(n.R);
            g.EndFigure(true);
        }
        ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x14), null, geo);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(n.O, false);
            g.LineTo(n.A); g.LineTo(n.D); g.LineTo(n.S); g.LineTo(n.R);
            g.EndFigure(false);
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, line);
        NotaGraph.Node(ctx, n.A, _drag == 0, NotaPalette.AccentBright);
        NotaGraph.Node(ctx, n.D, _drag == 1, NotaPalette.AccentBright);
        NotaGraph.Node(ctx, n.S, false, NotaPalette.AccentBright);
        NotaGraph.Node(ctx, n.R, _drag == 2, NotaPalette.AccentBright);

        // The loudest voice on the curve, in teal.
        if (_live is { Live: true, Stage: >= 0 } lv)
        {
            double env = Math.Clamp(lv.Env, 0, 1), top = n.A.Y, bot = n.O.Y;
            double y = bot - env * (bot - top);
            double x = lv.Stage switch
            {
                0 => n.O.X + (n.A.X - n.O.X) * env,
                1 => n.A.X + (n.D.X - n.A.X) * (1 - env) / Math.Max(1e-3, 1 - _s),
                2 => (n.D.X + n.S.X) / 2,
                _ => n.S.X + (n.R.X - n.S.X) * (1 - env / Math.Max(1e-3, _s)),
            };
            x = Math.Clamp(x, n.O.X, n.R.X);
            NotaGraph.Node(ctx, new Point(x, y), true, NotaPalette.Teal);
        }

        SamplerInk.Corner(ctx, r, NotaGraph.Corner.TopLeft, _tl, NotaPalette.TextTertiary);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.TopRight, "note held", NotaPalette.TextAxis);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.BottomLeft, "0", NotaPalette.TextAxis);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.BottomRight, _br, NotaPalette.TextAxis);
    }
}

// ---- the filter -------------------------------------------------------------------------
internal sealed class SamplerFilterView : Control
{
    private int _type;
    private double _cut = 1, _reso, _liveHz = -1;
    private string _tl = "";
    private bool _drag;
    private Point _last;

    public event Action<string, double>? Changed;
    public event Action? DragBegin, DragEnd, Reset;

    private const double FLo = 30, FHi = 18000, DbTop = 18, DbBot = -42;

    public SamplerFilterView() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeAll); }

    public void Set(int type, double cutNorm, double reso, double liveHz, string tl)
    {
        _type = type; _cut = cutNorm; _reso = reso; _liveHz = liveHz; _tl = tl;
        InvalidateVisual();
    }

    private static double XOf(double hz, double w) => Math.Log(Math.Clamp(hz, FLo, FHi) / FLo) / Math.Log(FHi / FLo) * w;

    // The engine's TPT SVF magnitude at f for cutoff fc (k = 2 − 1.9·reso).
    private static double MagDb(int type, double f, double fc, double reso)
    {
        double k = 2.0 - 1.9 * reso, x = f / fc, re = 1 - x * x, im = k * x;
        double den = Math.Sqrt(re * re + im * im);
        double mag = type switch { 1 => 1 / den, 2 => x * x / den, 3 => x / den, _ => 1 };
        return 20 * Math.Log10(Math.Max(mag, 1e-6));
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2) { Reset?.Invoke(); e.Handled = true; return; }
        _drag = true; _last = e.GetPosition(this);
        DragBegin?.Invoke();
        e.Pointer.Capture(this);
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (!_drag) return;
        var p = e.GetPosition(this);
        double k = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? 0.2 : 1;
        // x across the window spans 30 Hz … 18 kHz; the cutoff param spans 20 Hz … 20 kHz.
        double octPerPx = Math.Log2(FHi / FLo) / Math.Max(1, Bounds.Width);
        _cut = Math.Clamp(_cut + (p.X - _last.X) * octPerPx / Math.Log2(1000) * k, 0, 1);
        _reso = Math.Clamp(_reso - (p.Y - _last.Y) / Math.Max(1, Bounds.Height) * k, 0, 1);
        Changed?.Invoke("cutoff", _cut);
        Changed?.Invoke("resonance", _reso);
        _last = p;
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (!_drag) return;
        _drag = false;
        e.Pointer.Capture(null);
        DragEnd?.Invoke();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, r);
        double top = 12, bot = h - 12;
        double Y(double db) => top + (DbTop - Math.Clamp(db, DbBot, DbTop)) / (DbTop - DbBot) * (bot - top);
        var grid = NotaGraph.GridPen;
        foreach (var hz in new[] { 100.0, 1000, 10000 }) { double x = XOf(hz, w); ctx.DrawLine(grid, new Point(x, 0), new Point(x, h)); }
        ctx.DrawLine(grid, new Point(0, Y(0)), new Point(w, Y(0)));
        ctx.DrawLine(grid, new Point(0, Y(-24)), new Point(w, Y(-24)));

        double fc = SamplerModel.CutoffHz((float)_cut);
        var pts = new List<Point>();
        for (int i = 0; i <= 120; i++)
        {
            double x = i / 120.0 * w;
            double f = FLo * Math.Pow(FHi / FLo, i / 120.0);
            pts.Add(new Point(x, Y(MagDb(_type, f, fc, _reso))));
        }
        var fill = new StreamGeometry();
        using (var g = fill.Open())
        {
            g.BeginFigure(new Point(0, bot), true);
            foreach (var p in pts) g.LineTo(p);
            g.LineTo(new Point(w, bot));
            g.EndFigure(true);
        }
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(pts[0], false);
            for (int i = 1; i < pts.Count; i++) g.LineTo(pts[i]);
            g.EndFigure(false);
        }
        if (_type > 0) ctx.DrawGeometry(NotaPalette.Wash(NotaPalette.Accent, 0x14), null, fill);
        ctx.DrawGeometry(null, _type > 0 ? NotaGraph.PrimaryPen : new Pen(NotaPalette.TextTertiary, 1.2), line);

        if (_type > 0)
        {
            double cx = XOf(fc, w);
            ctx.DrawLine(new Pen(NotaPalette.AccentDim, 1, new DashStyle(new double[] { 3, 3 }, 0)), new Point(cx, 0), new Point(cx, h));
            NotaGraph.Node(ctx, new Point(cx, Y(MagDb(_type, fc, fc, _reso))), _drag, NotaPalette.AccentBright);
            if (_liveHz > 0 && Math.Abs(Math.Log2(_liveHz / fc)) > 0.05)
            {
                double lx = XOf(_liveHz, w);
                ctx.DrawLine(new Pen(NotaPalette.Teal, 1.2), new Point(lx, 0), new Point(lx, h));
            }
        }

        SamplerInk.Corner(ctx, r, NotaGraph.Corner.TopLeft, _tl, NotaPalette.TextTertiary);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.BottomLeft, "30\u2009Hz", NotaPalette.TextAxis);
        SamplerInk.Corner(ctx, r, NotaGraph.Corner.BottomRight, "18\u2009k", NotaPalette.TextAxis);
        var k1 = SamplerInk.Mono("1\u2009k", NotaPalette.TextAxis);
        ctx.DrawText(k1, new Point(XOf(1000, w) + 3, h - 3 - k1.Height + 1));
    }
}
