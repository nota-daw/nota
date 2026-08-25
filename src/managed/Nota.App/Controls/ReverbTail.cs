// SPDX-License-Identifier: AGPL-3.0-only OR LicenseRef-Nota-Commercial
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for dual-license terms.
//
// Interactive reverb decay tail (mockup 2h): a real decaying impulse response with the
// teal pre-delay gap marked and RT60 printed. Drag the tail (right area) to set decay,
// drag near the gap edge to set pre-delay. Reads/writes the device's Decay + Pre-Delay
// params with automation gestures.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class ReverbTail : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Accent = NotaPalette.AccentBright;
    private static readonly IBrush Teal = NotaPalette.Teal;
    private static readonly IBrush GridB = new SolidColorBrush(Color.FromArgb(0x40, 0x3A, 0x36, 0x2D));
    private static readonly IBrush Fill = new SolidColorBrush(Color.FromArgb(0x20, 0xD8, 0xA0, 0x3D));
    private static readonly IBrush TealFill = new SolidColorBrush(Color.FromArgb(0x1E, 0x5B, 0x9E, 0x9C));
    private static readonly IBrush Axis = NotaPalette.TextDisabled;
    private static readonly Typeface Face = new(FontFamily.Default);

    private readonly IAudioEngine _e;
    private readonly int _t, _di, _decayP, _preP;
    private readonly Func<double, double> _rt60;
    private const double AxisSec = 4.0, MaxPreMs = 200.0, GapFrac = 0.18;
    private int _drag = -1;   // 0 = decay, 1 = pre-delay

    public ReverbTail(IAudioEngine e, int t, int di, int decayP, int preP, Func<double, double> rt60)
    { _e = e; _t = t; _di = di; _decayP = decayP; _preP = preP; _rt60 = rt60; MinHeight = 96; }
    public void Tick() => InvalidateVisual();

    private float P(int p) => _e.DeviceGetParam(_t, _di, p);
    private (double x0, double x1, double top, double bot) Geo() => (8, Bounds.Width - 8, 14, Bounds.Height - 14);
    private double GapX() { var (x0, x1, _, _) = Geo(); return x0 + P(_preP) * GapFrac * (x1 - x0); }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        var p = e.GetPosition(this);
        _drag = Math.Abs(p.X - GapX()) < 10 ? 1 : 0;
        Gest(true); e.Pointer.Capture(this); Apply(p); e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag >= 0) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (_drag >= 0) { Gest(false); _drag = -1; e.Pointer.Capture(null); } }

    private void Gest(bool begin)
    {
        int p = _drag == 1 ? _preP : _decayP;
        if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.DeviceParam, _di, p, "");
        else _e.EndAutomationWrite(_t, AutomationTarget.DeviceParam, _di, p, "");
    }
    private void Apply(Point pt)
    {
        var (x0, x1, _, _) = Geo();
        if (_drag == 1) _e.DeviceSetParam(_t, _di, _preP, (float)Math.Clamp((pt.X - x0) / (GapFrac * (x1 - x0)), 0, 1));
        else _e.DeviceSetParam(_t, _di, _decayP, (float)Math.Clamp((pt.X - x0) / (x1 - x0), 0, 1));
        InvalidateVisual();
    }

    private void Txt(DrawingContext c, string t, double x, double y, IBrush b) => c.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 5, 5);
        var (x0, x1, top, bot) = Geo();
        for (int i = 1; i <= 3; i++) { double gy = top + (bot - top) * i / 4.0; ctx.DrawLine(new Pen(GridB, 1), new Point(x0, gy), new Point(x1, gy)); }

        double gapX = GapX();
        // Pre-delay gap (teal).
        ctx.FillRectangle(TealFill, new Rect(x0, top, gapX - x0, bot - top));
        ctx.DrawLine(new Pen(Teal, 1.4), new Point(gapX, top), new Point(gapX, bot));

        // Decay envelope: exp(-6.9 t / RT60) over a 4 s axis, drawn as hair lines + fill.
        double rt60 = Math.Max(0.05, _rt60(P(_decayP)));
        var geo = new StreamGeometry();
        using (var g = geo.Open())
        {
            g.BeginFigure(new Point(gapX, bot), true);
            for (double x = gapX; x <= x1; x += 2)
            {
                double t = (x - gapX) / (x1 - gapX) * AxisSec;
                double env = Math.Exp(-6.9 * t / rt60);
                g.LineTo(new Point(x, bot - env * (bot - top)));
            }
            g.LineTo(new Point(x1, bot)); g.EndFigure(true);
        }
        ctx.DrawGeometry(Fill, null, geo);
        var pen = new Pen(Accent, 1.6, lineJoin: PenLineJoin.Round);
        Point? prev = null;
        for (double x = gapX; x <= x1; x += 3)
        {
            double t = (x - gapX) / (x1 - gapX) * AxisSec;
            double env = Math.Exp(-6.9 * t / rt60);
            var cur = new Point(x, bot - env * (bot - top));
            if (prev is { } pp) ctx.DrawLine(pen, pp, cur);
            prev = cur;
            if (((int)x & 3) == 0) ctx.DrawLine(new Pen(Fill, 1), new Point(x, bot), new Point(x, bot - env * (bot - top)));
        }

        Txt(ctx, $"DECAY TAIL", x0 + 2, top - 1, Axis);
        Txt(ctx, $"RT60 {rt60:0.00} s", x1 - 66, top - 1, Accent);
        Txt(ctx, $"pre {P(_preP) * MaxPreMs:0} ms", gapX + 3, bot - 10, Teal);
        Txt(ctx, "4 s", x1 - 16, bot + 2, Axis);
    }
}
