// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Vocoder's envelope graph (specs/028-vocoder-effect, after its design mockup): one bar
// per band at its centre on a 20 Hz – 20 kHz log axis, its height the modulator's envelope in
// that band (viz ink); a teal line through the carrier's level in the same bands; and the
// Low / High edges of the band range as rules. An edge is a handle: under the pointer or in the
// hand it turns accent, and dragging it moves Low or High (the same params as the knobs). The
// bars are set the moment a reading arrives — a meter that eases lies about the sound. With a
// Track carrier and no track picked (this track then passes through unchanged) the window says
// so in one line instead of drawing empty bars.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class VocoderGraph : Control
{
    private const double LoHz = 20, HiHz = 20000, Top = 16, Bottom = 13;
    private const int Max = 32;
    private readonly float[] _env = new float[Max], _car = new float[Max], _ctr = new float[Max];
    private int _n;
    private double _low = 80, _high = 8000;
    private bool _on = true, _active = true;
    private int _hover = -1, _drag = -1;   // 0 = Low edge, 1 = High edge

    /// <summary>An edge drag: 0 = Low, 1 = High, with the new frequency in Hz.</summary>
    public event Action<int, double>? EdgeChanged;
    public event Action<int>? GestureBegin;
    public event Action<int>? GestureEnd;
    public event Action<int>? ResetRequested;

    public VocoderGraph() { ClipToBounds = true; MinHeight = 60; }

    /// <summary>A fresh reading: <paramref name="scope"/> in the Vocoder scope layout
    /// (band count at 3, envelopes from 4, carrier levels from 36, centres from 68).</summary>
    public void Set(float[] scope, int count, double lowHz, double highHz, bool on, bool active)
    {
        _n = count > 3 ? Math.Clamp((int)scope[3], 0, Max) : 0;
        for (int b = 0; b < Max; b++)
        {
            bool ok = b < _n && count > 68 + b;
            _env[b] = ok ? scope[4 + b] : 0;
            _car[b] = ok ? scope[36 + b] : 0;
            _ctr[b] = ok ? scope[68 + b] : 0;
        }
        _low = lowHz; _high = highHz; _on = on; _active = active;
        InvalidateVisual();
    }

    private double X(double hz) => Math.Log(Math.Clamp(hz, LoHz, HiHz) / LoHz) / Math.Log(HiHz / LoHz) * Bounds.Width;
    private double Hz(double x) => LoHz * Math.Pow(HiHz / LoHz, Math.Clamp(x / Math.Max(1, Bounds.Width), 0, 1));

    private int EdgeAt(Point p)
    {
        double dl = Math.Abs(p.X - X(_low)), dh = Math.Abs(p.X - X(_high));
        if (Math.Min(dl, dh) > 6) return -1;
        return dl <= dh ? 0 : 1;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        var p = e.GetPosition(this);
        if (_drag >= 0) { EdgeChanged?.Invoke(_drag, Hz(p.X)); return; }
        int h = EdgeAt(p);
        if (h != _hover) { _hover = h; Cursor = h >= 0 ? new Cursor(StandardCursorType.SizeWestEast) : Cursor.Default; InvalidateVisual(); }
    }
    protected override void OnPointerExited(PointerEventArgs e)
    {
        base.OnPointerExited(e);
        if (_drag < 0 && _hover >= 0) { _hover = -1; InvalidateVisual(); }
    }
    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        int edge = EdgeAt(e.GetPosition(this));
        if (edge < 0) return;
        if (e.ClickCount == 2) { ResetRequested?.Invoke(edge); e.Handled = true; return; }
        _drag = edge; GestureBegin?.Invoke(edge); e.Pointer.Capture(this); e.Handled = true; InvalidateVisual();
    }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_drag < 0) return;
        int edge = _drag; _drag = -1; e.Pointer.Capture(null); GestureEnd?.Invoke(edge); InvalidateVisual();
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        // Capture taken away mid-drag (the window deactivates): end the gesture and drop the accent.
        if (_drag >= 0) { int edge = _drag; _drag = -1; GestureEnd?.Invoke(edge); InvalidateVisual(); }
        base.OnPointerCaptureLost(e);
    }

    public override void Render(DrawingContext ctx)
    {
        double w = Bounds.Width, h = Bounds.Height;
        if (w <= 0 || h <= 0) return;
        var frame = new Rect(0, 0, w, h);
        NotaGraph.Window(ctx, frame);
        double y0 = Top, y1 = h - Bottom, ph = Math.Max(1, y1 - y0);
        foreach (double hz in new[] { 50.0, 100, 200, 500, 1000, 2000, 5000, 10000 })
            ctx.DrawLine(NotaGraph.GridPen, new Point(Math.Round(X(hz)) + 0.5, y0), new Point(Math.Round(X(hz)) + 0.5, y1));

        NotaGraph.Title(ctx, frame, "Envelope");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomLeft, "20 Hz");
        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.BottomRight, "20 k");

        if (_active)
        {
            NotaGraph.Legend(ctx, w - 6, 4, ("Modulator", NotaPalette.VizInk, NotaGraph.Mark.Line), ("Carrier", NotaPalette.Teal, NotaGraph.Mark.Line));
            // Bars: each band's slot runs halfway to its neighbours on the log axis.
            var ink = _on ? NotaPalette.VizInk : NotaPalette.TextAxis;
            for (int b = 0; b < _n; b++)
            {
                if (_ctr[b] <= 0) continue;
                double x = X(_ctr[b]);
                double xl = b > 0 ? (x + X(_ctr[b - 1])) / 2 : x - (X(_ctr[Math.Min(_n - 1, 1)]) - x) / 2;
                double xr = b < _n - 1 ? (x + X(_ctr[b + 1])) / 2 : x + (x - X(_ctr[Math.Max(0, _n - 2)])) / 2;
                double bw = Math.Max(1, xr - xl - 2), bh = Math.Clamp(_env[b], 0, 1) * ph * 0.92;
                if (bh >= 0.5) ctx.DrawRectangle(ink, null, new RoundedRect(new Rect(xl + 1, y1 - bh, bw, bh), NotaRadius.BarValue));
            }
            // The carrier's level per band, as one line.
            if (_on && _n > 1)
            {
                var geo = new StreamGeometry();
                using (var g = geo.Open())
                {
                    for (int b = 0; b < _n; b++)
                    {
                        var pt = new Point(X(_ctr[b]), y1 - Math.Clamp(_car[b], 0, 1) * ph * 0.92);
                        if (b == 0) g.BeginFigure(pt, false); else g.LineTo(pt);
                    }
                    g.EndFigure(false);
                }
                ctx.DrawGeometry(null, NotaGraph.SecondaryPen(NotaPalette.Teal), geo);
            }
        }
        else
        {
            var ft = new FormattedText("Pick a carrier track · until then the sound passes through", System.Globalization.CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight, NotaFonts.Sans, 10, NotaPalette.TextDisabled);
            ctx.DrawText(ft, new Point((w - ft.Width) / 2, y0 + 14));
        }

        // The band range's edges: rules at rest, the accent under the hand.
        for (int edge = 0; edge < 2; edge++)
        {
            bool hot = edge == _drag || (_drag < 0 && edge == _hover);
            double x = Math.Round(X(edge == 0 ? _low : _high)) + 0.5;
            ctx.DrawLine(new Pen(hot ? NotaPalette.Accent : NotaPalette.Rule, hot ? 1.5 : 1), new Point(x, y0 - 4), new Point(x, y1));
        }
    }
}
