// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// EQ-8 editor (eight-band mixing-console style). Draws the combined magnitude response over
// a log-frequency / dB grid with a real-time spectrum analyzer behind it, and one
// numbered draggable dot per enabled band. Drag = freq (+gain for shelves/bells);
// mouse wheel = Q; double-click empty space enables a new band; right-click a dot
// picks its filter type or removes it. Dots + curve are wired straight to the
// device params (8 bands × 5: On/Type/Freq/Gain/Q). The engine owns the DSP; the
// drawn curve mirrors its RBJ biquads, and the analyzer reads a pre-EQ scope ring.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

public sealed class EqCurve : Control
{
    private const int Bands = 8, PerBand = 5;
    private const int On = 0, TypeF = 1, FreqF = 2, GainF = 3, QF = 4;
    // Filter types (mirror Eq.h).
    private const int LowCut = 0, LowShelf = 1, Bell = 2, Notch = 3, HighShelf = 4, HighCut = 5;
    private static readonly string[] TypeNames = { "Low cut", "Low shelf", "Bell", "Notch", "High shelf", "High cut" };

    private const double Fmin = 20, Fmax = 20000, GMax = 18;
    private const int FftN = 2048, Bins = FftN / 2;

    private static readonly IBrush Bg = NotaPalette.BgSunken;
    private static readonly IPen GridPen = new Pen(new SolidColorBrush(Color.Parse("#232019")), 1);
    private static readonly IPen GridPenFaint = new Pen(new SolidColorBrush(Color.FromArgb(0x60, 0x23, 0x20, 0x19)), 1);
    private static readonly IPen ZeroPen = new Pen(NotaPalette.BorderStrong, 1);
    private static readonly IPen CurvePen = new Pen(NotaPalette.Accent, 1.6);
    private static readonly IBrush CurveFill = new SolidColorBrush(Color.FromArgb(0x1E, 0xD8, 0xA0, 0x3D));
    private static readonly IBrush SpecFill = new SolidColorBrush(Color.FromArgb(0x30, 0x8A, 0xA6, 0xC0));
    private static readonly IPen SpecPen = new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0x9C, 0xB4, 0xCC)), 1);
    private static readonly IBrush DotOn = NotaPalette.AccentBright;
    private static readonly IBrush DotSel = new SolidColorBrush(Color.Parse("#FFFFFF"));
    private static readonly IBrush DotRing = NotaPalette.BgSunken;
    private static readonly IBrush LabelDim = NotaPalette.TextTertiary;
    private static readonly IBrush LabelBright = NotaPalette.TextSecondary;
    private static readonly IBrush OnAccentText = NotaPalette.TextOnAccent;
    private static readonly Typeface Mono = new("monospace");
    private static readonly Typeface DotFont = new(FontFamily.Default, FontStyle.Normal, FontWeight.Bold);

    private readonly IAudioEngine _engine;
    private readonly int _track, _device;
    private int _drag = -1, _selected = -1;

    // Analyzer state: pre-EQ scope → FFT → smoothed magnitude (dBFS) per bin.
    private readonly float[] _scope = new float[FftN];
    private readonly double[] _re = new double[FftN], _im = new double[FftN];
    private readonly double[] _specDb = new double[Bins];
    private readonly double[] _hann = new double[FftN];

    public EqCurve(IAudioEngine engine, int track, int device)
    {
        _engine = engine;
        _track = track;
        _device = device;
        MinHeight = 150;
        for (int i = 0; i < FftN; i++) _hann[i] = 0.5 - 0.5 * Math.Cos(2 * Math.PI * i / (FftN - 1));
        for (int i = 0; i < Bins; i++) _specDb[i] = -120;
    }

    // ---- param helpers ----------------------------------------------------
    private float P(int band, int field) => _engine.DeviceGetParam(_track, _device, band * PerBand + field);
    private void SetP(int band, int field, double v) => _engine.DeviceSetParam(_track, _device, band * PerBand + field, (float)v);
    private bool BandOn(int b) => P(b, On) > 0.5f;
    private int BandType(int b) => Math.Clamp((int)Math.Round(P(b, TypeF)), 0, 5);
    private static bool HasGain(int type) => type is LowShelf or Bell or HighShelf;

    // ---- coordinate mapping ----------------------------------------------
    private double FreqToX(double f, double w) => w * Math.Log(f / Fmin) / Math.Log(Fmax / Fmin);
    private double XToFreq(double x, double w) => Fmin * Math.Pow(Fmax / Fmin, Math.Clamp(x / w, 0, 1));
    private double GainToY(double g, double h) => h * (0.5 - g / (2 * GMax));
    private double YToGain(double y, double h) => Math.Clamp(GMax * (1 - 2 * y / h), -GMax, GMax);

    // ---- magnitude response (mirrors Eq.h RBJ biquads) --------------------
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

    // Combined response in dB at frequency f (sum of enabled bands).
    private double MagnitudeDb(double f, double sr)
    {
        double total = 0;
        double w = 2 * Math.PI * f / sr, cw = Math.Cos(w), sw = Math.Sin(w);
        double cos2 = Math.Cos(2 * w), sin2 = Math.Sin(2 * w);
        for (int b = 0; b < Bands; b++)
        {
            if (!BandOn(b)) continue;
            var (b0, b1, b2, a1, a2) = Coeffs(BandType(b), sr, P(b, FreqF), P(b, GainF), P(b, QF));
            double numR = b0 + b1 * cw + b2 * cos2, numI = -(b1 * sw + b2 * sin2);
            double denR = 1 + a1 * cw + a2 * cos2, denI = -(a1 * sw + a2 * sin2);
            double mag2 = (numR * numR + numI * numI) / Math.Max(1e-12, denR * denR + denI * denI);
            total += 10 * Math.Log10(Math.Max(1e-12, mag2));
        }
        return total;
    }

    // ---- analyzer: pull scope, window, FFT, smooth ------------------------
    public void Tick()
    {
        int n = _engine.DeviceScope(_track, _device, _scope, FftN);
        if (n < FftN) { Decay(); InvalidateVisual(); return; }
        for (int i = 0; i < FftN; i++) { _re[i] = _scope[i] * _hann[i]; _im[i] = 0; }
        Fft(_re, _im);
        double sr = _engine.SampleRate > 0 ? _engine.SampleRate : 48000;
        double refMag = FftN / 4.0;   // Hann-windowed full-scale sine → ~0 dB
        for (int k = 1; k < Bins; k++)
        {
            double mag = Math.Sqrt(_re[k] * _re[k] + _im[k] * _im[k]);
            double db = 20 * Math.Log10(mag / refMag + 1e-9);
            // Fast attack, slow release for a lively-but-readable trace.
            _specDb[k] = db > _specDb[k] ? db : Math.Max(db, _specDb[k] - 2.5);
        }
        _sr = sr;
        InvalidateVisual();
    }
    private double _sr = 48000;
    private void Decay() { for (int k = 1; k < Bins; k++) _specDb[k] = Math.Max(-120, _specDb[k] - 2.5); }

    // In-place iterative radix-2 Cooley–Tukey FFT.
    private static void Fft(double[] re, double[] im)
    {
        int n = re.Length;
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1) j ^= bit;
            j ^= bit;
            if (i < j) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            for (int i = 0; i < n; i += len)
            {
                double cr = 1, ci = 0;
                for (int k = 0; k < len / 2; k++)
                {
                    int a = i + k, b = i + k + len / 2;
                    double tr = re[b] * cr - im[b] * ci, ti = re[b] * ci + im[b] * cr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                    double ncr = cr * wr - ci * wi; ci = cr * wi + ci * wr; cr = ncr;
                }
            }
        }
    }

    // ---- interaction ------------------------------------------------------
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
            if (b >= 0) { _selected = b; ShowBandMenu(b); e.Handled = true; InvalidateVisual(); }
            return;
        }

        if (e.ClickCount == 2)   // double-click empty space → enable a new band here
        {
            if (HitDot(pt, w, h) < 0) { AddBandAt(XToFreq(pt.X, w)); e.Handled = true; }
            return;
        }

        int hit = HitDot(pt, w, h);
        _selected = hit;
        if (hit >= 0) { _drag = hit; e.Pointer.Capture(this); Apply(pt, w, h); }
        e.Handled = true;
        InvalidateVisual();
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        if (_drag < 0) return;
        Apply(e.GetPosition(this), Bounds.Width, Bounds.Height);
    }

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        _drag = -1;
        e.Pointer.Capture(null);
    }

    protected override void OnPointerWheelChanged(PointerWheelEventArgs e)
    {
        int b = _selected >= 0 ? _selected : HitDot(e.GetPosition(this), Bounds.Width, Bounds.Height);
        if (b < 0) return;
        double q = Math.Clamp(P(b, QF) * (e.Delta.Y > 0 ? 1.15 : 1 / 1.15), 0.1, 18);
        SetP(b, QF, q);
        e.Handled = true;
        InvalidateVisual();
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
            SetP(b, On, 1);
            SetP(b, TypeF, Bell);
            SetP(b, FreqF, Math.Clamp(freq, Fmin, Fmax));
            SetP(b, GainF, 0);
            SetP(b, QF, 0.7);
            _selected = b;
            InvalidateVisual();
            return;
        }
    }

    private void ShowBandMenu(int b)
    {
        var flyout = new MenuFlyout();
        int cur = BandType(b);
        for (int t = 0; t < TypeNames.Length; t++)
        {
            int tt = t;
            var mi = new MenuItem { Header = (t == cur ? "● " : "   ") + TypeNames[t] };
            mi.Click += (_, _) => { SetP(b, TypeF, tt); InvalidateVisual(); };
            flyout.Items.Add(mi);
        }
        flyout.Items.Add(new Separator());
        var rm = new MenuItem { Header = "Remove band" };
        rm.Click += (_, _) => { SetP(b, On, 0); if (_selected == b) _selected = -1; InvalidateVisual(); };
        flyout.Items.Add(rm);
        flyout.ShowAt(this, showAtPointer: true);
    }

    // ---- render -----------------------------------------------------------
    private void Label(DrawingContext ctx, string s, double x, double y, IBrush brush, double size = 8)
        => ctx.DrawText(new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, size, brush), new Point(x, y));

    // Bold digit centred on (cx, cy) using the text's own metrics.
    private void DotLabel(DrawingContext ctx, string s, double cx, double cy, IBrush brush, double size)
    {
        var ft = new FormattedText(s, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, DotFont, size, brush);
        ctx.DrawText(ft, new Point(cx - ft.Width / 2, cy - ft.Height / 2));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        double sr = _sr;
        ctx.FillRectangle(Bg, new Rect(0, 0, w, h), 5);

        // --- grid: vertical decade lines + minor ticks, horizontal dB lines ---
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

        // --- spectrum analyzer (behind the curve) ---
        var spec = new StreamGeometry();
        using (var g = spec.Open())
        {
            g.BeginFigure(new Point(0, h), true);
            for (int k = 1; k < Bins; k++)
            {
                double f = k * sr / FftN;
                if (f < Fmin || f > Fmax) continue;
                double x = FreqToX(f, w);
                double norm = Math.Clamp((_specDb[k] + 84) / 84, 0, 1);   // -84..0 dBFS → 0..1
                g.LineTo(new Point(x, h - norm * h));
            }
            g.LineTo(new Point(w, h));
            g.EndFigure(true);
        }
        ctx.DrawGeometry(SpecFill, SpecPen, spec);

        // --- combined EQ magnitude curve + fill ---
        const int n = 128;
        var curve = new StreamGeometry();
        var fill = new StreamGeometry();
        using (var gc = curve.Open())
        using (var gf = fill.Open())
        {
            gf.BeginFigure(new Point(0, h / 2), true);
            for (int i = 0; i <= n; i++)
            {
                double x = w * i / n;
                double y = Math.Clamp(GainToY(MagnitudeDb(XToFreq(x, w), sr), h), -2, h + 2);
                var pt = new Point(x, y);
                if (i == 0) gc.BeginFigure(pt, false); else gc.LineTo(pt);
                gf.LineTo(pt);
            }
            gf.LineTo(new Point(w, h / 2));
            gf.EndFigure(true);
        }
        ctx.DrawGeometry(CurveFill, null, fill);
        ctx.DrawGeometry(null, CurvePen, curve);

        // --- band dots (numbered) ---
        for (int b = 0; b < Bands; b++)
        {
            if (!BandOn(b)) continue;
            int type = BandType(b);
            double x = FreqToX(P(b, FreqF), w);
            double y = GainToY(HasGain(type) ? P(b, GainF) : 0, h);
            bool sel = b == _selected;
            double r = sel ? 8 : 6.5;
            ctx.DrawEllipse(sel ? DotSel : DotOn, new Pen(DotRing, 1.5), new Point(x, y), r, r);
            DotLabel(ctx, (b + 1).ToString(), x, y, OnAccentText, 9.5);
        }

        // --- readout: selected band (or a hint) ---
        string txt;
        if (_selected >= 0 && BandOn(_selected))
        {
            int b = _selected, type = BandType(b);
            txt = string.Format(CultureInfo.InvariantCulture, "B{0} {1}  {2:0} Hz{3}  Q {4:0.00}",
                b + 1, TypeNames[type], P(b, FreqF),
                HasGain(type) ? string.Format(CultureInfo.InvariantCulture, "  {0:+0.0;-0.0} dB", P(b, GainF)) : "",
                P(b, QF));
        }
        else
        {
            int active = 0; for (int b = 0; b < Bands; b++) if (BandOn(b)) active++;
            txt = $"{active}/8 bands · double-click to add · right-click a dot for type · wheel = Q";
        }
        Label(ctx, txt, 6, h - 11, LabelBright);
    }
}
