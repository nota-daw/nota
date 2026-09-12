// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Compressor transfer plot (mockup 2n): input→output dB curve with a dashed unity
// diagonal, a threshold line, the soft knee drawn as a curve and the operating point
// as a lit handle. Drag the handle: left/right sets Threshold, up/down sets Ratio.
// Reads/writes the device's Threshold (0) / Ratio (1) params (raw dB / ratio) with
// automation gestures; the Knee (5) shapes the curve.

using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using Nota.Application;

namespace Nota.App;

internal sealed class CompTransfer : Control
{
    private static readonly IBrush Sunken = NotaPalette.BgSunken;
    private static readonly IBrush BorderDef = NotaPalette.BorderDefault;
    private static readonly IBrush Accent = NotaPalette.AccentBright;
    private static readonly IBrush Grid = NotaPalette.Wash(NotaPalette.BorderStrong, 0x3C);
    private static readonly IBrush Axis = NotaPalette.TextDisabled;
    private static readonly IBrush Fill = NotaPalette.Wash(NotaPalette.Accent, 0x16);
    private static readonly Typeface Face = new(FontFamily.Default);
    private const int Threshold = 0, Ratio = 1, Knee = 5;
    private const double Lo = -60, Hi = 0;

    private readonly IAudioEngine _e; private readonly int _t, _di;
    private bool _drag; private double _startY, _startRatio;

    public CompTransfer(IAudioEngine e, int t, int di) { _e = e; _t = t; _di = di; MinHeight = 90; }
    public void Tick() => InvalidateVisual();
    private float P(int p) => _e.DeviceGetParam(_t, _di, p);
    private (double x0, double x1, double top, double bot) Geo() => (18, Bounds.Width - 6, 6, Bounds.Height - 16);
    private double X(double db, double x0, double x1) => x0 + (db - Lo) / (Hi - Lo) * (x1 - x0);
    private double Y(double db, double top, double bot) => bot - (db - Lo) / (Hi - Lo) * (bot - top);

    private double OutDb(double inDb, double thr, double ratio, double knee)
    {
        double over = inDb - thr, slope = 1.0 - 1.0 / ratio, gr;
        if (knee > 0.01 && 2 * over > -knee && 2 * over < knee) { double t = over + knee / 2; gr = slope * t * t / (2 * knee); }
        else gr = over > 0 ? slope * over : 0;
        return inDb - gr;
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    { _drag = true; _startY = e.GetPosition(this).Y; _startRatio = P(Ratio); Gest(true); e.Pointer.Capture(this); Apply(e.GetPosition(this)); e.Handled = true; }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e) { if (_drag) { _drag = false; Gest(false); e.Pointer.Capture(null); } }
    private void Gest(bool begin)
    {
        void G(int p) { if (begin) _e.BeginAutomationWrite(_t, AutomationTarget.DeviceParam, _di, p, ""); else _e.EndAutomationWrite(_t, AutomationTarget.DeviceParam, _di, p, ""); }
        G(Threshold); G(Ratio);
    }
    private void Apply(Point p)
    {
        var (x0, x1, top, bot) = Geo();
        double thr = Math.Clamp(Lo + (p.X - x0) / Math.Max(1, x1 - x0) * (Hi - Lo), Lo, Hi);
        double ratio = Math.Clamp(_startRatio + (p.Y - _startY) / 8.0, 1, 20);
        _e.DeviceSetParam(_t, _di, Threshold, (float)thr);
        _e.DeviceSetParam(_t, _di, Ratio, (float)ratio);
        InvalidateVisual();
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height; if (w <= 0) return;
        ctx.DrawRectangle(Sunken, new Pen(BorderDef, 1), new Rect(0, 0, w, h), 5, 5);
        var (x0, x1, top, bot) = Geo();
        for (int i = 1; i <= 3; i++) { double gx = x0 + (x1 - x0) * i / 4.0, gy = top + (bot - top) * i / 4.0; ctx.DrawLine(new Pen(Grid, 1), new Point(gx, top), new Point(gx, bot)); ctx.DrawLine(new Pen(Grid, 1), new Point(x0, gy), new Point(x1, gy)); }
        // Unity diagonal (dashed).
        ctx.DrawLine(new Pen(Axis, 1) { DashStyle = DashStyle.Dash }, new Point(X(Lo, x0, x1), Y(Lo, top, bot)), new Point(X(Hi, x0, x1), Y(Hi, top, bot)));

        double thr = P(Threshold), ratio = Math.Max(1, P(Ratio)), knee = P(Knee);
        // Threshold line.
        double tx = X(thr, x0, x1);
        ctx.DrawLine(new Pen(NotaPalette.Wash(NotaPalette.AccentBright, 0x50), 1), new Point(tx, top), new Point(tx, bot));

        // Transfer curve + fill.
        var geo = new StreamGeometry();
        var pts = new System.Collections.Generic.List<Point>();
        for (double db = Lo; db <= Hi + 0.01; db += 1.5) pts.Add(new Point(X(db, x0, x1), Y(OutDb(db, thr, ratio, knee), top, bot)));
        using (var gc = geo.Open()) { gc.BeginFigure(new Point(pts[0].X, bot), true); foreach (var pt in pts) gc.LineTo(pt); gc.LineTo(new Point(pts[^1].X, bot)); gc.EndFigure(true); }
        ctx.DrawGeometry(Fill, null, geo);
        var pen = new Pen(Accent, 1.8, lineJoin: PenLineJoin.Round);
        for (int i = 1; i < pts.Count; i++) ctx.DrawLine(pen, pts[i - 1], pts[i]);

        // Operating handle at the threshold knee point.
        ctx.DrawEllipse(Accent, new Pen(Sunken, 2), new Point(tx, Y(OutDb(thr, thr, ratio, knee), top, bot)), 4.5, 4.5);

        void Txt(string t, double x, double y, IBrush b) => ctx.DrawText(new FormattedText(t, CultureInfo.InvariantCulture, FlowDirection.LeftToRight, Face, 8, b), new Point(x, y));
        Txt("TRANSFER", x0 + 1, top - 1, Axis);
        Txt($"{ratio:0}:1 · knee {knee:0}", x1 - 68, top - 1, Accent);
        Txt("−60", 1, bot - 5, Axis);
        Txt("0 dB in", x1 - 34, bot + 3, Axis);
    }
}
