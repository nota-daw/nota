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
internal sealed class PentadFilterCurve : FilterGraphBase
{
    private double _cut = 0.5, _reso, _comp;
    private bool _drag;

    public Action<double, double>? Changed;   // (cutoffNorm, resoNorm) while dragging
    public event Action? DragStarted;
    public event Action? DragEnded;

    public PentadFilterCurve() { Cursor = new Cursor(StandardCursorType.Hand); ClipToBounds = true; }

    public void Set(double cutoffNorm, double resoNorm, bool bassComp) { _cut = cutoffNorm; _reso = resoNorm; _comp = bassComp ? 1 : 0; }

    // The shared filter graph (#531) over 20 Hz … 20 kHz, the cutoff's own map.
    protected override (double Lo, double Hi) Span => (20.0, 20000.0);
    protected override (double Top, double Bottom) DbSpan => (18.0, -42.0);
    protected override double CutoffHz => 20 * Math.Pow(1000, _cut);
    protected override double ResponseDb(double hz) => Db(Math.Log(hz / 20) / Math.Log(1000));

    private void Apply(PointerEventArgs e)
    {
        var r = PlotRect; if (r.Width <= 0 || r.Height <= 0) return;
        var p = e.GetPosition(this);
        _cut = Math.Clamp((p.X - r.X) / r.Width, 0, 1);
        _reso = Math.Clamp(1 - (p.Y - r.Y) / r.Height, 0, 1);
        Changed?.Invoke(_cut, _reso); InvalidateVisual();
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { base.OnPointerPressed(e); if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return; _drag = Dragging = true; DragStarted?.Invoke(); Apply(e); e.Pointer.Capture(this); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { base.OnPointerMoved(e); if (_drag) Apply(e); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    { base.OnPointerReleased(e); if (_drag) { _drag = Dragging = false; InvalidateVisual(); DragEnded?.Invoke(); e.Pointer.Capture(null); } }

    // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    { if (_drag) { _drag = Dragging = false; InvalidateVisual(); DragEnded?.Invoke(); } base.OnPointerCaptureLost(e); }

    // dB at normalized x (0..1 across 20 Hz..20 kHz): 4 identical poles with feedback k.
    private double Db(double x)
    {
        double k = 4.6 * _reso * 0.97;                     // stay just below the self-osc singularity
        double wr = Math.Pow(1000, x - _cut);              // ω / ωc
        // H = G⁴ / (1 + k G⁴), G = 1 / (1 + j wr); input scaled by (1 + comp k).
        double re = 1, im = wr;                            // 1 + j wr
        double r2 = re * re - im * im, i2 = 2 * re * im;
        double r4 = r2 * r2 - i2 * i2, i4 = 2 * r2 * i2;
        double dr = r4 + k, di = i4;                        // (1 + j wr)^4 + k
        double mag = (1 + _comp * k) / Math.Sqrt(dr * dr + di * di);
        return 20 * Math.Log10(Math.Max(mag, 1e-6));
    }

    public override void Render(DrawingContext ctx)
    {
        if (Bounds.Width < 2 || Bounds.Height < 2) return;
        DrawBack(ctx);
        DrawCurve(ctx, ResponseDb);
        var at = new Point(XOf(CutoffHz), YOf(ResponseDb(CutoffHz)));
        DrawFront(ctx, at.Y);
        DrawHandle(ctx, at);
    }
}

// N voice cells, lit (accent) while a voice sounds, brightness following its level.
internal sealed class PentadVoiceStrip : Control
{
    private static readonly Typeface NumFace = NotaFonts.Sans;
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
            ctx.DrawRectangle(lit ? new SolidColorBrush(Color.FromArgb(a, ac.R, ac.G, ac.B)) : NotaPalette.BgSunken, lit ? on : off, r, NotaRadius.SwitchValue, NotaRadius.SwitchValue);
            if (cw >= 12)
            {
                var ft = new NotaFormattedText((i + 1).ToString(NotaNum.Culture), CultureInfo.InvariantCulture, FlowDirection.LeftToRight, NumFace, NotaGraph.AxisSize, lit ? NotaPalette.AccentBright : NotaPalette.TextDisabled, this);
                ctx.DrawText(ft, new Point(r.X + (r.Width - ft.Width) / 2, r.Y + (r.Height - ft.Height) / 2));
            }
        }
    }
}

// A level bar in dBFS (−48..+3), drawn like every level meter (MeterScale): on the well,
// corner 2, green below −6 dB, amber to 0 dBFS, red at and above. Upright it is one 3px
// channel centred in its width (the L / R caption under it keeps the width); lying down it
// fills its height (a thin wet level).
internal sealed class PentadLevelBar : Control
{
    private double _db = -100;
    public bool Vertical { get; init; }
    public void SetLinear(double peak)
    {
        double db = peak > 1e-6 ? 20 * Math.Log10(peak) : -100;
        if (Math.Abs(db - _db) < 0.05) return;
        _db = db; InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w < 1 || h < 1) return;
        // Its own −48..+3 dBFS scale, with the shared zones in dB: amber from −6, red from 0 dBFS.
        static double Frac(double db) => Math.Clamp((db + 48) / 51.0, 0, 1);
        double f = Frac(_db), c = Frac(MeterScale.CautionDb), a = Frac(MeterScale.AlertDb);
        if (Vertical)
        {
            double cw = Math.Min(w, MeterScale.ChannelW);
            MeterScale.DrawChannel(ctx, new Rect((w - cw) / 2, 0, cw, h), f, 0, horizontal: false, c, a);
        }
        else MeterScale.DrawChannel(ctx, new Rect(0, 0, w, h), f, 0, horizontal: true, c, a);
    }
}
