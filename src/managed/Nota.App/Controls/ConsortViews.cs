// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Custom-drawn views for the Nota Consort card: the patch-bay jack tables (mirroring the
// engine's jack indices), LFO shape glyphs, the dual-ladder response (drag = cutoff /
// resonance), the BBD tap display, the 16-step type strip, the step pitch lane, the jack
// field with its cables (drag jack → jack to patch, Alt-click to pull) and the source ×
// destination matrix. Values are pushed in by the card's refresh tick; edits come back as
// events so the card writes the params and brackets automation gestures.

using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// ---- patch-bay jacks (indices == the engine's consort::Src / consort::Dst — append only) ----
internal static class ConsortJacks
{
    public const int JackScale = 63;
    public enum Tone { Mod, Env, Audio }

    public readonly record struct Jack(bool Out, int Index, string Short, string Name, Tone Tone);

    // Sources (outputs), index 1..20.
    public static readonly Jack[] Sources =
    {
        new(true, 0, "", "—", Tone.Mod),
        new(true, 1, "out", "LFO out", Tone.Mod),
        new(true, 2, "1", "Env 1 out", Tone.Env),
        new(true, 3, "2", "Env 2 out", Tone.Env),
        new(true, 4, "1", "Osc 1 out", Tone.Audio),
        new(true, 5, "2", "Osc 2 out", Tone.Audio),
        new(true, 6, "3", "Osc 3 out", Tone.Audio),
        new(true, 7, "4", "Osc 4 out", Tone.Audio),
        new(true, 8, "n", "Noise out", Tone.Audio),
        new(true, 9, "out", "Filt out", Tone.Audio),
        new(true, 10, "pit", "Seq pitch", Tone.Audio),
        new(true, 11, "gt", "Seq gate", Tone.Audio),
        new(true, 12, "clk", "Clock out", Tone.Audio),
        new(true, 13, "pit", "Kbd pitch", Tone.Mod),
        new(true, 14, "gt", "Kbd gate", Tone.Mod),
        new(true, 15, "vel", "Velocity", Tone.Mod),
        new(true, 16, "at", "Aftertouch", Tone.Mod),
        new(true, 17, "wh", "Mod wheel", Tone.Mod),
        new(true, 18, "a1›", "Atten 1 out", Tone.Mod),
        new(true, 19, "a2›", "Atten 2 out", Tone.Mod),
        new(true, 20, "Σ›", "Sum out", Tone.Mod),
    };
    // Destinations (inputs), index 1..22.
    public static readonly Jack[] Dests =
    {
        new(false, 0, "", "—", Tone.Mod),
        new(false, 1, "rate", "LFO rate", Tone.Mod),
        new(false, 2, "g1", "Gate 1 in", Tone.Mod),
        new(false, 3, "g2", "Gate 2 in", Tone.Mod),
        new(false, 4, "p", "Osc pitch", Tone.Mod),
        new(false, 5, "p1", "Osc 1 pitch", Tone.Mod),
        new(false, 6, "p2", "Osc 2 pitch", Tone.Mod),
        new(false, 7, "p3", "Osc 3 pitch", Tone.Mod),
        new(false, 8, "p4", "Osc 4 pitch", Tone.Mod),
        new(false, 9, "pw", "Osc PWM 1–4", Tone.Mod),
        new(false, 10, "c1", "Filt 1 cutoff", Tone.Mod),
        new(false, 11, "c2", "Filt 2 cutoff", Tone.Mod),
        new(false, 12, "res", "Filt res", Tone.Mod),
        new(false, 13, "in", "Filt in", Tone.Mod),
        new(false, 14, "cv", "VCA CV", Tone.Mod),
        new(false, 15, "in", "VCA in", Tone.Mod),
        new(false, 16, "tim", "Delay time", Tone.Mod),
        new(false, 17, "fb", "Delay feedback", Tone.Mod),
        new(false, 18, "mix", "Delay mix", Tone.Mod),
        new(false, 19, "ext", "Ext in", Tone.Mod),
        new(false, 20, "a1", "Atten 1 in", Tone.Mod),
        new(false, 21, "a2", "Atten 2 in", Tone.Mod),
        new(false, 22, "Σ", "Sum in", Tone.Mod),
    };
    // Inputs whose internal normal a cable breaks.
    public static readonly HashSet<int> Normalled = new() { 2, 3, 13, 15, 19 };
    public static int Points => Sources.Length - 1 + Dests.Length - 1;

    public static IBrush ToneBrush(Tone t) => t switch { Tone.Env => NotaPalette.Teal, Tone.Audio => NotaPalette.Accent, _ => ChamberInk.Mauve };
    public static Color ToneColor(Tone t) => t switch { Tone.Env => NotaPalette.Teal.Color, Tone.Audio => NotaPalette.AccentColor, _ => ChamberInk.MauveColor };
    public static Jack Src(int i) => i > 0 && i < Sources.Length ? Sources[i] : Sources[0];
    public static Jack Dst(int i) => i > 0 && i < Dests.Length ? Dests[i] : Dests[0];
    public static string ShortName(string n) => n.Replace(" out", "").Replace(" in", "").Replace("Osc ", "Osc").Replace("Filt ", "Filt").Replace(" cutoff", "").Replace("Delay ", "Dly ").Replace(" pitch", "").Replace("Env ", "Env");
    public static int FromNorm(float v) => Math.Clamp((int)Math.Round(v * JackScale), 0, JackScale);
    public static float ToNorm(int i) => i / (float)JackScale;
}

// One cable as the views see it: slot index, source, destination, depth −1..1.
internal readonly record struct ConsortCable(int Slot, int Src, int Dst, float Depth);

// ---- LFO shape glyphs: 0 sine, 1 ramp up, 2 ramp down, 3 square, 4 S&H, 5 smooth random ----
internal sealed class ConsortLfoIcon : Control
{
    private readonly int _s;
    public IBrush Stroke { get; set; } = NotaPalette.TextTertiary;
    public ConsortLfoIcon(int shape, double w = 16, double h = 8) { _s = shape; Width = w; Height = h; }
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, t = 0.8, b = h - 0.8, m = h / 2;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            switch (_s)
            {
                case 0:
                    c.BeginFigure(new Point(0, m), false);
                    for (int i = 1; i <= 24; i++) { double x = i / 24.0; c.LineTo(new Point(x * w, m - Math.Sin(x * Math.PI * 2) * (m - t))); }
                    break;
                case 1:
                    c.BeginFigure(new Point(0, b), false);
                    c.LineTo(new Point(w * 0.48, t)); c.LineTo(new Point(w * 0.48, b)); c.LineTo(new Point(w * 0.96, t)); c.LineTo(new Point(w * 0.96, b));
                    break;
                case 2:
                    c.BeginFigure(new Point(0, t), false);
                    c.LineTo(new Point(w * 0.48, b)); c.LineTo(new Point(w * 0.48, t)); c.LineTo(new Point(w * 0.96, b));
                    break;
                case 3:
                    c.BeginFigure(new Point(0, b), false);
                    c.LineTo(new Point(0, t)); c.LineTo(new Point(w * 0.5, t)); c.LineTo(new Point(w * 0.5, b)); c.LineTo(new Point(w, b));
                    break;
                case 4:
                    double[] lv = { 0.6, 0.2, 0.8, 0.45 };
                    c.BeginFigure(new Point(0, t + (b - t) * lv[0]), false);
                    for (int i = 0; i < 4; i++)
                    {
                        double y = t + (b - t) * lv[i];
                        c.LineTo(new Point(w * i / 4.0, y)); c.LineTo(new Point(w * (i + 1) / 4.0, y));
                    }
                    break;
                default:
                    c.BeginFigure(new Point(0, b), false);
                    double[] rv = { 0.9, 0.35, 0.7, 0.1, 0.5 };
                    for (int i = 1; i <= 24; i++)
                    {
                        double x = i / 24.0 * 4; int k = Math.Min(3, (int)x); double f = x - k; f = 0.5 - 0.5 * Math.Cos(f * Math.PI);
                        c.LineTo(new Point(i / 24.0 * w, t + (b - t) * (rv[k] + (rv[k + 1] - rv[k]) * f)));
                    }
                    break;
            }
        }
        ctx.DrawGeometry(null, new Pen(Stroke, 1.3, lineJoin: PenLineJoin.Round), g);
    }
}

// ---- small jack glyph (a status light on the panels: patched = filled in the cable colour) ----
internal sealed class ConsortJackDot : Control
{
    private IBrush? _fill;
    public ConsortJackDot(double d = 11) { Width = d; Height = d; }
    public void Set(IBrush? patched) { if (!ReferenceEquals(patched, _fill)) { _fill = patched; InvalidateVisual(); } }
    public override void Render(DrawingContext ctx)
    {
        double r = Math.Min(Bounds.Width, Bounds.Height) / 2 - 0.75;
        var c = new Point(Bounds.Width / 2, Bounds.Height / 2);
        ctx.DrawEllipse(NotaPalette.BgSunken, new Pen(_fill ?? NotaPalette.BorderStrong, 1.5), c, r, r);
        if (_fill != null) ctx.DrawEllipse(_fill, null, c, r * 0.36, r * 0.36);
    }
}

// ---- dual transistor-ladder response: L (slate) / R (brass) or the series HP→LP curve ----
internal sealed class ConsortFilterCurve : Control
{
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private double _cut = 0.5, _reso, _space, _comp;
    private int _mode = 1;
    private bool _drag;
    public Action<double, double>? Changed;
    public event Action? DragStarted;
    public event Action? DragEnded;
    public ConsortFilterCurve() { Cursor = new Cursor(StandardCursorType.Hand); ClipToBounds = true; }

    public void Set(double cutNorm, double resoNorm, double spacingOct, int mode, bool bassComp)
    { _cut = cutNorm; _reso = resoNorm; _space = spacingOct; _mode = mode; _comp = bassComp ? 1 : 0; InvalidateVisual(); }

    private void Apply(PointerEventArgs e)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        var p = e.GetPosition(this);
        _cut = Math.Clamp(p.X / w, 0, 1); _reso = Math.Clamp(1 - p.Y / h, 0, 1);
        Changed?.Invoke(_cut, _reso); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; _drag = true; DragStarted?.Invoke(); Apply(e); e.Pointer.Capture(this); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag) { _drag = false; DragEnded?.Invoke(); e.Pointer.Capture(null); } }

    // |H| of a 4-pole ladder at ω/ωc = wr (resonance k), low-pass or high-pass tap.
    private double Mag(double wr, bool hp)
    {
        double k = 4.3 * _reso * 0.95;
        double r2 = 1 - wr * wr, i2 = 2 * wr;
        double r4 = r2 * r2 - i2 * i2, i4 = 2 * r2 * i2;
        double dr = r4 + k, di = i4;
        double den = Math.Sqrt(dr * dr + di * di);
        double num = hp ? wr * wr * wr * wr : 1;
        return (1 + _comp * k) * num / Math.Max(den, 1e-9);
    }
    private static double Db(double m) => 20 * Math.Log10(Math.Max(m, 1e-6));

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        var gp = new Pen(NotaPalette.SurfaceCard, 1);
        foreach (double hz in new[] { 100.0, 1000.0, 10000.0 }) { double gx = Math.Log10(hz / 20) / 3 * w; ctx.DrawLine(gp, new Point(gx, 0), new Point(gx, h)); }
        double y0 = h * 0.42;
        ctx.DrawLine(gp, new Point(0, y0), new Point(w, y0));
        double Y(double db) => Math.Clamp(y0 - db * (h * 0.02), 1, h - 1);
        double cutB = Math.Clamp(_cut + _space / 10.0, -0.5, 1.5);   // spacing in decades of the 3-decade axis

        StreamGeometry Curve(Func<double, double> db)
        {
            var g = new StreamGeometry();
            using var c = g.Open();
            c.BeginFigure(new Point(0, Y(db(0))), false);
            for (double x = 1.5; x <= w; x += 1.5) c.LineTo(new Point(x, Y(db(x / w))));
            return g;
        }
        double Wr(double xn, double cn) => Math.Pow(1000, xn - cn);
        var brass = NotaPalette.Accent;
        if (_mode == 0)
        {
            var faint = NotaPalette.Wash(NotaPalette.Ink("#6D8FB5"), 0x55);
            ctx.DrawGeometry(null, new Pen(faint, 1), Curve(x => Db(Mag(Wr(x, _cut), true))));
            ctx.DrawGeometry(null, new Pen(brass, 1.5), Curve(x => Db(Mag(Wr(x, _cut), true) * Mag(Wr(x, cutB), false))));
        }
        else
        {
            ctx.DrawGeometry(null, new Pen(ChamberInk.Slate, 1.5), Curve(x => Db(Mag(Wr(x, _cut), _mode == 2))));
            ctx.DrawGeometry(null, new Pen(brass, 1.5), Curve(x => Db(Mag(Wr(x, cutB), false))));
        }
        string Hz(double n) { double hz = 20 * Math.Pow(1000, n); return hz >= 1000 ? $"{hz / 1000:0.00} kHz" : $"{hz:0} Hz"; }
        var lt = new FormattedText((_mode == 0 ? "HP " : "L ") + Hz(_cut), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, _mode == 0 ? NotaPalette.TextSecondary : ChamberInk.Slate);
        var rt = new FormattedText((_mode == 0 ? "LP " : "R ") + Hz(cutB), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, NotaPalette.AccentBright);
        ctx.DrawText(lt, new Point(4, 3));
        ctx.DrawText(rt, new Point(w - rt.Width - 4, 3));
        double hx = Math.Clamp(_cut * w, 4, w - 4), hy = Math.Clamp(Y(Db(Mag(1, _mode == 2 || _mode == 0))), 4, h - 4);
        ctx.DrawEllipse(_mode == 1 ? ChamberInk.Slate : NotaPalette.AccentBright, new Pen(NotaPalette.BgSunken, 2), new Point(hx, hy), 3.5, 3.5);
    }
}

// ---- BBD taps: the L lane on top, R below; bar height = level of each repeat ----
internal sealed class ConsortDelayView : Control
{
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private double _tl = 0.26, _tr = 0.39, _fb = 0.6, _mix = 0.4;
    private bool _ping = true, _digital;
    public ConsortDelayView() { ClipToBounds = true; }
    public void Set(double tl, double tr, double fb, double mix, bool ping, bool digital)
    { _tl = tl; _tr = tr; _fb = fb; _mix = mix; _ping = ping; _digital = digital; InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        double mid = h / 2;
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(0, mid), new Point(w, mid));
        // Taps over a window that shows about four repeats.
        var taps = new List<(double T, double A, bool R)>();
        if (_ping)
        {
            double t = 0, a = 1;
            for (int k = 0; k < 16; k++) { bool r = (k & 1) == 1; t += r ? _tr : _tl; if (k > 0) a *= _fb; taps.Add((t, a, r)); }
        }
        else
        {
            for (int k = 1; k <= 8; k++) { taps.Add((_tl * k, Math.Pow(_fb, k - 1), false)); taps.Add((_tr * k, Math.Pow(_fb, k - 1), true)); }
        }
        double span = Math.Max(_tl, _tr) * 4.2;
        span = ChamberInk.Axis(0, span);
        var bl = ChamberInk.Slate; var br = NotaPalette.Accent;
        foreach (var (t, a, r) in taps)
        {
            if (t > span) continue;
            double x = 6 + (w - 12) * t / span;
            double ht = (mid - 8) * Math.Clamp(ChamberInk.DbHeight(a * Math.Max(0.15, _mix), 48), 0, 1);
            if (ht < 1) continue;
            if (!r) ctx.FillRectangle(bl, new Rect(x - 1.5, mid - 3 - ht, 3, ht));
            else ctx.FillRectangle(br, new Rect(x - 1.5, mid + 3, 3, ht));
        }
        var ll = new FormattedText("L", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, bl);
        var rl = new FormattedText("R", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, br);
        ctx.DrawText(ll, new Point(4, 2)); ctx.DrawText(rl, new Point(4, h - rl.Height - 1));
        var info = new FormattedText($"fb {_fb * 100:0} % · {(_digital ? "digital" : "compander on")}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, NotaPalette.TextSecondary);
        ctx.DrawText(info, new Point(w - info.Width - 4, 2));
        var ax = new FormattedText(ChamberInk.AxisLabel(span), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, NotaPalette.TextDisabled);
        ctx.DrawText(ax, new Point(w - ax.Width - 4, h - ax.Height - 1));
    }
}

// ---- the 16 step cells: type (note / ratchet / tie / rest), playhead, length. Click / drag paints ----
internal sealed class ConsortStepGrid : Control
{
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private readonly int[] _type = new int[16];
    private int _len = 16, _play = -1, _ratchet = 3;
    private bool _drag; private int _last = -1;
    public event Action<int, bool>? Paint;      // (step, erase = right-button / Alt)
    public event Action? PaintStarted;
    public event Action? PaintEnded;
    public ConsortStepGrid() { Cursor = new Cursor(StandardCursorType.Hand); }
    public void Set(ReadOnlySpan<int> types, int len, int play, int ratchet)
    {
        for (int i = 0; i < 16; i++) _type[i] = types[i];
        _len = len; _play = play; _ratchet = ratchet; InvalidateVisual();
    }
    private int Hit(Point p) { double cw = Bounds.Width / 16; int i = (int)(p.X / cw); return i >= 0 && i < 16 ? i : -1; }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pp = e.GetCurrentPoint(this).Properties;
        if (!pp.IsLeftButtonPressed) return;
        int i = Hit(e.GetPosition(this)); if (i < 0) return;
        _drag = true; _last = i; PaintStarted?.Invoke();
        Paint?.Invoke(i, (e.KeyModifiers & KeyModifiers.Alt) != 0);
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        int i = Hit(e.GetPosition(this));
        if (i >= 0 && i != _last) { _last = i; Paint?.Invoke(i, (e.KeyModifiers & KeyModifiers.Alt) != 0); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag) { _drag = false; PaintEnded?.Invoke(); e.Pointer.Capture(null); } }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 16 || h < 4) return;
        const double gap = 3;
        double cw = (w - gap * 15) / 16;
        var ac = NotaPalette.AccentColor; var mv = ChamberInk.MauveColor; var tl = NotaPalette.Teal.Color;
        for (int i = 0; i < 16; i++)
        {
            var r = new Rect(i * (cw + gap) + 0.5, 0.5, cw - 1, h - 1);
            bool inLen = i < _len;
            int t = _type[i];
            (Color c, string lbl) = t switch { 1 => (mv, $"×{_ratchet}"), 2 => (tl, "~"), 3 => (Colors.Transparent, "·"), _ => (ac, (i + 1).ToString(CultureInfo.InvariantCulture)) };
            IBrush fill = t == 3 ? NotaPalette.BgSunken : new SolidColorBrush(Color.FromArgb((byte)(inLen ? 0x40 : 0x14), c.R, c.G, c.B));
            IBrush stroke = t == 3 ? NotaPalette.BorderDefault : new SolidColorBrush(Color.FromArgb((byte)(inLen ? 0xE0 : 0x50), c.R, c.G, c.B));
            ctx.DrawRectangle(fill, new Pen(stroke, i == _play ? 2 : 1), r, 3, 3);
            var ft = new FormattedText(lbl, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, inLen ? (t == 3 ? NotaPalette.TextDisabled : NotaPalette.TextPrimary) : NotaPalette.TextDisabled);
            ctx.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
            if (i == _play) ctx.DrawRectangle(null, new Pen(NotaPalette.AccentBright, 1), r.Inflate(1.5), 4, 4);
        }
    }
}

// ---- step pitch lane: one bar per step at its pitch (±24 st); drag up/down to set ----
internal sealed class ConsortPitchLane : Control
{
    private static readonly Typeface Sans = new("Inter, system-ui, sans-serif", FontStyle.Normal, FontWeight.Bold);
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private readonly int[] _p = new int[16], _t = new int[16];
    private int _len = 16, _play = -1, _dragStep = -1;
    private bool _arp;
    public event Action<int, int>? PitchChanged;   // (step, semitones)
    public event Action<int>? DragStarted;
    public event Action<int>? DragEnded;
    public ConsortPitchLane() { Cursor = new Cursor(StandardCursorType.SizeNorthSouth); ClipToBounds = true; }
    public void Set(ReadOnlySpan<int> pitch, ReadOnlySpan<int> types, int len, int play, bool arp)
    {
        for (int i = 0; i < 16; i++) { _p[i] = pitch[i]; _t[i] = types[i]; }
        _len = len; _play = play; _arp = arp; InvalidateVisual();
    }
    private double Top => 14;
    private int PitchAt(double y) { double hh = Bounds.Height - Top - 4; return Math.Clamp((int)Math.Round((1 - (y - Top) / hh) * 48 - 24), -24, 24); }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        var pt = e.GetPosition(this);
        double cw = Bounds.Width / 16; int i = Math.Clamp((int)(pt.X / cw), 0, 15);
        _dragStep = i; DragStarted?.Invoke(i);
        int v = e.ClickCount == 2 ? 0 : PitchAt(pt.Y);
        _p[i] = v; PitchChanged?.Invoke(i, v); InvalidateVisual();
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragStep < 0) return;
        int v = PitchAt(e.GetPosition(this).Y);
        if (v != _p[_dragStep]) { _p[_dragStep] = v; PitchChanged?.Invoke(_dragStep, v); InvalidateVisual(); }
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_dragStep >= 0) { DragEnded?.Invoke(_dragStep); _dragStep = -1; e.Pointer.Capture(null); } }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 16 || h < Top + 6) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        var cap = new FormattedText(_arp ? "PITCH · ARP (STEP TYPES SHAPE THE RHYTHM)" : "PITCH · STEP OFFSETS FROM THE KEY", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 7, NotaPalette.TextTertiary);
        ctx.DrawText(cap, new Point(6, 3));
        double hh = h - Top - 4, cw = w / 16;
        double Y(int p) => Top + (1 - (p + 24) / 48.0) * hh;
        var grid = new Pen(NotaPalette.SurfaceCard, 1);
        foreach (int p in new[] { -12, 0, 12 }) ctx.DrawLine(grid, new Point(0, Y(p)), new Point(w, Y(p)));
        var ac = NotaPalette.AccentColor; var mv = ChamberInk.MauveColor; var tl = NotaPalette.Teal.Color;
        for (int i = 0; i < 16; i++)
        {
            if (_t[i] == 3) continue;
            bool on = i < _len;
            byte a = (byte)(_arp ? 0x60 : on ? 0xFF : 0x48);
            var col = _t[i] == 2 ? tl : ac;
            double y = Y(_p[i]);
            var r = new Rect(i * cw + 3, y - 3.5, cw - 6, 7);
            ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, col.R, col.G, col.B)), null, r, 2.5, 2.5);
            if (_t[i] == 1) ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(a, mv.R, mv.G, mv.B)), null, new Rect(r.X, r.Bottom + 2, r.Width, 4), 2, 2);
        }
        if (_play >= 0 && _play < 16)
        {
            double x = (_play + 0.5) * cw;
            ctx.DrawLine(new Pen(NotaPalette.Accent, 2), new Point(x, Top - 2), new Point(x, h));
        }
        if (_dragStep >= 0)
        {
            var ft = new FormattedText(_p[_dragStep] == 0 ? "0 st" : $"{_p[_dragStep]:+0;-0} st", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, NotaPalette.AccentBright);
            double x = Math.Clamp((_dragStep + 0.5) * cw - ft.Width / 2, 2, w - ft.Width - 2);
            ctx.DrawText(ft, new Point(x, Math.Max(Top, Y(_p[_dragStep]) - 16)));
        }
    }
}

// ---- jack field with cables. Strip mode: grouped rows (label under each jack); List mode:
//      titled columns (name beside each jack). Drag jack → jack to patch (out ↔ in), Alt-click a
//      jack to pull its cables, right-click for its cable list. ----
internal sealed class ConsortJackField : Control
{
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private static readonly Typeface Sans = new("Inter, system-ui, sans-serif");
    private static readonly Typeface SansBold = new("Inter, system-ui, sans-serif", FontStyle.Normal, FontWeight.Bold);

    public readonly record struct Group(string Title, ConsortJacks.Jack[] Jacks);
    private readonly List<List<Group>> _rows;     // strip mode: rows of groups; list mode: columns (one group each)
    private readonly bool _list;
    private readonly Dictionary<(bool, int), Point> _pos = new();
    private IReadOnlyList<ConsortCable> _cables = Array.Empty<ConsortCable>();
    private ConsortJacks.Jack? _from; private Point _drag; private bool _moved;
    private (bool, int)? _hover;

    public event Action<int, int>? Connect;                    // (source, destination)
    public event Action<ConsortJacks.Jack>? Pull;              // Alt-click: remove the jack's cables
    public event Action<ConsortJacks.Jack, Control>? Context;  // right-click

    public ConsortJackField(List<List<Group>> rowsOrColumns, bool listMode)
    { _rows = rowsOrColumns; _list = listMode; ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }

    public void SetCables(IReadOnlyList<ConsortCable> cables) { _cables = cables; InvalidateVisual(); }

    private void Layout()
    {
        _pos.Clear();
        double w = Bounds.Width, h = Bounds.Height;
        if (_list)
        {
            double colW = w / _rows.Count;
            for (int c = 0; c < _rows.Count; c++)
            {
                int k = 0;
                foreach (var g in _rows[c])
                    foreach (var j in g.Jacks) { _pos[(j.Out, j.Index)] = new Point(c * colW + 10, 24 + k * 15.5); k++; }
            }
            return;
        }
        int nr = _rows.Count;
        double rowH = h / nr;
        for (int r = 0; r < nr; r++)
        {
            int count = 0; foreach (var g in _rows[r]) count += g.Jacks.Length;
            const double gapG = 10;
            double cw = Math.Min(24, (w - 8 - gapG * (_rows[r].Count - 1)) / Math.Max(1, count));
            double x = 4 + cw / 2;
            double y = r * rowH + rowH * 0.5 + 1;
            foreach (var g in _rows[r])
            {
                foreach (var j in g.Jacks) { _pos[(j.Out, j.Index)] = new Point(x, y); x += cw; }
                x += gapG;
            }
        }
    }

    private ConsortJacks.Jack? HitJack(Point p)
    {
        foreach (var row in _rows)
            foreach (var g in row)
                foreach (var j in g.Jacks)
                    if (_pos.TryGetValue((j.Out, j.Index), out var c) && (Math.Abs(p.X - c.X) < 8 || (_list && p.X > c.X && p.X < c.X + 90)) && Math.Abs(p.Y - c.Y) < 7.5)
                        return j;
        return null;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        var j = HitJack(e.GetPosition(this));
        if (j is null) return;
        if (pt.Properties.IsRightButtonPressed) { Context?.Invoke(j.Value, this); e.Handled = true; return; }
        if (!pt.Properties.IsLeftButtonPressed) return;
        if ((e.KeyModifiers & KeyModifiers.Alt) != 0) { Pull?.Invoke(j.Value); e.Handled = true; return; }
        _from = j; _drag = e.GetPosition(this); _moved = false;
        e.Pointer.Capture(this); e.Handled = true; InvalidateVisual();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        var h = HitJack(p);
        (bool, int)? hv = h is { } hj ? (hj.Out, hj.Index) : null;
        if (hv != _hover) { _hover = hv; InvalidateVisual(); }
        if (_from is null) return;
        _moved = true; _drag = p; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_from is not { } f) return;
        var t = HitJack(e.GetPosition(this));
        if (t is { } to && to.Out != f.Out) { if (f.Out) Connect?.Invoke(f.Index, to.Index); else Connect?.Invoke(to.Index, f.Index); }
        else if (!_moved) Context?.Invoke(f, this);
        _from = null; e.Pointer.Capture(null); InvalidateVisual();
    }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hover != null) { _hover = null; InvalidateVisual(); } }

    private static void Cable(DrawingContext ctx, Point a, Point b, IBrush brush, bool list, double thick = 2.4)
    {
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            c.BeginFigure(a, false);
            if (list) { double dx = (b.X - a.X) * 0.5; c.CubicBezierTo(new Point(a.X + dx, a.Y + 6), new Point(b.X - dx, b.Y + 6), b); }
            else { double sag = 18 + Math.Abs(b.X - a.X) * 0.18; c.CubicBezierTo(new Point(a.X, a.Y + sag), new Point(b.X, b.Y + sag), b); }
        }
        ctx.DrawGeometry(null, new Pen(brush, thick, lineCap: PenLineCap.Round), g);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 20 || h < 20) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        Layout();
        // jack colour = the tone of the (first) cable plugged into it
        var lit = new Dictionary<(bool, int), IBrush>();
        foreach (var c in _cables)
        {
            var br = ConsortJacks.ToneBrush(ConsortJacks.Src(c.Src).Tone);
            lit.TryAdd((true, c.Src), br); lit.TryAdd((false, c.Dst), br);
        }
        // captions
        if (_list)
        {
            double colW = w / _rows.Count;
            for (int c = 0; c < _rows.Count; c++)
            {
                var t = new FormattedText(_rows[c][0].Title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SansBold, 7.5, NotaPalette.TextTertiary);
                ctx.DrawText(t, new Point(c * colW + 4, 4));
            }
        }
        else
        {
            int nr = _rows.Count; double rowH = h / nr;
            for (int r = 0; r < nr; r++)
                foreach (var g in _rows[r])
                {
                    if (g.Jacks.Length == 0 || !_pos.TryGetValue((g.Jacks[0].Out, g.Jacks[0].Index), out var p0)) continue;
                    var t = new FormattedText(g.Title, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SansBold, 7, NotaPalette.TextTertiary);
                    ctx.DrawText(t, new Point(p0.X - 5, r * rowH + rowH * 0.5 - 17));
                }
        }
        // jacks
        foreach (var row in _rows)
            foreach (var g in row)
                foreach (var j in g.Jacks)
                {
                    if (!_pos.TryGetValue((j.Out, j.Index), out var p)) continue;
                    bool hov = _hover == (j.Out, j.Index) || (_from is { } f && f.Out == j.Out && f.Index == j.Index);
                    lit.TryGetValue((j.Out, j.Index), out var col);
                    if (j.Out) ctx.DrawEllipse(null, new Pen(NotaPalette.SurfaceRaised, 2), p, 7.5, 7.5);   // output collar
                    ctx.DrawEllipse(NotaPalette.BgSunken, new Pen(col ?? (hov ? NotaPalette.TextSecondary : NotaPalette.BorderStrong), 1.6), p, 5.5, 5.5);
                    if (col != null) ctx.DrawEllipse(col, null, p, 2, 2);
                    if (_list)
                    {
                        var t = new FormattedText(j.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 8.5, col != null || hov ? NotaPalette.TextPrimary : NotaPalette.TextTertiary);
                        ctx.DrawText(t, new Point(p.X + 10, p.Y - t.Height / 2));
                    }
                    else
                    {
                        var t = new FormattedText(j.Short, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 6.5, col != null ? NotaPalette.TextSecondary : NotaPalette.TextDisabled);
                        ctx.DrawText(t, new Point(p.X - t.Width / 2, p.Y + 7));
                    }
                }
        // cables over the jacks
        foreach (var c in _cables)
        {
            if (!_pos.TryGetValue((true, c.Src), out var a) || !_pos.TryGetValue((false, c.Dst), out var b)) continue;
            var col = ConsortJacks.ToneColor(ConsortJacks.Src(c.Src).Tone);
            Cable(ctx, a, b, new SolidColorBrush(Color.FromArgb(0xDC, col.R, col.G, col.B)), _list);
            ctx.DrawEllipse(new SolidColorBrush(col), null, a, 2, 2); ctx.DrawEllipse(new SolidColorBrush(col), null, b, 2, 2);
        }
        if (_from is { } fr && _moved && _pos.TryGetValue((fr.Out, fr.Index), out var fp))
        {
            var col = fr.Out ? ConsortJacks.ToneColor(fr.Tone) : NotaPalette.AccentBrightColor;
            Cable(ctx, fp, _drag, new SolidColorBrush(Color.FromArgb(0x90, col.R, col.G, col.B)), _list, 2);
        }
        // hover tooltip-ish caption in strip mode: the full jack name
        if (!_list && _hover is { } hk)
        {
            var j = hk.Item1 ? ConsortJacks.Src(hk.Item2) : ConsortJacks.Dst(hk.Item2);
            if (_pos.TryGetValue(hk, out var p))
            {
                var t = new FormattedText($"{j.Name} · {(j.Out ? "out" : "in")}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 8, NotaPalette.TextPrimary);
                double x = Math.Clamp(p.X - t.Width / 2, 1, w - t.Width - 1), y = p.Y - 21;
                if (y < 1) y = p.Y + 15;
                ctx.DrawRectangle(NotaPalette.SurfaceRaised, new Pen(NotaPalette.BorderStrong, 1), new Rect(x - 3, y - 1, t.Width + 6, t.Height + 2), 3, 3);
                ctx.DrawText(t, new Point(x, y));
            }
        }
    }
}

// ---- source × destination matrix: rows = sources, columns = destinations, cell = depth ----
internal sealed class ConsortPatchMatrix : Control
{
    private static readonly Typeface Mono = new("ui-monospace, Menlo, monospace");
    private static readonly Typeface Sans = new("Inter, system-ui, sans-serif");
    private static readonly Typeface SansBold = new("Inter, system-ui, sans-serif", FontStyle.Normal, FontWeight.Bold);
    private int[] _rows = Array.Empty<int>(), _cols = Array.Empty<int>();
    private IReadOnlyList<ConsortCable> _cables = Array.Empty<ConsortCable>();
    private const double HeadH = 16, RowHdrW = 78;
    private (int r, int c)? _drag; private double _dragY, _dragV;

    public event Action<int, int, float>? DepthChanged;   // (src, dst, depth) — creates the cable when missing
    public event Action<int, int>? Removed;               // (src, dst)
    public event Action<int, int>? GestureBegin;
    public event Action<int, int>? GestureEnd;
    public event Action<bool, int, Rect>? HeaderClicked;  // (isRow, index, rect)

    public ConsortPatchMatrix() { ClipToBounds = true; Cursor = new Cursor(StandardCursorType.Hand); }
    public void Set(int[] rows, int[] cols, IReadOnlyList<ConsortCable> cables) { _rows = rows; _cols = cols; _cables = cables; InvalidateVisual(); }

    private double RowH => _rows.Length == 0 ? 0 : (Bounds.Height - HeadH) / _rows.Length;
    private double ColW => _cols.Length == 0 ? 0 : (Bounds.Width - RowHdrW) / _cols.Length;
    private ConsortCable? Find(int s, int d) { foreach (var c in _cables) if (c.Src == s && c.Dst == d) return c; return null; }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        var pt = e.GetCurrentPoint(this);
        if (!pt.Properties.IsLeftButtonPressed) return;
        var p = e.GetPosition(this);
        if (p.Y < HeadH && p.X >= RowHdrW) { int c = (int)((p.X - RowHdrW) / ColW); if (c >= 0 && c < _cols.Length) HeaderClicked?.Invoke(false, c, new Rect(RowHdrW + c * ColW, 0, ColW, HeadH)); e.Handled = true; return; }
        if (p.X < RowHdrW && p.Y >= HeadH) { int r = (int)((p.Y - HeadH) / RowH); if (r >= 0 && r < _rows.Length) HeaderClicked?.Invoke(true, r, new Rect(0, HeadH + r * RowH, RowHdrW, RowH)); e.Handled = true; return; }
        if (p.X < RowHdrW || p.Y < HeadH) return;
        int ri = (int)((p.Y - HeadH) / RowH), ci = (int)((p.X - RowHdrW) / ColW);
        if (ri < 0 || ri >= _rows.Length || ci < 0 || ci >= _cols.Length) return;
        int s = _rows[ri], d = _cols[ci];
        var cur = Find(s, d);
        if ((e.KeyModifiers & KeyModifiers.Alt) != 0 || (e.ClickCount == 2 && cur != null)) { Removed?.Invoke(s, d); e.Handled = true; return; }
        _drag = (ri, ci); _dragY = p.Y; _dragV = cur?.Depth ?? 0;
        GestureBegin?.Invoke(s, d);
        if (cur == null) { _dragV = 0.5; DepthChanged?.Invoke(s, d, 0.5f); }
        e.Pointer.Capture(this); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_drag is not { } dg) return;
        double y = e.GetPosition(this).Y;
        bool fine = (e.KeyModifiers & (KeyModifiers.Control | KeyModifiers.Meta | KeyModifiers.Shift)) != 0;
        _dragV = Math.Clamp(_dragV + (_dragY - y) / (fine ? 1000.0 : 100.0), -1, 1); _dragY = y;
        DepthChanged?.Invoke(_rows[dg.r], _cols[dg.c], (float)Math.Round(_dragV * 100) / 100f);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag is not { } dg) return;
        GestureEnd?.Invoke(_rows[dg.r], _cols[dg.c]);
        _drag = null; e.Pointer.Capture(null);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 40 || h < 30 || _rows.Length == 0 || _cols.Length == 0) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        double rh = RowH, cw = ColW;
        var corner = new FormattedText("SRC → DST", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SansBold, 7, NotaPalette.TextDisabled);
        ctx.DrawText(corner, new Point(4, (HeadH - corner.Height) / 2));
        for (int c = 0; c < _cols.Length; c++)
        {
            var d = ConsortJacks.Dst(_cols[c]);
            var t = new FormattedText(d.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, SansBold, 7.5, NotaPalette.TextSecondary) { MaxTextWidth = cw - 4, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(t, new Point(RowHdrW + c * cw + (cw - t.Width) / 2, (HeadH - t.Height) / 2));
        }
        var line = new Pen(NotaPalette.BorderDefault, 1);
        for (int r = 0; r < _rows.Length; r++)
        {
            double y = HeadH + r * rh;
            ctx.DrawLine(line, new Point(0, y), new Point(w, y));
            var s = ConsortJacks.Src(_rows[r]);
            var t = new FormattedText(s.Name, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Sans, 8.5, NotaPalette.TextPrimary) { MaxTextWidth = RowHdrW - 6, MaxLineCount = 1, Trimming = TextTrimming.CharacterEllipsis };
            ctx.DrawText(t, new Point(4, y + (rh - t.Height) / 2));
            var tc = ConsortJacks.ToneColor(s.Tone);
            for (int c = 0; c < _cols.Length; c++)
            {
                var cell = new Rect(RowHdrW + c * cw + 2, y + 3, cw - 4, rh - 6);
                var cab = Find(_rows[r], _cols[c]);
                if (cab is { } cb)
                {
                    ctx.DrawRectangle(new SolidColorBrush(Color.FromArgb(0x30, tc.R, tc.G, tc.B)), new Pen(new SolidColorBrush(tc), 1.2), cell, 3, 3);
                    // depth bar from the centre
                    double mid = cell.X + cell.Width / 2, bw = cell.Width / 2 * Math.Abs(cb.Depth);
                    ctx.FillRectangle(new SolidColorBrush(Color.FromArgb(0x38, tc.R, tc.G, tc.B)), new Rect(cb.Depth >= 0 ? mid : mid - bw, cell.Bottom - 3, bw, 2));
                    var v = new FormattedText($"{cb.Depth * 100:+0;-0;0}", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, NotaPalette.TextPrimary);
                    ctx.DrawText(v, new Point(cell.X + (cell.Width - v.Width) / 2, cell.Y + (cell.Height - v.Height) / 2));
                }
                else
                {
                    ctx.DrawRectangle(NotaPalette.BgSunken, new Pen(NotaPalette.BorderDefault, 1), cell, 3, 3);
                    var v = new FormattedText("·", CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 8, NotaPalette.TextDisabled);
                    ctx.DrawText(v, new Point(cell.X + (cell.Width - v.Width) / 2, cell.Y + (cell.Height - v.Height) / 2));
                }
            }
        }
    }
}
