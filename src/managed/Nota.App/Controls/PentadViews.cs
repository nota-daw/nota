// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Custom-drawn views for the Nota Pentad editor card: waveform glyphs (saw / pulse /
// triangle / square), the draggable 4-pole low-pass response, the ADSR curves (analog
// RC shapes), the voice-activity strip and the level bars. Values are pushed in by the
// card's refresh tick; the filter curve is the only one that edits (drag X = cutoff,
// Y = resonance).

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

// 0 saw, 1 pulse, 2 triangle, 3 square.
internal sealed class PentadWaveIcon : Control
{
    private readonly int _w;
    public IBrush Stroke { get; set; } = NotaPalette.TextTertiary;
    public PentadWaveIcon(int wave, double w = 14, double h = 7) { _w = wave; Width = w; Height = h; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height, top = 0.8, bot = h - 0.8;
        var g = new StreamGeometry();
        using (var c = g.Open())
        {
            switch (_w)
            {
                case 0:   // two saw teeth
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(w * 0.45, top)); c.LineTo(new Point(w * 0.45, bot)); c.LineTo(new Point(w * 0.9, top)); c.LineTo(new Point(w * 0.9, bot));
                    break;
                case 1:   // narrow-ish pulse
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(0, top)); c.LineTo(new Point(w * 0.4, top)); c.LineTo(new Point(w * 0.4, bot)); c.LineTo(new Point(w, bot));
                    break;
                case 2:   // triangle
                    c.BeginFigure(new Point(0, h / 2), false);
                    c.LineTo(new Point(w * 0.25, top)); c.LineTo(new Point(w * 0.75, bot)); c.LineTo(new Point(w, h / 2));
                    break;
                default:  // square
                    c.BeginFigure(new Point(0, bot), false);
                    c.LineTo(new Point(0, top)); c.LineTo(new Point(w * 0.5, top)); c.LineTo(new Point(w * 0.5, bot)); c.LineTo(new Point(w, bot)); c.LineTo(new Point(w, top));
                    break;
            }
        }
        ctx.DrawGeometry(null, new Pen(Stroke, 1.4, lineJoin: PenLineJoin.Round), g);
    }
}

// 24 dB/oct low-pass magnitude over 20 Hz..20 kHz (log X), with the resonant peak and the
// CEM-style passband loss at high resonance (unless bass compensation is on). Drag to edit.
internal sealed class PentadFilterCurve : Control
{
    private static readonly IBrush Grid = NotaPalette.SurfaceCard;
    private static readonly IBrush Fill = NotaPalette.Wash(NotaPalette.Accent, 0x1E);
    private static readonly Typeface Mono = new("ui-monospace, monospace");
    private double _cut = 0.5, _reso, _comp;
    private bool _drag;

    public Action<double, double>? Changed;   // (cutoffNorm, resoNorm) while dragging
    public event Action? DragStarted;
    public event Action? DragEnded;

    public PentadFilterCurve() { Cursor = new Cursor(StandardCursorType.Hand); ClipToBounds = true; }

    public void Set(double cutoffNorm, double resoNorm, bool bassComp) { _cut = cutoffNorm; _reso = resoNorm; _comp = bassComp ? 1 : 0; }

    private void Apply(PointerEventArgs e)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0 || h <= 0) return;
        var p = e.GetPosition(this);
        _cut = Math.Clamp(p.X / w, 0, 1);
        _reso = Math.Clamp(1 - p.Y / h, 0, 1);
        Changed?.Invoke(_cut, _reso); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; _drag = true; DragStarted?.Invoke(); Apply(e); e.Pointer.Capture(this); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag) { _drag = false; DragEnded?.Invoke(); e.Pointer.Capture(null); } }

    // dB at normalized x (0..1 across 20 Hz..20 kHz): 4 identical poles with feedback k.
    private double Db(double x)
    {
        double k = 4.6 * _reso * 0.97;                     // stay just below the self-osc singularity
        double wr = Math.Pow(1000, x - _cut);              // ω / ωc
        // H = G⁴ / (1 + k G⁴), G = 1 / (1 + j wr); input scaled by (1 + comp k).
        double re = 1, im = wr;                            // 1 + j wr
        // (1 + j wr)^4
        double r2 = re * re - im * im, i2 = 2 * re * im;
        double r4 = r2 * r2 - i2 * i2, i4 = 2 * r2 * i2;
        double dr = r4 + k, di = i4;                        // (1 + j wr)^4 + k
        double mag = (1 + _comp * k) / Math.Sqrt(dr * dr + di * di);
        return 20 * Math.Log10(Math.Max(mag, 1e-6));
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        ctx.FillRectangle(Brushes.Transparent, new Rect(0, 0, w, h));
        var gp = new Pen(Grid, 1);
        foreach (double hz in new[] { 100.0, 1000.0, 10000.0 }) { double gx = Math.Log10(hz / 20) / 3 * w; ctx.DrawLine(gp, new Point(gx, 0), new Point(gx, h)); }
        double y0 = h * 0.38;                               // 0 dB line
        ctx.DrawLine(gp, new Point(0, y0), new Point(w, y0));
        double Y(double db) => Math.Clamp(y0 - db * (h * 0.018), 1, h - 1);

        var line = new StreamGeometry(); var fill = new StreamGeometry();
        using (var lc = line.Open())
        using (var fc = fill.Open())
        {
            lc.BeginFigure(new Point(0, Y(Db(0))), false);
            fc.BeginFigure(new Point(0, h), true);
            fc.LineTo(new Point(0, Y(Db(0))));
            for (double x = 1; x <= w; x += 1.5) { var p = new Point(x, Y(Db(x / w))); lc.LineTo(p); fc.LineTo(p); }
            fc.LineTo(new Point(w, h));
        }
        ctx.DrawGeometry(Fill, null, fill);
        ctx.DrawGeometry(null, new Pen(NotaPalette.Accent, 1.5), line);

        double hz0 = 20 * Math.Pow(1000, _cut);
        string txt = (hz0 >= 1000 ? $"{hz0 / 1000:0.00} kHz" : $"{hz0:0} Hz") + $" · Q {_reso * 10:0.0}";
        var ft = new FormattedText(txt, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, NotaPalette.AccentBright);
        ctx.DrawText(ft, new Point(w - ft.Width - 4, 3));

        double hx = Math.Clamp(_cut * w, 4, w - 4), hy = Math.Clamp(Y(Db(_cut)), 4, h - 4);
        ctx.DrawEllipse(NotaPalette.AccentBright, new Pen(NotaPalette.BgSunken, 2), new Point(hx, hy), 3.5, 3.5);
    }
}

// ADSR with RC curves: attack charges toward an overshoot (concave), decay/release discharge.
internal sealed class PentadEnvCurve : Control
{
    private double _a, _d, _s = 0.8, _r = 0.4;
    public IBrush Accent { get; set; } = NotaPalette.Accent;
    public PentadEnvCurve() { ClipToBounds = true; }

    public void Set(double a, double d, double s, double r) { _a = a; _d = d; _s = s; _r = r; }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        double top = 3, bot = h - 2, span = bot - top;
        double wa = w * (0.03 + 0.24 * _a), wd = w * (0.04 + 0.24 * _d), wr = w * (0.04 + 0.24 * _r);
        double ws = Math.Max(w * 0.08, w - wa - wd - wr);
        double xa = wa, xd = xa + wd, xs = xd + ws;
        double Lv(double v) => bot - span * v;
        var line = new StreamGeometry(); var fill = new StreamGeometry();
        void Path(StreamGeometryContext c, bool closed)
        {
            c.BeginFigure(new Point(0, bot), closed);
            const int n = 14;
            for (int i = 1; i <= n; i++) { double t = i / (double)n; double v = (1 - Math.Exp(-t * 1.47)) / (1 - Math.Exp(-1.47)); c.LineTo(new Point(xa * t, Lv(v))); }
            for (int i = 1; i <= n; i++) { double t = i / (double)n; double v = _s + (1 - _s) * Math.Exp(-t * 4); c.LineTo(new Point(xa + wd * t, Lv(v))); }
            c.LineTo(new Point(xs, Lv(_s)));
            for (int i = 1; i <= n; i++) { double t = i / (double)n; double v = _s * Math.Exp(-t * 4); c.LineTo(new Point(xs + wr * t, Lv(v))); }
            c.LineTo(new Point(w, bot));
        }
        using (var lc = line.Open()) Path(lc, false);
        using (var fc = fill.Open()) Path(fc, true);
        var col = ((ISolidColorBrush)Accent).Color;
        ctx.DrawGeometry(new SolidColorBrush(Color.FromArgb(0x1F, col.R, col.G, col.B)), null, fill);
        ctx.DrawGeometry(null, new Pen(Accent, 1.4), line);
    }
}

// N voice cells, lit (brass) while a voice sounds, brightness following its level.
internal sealed class PentadVoiceStrip : Control
{
    private static readonly Typeface Mono = new("ui-monospace, monospace");
    private readonly float[] _lv = new float[16];
    private int _n = 5;

    public void Set(int count, ReadOnlySpan<float> levels)
    {
        _n = Math.Clamp(count, 1, 16);
        for (int i = 0; i < 16; i++) _lv[i] = i < levels.Length ? levels[i] : 0f;
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        const double gap = 3;
        double cw = Math.Min(h, (w - gap * (_n - 1)) / _n);
        double x0 = w - (cw * _n + gap * (_n - 1));    // right-aligned
        var off = new Pen(NotaPalette.BorderDefault, 1);
        var on = new Pen(NotaPalette.Accent, 1);
        var ac = NotaPalette.AccentColor;
        for (int i = 0; i < _n; i++)
        {
            var r = new Rect(x0 + i * (cw + gap) + 0.5, (h - cw) / 2 + 0.5, cw - 1, cw - 1);
            float lv = _lv[i];
            bool lit = lv > 0.0005f;
            byte a = (byte)(lit ? 0x22 + (int)(Math.Clamp(lv, 0f, 1f) * 0x40) : 0);
            ctx.DrawRectangle(lit ? new SolidColorBrush(Color.FromArgb(a, ac.R, ac.G, ac.B)) : NotaPalette.BgSunken, lit ? on : off, r, 3, 3);
            if (cw >= 12)
            {
                var ft = new FormattedText((i + 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Mono, 7, lit ? NotaPalette.AccentBright : NotaPalette.TextDisabled);
                ctx.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
            }
        }
    }
}

// A level bar in dBFS (−48..+3): green, amber above −6 dB. Vertical or horizontal.
internal sealed class PentadLevelBar : Control
{
    private double _db = -100;
    public bool Vertical { get; init; }
    public void SetLinear(double peak) { _db = peak > 1e-6 ? 20 * Math.Log10(peak) : -100; InvalidateVisual(); }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 2 || h < 2) return;
        ctx.DrawRectangle(NotaPalette.BgSunken, null, new Rect(0, 0, w, h), 2, 2);
        static double Frac(double db) => Math.Clamp((db + 48) / 51.0, 0, 1);
        double f = Frac(_db), fw = Frac(-6);
        if (f <= 0) return;
        if (Vertical)
        {
            double ih = h - 2;
            double gTop = h - 1 - ih * Math.Min(f, fw);
            ctx.FillRectangle(NotaPalette.Success, new Rect(1, gTop, w - 2, h - 1 - gTop));
            if (f > fw) ctx.FillRectangle(NotaPalette.Warning, new Rect(1, h - 1 - ih * f, w - 2, ih * (f - fw)));
        }
        else
        {
            double iw = w;
            ctx.FillRectangle(NotaPalette.Success, new Rect(0, 0, iw * Math.Min(f, fw), h));
            if (f > fw) ctx.FillRectangle(NotaPalette.Warning, new Rect(iw * fw, 0, iw * (f - fw), h));
        }
    }
}
