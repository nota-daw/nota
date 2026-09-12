// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Dynamic EQ-8 response graph. Like the static EQ-8 curve, but a dynamic EQ has two
// answers at once — where a band sits and where it is right now — so it draws two
// curves: the static response in brass and the live, momentary response (static gain
// plus each band's current dynamic gain) as a teal dashed line. The gap between them
// is the compression happening. Dynamic bands announce themselves: a teal handle plus
// a dashed vertical "reach" line showing how far the band can travel. Drag a dot =
// freq (+gain for shelves/bells); wheel = Q; double-click empty = add a bell;
// right-click a dot = type / dynamic mode / remove. Momentary gains come from the
// device's scope telemetry (8 floats, dB per band); a per-band GR history is kept for
// the sparkline in the strip.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed class DynamicEqCurve : Control
{
    private const int Bands = 8, PerBand = 10;
    private const int On = 0, TypeF = 1, FreqF = 2, GainF = 3, QF = 4, ModeF = 5, ThrF = 6, RangeF = 7, AtkF = 8, RelF = 9;
    private const int LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5;
    private static readonly string[] TypeNames = { "Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut" };
    private static readonly string[] ModeNames = { "Static", "Dynamic ↓ (above)", "Dynamic ↑ (below)" };

    private const double Fmin = 20, Fmax = 20000, GMax = 18;
    public const int HistLen = 120;   // 2 s at 60 Hz

    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IPen GridPen = new Pen(NotaPalette.WellGrid, 1);
    private static readonly IPen GridPenFaint = new Pen(NotaPalette.Wash(NotaPalette.WellGrid, 0x60), 1);
    private static readonly IPen ZeroPen = new Pen(NotaPalette.BorderStrong, 1);
    private static readonly IPen CurvePen = new Pen(NotaPalette.Accent, 1.7);
    private static readonly IBrush CurveFill = NotaPalette.Wash(NotaPalette.Accent, 0x1E);
    private static readonly IPen DynPen = new Pen(NotaPalette.Teal, 1.5) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    private static readonly IPen ReachPen = new Pen(NotaPalette.Wash(NotaPalette.Teal, 0x88), 1) { DashStyle = new DashStyle(new double[] { 2, 3 }, 0) };
    private static readonly IBrush DotBrass = NotaPalette.AccentBright;
    private static readonly IBrush DotTeal = NotaPalette.Teal;
    private static readonly IBrush DotOff = NotaPalette.TextDisabled;
    private static readonly IBrush DotRing = NotaPalette.BgSunken;
    private static readonly IBrush SelRing = NotaPalette.AccentBright;
    private static readonly IBrush LabelDim = NotaPalette.TextTertiary;
    private static readonly IBrush LabelBright = NotaPalette.TextSecondary;
    private static readonly IBrush OnAccentText = NotaPalette.TextOnAccent;
    private static readonly Typeface Mono = new("monospace");
    private static readonly Typeface DotFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private int _drag = -1, _selected = 4;
    private bool _gestFreq, _gestGain;

    private readonly float[] _gr = new float[Bands];
    private readonly float[] _histBuf = new float[Bands * HistLen];
    private int _histHead;

    public event Action? SelectionChanged;
    public int SelectedBand => _selected;
    public float Gr(int b) => (b >= 0 && b < Bands) ? _gr[b] : 0f;

    // Copy a band's GR history oldest→newest into dst (length HistLen).
    public void FillHistory(int band, float[] dst)
    {
        if (band < 0 || band >= Bands) { Array.Clear(dst); return; }
        for (int i = 0; i < HistLen; i++)
            dst[i] = _histBuf[band * HistLen + (_histHead + i) % HistLen];
    }

    public DynamicEqCurve(IAudioEngine engine, int track, int device)
    {
        _engine = engine; _track = track; _device = device;
        MinHeight = 150;
    }

    // ---- param helpers ----
    private float P(int band, int field) => _engine.DeviceGetParam(_track, _device, band * PerBand + field);
    private void SetP(int band, int field, double v) => _engine.DeviceSetParam(_track, _device, band * PerBand + field, (float)v);
    private bool BandOn(int b) => P(b, On) > 0.5f;
    private int BandType(int b) => Math.Clamp((int)Math.Round(P(b, TypeF)), 0, 5);
    private int BandMode(int b) => Math.Clamp((int)Math.Round(P(b, ModeF)), 0, 2);
    private static bool HasGain(int type) => type is LowShelf or Bell or HighShelf;
    private bool IsDyn(int b) => BandOn(b) && HasGain(BandType(b)) && BandMode(b) != 0;

    public void Select(int b)
    {
        if (b == _selected) return;
        _selected = b; SelectionChanged?.Invoke(); InvalidateVisual();
    }

    // ---- coordinate mapping ----
    private double FreqToX(double f, double w) => w * Math.Log(f / Fmin) / Math.Log(Fmax / Fmin);
    private double XToFreq(double x, double w) => Fmin * Math.Pow(Fmax / Fmin, Math.Clamp(x / w, 0, 1));
    private double GainToY(double g, double h) => h * (0.5 - g / (2 * GMax));
    private double YToGain(double y, double h) => Math.Clamp(GMax * (1 - 2 * y / h), -GMax, GMax);

    // ---- magnitude response (mirrors DynamicEq.h RBJ biquads) ----
    private (double b0, double b1, double b2, double a1, double a2) Coeffs(int type, double sr, double f0, double gDb, double q)
    {
        double w0 = 2 * Math.PI * Math.Clamp(f0, 20, sr * 0.49) / sr, cw = Math.Cos(w0), sw = Math.Sin(w0);
        double a = sw / (2 * Math.Max(q, 0.1));
        (double, double, double, double, double) N(double b0, double b1, double b2, double a0, double a1, double a2)
            => (b0 / a0, b1 / a0, b2 / a0, a1 / a0, a2 / a0);
        switch (type)
        {
            case LowCut:  return N((1 + cw) / 2, -(1 + cw), (1 + cw) / 2, 1 + a, -2 * cw, 1 - a);
            case HighCut: return N((1 - cw) / 2, 1 - cw, (1 - cw) / 2, 1 + a, -2 * cw, 1 - a);
            case Notch:   return N(1, -2 * cw, 1, 1 + a, -2 * cw, 1 - a);
            case LowShelf:
            {
                double A = Math.Pow(10, gDb / 40), al = sw / 2 * Math.Sqrt(2), t = 2 * Math.Sqrt(A) * al;
                return N(A * ((A + 1) - (A - 1) * cw + t), 2 * A * ((A - 1) - (A + 1) * cw), A * ((A + 1) - (A - 1) * cw - t),
                         (A + 1) + (A - 1) * cw + t, -2 * ((A - 1) + (A + 1) * cw), (A + 1) + (A - 1) * cw - t);
            }
            case HighShelf:
            {
                double A = Math.Pow(10, gDb / 40), al = sw / 2 * Math.Sqrt(2), t = 2 * Math.Sqrt(A) * al;
                return N(A * ((A + 1) + (A - 1) * cw + t), -2 * A * ((A - 1) + (A + 1) * cw), A * ((A + 1) + (A - 1) * cw - t),
                         (A + 1) - (A - 1) * cw + t, 2 * ((A - 1) - (A + 1) * cw), (A + 1) - (A - 1) * cw - t);
            }
            default:
            {
                double A = Math.Pow(10, gDb / 40);
                return N(1 + a * A, -2 * cw, 1 - a * A, 1 + a / A, -2 * cw, 1 - a / A);
            }
        }
    }

    // Combined response in dB at frequency f. momentary=true adds each band's live GR.
    private double MagnitudeDb(double f, double sr, bool momentary)
    {
        double total = 0;
        double w = 2 * Math.PI * f / sr, cw = Math.Cos(w), sw = Math.Sin(w);
        double cos2 = Math.Cos(2 * w), sin2 = Math.Sin(2 * w);
        for (int b = 0; b < Bands; b++)
        {
            if (!BandOn(b)) continue;
            double g = P(b, GainF) + (momentary && IsDyn(b) ? _gr[b] : 0);
            var (b0, b1, b2, a1, a2) = Coeffs(BandType(b), sr, P(b, FreqF), g, P(b, QF));
            double numR = b0 + b1 * cw + b2 * cos2, numI = -(b1 * sw + b2 * sin2);
            double denR = 1 + a1 * cw + a2 * cos2, denI = -(a1 * sw + a2 * sin2);
            double mag2 = (numR * numR + numI * numI) / Math.Max(1e-12, denR * denR + denI * denI);
            total += 10 * Math.Log10(Math.Max(1e-12, mag2));
        }
        return total;
    }

    // ---- telemetry pull + history ----
    public void Tick()
    {
        int n = _engine.DeviceScope(_track, _device, _gr, Bands);
        for (int b = n; b < Bands; b++) _gr[b] = 0;
        _histHead = (_histHead + HistLen - 1) % HistLen;          // advance ring (write oldest slot)
        for (int b = 0; b < Bands; b++) _histBuf[b * HistLen + _histHead] = _gr[b];
        InvalidateVisual();
    }
    private double Sr => _engine.SampleRate > 0 ? _engine.SampleRate : 48000;

    // ---- interaction ----
    private int HitDot(Point p, double w, double h)
    {
        int best = -1; double bestD = 16 * 16;
        for (int b = 0; b < Bands; b++)
        {
            if (!BandOn(b)) continue;
            double dx = p.X - FreqToX(P(b, FreqF), w);
            double dy = p.Y - GainToY(HasGain(BandType(b)) ? P(b, GainF) : 0, h);
            double d = dx * dx + dy * dy;
            if (d < bestD) { bestD = d; best = b; }
        }
        return best;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        double w = Bounds.Width, h = Bounds.Height;
        var pt = e.GetPosition(this);
        var props = e.GetCurrentPoint(this).Properties;

        if (props.IsRightButtonPressed)
        {
            int b = HitDot(pt, w, h);
            if (b >= 0) { Select(b); ShowBandMenu(b); e.Handled = true; }
            return;
        }
        if (e.ClickCount == 2)
        {
            if (HitDot(pt, w, h) < 0) { AddBandAt(XToFreq(pt.X, w)); e.Handled = true; }
            return;
        }
        int hit = HitDot(pt, w, h);
        Select(hit);
        if (hit >= 0)
        {
            _drag = hit; e.Pointer.Capture(this);
            _gestFreq = true; _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, hit * PerBand + FreqF, "");
            if (HasGain(BandType(hit))) { _gestGain = true; _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, hit * PerBand + GainF, ""); }
            Apply(pt, w, h);
        }
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag < 0) return;
        Apply(e.GetPosition(this), Bounds.Width, Bounds.Height);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag >= 0)
        {
            if (_gestFreq) _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, _drag * PerBand + FreqF, "");
            if (_gestGain) _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, _drag * PerBand + GainF, "");
        }
        _drag = -1; _gestFreq = _gestGain = false;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        int b = _selected >= 0 && BandOn(_selected) ? _selected : HitDot(e.GetPosition(this), Bounds.Width, Bounds.Height);
        if (b < 0) return;
        double q = Math.Clamp(P(b, QF) * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.1, 18);
        int pi = b * PerBand + QF;
        _engine.BeginAutomationWrite(_track, AutomationTarget.DeviceParam, _device, pi, "");
        SetP(b, QF, q);
        _engine.EndAutomationWrite(_track, AutomationTarget.DeviceParam, _device, pi, "");
        e.Handled = true; InvalidateVisual();
    }

    private void Apply(Point p, double w, double h)
    {
        if (_drag < 0) return;
        SetP(_drag, FreqF, Math.Clamp(XToFreq(p.X, w), Fmin, Fmax));
        if (HasGain(BandType(_drag))) SetP(_drag, GainF, YToGain(p.Y, h));
        InvalidateVisual();
    }

    private void AddBandAt(double freq)
    {
        for (int b = 0; b < Bands; b++)
        {
            if (BandOn(b)) continue;
            SetP(b, On, 1); SetP(b, TypeF, Bell); SetP(b, FreqF, Math.Clamp(freq, Fmin, Fmax));
            SetP(b, GainF, 0); SetP(b, QF, 0.7);
            Select(b); return;
        }
    }

    private void ShowBandMenu(int b)
    {
        var flyout = new MenuFlyout();
        int curT = BandType(b);
        for (int t = 0; t < TypeNames.Length; t++)
        {
            int tt = t;
            var mi = new MenuItem { Header = (t == curT ? "● " : "   ") + TypeNames[t] };
            mi.Click += (_, _) => { SetP(b, TypeF, tt); InvalidateVisual(); SelectionChanged?.Invoke(); };
            flyout.Items.Add(mi);
        }
        flyout.Items.Add(new Separator());
        int curM = BandMode(b);
        for (int m = 0; m < ModeNames.Length; m++)
        {
            int mm = m;
            var mi = new MenuItem { Header = (m == curM ? "● " : "   ") + ModeNames[m], IsEnabled = HasGain(curT) || m == 0 };
            mi.Click += (_, _) =>
            {
                // Seed a working Range when engaging dynamics (default is 0 → no GR ever).
                if (mm != 0 && Math.Abs(P(b, RangeF)) < 0.01) SetP(b, RangeF, mm == 1 ? -6.0 : 6.0);
                SetP(b, ModeF, mm); InvalidateVisual(); SelectionChanged?.Invoke();
            };
            flyout.Items.Add(mi);
        }
        flyout.Items.Add(new Separator());
        var rm = new MenuItem { Header = "Remove band" };
        rm.Click += (_, _) => { SetP(b, On, 0); if (_selected == b) Select(-1); InvalidateVisual(); SelectionChanged?.Invoke(); };
        flyout.Items.Add(rm);
        flyout.ShowAt(this, showAtPointer: true);
    }

    // ---- render ----
    private void Label(DrawingContext ctx, string s, double x, double y, IBrush brush, double size = 8)
        => ctx.DrawText(new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, brush), new Point(x, y));
    private void DotLabel(DrawingContext ctx, string s, double cx, double cy, IBrush brush, double size)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, DotFont, size, brush);
        ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, sr = Sr;
        if (w <= 0 || h <= 0) return;
        ctx.FillRectangle(Bg, new Rect(0, 0, w, h), 5);

        // grid
        double[] majors = { 100, 1000, 10000 };
        double[] minors = { 30, 50, 70, 200, 300, 500, 700, 2000, 3000, 5000, 7000, 20000 };
        foreach (var f in minors) { double x = FreqToX(f, w); ctx.DrawLine(GridPenFaint, new Point(x, 0), new Point(x, h)); }
        foreach (var f in majors)
        {
            double x = FreqToX(f, w);
            ctx.DrawLine(GridPen, new Point(x, 0), new Point(x, h));
            Label(ctx, f >= 1000 ? $"{f / 1000:0}k" : $"{f:0}", x + 2, h - 11, LabelDim);
        }
        for (int db = -12; db <= 12; db += 6)
        {
            double y = GainToY(db, h);
            ctx.DrawLine(db == 0 ? ZeroPen : GridPen, new Point(0, y), new Point(w, y));
            if (db != 0) Label(ctx, $"{(db > 0 ? "+" : "")}{db}", 2, y - 9, LabelDim);
        }

        // reach lines for dynamic bands (how far each can travel)
        for (int b = 0; b < Bands; b++)
        {
            if (!IsDyn(b)) continue;
            double x = FreqToX(P(b, FreqF), w);
            double y0 = GainToY(P(b, GainF), h);
            double y1 = GainToY(P(b, GainF) + P(b, RangeF), h);
            ctx.DrawLine(ReachPen, new Point(x, y0), new Point(x, y1));
        }

        // static curve (brass) + fill
        DrawCurve(ctx, w, h, sr, false, CurveFill, CurvePen);
        // momentary curve (teal dashed) — only meaningful when something is engaged
        DrawCurve(ctx, w, h, sr, true, null, DynPen);

        // band dots
        for (int b = 0; b < Bands; b++)
        {
            bool on = BandOn(b);
            int type = BandType(b);
            double x = FreqToX(P(b, FreqF), w);
            double y = GainToY(HasGain(type) ? P(b, GainF) : 0, h);
            bool sel = b == _selected;
            bool dyn = IsDyn(b);
            double r = sel ? 8.5 : 7;
            IBrush fill = !on ? DotOff : dyn ? DotTeal : DotBrass;
            var ring = sel ? new Pen(SelRing, 2) : new Pen(DotRing, 1.5);
            ctx.DrawEllipse(fill, ring, new Point(x, y), r, r);
            DotLabel(ctx, (b + 1).ToString(), x, y, on ? OnAccentText : LabelDim, 9.5);
        }

        // readout
        string txt;
        if (_selected >= 0 && BandOn(_selected))
        {
            int b = _selected, type = BandType(b);
            string dynTxt = IsDyn(b) ? $"  {(BandMode(b) == 1 ? "↓" : "↑")} {P(b, RangeF):+0.0;-0.0} dB · GR {_gr[b]:+0.0;-0.0;0.0}" : "";
            txt = string.Format(CultureInfo.InvariantCulture, "B{0} {1}  {2:0} Hz{3}  Q {4:0.00}{5}",
                b + 1, TypeNames[type], P(b, FreqF),
                HasGain(type) ? $"  {P(b, GainF):+0.0;-0.0} dB" : "", P(b, QF), dynTxt);
        }
        else
        {
            int active = 0; for (int b = 0; b < Bands; b++) if (BandOn(b)) active++;
            txt = $"{active}/8 bands · double-click to add · right-click a dot for type/dynamics · wheel = Q";
        }
        Label(ctx, txt, 6, 4, LabelBright);
    }

    private void DrawCurve(DrawingContext ctx, double w, double h, double sr, bool momentary, IBrush? fill, IPen pen)
    {
        const int n = 160;
        var curve = new StreamGeometry();
        var fillGeo = fill != null ? new StreamGeometry() : null;
        using (var gc = curve.Open())
        {
            var gf = fillGeo?.Open();
            gf?.BeginFigure(new Point(0, h / 2), true);
            for (int i = 0; i <= n; i++)
            {
                double x = w * i / n;
                double y = Math.Clamp(GainToY(MagnitudeDb(XToFreq(x, w), sr, momentary), h), -2, h + 2);
                var pt = new Point(x, y);
                if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
                gf?.LineTo(pt);
            }
            if (gf != null) { gf.LineTo(new Point(w, h / 2)); gf.EndFigure(true); gf.Dispose(); }
        }
        if (fill != null && fillGeo != null) ctx.DrawGeometry(fill, null, fillGeo);
        ctx.DrawGeometry(null, pen, curve);
    }
}
