// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota EQ-3 (device kind 16) card controls:
//  · Eq3Fader — a band's gain fader, drawn as an upright value bar filling from 0 dB to the
//    value, the range's ends and 0 dB marked beside it. Vertical drag (relative — a click never
//    jumps), 0.5 dB steps with a 0 dB detent; Shift / Ctrl / ⌘ for fine 0.1 dB steps;
//    double-click resets (0 dB, and the kill off); ⌥-click types the gain.
//  · Eq3Graph — the response window: the output spectrum (engine FFT) behind, each band's own
//    curve thin in grey (Low light, Mid darker, High light and dashed), the sum in neutral ink, the two crossovers as dashed lines with handles
//    along the top that drag left / right (the low/mid one held to at most half the mid/high
//    one). A drag anywhere else in a band's zone rides that band's gain up / down; double-click
//    a handle or a zone to reset it, ⌥-click it to type the crossover / the band's gain.
// Eq3Math mirrors Eq3.h (Linkwitz-Riley tree, the low band's allpass) so the curves are the DSP.

using System;
using System.Globalization;
using System.Numerics;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal static class Eq3Math
{
    public const double FMin = 20, FMax = 20000;
    public static readonly string[] Names = { "LOW", "MID", "HIGH" };

    /// <summary>The band hues: rose, neutral ink, teal — the strips, the graph labels and the band curves.</summary>
    public static SolidColorBrush Hue(int band) => NotaPalette.TextPrimary;

    /// <summary>The band curve's ink on the graph, told apart by shade and stroke (style guide):
    /// Low the lighter grey, Mid the darker grey (so the neutral sum reads), High the lighter
    /// grey dashed.</summary>
    public static SolidColorBrush CurveInk(int band) => band == 1 ? NotaPalette.TextTertiary : NotaPalette.TextSecondary;

    /// <summary>The band curve's dash: High is dashed, Low and Mid solid.</summary>
    public static DashStyle? CurveDash(int band) => band == 2 ? HighDash : null;
    private static readonly DashStyle HighDash = new(new double[] { 3, 2 }, 0);

    public static double X(double f) => Math.Log(Math.Clamp(f, FMin, FMax) / FMin) / Math.Log(FMax / FMin);
    public static double F(double x) => FMin * Math.Pow(FMax / FMin, Math.Clamp(x, 0, 1));

    public static double GainDb(double v, bool iso) => iso ? -24 + 30 * v : (v - 0.5) * 30;
    public static double GainNorm(double db, bool iso) => Math.Clamp(iso ? (db + 24) / 30 : db / 30 + 0.5, 0, 1);
    public static double TopDb(bool iso) => iso ? 6 : 15;
    public static double BottomDb(bool iso) => iso ? -24 : -15;

    public static double LoHz(double v) => 50 * Math.Pow(40, Math.Clamp(v, 0, 1));
    public static double LoNorm(double hz) => Math.Log(Math.Clamp(hz, 50, 2000) / 50) / Math.Log(40);
    public static double HiHz(double v) => 500 * Math.Pow(36, Math.Clamp(v, 0, 1));
    public static double HiNorm(double hz) => Math.Log(Math.Clamp(hz, 500, 18000) / 500) / Math.Log(36);

    public static string Hz(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.0}\u2009kHz") : NotaNum.F($"{hz:0}\u2009Hz");
    /// <summary>Compact, for the strip ranges: 250 · 2.5k.</summary>
    public static string HzShort(double hz) => hz >= 1000 ? NotaNum.F($"{hz / 1000:0.#}k") : NotaNum.F($"{hz:0}");
    public static string Db(double db) => NotaNum.F($"{db:+0.0;−0.0;0.0}");

    // One Butterworth-2 section at s = j·f/fc with damping k = 1/Q.
    private static Complex Sec(double w, double k, bool hp)
    {
        var s = new Complex(0, w);
        var d = s * s + k * s + 1;
        return (hp ? s * s : Complex.One) / d;
    }
    private static Complex Cascade(double f, double fc, bool lr8, bool hp)
    {
        double w = f / fc;
        Complex h = Complex.One;
        int n = lr8 ? 4 : 2;
        for (int i = 0; i < n; i++) h *= Sec(w, lr8 ? (i % 2 == 0 ? 1.8477590650 : 0.7653668647) : 1.4142135624, hp);
        return h;
    }

    /// <summary>The three bands' complex responses at <paramref name="f"/> (unity gains).</summary>
    public static (Complex Low, Complex Mid, Complex High) Bands(double f, double f1, double f2, bool lr8)
    {
        Complex lp1 = Cascade(f, f1, lr8, false), hp1 = Cascade(f, f1, lr8, true);
        Complex lp2 = Cascade(f, f2, lr8, false), hp2 = Cascade(f, f2, lr8, true);
        return (lp1 * (lp2 + hp2), hp1 * lp2, hp1 * hp2);
    }

    public static double ToDb(double mag) => 20 * Math.Log10(Math.Max(mag, 1e-6));
}

internal sealed class Eq3Fader : Control
{
    private double _db, _top = 6, _bottom = -24;
    private bool _killed, _drag, _hover;
    private double _startY, _startDb;
    private IBrush _hue = NotaPalette.VizInk;

    public event Action? GestureBegin;
    public event Action? GestureEnd;
    public event Action<double>? Changed;      // dB
    public event Action? ResetRequested;
    public bool Dragging => _drag;

    public Eq3Fader() { Cursor = new Cursor(StandardCursorType.SizeNorthSouth); MinHeight = 60; }

    public void Set(double db, bool killed, double top, double bottom, IBrush hue)
    {
        if (Math.Abs(db - _db) < 1e-6 && killed == _killed && top == _top && bottom == _bottom && ReferenceEquals(hue, _hue)) return;
        _db = db; _killed = killed; _top = top; _bottom = bottom; _hue = hue;
        InvalidateVisual();
    }

    private const double Pad = 0;
    private double Y(double db) => Pad + (_top - Math.Clamp(db, _bottom, _top)) / (_top - _bottom) * Math.Max(1, Bounds.Height - 2 * Pad);

    protected override void OnPointerEntered(PointerEventArgs e) { base.OnPointerEntered(e); _hover = true; InvalidateVisual(); }
    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); _hover = false; InvalidateVisual(); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles (MIDI learn)
        e.Handled = true;
        if ((e.ClickCount == 2 || (e.KeyModifiers & KeyModifiers.Alt) != 0)) { ResetRequested?.Invoke(); return; }
        _drag = true; _startY = e.GetPosition(this).Y; _startDb = _db;
        e.Pointer.Capture(this);
        GestureBegin?.Invoke();
    }
    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (!_drag) return;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double span = _top - _bottom;
        double px = Math.Max(140, Bounds.Height - 2 * Pad);   // the full range over the slot (≥ 140 px)
        double db = _startDb + (_startY - e.GetPosition(this).Y) / px * span * (fine ? 0.2 : 1);
        if (fine) db = Math.Round(db * 10) / 10;
        else { db = Math.Round(db * 2) / 2; if (Math.Abs(db) < 0.75) db = 0; }   // 0.5 dB steps, 0 dB detent
        db = Math.Clamp(db, _bottom, _top);
        if (Math.Abs(db - _db) < 1e-9) return;
        _startY = e.GetPosition(this).Y; _startDb = db;   // re-anchor so a mode change (fine) never jumps
        _db = db; InvalidateVisual(); Changed?.Invoke(db);
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (!_drag) return;
        _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke(); InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag) { _drag = false; GestureEnd?.Invoke(); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    // An upright value bar (style guide): a 16 px groove, the soft tint from 0 dB to the value and
    // a 3 px orange line at it; the tint lifts under the pointer and deepens in the hand; killed,
    // the line drops to ink 4 and the fill goes. The scale (the ends and 0 dB) sits to its left,
    // ticks at 0 dB and half the cut to its right.
    private static readonly IBrush FillHover = NotaPalette.Derived(() => SliderTrack.Mix(NotaPalette.AccentSoft.Color, NotaPalette.Accent.Color, 0.2));
    private static readonly IBrush FillDrag = NotaPalette.Derived(() => SliderTrack.Mix(NotaPalette.AccentSoft.Color, NotaPalette.Accent.Color, 0.45));
    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));   // the whole column hit-tests
        const double bw = SliderTrack.Height0, line = SliderTrack.LineW;
        double bx = Math.Round(w / 2 - bw / 2);
        var bar = new RoundedRect(new Rect(bx, 0, bw, h), NotaRadius.ValueBarValue);
        NotaDepth.Groove(ctx, this, bar);
        double y0 = Y(0), yv = Y(_db);
        using (ctx.PushClip(bar))
        {
            if (!_killed && Math.Abs(yv - y0) > 0.5)
                ctx.FillRectangle(_drag ? FillDrag : _hover ? FillHover : NotaPalette.AccentSoft, new Rect(bx, Math.Min(yv, y0), bw, Math.Abs(yv - y0)));
            double ly = Math.Clamp(yv, line / 2, h - line / 2) - line / 2;
            ctx.FillRectangle(_killed ? NotaPalette.TextDisabled : NotaPalette.Accent, new Rect(bx, ly, bw, line));
        }
        void Lbl(string t, double y, int align)
        {
            var ft = new NotaFormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, NotaType.Axis, NotaPalette.TextTertiary, this);
            double ty = align < 0 ? y : align > 0 ? y - ft.Height : y - ft.Height / 2;
            ctx.DrawText(ft, new Point(Math.Max(0, bx - 6 - ft.Width), Math.Clamp(ty, 0, h - ft.Height)));
        }
        Lbl(NotaNum.F($"+{_top:0}"), 0, -1);
        Lbl("0", y0, 0);
        Lbl(NotaNum.F($"{_bottom:0}"), h, 1);
        // Ticks on the right: 0 dB, and half the cut.
        double tx0 = bx + bw + 2, yHalf = Y(_bottom / 2);
        ctx.DrawLine(new Pen(NotaPalette.BorderStrong, 1), new Point(tx0, Math.Round(y0) + 0.5), new Point(tx0 + 6, Math.Round(y0) + 0.5));
        ctx.DrawLine(new Pen(NotaPalette.BorderDefault, 1), new Point(tx0, Math.Round(yHalf) + 0.5), new Point(tx0 + 4, Math.Round(yHalf) + 0.5));
    }
}

internal sealed class Eq3Graph : Control, IValueHandles
{
    private readonly double[] _db = new double[3];
    private readonly bool[] _kill = new bool[3];
    private double _f1 = 250, _f2 = 2500, _top = 6;
    private bool _lr8, _iso = true, _specValid;
    private readonly float[] _spec = new float[256];   // dsp::SpectrumAnalyzer::kPoints
    private int _specN;

    // Drag state: 0 / 1 = a crossover handle, 2 + b = band b's gain.
    private int _drag = -1, _hover = -1;
    private double _grabDx, _startY, _startDb;

    public event Action<int>? XoverBegin;
    public event Action<int, double>? XoverChanged;   // which (0 low/mid, 1 mid/high), Hz
    public event Action<int>? XoverEnd;
    public event Action<int>? XoverReset;
    public event Action<int>? GainBegin;
    public event Action<int, double>? GainChanged;    // band, dB
    public event Action<int>? GainEnd;
    public event Action<int>? GainReset;
    public bool Dragging => _drag >= 0;

    public Eq3Graph() { ClipToBounds = true; MinHeight = 60; MinWidth = 160; Cursor = new Cursor(StandardCursorType.SizeNorthSouth); }

    public void Set(double lowDb, double midDb, double highDb, bool kLow, bool kMid, bool kHigh, double f1, double f2, bool lr8, bool iso)
    {
        _db[0] = lowDb; _db[1] = midDb; _db[2] = highDb;
        _kill[0] = kLow; _kill[1] = kMid; _kill[2] = kHigh;
        _f1 = f1; _f2 = f2; _lr8 = lr8; _iso = iso; _top = Eq3Math.TopDb(iso);
        InvalidateVisual();
    }

    /// <summary>The engine's output spectrum (dB per log band 20 Hz … 20 kHz).</summary>
    public void SetSpectrum(float[] src, int offset, int count, bool valid)
    {
        _specN = Math.Min(count, _spec.Length);
        Array.Copy(src, offset, _spec, 0, _specN);
        _specValid = valid && _specN > 1;
        InvalidateVisual();
    }

    // dB → y: the fader's top at 10 %, −36 dB at 90 % (as the mockup draws it).
    private double Y(double db, double h) => Math.Clamp(0.1 + (_top - db) / (_top + 36) * 0.8, 0, 1) * h;

    private Rect Handle(int which, out FormattedText text)
    {
        double f = which == 0 ? _f1 : _f2;
        text = new NotaFormattedText(Eq3Math.Hz(f), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, NotaGraph.AxisSize, NotaPalette.TextPrimary, this);
        double w = text.Width + 14, x = Eq3Math.X(f) * Bounds.Width;
        x = Math.Clamp(x - w / 2, 1, Math.Max(1, Bounds.Width - w - 1));
        return new Rect(Math.Round(x), 4, Math.Round(w), Math.Round(text.Height + 4));
    }

    private int HitHandle(Point p)
    {
        for (int i = 1; i >= 0; i--) if (Handle(i, out _).Inflate(new Thickness(2, 3)).Contains(p)) return i;
        return -1;
    }
    private int ZoneAt(double x)
    {
        double f = Eq3Math.F(x / Math.Max(1, Bounds.Width));
        return f < _f1 ? 0 : f < _f2 ? 1 : 2;
    }

    // Typed entry: handles 0 / 1 are the crossovers (the box over the handle), 2…4 the bands' zones.
    public int HandleAt(Point p) => HitHandle(p) is var h and >= 0 ? h : 2 + ZoneAt(p.X);
    public Rect HandleRect(int handle)
    {
        if (handle < 2) return Handle(handle, out _);
        int b = handle - 2;
        double lo = b == 0 ? Eq3Math.FMin : b == 1 ? _f1 : _f2, hi = b == 0 ? _f1 : b == 1 ? _f2 : Eq3Math.FMax;
        double x = Eq3Math.X(Math.Sqrt(lo * hi)) * Bounds.Width;
        return ValueEntry.Near(new Point(x, Y(_db[b], Bounds.Height)), Bounds.Size);
    }

    protected override void OnPointerExited(PointerEventArgs e) { base.OnPointerExited(e); if (_hover != -1) { _hover = -1; InvalidateVisual(); } }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles (MIDI learn)
        e.Handled = true;
        var p = e.GetPosition(this);
        int h = HitHandle(p);
        if (e.ClickCount == 2)
        {
            if (h >= 0) XoverReset?.Invoke(h); else GainReset?.Invoke(ZoneAt(p.X));
            return;
        }
        if (h >= 0)
        {
            _drag = h;
            _grabDx = p.X - Eq3Math.X(h == 0 ? _f1 : _f2) * Bounds.Width;   // no jump: keep where it was grabbed
            XoverBegin?.Invoke(h);
        }
        else
        {
            int b = ZoneAt(p.X);
            _drag = 2 + b; _startY = p.Y; _startDb = _db[b];
            GainBegin?.Invoke(b);
        }
        e.Pointer.Capture(this);
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag < 0)
        {
            int h = HitHandle(p);
            if (h != _hover) { _hover = h; InvalidateVisual(); }
            Cursor = new Cursor(h >= 0 ? StandardCursorType.SizeWestEast : StandardCursorType.SizeNorthSouth);
            return;
        }
        if (_drag < 2)
        {
            double f = Eq3Math.F((p.X - _grabDx) / Math.Max(1, Bounds.Width));
            f = _drag == 0 ? Math.Clamp(f, 50, Math.Min(2000, _f2 / 2)) : Math.Clamp(f, Math.Max(500, _f1 * 2), 18000);
            if (_drag == 0) _f1 = f; else _f2 = f;
            XoverChanged?.Invoke(_drag, f);
            InvalidateVisual();
            return;
        }
        int b = _drag - 2;
        bool fine = (e.KeyModifiers & (KeyModifiers.Shift | KeyModifiers.Control | KeyModifiers.Meta)) != 0;
        double top = Eq3Math.TopDb(_iso), bot = Eq3Math.BottomDb(_iso);
        double px = Math.Max(140, Bounds.Height);
        double db = _startDb + (_startY - p.Y) / px * (top - bot) * (fine ? 0.2 : 1);
        if (fine) db = Math.Round(db * 10) / 10;
        else { db = Math.Round(db * 2) / 2; if (Math.Abs(db) < 0.75) db = 0; }
        db = Math.Clamp(db, bot, top);
        if (Math.Abs(db - _db[b]) < 1e-9) return;
        _startY = p.Y; _startDb = db;
        _db[b] = db;
        GainChanged?.Invoke(b, db);
        InvalidateVisual();
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        int d = _drag; _drag = -1;
        e.Pointer.Capture(null);
        if (d < 2) XoverEnd?.Invoke(d); else GainEnd?.Invoke(d - 2);
        InvalidateVisual();
    }

    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag >= 0)
        {
            int d = _drag; _drag = -1;
            if (d < 2) XoverEnd?.Invoke(d); else GainEnd?.Invoke(d - 2);
            InvalidateVisual();
        }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var r = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, r);

        // Grid: decades, 0 dB brighter, the cut's steps faint.
        foreach (double f in new[] { 100.0, 1000, 10000 })
        {
            double x = Math.Round(Eq3Math.X(f) * w) + 0.5;
            ctx.DrawLine(NotaGraph.GridPen, new Point(x, 0), new Point(x, h));
        }
        var faint = new Pen(NotaPalette.GridSubBeat, 1);
        foreach (double db in new[] { -12.0, -24 }) { double y = Math.Round(Y(db, h)) + 0.5; ctx.DrawLine(faint, new Point(0, y), new Point(w, y)); }
        double y0 = Math.Round(Y(0, h)) + 0.5;
        ctx.DrawLine(new Pen(NotaPalette.GridBar, 1), new Point(0, y0), new Point(w, y0));

        // Output spectrum, behind everything, filled as on EQ-8: 0 dBFS at the top, −90 at the bottom.
        if (_specValid) SpectrumFill.Draw(ctx, _spec, w, h, 90, _specN);

        // Band curves and the sum.
        int n = (int)Math.Clamp(w / 2, 60, 320);
        var lin = new double[3];
        for (int b = 0; b < 3; b++) lin[b] = _kill[b] ? 0 : Math.Pow(10, _db[b] / 20);
        var band = new StreamGeometry[3];
        var bc = new StreamGeometryContext[3];
        for (int b = 0; b < 3; b++) { band[b] = new StreamGeometry(); bc[b] = band[b].Open(); }
        var sum = new StreamGeometry();
        using (var sc = sum.Open())
        {
            for (int i = 0; i < n; i++)
            {
                double fx = (double)i / (n - 1), f = Eq3Math.F(fx), x = fx * w;
                var (lo, mid, hi) = Eq3Math.Bands(f, _f1, _f2, _lr8);
                Complex[] c = { lo, mid, hi };
                Complex s = Complex.Zero;
                for (int b = 0; b < 3; b++)
                {
                    s += lin[b] * c[b];
                    var pt = new Point(x, Y(Eq3Math.ToDb(lin[b] * c[b].Magnitude), h));
                    if (i == 0) bc[b].BeginFigure(pt, false); else bc[b].LineTo(pt);
                }
                var ps = new Point(x, Y(Eq3Math.ToDb(s.Magnitude), h));
                if (i == 0) sc.BeginFigure(ps, false); else sc.LineTo(ps);
            }
            sc.EndFigure(false);
        }
        for (int b = 0; b < 3; b++)
        {
            bc[b].EndFigure(false); bc[b].Dispose();
            if (_kill[b]) continue;
            ctx.DrawGeometry(null, new Pen(Eq3Math.CurveInk(b), 1.2, Eq3Math.CurveDash(b), lineJoin: PenLineJoin.Round), band[b]);
        }
        ctx.DrawGeometry(null, NotaGraph.PrimaryPen, sum);

        // Crossovers: dashed lines and their handles.
        var dash = new Pen(NotaPalette.BorderStrong, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
        foreach (double f in new[] { _f1, _f2 })
        {
            double x = Math.Round(Eq3Math.X(f) * w) + 0.5;
            ctx.DrawLine(dash, new Point(x, 0), new Point(x, h));
        }

        // Band labels along the top, centred in each band's zone.
        double xl = Eq3Math.X(_f1) * w, xh = Eq3Math.X(_f2) * w;
        double[] centres = { xl / 2, (xl + xh) / 2, (xh + w) / 2 };
        var names = new FormattedText[3];
        var vals = new FormattedText[3];
        var lx = new double[3];
        var lw = new double[3];
        for (int b = 0; b < 3; b++)
        {
            names[b] = new NotaFormattedText(NotaText.Label(Eq3Math.Names[b]), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NotaFonts.Sans, NotaGraph.AxisSize, NotaPalette.TextTertiary, this);
            vals[b] = new NotaFormattedText(_kill[b] ? "kill" : Eq3Math.Db(_db[b]), CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
                NotaFonts.Sans, NotaGraph.AxisSize, _kill[b] ? NotaPalette.TextDisabled : NotaPalette.TextPrimary, this);
            lw[b] = names[b].Width + 4 + vals[b].Width;
            lx[b] = centres[b] - lw[b] / 2;
        }
        // Keep them inside and apart: push right from the left edge, then back from the right one.
        lx[0] = Math.Max(3, lx[0]);
        for (int b = 1; b < 3; b++) lx[b] = Math.Max(lx[b], lx[b - 1] + lw[b - 1] + 8);
        lx[2] = Math.Min(lx[2], w - lw[2] - 3);
        for (int b = 1; b >= 0; b--) lx[b] = Math.Min(lx[b], lx[b + 1] - lw[b] - 8);
        for (int b = 0; b < 3; b++)
        {
            ctx.DrawText(names[b], new Point(lx[b], 6));
            ctx.DrawText(vals[b], new Point(lx[b] + names[b].Width + 4, 6));
        }
        for (int i = 0; i < 2; i++)
        {
            var rect = Handle(i, out var text);
            bool hot = _drag == i || _hover == i;
            ctx.DrawRectangle(hot ? NotaPalette.Accent : NotaPalette.SurfaceHover, null, rect, NotaRadius.CellValue, NotaRadius.CellValue);
            if (hot) text.SetForegroundBrush(NotaPalette.TextOnAccent);
            ctx.DrawText(text, new Point(rect.X + 7, rect.Y + (rect.Height - text.Height) / 2));
        }

        // Axis labels: 0 and the fader's floor on the right, the range in the bottom corners.
        void RightLbl(string t, double db)
        {
            var ft = NotaGraph.AxisText(t);
            ctx.DrawText(ft, new Point(w - ft.Width - 5, Y(db, h) - ft.Height / 2));
        }
        RightLbl("0", 0);
        RightLbl(NotaNum.F($"{Eq3Math.BottomDb(_iso):0}"), Eq3Math.BottomDb(_iso));
        NotaGraph.Axis(ctx, r, NotaGraph.Corner.BottomLeft, "20");
        NotaGraph.Axis(ctx, r, NotaGraph.Corner.BottomRight, "20k Hz");
    }
}
