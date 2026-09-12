// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Custom-drawn views for the Nota Chamber card: the two BLEND-column faders (the two-tone
// convolution / algorithm split and the wet level), the IR waveform with draggable Start /
// Decay trims (also a file drop target), the three-band decay graph, the tail-EQ response
// with its two handles and the modulation LFO trace. Values are pushed in by the card's
// refresh tick; the interactive views raise Changed / DragStarted / DragEnded so the card
// writes the params and records automation gestures.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class ChamberInk
{
    public static Color SlateColor => NotaPalette.InkColor("#6D8FB5");   // convolution
    public static Color MauveColor => NotaPalette.InkColor("#B57286");   // shimmer / pitch, low band
    public static readonly IBrush Slate = new SolidColorBrush(SlateColor);
    public static readonly IBrush Mauve = new SolidColorBrush(MauveColor);
    public static readonly IBrush Veil = NotaPalette.Wash(NotaPalette.SurfaceAbyss, 0xB8);
    public static readonly IBrush GridLine = NotaPalette.SurfaceCard;
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private static readonly Typeface Sans = new("Inter, system-ui, sans-serif", FontStyle.Normal, FontWeight.Bold);

    public static FormattedText Text(string s, double size, IBrush b) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, b);
    public static FormattedText Caps(string s, double size, IBrush b) => new(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, size, b);
    public static IBrush Alpha(Color c, byte a) => new SolidColorBrush(Color.FromArgb(a, c.R, c.G, c.B));

    // A time axis that stays put while the content moves inside it (so turning a knob visibly
    // moves the curve instead of rescaling it away), and steps to the next range only when the
    // content no longer fits or shrinks well below it.
    private static readonly double[] Ranges = { 0.25, 0.5, 0.75, 1, 1.5, 2, 3, 4, 5, 6, 8, 10, 12, 16, 20, 30, 45, 60, 90 };
    public static double Axis(double current, double content)
    {
        double need = Math.Max(0.1, content) * 1.08;
        if (current > 0 && need <= current && need >= current * 0.4) return current;
        foreach (double r in Ranges) if (r >= need) return r;
        return Ranges[^1];
    }
    public static string AxisLabel(double s) => s >= 1 ? FormattableString.Invariant($"{s:0.##} s") : FormattableString.Invariant($"{s * 1000:0} ms");
    // Normalised amplitude → 0..1 height on a −60 dB floor.
    public static double DbHeight(double v, double floorDb = 60) => Math.Clamp((20 * Math.Log10(Math.Max(v, 1e-9)) + floorDb) / floorDb, 0, 1);
}

// A vertical fader. Split = the BLEND C/A fader: slate (convolution) above the handle, brass
// (algorithm) below, value = the algorithm share. Otherwise a brass fill from the bottom.
internal sealed class ChamberFader : Control
{
    private double _v;
    private bool _drag;
    public bool Split { get; init; }
    public double Default { get; set; } = double.NaN;
    public bool Dragging => _drag;
    public event Action<double>? Changed;
    public event Action? DragStarted;
    public event Action? DragEnded;

    public ChamberFader() { Width = 16; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(double v) { if (_drag) return; _v = Math.Clamp(v, 0, 1); InvalidateVisual(); }

    private void Apply(PointerEventArgs e)
    {
        double h = Bounds.Height; if (h <= 2) return;
        _v = Math.Clamp(1 - e.GetPosition(this).Y / h, 0, 1);
        Changed?.Invoke(_v); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.ClickCount == 2 && !double.IsNaN(Default))
        {
            DragStarted?.Invoke(); _v = Default; Changed?.Invoke(_v); DragEnded?.Invoke(); InvalidateVisual(); e.Handled = true; return;
        }
        _drag = true; DragStarted?.Invoke(); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag) { _drag = false; e.Pointer.Capture(null); DragEnded?.Invoke(); } }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 4) return;
        var rect = new Rect(0.5, 0.5, w - 1, h - 1);
        double r = Math.Min(8, w / 2);
        ctx.DrawRectangle(NotaPalette.BgSunken, null, rect, r, r);
        double y = (1 - _v) * h;
        using (ctx.PushClip(new RoundedRect(rect, r)))
        {
            if (Split) ctx.FillRectangle(ChamberInk.Alpha(ChamberInk.SlateColor, 0x59), new Rect(0, 0, w, y));
            ctx.FillRectangle(ChamberInk.Alpha(NotaPalette.AccentColor, 0x48), new Rect(0, y, w, h - y));
        }
        ctx.DrawRectangle(null, new Pen(NotaPalette.BorderDefault, 1), rect, r, r);
        double hy = Math.Clamp(y, 1.5, h - 2.5);
        ctx.DrawRectangle(Split ? NotaPalette.AccentBright : NotaPalette.Accent, null, new Rect(1.5, hy - 1, w - 3, 2), 1, 1);
    }
}

// The IR: a peak envelope of the source (left / LL above the centre line in brass, right / RR
// below in mauve), veiled outside the Start … Decay window. Drag a trim line to move it,
// double-click to reset both. Drop an audio file (Finder or the browser) to load it.
internal sealed class ChamberIrView : Control
{
    private float[] _l = Array.Empty<float>(), _r = Array.Empty<float>();
    private double _sec, _start, _end = 1, _attackSec;
    private bool _reverse, _building, _hover;
    private string? _empty;
    private int _drag;   // 0 none, 1 start, 2 end
    private float[] _res = Array.Empty<float>();
    private double _resSec, _preSec, _axis;
    private const double SourceShare = 0.56;   // top lane = the source + trims, bottom = the result

    public event Action<double, double>? TrimChanged;    // (start, end) fractions
    public event Action<int>? DragStarted;               // 1 start / 2 end
    public event Action<int>? DragEnded;
    public event Action<string>? FileDropped;
    public event Action? DropHover;                      // a file drag is over the view
    public int DraggingHandle => _drag;

    public ChamberIrView()
    {
        ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        DragDrop.SetAllowDrop(this, true);
        DragDrop.AddDragOverHandler(this, (_, e) => { if (BrowserView.IsAcceptableDrag(e)) { e.DragEffects = DragDropEffects.Copy; _hover = true; DropHover?.Invoke(); InvalidateVisual(); e.Handled = true; } });
        DragDrop.AddDragLeaveHandler(this, (_, _) => { _hover = false; InvalidateVisual(); });
        DragDrop.AddDropHandler(this, (_, e) =>
        {
            _hover = false; InvalidateVisual();
            foreach (var it in BrowserView.DroppedItems(e))
                if (it.Path is { Length: > 0 } p) { FileDropped?.Invoke(p); break; }
            e.Handled = true;
        });
    }

    public void SetWave(float[] l, float[] r) { _l = l; _r = r; InvalidateVisual(); }
    // The processed IR as convolved (normalised envelope over `seconds`), after `preSec`.
    public void SetResult(float[] env, double seconds, double preSec)
    {
        _res = env; _resSec = seconds; _preSec = preSec;
        _axis = ChamberInk.Axis(_axis, preSec + seconds);
        InvalidateVisual();
    }
    public void Set(double seconds, double start, double end, double attackSec, bool reverse, bool building, string? emptyText)
    {
        _sec = seconds; _attackSec = attackSec; _reverse = reverse; _building = building; _empty = emptyText;
        if (_drag == 0) { _start = start; _end = end; }
        InvalidateVisual();
    }

    private double X(double f) => f * Bounds.Width;
    private void Apply(PointerEventArgs e)
    {
        double w = Bounds.Width; if (w <= 2) return;
        double f = Math.Clamp(e.GetPosition(this).X / w, 0, 1);
        if (_drag == 1) _start = Math.Min(f, Math.Min(0.9, _end - 0.02));
        else _end = Math.Max(f, Math.Max(0.05, _start + 0.02));
        TrimChanged?.Invoke(_start, _end); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _empty != null) return;
        if (e.GetPosition(this).Y > Bounds.Height * SourceShare) return;   // the RESULT lane is read-only
        if (e.ClickCount == 2)
        {
            DragStarted?.Invoke(1); DragStarted?.Invoke(2);
            _start = 0; _end = 1; TrimChanged?.Invoke(_start, _end);
            DragEnded?.Invoke(1); DragEnded?.Invoke(2);
            InvalidateVisual(); e.Handled = true; return;
        }
        double x = e.GetPosition(this).X;
        _drag = Math.Abs(x - X(_start)) <= Math.Abs(x - X(_end)) ? 1 : 2;
        DragStarted?.Invoke(_drag); e.Pointer.Capture(this); Apply(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag != 0) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag != 0) { int d = _drag; _drag = 0; e.Pointer.Capture(null); DragEnded?.Invoke(d); } }

    private static string Sec(double s) => s >= 1 ? FormattableString.Invariant($"{s:0.00} s") : FormattableString.Invariant($"{s * 1000:0} ms");

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 4 || h < 8) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        if (_empty != null)
        {
            var t = ChamberInk.Text(_empty, 8, NotaPalette.TextTertiary);
            ctx.DrawText(t, new Point((w - t.Width) / 2, (h - t.Height) / 2));
            if (_hover) ctx.DrawRectangle(null, new Pen(NotaPalette.AccentBright, 1.5), new Rect(1, 1, w - 2, h - 2), 3, 3);
            return;
        }
        double full = h;
        h = Math.Floor(full * SourceShare);                 // ---- SOURCE lane (top) ----
        double mid = h / 2, amp = mid - 3;
        ctx.DrawLine(new Pen(ChamberInk.GridLine, 1), new Point(0, mid), new Point(w, mid));
        var clip = ctx.PushClip(new Rect(0, 0, w, h));
        void Env(float[] a, bool up, Color c, double alphaLine)
        {
            int n = a.Length; if (n < 2) return;
            var fill = new StreamGeometry(); var line = new StreamGeometry();
            using (var f = fill.Open())
            using (var l = line.Open())
            {
                f.BeginFigure(new Point(0, mid), true);
                for (int i = 0; i < n; i++)
                {
                    double x = i * w / (n - 1), v = Math.Sqrt(Math.Clamp(a[i], 0f, 1f));   // √ lifts the tail into view
                    var p = new Point(x, up ? mid - v * amp : mid + v * amp);
                    f.LineTo(p);
                    if (i == 0) l.BeginFigure(p, false); else l.LineTo(p);
                }
                f.LineTo(new Point(w, mid));
            }
            ctx.DrawGeometry(ChamberInk.Alpha(c, 0x30), null, fill);
            ctx.DrawGeometry(null, new Pen(ChamberInk.Alpha(c, (byte)(255 * alphaLine)), 1.1), line);
        }
        Env(_l, true, NotaPalette.AccentColor, 1.0);
        Env(_r.Length > 0 ? _r : _l, false, ChamberInk.MauveColor, 0.7);

        double xs = X(_start), xe = X(_end);
        // Attack: the fade-in shape across the head of the kept window.
        if (_attackSec > 0.0005 && _sec > 0)
        {
            double xa = xs + (_attackSec / _sec) * w;
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(xs, h - 2), false);
                for (int i = 1; i <= 16; i++) { double t = i / 16.0; c.LineTo(new Point(xs + (xa - xs) * t, h - 2 - (h - 4) * t * t)); }
            }
            ctx.DrawGeometry(null, new Pen(ChamberInk.Alpha(NotaPalette.AccentBrightColor, 0x90), 1, new DashStyle(new double[] { 2, 2 }, 0)), g);
        }
        ctx.FillRectangle(ChamberInk.Veil, new Rect(0, 0, xs, h));
        ctx.FillRectangle(ChamberInk.Veil, new Rect(xe, 0, w - xe, h));
        var brass = new Pen(NotaPalette.Accent, 1);
        ctx.DrawLine(brass, new Point(xs + 0.5, 0), new Point(xs + 0.5, h));
        ctx.DrawLine(brass, new Point(xe - 0.5, 0), new Point(xe - 0.5, h));
        ctx.DrawRectangle(_drag == 1 ? NotaPalette.AccentBright : NotaPalette.Accent, null, new Rect(xs - 1.5, mid - 7, 3, 14), 1.5, 1.5);
        ctx.DrawRectangle(_drag == 2 ? NotaPalette.AccentBright : NotaPalette.Accent, null, new Rect(xe - 1.5, mid - 7, 3, 14), 1.5, 1.5);

        var st = ChamberInk.Text(FormattableString.Invariant($"start {Sec(_start * _sec)}"), 7, NotaPalette.AccentBright);
        ctx.DrawText(st, new Point(Math.Min(xs + 4, w - st.Width - 2), 2));
        var en = ChamberInk.Text(FormattableString.Invariant($"end {Sec(_end * _sec)}"), 7, NotaPalette.AccentBright);
        ctx.DrawText(en, new Point(Math.Max(2, xe - en.Width - 4), h - en.Height - 2));
        if (_reverse)
        {
            var rv = ChamberInk.Text("◀ REVERSED", 7, ChamberInk.Mauve);
            ctx.DrawText(rv, new Point((xs + xe - rv.Width) / 2, 2));
        }
        clip.Dispose();
        RenderResult(ctx, w, h, full);
        if (_building)
        {
            var b = ChamberInk.Text("building…", 7, NotaPalette.TextTertiary);
            ctx.DrawText(b, new Point(w - b.Width - 4, h + 2));
        }
        if (_hover) ctx.DrawRectangle(null, new Pen(NotaPalette.AccentBright, 1.5), new Rect(1, 1, w - 2, full - 2), 3, 3);
    }

    // ---- RESULT lane (bottom): the IR as it is convolved — pre-delay gap, stretched by Size,
    // faded by Attack / Decay, reversed — on a steady time axis, in dB (−60 dB floor).
    private void RenderResult(DrawingContext ctx, double w, double top, double full)
    {
        double lh = full - top;
        if (lh < 10) return;
        ctx.FillRectangle(ChamberInk.Alpha(NotaPalette.SurfaceAbyss.Color, 0xFF), new Rect(0, top, w, lh));
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, top + 0.5), new Point(w, top + 0.5));
        double axis = _axis > 0 ? _axis : 1, bot = full - 2, peakY = top + 9;
        double X(double t) => Math.Clamp(t / axis, 0, 1) * w;
        var gp = new Pen(ChamberInk.GridLine, 1);
        for (int i = 1; i < 4; i++) ctx.DrawLine(gp, new Point(w * i / 4, top + 1), new Point(w * i / 4, full));
        int n = _res.Length;
        if (n >= 2 && _resSec > 0)
        {
            var fill = new StreamGeometry(); var line = new StreamGeometry();
            double x0 = X(_preSec);
            using (var f = fill.Open())
            using (var l = line.Open())
            {
                f.BeginFigure(new Point(x0, bot), true);
                for (int i = 0; i < n; i++)
                {
                    double x = X(_preSec + (i + 0.5) / n * _resSec);
                    var p = new Point(x, bot - (bot - peakY) * ChamberInk.DbHeight(_res[i]));
                    f.LineTo(p);
                    if (i == 0) l.BeginFigure(p, false); else l.LineTo(p);
                    if (x >= w) break;
                }
                f.LineTo(new Point(Math.Min(w, X(_preSec + _resSec)), bot));
            }
            ctx.DrawGeometry(ChamberInk.Alpha(NotaPalette.AccentColor, 0x3A), null, fill);
            ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, 1), line);
            if (_preSec > 0.0005)
            {
                ctx.DrawLine(new Pen(NotaPalette.Teal, 1, new DashStyle(new double[] { 2, 2 }, 0)), new Point(x0, top + 2), new Point(x0, full));
                var pt = ChamberInk.Text(FormattableString.Invariant($"pre {_preSec * 1000:0} ms"), 7, NotaPalette.Teal);
                ctx.DrawText(pt, new Point(Math.Min(x0 + 3, w - pt.Width - 2), bot - pt.Height));
            }
            if (_preSec + _resSec > axis * 1.001)
            {
                var more = ChamberInk.Text("▸", 8, NotaPalette.AccentBright);
                ctx.DrawText(more, new Point(w - more.Width - 2, peakY - 2));
            }
        }
        var len = ChamberInk.Text(FormattableString.Invariant($"as heard · {_resSec:0.00} s · axis {ChamberInk.AxisLabel(axis)}"), 7, NotaPalette.TextDisabled);
        ctx.DrawText(len, new Point(w - len.Width - 4, top + 2));
    }
}

// DECAY PER BAND: the energy decay (dB, straight lines) of the low / mid / high bands after the
// pre-delay, each ending at its RT60 on the time axis. Drag a band's end marker to set its RT60
// (mid = Decay; low / high are stored relative to it).
internal sealed class ChamberDecayGraph : Control
{
    private double _pre, _tmax = 4;
    private readonly double[] _rt = { 3, 3, 1.5 };
    private bool _freeze;
    private int _drag = -1;
    private float[] _echo = Array.Empty<float>();
    private double _echoSec;
    private string _info = "";
    private static readonly IBrush[] Ink = { ChamberInk.Mauve, NotaPalette.Accent, NotaPalette.Teal };
    private static readonly string[] Names = { "low", "mid", "high" };

    public event Action<int, double>? Changed;   // band, RT60 seconds
    public event Action<int>? DragStarted;
    public event Action<int>? DragEnded;
    public int DraggingBand => _drag;

    public ChamberDecayGraph() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.SizeWestEast); }

    public void Set(double preSec, double rtLow, double rtMid, double rtHigh, bool freeze, string info)
    {
        _pre = preSec; _freeze = freeze; _info = info;
        if (_drag < 0)
        {
            _rt[0] = rtLow; _rt[1] = rtMid; _rt[2] = rtHigh;
            double content = _freeze ? Math.Max(_pre + 2, _tmax / 1.08) : _pre + Math.Max(_rt[0], Math.Max(_rt[1], _rt[2]));
            _tmax = ChamberInk.Axis(_tmax, content);   // steady axis: the curves move, not the scale
        }
        InvalidateVisual();
    }
    // The algorithm's rendered impulse response (normalised envelope over `seconds`).
    public void SetEchogram(float[] env, double seconds) { _echo = env; _echoSec = seconds; InvalidateVisual(); }

    private const double Pad = 6, Top = 16;
    private double X(double t) => Pad + t / _tmax * (Bounds.Width - 2 * Pad);
    private double T(double x) => (x - Pad) / Math.Max(1, Bounds.Width - 2 * Pad) * _tmax;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || _freeze) return;
        double x = e.GetPosition(this).X, best = double.MaxValue;
        for (int b = 0; b < 3; b++) { double d = Math.Abs(x - X(_pre + _rt[b])); if (d < best) { best = d; _drag = b; } }
        DragStarted?.Invoke(_drag); e.Pointer.Capture(this); OnPointerMoved(e); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0) return;
        _rt[_drag] = Math.Clamp(T(e.GetPosition(this).X) - _pre, 0.05, 70);
        Changed?.Invoke(_drag, _rt[_drag]); InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag >= 0) { int b = _drag; _drag = -1; e.Pointer.Capture(null); DragEnded?.Invoke(b); } }

    private static string S(double s) => s >= 10 ? FormattableString.Invariant($"{s:0} s") : FormattableString.Invariant($"{s:0.0} s");

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 24) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        var gp = new Pen(ChamberInk.GridLine, 1);
        double bot = h - 4;
        ctx.DrawLine(gp, new Point(0, (Top + bot) / 2), new Point(w, (Top + bot) / 2));
        ctx.DrawLine(gp, new Point(w / 3, Top - 4), new Point(w / 3, h));
        ctx.DrawLine(gp, new Point(2 * w / 3, Top - 4), new Point(2 * w / 3, h));
        var cap = ChamberInk.Caps("DECAY PER BAND", 7, NotaPalette.TextTertiary);
        ctx.DrawText(cap, new Point(5, 3));
        if (_info.Length > 0) ctx.DrawText(ChamberInk.Text(_info, 7, NotaPalette.TextDisabled), new Point(5 + cap.Width + 6, 3));

        // The real echogram (dB, −60 dB floor) behind the RT60 lines — Size, Diffusion, the
        // mode, modulation and Freeze all show up here.
        int en = _echo.Length;
        if (en >= 2 && _echoSec > 0)
        {
            var fill = new StreamGeometry(); var line = new StreamGeometry();
            using (var f = fill.Open())
            using (var l = line.Open())
            {
                f.BeginFigure(new Point(X(_pre), bot), true);
                for (int i = 0; i < en; i++)
                {
                    double x = X(_pre + (i + 0.5) / en * _echoSec);
                    var p = new Point(x, bot - (bot - Top) * ChamberInk.DbHeight(_echo[i]));
                    f.LineTo(p);
                    if (i == 0) l.BeginFigure(p, false); else l.LineTo(p);
                    if (x >= w) break;
                }
                f.LineTo(new Point(Math.Min(w, X(_pre + _echoSec)), bot));
            }
            ctx.DrawGeometry(ChamberInk.Alpha(NotaPalette.AccentColor, 0x22), null, fill);
            ctx.DrawGeometry(null, new Pen(ChamberInk.Alpha(NotaPalette.AccentColor, 0x60), 0.8), line);
        }

        // legend (right-aligned): swatch + "low 6.1 s"
        double lx = w - 5;
        for (int b = 2; b >= 0; b--)
        {
            var t = ChamberInk.Text(FormattableString.Invariant($"{Names[b]} {(_freeze ? "∞" : S(_rt[b]))}"), 7, b == 1 ? NotaPalette.AccentBright : NotaPalette.TextSecondary);
            lx -= t.Width; ctx.DrawText(t, new Point(lx, 3));
            lx -= 11; ctx.FillRectangle(Ink[b], new Rect(lx, 7, 8, 2));
            lx -= 8;
        }

        double x0 = X(0), xp = X(_pre);
        for (int b = 0; b < 3; b++)
        {
            var g = new StreamGeometry();
            using (var c = g.Open())
            {
                c.BeginFigure(new Point(x0, Top), false);
                c.LineTo(new Point(xp, Top));
                if (_freeze) c.LineTo(new Point(w, Top));
                else
                {
                    // −60 dB at pre + RT on the same dB scale as the echogram (a straight decay).
                    c.LineTo(new Point(X(_pre + _rt[b]), bot));
                }
            }
            ctx.DrawGeometry(null, new Pen(Ink[b], b == 1 ? 1.8 : 1.5), g);
            if (!_freeze)
            {
                double hx = Math.Clamp(X(_pre + _rt[b]), 4, w - 4);
                ctx.DrawEllipse(Ink[b], new Pen(NotaPalette.BgSunken, 2), new Point(hx, bot - 1), _drag == b ? 4.5 : 3.5, _drag == b ? 4.5 : 3.5);
            }
        }
        var tm = ChamberInk.Text(ChamberInk.AxisLabel(_tmax), 7, NotaPalette.TextDisabled);
        ctx.DrawText(tm, new Point(w - tm.Width - 4, bot - tm.Height - 3));
    }
}

// TAIL EQ response (log 30 Hz … 18 kHz, ±20 dB): low cut + low shelf, high shelf + high cut
// (the same RBJ biquads the engine runs). Handle 0 (brass) = low cut (X) + low shelf gain (Y);
// handle 1 = high cut (X) + high shelf gain (Y).
internal sealed class ChamberEqCurve : Control
{
    private double _lc, _lg = 0.5, _hs = 0.5, _hc = 1;
    private int _drag = -1;
    private const double FLo = 30, FHi = 18000, DbSpan = 20, Sr = 48000;

    public event Action<int, double, double>? Changed;   // handle, cut (norm), gain (norm)
    public event Action<int>? DragStarted;
    public event Action<int>? DragEnded;
    public int DraggingHandle => _drag;

    public ChamberEqCurve() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    public void Set(double lowCut, double lowGain, double highShelf, double highCut)
    { if (_drag >= 0) return; _lc = lowCut; _lg = lowGain; _hs = highShelf; _hc = highCut; InvalidateVisual(); }

    public static double LowCutHz(double v) => 20 * Math.Pow(100, Math.Clamp(v, 0, 1));
    public static double HighCutHz(double v) => 1000 * Math.Pow(20, Math.Clamp(v, 0, 1));
    private double X(double hz) => Math.Log(Math.Clamp(hz, FLo, FHi) / FLo) / Math.Log(FHi / FLo) * Bounds.Width;
    private double Hz(double x) => FLo * Math.Pow(FHi / FLo, Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1));
    private double Y(double db) => Bounds.Height / 2 - db / DbSpan * (Bounds.Height / 2 - 3);

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        double d0 = Dist(p, X(LowCutHz(_lc)), Y((_lg - 0.5) * 36)), d1 = Dist(p, X(HighCutHz(_hc)), Y((_hs - 0.5) * 36));
        _drag = d0 <= d1 ? 0 : 1;
        DragStarted?.Invoke(_drag); e.Pointer.Capture(this); OnPointerMoved(e); e.Handled = true;
    }
    private static double Dist(Point p, double x, double y) => Math.Sqrt((p.X - x) * (p.X - x) + (p.Y - y) * (p.Y - y));
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag < 0) return;
        var p = e.GetPosition(this);
        double hz = Hz(p.X);
        double db = Math.Clamp(-(p.Y - Bounds.Height / 2) / (Bounds.Height / 2 - 3) * DbSpan, -18, 18);
        double gn = Math.Clamp(0.5 + db / 36, 0, 1);
        if (_drag == 0) { _lc = p.X <= 2 ? 0 : Math.Clamp(Math.Log(hz / 20) / Math.Log(100), 0, 1); _lg = gn; Changed?.Invoke(0, _lc, _lg); }
        else { _hc = p.X >= Bounds.Width - 2 ? 1 : Math.Clamp(Math.Log(hz / 1000) / Math.Log(20), 0, 1); _hs = gn; Changed?.Invoke(1, _hc, _hs); }
        InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag >= 0) { int d = _drag; _drag = -1; e.Pointer.Capture(null); DragEnded?.Invoke(d); } }

    // |H| (dB) of the four stages at f.
    private double Db(double f)
    {
        double lc = LowCutHz(_lc), hc = HighCutHz(_hc), lg = (_lg - 0.5) * 36, hg = (_hs - 0.5) * 36;
        double db = 0;
        if (_lc > 0.001) db += Mag(Biquad.Hp(lc), f);
        if (Math.Abs(lg) > 0.05) db += Mag(Biquad.LowShelf(Math.Max(250, lc * 2), lg), f);
        if (Math.Abs(hg) > 0.05) db += Mag(Biquad.HighShelf(Math.Min(4000, hc * 0.5), hg), f);
        if (_hc < 0.999) db += Mag(Biquad.Lp(hc), f);
        return db;
    }
    private static double Mag((double b0, double b1, double b2, double a0, double a1, double a2) c, double f)
    {
        double w = 2 * Math.PI * f / Sr, cw = Math.Cos(w), sw = Math.Sin(w), c2 = Math.Cos(2 * w), s2 = Math.Sin(2 * w);
        double nr = c.b0 + c.b1 * cw + c.b2 * c2, ni = -(c.b1 * sw + c.b2 * s2);
        double dr = c.a0 + c.a1 * cw + c.a2 * c2, di = -(c.a1 * sw + c.a2 * s2);
        return 10 * Math.Log10((nr * nr + ni * ni) / Math.Max(1e-30, dr * dr + di * di));
    }
    private static class Biquad
    {
        public static (double, double, double, double, double, double) Hp(double f) { double w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071); return ((1 + c) / 2, -(1 + c), (1 + c) / 2, 1 + al, -2 * c, 1 - al); }
        public static (double, double, double, double, double, double) Lp(double f) { double w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / (2 * 0.7071); return ((1 - c) / 2, 1 - c, (1 - c) / 2, 1 + al, -2 * c, 1 - al); }
        public static (double, double, double, double, double, double) LowShelf(double f, double db)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / 2 * Math.Sqrt(2), sa = 2 * Math.Sqrt(A) * al;
            return (A * ((A + 1) - (A - 1) * c + sa), 2 * A * ((A - 1) - (A + 1) * c), A * ((A + 1) - (A - 1) * c - sa), (A + 1) + (A - 1) * c + sa, -2 * ((A - 1) + (A + 1) * c), (A + 1) + (A - 1) * c - sa);
        }
        public static (double, double, double, double, double, double) HighShelf(double f, double db)
        {
            double A = Math.Pow(10, db / 40), w = 2 * Math.PI * f / Sr, c = Math.Cos(w), al = Math.Sin(w) / 2 * Math.Sqrt(2), sa = 2 * Math.Sqrt(A) * al;
            return (A * ((A + 1) + (A - 1) * c + sa), -2 * A * ((A - 1) + (A + 1) * c), A * ((A + 1) + (A - 1) * c - sa), (A + 1) - (A - 1) * c + sa, 2 * ((A - 1) - (A + 1) * c), (A + 1) - (A - 1) * c - sa);
        }
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 16) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        var gp = new Pen(ChamberInk.GridLine, 1);
        ctx.DrawLine(gp, new Point(0, h / 2), new Point(w, h / 2));
        foreach (double hz in new[] { 100.0, 1000.0, 10000.0 }) { double gx = X(hz); ctx.DrawLine(gp, new Point(gx, 0), new Point(gx, h)); }

        var line = new StreamGeometry(); var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            double y0 = Math.Clamp(Y(Db(FLo)), 1, h - 1);
            lc.BeginFigure(new Point(0, y0), false);
            fc.BeginFigure(new Point(0, h / 2), true); fc.LineTo(new Point(0, y0));
            for (double x = 1.5; x <= w; x += 1.5)
            {
                var p = new Point(x, Math.Clamp(Y(Db(Hz(x))), 1, h - 1));
                lc.LineTo(p); fc.LineTo(p);
            }
            fc.LineTo(new Point(w, h / 2));
        }
        ctx.DrawGeometry(ChamberInk.Alpha(NotaPalette.AccentColor, 0x1C), null, fill);
        ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, 1.6), line);

        var t0 = ChamberInk.Text("30", 7, NotaPalette.TextDisabled);
        ctx.DrawText(t0, new Point(4, h - t0.Height - 2));
        var t1 = ChamberInk.Text("18k Hz", 7, NotaPalette.TextDisabled);
        ctx.DrawText(t1, new Point(w - t1.Width - 4, h - t1.Height - 2));

        var ring = new Pen(NotaPalette.BgSunken, 2);
        double hx0 = Math.Clamp(X(LowCutHz(_lc)), 4, w - 4), hy0 = Math.Clamp(Y((_lg - 0.5) * 36), 4, h - 4);
        double hx1 = Math.Clamp(X(HighCutHz(_hc)), 4, w - 4), hy1 = Math.Clamp(Y((_hs - 0.5) * 36), 4, h - 4);
        ctx.DrawEllipse(NotaPalette.AccentBright, ring, new Point(hx0, hy0), _drag == 0 ? 4.5 : 3.8, _drag == 0 ? 4.5 : 3.8);
        ctx.DrawEllipse(NotaPalette.TextSecondary, ring, new Point(hx1, hy1), _drag == 1 ? 4.5 : 3.8, _drag == 1 ? 4.5 : 3.8);
    }
}

// The modulation LFO: a teal sine scrolling at the Rate, its swing following the Depth.
internal sealed class ChamberLfoView : Control
{
    private double _rate = 0.4, _depth = 0.3, _phase;
    private DateTime _last = DateTime.UtcNow;
    public ChamberLfoView() { ClipToBounds = true; }

    public void Tick(double rateHz, double depth)
    {
        var now = DateTime.UtcNow;
        double dt = Math.Clamp((now - _last).TotalSeconds, 0, 0.1);
        _last = now; _rate = rateHz; _depth = depth;
        _phase = (_phase + dt * rateHz) % 1.0;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 8 || h < 8) return;
        ctx.DrawLine(new Pen(ChamberInk.GridLine, 1), new Point(0, h / 2), new Point(w, h / 2));
        double amp = (h / 2 - 3) * (0.12 + 0.88 * Math.Clamp(_depth, 0, 1));
        const double cycles = 3;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            for (double x = 0; x <= w; x += 1)
            {
                double ph = (x / w * cycles - _phase) * 2 * Math.PI;
                var p = new Point(x, h / 2 - Math.Sin(ph) * amp);
                if (x == 0) c.BeginFigure(p, false); else c.LineTo(p);
            }
        }
        ctx.DrawGeometry(null, new Pen(NotaPalette.Teal, 1.5), g);
        var t = ChamberInk.Text(_rate >= 1 ? FormattableString.Invariant($"{_rate:0.0} Hz") : FormattableString.Invariant($"{_rate:0.00} Hz"), 7, NotaPalette.TextTertiary);
        ctx.DrawText(t, new Point(w - t.Width - 3, 1));
    }
}
