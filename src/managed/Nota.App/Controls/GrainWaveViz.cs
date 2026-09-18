// SPDX-License-Identifier: AGPL-3.0-only
// Copyright (c) 2026 Egor Khindikaynen (Nota). See LICENSES/ for license terms.
//
// Nota Grain sample view — a graph window (NotaGraph) with the sample drawn as brass bars
// (scaled so its loudest peak fills the height) around a centre line and a time grid
// labelled along the bottom. The Spray region is a Brass Wash band around the read
// Position (a dashed brass line); each held voice's read head is a Brass Light line with a
// notch on top, and the live grain cloud — every grain the engine is playing — is a teal
// dot at its read position, high for left and low for right, larger the louder its window
// is. Grains/sec sits in the top-left corner.
//
// It is also the Position control: drag across it (or click) to move the read position;
// double-click puts it back on its default. Left button only; GestureBegin/End frame an
// automation write.

using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;

namespace Nota.App;

internal sealed class GrainWaveViz : Control
{
    private static readonly IPen MidLine = new Pen(NotaPalette.GridBar, 1);
    private static readonly IPen PosPen = new Pen(NotaPalette.AccentDim, 1) { DashStyle = new DashStyle(new double[] { 3, 3 }, 0) };
    private static readonly IPen HeadPen = new Pen(NotaPalette.AccentBright, 1.2);
    private static readonly IBrush SprayFill = NotaPalette.AccentSubtle;
    private static readonly IBrush[] GrainInk =
    {
        NotaPalette.Wash(NotaPalette.TealBright, 0x55), NotaPalette.Wash(NotaPalette.TealBright, 0x99),
        NotaPalette.Wash(NotaPalette.TealBright, 0xDD),
    };

    private float[] _peaks = Array.Empty<float>();   // min/max pairs across the file
    private double _gain = 1;                         // draws the file's loudest peak full height
    private double _dur, _pos, _spray, _grPerSec;
    private int _mode;
    private float[] _heads = Array.Empty<float>();
    private int _headN;
    private float[] _cloud = Array.Empty<float>();   // (position, pan, level) triples
    private int _grainN;
    private bool _drag;

    public Action<double>? ValueChanged;
    public Action? GestureBegin;
    public Action? GestureEnd;
    /// <summary>Double-click: put Position back on its default.</summary>
    public Action? Reset;
    public bool Dragging => _drag;

    public GrainWaveViz()
    {
        MinHeight = 40; ClipToBounds = true;
        Cursor = new Cursor(StandardCursorType.SizeWestEast);
        DoubleTapped += (_, e) => { Reset?.Invoke(); e.Handled = true; };
    }

    public void SetPeaks(float[] peaks, double durationSec)
    {
        float max = 0;
        foreach (var p in peaks) max = Math.Max(max, Math.Abs(p));
        _peaks = peaks; _dur = durationSec; _gain = max > 1e-4f ? 1.0 / max : 1;
        InvalidateVisual();
    }
    public void SetState(double pos, double spray, int mode, double grPerSec)
    {
        if (pos == _pos && spray == _spray && mode == _mode && Math.Abs(grPerSec - _grPerSec) < 0.5) return;
        _pos = pos; _spray = spray; _mode = mode; _grPerSec = grPerSec; InvalidateVisual();
    }
    public void SetPlayheads(float[] heads, int n)
    {
        if (n == 0 && _headN == 0) return;
        _heads = heads; _headN = n; InvalidateVisual();
    }
    /// <summary>The grain cloud as (read position 0..1, pan 0..1, window level 0..1) triples.</summary>
    public void SetGrains(float[] triples, int n)
    {
        if (n == 0 && _grainN == 0) return;
        _cloud = triples; _grainN = n; InvalidateVisual();
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;   // right-click bubbles
        if (e.ClickCount > 1) return;                                           // DoubleTapped resets
        _drag = true; GestureBegin?.Invoke();
        e.Pointer.Capture(this);
        Apply(e.GetPosition(this));
        e.Handled = true;
    }
    protected override void OnPointerMoved(PointerEventArgs e) { if (_drag) Apply(e.GetPosition(this)); }
    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        if (_drag) { _drag = false; e.Pointer.Capture(null); GestureEnd?.Invoke(); }
    }
    protected override void OnPointerCaptureLost(PointerCaptureLostEventArgs e)
    {
        if (_drag) { _drag = false; GestureEnd?.Invoke(); }
    }
    private void Apply(Point p)
    {
        double w = Bounds.Width - 2 * Inset;
        if (w <= 0) return;
        _pos = Math.Clamp((p.X - Inset) / w, 0, 1);
        ValueChanged?.Invoke(_pos);
        InvalidateVisual();
    }

    private const double Inset = 4;

    public override void Render(DrawingContext ctx)
    {
        double W = Bounds.Width, H = Bounds.Height;
        if (W <= 0 || H <= 0) return;
        var frame = new Rect(0, 0, W, H);
        NotaGraph.Window(ctx, frame);
        using var clip = ctx.PushClip(frame.Deflate(1));
        double x0 = Inset, w = W - 2 * Inset, cy = H / 2, amp = cy - 14;

        // Spray band around the read position (where grains scatter). Key mode moves the
        // position with the note, so there is no one band to draw.
        double px = x0 + Math.Clamp(_pos, 0, 1) * w;
        if (_mode != 2 && _spray > 0.001)
        {
            double half = _spray * 0.25 * w;
            double s0 = Math.Max(x0, px - half), s1 = Math.Min(x0 + w, px + half);
            ctx.FillRectangle(SprayFill, new Rect(s0, 1, s1 - s0, H - 2));
        }

        // Time grid, labelled along the bottom.
        if (_dur > 0)
        {
            double step = _dur <= 0.5 ? 0.1 : _dur <= 2.5 ? 0.5 : _dur <= 6 ? 1.0 : _dur <= 15 ? 2.0 : _dur <= 40 ? 5.0 : 10.0;
            for (double tt = step; tt < _dur - step * 0.25; tt += step)
            {
                double x = x0 + tt / _dur * w;
                ctx.DrawLine(NotaGraph.GridPen, new Point(x, 1), new Point(x, H - 1));
                var ft = NotaGraph.AxisText(NotaNum.Time(tt));
                ctx.DrawText(ft, new Point(x - ft.Width - 3, H - ft.Height - 2));
            }
        }
        ctx.DrawLine(MidLine, new Point(1, cy), new Point(W - 1, cy));

        // Brass bars, one every 3 px, each the peak of its slice of the file.
        int n = _peaks.Length / 2;
        if (n > 0 && amp > 0)
        {
            var bar = new Pen(NotaPalette.Accent, 1.4);
            int bars = Math.Max(1, (int)(w / 3));
            for (int b = 0; b < bars; b++)
            {
                int i0 = b * n / bars, i1 = Math.Max(i0 + 1, (b + 1) * n / bars);
                float mn = 0, mx = 0;
                for (int i = i0; i < i1 && i < n; i++) { mn = Math.Min(mn, _peaks[i * 2]); mx = Math.Max(mx, _peaks[i * 2 + 1]); }
                double x = x0 + (b + 0.5) * w / bars;
                ctx.DrawLine(bar, new Point(x, cy - Math.Min(1, mx * _gain) * amp), new Point(x, cy - Math.Max(-1, mn * _gain) * amp));
            }
        }

        // The set read Position — where scanning centres and grains seed.
        if (_mode != 2) ctx.DrawLine(PosPen, new Point(px, 1), new Point(px, H - 1));

        // Each held voice's read head.
        for (int i = 0; i < _headN && i < _heads.Length; i++)
        {
            double hx = x0 + Math.Clamp(_heads[i], 0, 1) * w;
            ctx.DrawLine(HeadPen, new Point(hx, 1), new Point(hx, H - 1));
            var tri = new StreamGeometry();
            using (var g = tri.Open()) { g.BeginFigure(new Point(hx - 3, 1), true); g.LineTo(new Point(hx + 3, 1)); g.LineTo(new Point(hx, 5)); g.EndFigure(true); }
            ctx.DrawGeometry(NotaPalette.AccentBright, null, tri);
        }

        // The grain cloud: x = read position, y = pan (left high), size and ink by level.
        for (int i = 0; i < _grainN && i * 3 + 2 < _cloud.Length; i++)
        {
            double gx = x0 + Math.Clamp(_cloud[i * 3], 0, 1) * w;
            double gy = cy + (Math.Clamp(_cloud[i * 3 + 1], 0, 1) - 0.5) * 2 * amp;
            double lv = Math.Clamp(_cloud[i * 3 + 2], 0, 1);
            double r = 1.2 + lv * 2.2;
            ctx.DrawEllipse(GrainInk[Math.Min(2, (int)(lv * 3))], null, new Point(gx, gy), r, r);
        }

        NotaGraph.Axis(ctx, frame, NotaGraph.Corner.TopLeft, NotaNum.Unit(_grPerSec, "0", "gr/s"));
    }
}
